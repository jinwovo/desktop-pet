using DesktopPet.Core;

public static class WalkEntryTests
{
    public static IEnumerable<(string Name, Action Test)> Create()
    {
        yield return ("Entry weight eases from 0 to 1 exactly within 90 ms at 30, 60, 120, and 144 Hz", () =>
        {
            foreach (var hz in new[] { 30, 60, 120, 144 })
            {
                var controller = new WalkAppearanceController();
                var dt = 1.0 / hz;
                var frames = (int)Math.Ceiling(0.09 / dt);
                var lastDt = 0.09 - (frames - 1) * dt;

                controller.Update(PetState.Idle, true, 0, 100, 0);
                controller.Update(PetState.Walk, true, 100, 100, 0, distanceDeltaPx: 1, walkCycleDistancePx: 100);

                for (var i = 0; i < frames - 1; i++)
                    controller.Update(PetState.Walk, true, 100, 100, dt, distanceDeltaPx: 1, walkCycleDistancePx: 100);

                controller.Update(PetState.Walk, true, 100, 100, lastDt, distanceDeltaPx: 1, walkCycleDistancePx: 100);

                Check(controller.EntryWeight == 1, $"EntryWeight did not reach exactly 1 at {hz} Hz (was {controller.EntryWeight})");
                Check(controller.AnimationElapsedMs > 0, $"Animation clock did not advance at {hz} Hz");
            }
        });

        yield return ("Phase advances only by supplied distance and ignores zero, NaN, and negative elapsed", () =>
        {
            var controller = new WalkAppearanceController();
            controller.Update(PetState.Idle, true, 0, 100, 0);
            controller.Update(PetState.Walk, true, 100, 100, 0, distanceDeltaPx: 10, walkCycleDistancePx: 100);
            var phaseAfterDistance = controller.GaitPhase;

            controller.Update(PetState.Walk, true, 100, 100, 0, distanceDeltaPx: 0, walkCycleDistancePx: 100);
            Check(controller.GaitPhase == phaseAfterDistance, "Zero distance advanced gait phase");

            controller.Update(PetState.Walk, true, 100, 100, double.NaN, distanceDeltaPx: 0, walkCycleDistancePx: 100);
            Check(controller.GaitPhase == phaseAfterDistance, "NaN elapsed advanced gait phase");

            controller.Update(PetState.Walk, true, 100, 100, -0.5, distanceDeltaPx: 0, walkCycleDistancePx: 100);
            Check(controller.GaitPhase == phaseAfterDistance && controller.EntryWeight == 0, "Invalid elapsed advanced entry or phase");

            controller.Update(PetState.Walk, true, 100, 100, 0.016, distanceDeltaPx: 5, walkCycleDistancePx: 100);
            Check(controller.GaitPhase != phaseAfterDistance, "Supplied distance did not advance gait phase");
        });

        yield return ("Stopping mid-entry then resuming at zero elapsed retains effective vertical strength", () =>
        {
            var controller = new WalkAppearanceController();
            controller.Update(PetState.Idle, true, 0, 100, 0);
            controller.Update(PetState.Walk, true, 100, 100, 0, distanceDeltaPx: 1, walkCycleDistancePx: 100);
            controller.Update(PetState.Walk, true, 100, 100, 0.03, distanceDeltaPx: 1, walkCycleDistancePx: 100);
            var weightAtStop = controller.EntryWeight;

            controller.Update(PetState.Idle, true, 0, 100, 0, distanceDeltaPx: 0, walkCycleDistancePx: 100);
            controller.Update(PetState.Idle, true, 0, 100, 0.03, distanceDeltaPx: 0, walkCycleDistancePx: 100);
            Check(controller.IsSettling, "Controller did not enter settling state after stopping");
            Check(controller.SettleProgress < 1, "Settle progress did not start below 1");

            var settleProgress = controller.SettleProgress;
            var expectedEffective = weightAtStop * (1 - settleProgress * settleProgress * (3 - 2 * settleProgress));
            Check(Math.Abs(controller.EntryWeight - weightAtStop) < 1e-9,
                $"EntryWeight {controller.EntryWeight} did not match effective strength {expectedEffective}");

            controller.Update(PetState.Walk, true, 100, 100, 0, distanceDeltaPx: 0, walkCycleDistancePx: 100);
            Check(controller.EntryWeight == expectedEffective,
                $"Resume at zero elapsed reset EntryWeight to {controller.EntryWeight} instead of retaining {expectedEffective}");
        });

        yield return ("Legacy Update without distance keeps EntryWeight at 1 and leaves animation clock untouched", () =>
        {
            var controller = new WalkAppearanceController();
            controller.Update(PetState.Idle, true, 0, 100, 0);
            controller.Update(PetState.Walk, true, 100, 100, 0.05);
            Check(controller.EntryWeight == 1, $"Legacy EntryWeight was {controller.EntryWeight}, not 1");
            Check(Math.Abs(controller.AnimationElapsedMs - 50) < 1e-9, "Entry easing consumed legacy animation time");
        });

        yield return ("Resting direction changes reflect the retained planted-foot phase", () =>
        {
            var controller = new WalkAppearanceController();
            controller.Update(PetState.Walk, true, 100, 100, 0.09, distanceDeltaPx: 17, walkCycleDistancePx: 100);
            controller.Update(PetState.Idle, true, 0, 100, 0, distanceDeltaPx: 0, walkCycleDistancePx: 100);
            controller.Update(PetState.Idle, true, 0, 100, 0.2, distanceDeltaPx: 0, walkCycleDistancePx: 100);
            controller.Update(PetState.Idle, false, 0, 100, 0.02, distanceDeltaPx: 0, walkCycleDistancePx: 100);
            controller.Update(PetState.Walk, false, 0, 100, 0, distanceDeltaPx: 0, walkCycleDistancePx: 100);
            Check(!controller.FacingRight && Math.Abs(controller.GaitPhase - GroundedGait.ReversePhase(0.17)) < 1e-9,
                "The stored phase kept the old orientation after the rest-facing direction changed");
        });

        yield return ("Airborne interruption clears the previous grounded gait contact", () =>
        {
            var controller = new WalkAppearanceController();
            controller.Update(PetState.Walk, true, 100, 100, 0.09, distanceDeltaPx: 31, walkCycleDistancePx: 100);
            controller.Update(PetState.Falling, true, 0, 100, 0.03, distanceDeltaPx: 9, walkCycleDistancePx: 100);
            controller.Update(PetState.Idle, true, 0, 100, 0.03, distanceDeltaPx: 0, walkCycleDistancePx: 100);
            controller.Update(PetState.Walk, true, 100, 100, 0.03, distanceDeltaPx: 4, walkCycleDistancePx: 100);
            Check(Math.Abs(controller.GaitPhase - 0.04) < 1e-9, "The first stride reused a contact from before the airborne state");
        });

        yield return ("Zero travel does not flip a distance-driven walk before the first reverse step", () =>
        {
            var controller = new WalkAppearanceController();
            controller.Update(PetState.Walk, true, 100, 100, 0.2, distanceDeltaPx: 17, walkCycleDistancePx: 100);
            var phase = controller.GaitPhase;
            controller.Update(PetState.Walk, false, -100, 100, 0.01, distanceDeltaPx: 0, walkCycleDistancePx: 100);
            Check(controller.FacingRight && controller.GaitPhase == phase, "A zero-travel frame flipped the planted pose");
            controller.Update(PetState.Walk, false, -100, 100, 0.01, distanceDeltaPx: -2, walkCycleDistancePx: 100);
            Check(!controller.FacingRight && controller.View == WalkView.Turn, "The first reversing step did not change facing");
        });

        yield return ("Reset returns EntryWeight to 1", () =>
        {
            var controller = new WalkAppearanceController();
            controller.Update(PetState.Idle, true, 0, 100, 0);
            controller.Update(PetState.Walk, true, 100, 100, 0.03, distanceDeltaPx: 1, walkCycleDistancePx: 100);
            Check(controller.EntryWeight > 0 && controller.EntryWeight < 1, "Fixture did not enter partial easing before reset");

            controller.Reset();
            Check(controller.EntryWeight == 1, $"Reset EntryWeight was {controller.EntryWeight}, not 1");
            Check(controller.AnimationElapsedMs == 0, "Reset did not clear animation clock");
            Check(controller.GaitPhase == 0, "Reset did not clear gait phase");
        });
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
