using Microsoft.UI.Xaml.Controls;
using NestVaultWindows.Models;
using NestVaultWindows.ViewModels;

namespace NestVaultWindows.Views;

public sealed partial class BackupQueueDialog : ContentDialog
{
    public BackupQueueViewModel ViewModel { get; }

    public BackupQueueDialog()
    {
        ViewModel = new BackupQueueViewModel(App.Api, App.Config, App.Scheduler);
        InitializeComponent();
    }

    private void ProfileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        foreach (var added in e.AddedItems)
            if (added is BackupProfile p) ViewModel.ToggleProfile(p, true);
        foreach (var removed in e.RemovedItems)
            if (removed is BackupProfile p) ViewModel.ToggleProfile(p, false);
    }
}
