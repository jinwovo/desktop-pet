using System.Threading;
using System.Diagnostics;
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
    private int _disposed;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly CancellationToken _shutdownToken;

    public event Action<WindowSnapshot>? WindowClosed;

    public WindowCloseWatcher(WindowSnapshotCache cache, TimeSpan? refreshInterval = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        var interval = refreshInterval ?? TimeSpan.FromMilliseconds(800);
        // Timer truncates fractional milliseconds: a sub-ms positive period becomes zero
        // (one-shot), while periods above its native limit throw after native resources exist.
        if (interval.TotalMilliseconds < 1 || interval.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentOutOfRangeException(nameof(refreshInterval), "Refresh interval must be between 1 and 4294967294 milliseconds.");
        _cache = cache;
        _shutdownToken = _shutdown.Token;
        _ownProcessId = (uint)Environment.ProcessId;

        _destroyHook = new WinEventHook(NativeMethods.EVENT_OBJECT_DESTROY, NativeMethods.EVENT_OBJECT_DESTROY);
        _destroyHook.EventReceived += OnDestroyEvent;

        try
        {
            _refreshTimer = new Timer(_ => RefreshEligibleWindows(), null, TimeSpan.Zero, interval);
        }
        catch
        {
            _destroyHook.EventReceived -= OnDestroyEvent;
            _destroyHook.Dispose();
            _shutdown.Dispose();
            throw;
        }
    }

    private void RefreshEligibleWindows()
    {
        // Guard against overlap: if a previous refresh (many/large windows) is still running when
        // the next tick fires, skip rather than pile up PrintWindow calls on more threads.
        if (_shutdownToken.IsCancellationRequested || Interlocked.CompareExchange(ref _refreshing, 1, 0) != 0)
        {
            return;
        }

        try
        {
            if (_shutdownToken.IsCancellationRequested) return;
            // Match the tracked set to the cache bound; cycling more windows through a small
            // cache would evict fresh images and force expensive re-captures on every sweep.
            var eligible = TopLevelWindows.EnumerateEligible(_ownProcessId)
                .Take(WindowCaptureLimits.MaxCachedWindows).ToList();
            foreach (var hwnd in eligible)
            {
                if (_shutdownToken.IsCancellationRequested) return;
                try
                {
                    _cache.Refresh(hwnd, _shutdownToken);
                }
                catch (Exception ex)
                {
                    // Native/GDI windows can disappear or fail independently. One failed preview
                    // must not escape the Timer callback and take down the pet process.
                    _cache.Remove(hwnd);
                    Trace.TraceWarning($"Window capture skipped ({hwnd}): {ex.Message}");
                }
            }

            // Windows that closed or minimized since the last sweep no longer need a snapshot.
            if (!_shutdownToken.IsCancellationRequested) _cache.RetainOnly(eligible);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Window capture refresh skipped: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _refreshing, 0);
        }
    }

    private void OnDestroyEvent(WinEventArgs args)
    {
        if (_shutdownToken.IsCancellationRequested) return;
        var snapshot = _cache.TryGet(args.Hwnd);
        if (snapshot is null)
        {
            // Never captured (e.g. closed faster than the refresh interval) — nothing to animate.
            return;
        }

        _cache.Remove(args.Hwnd);
        if (!_shutdownToken.IsCancellationRequested)
        {
            try { WindowClosed?.Invoke(snapshot); }
            catch (Exception ex) { Trace.TraceWarning($"Window fall overlay skipped: {ex.Message}"); }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _shutdown.Cancel();
        _refreshTimer.Dispose();
        _destroyHook.EventReceived -= OnDestroyEvent;
        _destroyHook.Dispose();
        // Do not wait on the UI thread: an already running PrintWindow is synchronous and has
        // no cancellation/timeout parameter. Its result is discarded when it eventually returns.
        _cache.Clear();
        _shutdown.Dispose();
    }
}
