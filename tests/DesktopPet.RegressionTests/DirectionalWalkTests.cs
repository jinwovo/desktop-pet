using System.Text.Json.Nodes;
using DesktopPet.Core;

public static class DirectionalWalkTests
{
    public static IEnumerable<(string Name, Action Run)> Create()
    {
        yield return ("Frontal resting artwork is explicit and preserves legacy orientation by default", () => InFolder(folder =>
        {
            var manifest = Manifest();
            Write(folder, manifest);
            var legacy = CharacterLoader.Load(folder);
            Check(!legacy.FrontFacingRest && !legacy.UsesFrontalRestFrames(PetState.Idle), "Legacy resting orientation changed");
            manifest["frontFacingRest"] = true;
            Write(folder, manifest);
            var character = CharacterLoader.Load(folder);
            foreach (var state in new[] { PetState.Idle, PetState.Sit, PetState.Cry, PetState.Angry, PetState.Sleep })
                Check(character.UsesFrontalRestFrames(state), "Frontal resting pose still follows walking direction");
            foreach (var state in new[] { PetState.Walk, PetState.LookAtCursor, PetState.Dragged, PetState.Falling })
                Check(!character.UsesFrontalRestFrames(state), "Frontal resting option overrides a directional state");
            manifest["frontFacingRest"] = "invalid";
            Write(folder, manifest);
            ExpectInvalid(() => CharacterLoader.Load(folder));
        }));

        yield return ("Directional walk artwork is resolved and included in the complete decode set", () => InFolder(folder =>
        {
            Directory.CreateDirectory(Path.Combine(folder, "poses"));
            var manifest = Manifest();
            manifest["frames"]!["walk"] = new JsonArray("side.png", "side2.png", "side.png");
            manifest["frames"]!["walkFront"] = new JsonArray("poses/front.png", "poses/front2.png", "poses/front.png");
            manifest["frames"]!["walkTurn"] = new JsonArray("poses/turn.png", "poses/turn2.png", "poses/turn.png");
            foreach (var file in new[] { "poses/front.png", "poses/front2.png", "poses/turn.png", "poses/turn2.png" })
                File.WriteAllText(Path.Combine(folder, file), "loader path validation only");
            Write(folder, manifest);
            var character = CharacterLoader.Load(folder);
            var all = character.AllFramePaths().ToArray();
            Check(all.Length == 7, "Directional artwork was omitted from preload or repeated image paths were decoded twice");
            foreach (var file in new[] { "poses/front.png", "poses/front2.png", "poses/turn.png", "poses/turn2.png" })
                Check(all.Contains(Path.GetFullPath(Path.Combine(folder, file)), StringComparer.OrdinalIgnoreCase), "Directional preload path was not resolved");
            Check(character.GetFrames(PetState.Walk).Select(Path.GetFileName).SequenceEqual(new[] { "side.png", "side2.png", "side.png" }),
                "Optional viewpoints changed the canonical side gait");
            Check(character.HasDirectionalWalk && character.GetWalkFrames(WalkView.Front).Count == 3 &&
                character.GetWalkFrames(WalkView.Turn).Count == 3, "Loaded viewpoints were not exposed to the renderer");
            Check(character.GetWalkFrames(WalkView.Side).SequenceEqual(character.GetFrames(PetState.Walk)),
                "Side viewpoint did not use the original walk frame list");
        }));

        yield return ("Legacy characters retain the exact walking frames when alternate viewpoints are absent", () => InFolder(folder =>
        {
            foreach (var optional in new[] { "omitted", "null", "missing" })
            {
                var manifest = Manifest();
                if (optional == "null")
                {
                    manifest["frames"]!["walkFront"] = null;
                    manifest["frames"]!["walkTurn"] = null;
                }
                else if (optional == "missing")
                {
                    manifest["frames"]!["walkFront"] = new JsonArray("missing-front.png");
                    manifest["frames"]!["walkTurn"] = new JsonArray("missing-turn.png");
                }
                Write(folder, manifest);
                var character = CharacterLoader.Load(folder);
                var walk = character.GetFrames(PetState.Walk);
                Check(walk.Select(Path.GetFileName).SequenceEqual(new[] { "side.png", "side2.png" }), "Legacy walk frames changed");
                Check(character.AllFramePaths().Count() == 3, "Missing optional views changed the decoded image set");
                Check(Path.GetFileName(character.GetFramePath(PetState.Walk, walk, 100)) == "side2.png", "Legacy walking cadence changed");
                Check(!character.HasDirectionalWalk, "Missing alternate artwork enabled directional appearance");
                foreach (var view in Enum.GetValues<WalkView>())
                    Check(character.GetWalkFrames(view).SequenceEqual(walk), "Legacy alternate view did not fall back to its normal walk");
            }
        }));

        yield return ("Alternate walking viewpoints reject traversal, rooted and alternate-stream paths", () => InFolder(folder =>
        {
            foreach (var key in new[] { "walkFront", "walkTurn" })
            {
                foreach (var path in new[] { "../outside.png", "..\\outside.png", Path.Combine(folder, "side.png"), "C:side.png", "side.png:hidden", "", " " })
                {
                    var manifest = Manifest();
                    manifest["frames"]![key] = new JsonArray(path);
                    Write(folder, manifest);
                    ExpectInvalid(() => CharacterLoader.Load(folder));
                }
                var withNull = Manifest();
                withNull["frames"]![key] = new JsonArray((JsonNode?)null);
                Write(folder, withNull);
                ExpectInvalid(() => CharacterLoader.Load(folder));
            }
        }));

        yield return ("Directional variants reject partial or mismatched gait cycles before rendering", () => InFolder(folder =>
        {
            File.WriteAllText(Path.Combine(folder, "front.png"), "loader path validation only");
            foreach (var key in new[] { "walkFront", "walkTurn" })
            {
                foreach (var count in new[] { 1, 3 })
                {
                    var manifest = Manifest();
                    manifest["frames"]![key] = new JsonArray(Enumerable.Repeat("front.png", count).Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());
                    Write(folder, manifest);
                    ExpectInvalid(() => CharacterLoader.Load(folder));
                }
                var partial = Manifest();
                partial["frames"]![key] = new JsonArray("front.png", "missing.png");
                Write(folder, partial);
                ExpectInvalid(() => CharacterLoader.Load(folder));
            }
        }));

        yield return ("Partial walk viewpoints never substitute frontal artwork for a moving turn", () => InFolder(folder =>
        {
            File.WriteAllText(Path.Combine(folder, "alternate.png"), "loader path validation only");
            foreach (var supplied in new[] { "walkFront", "walkTurn" })
            {
                var manifest = Manifest();
                manifest["frames"]![supplied] = new JsonArray("alternate.png", "alternate.png");
                Write(folder, manifest);
                var character = CharacterLoader.Load(folder);
                Check(character.GetWalkFrames(WalkView.Front).All(file => Path.GetFileName(file) == "alternate.png"),
                    "A stationary/manual front view omitted available alternate artwork");
                Check(character.UsesFrontalWalkFrames(WalkView.Front) == (supplied == "walkFront"),
                    "The renderer cannot distinguish actual front artwork from a directional fallback");
                var turn = character.GetWalkFrames(WalkView.Turn);
                Check(supplied == "walkTurn" ? turn.All(file => Path.GetFileName(file) == "alternate.png") :
                    turn.SequenceEqual(character.GetFrames(PetState.Walk)),
                    "A missing moving turn was replaced with frontal artwork");
                Check(!character.UsesFrontalWalkFrames(WalkView.Turn), "A moving turn was marked as frontal");
                Check(!character.UsesFrontalWalkFrames(WalkView.Side), "A side gait was marked as unmirrored frontal artwork");
            }
        }));

        yield return ("Lateral walking stays in side or quarter views at every speed and refresh rate", () =>
        {
            foreach (var right in new[] { true, false })
            foreach (var multiplier in new[] { 0.25, 1, 3 })
            foreach (var refreshRate in new[] { 30, 60, 144 })
            {
                var controller = Controller();
                var sawSide = false;
                var sawQuarterAfterCruise = false;
                var target = 45 * multiplier;
                var velocity = (right ? 1 : -1) * target;
                var elapsed = 0d;
                while (elapsed < 16 - 1e-12)
                {
                    var dt = Math.Min(1d / refreshRate, 16 - elapsed);
                    controller.Update(PetState.Walk, right, velocity, 45, dt, target);
                    elapsed += dt;
                    Check(controller.View != WalkView.Front, "Pet faced fully forward while still translating sideways");
                    Check(controller.FacingRight == right, "Walking view faces against its actual direction");
                    if (controller.View == WalkView.Side) sawSide = true;
                    if (elapsed > 1 && sawSide && controller.View == WalkView.Turn) sawQuarterAfterCruise = true;
                }
                Check(sawSide && sawQuarterAfterCruise, "Walking never varied between its side and quarter views");
                Near(controller.AnimationElapsedMs, 16000 * multiplier, 1e-5, "Quarter glances reset or froze the gait");
            }
        });

        yield return ("Weighted gait timings select the same footfall phase across side, front and turn artwork", () => InFolder(folder =>
        {
            var manifest = Manifest();
            manifest["frames"]!["walkFront"] = new JsonArray("front.png", "front2.png");
            manifest["frames"]!["walkTurn"] = new JsonArray("turn.png", "turn2.png");
            manifest["frameDurationsMs"] = new JsonObject { ["walk"] = new JsonArray(80, 170) };
            foreach (var file in new[] { "front.png", "front2.png", "turn.png", "turn2.png" })
                File.WriteAllText(Path.Combine(folder, file), "loader path validation only");
            Write(folder, manifest);
            var character = CharacterLoader.Load(folder);
            foreach (var (elapsed, index) in new (double, int)[] { (0, 0), (79.99, 0), (80, 1), (249.99, 1), (250, 0), (580, 1) })
            {
                foreach (var view in Enum.GetValues<WalkView>())
                {
                    var frames = character.GetWalkFrames(view);
                    Check(character.GetFramePath(PetState.Walk, frames, elapsed) == frames[index],
                        "Changing the viewpoint changed the weighted walking footfall");
                }
            }
        }));

        yield return ("Moving reversals turn toward the actual velocity without a frontal or backward slide", () =>
        {
            var controller = Controller();
            Advance(controller, 0.4, 1d / 120, true, 45);
            Check(controller.View == WalkView.Side && controller.FacingRight, "Fixture never settled into right profile");
            var phaseBefore = controller.AnimationElapsedMs;
            var newTurn = false;
            var newSide = false;
            for (var elapsed = 0d; elapsed < 0.7; elapsed += 0.005)
            {
                controller.Update(PetState.Walk, false, -45, 45, 0.005);
                Check(controller.View != WalkView.Front, "Reversal faced front while moving laterally");
                Check(!controller.FacingRight, "Reversal kept facing right while actually moving left");
                if (controller.View == WalkView.Turn) newTurn = true;
                if (controller.View == WalkView.Side) newSide = true;
            }
            Check(newTurn && newSide, "Reversal omitted its quarter turn or never resumed the side gait");
            Check(controller.AnimationElapsedMs > phaseBefore + 650, "Direction reversal restarted the gait phase");
        });

        yield return ("Walking artwork follows actual travel even while the target direction changes", () =>
        {
            var controller = Controller();
            Advance(controller, 0.4, 0.01, true, 45);
            controller.Update(PetState.Walk, false, 45, 45, 0.01);
            Check(controller.FacingRight && controller.View != WalkView.Front,
                "A new left target turned the artwork while momentum was still carrying it right");
            controller.Update(PetState.Walk, true, -45, 45, 0.01);
            Check(!controller.FacingRight && controller.View == WalkView.Turn,
                "A reversed velocity did not turn the artwork toward actual travel");
        });

        yield return ("View transitions are independent of walking speed and gait phase is consistent across refresh rates", () =>
        {
            foreach (var multiplier in new[] { 0.25, 1, 3 })
            {
                WalkView? expectedView = null;
                foreach (var refreshRate in new[] { 30, 60, 144 })
                {
                    var controller = Controller();
                    Advance(controller, 4.8, 1d / refreshRate, true, 45 * multiplier);
                    Near(controller.AnimationElapsedMs, 4800 * multiplier, 1e-6, "Gait phase depends on refresh rate or viewpoint switches");
                    expectedView ??= controller.View;
                    Check(controller.View == expectedView && controller.View != WalkView.Front,
                        "Gaze schedule depends on walk speed or render frequency");
                }
            }
        });

        yield return ("A frontal resting pose appears only after walking stops, including slow user settings", () =>
        {
            foreach (var target in new[] { 11.25, 45, 135 })
            {
                var controller = Controller();
                for (var i = 0; i < 40; i++) controller.Update(PetState.Walk, true, target, 45, 0.01, target);
                Check(controller.View == WalkView.Side, "A stable slow user setting was mistaken for a walk stopping");
                var phase = controller.AnimationElapsedMs;
                controller.Update(PetState.Walk, true, target * 0.5, 45, 0.02, target);
                Check(controller.View == WalkView.Turn, "Slowing down did not turn toward the resting pose");
                controller.Update(PetState.Walk, true, target * 0.1, 45, 0.02, target);
                Check(controller.View != WalkView.Front && controller.AnimationElapsedMs > phase,
                    "Deceleration showed a frontal pose before lateral movement stopped");
                var phaseAtStop = controller.AnimationElapsedMs;
                for (var i = 0; i < 20; i++) controller.Update(PetState.Walk, true, 0, 45, 0.01, target);
                Near(controller.AnimationElapsedMs, phaseAtStop, 1e-9, "Stationary feet kept walking");
                Check(controller.View != WalkView.Front, "A walk state with paused velocity unexpectedly used a frontal walking pose");
                controller.Update(PetState.Idle, true, 0, 45, 0.02, target);
                Check(controller.View == WalkView.Front && controller.AnimationElapsedMs == 0,
                    "Actual stopped state did not return to a frontal resting pose");
                controller.Update(PetState.Walk, true, target, 45, 0.02, target);
                Check(controller.View == WalkView.Turn, "Resuming movement skipped its departure turn");
            }
        });

        yield return ("Real brain walking and stopping never pair a frontal pose with continuing sideways travel", () =>
        {
            foreach (var right in new[] { true, false })
            foreach (var refreshRate in new[] { 30, 60, 144 })
            {
                var brain = new PetBrain(new PlatformLayout(new[] { new Platform(0, 10000, 600) }),
                    100, 90, 5000, 510, new SequenceRandom(0, 0.2, right ? 0.9 : 0.1, 0, 0.8, 0.9))
                {
                    WalkSpeedPxPerSec = 45,
                    SleepEnabled = false,
                };
                var controller = Controller();
                var sawWalk = false;
                var sawRest = false;
                var restX = 0d;
                var restingSeconds = 0d;
                for (var i = 0; i < refreshRate * 8 && restingSeconds < 0.5; i++)
                {
                    var dt = 1d / refreshRate;
                    brain.Tick(dt, new PetPoint(-10000, -10000));
                    controller.Update(brain.State, brain.FacingRight, brain.HorizontalVelocityPxPerSec,
                        45, dt, brain.WalkSpeedPxPerSec);
                    if (brain.State == PetState.Walk)
                    {
                        sawWalk = true;
                        Check(controller.View != WalkView.Front, "Real behavior displayed a frontal pose during lateral walking");
                        if (Math.Abs(brain.HorizontalVelocityPxPerSec) > 0.5)
                            Check(controller.FacingRight == (brain.HorizontalVelocityPxPerSec > 0), "Real walking slid against its displayed direction");
                    }
                    else if (sawWalk && brain.State == PetState.Idle)
                    {
                        Check(controller.View == WalkView.Front && brain.HorizontalVelocityPxPerSec == 0,
                            "Frontal resting pose appeared before movement stopped");
                        if (!sawRest) { sawRest = true; restX = brain.X; }
                        else Near(brain.X, restX, 1e-9, "Frontal resting character kept translating sideways");
                        restingSeconds += dt;
                    }
                }
                Check(sawWalk && sawRest && restingSeconds >= 0.5, "Fixture did not observe a real walk followed by stable frontal rest");
            }
        });

        yield return ("Ending walking clears a glance and the next walk begins in the actual direction", () =>
        {
            foreach (var nextState in new[] { PetState.Idle, PetState.Dragged, PetState.Falling, PetState.Cry, PetState.Sleep })
            {
                var controller = Controller();
                Advance(controller, 2.5, 0.01, true, 45);
                controller.Update(nextState, false, 0, 45, 0.01);
                Check(controller.View == WalkView.Front && controller.AnimationElapsedMs == 0, "Non-walk state retained a stale walk viewpoint or phase");
                controller.Update(PetState.Walk, false, -45, 45, 0.01);
                Check(controller.View == WalkView.Turn && !controller.FacingRight, "Next walk reused the previous direction or stale glance");
            }
        });

        yield return ("Invalid render times and non-finite velocities cannot poison walking appearance", () =>
        {
            var controller = Controller();
            Advance(controller, 0.4, 0.01, true, 45);
            var phase = controller.AnimationElapsedMs;
            var view = controller.View;
            foreach (var invalid in new[] { 0d, -0.01, double.NaN, double.NegativeInfinity, double.PositiveInfinity })
                controller.Update(PetState.Walk, true, 45, 45, invalid);
            Check(controller.AnimationElapsedMs == phase && controller.View == view, "Invalid render times advanced the animation");
            foreach (var velocity in new[] { double.NaN, double.NegativeInfinity, double.PositiveInfinity })
            {
                controller.Update(PetState.Walk, true, velocity, 45, 0.01);
                Check(double.IsFinite(controller.AnimationElapsedMs), "Non-finite velocity poisoned the gait phase");
            }
            foreach (var nominal in new[] { 0d, -1, double.NaN, double.PositiveInfinity })
            {
                controller.Update(PetState.Walk, true, 45, nominal, 0.01);
                Check(double.IsFinite(controller.AnimationElapsedMs), "Invalid nominal speed poisoned the gait phase");
            }
            controller.Update(PetState.Walk, true, 45, 45, 3600);
            Check(double.IsFinite(controller.AnimationElapsedMs) && controller.AnimationElapsedMs < phase + 100_000,
                "A suspended renderer performed an unbounded animation catch-up");
        });

        yield return ("Appearance reset clears both animation phase and an in-flight direction reversal", () =>
        {
            var controller = Controller();
            Advance(controller, 0.4, 0.01, true, 45);
            controller.Update(PetState.Walk, false, -45, 45, 0.05);
            controller.Reset(false);
            Check(controller.View == WalkView.Front && !controller.FacingRight && controller.AnimationElapsedMs == 0,
                "Changing a presentation retained the previous orientation or walking phase");
            controller.Update(PetState.Walk, false, -45, 45, 0.01);
            Check(controller.View == WalkView.Turn && !controller.FacingRight, "Presentation reset left a delayed old reversal active");
        });
    }

