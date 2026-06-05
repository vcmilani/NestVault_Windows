using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NestVault_Windows.Models;
using NestVault_Windows.Services;

namespace NestVault_Windows.ViewModels;

public partial class CleanupViewModel : ObservableObject
{
    private readonly APIService _api;

    public enum CleanupTarget { All, Specific }

    [ObservableProperty] private CleanupTarget         _target = CleanupTarget.All;
    [ObservableProperty] private string?               _selectedLabel;
    [ObservableProperty] private int                   _keepCount = 5;
    [ObservableProperty] private List<BackupSummary>   _availableBackups = [];
    [ObservableProperty] private List<VersionPreview>  _preview = [];
    [ObservableProperty] private bool                  _isLoadingPreview;
    [ObservableProperty] private bool                  _isRunning;
    [ObservableProperty] private List<CleanupResult>   _results = [];
    [ObservableProperty] private string?               _error;

    public bool HasError => Error is not null;
    public bool IsSpecificTarget => Target == CleanupTarget.Specific;

    public record CleanupResultItem(string Label, int Removed, int Kept, int StorageFilesRemoved);
    public List<CleanupResultItem> ResultItems => Results.Select(r => new CleanupResultItem(r.Label, r.Removed, r.Kept, r.StorageFilesRemoved)).ToList();

    public record VersionPreview(string Label, int TotalVersions, int ToRemove);

    public CleanupViewModel(APIService api)
    {
        _api = api;
        _api.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(APIService.Backups))
            {
                AvailableBackups = _api.Backups;
                _ = RefreshPreviewAsync();
            }
        };
        AvailableBackups = _api.Backups;
    }

    partial void OnTargetChanged(CleanupTarget _)       => _ = RefreshPreviewAsync();
    partial void OnSelectedLabelChanged(string? _)      => _ = RefreshPreviewAsync();
    partial void OnKeepCountChanged(int _)              => _ = RefreshPreviewAsync();

    [RelayCommand]
    private async Task RefreshPreviewAsync()
    {
        IsLoadingPreview = true;
        Results = [];
        Error   = null;

        try
        {
            var backups = Target == CleanupTarget.Specific && !string.IsNullOrEmpty(SelectedLabel)
                ? _api.Backups.Where(b => b.Label == SelectedLabel).ToList()
                : _api.Backups;

            var items = new List<VersionPreview>();
            foreach (var backup in backups)
            {
                var toRemove = Math.Max(0, backup.VersionCount - KeepCount);
                if (toRemove > 0)
                    items.Add(new VersionPreview(backup.Label, backup.VersionCount, toRemove));
            }
            Preview = items;
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { IsLoadingPreview = false; }
    }

    [RelayCommand]
    private async Task RunCleanupAsync()
    {
        IsRunning = true;
        Error     = null;
        Results   = [];

        try
        {
            if (Target == CleanupTarget.Specific && !string.IsNullOrEmpty(SelectedLabel))
                Results = [await _api.CleanupAsync(SelectedLabel, KeepCount)];
            else
                Results = await _api.CleanupAllAsync(KeepCount);

            await _api.FetchBackupsAsync();
            await RefreshPreviewAsync();
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { IsRunning = false; }
    }
}
