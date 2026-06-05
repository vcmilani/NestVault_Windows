using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using NestVault_Windows.Models;

namespace NestVault_Windows.Services;

public partial class APIService : ObservableObject
{
    [ObservableProperty] private string _serverUrl = ConfigStore.LoadSetting("server_url", "http://192.168.1.100:8000");
    [ObservableProperty] private string _apiKey    = ConfigStore.LoadSetting("api_key", "");
    [ObservableProperty] private bool   _isConnected;
    [ObservableProperty] private string? _connectionError;
    [ObservableProperty] private bool   _isLoadingBackups;
    [ObservableProperty] private List<BackupSummary> _backups = [];
    [ObservableProperty] private string _serverVersion = "";
    [ObservableProperty] private int    _backoffFailureCount;
    [ObservableProperty] private DateTimeOffset? _backoffNextRetry;

    private readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };

    public string BackoffHumanReadable
    {
        get
        {
            if (!BackoffNextRetry.HasValue) return "";
            var secs = (int)(BackoffNextRetry.Value - DateTimeOffset.Now).TotalSeconds;
            return secs <= 0 ? "Ready to retry" : $"Next retry in {secs}s";
        }
    }

    public GlobalStats GlobalStats => new(
        Backups.Count,
        Backups.Sum(b => b.VersionCount),
        Backups.Sum(b => b.FileCount),
        Backups.Sum(b => b.TotalSizeBytes)
    );

    public void SaveSettings()
    {
        ConfigStore.SaveSetting("server_url", ServerUrl);
        ConfigStore.SaveSetting("api_key", ApiKey);
    }

    // MARK: - Batch Support

    public bool SupportsBatch()
    {
        var parts = ServerVersion.Split('.');
        if (parts.Length < 2) return false;
        if (!int.TryParse(parts[0], out var major) || !int.TryParse(parts[1], out var minor)) return false;
        return major > 2 || (major == 2 && minor >= 6);
    }

    public async Task<List<CheckBatchResultItem>> CheckBatchAsync(
        string label, string versionKey,
        List<CheckBatchItem> items,
        CancellationToken ct = default)
    {
        var req = new CheckBatchRequest(label, versionKey, items);
        using var request = BuildRequest(HttpMethod.Post, "/check/batch", req);
        request.Options.TryAdd("timeout", 30);
        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<List<CheckBatchResultItem>>(ct)
               ?? [];
    }

    // MARK: - Health

    public async Task CheckHealthAsync()
    {
        try
        {
            using var request = BuildRequest(HttpMethod.Get, "/health", null as object);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var response = await _http.SendAsync(request, cts.Token);
            response.EnsureSuccessStatusCode();
            var health = await response.Content.ReadFromJsonAsync<HealthResponse>(cts.Token);
            if (health is null) throw new Exception("Empty health response");

            IsConnected     = true;
            ServerVersion   = health.Version;
            ConnectionError = null;
            BackoffFailureCount = 0;
            BackoffNextRetry    = null;
        }
        catch (Exception ex)
        {
            IsConnected     = false;
            ConnectionError = ex.Message;
            BackoffFailureCount++;
        }
    }

    // MARK: - Backups

    public async Task FetchBackupsAsync()
    {
        IsLoadingBackups = true;
        try
        {
            using var request = BuildRequest(HttpMethod.Get, "/backups", null as object);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var response = await _http.SendAsync(request, cts.Token);
            response.EnsureSuccessStatusCode();
            Backups = await response.Content.ReadFromJsonAsync<List<BackupSummary>>(cts.Token) ?? [];
        }
        catch { /* silent — caller checks IsConnected */ }
        finally { IsLoadingBackups = false; }
    }

    public async Task DeleteBackupAsync(string label, CancellationToken ct = default)
    {
        using var request = BuildRequest(HttpMethod.Delete, $"/backups/{Uri.EscapeDataString(label)}", null as object);
        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response);
    }

    // MARK: - Versions

    public async Task<List<BackupVersion>> FetchVersionsAsync(string label, CancellationToken ct = default)
    {
        using var request = BuildRequest(HttpMethod.Get, $"/backups/{Uri.EscapeDataString(label)}/versions", null as object);
        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<List<BackupVersion>>(ct) ?? [];
    }

    public async Task<BackupVersion?> FetchVersionDetailAsync(string label, string versionKey, CancellationToken ct = default)
    {
        using var request = BuildRequest(HttpMethod.Get,
            $"/backups/{Uri.EscapeDataString(label)}/versions/{Uri.EscapeDataString(versionKey)}", null as object);
        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<BackupVersion>(ct);
    }

    public async Task<VersionDeletedResponse?> DeleteVersionAsync(string label, string versionKey, CancellationToken ct = default)
    {
        using var request = BuildRequest(HttpMethod.Delete,
            $"/backups/{Uri.EscapeDataString(label)}/versions/{Uri.EscapeDataString(versionKey)}", null as object);
        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<VersionDeletedResponse>(ct);
    }

    // MARK: - Files

    public async Task<List<VersionFile>> FetchFilesAsync(string label, string versionKey, CancellationToken ct = default)
    {
        var escapedVersion = Uri.EscapeDataString(versionKey);
        using var request = BuildRequest(HttpMethod.Get,
            $"/files?backup_label={Uri.EscapeDataString(label)}&version_key={escapedVersion}", null as object);
        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<List<VersionFile>>(ct) ?? [];
    }

    // MARK: - Cleanup

    public async Task<CleanupResult> CleanupAsync(string label, int keep, CancellationToken ct = default)
    {
        var body = new { backup_label = label, keep };
        using var request = BuildRequest(HttpMethod.Post, $"/backups/{Uri.EscapeDataString(label)}/cleanup", body);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(120));
        using var response = await _http.SendAsync(request, cts.Token);
        await EnsureSuccessAsync(response);
        var result = await response.Content.ReadFromJsonAsync<CleanupResult>(cts.Token) ?? new();
        result.Label = label;
        return result;
    }

    public async Task<List<CleanupResult>> CleanupAllAsync(int keep, CancellationToken ct = default)
    {
        var results = new List<CleanupResult>();
        foreach (var backup in Backups)
        {
            var r = await CleanupAsync(backup.Label, keep, ct);
            results.Add(r);
        }
        return results;
    }

    // MARK: - Absorb

    public async Task<AbsorbResponse?> AbsorbAsync(
        string label, string versionKey, string sourceVersionKey,
        HttpClient? session = null, CancellationToken ct = default)
    {
        var http = session ?? _http;
        var body = new AbsorbRequest(sourceVersionKey);
        using var request = BuildRequest(HttpMethod.Post,
            $"/backups/{Uri.EscapeDataString(label)}/versions/{Uri.EscapeDataString(versionKey)}/absorb", body);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(300));
        using var response = await http.SendAsync(request, cts.Token);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<AbsorbResponse>(cts.Token);
    }

    // MARK: - Request Builder

    public HttpRequestMessage BuildRequest<T>(HttpMethod method, string path, T? body)
    {
        var url = ServerUrl.TrimEnd('/') + path;
        var request = new HttpRequestMessage(method, url);
        if (!string.IsNullOrEmpty(ApiKey))
            request.Headers.Add("X-API-Key", ApiKey);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return request;
    }

    public HttpRequestMessage BuildUploadRequest(string path, bool withBody = false)
    {
        var url = ServerUrl.TrimEnd('/') + path;
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        if (!string.IsNullOrEmpty(ApiKey))
            request.Headers.Add("X-API-Key", ApiKey);
        return request;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync();
        var preview = body.Length > 200 ? body[..200] : body;
        throw new HttpRequestException($"HTTP {(int)response.StatusCode} — {preview}",
            null, response.StatusCode);
    }
}
