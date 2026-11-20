namespace DesktopPet.Core;

public static class RoamingPauseTests
{
    public static IEnumerable<(string Name, Action Run)> Create()
    {
        yield return ("PauseFromIdleNeverWalks", () =>
        {
            var rng = new Random(42);
            var layout = new PlatformLayout(new[] { new Platform(0, 2000, 600) });
            var brain = new PetBrain(layout, 100, 90, 900, 510, rng) { SleepEnabled = false };
            brain.RoamingPaused = true;
            var cursor = new PetPoint(5000, 500);
            for (var i = 0; i < 7200; i++)
            {
                brain.Tick(1.0 / 60, cursor);
                Check(brain.State != PetState.Walk && brain.X == 900 && brain.Y == 510, "paused pet roamed");
            }
            Check(brain.State != PetState.Walk, "never walks while paused");
            Check(Math.Abs(brain.X - 900) < 1, "position unchanged after 120s paused");
        });

        yield return ("PauseWalkingDeceleratesAndSettles", () =>
        {
            var rng = new Random(42);
            var layout = new PlatformLayout(new[] { new Platform(0, 2000, 600) });
            var brain = new PetBrain(layout, 100, 90, 900, 510, rng) { SleepEnabled = false };
            var cursor = new PetPoint(5000, 500);
            var walked = false;
            for (var i = 0; i < 1800 && !walked; i++)
            {
                brain.Tick(1.0 / 60, cursor);
                if (brain.State == PetState.Walk && Math.Abs(brain.HorizontalVelocityPxPerSec) > 25) walked = true;
            }
            Check(walked, "entered walk within 30s");
            var pauseX = brain.X;
            brain.RoamingPaused = true;
            for (var i = 0; i < 60; i++) brain.Tick(1.0 / 60, cursor);
            var maxShift = Math.Abs(brain.X - pauseX);
            Check(maxShift <= 12, "moved <=12px while decelerating");
            Check(brain.State == PetState.Idle, "settled idle by 1s");
            Check(Math.Abs(brain.HorizontalVelocityPxPerSec) <= 1, "no sliding thereafter");
            var stoppedX = brain.X;
            for (var i = 0; i < 7200; i++)
            {
                brain.Tick(1.0 / 60, cursor);
                Check(brain.State != PetState.Walk && brain.X == stoppedX, "no drift after settling");
            }
        });

        yield return ("ResumePermitsFutureWalk", () =>
        {
            var rng = new Random(42);
            var layout = new PlatformLayout(new[] { new Platform(0, 2000, 600) });
            var brain = new PetBrain(layout, 100, 90, 900, 510, rng) { SleepEnabled = false };
            var cursor = new PetPoint(5000, 500);
            brain.RoamingPaused = true;
            for (var i = 0; i < 1800; i++) brain.Tick(1.0 / 60, cursor);
            brain.RoamingPaused = false;
            var walked = false;
            for (var i = 0; i < 1800 && !walked; i++)
            {
                brain.Tick(1.0 / 60, cursor);
                if (brain.State == PetState.Walk && Math.Abs(brain.HorizontalVelocityPxPerSec) > 25) walked = true;
            }
            Check(walked, "walked within 30s after resume");
        });

        yield return ("PausedDragThrowRetainsFlag", () =>
        {
            var rng = new Random(42);
            var layout = new PlatformLayout(new[] { new Platform(0, 2000, 600) });
            var brain = new PetBrain(layout, 100, 90, 900, 510, rng) { SleepEnabled = false };
            brain.RoamingPaused = true;
            brain.BeginDrag(new PetPoint(900, 510));
            brain.UpdateDrag(new PetPoint(1100, 200));
            brain.EndDrag(500, 0);
            Check(brain.State == PetState.Falling, "drag throw enters falling");
            Check(brain.RoamingPaused, "pause retained after drag/throw");
            for (var i = 0; i < 180; i++) brain.Tick(1.0 / 60, new PetPoint(5000, 500));
            Check(Math.Abs(brain.Y - 510) < 1, "landed on floor y=510");
            Check(brain.RoamingPaused, "pause retained after landing");
            brain.StartNagging(PetState.Angry);
            Check(brain.IsNagging, "StartNagging works while paused");
            Check(brain.RoamingPaused, "pause retained after StartNagging");
            brain.StopNagging();
            Check(!brain.IsNagging, "StopNagging works while paused");
            Check(brain.RoamingPaused, "pause retained after StopNagging");
        });

        yield return ("Pausing at either ledge stops without a new hop or an unnecessary turn", () =>
        {
            var directions = new HashSet<bool>();
            for (var seed = 0; seed < 10 && directions.Count < 2; seed++)
            {
                foreach (var paused in new[] { false, true })
                {
                    var layout = new PlatformLayout(new[] { new Platform(0, 2000, 600) });
                    layout.UpdateWindowPlatforms(new[] { new Platform(500, 1400, 300) });
                    var brain = new PetBrain(layout, 100, 90, 900, 210, new Random(seed)) { SleepEnabled = false };
                    var cursor = new PetPoint(5000, 500);
                    for (var i = 0; i < 1800 && (brain.State != PetState.Walk || Math.Abs(brain.HorizontalVelocityPxPerSec) <= 25); i++)
                        brain.Tick(1.0 / 60, cursor);
                    Check(brain.State == PetState.Walk && Math.Abs(brain.HorizontalVelocityPxPerSec) > 25, "ledge walk fixture");
                    var right = brain.FacingRight;
                    directions.Add(right);
                    layout.UpdateWindowPlatforms(new[] { new Platform(right ? 500 : brain.X - 0.05, right ? brain.X + 100.05 : 1400, 300) });
                    brain.RoamingPaused = paused;
                    brain.Tick(1.0 / 60, cursor);
                    if (!paused) Check(brain.State == PetState.Falling, "control must have an available hop route");
                    else Check(brain.State == PetState.Idle && brain.Y == 210 && brain.FacingRight == right, "pause launched a hop or flipped the pet at the edge");
                }
            }
            Check(directions.Count == 2, "both ledge directions must be exercised");
        });

        yield return ("PausedPlatformRemovalContinuesGravity", () =>
        {
            var layout = new PlatformLayout(new[] { new Platform(0, 2000, 600) });
            layout.UpdateWindowPlatforms(new[] { new Platform(500, 1400, 300) });
            var brain = new PetBrain(layout, 100, 90, 900, 210, new Random(42)) { SleepEnabled = false, RoamingPaused = true };
            brain.Tick(1.0 / 60, new PetPoint(5000, 500));
            Check(brain.Y == 210, "window support fixture");
            layout.UpdateWindowPlatforms(Array.Empty<Platform>());
            brain.Tick(1.0 / 60, new PetPoint(5000, 500));
            Check(brain.State == PetState.Falling, "removed support must begin fall");
            for (var i = 0; i < 120; i++) brain.Tick(1.0 / 60, new PetPoint(5000, 500));
            Check(brain.Y == 510 && brain.State != PetState.Falling && brain.RoamingPaused, "paused pet must land on desktop");
        });
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("RoamingPauseTests failed: " + name);
    }
}
