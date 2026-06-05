using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NestVault_Windows.Models;
using NestVault_Windows.Services;

namespace NestVault_Windows.ViewModels;

public partial class BackupsViewModel : ObservableObject
{
    private readonly APIService _api;

    [ObservableProperty] private List<BackupSummary> _filteredBackups = [];
    [ObservableProperty] private BackupSummary?      _selectedBackup;
    [ObservableProperty] private List<BackupVersion> _versions = [];
    [ObservableProperty] private BackupVersion?      _selectedVersion;
    [ObservableProperty] private List<VersionFile>   _files = [];
    [ObservableProperty] private string              _searchText = "";
    [ObservableProperty] private bool                _isLoadingVersions;
    [ObservableProperty] private bool                _isLoadingFiles;
    [ObservableProperty] private string?             _error;

    public BackupsViewModel(APIService api)
    {
        _api = api;
        _api.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(APIService.Backups))
                ApplyFilter();
        };
        ApplyFilter();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnSelectedBackupChanged(BackupSummary? value)
    {
        Versions        = [];
        SelectedVersion = null;
        Files           = [];
        if (value is not null)
            _ = LoadVersionsAsync(value.Label);
    }

    partial void OnSelectedVersionChanged(BackupVersion? value)
    {
        Files = [];
        if (value is not null && SelectedBackup is not null)
            _ = LoadFilesAsync(SelectedBackup.Label, value.VersionKey);
    }

    private void ApplyFilter()
    {
        var all = _api.Backups;
        FilteredBackups = string.IsNullOrWhiteSpace(SearchText)
            ? all
            : all.Where(b => b.Label.Contains(SearchText, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private async Task LoadVersionsAsync(string label)
    {
        IsLoadingVersions = true;
        Error = null;
        try   { Versions = await _api.FetchVersionsAsync(label); }
        catch (Exception ex) { Error = ex.Message; }
        finally { IsLoadingVersions = false; }
    }

    private async Task LoadFilesAsync(string label, string versionKey)
    {
        IsLoadingFiles = true;
        Error = null;
        try   { Files = await _api.FetchFilesAsync(label, versionKey); }
        catch (Exception ex) { Error = ex.Message; }
        finally { IsLoadingFiles = false; }
    }

    [RelayCommand]
    private async Task DeleteVersionAsync(BackupVersion? version)
    {
        if (version is null || SelectedBackup is null) return;
        try
        {
            await _api.DeleteVersionAsync(SelectedBackup.Label, version.VersionKey);
            Versions = Versions.Where(v => v.VersionKey != version.VersionKey).ToList();
            if (SelectedVersion?.VersionKey == version.VersionKey)
            {
                SelectedVersion = null;
                Files = [];
            }
        }
        catch (Exception ex) { Error = ex.Message; }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await _api.FetchBackupsAsync();
        ApplyFilter();
        if (SelectedBackup is not null)
            await LoadVersionsAsync(SelectedBackup.Label);
    }
}
