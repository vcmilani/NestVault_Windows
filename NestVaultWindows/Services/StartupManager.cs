using System;
using Microsoft.Win32;

namespace NestVaultWindows.Services;

public static class StartupManager
{
    private const string RegistryKey  = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName      = "NestVault";

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, writable: false);
                return key?.GetValue(AppName) is not null;
            }
            catch { return false; }
        }
    }

    public static void SetEnabled(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, writable: true);
            if (key is null) return;

            if (enable)
            {
                var exe = Environment.ProcessPath ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
                key.SetValue(AppName, $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue(AppName, throwOnMissingValue: false);
            }
        }
        catch { /* ignore registry errors */ }
    }
}
