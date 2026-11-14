using DesktopPet.Core;

public static class MotionRegressionTests
{
    private static readonly PetPoint Away = new(-10_000, -10_000);

    public static IEnumerable<(string Name, Action Run)> Create()
    {
        yield return ("Analytic throw follows the same trajectory at 30, 60 and 144 Hz", () =>
        {
            var positions = new List<(double X, double Y)>();
            foreach (var refreshRate in new[] { 30, 60, 144 })
            {
                var brain = new PetBrain(new PlatformLayout(new[] { new Platform(0, 5000, 2000) }), 100, 90, 500, 400);
                brain.BeginDrag(new PetPoint(500, 400));
                brain.EndDrag(350, -500);
                Advance(brain, 0.6, 1.0 / refreshRate);
                var factor = (Math.Pow(0.62, 0.6) - 1) / Math.Log(0.62);
                Near(brain.X, 500 + 350 * factor, 1e-7, "Horizontal drag integration depends on frame rate");
                Near(brain.Y, 400 - 500 * 0.6 + 700 * 0.6 * 0.6, 1e-7, "Gravity integration depends on frame rate");
                Check(brain.State == PetState.Falling, "Flight unexpectedly ended");
                positions.Add((brain.X, brain.Y));
            }
            Check(positions.Max(p => p.X) - positions.Min(p => p.X) < 1e-7, "Throw endpoints diverged");
        });

        yield return ("Invalid elapsed time cannot reverse or poison simulation", () =>
        {
            var brain = MakeBrain(new PlatformLayout(new[] { new Platform(0, 1000, 600) }));
            foreach (var elapsed in new[] { -1, 0, double.NaN, double.PositiveInfinity }) brain.Tick(elapsed, Away);
            Near(brain.X, 199, 0, "Invalid elapsed time moved X");
            Near(brain.Y, 410, 0, "Invalid elapsed time moved Y");
            Near(brain.StateElapsedMs, 0, 0, "Invalid elapsed time changed state clock");
        });

        yield return ("A planned down-hop clears its source and lands on the selected window", () =>
        {
            foreach (var refreshRate in new[] { 30, 60, 144 })
            {
                var layout = new PlatformLayout(new[] { new Platform(0, 2000, 2000) });
                layout.UpdateWindowPlatforms(new[] { new Platform(100, 300, 500), new Platform(310, 650, 800) });
                var brain = MakeBrain(layout);
                var landing = ObserveFirstHop(brain, 1.0 / refreshRate);
                Near(landing.GroundY, 800, 1e-7, "Down-hop landed back on its source");
                Check(landing.CenterX >= 360 && landing.CenterX <= 600, "Down-hop missed a supported landing position");
            }
        });

        yield return ("A reachable climb arrives on the upper ledge at varying refresh rates", () =>
        {
            foreach (var refreshRate in new[] { 30, 60, 144 })
            {
                var layout = new PlatformLayout(new[] { new Platform(0, 2000, 2000) });
                layout.UpdateWindowPlatforms(new[] { new Platform(100, 300, 500), new Platform(270, 500, 450) });
                var landing = ObserveFirstHop(MakeBrain(layout), 1.0 / refreshRate);
                Near(landing.GroundY, 450, 1e-7, "Climb missed the selected upper ledge");
                Check(landing.CenterX >= 319 && landing.CenterX <= 450, "Climb did not reach the ledge interior");
            }
        });

        yield return ("An attractive but unreachable jump causes a turn instead of a failed launch", () =>
        {
            var layout = new PlatformLayout(new[] { new Platform(0, 2000, 2000) });
            layout.UpdateWindowPlatforms(new[] { new Platform(100, 300, 500), new Platform(400, 700, 450) });
            var brain = MakeBrain(layout);
            for (var i = 0; i < 360; i++)
            {
                brain.Tick(1.0 / 120, Away);
                Check(brain.State != PetState.Falling, "Launched toward an unreachable shelf");
            }
            Check(brain.X < 199, "Pet failed to turn away from unreachable shelf");
            Near(brain.Y + 90, 500, 1e-7, "Pet lost its supporting window");
        });

        yield return ("An unreachable easy ledge does not hide a reachable second candidate", () =>
        {
            var layout = new PlatformLayout(new[] { new Platform(0, 2000, 2000) });
            layout.UpdateWindowPlatforms(new[]
            {
                new Platform(100, 300, 500), new Platform(400, 700, 480), new Platform(270, 500, 450),
            });
            var landing = ObserveFirstHop(MakeBrain(layout), 1.0 / 120);
            Near(landing.GroundY, 450, 1e-7, "Planner stopped after its first unreachable candidate");
        });

        yield return ("A wide desktop under the source is a valid down-hop destination", () =>
        {
            var layout = new PlatformLayout(new[] { new Platform(0, 2000, 800) });
            layout.UpdateWindowPlatforms(new[] { new Platform(100, 300, 500) });
            var landing = ObserveFirstHop(MakeBrain(layout), 1.0 / 120);
            Near(landing.GroundY, 800, 1e-7, "Wide desktop was excluded because it extends under the source");
        });

        yield return ("Walking eases into motion and slows before returning to rest", () =>
        {
            var brain = new PetBrain(new PlatformLayout(new[] { new Platform(0, 10_000, 600) }), 100, 90, 500, 510,
                new SequenceRandom(0, 0.2, 0.9, 0, 0.8, 0.9));
            for (var i = 0; i < 200 && brain.State != PetState.Walk; i++) brain.Tick(0.005, Away);
            Check(brain.State == PetState.Walk, "Pet did not start walking");
            var before = brain.X;
            brain.Tick(0.02, Away);
            Check(brain.HorizontalVelocityPxPerSec is > 0 and < 30, "Walking starts at full speed");
            Check(brain.X - before < 0.5, "Walking position jumps on departure");
            Advance(brain, 0.4, 0.01);
            Check(brain.HorizontalVelocityPxPerSec > 58, "Walking never reaches its configured speed");
            var sawSlowdown = false;
            var lastVelocity = brain.HorizontalVelocityPxPerSec;
            for (var i = 0; i < 400 && brain.State == PetState.Walk; i++)
            {
                var previous = brain.HorizontalVelocityPxPerSec;
                brain.Tick(0.01, Away);
                if (brain.State == PetState.Walk && brain.HorizontalVelocityPxPerSec < previous - 0.2) sawSlowdown = true;
                if (brain.State != PetState.Walk) lastVelocity = previous;
            }
            Check(sawSlowdown && brain.State == PetState.Idle, "Walking did not ease out before idling");
            Check(lastVelocity < 1.3, "Pet stopped instantly from a visible speed");
        });

        yield return ("Dragging retains the direction the pet had before being picked up", () =>
        {
            var brain = new PetBrain(new PlatformLayout(new[] { new Platform(0, 10_000, 600) }), 100, 90, 500, 510,
                new SequenceRandom(0, 0.2, 0.1, 0.5));
            for (var i = 0; i < 200 && brain.State != PetState.Walk; i++) brain.Tick(0.005, Away);
            Advance(brain, 0.1, 0.01);
            Check(!brain.FacingRight, "Test pet did not walk left");
            brain.BeginDrag(new PetPoint(brain.X, brain.Y));
            brain.UpdateDrag(new PetPoint(600, 200));
            Check(!brain.FacingRight, "Picking up the pet flipped its artwork");
        });
    }

