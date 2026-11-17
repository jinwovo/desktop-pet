namespace DesktopPet.Core;

public readonly record struct ForegroundWindowSnapshot(uint ProcessId, bool Visible, bool Minimized,
    bool Shell, DesktopRect Bounds);

[Flags]
public enum PetSuppressionReason { None = 0, Fullscreen = 1, SessionLocked = 2, Suspended = 4 }

public static class FullscreenPolicy
{
    /// <summary>Inputs are physical screen pixels; taskbar-excluding maximized windows do not qualify.</summary>
    public static bool ShouldPause(ForegroundWindowSnapshot foreground, DesktopRect petMonitor, uint ownProcessId)
    {
        if (!foreground.Visible || foreground.Minimized || foreground.Shell || foreground.ProcessId == 0 ||
            foreground.ProcessId == ownProcessId || !foreground.Bounds.IsValid || !petMonitor.IsValid) return false;
        const double tolerance = 2;
        var b = foreground.Bounds;
        return b.Left <= petMonitor.Left + tolerance && b.Top <= petMonitor.Top + tolerance &&
            b.Right >= petMonitor.Right - tolerance && b.Bottom >= petMonitor.Bottom - tolerance;
    }
}
