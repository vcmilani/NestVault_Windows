using System;
using System.Threading.Tasks;
using H.NotifyIcon;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.AppNotifications;
using NestVault_Windows.Services;
using NestVault_Windows.ViewModels;

namespace NestVault_Windows;

public partial class App : Application
{
    // Global services
    public static APIService      Api       { get; } = new();
    public static ConfigStore     Config    { get; } = new();
    public static PowerMonitor    Power     { get; } = new();
    public static ScheduleManager Scheduler { get; private set; } = null!;
    public static BackupRunner    Runner    { get; } = new(Api);

    public static MainWindow? MainAppWindow { get; private set; }

    private TaskbarIcon? _trayIcon;

    public App()
    {
        InitializeComponent();
        Scheduler = new ScheduleManager(Api, Config, Power);
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Register toast notifications
        try { AppNotificationManager.Default.Register(); }
        catch { /* not critical */ }

        // Create main window
        MainAppWindow = new MainWindow();
        MainAppWindow.Activate();

        // Setup system tray
        SetupTray();

        // Start background services
        Power.Refresh();
        Scheduler.Start();

        // Initial data load
        await Task.Run(async () =>
        {
            await Api.CheckHealthAsync();
            if (Api.IsConnected)
                await Api.FetchBackupsAsync();
        });
    }

    private void SetupTray()
    {
        if (Resources.TryGetValue("TrayIcon", out var obj) && obj is TaskbarIcon tray)
        {
            _trayIcon = tray;

            tray.LeftClickCommand    = new RelayCommand(ShowMainWindow);
            tray.DoubleClickCommand  = new RelayCommand(ShowMainWindow);

            if (tray.ContextFlyout is MenuFlyout menu)
            {
                foreach (var item in menu.Items)
                {
                    if (item is MenuFlyoutItem fi)
                    {
                        if (fi.Text == "Open NestVault")
                            fi.Click += (_, _) => ShowMainWindow();
                        else if (fi.Text == "Quit")
                            fi.Click += (_, _) => QuitApp();
                    }
                }
            }
        }
    }

    public static void ShowMainWindow()
    {
        if (MainAppWindow is null) return;
        MainAppWindow.DispatcherQueue.TryEnqueue(() =>
        {
            MainAppWindow.Show();
            MainAppWindow.Activate();
            if (MainAppWindow.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter p)
                p.Restore();
        });
    }

    private static void QuitApp()
    {
        Scheduler.Stop();
        Current.Exit();
    }
}

// Minimal RelayCommand for tray
file sealed class RelayCommand : System.Windows.Input.ICommand
{
    private readonly Action _execute;
    public RelayCommand(Action execute) { _execute = execute; }
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? _) => true;
    public void Execute(object? _)    => _execute();
}
