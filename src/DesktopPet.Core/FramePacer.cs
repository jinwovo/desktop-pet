namespace DesktopPet.Core;

/// <summary>Limits work using a fixed time grid, without turning 60 Hz on a 144 Hz display into 48 Hz.</summary>
public sealed class FramePacer
{
    private int _maximumFps = 60;
    private double _nextDue = double.NaN;
    private double _lastSeen = double.NaN;
    public int MaximumFps
    {
        get => _maximumFps;
        set
        {
            if (value is not (30 or 60 or 120)) throw new ArgumentOutOfRangeException(nameof(value));
            if (_maximumFps == value) return;
            _maximumFps = value;
            Reset();
        }
    }
    public bool ShouldRender(double elapsedMilliseconds)
    {
        if (!double.IsFinite(elapsedMilliseconds) || elapsedMilliseconds < 0 || elapsedMilliseconds > TimeSpan.MaxValue.TotalMilliseconds) return false;
        var interval = 1000d / MaximumFps;
        if (elapsedMilliseconds == _lastSeen) return false;
        if (double.IsNaN(_nextDue) || elapsedMilliseconds < _lastSeen)
        {
            _lastSeen = elapsedMilliseconds;
            _nextDue = elapsedMilliseconds + interval;
            return true;
        }
        _lastSeen = elapsedMilliseconds;
        if (elapsedMilliseconds + 0.000001 < _nextDue) return false;
        _nextDue += (Math.Floor(Math.Max(0, elapsedMilliseconds - _nextDue) / interval) + 1) * interval;
        return true;
    }
    public void Reset() { _nextDue = _lastSeen = double.NaN; }
}