    private static WalkAppearanceController Controller() => new(new FixedRandom());

    private static void Advance(WalkAppearanceController controller, double duration, double step, bool right, double velocity)
    {
        var elapsed = 0d;
        while (elapsed < duration - 1e-12)
        {
            var interval = Math.Min(step, duration - elapsed);
            controller.Update(PetState.Walk, right, velocity, 45, interval);
            elapsed += interval;
        }
    }

    private sealed class FixedRandom : Random
    {
        public override double NextDouble() => 0.5;
    }

    private sealed class SequenceRandom(params double[] values) : Random
    {
        private int _index;
        public override double NextDouble() => values[Math.Min(_index++, values.Length - 1)];
    }

    private static void Near(double actual, double expected, double tolerance, string message) =>
        Check(Math.Abs(actual - expected) <= tolerance, $"{message}: expected {expected}, actual {actual}");

    private static JsonObject Manifest() => JsonNode.Parse("""
        { "name":"Directional fixture", "frameSizePx":{"width":128,"height":128},
          "anchor":{"x":64,"y":122}, "frameIntervalMs":100,
          "frames":{"idle":["idle.png"],"walk":["side.png","side2.png"]} }
        """)!.AsObject();

    private static void Write(string folder, JsonObject manifest)
    {
        File.WriteAllText(Path.Combine(folder, "character.json"), manifest.ToJsonString());
        foreach (var file in new[] { "idle.png", "side.png", "side2.png" })
            File.WriteAllText(Path.Combine(folder, file), "loader path validation only");
    }

    private static void ExpectInvalid(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new Exception("Unsafe alternate walking artwork was accepted");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void InFolder(Action<string> run)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
        var folder = Path.Combine(root, "DesktopPet-Directional-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try { run(folder); }
        finally
        {
            var target = Path.GetFullPath(folder);
            Check(target.StartsWith(root, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(target).StartsWith("DesktopPet-Directional-"), "Unsafe cleanup path");
            Directory.Delete(target, recursive: true);
        }
    }
}
