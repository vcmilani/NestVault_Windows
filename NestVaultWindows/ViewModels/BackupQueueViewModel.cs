using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NestVaultWindows.Models;
using NestVaultWindows.Services;

namespace NestVaultWindows.ViewModels;

public partial class BackupQueueViewModel : ObservableObject
{
    private readonly APIService      _api;
    private readonly ConfigStore     _store;
    private readonly ScheduleManager _scheduler;
    private BackupQueue?             _queue;

    [ObservableProperty] private List<BackupProfile>        _allProfiles = [];
    [ObservableProperty] private List<BackupProfile>        _selectedProfiles = [];
    [ObservableProperty] private List<BackupQueue.QueueItem> _queueItems = [];
    [ObservableProperty] private BackupQueue.QueueStatus    _status = BackupQueue.QueueStatus.Idle;
    [ObservableProperty] private double                     _progress;
    [ObservableProperty] private bool                       _canStart;
    [ObservableProperty] private bool                       _canCancel;

    public bool IsIdle     => Status is BackupQueue.QueueStatus.Idle;
    public bool IsRunning  => Status is BackupQueue.QueueStatus.Running;
    public bool IsFinished => Status is BackupQueue.QueueStatus.Done or BackupQueue.QueueStatus.Cancelled;

    public BackupQueueViewModel(APIService api, ConfigStore store, ScheduleManager scheduler)
    {
        _api       = api;
        _store     = store;
        _scheduler = scheduler;

        _store.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ConfigStore.Profiles))
            {
                AllProfiles     = _store.Profiles.Where(p => p.Enabled).ToList();
                SelectedProfiles = AllProfiles; // select all by default
                CanStart        = SelectedProfiles.Count > 0;
            }
        };

        AllProfiles      = _store.Profiles.Where(p => p.Enabled).ToList();
        SelectedProfiles = AllProfiles;
        CanStart         = SelectedProfiles.Count > 0;
    }

    public void ToggleProfile(BackupProfile profile, bool selected)
    {
        if (selected && !SelectedProfiles.Contains(profile))
            SelectedProfiles = [.. SelectedProfiles, profile];
        else if (!selected)
            SelectedProfiles = SelectedProfiles.Where(p => p.Id != profile.Id).ToList();
        CanStart = SelectedProfiles.Count > 0;
    }

    [RelayCommand]
    private async Task StartQueueAsync()
    {
        if (SelectedProfiles.Count == 0) return;

        _queue = new BackupQueue(_api, SelectedProfiles);
        _queue.PropertyChanged += (_, e) =>
        {
            QueueItems = _queue.Items;
            Status     = _queue.Status;
            Progress   = _queue.Progress;
            CanCancel  = _queue.Status == BackupQueue.QueueStatus.Running;
            CanStart   = _queue.Status is BackupQueue.QueueStatus.Idle
                                       or BackupQueue.QueueStatus.Done
                                       or BackupQueue.QueueStatus.Cancelled;
        };

        CanStart  = false;
        CanCancel = true;
        QueueItems = _queue.Items;

        await _queue.RunAsync();

        TaskbarProgressHelper.ClearProgress();
        CanCancel = false;
        CanStart  = true;
    }

    [RelayCommand]
    private void CancelQueue() => _queue?.Cancel();
}
