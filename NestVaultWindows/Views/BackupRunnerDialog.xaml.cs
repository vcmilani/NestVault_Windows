using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using NestVaultWindows.Models;
using NestVaultWindows.ViewModels;

namespace NestVaultWindows.Views;

public sealed partial class BackupRunnerDialog : ContentDialog
{
    public BackupRunnerViewModel ViewModel { get; }
    public string DialogTitle { get; }

    public BackupRunnerDialog(BackupProfile profile)
    {
        DialogTitle = $"Backup: {profile.Name}";
        ViewModel   = new BackupRunnerViewModel(App.Api, App.Config, App.Scheduler);
        InitializeComponent();

        // Start backup as soon as dialog loads
        Loaded += async (_, _) => await ViewModel.StartAsync(profile);
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BackupRunnerViewModel.Entries))
                AutoScrollLog();
        };
    }

    private void AutoScrollLog()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            LogScroller.ChangeView(null, LogScroller.ScrollableHeight, null);
        });
    }
}
