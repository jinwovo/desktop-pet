namespace DesktopPet.Core;

public readonly record struct RigJointPose(double HeadAngle, double TorsoAngle, double HeadLagY, double NearFootRoll, double FarFootRoll);

/// <summary>Small rigid-part rotations; planted feet never rotate and settling removes secondary motion.</summary>
public static class RigSecondaryMotion
{
    public static RigJointPose Sample(double phase, double settleProgress, bool reducedMotion = false)
    {
        if (reducedMotion) return default;
        var p = GroundedGait.NormalizePhase(phase);
        var t = double.IsFinite(settleProgress) ? Math.Clamp(settleProgress, 0, 1) : 0;
        var strength = 1 - t * t * (3 - 2 * t);
        var sway = Math.Sin(p * 2 * Math.PI);
        return new(-0.45 * sway * strength, 0.3 * sway * strength,
            0.18 * Math.Sin(p * 4 * Math.PI) * strength, Roll(p) * strength,
            Roll(GroundedGait.NormalizePhase(p + 0.5)) * strength);
    }
    private static double Roll(double phase)
    {
        if (phase <= GroundedGait.StanceFraction) return 0;
        var t = (phase - GroundedGait.StanceFraction) / (1 - GroundedGait.StanceFraction);
        var lift = Math.Sin(Math.PI * t);
        return 8 * Math.Sin(2 * Math.PI * t) * lift * lift;
    }
}

/// <summary>Short, bounded transitions finish exactly instead of leaving an exponential tail.</summary>
public sealed class RigViewBlend
{
    private readonly BoundedPoseBlend _blend = new(0.1);
    public double TurnWeight => _blend.Value;
    public void Reset() => _blend.Reset();
    public double Update(WalkView view, double elapsedSeconds, bool reducedMotion = false)
        => _blend.Update(view == WalkView.Turn ? 1 : 0, elapsedSeconds, reducedMotion);
}

/// <summary>Only the upper-body artwork eases across a reversal; contact feet reverse immediately.</summary>
public sealed class RigFacingBlend
{
    private readonly BoundedPoseBlend _blend = new(0.12);
    public double RightWeight => _blend.Value;
    public void Reset() => _blend.Reset();
    public double Update(bool facingRight, double elapsedSeconds, bool reducedMotion = false)
        => _blend.Update(facingRight ? 1 : 0, elapsedSeconds, reducedMotion);
}

internal sealed class BoundedPoseBlend(double duration)
{
    private bool _initialized;
    private double _target, _from, _elapsed;
    public double Value { get; private set; }
    public void Reset() { _initialized = false; Value = 0; }
    public double Update(double target, double elapsedSeconds, bool immediate)
    {
        if (!_initialized || immediate)
        {
            _initialized = true; _target = _from = Value = target; _elapsed = duration; return Value;
        }
        if (_target != target) { _target = target; _from = Value; _elapsed = 0; }
        var dt = double.IsFinite(elapsedSeconds) ? Math.Clamp(elapsedSeconds, 0, 0.25) : 0;
        _elapsed = Math.Min(duration, _elapsed + dt);
        var t = _elapsed / duration;
        return Value = _from + (_target - _from) * t * t * (3 - 2 * t);
    }
}
