using System;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NestVault_Windows.Services;
using NestVault_Windows.Views;
using Windows.Graphics;

namespace NestVault_Windows;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Mica backdrop (Windows 11 material)
        if (MicaController.IsSupported())
            SystemBackdrop = new MicaBackdrop { Kind = MicaKind.Base };

        // Custom title bar (extend content)
        ExtendsContentIntoTitleBar = true;

        // Minimum size
        AppWindow.Resize(new SizeInt32(1060, 700));
        AppWindow.SetIcon("Assets/app.ico");
        Title = "NestVault";

        // Minimize to tray instead of closing
        AppWindow.Closing += (_, args) =>
        {
            args.Cancel = true;
            Hide();
        };

        TaskbarProgressHelper.Initialize(this);
    }

    private void NavView_Loaded(object sender, RoutedEventArgs e)
    {
        NavView.SelectedItem = NavView.MenuItems[0]; // Dashboard
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item)
            Navigate(item.Tag?.ToString());
    }

    private void Navigate(string? tag)
    {
        Type? pageType = tag switch
        {
            "Dashboard"     => typeof(DashboardPage),
            "Backups"       => typeof(BackupsPage),
            "BackupConfigs" => typeof(BackupConfigsPage),
            "Cleanup"       => typeof(CleanupPage),
            "Settings"      => typeof(SettingsPage),
            _               => null
        };

        if (pageType is not null && ContentFrame.CurrentSourcePageType != pageType)
            ContentFrame.Navigate(pageType);
    }

    public void Show()
    {
        AppWindow.Show();
        Activate();
    }
}
