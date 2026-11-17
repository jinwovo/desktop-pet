using System.Diagnostics;

namespace DesktopPet.Core;

/// <summary>Runs non-overlapping background scans only while the pet is visible.</summary>
/// <remarks>
/// Starts paused. Resume requests an immediate background scan; later scans are scheduled
/// after the previous scan finishes. Pause/Dispose discard any in-flight result without
/// waiting for native enumeration. Publication is serialized with lifecycle changes.
/// The publisher must be fast, nonblocking and must not reenter this monitor.
/// </remarks>
public sealed class WindowPlatformMonitor : IDisposable
{
    private readonly object _gate = new();
    private readonly Func<IReadOnlyList<Platform>> _scan;
    private readonly Action<IReadOnlyList<Platform>> _publish;
    private readonly ITimer _timer;
    private readonly TimeSpan _interval;
    private bool _running, _scanning, _disposed;
    private long _generation;

    public WindowPlatformMonitor(Func<IReadOnlyList<Platform>> scan,
        Action<IReadOnlyList<Platform>> publish, TimeProvider? timeProvider = null,
        TimeSpan? interval = null)
    {
        ArgumentNullException.ThrowIfNull(scan);
        ArgumentNullException.ThrowIfNull(publish);
        _interval = interval ?? TimeSpan.FromMilliseconds(300);
        if (_interval.TotalMilliseconds is < 1 or > uint.MaxValue - 1)
            throw new ArgumentOutOfRangeException(nameof(interval));
        _scan = scan;
        _publish = publish;
        _timer = (timeProvider ?? TimeProvider.System).CreateTimer(
            _ => ScanOnce(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public void Resume()
    {
        lock (_gate)
        {
            if (_disposed || _running) return;
            _running = true;
            _generation++;
            if (!_scanning) Schedule(TimeSpan.Zero);
        }
    }

    public void Pause()
    {
        lock (_gate)
        {
            if (_disposed || !_running) return;
            _running = false;
            _generation++;
            Schedule(Timeout.InfiniteTimeSpan);
        }
    }

    private void Schedule(TimeSpan dueTime) => _timer.Change(dueTime, Timeout.InfiniteTimeSpan);

    private void ScanOnce()
    {
        long generation;
        lock (_gate)
        {
            if (_disposed || !_running || _scanning) return;
            _scanning = true;
            generation = _generation;
            Schedule(Timeout.InfiniteTimeSpan);
        }

        IReadOnlyList<Platform>? snapshot = null;
        try { snapshot = _scan(); }
        catch (Exception ex)
        {
            // Never let a failed cross-process scan terminate a ThreadPool timer callback.
            Trace.TraceWarning("Window platform scan failed: {0}", ex.GetType().Name);
        }

        lock (_gate)
        {
            _scanning = false;
            if (_disposed || !_running) return;
            if (generation == _generation && snapshot is not null)
            {
                try { _publish(snapshot); }
                catch (Exception ex) { Trace.TraceWarning("Window platform publication failed: {0}", ex.GetType().Name); }
            }
            // A hide/show during native enumeration invalidates that snapshot. Refresh now
            // rather than waiting another interval, while retaining the one-scan limit.
            Schedule(generation == _generation ? _interval : TimeSpan.Zero);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _running = false;
            _generation++;
            _timer.Dispose();
        }
    }
}
