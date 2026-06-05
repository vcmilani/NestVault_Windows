using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace NestVault_Windows.Services;

public static class TaskbarProgressHelper
{
    [ComImport]
    [Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);
        void MarkFullscreenWindow(IntPtr hwnd, bool fFullscreen);
        void SetProgressValue(IntPtr hwnd, ulong ullCompleted, ulong ullTotal);
        void SetProgressState(IntPtr hwnd, int tbpFlags);
    }

    [ComImport]
    [Guid("56fdf344-fd6d-11d0-958a-006097c9a090")]
    [ClassInterface(ClassInterfaceType.None)]
    private class TaskbarInstance { }

    private const int TBPF_NOPROGRESS  = 0;
    private const int TBPF_INDETERMINATE = 1;
    private const int TBPF_NORMAL       = 2;
    private const int TBPF_ERROR        = 4;
    private const int TBPF_PAUSED       = 8;

    private static ITaskbarList3? _taskbar;
    private static IntPtr _hwnd = IntPtr.Zero;

    public static void Initialize(Window window)
    {
        try
        {
            _hwnd    = WinRT.Interop.WindowNative.GetWindowHandle(window);
            _taskbar = (ITaskbarList3)new TaskbarInstance();
            _taskbar.HrInit();
        }
        catch { _taskbar = null; }
    }

    public static void SetProgress(double value)
    {
        if (_taskbar is null || _hwnd == IntPtr.Zero) return;
        try
        {
            if (value <= 0)
            {
                _taskbar.SetProgressState(_hwnd, TBPF_NOPROGRESS);
                return;
            }
            _taskbar.SetProgressState(_hwnd, TBPF_NORMAL);
            _taskbar.SetProgressValue(_hwnd, (ulong)(value * 1000), 1000);
        }
        catch { }
    }

    public static void ClearProgress()
    {
        if (_taskbar is null || _hwnd == IntPtr.Zero) return;
        try { _taskbar.SetProgressState(_hwnd, TBPF_NOPROGRESS); }
        catch { }
    }

    public static void SetIndeterminate()
    {
        if (_taskbar is null || _hwnd == IntPtr.Zero) return;
        try { _taskbar.SetProgressState(_hwnd, TBPF_INDETERMINATE); }
        catch { }
    }
}
