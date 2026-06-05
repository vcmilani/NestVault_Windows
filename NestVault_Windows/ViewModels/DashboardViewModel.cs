using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NestVault_Windows.Models;
using NestVault_Windows.Services;

namespace NestVault_Windows.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly APIService _api;

    [ObservableProperty] private GlobalStats _stats = new(0, 0, 0, 0);
    [ObservableProperty] private List<BackupSummary> _backups = [];
    [ObservableProperty] private bool   _isConnected;
    [ObservableProperty] private bool   _isLoading;
    [ObservableProperty] private string _serverVersion = "";
    [ObservableProperty] private string? _connectionError;
    [ObservableProperty] private string _appVersion = GetAppVersion();

    public DashboardViewModel(APIService api)
    {
        _api = api;
        _api.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(APIService.Backups)
                or nameof(APIService.IsConnected)
                or nameof(APIService.ServerVersion)
                or nameof(APIService.ConnectionError))
                Refresh();
        };
        Refresh();
    }

    private void Refresh()
    {
        IsConnected     = _api.IsConnected;
        Backups         = _api.Backups;
        Stats           = _api.GlobalStats;
        ServerVersion   = _api.ServerVersion;
        ConnectionError = _api.ConnectionError;
    }

    [RelayCommand]
    private async Task ReloadAsync()
    {
        IsLoading = true;
        await _api.CheckHealthAsync();
        if (_api.IsConnected)
            await _api.FetchBackupsAsync();
        IsLoading = false;
        Refresh();
    }

    private static string GetAppVersion()
    {
        var asm = System.Reflection.Assembly.GetExecutingAssembly();
        var ver = asm.GetName().Version;
        return ver is null ? "3.0" : $"{ver.Major}.{ver.Minor}.{ver.Build}";
    }
}
