using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using NestVault_Windows.Models;

namespace NestVault_Windows.Services;

public partial class ScheduleManager : ObservableObject, IDisposable
{
    [ObservableProperty] private bool          _isRunningScheduled;
    [ObservableProperty] private Guid?         _currentProfileId;
    [ObservableProperty] private DateTimeOffset _lastTickDate = DateTimeOffset.MinValue;
    [ObservableProperty] private BackupRunner?  _activeRunner;
    [ObservableProperty] private BackupRunner?  _activeManualRunner;
    [ObservableProperty] private Guid?          _activeManualProfileId;
    [ObservableProperty] private BackupQueue?   _activeQueue;

    // User preferences (persisted)
    public bool PauseOnBattery
    {
        get => ConfigStore.LoadSetting("schedule.pauseOnBattery", "true") == "true";
        set => ConfigStore.SaveSetting("schedule.pauseOnBattery", value ? "true" : "false");
    }

    public int MinBatteryPercent
    {
        get => int.TryParse(ConfigStore.LoadSetting("schedule.minBatteryPercent", "50"), out var v) ? v : 50;
        set => ConfigStore.SaveSetting("schedule.minBatteryPercent", value.ToString());
    }

    public BackupSchedule QueueSchedule
    {
        get => ConfigStore.LoadJson<BackupSchedule>("queue.schedule.config") ?? new BackupSchedule();
        set => ConfigStore.SaveJson("queue.schedule.config", value);
    }

    public DateTimeOffset? QueueScheduleLastRun
    {
        get
        {
            var s = ConfigStore.LoadSetting("queue.schedule.lastRun", "");
            return DateTimeOffset.TryParse(s, out var d) ? d : null;
        }
        set => ConfigStore.SaveSetting("queue.schedule.lastRun", value?.ToString("O") ?? "");
    }

    public DateTimeOffset? NextQueueRun
    {
        get
        {
            var qs = QueueSchedule;
            return qs.Enabled ? qs.NextRun(QueueScheduleLastRun) : null;
        }
    }

    private readonly APIService    _api;
    private readonly ConfigStore   _store;
    private readonly PowerMonitor  _power;
    private Timer?                 _timer;

    public ScheduleManager(APIService api, ConfigStore store, PowerMonitor power)
    {
        _api   = api;
        _store = store;
        _power = power;
    }

    public void Start()
    {
        _timer?.Dispose();
        _timer = new Timer(_ => Tick(), null, TimeSpan.Zero, TimeSpan.FromSeconds(30));
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    // MARK: - Tick

    private void Tick()
    {
        LastTickDate = DateTimeOffset.Now;
        if (IsRunningScheduled || ActiveManualRunner is not null || ActiveQueue is not null) return;

        // Check API connectivity
        if (!_api.IsConnected)
        {
            Task.Run(() => _api.CheckHealthAsync());
            return;
        }

        // Battery checks
        if (!_power.IsOnAC)
        {
            if (PauseOnBattery) return;
            if (_power.BatteryPercent < MinBatteryPercent) return;
        }

        // Network check
        if (!_power.IsNetworkAvailable) return;

        // Queue schedule fires before individual profiles
        var qs = QueueSchedule;
        if (qs.IsDue(QueueScheduleLastRun))
        {
            var profiles = _store.Profiles
                .Where(p => p.Enabled && !string.IsNullOrEmpty(p.Label) && !string.IsNullOrEmpty(p.SourcePath))
                .ToList();
            if (profiles.Count > 0)
            {
                Task.Run(() => RunScheduledQueueAsync(profiles));
                return;
            }
        }

        // Find a due individual profile
        var due = _store.Profiles.FirstOrDefault(p =>
            p.Enabled &&
            !string.IsNullOrEmpty(p.Label) &&
            !string.IsNullOrEmpty(p.SourcePath) &&
            p.Schedule.Enabled &&
            p.Schedule.IsDue(p.LastRun));

        if (due is not null)
            Task.Run(() => RunScheduledAsync(due));
    }

    // MARK: - Scheduled Queue

    private async Task RunScheduledQueueAsync(List<BackupProfile> profiles)
    {
        var queue = new BackupQueue(_api, profiles);
        ActiveQueue            = queue;
        QueueScheduleLastRun   = DateTimeOffset.Now;
        await queue.RunAsync();
        ActiveQueue = null;
    }

    // MARK: - Individual Profile

    private async Task RunScheduledAsync(BackupProfile profile)
    {
        IsRunningScheduled = true;
        CurrentProfileId   = profile.Id;

        var runner = new BackupRunner(_api);
        ActiveRunner = runner;
        await runner.RunAsync(profile);

        var updated = profile;
        updated.LastRun = DateTimeOffset.Now;
        updated.LastRunStatus = runner.Status switch
        {
            BackupRunner.RunStatus.Done      => "done",
            BackupRunner.RunStatus.Failed    => "failed",
            BackupRunner.RunStatus.Cancelled => "cancelled",
            _                                => "unknown"
        };
        if (runner.WasFullBackup && runner.Status == BackupRunner.RunStatus.Done)
            updated.LastFullBackupDate = DateTimeOffset.Now;

        _store.Update(updated);

        ActiveRunner       = null;
        CurrentProfileId   = null;
        IsRunningScheduled = false;
    }

    // MARK: - Manual Run Registration

    public void RegisterManualRunner(BackupRunner runner, Guid profileId)
    {
        ActiveManualRunner    = runner;
        ActiveManualProfileId = profileId;
    }

    public void ClearManualRunner(BackupRunner runner)
    {
        if (!ReferenceEquals(ActiveManualRunner, runner)) return;
        ActiveManualRunner    = null;
        ActiveManualProfileId = null;
    }

    public void Dispose()
    {
        _timer?.Dispose();
        GC.SuppressFinalize(this);
    }
}
