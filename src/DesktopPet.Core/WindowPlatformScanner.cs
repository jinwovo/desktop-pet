using DesktopPet.Interop;

namespace DesktopPet.Core;

/// <summary>
/// Builds the live list of "stand on top of this window" platforms from the other top-level
/// windows on screen. Cheap (window rects only, no pixel capture), so it's safe to poll often —
/// still done off the UI thread by the caller to keep the pet's render loop free of cross-process
/// Win32 round-trips.
/// </summary>
public static class WindowPlatformScanner
{
    /// <summary>Ignore slivers too narrow to plausibly stand on.</summary>
    private const double MinPlatformWidthPx = 60;

    public static List<Platform> Scan(uint excludeProcessId, double dpiScale)
    {
        // EnumWindows walks top-level windows in z-order, front-most first. We rely on that
        // ordering below: a window's top edge is only a real platform where no window *in front
        // of it* covers that edge. Without this the pet happily stands on the top edge of a
        // window that's completely hidden behind another one, and appears to float in mid-air.
        var rects = new List<RECT>();
        foreach (var hwnd in TopLevelWindows.EnumerateEligible(excludeProcessId))
        {
            if (NativeMethods.TryGetVisibleBounds(hwnd, out var rect) && rect.Width > 0 && rect.Height > 0)
            {
                rects.Add(rect);
            }
        }

        var platforms = new List<Platform>();
        for (var i = 0; i < rects.Count; i++)
        {
            var self = rects[i];
            var exposed = new List<(double Left, double Right)> { (self.Left, self.Right) };

            for (var j = 0; j < i && exposed.Count > 0; j++)
            {
                var inFront = rects[j];
                // Does the nearer window span the height of this window's top edge?
                if (inFront.Top <= self.Top && inFront.Bottom > self.Top)
                {
                    exposed = Subtract(exposed, inFront.Left, inFront.Right);
                }
            }

            foreach (var (left, right) in exposed)
            {
                if (right - left >= MinPlatformWidthPx)
                {
                    platforms.Add(new Platform(left / dpiScale, right / dpiScale, self.Top / dpiScale));
                }
            }
        }

        return platforms;
    }

    /// <summary>Removes the [cutLeft, cutRight) span from a set of disjoint, ascending spans.</summary>
    private static List<(double Left, double Right)> Subtract(
        List<(double Left, double Right)> spans, double cutLeft, double cutRight)
    {
        var result = new List<(double Left, double Right)>(spans.Count + 1);
        foreach (var span in spans)
        {
            if (cutRight <= span.Left || cutLeft >= span.Right)
            {
                result.Add(span);
                continue;
            }

            if (cutLeft > span.Left)
            {
                result.Add((span.Left, cutLeft));
            }

            if (cutRight < span.Right)
            {
                result.Add((cutRight, span.Right));
            }
        }

        return result;
    }
}
