using System.Text;

namespace DesktopPet.Interop;

public static class TopLevelWindows
{
    /// <summary>
    /// Shell surfaces that are technically top-level, titled windows but aren't "programs" in the
    /// user's mental model — animating them falling on every open/close would be noisy and wrong
    /// (e.g. the Start menu, Action Center and search flyouts all use CoreWindow).
    /// </summary>
    private static readonly HashSet<string> ExcludedClassNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "Progman",
        "WorkerW",
        "Windows.UI.Core.CoreWindow",
        "ForegroundStaging",
        "MultitaskingViewFrame",
        "XamlExplorerHostIslandWindow",
        "TaskListThumbnailWnd",
        "tooltips_class32",
    };

    /// <summary>
    /// A window we're willing to animate on close: visible, has a title bar and a title,
    /// is its own root ancestor (truly top-level), isn't minimized, isn't a tool window or known
    /// shell surface, and isn't owned by <paramref name="excludeProcessId"/> (so the pet app never
    /// captures/animates itself or its own fall-overlay windows).
    /// </summary>
    public static bool IsEligible(IntPtr hwnd, uint excludeProcessId)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindowVisible(hwnd))
        {
            return false;
        }

        if (NativeMethods.IsIconic(hwnd))
        {
            return false;
        }

        if (NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT) != hwnd)
        {
            return false;
        }

        if (NativeMethods.GetWindowTextLength(hwnd) == 0)
        {
            return false;
        }

        var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE);
        if ((style & NativeMethods.WS_CAPTION) == 0)
        {
            return false;
        }

        var exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0)
        {
            return false;
        }

        if (ExcludedClassNames.Contains(GetClassName(hwnd)))
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == excludeProcessId)
        {
            return false;
        }

        return true;
    }

    public static string GetClassName(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        NativeMethods.GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static string GetTitle(IntPtr hwnd)
    {
        var length = NativeMethods.GetWindowTextLength(hwnd);
        if (length == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder(length + 1);
        NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static List<IntPtr> EnumerateEligible(uint excludeProcessId)
    {
        var result = new List<IntPtr>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (IsEligible(hwnd, excludeProcessId))
            {
                result.Add(hwnd);
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }
}
