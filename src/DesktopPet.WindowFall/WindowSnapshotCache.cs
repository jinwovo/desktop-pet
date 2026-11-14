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
    private long _generation;
    private static int _captureInProgress;

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
            _generation++;
        }
    }

    /// <summary>Releases previews and invalidates any capture that is still in flight.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _snapshots.Clear();
            _generation++;
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

    /// <summary>
    /// How long a snapshot of an unchanged window is trusted before being re-captured.
    ///
    /// This is the main cost control. PrintWindow doesn't render the window itself — it sends
    /// WM_PRINT and makes the *owning application* redraw, on that app's own UI thread. Measured
    /// at ~150ms to sweep six ordinary windows, which, repeated every second, visibly slows other
    /// programs (closing a window got choppy). Since the snapshot only has to be good enough for
    /// a half-second fall animation, a slightly stale image of a window nobody touched is fine.
    /// </summary>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Captures the window via PrintWindow and stores its snapshot — but skips the capture
    /// entirely when the window hasn't moved or resized and the existing snapshot is recent.
    /// </summary>
    public WindowSnapshot? Refresh(IntPtr hwnd, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested || !NativeMethods.GetWindowRect(hwnd, out var rect) ||
            !WindowCaptureLimits.TryGetCaptureSize(rect, out _, out _))
        {
            return null;
        }

        long generation;
        lock (_lock)
        {
            if (cancellationToken.IsCancellationRequested) return null;
            generation = _generation;
            if (_snapshots.TryGetValue(hwnd, out var existing)
                && DateTime.UtcNow - existing.CapturedAtUtc < StaleAfter
                && existing.RectPx.Width == rect.Width
                && existing.RectPx.Height == rect.Height)
            {
                if (SameRect(existing.RectPx, rect))
                {
                    return existing;
                }

                // Only moved: the pixels are still valid, so just track the new position.
                // Re-capturing on every move is what made dragging a window feel sluggish.
                var moved = existing with { RectPx = rect };
                _snapshots[hwnd] = moved;
                return moved;
            }
        }

        var snapshot = Capture(hwnd, cancellationToken); // expensive — intentionally outside the lock
        if (snapshot is not null)
        {
            lock (_lock)
            {
                if (cancellationToken.IsCancellationRequested || generation != _generation) return null;
                if (!_snapshots.ContainsKey(hwnd) && _snapshots.Count >= WindowCaptureLimits.MaxCachedWindows)
                {
                    var oldest = _snapshots.MinBy(pair => pair.Value.CapturedAtUtc).Key;
                    _snapshots.Remove(oldest);
                }
                _snapshots[hwnd] = snapshot;
            }
        }

        return snapshot;
    }

    private static bool SameRect(RECT a, RECT b) =>
        a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom;

    /// <summary>
    /// Drops snapshots for windows that are no longer being tracked. Without this the cache grows
    /// for the life of the process as windows are opened and closed, holding a bitmap for each.
    /// </summary>
    public void RetainOnly(IEnumerable<IntPtr> keep)
    {
        var alive = new HashSet<IntPtr>(keep);
        lock (_lock)
        {
            var removed = false;
            foreach (var gone in _snapshots.Keys.Where(k => !alive.Contains(k)).ToList())
            {
                _snapshots.Remove(gone);
                removed = true;
            }
            if (removed) _generation++;
        }
    }

    /// <summary>
    /// Longest edge the cached preview is kept at. The fall overlay is a transparent (and
    /// therefore software-rendered) window, so compositing a full-resolution window capture every
    /// frame is expensive enough to be visible. The animation shrinks away in half a second, so
    /// the reduced detail isn't noticeable.
    /// </summary>
    public static WindowSnapshot? Capture(IntPtr hwnd, CancellationToken cancellationToken = default)
    {
        // A native capture cannot be aborted. Share this nonblocking gate across service
        // instances so toggling the feature off/on cannot accumulate hung capture threads.
        if (cancellationToken.IsCancellationRequested || Interlocked.CompareExchange(ref _captureInProgress, 1, 0) != 0)
            return null;
        try { return CaptureCore(hwnd, cancellationToken); }
        finally { Interlocked.Exchange(ref _captureInProgress, 0); }
    }

    private static WindowSnapshot? CaptureCore(IntPtr hwnd, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested || !NativeMethods.GetWindowRect(hwnd, out var rect))
        {
            return null;
        }

        if (!WindowCaptureLimits.TryGetCaptureSize(rect, out var width, out var height))
        {
            return null;
        }

        if (cancellationToken.IsCancellationRequested) return null;
        using var bitmap = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            var hdc = graphics.GetHdc();
            bool captured;
            try
            {
                if (cancellationToken.IsCancellationRequested) return null;
                captured = NativeMethods.PrintWindow(hwnd, hdc, NativeMethods.PW_RENDERFULLCONTENT);
            }
            finally
            {
                graphics.ReleaseHdc(hdc);
            }

            if (!captured || cancellationToken.IsCancellationRequested)
            {
                return null;
            }
        }

        using var preview = Downscale(bitmap);

        if (cancellationToken.IsCancellationRequested) return null;
        var hBitmap = preview.GetHbitmap();
        try
        {
            var bitmapSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                IntPtr.Zero,
                System.Windows.Int32Rect.Empty,
                System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
            bitmapSource.Freeze();

            if (cancellationToken.IsCancellationRequested) return null;
            return new WindowSnapshot(hwnd, TopLevelWindows.GetTitle(hwnd), rect, bitmapSource, DateTime.UtcNow);
        }
        finally
        {
            NativeMethods.DeleteObject(hBitmap);
        }
    }

    private static System.Drawing.Bitmap Downscale(System.Drawing.Bitmap source)
    {
        var (w, h) = WindowCaptureLimits.GetPreviewSize(source.Width, source.Height);
        if (source.Width == w && source.Height == h)
        {
            return (System.Drawing.Bitmap)source.Clone();
        }

        var scaled = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            using var g = System.Drawing.Graphics.FromImage(scaled);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
            g.DrawImage(source, 0, 0, w, h);
            return scaled;
        }
        catch
        {
            scaled.Dispose();
            throw;
        }
    }
}
