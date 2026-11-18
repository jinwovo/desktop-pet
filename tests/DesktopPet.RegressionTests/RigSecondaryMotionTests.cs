using DesktopPet.Core;

public static class RigSecondaryMotionTests
{
    public static IEnumerable<(string Name, Action Run)> Create()
    {
        yield return ("Secondary joints never rotate planted feet and converge to rest without stretching", () =>
        {
            for (var i = 0; i < 10000; i++)
            {
                var phase = i / 10000d;
                var gait = GroundedGait.Sample(phase, 30);
                var pose = RigSecondaryMotion.Sample(phase, 0);
                Check(!gait.FootA.IsPlanted || pose.NearFootRoll == 0, "Planted near foot rotated");
                Check(!gait.FootB.IsPlanted || pose.FarFootRoll == 0, "Planted far foot rotated");
                Check(Math.Abs(pose.HeadAngle) <= 0.45 && Math.Abs(pose.TorsoAngle) <= 0.3 && Math.Abs(pose.NearFootRoll) <= 8, "Secondary movement exceeded its authored bound");
                Check(RigSecondaryMotion.Sample(phase, 1) == default && RigSecondaryMotion.Sample(phase, 0, true) == default, "Settling or reduced motion retained secondary articulation");
            }
        });
        yield return ("View blending is continuous under rapid retargets and consistent across refresh rates", () =>
        {
            double? reference = null;
            foreach (var hz in new[] { 30, 60, 120, 144 })
            {
                var blend = new RigViewBlend();
                Check(blend.Update(WalkView.Side, 0) == 0, "Initial view was blank");
                for (var i = 0; i < hz; i++) blend.Update(WalkView.Turn, 0.5 / hz);
                reference ??= blend.TurnWeight;
                Check(Math.Abs(blend.TurnWeight - reference.Value) < 1e-10, "Blend speed depends on refresh rate");
                var before = blend.TurnWeight;
                Check(blend.Update(WalkView.Side, 0) == before, "Retarget jumped without elapsed time");
                Check(blend.Update(WalkView.Side, 1d / 60) < before && blend.TurnWeight > 0, "Rapid retarget swapped views in one frame");
                before = blend.TurnWeight;
                Check(blend.Update(WalkView.Turn, double.NaN) == before, "Invalid delta changed blending");
                Check(blend.Update(WalkView.Side, 0, true) == 0, "Reduced motion still blended");
            }
        });
        yield return ("Settling presents its exact final blend before the rest-frame handoff at every refresh rate", () =>
        {
            foreach (var hz in new[] { 4, 30, 60, 120, 144 })
            {
                var controller = new WalkAppearanceController();
                controller.Update(PetState.Walk, true, 30, 30, 1d / hz, distanceDeltaPx: 1, walkCycleDistancePx: 30);
                controller.Update(PetState.Idle, true, 0, 30, 1d / hz, distanceDeltaPx: 0, walkCycleDistancePx: 30);
                var sawFinal = false;
                for (var i = 0; i < hz + 3 && controller.IsSettling; i++)
                {
                    controller.Update(PetState.Idle, true, 0, 30, 1d / hz, distanceDeltaPx: 0, walkCycleDistancePx: 30);
                    if (controller.IsSettling && controller.SettleProgress == 1) sawFinal = true;
                }
                Check(sawFinal && !controller.DisplayWalkPose, "Refresh interval skipped the final resting artwork or never released the live rig");
            }
        });
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
