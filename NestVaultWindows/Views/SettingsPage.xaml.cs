using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NestVaultWindows.ViewModels;

namespace NestVaultWindows.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        ViewModel = new SettingsViewModel(App.Api, App.Power, App.Scheduler);
        InitializeComponent();
    }

    private void ToggleApiKey_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ShowApiKey = !ViewModel.ShowApiKey;
    }
}
