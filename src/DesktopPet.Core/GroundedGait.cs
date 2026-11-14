namespace DesktopPet.Core;

public readonly record struct GaitFootPose(double X, double Lift, bool IsPlanted);
public readonly record struct GroundedGaitPose(GaitFootPose FootA, GaitFootPose FootB, double BodyBob);

/// <summary>
/// A gait in unscaled sprite pixels. Body movement advances phase by distance / cycle distance.
/// During stance d(foot.X)/d(distance) = -1, so a planted foot stays fixed in world coordinates.
/// BodyBob is applied to the body only; the feet keep their own common ground baseline.
/// </summary>
public static class GroundedGait
{
    public const double StanceFraction = 0.6;

    public static GroundedGaitPose Sample(double phase, double cycleDistancePx, double footLiftPx = 4)
    {
        if (!double.IsFinite(cycleDistancePx) || cycleDistancePx <= 0)
            throw new ArgumentOutOfRangeException(nameof(cycleDistancePx));
        if (!double.IsFinite(footLiftPx) || footLiftPx < 0)
            throw new ArgumentOutOfRangeException(nameof(footLiftPx));
        var normalized = NormalizePhase(phase);
        return new GroundedGaitPose(
            SampleFoot(normalized, cycleDistancePx, footLiftPx),
            SampleFoot(NormalizePhase(normalized + 0.5), cycleDistancePx, footLiftPx),
            -footLiftPx * 0.18 * Math.Cos(4 * Math.PI * (normalized - StanceFraction / 2)));
    }

    /// <summary>Bring the raised foot down without moving either foot along the floor.</summary>
    public static GroundedGaitPose Settle(GroundedGaitPose pose, double progress)
    {
        var t = double.IsFinite(progress) ? Math.Clamp(progress, 0, 1) : 0;
        var smooth = t * t * (3 - 2 * t);
        GaitFootPose Lower(GaitFootPose foot) => new(foot.X, foot.Lift * (1 - smooth), foot.IsPlanted || t >= 1);
        return new GroundedGaitPose(Lower(pose.FootA), Lower(pose.FootB), pose.BodyBob * (1 - smooth));
    }

    public static double NormalizePhase(double phase)
    {
        if (!double.IsFinite(phase)) return 0;
        var normalized = phase - Math.Floor(phase);
        return normalized >= 1 ? 0 : normalized;
    }

    /// <summary>Reflect the gait when the sprite turns around, preserving both world foot positions.</summary>
    public static double ReversePhase(double phase) => NormalizePhase(StanceFraction - NormalizePhase(phase));

    private static GaitFootPose SampleFoot(double phase, double distance, double lift)
    {
        if (phase < StanceFraction)
            return new GaitFootPose((StanceFraction / 2 - phase) * distance, 0, true);

        var t = (phase - StanceFraction) / (1 - StanceFraction);
        var t2 = t * t;
        var t3 = t2 * t;
        var back = -StanceFraction / 2 * distance;
        var front = StanceFraction / 2 * distance;
        // Hermite tangents match the stance's -distance derivative at lift-off and landing.
        // The tiny overshoot at each end is intentional: there is no horizontal velocity snap.
        var tangent = -(1 - StanceFraction) * distance;
        var x = (2 * t3 - 3 * t2 + 1) * back + (t3 - 2 * t2 + t) * tangent +
            (-2 * t3 + 3 * t2) * front + (t3 - t2) * tangent;
        var sine = Math.Sin(Math.PI * t);
        return new GaitFootPose(x, lift * sine * sine, false);
    }
}
