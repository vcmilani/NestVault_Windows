using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using NestVault_Windows.Models;

namespace NestVault_Windows.Services;

public record LogEntry(string Text, LogKind Kind);
public enum LogKind { Info, Success, Warning, Error }

public partial class BackupRunner : ObservableObject
{
    // MARK: - State

    public enum RunStatus { Idle, Running, Done, Failed, Cancelled }

    public class BackupStats
    {
        public int Total, Uploaded, Registered, Cached, Ignored, Errors, Inherited, Skipped;
    }

    [ObservableProperty] private RunStatus _status = RunStatus.Idle;
    [ObservableProperty] private List<LogEntry> _entries = [];
    [ObservableProperty] private double _progress;
    [ObservableProperty] private BackupStats _stats = new();
    [ObservableProperty] private string _currentFile = "";
    [ObservableProperty] private bool _wasFullBackup = true;

    private enum FileAction { Skip, CachedRegister, Register, Upload }
    private record ScannedFile(string Path, double Mtime, long Size);
    private record HashedFile(string Path, string ServerPath, string Sha256, long Size, double Mtime);

    private readonly APIService _api;
    private CancellationTokenSource _cts = new();
    private HttpClient _session = new() { Timeout = Timeout.InfiniteTimeSpan };

    private const int MaxRetries = 3;
    private const int BatchSize  = 100;

    public BackupRunner(APIService api) { _api = api; }

    public void Cancel()
    {
        if (Status != RunStatus.Running) return;
        _cts.Cancel();
        Log("Cancellation requested...", LogKind.Warning);
    }

    // MARK: - Run

