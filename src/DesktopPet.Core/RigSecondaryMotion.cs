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

/// <summary>Exponential view blending supports rapid retargets without swapping raster banks.</summary>
public sealed class RigViewBlend
{
    private bool _initialized;
    public double TurnWeight { get; private set; }
    public void Reset() { _initialized = false; TurnWeight = 0; }
    public double Update(WalkView view, double elapsedSeconds, bool reducedMotion = false)
    {
        var target = view == WalkView.Turn ? 1d : 0d;
        if (!_initialized || reducedMotion) { _initialized = true; return TurnWeight = target; }
        var dt = double.IsFinite(elapsedSeconds) ? Math.Clamp(elapsedSeconds, 0, 0.25) : 0;
        TurnWeight += (target - TurnWeight) * (1 - Math.Exp(-dt / 0.055));
        if (Math.Abs(TurnWeight - target) < 0.00001) TurnWeight = target;
        return TurnWeight;
    }
}
