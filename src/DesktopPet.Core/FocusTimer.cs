namespace DesktopPet.Core;

public enum FocusSessionKind { Focus, Break }
public enum FocusTimerStatus { Stopped, Running, Paused, Completed }
public readonly record struct FocusTimerSnapshot(FocusSessionKind Session, FocusTimerStatus Status,
    TimeSpan Remaining, int CompletedFocusSessions);
public readonly record struct FocusSessionCompletion(FocusSessionKind Session, TimeSpan Duration, int CompletedFocusSessions);

/// <summary>
/// A local, manually advanced focus timer. Monotonic timestamps avoid wall-clock adjustments;
/// a delayed tick completes only the current session and never invents unattended follow-up cycles.
/// </summary>
public sealed class FocusTimer : IDisposable
{
    private readonly TimeProvider _clock;
    private readonly TimeSpan _focusDuration;
    private readonly TimeSpan _breakDuration;
    private FocusSessionKind _session;
    private FocusTimerStatus _status;
    private TimeSpan _remaining;
    private long _checkpoint;
    private int _completedFocusSessions;
    private bool _disposed;

    public FocusTimer(TimeProvider? clock = null, TimeSpan? focusDuration = null, TimeSpan? breakDuration = null)
    {
        _clock = clock ?? TimeProvider.System;
        _focusDuration = ValidateDuration(focusDuration ?? TimeSpan.FromMinutes(25), nameof(focusDuration));
        _breakDuration = ValidateDuration(breakDuration ?? TimeSpan.FromMinutes(5), nameof(breakDuration));
        if (_clock.TimestampFrequency <= 0) throw new ArgumentException("타이머 시계의 주파수가 올바르지 않습니다.", nameof(clock));
        _remaining = _focusDuration;
    }

    public FocusTimerSnapshot Snapshot => new(_session, _status, _remaining, _completedFocusSessions);
    public event Action<FocusSessionCompletion>? SessionCompleted;

    public void Start(FocusSessionKind session = FocusSessionKind.Focus)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!Enum.IsDefined(session)) throw new ArgumentOutOfRangeException(nameof(session));
        _session = session;
        _remaining = Duration(session);
        _checkpoint = _clock.GetTimestamp();
        _status = FocusTimerStatus.Running;
    }

    public void Pause()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Tick();
        if (_status == FocusTimerStatus.Running) _status = FocusTimerStatus.Paused;
    }

    public void Resume()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_status != FocusTimerStatus.Paused) return;
        _checkpoint = _clock.GetTimestamp();
        _status = FocusTimerStatus.Running;
    }

    public void Restart()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Start(_session);
    }

    public void Stop()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _status = FocusTimerStatus.Stopped;
        _session = FocusSessionKind.Focus;
        _remaining = _focusDuration;
    }

    public void Tick()
    {
        if (_disposed || _status != FocusTimerStatus.Running) return;
        var now = _clock.GetTimestamp();
        if (now < _checkpoint) return; // A broken/custom monotonic clock must never add time or fire early.
        var elapsed = _clock.GetElapsedTime(_checkpoint, now);
        if (elapsed <= TimeSpan.Zero) return;
        _checkpoint = now;
        if (elapsed < _remaining)
        {
            _remaining -= elapsed;
            return;
        }
        _remaining = TimeSpan.Zero;
        _status = FocusTimerStatus.Completed;
        if (_session == FocusSessionKind.Focus) _completedFocusSessions++;
        SessionCompleted?.Invoke(new FocusSessionCompletion(_session, Duration(_session), _completedFocusSessions));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _status = FocusTimerStatus.Stopped;
        _remaining = TimeSpan.Zero;
        SessionCompleted = null;
    }

    private TimeSpan Duration(FocusSessionKind session) => session == FocusSessionKind.Focus ? _focusDuration : _breakDuration;
    private static TimeSpan ValidateDuration(TimeSpan duration, string parameter) => duration > TimeSpan.Zero && duration <= TimeSpan.FromDays(1)
        ? duration : throw new ArgumentOutOfRangeException(parameter, "타이머 시간은 0보다 크고 24시간 이하여야 합니다.");
}
