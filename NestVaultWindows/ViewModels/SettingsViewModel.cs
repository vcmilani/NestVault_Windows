using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NestVaultWindows.Models;
using NestVaultWindows.Services;

namespace NestVaultWindows.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly APIService    _api;
    private readonly PowerMonitor  _power;
    private readonly ScheduleManager _scheduler;

    [ObservableProperty] private string  _serverUrl = "";
    [ObservableProperty] private string  _apiKey    = "";
    [ObservableProperty] private bool    _showApiKey;
    [ObservableProperty] private bool    _isTestingConnection;
    [ObservableProperty] private string  _testConnectionStatus = "";
    [ObservableProperty] private bool    _launchAtStartup;
    [ObservableProperty] private bool    _pauseOnBattery;
    [ObservableProperty] private int     _minBatteryPercent;
    [ObservableProperty] private bool    _isOnAC;
    [ObservableProperty] private int     _batteryPercent;
    [ObservableProperty] private string  _networkType = "";
    [ObservableProperty] private bool    _isNetworkAvailable;
    [ObservableProperty] private BackupSchedule _queueSchedule = new();
    [ObservableProperty] private string  _appVersion  = "";
    [ObservableProperty] private string  _serverVersion = "";
    [ObservableProperty] private bool    _isDirty;

    public string PowerStatusText => IsOnAC ? "AC Power" : $"Battery ({BatteryPercent}%)";

    public SettingsViewModel(APIService api, PowerMonitor power, ScheduleManager scheduler)
    {
        _api       = api;
        _power     = power;
        _scheduler = scheduler;

        Load();

        _api.PropertyChanged   += (_, e) => { if (e.PropertyName is nameof(APIService.ServerVersion) or nameof(APIService.IsConnected)) RefreshStatus(); };
        _power.PropertyChanged += (_, e) => RefreshPower();
    }

    private void Load()
    {
        ServerUrl          = _api.ServerUrl;
        ApiKey             = _api.ApiKey;
        LaunchAtStartup    = StartupManager.IsEnabled;
        PauseOnBattery     = _scheduler.PauseOnBattery;
        MinBatteryPercent  = _scheduler.MinBatteryPercent;
        QueueSchedule      = _scheduler.QueueSchedule;
        AppVersion         = GetAppVersion();
        IsDirty            = false;
        RefreshStatus();
        RefreshPower();
    }

    private void RefreshStatus()
    {
        ServerVersion = _api.IsConnected ? $"v{_api.ServerVersion}" : "Disconnected";
    }

    private void RefreshPower()
    {
        IsOnAC             = _power.IsOnAC;
        BatteryPercent     = _power.BatteryPercent;
        NetworkType        = _power.NetworkType;
        IsNetworkAvailable = _power.IsNetworkAvailable;
    }

    partial void OnServerUrlChanged(string _) => IsDirty = true;
    partial void OnApiKeyChanged(string _)    => IsDirty = true;

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        IsTestingConnection   = true;
        TestConnectionStatus  = "";
        var saved = _api.ServerUrl;
        _api.ServerUrl = ServerUrl;
        await _api.CheckHealthAsync();
        _api.ServerUrl = saved;
        TestConnectionStatus = _api.IsConnected
            ? $"Connected ({_api.ServerVersion})"
            : $"Failed: {_api.ConnectionError}";
        IsTestingConnection = false;
    }

    [RelayCommand]
    private void Save()
    {
        _api.ServerUrl  = ServerUrl;
        _api.ApiKey     = ApiKey;
        _api.SaveSettings();
        StartupManager.SetEnabled(LaunchAtStartup);
        _scheduler.PauseOnBattery    = PauseOnBattery;
        _scheduler.MinBatteryPercent = MinBatteryPercent;
        _scheduler.QueueSchedule     = QueueSchedule;
        IsDirty = false;
    }

    private static string GetAppVersion()
    {
        var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        return ver is null ? "3.0" : $"{ver.Major}.{ver.Minor}";
    }
}
