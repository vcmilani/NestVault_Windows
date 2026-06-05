using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace NestVault_Windows.Models;

// MARK: - BackupSummary  (GET /backups)

public record BackupSummary(
    [property: JsonPropertyName("id")]                 int Id,
    [property: JsonPropertyName("label")]              string Label,
    [property: JsonPropertyName("client_name")]        string? ClientName,
    [property: JsonPropertyName("prefix")]             string? Prefix,
    [property: JsonPropertyName("status")]             string? Status,
    [property: JsonPropertyName("created_at")]         string? CreatedAt,
    [property: JsonPropertyName("last_version")]       string? LastVersion,
    [property: JsonPropertyName("version_count")]      int VersionCount,
    [property: JsonPropertyName("file_count")]         int FileCount,
    [property: JsonPropertyName("total_size_bytes")]   long TotalSizeBytes
)
{
    public string FormattedSize => ModelHelpers.FormatBytes(TotalSizeBytes);
    public DateTimeOffset? LastVersionDate => ModelHelpers.ParseIso(LastVersion);
    public string FormattedLastVersion => LastVersionDate?.ToString("MMM d, yyyy") ?? "—";
}

// MARK: - BackupVersion  (GET /backups/{label}/versions)

public record BackupVersion(
    [property: JsonPropertyName("id")]                 int Id,
    [property: JsonPropertyName("version_key")]        string VersionKey,
    [property: JsonPropertyName("backup_label")]       string BackupLabel,
    [property: JsonPropertyName("status")]             string Status,
    [property: JsonPropertyName("created_at")]         string? CreatedAt,
    [property: JsonPropertyName("finished_at")]        string? FinishedAt,
    [property: JsonPropertyName("file_count")]         int FileCount,
    [property: JsonPropertyName("total_size_bytes")]   long TotalSizeBytes
)
{
    public string FormattedSize => ModelHelpers.FormatBytes(TotalSizeBytes);
    public DateTimeOffset? Date => ModelHelpers.ParseIso(VersionKey);
    public bool IsDone => Status == "done";
}

// MARK: - VersionFile  (GET /files)

public record VersionFile(
    [property: JsonPropertyName("id")]             int Id,
    [property: JsonPropertyName("original_path")]  string OriginalPath,
    [property: JsonPropertyName("sha256")]         string Sha256,
    [property: JsonPropertyName("size")]           long Size,
    [property: JsonPropertyName("mtime")]          double? Mtime,
    [property: JsonPropertyName("created_at")]     string? CreatedAt
)
{
    public string FormattedSize => ModelHelpers.FormatBytes(Size);
}

// MARK: - CheckResponse  (POST /check)

public record CheckResponse(
    [property: JsonPropertyName("needs_upload")]    bool NeedsUpload,
    [property: JsonPropertyName("content_exists")]  bool ContentExists,
    [property: JsonPropertyName("reason")]          string? Reason,
    [property: JsonPropertyName("file_id")]         int? FileId
);

// MARK: - Batch Check  (POST /check/batch — server v2.6+)

public record CheckBatchItem(
    [property: JsonPropertyName("original_path")]  string OriginalPath,
    [property: JsonPropertyName("sha256")]         string Sha256,
    [property: JsonPropertyName("size")]           long Size,
    [property: JsonPropertyName("mtime")]          double Mtime
);

public record CheckBatchRequest(
    [property: JsonPropertyName("backup_label")]  string BackupLabel,
    [property: JsonPropertyName("version_key")]   string VersionKey,
    [property: JsonPropertyName("files")]         List<CheckBatchItem> Files
);

public record CheckBatchResultItem(
    [property: JsonPropertyName("needs_upload")]    bool NeedsUpload,
    [property: JsonPropertyName("content_exists")]  bool ContentExists,
    [property: JsonPropertyName("reason")]          string Reason,
    [property: JsonPropertyName("file_id")]         int? FileId
);

// MARK: - UploadResponse  (POST /upload)

public record UploadResponse(
    [property: JsonPropertyName("status")]    string Status,
    [property: JsonPropertyName("file_id")]   int? FileId,
    [property: JsonPropertyName("sha256")]    string? Sha256,
    [property: JsonPropertyName("uploaded")]  bool? Uploaded
);

// MARK: - SyncResponse  (POST /sync)

public record SyncResponse(
    [property: JsonPropertyName("synced")]  bool Synced
);

// MARK: - CleanupResult  (POST /backups/{label}/cleanup)

public class CleanupResult
{
    public string Label { get; set; } = "";

    [JsonPropertyName("kept")]
    public int Kept { get; set; }

    [JsonPropertyName("versions_removed")]
    public List<string> VersionsRemoved { get; set; } = [];

    [JsonPropertyName("storage_files_removed")]
    public int StorageFilesRemoved { get; set; }

    public int Removed => VersionsRemoved.Count;
}

// MARK: - VersionCreatedResponse  (POST /backups/{label}/versions)

public record VersionCreatedResponse(
    [property: JsonPropertyName("created")]  bool Created,
    [property: JsonPropertyName("version")]  BackupVersion Version
);

// MARK: - GlobalStats (computed locally)

public record GlobalStats(int TotalBackups, int TotalVersions, int TotalFiles, long TotalSize)
{
    public string FormattedSize => ModelHelpers.FormatBytes(TotalSize);
}

// MARK: - BackupSchedule

public class BackupSchedule
{
    public enum ScheduleFrequency { Off, Hourly, Daily, Weekly, Custom }

