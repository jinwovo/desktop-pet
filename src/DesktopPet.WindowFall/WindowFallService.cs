using System.Windows;
using DesktopPet.Interop;

namespace DesktopPet.WindowFall;

/// <summary>
/// Top-level facade: wires the close watcher to the fall overlays. Construct once from the UI
/// thread (so the underlying WinEventHook rides the WPF message pump) and keep it alive for the
/// app's lifetime.
/// </summary>
public sealed class WindowFallService : IDisposable
{
    /// <summary>
    /// Overlays are pre-created and recycled rather than constructed per close — see
    /// <see cref="FallOverlayWindow"/>. A few are kept so closing several windows at once
    /// (e.g. shutting down a bunch of apps) still animates, while bounding the cost.
    /// </summary>
    private const int OverlayPoolSize = 2;

    private readonly WindowSnapshotCache _cache = new();
    private readonly WindowCloseWatcher _watcher;
    private readonly List<FallOverlayWindow> _overlays = new();
    private readonly double _dpiScale;
    private RECT _taskbarRectPx;

    public WindowFallService(double dpiScale)
    {
        _dpiScale = dpiScale;
        _taskbarRectPx = QueryTaskbarRectPx();

        for (var i = 0; i < OverlayPoolSize; i++)
        {
            _overlays.Add(new FallOverlayWindow());
        }

        _watcher = new WindowCloseWatcher(_cache);
        _watcher.WindowClosed += OnWindowClosed;
    }

    private void OnWindowClosed(WindowSnapshot snapshot)
    {
        var overlay = _overlays.FirstOrDefault(o => o.IsAvailable);
        // All overlays busy: skip this one rather than queue up work that would stutter the pet.
        overlay?.Play(snapshot, _dpiScale, _taskbarRectPx);
    }

    private RECT QueryTaskbarRectPx()
    {
        var hwnd = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (hwnd != IntPtr.Zero && NativeMethods.GetWindowRect(hwnd, out var rect))
        {
            return rect;
        }

        // Fallback if the taskbar window couldn't be found: a small strip at the bottom-right
        // of the primary screen's work area, in the same pixel units GetWindowRect would use.
        var right = (int)(SystemParameters.WorkArea.Right * _dpiScale);
        var bottom = (int)(SystemParameters.PrimaryScreenHeight * _dpiScale);
        return new RECT
        {
            Left = right - 200,
            Right = right,
            Top = bottom - 40,
            Bottom = bottom,
        };
    }

    public void Dispose()
    {
        _watcher.WindowClosed -= OnWindowClosed;
        _watcher.Dispose();

        foreach (var overlay in _overlays)
        {
            overlay.Shutdown();
        }

        _overlays.Clear();
    }
}
