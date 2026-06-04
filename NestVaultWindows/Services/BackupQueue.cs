using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using NestVaultWindows.Models;

namespace NestVaultWindows.Services;

public partial class BackupQueue : ObservableObject
{
    public enum QueueStatus { Idle, Running, Done, Cancelled }

    public enum ItemStatus { Waiting, Running, Done, Failed, Cancelled }

    public class QueueItem
    {
        public Guid          Id      { get; } = Guid.NewGuid();
        public BackupProfile Profile { get; init; } = new();
        public ItemStatus    Status  { get; set; } = ItemStatus.Waiting;

        public string StatusIcon => Status switch
        {
            ItemStatus.Waiting   => "⏳",
            ItemStatus.Running   => "↑",
            ItemStatus.Done      => "✓",
            ItemStatus.Failed    => "✗",
            ItemStatus.Cancelled => "⊘",
            _                    => "—"
        };
    }

    [ObservableProperty] private List<QueueItem> _items = [];
    [ObservableProperty] private int             _currentIndex = -1;
    [ObservableProperty] private QueueStatus     _status = QueueStatus.Idle;
    [ObservableProperty] private BackupRunner?   _currentRunner;

    private readonly APIService _api;
    private bool _isCancelled;

    public BackupQueue(APIService api, List<BackupProfile> profiles)
    {
        _api   = api;
        _items = profiles.Select(p => new QueueItem { Profile = p }).ToList();
    }

    public int    DoneCount    => Items.Count(i => i.Status == ItemStatus.Done);
    public int    FailedCount  => Items.Count(i => i.Status == ItemStatus.Failed);
    public double Progress
    {
        get
        {
            if (Items.Count == 0) return 0;
            var baseIdx = CurrentIndex < 0 ? 0.0 : (double)CurrentIndex;
            var sub     = CurrentRunner?.Progress ?? 0;
            return (baseIdx + sub) / Items.Count;
        }
    }

    public async Task RunAsync()
    {
        if (Items.Count == 0) return;
        Status       = QueueStatus.Running;
        _isCancelled = false;
        CurrentIndex = -1;

        for (int i = 0; i < Items.Count; i++)
        {
            if (_isCancelled)
            {
                for (int j = i; j < Items.Count; j++)
                    Items[j].Status = ItemStatus.Cancelled;
                break;
            }

            CurrentIndex      = i;
            Items[i].Status   = ItemStatus.Running;

            var runner = new BackupRunner(_api);
            CurrentRunner = runner;
            await runner.RunAsync(Items[i].Profile);

            Items[i].Status = runner.Status switch
            {
                BackupRunner.RunStatus.Done      => ItemStatus.Done,
                BackupRunner.RunStatus.Failed    => ItemStatus.Failed,
                BackupRunner.RunStatus.Cancelled => ItemStatus.Cancelled,
                _                                => ItemStatus.Failed
            };

            if (_isCancelled)
            {
                for (int j = i + 1; j < Items.Count; j++)
                    Items[j].Status = ItemStatus.Cancelled;
                break;
            }
        }

        CurrentRunner = null;
        CurrentIndex  = -1;
        Status        = _isCancelled ? QueueStatus.Cancelled : QueueStatus.Done;
    }

    public void Cancel()
    {
        if (Status != QueueStatus.Running) return;
        _isCancelled = true;
        CurrentRunner?.Cancel();
    }
}
