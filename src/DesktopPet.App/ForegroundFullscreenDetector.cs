using DesktopPet.Core;
using DesktopPet.Interop;

namespace DesktopPet.App;

internal static class ForegroundFullscreenDetector
{
    public static bool ShouldPause(IntPtr petWindow)
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero || !NativeMethods.GetWindowRect(foreground, out var bounds) ||
            MonitorHelper.GetMonitorBoundsForWindow(petWindow) is not { } monitor) return false;
        NativeMethods.GetWindowThreadProcessId(foreground, out var pid);
        var shell = TopLevelWindows.GetClassName(foreground) is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
        return FullscreenPolicy.ShouldPause(new(pid, NativeMethods.IsWindowVisible(foreground),
            NativeMethods.IsIconic(foreground), shell, new(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom)),
            new(monitor.Left, monitor.Top, monitor.Right, monitor.Bottom), (uint)Environment.ProcessId);
    }
}
