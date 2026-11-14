namespace DesktopPet.Core;

/// <summary>Time-based cursor filtering: throwing should not depend on mouse report frequency.</summary>
public sealed class DragVelocityTracker
{
    private PetPoint _position;
    private double _sampleTime;
    private bool _initialized;
    public PetPoint Velocity { get; private set; }
    public PetPoint ReleaseVelocity => Velocity.X * Velocity.X + Velocity.Y * Velocity.Y >= 90 * 90 ? Velocity : default;

    public void Reset(PetPoint position, double monotonicSeconds)
    {
        if (!double.IsFinite(position.X) || !double.IsFinite(position.Y) || !double.IsFinite(monotonicSeconds))
        {
            _initialized = false;
            Velocity = default;
            return;
        }
        _position = position;
        _sampleTime = monotonicSeconds;
        _initialized = true;
        Velocity = default;
    }

    public void Sample(PetPoint position, double monotonicSeconds)
    {
        if (!double.IsFinite(position.X) || !double.IsFinite(position.Y) || !double.IsFinite(monotonicSeconds)) return;
        var dt = monotonicSeconds - _sampleTime;
        if (!_initialized || dt < 0) { Reset(position, monotonicSeconds); return; }
        if (dt < 0.004) return; // Accumulate tiny mouse reports into one stable velocity sample.
        if (dt >= 0.12 && position == _position)
        {
            Velocity = default;
        }
        else
        {
            var alpha = 1 - Math.Exp(-dt / 0.035);
            var vx = Math.Clamp((position.X - _position.X) / dt, -1700, 1700);
            var vy = Math.Clamp((position.Y - _position.Y) / dt, -1700, 1700);
            Velocity = new PetPoint(Velocity.X + (vx - Velocity.X) * alpha, Velocity.Y + (vy - Velocity.Y) * alpha);
        }
        _position = position;
        _sampleTime = monotonicSeconds;
    }
}