    private static PetBrain MakeBrain(PlatformLayout layout) =>
        new(layout, 100, 90, 199, 410, new ConstantRandom(0.6));

    private static (double CenterX, double GroundY) ObserveFirstHop(PetBrain brain, double step)
    {
        var sawAirborne = false;
        for (var elapsed = 0.0; elapsed < 6; elapsed += step)
        {
            brain.Tick(step, Away);
            if (brain.State == PetState.Falling) sawAirborne = true;
            else if (sawAirborne) return (brain.X + 50, brain.Y + 90);
        }
        throw new Exception("No completed hop observed through normal state decisions");
    }

    private static void Advance(PetBrain brain, double duration, double step)
    {
        var elapsed = 0.0;
        while (elapsed < duration - 1e-12)
        {
            var remaining = Math.Min(step, duration - elapsed);
            brain.Tick(remaining, Away);
            elapsed += remaining;
        }
    }

    private static void Near(double actual, double expected, double tolerance, string message) =>
        Check(Math.Abs(actual - expected) <= tolerance, $"{message}: expected {expected}, actual {actual}");
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private sealed class ConstantRandom(double value) : Random { public override double NextDouble() => value; }
    private sealed class SequenceRandom(params double[] values) : Random
    {
        private int _index;
        public override double NextDouble() => values[Math.Min(_index++, values.Length - 1)];
    }
}
