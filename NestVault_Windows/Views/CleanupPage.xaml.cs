using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NestVault_Windows.ViewModels;

namespace NestVault_Windows.Views;

public sealed partial class CleanupPage : Page
{
    public CleanupViewModel ViewModel { get; }

    public CleanupPage()
    {
        ViewModel = new CleanupViewModel(App.Api);
        InitializeComponent();
    }

    private void TargetRadio_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ViewModel.Target = TargetRadio.SelectedIndex == 1
            ? CleanupViewModel.CleanupTarget.Specific
            : CleanupViewModel.CleanupTarget.All;
    }

    private async void RunCleanup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title             = "Confirm Cleanup",
            Content           = $"Remove old versions from {(ViewModel.Target == CleanupViewModel.CleanupTarget.All ? "all backups" : ViewModel.SelectedLabel)}, keeping {ViewModel.KeepCount} most recent?\n\nThis cannot be undone.",
            PrimaryButtonText = "Run Cleanup",
            CloseButtonText   = "Cancel",
            DefaultButton     = ContentDialogButton.Close,
            XamlRoot          = XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
            await ViewModel.RunCleanupCommand.ExecuteAsync(null);
    }
}
