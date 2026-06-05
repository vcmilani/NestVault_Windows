using Microsoft.UI.Xaml.Controls;
using NestVault_Windows.Models;
using NestVault_Windows.ViewModels;

namespace NestVault_Windows.Views;

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
