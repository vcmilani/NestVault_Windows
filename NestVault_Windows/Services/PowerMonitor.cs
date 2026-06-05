using System;
using System.Net.NetworkInformation;
using CommunityToolkit.Mvvm.ComponentModel;
using Windows.Devices.Power;
using Windows.System.Power;

namespace NestVault_Windows.Services;

public partial class PowerMonitor : ObservableObject, IDisposable
{
    [ObservableProperty] private bool   _isOnAC = true;
    [ObservableProperty] private int    _batteryPercent = 100;
    [ObservableProperty] private bool   _isNetworkAvailable;
    [ObservableProperty] private string _networkType = "";

    public PowerMonitor()
    {
        Refresh();
        try
        {
            NetworkChange.NetworkAvailabilityChanged += (_, _) => UpdateNetwork();
            Battery.AggregateBattery.ReportUpdated    += (_, _) => UpdateBattery();
        }
        catch { /* battery/network events unavailable on this hardware */ }
    }

    public void Refresh()
    {
        UpdateBattery();
        UpdateNetwork();
    }

    private void UpdateBattery()
    {
        try
        {
            var report  = Battery.AggregateBattery.GetReport();
            var onAC    = report.Status is BatteryStatus.NotPresent or BatteryStatus.Idle;
            var percent = 100;

            if (report.FullChargeCapacityInMilliwattHours.HasValue &&
                report.RemainingCapacityInMilliwattHours.HasValue &&
                report.FullChargeCapacityInMilliwattHours.Value > 0)
            {
                percent = (int)Math.Round(
                    report.RemainingCapacityInMilliwattHours.Value * 100.0 /
                    report.FullChargeCapacityInMilliwattHours.Value);
            }

            IsOnAC         = onAC;
            BatteryPercent = Math.Clamp(percent, 0, 100);
        }
        catch
        {
            IsOnAC         = true;
            BatteryPercent = 100;
        }
    }

    private void UpdateNetwork()
    {
        IsNetworkAvailable = NetworkInterface.GetIsNetworkAvailable();
        NetworkType = GetNetworkTypeName();
    }

    private static string GetNetworkTypeName()
    {
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback) continue;
            return ni.NetworkInterfaceType switch
            {
                NetworkInterfaceType.Wireless80211 => "Wi-Fi",
                NetworkInterfaceType.Ethernet       => "Ethernet",
                _                                   => "Connected"
            };
        }
        return "Disconnected";
    }

    public void Dispose() => GC.SuppressFinalize(this);
}
