using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NestVaultWindows.Models;
using NestVaultWindows.Services;

namespace NestVaultWindows.ViewModels;

public partial class BackupConfigsViewModel : ObservableObject
{
    private readonly APIService  _api;
    private readonly ConfigStore _store;

    [ObservableProperty] private List<BackupProfile> _profiles = [];
    [ObservableProperty] private BackupProfile?      _selectedProfile;
    [ObservableProperty] private BackupProfile?      _editingProfile;
    [ObservableProperty] private bool                _isEditing;
    [ObservableProperty] private bool                _isDirty;
    [ObservableProperty] private string              _testConnectionStatus = "";
    [ObservableProperty] private bool                _isTestingConnection;
    [ObservableProperty] private string              _newExclude = "";
    [ObservableProperty] private string              _cliImportText = "";
    [ObservableProperty] private string?             _cliImportError;

    public BackupConfigsViewModel(APIService api, ConfigStore store)
    {
        _api   = api;
        _store = store;
        _store.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ConfigStore.Profiles))
                Profiles = _store.Profiles;
        };
        Profiles = _store.Profiles;
    }

    partial void OnSelectedProfileChanged(BackupProfile? value)
    {
        if (value is null) { IsEditing = false; EditingProfile = null; return; }
        // Deep copy for editing
        EditingProfile = CloneProfile(value);
        IsEditing      = true;
        IsDirty        = false;
        TestConnectionStatus = "";
    }

    // MARK: - CRUD

    [RelayCommand]
    private void AddProfile()
    {
        var profile = new BackupProfile { Name = "New Backup" };
        _store.Add(profile);
        SelectedProfile = _store.Profiles[^1];
    }

    [RelayCommand]
    private void DeleteProfile(BackupProfile? profile)
    {
        if (profile is null) return;
        _store.Delete(profile);
        if (SelectedProfile?.Id == profile.Id)
        {
            SelectedProfile = null;
            EditingProfile  = null;
            IsEditing       = false;
        }
    }

    [RelayCommand]
    private void SaveProfile()
    {
        if (EditingProfile is null) return;
        _store.Update(EditingProfile);
        IsDirty = false;
    }

    [RelayCommand]
    private void DiscardChanges()
    {
        if (SelectedProfile is null) return;
        EditingProfile = CloneProfile(SelectedProfile);
        IsDirty        = false;
    }

    public void MarkDirty() => IsDirty = true;

    // MARK: - Excludes

    [RelayCommand]
    private void AddExclude()
    {
        var val = NewExclude.Trim();
        if (string.IsNullOrEmpty(val) || EditingProfile is null) return;
        if (!EditingProfile.Excludes.Contains(val))
        {
            EditingProfile.Excludes = [.. EditingProfile.Excludes, val];
            MarkDirty();
        }
        NewExclude = "";
    }

    [RelayCommand]
    private void RemoveExclude(string exclude)
    {
        if (EditingProfile is null) return;
        EditingProfile.Excludes = EditingProfile.Excludes.Where(e => e != exclude).ToList();
        MarkDirty();
    }

    // MARK: - Test Connection

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        if (EditingProfile is null) return;
        IsTestingConnection = true;
        TestConnectionStatus = "";

        var overrideUrl = EditingProfile.ServerOverride;
        var testUrl     = string.IsNullOrEmpty(overrideUrl) ? _api.ServerUrl : overrideUrl;

        // Temporarily swap URL
        var savedUrl    = _api.ServerUrl;
        var savedKey    = _api.ApiKey;
        _api.ServerUrl  = testUrl;
        if (!string.IsNullOrEmpty(EditingProfile.ServerOverride))
            _api.ApiKey = "";  // no override key in this context

        await _api.CheckHealthAsync();

        _api.ServerUrl  = savedUrl;
        _api.ApiKey     = savedKey;

        TestConnectionStatus = _api.IsConnected
            ? $"Connected ({_api.ServerVersion})"
            : $"Failed: {_api.ConnectionError}";

        IsTestingConnection = false;
    }

    // MARK: - CLI Import

    [RelayCommand]
    private void ImportFromCli()
    {
        CliImportError = null;
        var raw = CliImportText.Trim();
        if (string.IsNullOrEmpty(raw)) return;

        var profile = ParseCliCommand(raw);
        if (profile is null) { CliImportError = "Could not parse command. Expected: nestvault backup <path> --label ..."; return; }

        _store.Add(profile);
        SelectedProfile = _store.Profiles[^1];
        CliImportText   = "";
    }

    // MARK: - CLI Preview

    public string CliPreview => EditingProfile?.CliCommand(_api.ServerUrl) ?? "";

    // MARK: - Helpers

    private static BackupProfile CloneProfile(BackupProfile src) => new()
    {
        Id             = src.Id,
        Name           = src.Name,
        Label          = src.Label,
        SourcePath     = src.SourcePath,
        Excludes       = [.. src.Excludes],
        Workers        = src.Workers,
        Prefix         = src.Prefix,
        ServerOverride = src.ServerOverride,
        Enabled        = src.Enabled,
        Schedule       = new BackupSchedule
        {
            Frequency     = src.Schedule.Frequency,
            Hour          = src.Schedule.Hour,
            Minute        = src.Schedule.Minute,
            Weekday       = src.Schedule.Weekday,
            CustomMinutes = src.Schedule.CustomMinutes
        },
        LastRun            = src.LastRun,
        LastRunStatus      = src.LastRunStatus,
        Accumulate         = src.Accumulate,
        SmartSkip          = src.SmartSkip,
        LastFullBackupDate = src.LastFullBackupDate
    };

    private static BackupProfile? ParseCliCommand(string raw)
    {
        // Minimal CLI parser: nestvault backup <path> --label X --server Y ...
        var tokens = Tokenize(raw.Replace("\\\n", " "));
        int backupIdx = tokens.IndexOf("backup");
        if (backupIdx < 0) return null;
        var args = tokens[(backupIdx + 1)..];
        if (args.Count == 0) return null;

        var profile = new BackupProfile();
        bool seenFlag = false;

        for (int i = 0; i < args.Count; i++)
        {
            var tok = args[i];
            if (tok.StartsWith('-'))
            {
                seenFlag = true;
                switch (tok)
                {
                    case "--label" or "-l":   if (++i < args.Count) profile.Label = args[i]; break;
                    case "--server" or "-s":  if (++i < args.Count) profile.ServerOverride = args[i]; break;
                    case "--workers" or "-w": if (++i < args.Count && int.TryParse(args[i], out var w)) profile.Workers = w; break;
                    case "--prefix" or "-p":  if (++i < args.Count) profile.Prefix = args[i]; break;
                    case "--absorb":          profile.Accumulate = true; break;
                    case "--exclude" or "-e":
                        i++;
                        while (i < args.Count && !args[i].StartsWith('-'))
                            profile.Excludes.Add(args[i++]);
                        i--;
                        break;
                }
            }
            else if (!seenFlag && string.IsNullOrEmpty(profile.SourcePath))
            {
                profile.SourcePath = tok;
            }
        }

        if (string.IsNullOrEmpty(profile.SourcePath)) return null;
        profile.Name = string.IsNullOrEmpty(profile.Label) ? profile.SourcePath : profile.Label;
        return profile;
    }

    private static List<string> Tokenize(string input)
    {
        var tokens  = new List<string>();
        var current = System.Text.StringBuilder.Empty;
        char? inQuote = null;

        foreach (var ch in input)
        {
            if (inQuote.HasValue)
            {
                if (ch == inQuote.Value) inQuote = null;
                else current.Append(ch);
            }
            else if (ch == '"' || ch == '\'') inQuote = ch;
            else if (char.IsWhiteSpace(ch))
            {
                if (current.Length > 0) { tokens.Add(current.ToString()); current.Clear(); }
            }
            else current.Append(ch);
        }
        if (current.Length > 0) tokens.Add(current.ToString());
        return tokens;
    }
}

// Extension for System.Text.StringBuilder with Empty
file static class StringBuilderExt
{
    public static System.Text.StringBuilder Empty => new();
}
