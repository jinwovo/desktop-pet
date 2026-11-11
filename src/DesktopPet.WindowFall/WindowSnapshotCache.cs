using DesktopPet.Interop;

namespace DesktopPet.WindowFall;

/// <summary>
/// Caches the last-known rect + rendered bitmap for each tracked top-level window, refreshed
/// periodically (see <see cref="WindowCloseWatcher"/>). When a window is destroyed, Windows has
/// already torn it down by the time we're notified, so this cache is the only way to still show
/// "what it looked like" in the fall animation.
/// </summary>
public sealed class WindowSnapshotCache
{
    // Refresh() runs on a background timer thread (see WindowCloseWatcher) while TryGet/Remove run
    // on the UI thread from the WinEventHook callback — the dictionary itself needs a lock.
    private readonly object _lock = new();
    private readonly Dictionary<IntPtr, WindowSnapshot> _snapshots = new();

    public WindowSnapshot? TryGet(IntPtr hwnd)
    {
        lock (_lock)
        {
            return _snapshots.TryGetValue(hwnd, out var snapshot) ? snapshot : null;
        }
    }

    public void Remove(IntPtr hwnd)
    {
        lock (_lock)
        {
            _snapshots.Remove(hwnd);
        }
    }

    public IReadOnlyCollection<IntPtr> TrackedHandles
    {
        get
        {
            lock (_lock)
            {
                return _snapshots.Keys.ToList();
            }
        }
    }

    /// <summary>Captures the window's current pixels via PrintWindow and stores/replaces its snapshot.</summary>
    public WindowSnapshot? Refresh(IntPtr hwnd)
    {
        var snapshot = Capture(hwnd); // expensive (PrintWindow) — intentionally done outside the lock
        if (snapshot is not null)
        {
            lock (_lock)
            {
                _snapshots[hwnd] = snapshot;
            }
        }

        return snapshot;
    }

    /// <summary>
    /// Longest edge the cached preview is kept at. The fall overlay is a transparent (and
    /// therefore software-rendered) window, so compositing a full-resolution window capture every
    /// frame is expensive enough to be visible. The animation shrinks away in half a second, so
    /// the reduced detail isn't noticeable.
    /// </summary>
    private const int MaxPreviewEdgePx = 640;

    public static WindowSnapshot? Capture(IntPtr hwnd)
    {
        if (!NativeMethods.GetWindowRect(hwnd, out var rect))
        {
            return null;
        }

        var width = rect.Width;
        var height = rect.Height;
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        using var bitmap = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            var hdc = graphics.GetHdc();
            bool captured;
            try
            {
                captured = NativeMethods.PrintWindow(hwnd, hdc, NativeMethods.PW_RENDERFULLCONTENT);
            }
            finally
            {
                graphics.ReleaseHdc(hdc);
            }

            if (!captured)
            {
                return null;
            }
        }

        using var preview = Downscale(bitmap, MaxPreviewEdgePx);

        var hBitmap = preview.GetHbitmap();
        try
        {
            var bitmapSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                IntPtr.Zero,
                System.Windows.Int32Rect.Empty,
                System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
            bitmapSource.Freeze();

            return new WindowSnapshot(hwnd, TopLevelWindows.GetTitle(hwnd), rect, bitmapSource);
        }
        finally
        {
            NativeMethods.DeleteObject(hBitmap);
        }
    }

    private static System.Drawing.Bitmap Downscale(System.Drawing.Bitmap source, int maxEdge)
    {
        var longest = Math.Max(source.Width, source.Height);
        if (longest <= maxEdge)
        {
            return (System.Drawing.Bitmap)source.Clone();
        }

        var scale = (double)maxEdge / longest;
        var w = Math.Max(1, (int)(source.Width * scale));
        var h = Math.Max(1, (int)(source.Height * scale));

        var scaled = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = System.Drawing.Graphics.FromImage(scaled))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
            g.DrawImage(source, 0, 0, w, h);
        }
        return scaled;
    }
}