    public async Task RunAsync(BackupProfile profile)
    {
        _cts      = new CancellationTokenSource();
        _session  = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        Status    = RunStatus.Running;
        Entries   = [];
        Stats     = new BackupStats();
        Progress  = 0;
        WasFullBackup = true;

        var ct    = _cts.Token;
        var label = profile.Label;
        var source = profile.SourcePath;

        Log($"Starting backup: {label}", LogKind.Info);
        Log($"Source: {source}", LogKind.Info);

        // 1. Create / open backup
        try
        {
            await CreateBackupAsync(label, ct);
            Log("Backup registered.", LogKind.Success);
        }
        catch (Exception ex)
        {
            Log($"Failed to register backup: {ex.Message}", LogKind.Error);
            Status = RunStatus.Failed; return;
        }

        // 2. Create version
        string versionKey;
        try
        {
            versionKey = await CreateVersionAsync(label, ct);
            Log($"Version created: {versionKey}", LogKind.Success);
        }
        catch (Exception ex)
        {
            Log($"Failed to create version: {ex.Message}", LogKind.Error);
            Status = RunStatus.Failed; return;
        }

        // 3. Previous version cache (mtime+size fast path)
        var (cache, prevDoneKey) = await FetchPreviousVersionCacheAsync(label, ct);
        if (cache.Count > 0) Log($"Version cache loaded: {cache.Count} files.", LogKind.Info);

        // Local hash cache
        var hashCache = new LocalHashCache(label);
        await hashCache.LoadAsync();
        if (hashCache.Count > 0) Log($"Hash cache loaded: {hashCache.Count} files.", LogKind.Info);

        // 4. Walk filesystem
        List<ScannedFile> scannedFiles;
        try
        {
            scannedFiles = await Task.Run(() => ScanDirectory(source, profile.Excludes), ct);
        }
        catch (Exception ex)
        {
            Log($"Cannot read source: {ex.Message}", LogKind.Error);
            await FinalizeVersionAsync(label, versionKey, ok: false);
            Status = RunStatus.Failed; return;
        }

        Stats.Total = scannedFiles.Count;
        Log($"Files found: {scannedFiles.Count}", LogKind.Info);

        // Smart skip
        if (profile.SmartSkip && prevDoneKey is not null)
        {
            var noChanges = await Task.Run(() => DetectNoChanges(scannedFiles, hashCache), ct);
            var overdue   = profile.LastFullBackupDate is null ||
                            (DateTimeOffset.Now - profile.LastFullBackupDate.Value).TotalDays > 7;

            if (noChanges && !overdue)
            {
                Log($"Smart skip: no changes detected in {scannedFiles.Count} files.", LogKind.Info);
                WasFullBackup = false;
                try
                {
                    var result = await _api.AbsorbAsync(label, versionKey, prevDoneKey, _session, ct);
                    if (result is not null)
                    {
                        Stats.Inherited = result.Inherited;
                        Stats.Skipped   = result.Skipped;
                        Log($"Absorb done: {result.Inherited} inherited, {result.Skipped} skipped.", LogKind.Success);
                    }
                }
                catch (Exception ex) { Log($"Absorb error: {ex.Message}", LogKind.Warning); }

                await FinalizeVersionAsync(label, versionKey, ok: true);
                await Task.Run(() =>
                {
                    var paths = scannedFiles.Select(f => f.Path).ToHashSet();
                    hashCache.Prune(paths);
                }, ct);
                await hashCache.SaveAsync();
                Progress = 1.0; CurrentFile = ""; Status = RunStatus.Done;
                Log("─────────────────────────────────────", LogKind.Info);
                Log($"Summary: 0 uploaded, 0 registered, {Stats.Inherited} inherited, 0 errors.", LogKind.Success);
                return;
            }
            if (overdue && noChanges) Log("Smart skip: forced full backup (7-day limit).", LogKind.Info);
        }

        // 5. Build server paths
        var serverPaths = scannedFiles.Select(f =>
        {
            if (string.IsNullOrEmpty(profile.Prefix)) return f.Path;
            var rel = f.Path.StartsWith(source) ? f.Path[source.Length..] : "/" + Path.GetFileName(f.Path);
            return profile.Prefix.EndsWith('/') || profile.Prefix.EndsWith('\\')
                ? profile.Prefix + rel.TrimStart('/', '\\')
                : profile.Prefix + rel;
        }).ToList();

        // ── PHASE 1: Classify ────────────────────────────────────────────────

        var useBatch = _api.SupportsBatch();
        if (useBatch) Log("Batch mode enabled.", LogKind.Info);

        var fastEntries = new List<(string Path, string ServerPath, string Sha256, double Mtime)>();
        var slowEntries = new List<(string Path, string ServerPath, long Size, double Mtime)>();

        for (int i = 0; i < scannedFiles.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var file       = scannedFiles[i];
            var serverPath = serverPaths[i];

            var sha256 = hashCache.Lookup(file.Path, file.Mtime, file.Size);
            if (sha256 is not null)
            {
                fastEntries.Add((file.Path, serverPath, sha256, file.Mtime));
                continue;
            }
            if (cache.TryGetValue(serverPath, out var cached) &&
                cached.Mtime == file.Mtime && cached.Size == file.Size)
            {
                fastEntries.Add((file.Path, serverPath, cached.Sha256, file.Mtime));
                hashCache.Set(file.Path, file.Mtime, file.Size, cached.Sha256);
                continue;
            }
            slowEntries.Add((file.Path, serverPath, file.Size, file.Mtime));
        }

        Log($"Classifying: {fastEntries.Count} cached, {slowEntries.Count} need hashing.", LogKind.Info);

        // Hash slow files in parallel
        var hashedFiles  = new ConcurrentBag<HashedFile>();
        var totalSlow    = slowEntries.Count;
        var phase1Weight = totalSlow > 0 ? 0.4 : 0.0;
        var hashDone     = 0;
        var concurrency  = Math.Max(2, Environment.ProcessorCount);
        var sem          = new SemaphoreSlim(concurrency, concurrency);

        var hashTasks = slowEntries.Select(async entry =>
        {
            await sem.WaitAsync(ct);
            try
            {
                var sha256 = await ComputeSha256Async(entry.Path, ct);
                var hf = new HashedFile(entry.Path, entry.ServerPath, sha256, entry.Size, entry.Mtime);
                hashedFiles.Add(hf);
                hashCache.Set(entry.Path, entry.Mtime, entry.Size, sha256);
                var done = Interlocked.Increment(ref hashDone);
                Progress    = (double)done / Math.Max(totalSlow, 1) * phase1Weight;
                CurrentFile = Path.GetFileName(entry.Path);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                var s = Stats; s.Errors++;
                Log($"Hash error: {entry.Path} — {ex.Message}", LogKind.Error);
            }
            finally { sem.Release(); }
        });

        try { await Task.WhenAll(hashTasks); }
        catch (OperationCanceledException)
        {
            await FinalizeVersionAsync(label, versionKey, ok: false);
            Progress = 0; CurrentFile = ""; Status = RunStatus.Cancelled;
            Log("Cancelled.", LogKind.Warning); return;
        }

        // Classify hashed files
        var actionMap = new Dictionary<string, (FileAction Action, string Path, double Mtime)>();

        if (useBatch && hashedFiles.Count > 0)
        {
            var batches = hashedFiles
                .Select((f, i) => (f, i))
                .GroupBy(x => x.i / BatchSize)
                .Select(g => g.Select(x => x.f).ToList());

            foreach (var batch in batches)
            {
                ct.ThrowIfCancellationRequested();
                var items = batch.Select(h =>
                    new CheckBatchItem(h.ServerPath, h.Sha256, h.Size, h.Mtime)).ToList();
                try
                {
                    var results = await _api.CheckBatchAsync(label, versionKey, items, ct);
                    for (int i = 0; i < results.Count && i < batch.Count; i++)
                    {
                        var res = results[i]; var h = batch[i];
                        FileAction action = !res.NeedsUpload ? FileAction.Skip
                                          : res.ContentExists ? FileAction.Register
                                          : FileAction.Upload;
                        actionMap[h.ServerPath] = (action, h.Path, h.Mtime);
                    }
                }
                catch (Exception ex)
                {
                    Log($"Batch check error: {ex.Message}", LogKind.Warning);
                    foreach (var h in batch)
                        actionMap[h.ServerPath] = (FileAction.Upload, h.Path, h.Mtime);
                }
            }
        }
        else
        {
            foreach (var h in hashedFiles)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var body = new
                    {
                        backup_label  = label,
                        version_key   = versionKey,
                        original_path = h.ServerPath,
                        sha256        = h.Sha256,
                        size          = h.Size,
                        mtime         = h.Mtime
                    };
                    using var req  = _api.BuildRequest(HttpMethod.Post, "/check", body);
                    using var resp = await _session.SendAsync(req, ct);
                    var check = await resp.Content.ReadFromJsonAsync<CheckResponse>(cancellationToken: ct);
                    FileAction action = check?.NeedsUpload == false ? FileAction.Skip
                                      : check?.ContentExists == true ? FileAction.Register
                                      : FileAction.Upload;
                    actionMap[h.ServerPath] = (action, h.Path, h.Mtime);
                }
                catch { actionMap[h.ServerPath] = (FileAction.Upload, h.Path, h.Mtime); }
            }
        }

        // ── PHASE 2: Execute ─────────────────────────────────────────────────

        var workItems = new List<(FileAction Action, string Path, string ServerPath, string? Sha256, double Mtime)>();

        foreach (var f in fastEntries)
            workItems.Add((FileAction.CachedRegister, f.Path, f.ServerPath, f.Sha256, f.Mtime));

        foreach (var (sp, entry) in actionMap)
        {
            var sha256 = entry.Action == FileAction.Register
                ? hashedFiles.FirstOrDefault(h => h.Path == entry.Path)?.Sha256
                : null;
            workItems.Add((entry.Action, entry.Path, sp, sha256, entry.Mtime));
        }

        var totalWork        = workItems.Count;
        var concurrencyLimit = Math.Max(1, profile.Workers);
        var phase2Start      = phase1Weight;
        var phase2Done       = 0;
        var execSem          = new SemaphoreSlim(concurrencyLimit, concurrencyLimit);
        var statsLock        = new object();

        var execTasks = workItems.Select(async item =>
        {
            await execSem.WaitAsync(ct);
            try
            {
                var actionStr = await ExecuteWithRetryAsync(
                    item.Action, item.Path, label, versionKey,
                    item.ServerPath, item.Sha256, item.Mtime, ct);

                lock (statsLock)
                {
                    switch (actionStr)
                    {
                        case "upload":   Stats.Uploaded++;   break;
                        case "register": Stats.Registered++; break;
                        case "cached":   Stats.Cached++;     break;
                        default:         Stats.Ignored++;    break;
                    }
                }
                var done = Interlocked.Increment(ref phase2Done);
                Progress    = phase2Start + (double)done / Math.Max(totalWork, 1) * (1.0 - phase2Start);
                CurrentFile = Path.GetFileName(item.Path);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                lock (statsLock) { Stats.Errors++; }
                Log($"File error: {item.Path} — {ex.Message}", LogKind.Error);
            }
            finally { execSem.Release(); }
        });

        try { await Task.WhenAll(execTasks); }
        catch (OperationCanceledException)
        {
            await FinalizeVersionAsync(label, versionKey, ok: false);
            Progress = 0; CurrentFile = ""; Status = RunStatus.Cancelled;
            Log("Cancelled.", LogKind.Warning); return;
        }

        // 6. Sync
        try
        {
            var synced = await SyncVersionAsync(label, versionKey, serverPaths, ct);
            if (synced) Log("Sync complete.", LogKind.Success);
        }
        catch (Exception ex) { Log($"Sync skipped: {ex.Message}", LogKind.Warning); }

        // 7. Absorb (accumulative mode)
        if (profile.Accumulate)
        {
            if (prevDoneKey is null)
                Log("Accumulate: no previous version to absorb.", LogKind.Info);
            else if (Stats.Errors > 0)
                Log("Accumulate: skipped due to errors.", LogKind.Warning);
            else
            {
                try
                {
                    var result = await _api.AbsorbAsync(label, versionKey, prevDoneKey, _session, ct);
                    if (result is not null)
                    {
                        Stats.Inherited = result.Inherited;
                        Stats.Skipped   = result.Skipped;
                        Log($"Absorb done: {result.Inherited} inherited, {result.Skipped} skipped.", LogKind.Success);
                    }
                }
                catch (Exception ex) { Log($"Absorb error: {ex.Message}", LogKind.Warning); }
            }
        }

        // 8. Finalize
        await FinalizeVersionAsync(label, versionKey, ok: Stats.Errors == 0);

        var walkedPaths = scannedFiles.Select(f => f.Path).ToHashSet();
        hashCache.Prune(walkedPaths);
        await hashCache.SaveAsync();

        Progress    = 1.0;
        CurrentFile = "";
        Status      = RunStatus.Done;
        Log("─────────────────────────────────────", LogKind.Info);
        Log($"Summary: {Stats.Uploaded} uploaded, {Stats.Registered} registered, " +
            $"{Stats.Cached} cached, {Stats.Ignored} ignored, {Stats.Errors} errors.", LogKind.Success);
    }

    // MARK: - Filesystem Scan

    private static List<ScannedFile> ScanDirectory(string source, List<string> excludes)
    {
        var files = new List<ScannedFile>();
        foreach (var path in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(path);
            if (excludes.Contains(name)) continue;
            var skip = false;
            foreach (var ex in excludes)
            {
                if (path.Contains(ex, StringComparison.OrdinalIgnoreCase)) { skip = true; break; }
            }
            if (skip) continue;

            try
            {
                var info  = new FileInfo(path);
                var mtime = new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeMilliseconds() / 1000.0;
                files.Add(new ScannedFile(path, mtime, info.Length));
            }
            catch { /* skip inaccessible files */ }
        }
        return files;
    }

    // MARK: - Smart Skip Detection

    private static bool DetectNoChanges(List<ScannedFile> scanned, LocalHashCache hashCache)
    {
        if (scanned.Count == 0 || scanned.Count != hashCache.Count) return false;
        return scanned.All(f => hashCache.Lookup(f.Path, f.Mtime, f.Size) is not null);
    }

    // MARK: - Execute with Retry

    private async Task<string> ExecuteWithRetryAsync(
        FileAction action, string path, string label, string versionKey,
        string serverPath, string? sha256, double mtime, CancellationToken ct)
    {
        var currentAction = action;
        Exception? lastEx = null;
        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return await ExecuteActionAsync(currentAction, path, label, versionKey,
                                                serverPath, sha256, mtime, ct);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                lastEx = ex;
                if (currentAction is FileAction.CachedRegister or FileAction.Register)
                    currentAction = FileAction.Upload;
                if (attempt < MaxRetries)
                    await Task.Delay(500 * attempt, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastEx = ex;
                if (attempt < MaxRetries)
                    await Task.Delay(500 * attempt, ct);
            }
        }
        throw lastEx ?? new Exception("Unknown error");
    }

    private async Task<string> ExecuteActionAsync(
        FileAction action, string path, string label, string versionKey,
        string serverPath, string? sha256, double mtime, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var pathB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(serverPath));

        switch (action)
        {
            case FileAction.Skip:
                return "ignore";

            case FileAction.CachedRegister:
            case FileAction.Register:
            {
                using var req = _api.BuildUploadRequest("/upload");
                req.Headers.Add("X-Backup-Label",    label);
                req.Headers.Add("X-Version-Key",     versionKey);
                req.Headers.Add("X-Original-Path",   pathB64);
                req.Headers.Add("X-Mtime",           mtime.ToString("G", System.Globalization.CultureInfo.InvariantCulture));
                req.Headers.Add("X-Content-Sha256",  sha256 ?? "");
                using var resp = await _session.SendAsync(req, ct);
                resp.EnsureSuccessStatusCode();
                return action == FileAction.CachedRegister ? "cached" : "register";
            }

            case FileAction.Upload:
            {
                using var req = _api.BuildUploadRequest("/upload");
                req.Headers.Add("X-Backup-Label",  label);
                req.Headers.Add("X-Version-Key",   versionKey);
                req.Headers.Add("X-Original-Path", pathB64);
                req.Headers.Add("X-Mtime",         mtime.ToString("G", System.Globalization.CultureInfo.InvariantCulture));
                using var fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
                var content = new StreamContent(fileStream);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                req.Content = content;
                using var resp = await _session.SendAsync(req, ct);
                resp.EnsureSuccessStatusCode();
                return "upload";
            }

            default: return "ignore";
        }
    }

    // MARK: - API Helpers

    private async Task CreateBackupAsync(string label, CancellationToken ct)
    {
        var body = new { label, client_name = Environment.MachineName };
        using var req  = _api.BuildRequest(HttpMethod.Post, "/backups", body);
        using var resp = await _session.SendAsync(req, ct);
        if ((int)resp.StatusCode == 409) return;
        resp.EnsureSuccessStatusCode();
    }

    private async Task<string> CreateVersionAsync(string label, CancellationToken ct)
    {
        var versionKey = DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:ss").Replace("'T'", "T");
        versionKey = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
        var body = new { version_key = versionKey };
        using var req  = _api.BuildRequest(HttpMethod.Post, $"/backups/{Uri.EscapeDataString(label)}/versions", body);
        using var resp = await _session.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        try
        {
            var created = await resp.Content.ReadFromJsonAsync<VersionCreatedResponse>(cancellationToken: ct);
            if (created?.Version?.VersionKey is not null) return created.Version.VersionKey;
        }
        catch { }
        return versionKey;
    }

    private async Task<(Dictionary<string, FileCacheEntry> Cache, string? PrevDoneKey)>
        FetchPreviousVersionCacheAsync(string label, CancellationToken ct)
    {
        try
        {
            var versions = await _api.FetchVersionsAsync(label, ct);
            var done     = versions.FirstOrDefault(v => v.IsDone);
            if (done is null) return ([], null);

            var files = await _api.FetchFilesAsync(label, done.VersionKey, ct);
            var cache = files.ToDictionary(
                f => f.OriginalPath,
                f => new FileCacheEntry(f.Sha256, f.Mtime ?? 0, f.Size));
            return (cache, done.VersionKey);
        }
        catch { return ([], null); }
    }

    private async Task<bool> SyncVersionAsync(string label, string versionKey,
        List<string> paths, CancellationToken ct)
    {
        var body = new { backup_label = label, version_key = versionKey, existing_paths = paths };
        using var req  = _api.BuildRequest(HttpMethod.Post, "/sync", body);
        using var resp = await _session.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return false;
        var sync = await resp.Content.ReadFromJsonAsync<SyncResponse>(cancellationToken: ct);
        return sync?.Synced ?? false;
    }

    private async Task FinalizeVersionAsync(string label, string versionKey, bool ok)
    {
        var body = new { status = ok ? "done" : "failed" };
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var req  = _api.BuildRequest(HttpMethod.Patch,
                    $"/backups/{Uri.EscapeDataString(label)}/versions/{Uri.EscapeDataString(versionKey)}", body);
                using var resp = await _session.SendAsync(req);
                if (resp.IsSuccessStatusCode) return;
            }
            catch { }
            if (attempt < 3) await Task.Delay(2000);
        }
        Log("Warning: could not finalize version.", LogKind.Warning);
    }

    // MARK: - SHA-256

    private static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        using var sha    = SHA256.Create();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                                          bufferSize: 4_194_304, useAsync: true);
        var hash = await sha.ComputeHashAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    // MARK: - Log

    private void Log(string text, LogKind kind) =>
        Entries = [.. Entries, new LogEntry(text, kind)];
}

