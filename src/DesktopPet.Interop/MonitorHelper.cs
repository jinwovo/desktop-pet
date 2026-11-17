using System.Runtime.InteropServices;

namespace DesktopPet.Interop;

[StructLayout(LayoutKind.Sequential)]
public struct MONITORINFO
{
    public int cbSize;
    public RECT rcMonitor;
    public RECT rcWork;
    public uint dwFlags;
}

public static class MonitorHelper
{
    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);

    public static RECT? GetMonitorBoundsForWindow(IntPtr window)
    {
        if (window == IntPtr.Zero) return null;
        var monitor = MonitorFromWindow(window, 2); // MONITOR_DEFAULTTONEAREST
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        return monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info) ? info.rcMonitor : null;
    }

    /// <summary>Work-area rect (excludes taskbar) of every connected monitor, in screen pixels.</summary>
    public static List<RECT> GetWorkAreasPx()
    {
        var result = new List<RECT>();

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdcMonitor, ref RECT clipRect, IntPtr dwData) =>
        {
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(hMonitor, ref info))
            {
                result.Add(info.rcWork);
            }
            return true;
        }, IntPtr.Zero);

        return result;
    }
}
