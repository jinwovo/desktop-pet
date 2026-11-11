using System.Threading;
using DesktopPet.Interop;

namespace DesktopPet.WindowFall;

/// <summary>
/// Watches every top-level window on the system. Periodically refreshes a snapshot cache for
/// "normal" windows (title bar, visible, not ours), and raises <see cref="WindowClosed"/> with the
/// last-known snapshot the instant one of them is destroyed (closed, not minimized).
/// Must be constructed on the WPF UI thread so the WinEventHook callback rides its message pump —
/// but the periodic refresh itself runs on a thread-pool timer: capturing every open window via
/// PrintWindow is expensive, and doing that on the UI thread stalls the pet's own animation.
/// </summary>
public sealed class WindowCloseWatcher : IDisposable
{
    private readonly WindowSnapshotCache _cache;
    private readonly WinEventHook _destroyHook;
    private readonly Timer _refreshTimer;
    private readonly uint _ownProcessId;
    private int _refreshing;

    public event Action<WindowSnapshot>? WindowClosed;

    public WindowCloseWatcher(WindowSnapshotCache cache, TimeSpan? refreshInterval = null)
    {
        _cache = cache;
        _ownProcessId = (uint)Environment.ProcessId;

        _destroyHook = new WinEventHook(NativeMethods.EVENT_OBJECT_DESTROY, NativeMethods.EVENT_OBJECT_DESTROY);
        _destroyHook.EventReceived += OnDestroyEvent;

        var interval = refreshInterval ?? TimeSpan.FromMilliseconds(800);
        _refreshTimer = new Timer(_ => RefreshEligibleWindows(), null, TimeSpan.Zero, interval);
    }

    private void RefreshEligibleWindows()
    {
        // Guard against overlap: if a previous refresh (many/large windows) is still running when
        // the next tick fires, skip rather than pile up PrintWindow calls on more threads.
        if (Interlocked.Exchange(ref _refreshing, 1) == 1)
        {
            return;
        }

        try
        {
            foreach (var hwnd in TopLevelWindows.EnumerateEligible(_ownProcessId))
            {
                _cache.Refresh(hwnd);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _refreshing, 0);
        }
    }

    private void OnDestroyEvent(WinEventArgs args)
    {
        var snapshot = _cache.TryGet(args.Hwnd);
        if (snapshot is null)
        {
            // Never captured (e.g. closed faster than the refresh interval) — nothing to animate.
            return;
        }

        _cache.Remove(args.Hwnd);
        WindowClosed?.Invoke(snapshot);
    }

    public void Dispose()
    {
        _refreshTimer.Dispose();
        _destroyHook.Dispose();
    }
}
