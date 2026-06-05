using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NestVault_Windows.ViewModels;
using Windows.Storage.Pickers;

namespace NestVault_Windows.Views;

public sealed partial class BackupConfigsPage : Page
{
    public BackupConfigsViewModel ViewModel { get; }

    public BackupConfigsPage()
    {
        ViewModel = new BackupConfigsViewModel(App.Api, App.Config);
        InitializeComponent();
    }

    private async void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.EditingProfile is null) return;

        var picker = new FolderPicker();
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder;
        picker.FileTypeFilter.Add("*");

        // Associate with window handle (required on WinUI 3 unpackaged)
        WinRT.Interop.InitializeWithWindow.Initialize(picker,
            WinRT.Interop.WindowNative.GetWindowHandle(App.MainAppWindow!));

        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            ViewModel.EditingProfile.SourcePath = folder.Path;
            ViewModel.MarkDirty();
        }
    }

    private void RemoveExclude_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is string exclude)
            ViewModel.RemoveExcludeCommand.Execute(exclude);
    }

    private async void RunBackup_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedProfile is null) return;
        var dialog = new BackupRunnerDialog(ViewModel.SelectedProfile) { XamlRoot = XamlRoot };
        await dialog.ShowAsync();
    }

    private async void RunQueue_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new BackupQueueDialog { XamlRoot = XamlRoot };
        await dialog.ShowAsync();
    }
}
