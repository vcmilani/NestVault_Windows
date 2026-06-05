using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NestVault_Windows.Models;
using NestVault_Windows.Services;

namespace NestVault_Windows.ViewModels;

public partial class BackupRunnerViewModel : ObservableObject
{
    private readonly APIService      _api;
    private readonly ConfigStore     _store;
    private readonly ScheduleManager _scheduler;
    private BackupRunner?            _runner;
    private BackupProfile?           _profile;

    [ObservableProperty] private List<BackupRunner.LogEntry> _entries = [];
    [ObservableProperty] private double              _progress;
    [ObservableProperty] private BackupRunner.Stats  _stats = new();
    [ObservableProperty] private string              _currentFile = "";
    [ObservableProperty] private BackupRunner.RunStatus _status = BackupRunner.RunStatus.Idle;
    [ObservableProperty] private bool                _canCancel;
    [ObservableProperty] private string              _profileName = "";

    public bool IsFinished => Status is BackupRunner.RunStatus.Done
                                     or BackupRunner.RunStatus.Failed
                                     or BackupRunner.RunStatus.Cancelled;
    public bool HasErrors   => Stats.Errors > 0;

    public BackupRunnerViewModel(APIService api, ConfigStore store, ScheduleManager scheduler)
    {
        _api       = api;
        _store     = store;
        _scheduler = scheduler;
    }

    public async Task StartAsync(BackupProfile profile)
    {
        _profile      = profile;
        ProfileName   = profile.Name;
        _runner       = new BackupRunner(_api);

        _runner.PropertyChanged += (_, e) =>
        {
            Entries     = _runner.Entries;
            Progress    = _runner.Progress;
            Stats       = _runner.Stats;
            CurrentFile = _runner.CurrentFile;
            Status      = _runner.Status;
            CanCancel   = _runner.Status == BackupRunner.RunStatus.Running;
        };

        _scheduler.RegisterManualRunner(_runner, profile.Id);
        CanCancel = true;

        await _runner.RunAsync(profile);

        // Update profile with last run info
        var updated = profile;
        updated.LastRun = DateTimeOffset.Now;
        updated.LastRunStatus = _runner.Status switch
        {
            BackupRunner.RunStatus.Done      => "done",
            BackupRunner.RunStatus.Failed    => "failed",
            BackupRunner.RunStatus.Cancelled => "cancelled",
            _                                => "unknown"
        };
        if (_runner.WasFullBackup && _runner.Status == BackupRunner.RunStatus.Done)
            updated.LastFullBackupDate = DateTimeOffset.Now;
        _store.Update(updated);

        _scheduler.ClearManualRunner(_runner);
        CanCancel = false;

        // Send toast notification
        TaskbarProgressHelper.ClearProgress();
        SendCompletionNotification(_runner.Status, profile.Label);
    }

    [RelayCommand]
    private void Cancel() => _runner?.Cancel();

    private static void SendCompletionNotification(BackupRunner.RunStatus status, string label)
    {
        try
        {
            var title = status == BackupRunner.RunStatus.Done
                ? "Backup complete"
                : status == BackupRunner.RunStatus.Failed ? "Backup failed" : "Backup cancelled";
            ToastHelper.Show(title, label);
        }
        catch { /* toast not critical */ }
    }
}