    public ScheduleFrequency Frequency { get; set; } = ScheduleFrequency.Off;
    public int Hour          { get; set; } = 2;
    public int Minute        { get; set; } = 0;
    public int Weekday       { get; set; } = 1;  // 0=Sun … 6=Sat (DayOfWeek)
    public int CustomMinutes { get; set; } = 60;

    public bool Enabled => Frequency != ScheduleFrequency.Off;

    // Convenience for ComboBox binding (index matches enum order)
    public int FrequencyIndex
    {
        get => (int)Frequency;
        set => Frequency = (ScheduleFrequency)value;
    }

    public DateTimeOffset? NextRun(DateTimeOffset? lastRun = null)
    {
        var baseline = DateTimeOffset.Now;
        return Frequency switch
        {
            ScheduleFrequency.Off    => null,
            ScheduleFrequency.Hourly => (lastRun ?? baseline).AddHours(1),
            ScheduleFrequency.Custom => (lastRun ?? baseline).AddMinutes(CustomMinutes),
            ScheduleFrequency.Daily  => NextOccurrence(Hour, Minute, null, baseline),
            ScheduleFrequency.Weekly => NextOccurrence(Hour, Minute, (DayOfWeek)Weekday, baseline),
            _                        => null
        };
    }

    public bool IsDue(DateTimeOffset? lastRun)
    {
        if (!Enabled) return false;
        var next = NextRun(lastRun ?? DateTimeOffset.MinValue);
        return next.HasValue && DateTimeOffset.Now >= next.Value;
    }

    private static DateTimeOffset? NextOccurrence(int hour, int minute, DayOfWeek? weekday, DateTimeOffset after)
    {
        var candidate = new DateTimeOffset(after.Year, after.Month, after.Day,
                                           hour, minute, 0, after.Offset);
        if (candidate <= after) candidate = candidate.AddDays(1);

        if (weekday.HasValue)
        {
            int daysUntil = ((int)weekday.Value - (int)candidate.DayOfWeek + 7) % 7;
            candidate = candidate.AddDays(daysUntil);
        }
        return candidate;
    }
}

// MARK: - BackupProfile

public class BackupProfile
{
    public Guid   Id             { get; set; } = Guid.NewGuid();
    public string Name           { get; set; } = "New Backup";
    public string Label          { get; set; } = "";
    public string SourcePath     { get; set; } = "";
    public List<string> Excludes { get; set; } = [];
    public int    Workers        { get; set; } = 4;
    public string Prefix         { get; set; } = "";
    public string ServerOverride { get; set; } = "";
    public bool   Enabled        { get; set; } = true;

    public BackupSchedule Schedule      { get; set; } = new();
    public DateTimeOffset? LastRun      { get; set; }
    public string?        LastRunStatus { get; set; }

    public bool   Accumulate          { get; set; } = false;
    public bool   SmartSkip           { get; set; } = false;
    public DateTimeOffset? LastFullBackupDate { get; set; }

    public string CliCommand(string defaultServer)
    {
        var server = string.IsNullOrEmpty(ServerOverride) ? defaultServer : ServerOverride;
        var parts = new List<string>
        {
            $"nestvault backup {(string.IsNullOrEmpty(SourcePath) ? "<folder>" : SourcePath)}",
            $"  --label \"{(string.IsNullOrEmpty(Label) ? "<label>" : Label)}\"",
            $"  --server {server}"
        };
        if (Workers != 4) parts.Add($"  --workers {Workers}");
        if (!string.IsNullOrEmpty(Prefix))
        {
            var q = Prefix.Contains(' ') ? $"\"{Prefix}\"" : Prefix;
            parts.Add($"  --prefix {q}");
        }
        if (Excludes.Count > 0)
        {
            var exc = string.Join(" ", Excludes.Select(e => e.Contains(' ') ? $"\"{e}\"" : e));
            parts.Add($"  --exclude {exc}");
        }
        if (Accumulate) parts.Add("  --absorb");
        return string.Join(" \\\n", parts);
    }
}

// MARK: - Absorb  (POST /backups/{label}/versions/{key}/absorb)

public record AbsorbRequest(
    [property: JsonPropertyName("source_version_key")]  string SourceVersionKey
);

public record AbsorbResponse(
    [property: JsonPropertyName("inherited")]  int Inherited,
    [property: JsonPropertyName("skipped")]    int Skipped
);

// MARK: - APIService response types

public record HealthResponse(
    [property: JsonPropertyName("status")]   string Status,
    [property: JsonPropertyName("version")]  string Version,
    [property: JsonPropertyName("time")]     string Time
);

public record VersionDeletedResponse(
    [property: JsonPropertyName("status")]                    string Status,
    [property: JsonPropertyName("version_key")]               string VersionKey,
    [property: JsonPropertyName("files_removed_from_storage")] int FilesRemovedFromStorage
);

// MARK: - Helpers

public static class ModelHelpers
{
    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }

    public static DateTimeOffset? ParseIso(string? s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        if (DateTimeOffset.TryParse(s, out var result)) return result;
        if (DateTime.TryParseExact(s, "yyyy-MM-dd HH:mm:ss",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var dt))
            return new DateTimeOffset(dt, TimeSpan.Zero);
        return null;
    }
}

// Extension for BackupSummary and VersionFile to access helpers
public static class BackupExtensions
{
    public static string FormatBytes(long bytes) => ModelHelpers.FormatBytes(bytes);
    public static DateTimeOffset? ParseIso(string? s) => ModelHelpers.ParseIso(s);
}