// MARK: - Local Hash Cache

public class LocalHashCache
{
    private record Entry(double Mtime, long Size, string Sha256);

    private Dictionary<string, Entry> _store = [];
    private readonly string _filePath;

    public int Count => _store.Count;

    public LocalHashCache(string label)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NestVault", "hashcache");
        Directory.CreateDirectory(dir);
        var safe = Uri.EscapeDataString(label);
        _filePath = Path.Combine(dir, $"{safe}_hashcache.json");
    }

    public async Task LoadAsync()
    {
        try
        {
            if (!File.Exists(_filePath)) return;
            var json = await File.ReadAllTextAsync(_filePath);
            _store = JsonSerializer.Deserialize<Dictionary<string, Entry>>(json) ?? [];
        }
        catch { _store = []; }
    }

    public string? Lookup(string path, double mtime, long size)
    {
        if (_store.TryGetValue(path, out var e) && e.Mtime == mtime && e.Size == size)
            return e.Sha256;
        return null;
    }

    public void Set(string path, double mtime, long size, string sha256) =>
        _store[path] = new Entry(mtime, size, sha256);

    public void Prune(HashSet<string> keepPaths) =>
        _store = _store.Where(kv => keepPaths.Contains(kv.Key))
                       .ToDictionary(kv => kv.Key, kv => kv.Value);

    public async Task SaveAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(_store, new JsonSerializerOptions { WriteIndented = false });
            await File.WriteAllTextAsync(_filePath, json);
        }
        catch { }
    }
}

public record FileCacheEntry(string Sha256, double Mtime, long Size);
