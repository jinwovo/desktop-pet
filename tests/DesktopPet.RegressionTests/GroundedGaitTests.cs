using System.Text.Json.Nodes;
using DesktopPet.Core;

public static class GroundedGaitTests
{
    public static IEnumerable<(string Name, Action Run)> Create()
    {
        yield return ("Distance gait metadata is opt-in and rejects invalid or implausible cycle distances", () => InFolder(folder =>
        {
            Write(folder, Manifest());
            Check(CharacterLoader.Load(folder).WalkCycleDistancePx is null, "Legacy character opted into a new physical stride");
            foreach (var value in new[] { "null", "20", "512" })
            {
                var manifest = Manifest();
                manifest["walkCycleDistancePx"] = JsonNode.Parse(value);
                Write(folder, manifest);
                var loaded = CharacterLoader.Load(folder);
                if (value == "null") Check(loaded.WalkCycleDistancePx is null, "Null stride changed legacy behavior");
                else Near(loaded.WalkCycleDistancePx!.Value, double.Parse(value, System.Globalization.CultureInfo.InvariantCulture), 1e-12, "Valid stride changed");
            }
            foreach (var value in new[] { "0", "-1", "512.01", "1e309", "\"NaN\"", "[]" })
            {
                var manifest = Manifest();
                manifest["walkCycleDistancePx"] = JsonNode.Parse(value);
                Write(folder, manifest);
                ExpectInvalid(() => CharacterLoader.Load(folder));
            }
        }));

        yield return ("Each planted foot stays fixed in world space in both directions and at every sprite scale", () =>
        {
            foreach (var distance in new[] { 12d, 28, 30, 72 })
            foreach (var scale in new[] { 0.5, 1, 2.5 })
            foreach (var right in new[] { true, false })
            foreach (var footB in new[] { false, true })
            {
                var startPhase = footB ? 1.5 : 1.0;
                double? plantedWorldX = null;
                for (var i = 0; i <= 100; i++)
                {
                    var totalPhase = startPhase + 0.59 * i / 100;
                    var pose = GroundedGait.Sample(totalPhase, distance, 4);
                    var foot = footB ? pose.FootB : pose.FootA;
                    Check(foot.IsPlanted && foot.Lift == 0, "The stance foot lifted or lost ground contact during its planted interval");
                    var worldX = 1200 + (right ? 1 : -1) * (totalPhase * distance + foot.X) * scale;
                    plantedWorldX ??= worldX;
                    Near(worldX, plantedWorldX.Value, 1e-8, "A planted foot slid along the ground while the body advanced");
                    Near(600 - foot.Lift * scale, 600, 1e-12, "Body motion moved the planted foot above or below the ground");
                }
            }
        });

        yield return ("Grounded gait always has a support foot and smoothly closes stance and swing contacts", () =>
        {
            const double distance = 30;
            const double lift = 5;
            for (var i = 0; i <= 2000; i++)
            {
                var pose = GroundedGait.Sample(i / 2000d, distance, lift);
                Check(pose.FootA.IsPlanted || pose.FootB.IsPlanted, "Walking entered an airborne pose with neither foot supporting the body");
                foreach (var foot in new[] { pose.FootA, pose.FootB })
                {
                    Check(double.IsFinite(foot.X) && Math.Abs(foot.X) <= distance * 0.35, "Foot traveled outside a bounded step");
                    Check(double.IsFinite(foot.Lift) && foot.Lift >= 0 && foot.Lift <= lift + 1e-9, "Foot lift sank below ground or exceeded its clearance");
                    if (foot.IsPlanted) Near(foot.Lift, 0, 1e-12, "A foot marked planted was visibly above the ground");
                }
                Check(double.IsFinite(pose.BodyBob), "Body motion became non-finite");
            }
            const double epsilon = 1e-6;
            foreach (var boundary in new[] { GroundedGait.StanceFraction, 1d })
            {
                var before = GroundedGait.Sample(boundary - epsilon, distance, lift).FootA;
                var contact = GroundedGait.Sample(boundary, distance, lift).FootA;
                var after = GroundedGait.Sample(boundary + epsilon, distance, lift).FootA;
                Near(after.X, before.X, distance * epsilon * 3, "Foot position snapped at contact or cycle wrap");
                Near((contact.X - before.X) / epsilon, -distance, 0.002, "Foot velocity snapped entering contact");
                Near((after.X - contact.X) / epsilon, -distance, 0.002, "Foot velocity snapped leaving contact");
                Check(before.Lift < 1e-7 && after.Lift < 1e-7, "Foot lift snapped at contact");
            }
        });

        yield return ("Gait normalization remains a valid contact phase for tiny negative and very long travel", () =>
        {
            foreach (var phase in new[] { -1e-20, -1e12 - 0.25, 1e12 + 0.75, double.MaxValue, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                var normalized = GroundedGait.NormalizePhase(phase);
                Check(double.IsFinite(normalized) && normalized >= 0 && normalized < 1, "A rounded negative phase escaped the normalized cycle");
                var pose = GroundedGait.Sample(phase, 30, 4);
                Check(double.IsFinite(pose.FootA.X) && double.IsFinite(pose.FootB.X) && double.IsFinite(pose.BodyBob),
                    "Long or invalid travel poisoned foot contact geometry");
            }
        });

        yield return ("Settling lowers raised feet without dragging their ground-plane anchors", () =>
        {
            foreach (var phase in new[] { 0.1, 0.35, 0.65, 0.75, 0.9 })
            {
                var original = GroundedGait.Sample(phase, 30, 6);
                var lastA = original.FootA.Lift;
                var lastB = original.FootB.Lift;
                for (var i = 0; i <= 100; i++)
                {
                    var settled = GroundedGait.Settle(original, i / 100d);
                    Near(settled.FootA.X, original.FootA.X, 1e-12, "Settling dragged foot A horizontally");
                    Near(settled.FootB.X, original.FootB.X, 1e-12, "Settling dragged foot B horizontally");
                    Check(settled.FootA.Lift >= 0 && settled.FootA.Lift <= lastA + 1e-12, "Settling lifted or sank foot A");
                    Check(settled.FootB.Lift >= 0 && settled.FootB.Lift <= lastB + 1e-12, "Settling lifted or sank foot B");
                    lastA = settled.FootA.Lift;
                    lastB = settled.FootB.Lift;
                }
                Near(lastA, 0, 1e-12, "Foot A never reached the floor");
                Near(lastB, 0, 1e-12, "Foot B never reached the floor");
            }
        });

        yield return ("Distance gait phase uses actual displacement rather than endpoint velocity or elapsed time", () =>
        {
            var controller = Controller();
            var traveled = 0d;
            foreach (var (distance, endpointSpeed, dt) in new[]
            {
                (0.2, 100d, 0.01), (0.8, 1d, 0.10), (0d, 100d, 0.05),
                (1.6, 0d, 0.02), (4d, 1000d, 0.03), (0d, 45d, 0.20),
            })
            {
                controller.Update(PetState.Walk, true, endpointSpeed, 45, dt, 45,
                    distanceDeltaPx: distance, walkCycleDistancePx: 20);
                traveled += distance;
                PhaseNear(controller.GaitPhase, traveled / 20, 1e-10, "Gait was advanced by endpoint speed instead of actual travel");
                Check(controller.View != WalkView.Front, "A moving body used frontal walking artwork");
            }
            Near(controller.AnimationElapsedMs, traveled / 45 * 1000, 1e-8, "Distance-driven legacy clock no longer represents actual travel");
        });

        yield return ("Zero displacement freezes the feet even when the movement target and renderer keep running", () =>
        {
            var controller = Controller();
            controller.Update(PetState.Walk, true, 45, 45, 0.02, 45, distanceDeltaPx: 5, walkCycleDistancePx: 30);
            var phase = controller.GaitPhase;
            var elapsed = controller.AnimationElapsedMs;
            for (var i = 0; i < 500; i++)
            {
                controller.Update(PetState.Walk, true, 45, 45, 1d / 60, 45, distanceDeltaPx: 0, walkCycleDistancePx: 30);
                PhaseNear(controller.GaitPhase, phase, 1e-12, "Feet kept cycling against a wall or on a paused body");
                Near(controller.AnimationElapsedMs, elapsed, 1e-12, "No movement still accumulated gait time");
            }
        });

        yield return ("A world-space acceleration, brake, pause and reversal follows the same gait at all scales and refresh rates", () =>
        {
            double? finalPhase = null;
            foreach (var scale in new[] { 0.5, 1, 2.5 })
            foreach (var refreshRate in new[] { 30, 60, 144 })
            {
                var controller = Controller();
                var time = 0d;
                var previousX = 0d;
                var traveled = 0d;
                while (time < 4.5 - 1e-12)
                {
                    var dt = Math.Min(1d / refreshRate, 4.5 - time);
                    time += dt;
                    var (baseX, baseVelocity) = Travel(time);
                    var worldX = baseX * scale;
                    var spriteDelta = (worldX - previousX) / scale;
                    previousX = worldX;
                    traveled += Math.Abs(spriteDelta);
                    var right = time <= 2.5;
                    controller.Update(PetState.Walk, right, baseVelocity * scale, 45, dt, 40 * scale,
                        distanceDeltaPx: spriteDelta, walkCycleDistancePx: 20);
                    Check(controller.View != WalkView.Front, "Acceleration or braking inserted frontal artwork into a lateral walk");
                    if (time > 2 && time < 2.5) PhaseNear(controller.GaitPhase, 0, 1e-8, "Paused body kept stepping");
                }
                Near(traveled, 80, 1e-7, "Motion fixture lost its known total travel");
                Near(controller.AnimationElapsedMs, 80d / 45 * 1000, 1e-6, "Gait travel depends on scale, acceleration or render frequency");
                finalPhase ??= controller.GaitPhase;
                PhaseNear(controller.GaitPhase, finalPhase.Value, 1e-7, "Scale or refresh rate changed the final gait phase");
            }
        });

        yield return ("Mirroring a direction reversal preserves both foot anchors and their lift", () =>
        {
            foreach (var phase in new[] { 0.05, 0.2, 0.35, 0.55, 0.7, 0.9 })
            foreach (var scale in new[] { 0.5, 1, 2.5 })
            {
                var controller = Controller();
                controller.Update(PetState.Walk, true, 45, 45, 0.01, 45, distanceDeltaPx: phase * 30, walkCycleDistancePx: 30);
                var before = GroundedGait.Sample(controller.GaitPhase, 30, 4);
                controller.Update(PetState.Walk, false, 0, 45, 0.01, 45, distanceDeltaPx: 0, walkCycleDistancePx: 30);
                var after = GroundedGait.Sample(controller.GaitPhase, 30, 4);
                Check(!controller.FacingRight, "Stopped reversal did not turn toward the requested direction");
                Near(before.FootA.X * scale, -after.FootA.X * scale, 1e-8, "Mirroring teleported foot A along the floor");
                Near(before.FootB.X * scale, -after.FootB.X * scale, 1e-8, "Mirroring teleported foot B along the floor");
                Near(before.FootA.Lift, after.FootA.Lift, 1e-8, "Mirroring changed foot A lift");
                Near(before.FootB.Lift, after.FootB.Lift, 1e-8, "Mirroring changed foot B lift");
                Near(before.BodyBob, after.BodyBob, 1e-8, "Mirroring changed body height over the same planted feet");
            }
        });

        yield return ("The planted support remains fixed when a reversed movement sample also changes facing", () =>
        {
            foreach (var scale in new[] { 0.5, 1, 2.5 })
            {
                var controller = Controller();
                controller.Update(PetState.Walk, true, 45, 45, 0.01, 45, distanceDeltaPx: 0.2 * 30, walkCycleDistancePx: 30);
                var before = GroundedGait.Sample(controller.GaitPhase, 30, 4);
                const double bodyBefore = 500;
                const double delta = -0.2;
                controller.Update(PetState.Walk, false, -20, 45, 0.01, 45, distanceDeltaPx: delta, walkCycleDistancePx: 30);
                var after = GroundedGait.Sample(controller.GaitPhase, 30, 4);
                Check(before.FootA.IsPlanted && after.FootA.IsPlanted && !controller.FacingRight, "Fixture lost its planted foot or reversal");
                Near(bodyBefore + before.FootA.X * scale,
                    bodyBefore + delta * scale - after.FootA.X * scale, 1e-8,
                    "Reversal integrated its last displacement in the wrong orientation and slid the stance foot");
            }
        });

        yield return ("Edge clamping does not reverse the feet before actual body travel reverses", () =>
        {
            var controller = Controller();
            controller.Update(PetState.Walk, true, 45, 45, 0.01, 45, distanceDeltaPx: 6, walkCycleDistancePx: 30);
            var contact = GroundedGait.Sample(controller.GaitPhase, 30, 4).FootA.X;
            controller.Update(PetState.Walk, false, 0, 45, 0.01, 45, distanceDeltaPx: 0.2, walkCycleDistancePx: 30);
            Check(controller.FacingRight, "An edge's next target reversed the sprite before its last rightward motion ended");
            Near(0.2 + GroundedGait.Sample(controller.GaitPhase, 30, 4).FootA.X, contact, 1e-9,
                "The edge frame slid its planted foot");
            controller.Update(PetState.Walk, false, -10, 45, 0.01, 45, distanceDeltaPx: -0.1, walkCycleDistancePx: 30);
            Check(!controller.FacingRight, "The actual leftward step did not reverse the sprite");
            Near(0.1 - GroundedGait.Sample(controller.GaitPhase, 30, 4).FootA.X, contact, 1e-9,
                "The first reversed step teleported its planted foot");
        });

        yield return ("Real brain acceleration and braking advance the gait by measured travel including the final stopping frame", () =>
        {
            foreach (var right in new[] { true, false })
            foreach (var scale in new[] { 0.5, 1, 2.5 })
            foreach (var refreshRate in new[] { 30, 60, 144 })
            {
                var brain = new PetBrain(new PlatformLayout(new[] { new Platform(0, 10000, 600) }),
                    100 * scale, 90 * scale, 5000, 600 - 90 * scale,
                    new SequenceRandom(0, 0.2, right ? 0.9 : 0.1, 0, 0.8, 0.9)) { WalkSpeedPxPerSec = 45, SleepEnabled = false };
                var controller = Controller();
                var sawWalk = false;
                var finished = false;
                var traveled = 0d;
                var expectedPhase = 0d;
                var initializedGait = false;
                for (var i = 0; i < refreshRate * 8; i++)
                {
                    var before = brain.X;
                    var wasWalking = brain.State == PetState.Walk;
                    var dt = 1d / refreshRate;
                    brain.Tick(dt, new PetPoint(-10000, -10000));
                    var delta = (brain.X - before) / scale;
                    var advancesGait = wasWalking || brain.State == PetState.Walk;
                    var previousFacing = controller.FacingRight;
                    var hadGait = initializedGait;
                    if (advancesGait)
                    {
                        traveled += Math.Abs(delta);
                        expectedPhase = initializedGait
                            ? expectedPhase + delta * (previousFacing ? 1 : -1) / 30
                            : Math.Abs(delta) / 30;
                        initializedGait = true;
                    }
                    controller.Update(brain.State, brain.FacingRight, brain.HorizontalVelocityPxPerSec, 45, dt, 45,
                        distanceDeltaPx: delta, walkCycleDistancePx: 30);
                    // The first zero-speed walking frame may retain the old resting direction.
                    // When actual travel starts opposite it, reflection preserves the planted
                    // feet; accumulated absolute distance alone intentionally lacks that offset.
                    if (advancesGait && hadGait && previousFacing != controller.FacingRight)
                        expectedPhase = 0.6 - expectedPhase;
                    if (advancesGait)
                        PhaseNear(controller.GaitPhase, expectedPhase, 1e-8, "Measured travel or orientation lost a gait phase increment");
                    if (brain.State == PetState.Walk)
                    {
                        sawWalk = true;
                        Check(controller.View != WalkView.Front, "Real distance gait faced front while the body was translating");
                    }
                    if (sawWalk && wasWalking && brain.State == PetState.Idle)
                    {
                        Check(controller.IsSettling, "Real stopping frame skipped planting its raised foot");
                        finished = true;
                        break;
                    }
                }
                Check(finished && traveled > 0, "Fixture did not observe a full analytic walk and stop");
                Near(controller.AnimationElapsedMs, traveled / 45 * 1000, 1e-7,
                    "Analytic brain position and gait were integrated with different clocks");
                PhaseNear(controller.GaitPhase, expectedPhase, 1e-8,
                    "The last deceleration frame was discarded when the brain entered rest");
            }
        });

        yield return ("Distance-driven stopping holds the gait phase while raised feet settle to the ground", () =>
        {
            var controller = Controller();
            controller.Update(PetState.Walk, true, 45, 45, 0.01, 45, distanceDeltaPx: 0.75 * 30, walkCycleDistancePx: 30);
            var phase = controller.GaitPhase;
            var original = GroundedGait.Sample(phase, 30, 4);
            Check(original.FootA.Lift > 0, "Fixture did not stop with a raised foot");
            var previousProgress = 0d;
            var sawSettling = false;
            for (var i = 0; i < 40; i++)
            {
                controller.Update(PetState.Idle, true, 0, 45, 0.01, 45, distanceDeltaPx: 0, walkCycleDistancePx: 30);
                PhaseNear(controller.GaitPhase, phase, 1e-12, "Stopping restarted the gait before the raised foot landed");
                Check(controller.SettleProgress >= previousProgress && controller.SettleProgress <= 1, "Stop settling moved backward or exceeded its range");
                previousProgress = controller.SettleProgress;
                if (controller.IsSettling)
                {
                    sawSettling = true;
                    Check(controller.View != WalkView.Front, "Raised feet were replaced with frontal rest before settling finished");
                }
            }
            Check(sawSettling && !controller.IsSettling && controller.SettleProgress == 1, "Distance gait never completed its short settling phase");
            Check(controller.View == WalkView.Front, "Settled character never returned to its frontal resting pose");
        });

        yield return ("Settle artwork is preloaded and selects the same foot phase at half and full lowering", () => InFolder(folder =>
        {
            var manifest = Manifest();
            manifest["walkCycleDistancePx"] = 30;
            manifest["frames"]!["walkSettleSide"] = Files("settle_side", 8);
            manifest["frames"]!["walkSettleTurn"] = Files("settle_turn", 8);
            Write(folder, manifest);
            var character = CharacterLoader.Load(folder);
            Check(character.AllFramePaths().Count() == 25, "Half/full settle frames were not preloaded exactly once");
            foreach (var view in new[] { WalkView.Side, WalkView.Turn })
            {
                var prefix = view == WalkView.Side ? "settle_side" : "settle_turn";
                foreach (var phase in new[] { 0d, 0.24, 0.25, 0.51, 0.99, 1.25 })
                {
                    var index = (int)Math.Floor((phase % 1) * 4);
                    Check(Path.GetFileName(character.GetWalkFramePath(view, phase, 0.25)) == $"{prefix}_{index}.png", "Half-lowered foot selected another gait phase");
                    Check(Path.GetFileName(character.GetWalkFramePath(view, phase, 1)) == $"{prefix}_{4 + index}.png", "Fully lowered foot selected another gait phase");
                }
            }
        }));

        yield return ("Settle cycles reject incomplete phase banks and unsafe paths before presentation", () => InFolder(folder =>
        {
            foreach (var key in new[] { "walkSettleSide", "walkSettleTurn" })
            {
                foreach (var count in new[] { 1, 4, 7, 9 })
                {
                    var manifest = Manifest();
                    manifest["frames"]![key] = Files("settle", count);
                    Write(folder, manifest);
                    ExpectInvalid(() => CharacterLoader.Load(folder));
                }
                var unsafeManifest = Manifest();
                unsafeManifest["frames"]![key] = new JsonArray("../outside.png");
                Write(folder, unsafeManifest);
                ExpectInvalid(() => CharacterLoader.Load(folder));
            }
        }));

        yield return ("A side-only distance skin reuses its side settle bank when the turn view is a side fallback", () => InFolder(folder =>
        {
            foreach (var hasTurn in new[] { false, true })
            {
                var manifest = Manifest();
                manifest["walkCycleDistancePx"] = 30;
                manifest["frames"]!["walkSettleSide"] = Files("settle_side", 8);
                if (!hasTurn) manifest["frames"]!.AsObject().Remove("walkTurn");
                Write(folder, manifest);
                var character = CharacterLoader.Load(folder);
                var file = Path.GetFileName(character.GetWalkFramePath(WalkView.Turn, 0.375, 1));
                Check(file == (hasTurn ? "turn_1.png" : "settle_side_5.png"),
                    "Fallback turn ignored its actual side settling artwork or borrowed a different body angle");
            }
        }));
    }

    private static (double Position, double Velocity) Travel(double time)
    {
        if (time <= 1) return (20 * time * time, 40 * time);
        if (time <= 2) { var t = time - 1; return (20 + 40 * t - 20 * t * t, 40 - 40 * t); }
        if (time <= 2.5) return (40, 0);
        if (time <= 3.5) { var t = time - 2.5; return (40 - 20 * t * t, -40 * t); }
        { var t = time - 3.5; return (20 - 40 * t + 20 * t * t, -40 + 40 * t); }
    }

    private static WalkAppearanceController Controller() => new(new FixedRandom());
    private sealed class FixedRandom : Random { public override double NextDouble() => 0.5; }
    private sealed class SequenceRandom(params double[] values) : Random
    {
        private int _index;
        public override double NextDouble() => values[Math.Min(_index++, values.Length - 1)];
    }
    private static void Near(double actual, double expected, double tolerance, string message) =>
        Check(double.IsFinite(actual) && Math.Abs(actual - expected) <= tolerance, $"{message}: expected {expected}, actual {actual}");
    private static void PhaseNear(double actual, double expected, double tolerance, string message)
    {
        Check(double.IsFinite(actual) && actual >= 0 && actual < 1, "Gait phase escaped its normalized range");
        var difference = Math.Abs(actual - (expected % 1 + 1) % 1);
        Check(Math.Min(difference, 1 - difference) <= tolerance, $"{message}: expected cycle {expected}, actual {actual}");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void ExpectInvalid(Action action)
    {
        try { action(); } catch (InvalidDataException) { return; }
        throw new Exception("Invalid gait metadata was accepted");
    }
    private static JsonArray Files(string prefix, int count) => new(Enumerable.Range(0, count).Select(i => (JsonNode?)JsonValue.Create($"{prefix}_{i}.png")).ToArray());
    private static JsonObject Manifest() => new()
    {
        ["name"] = "Grounded fixture", ["frameSizePx"] = new JsonObject { ["width"] = 128, ["height"] = 128 },
        ["anchor"] = new JsonObject { ["x"] = 64, ["y"] = 122 }, ["frameIntervalMs"] = 100,
        ["frames"] = new JsonObject { ["idle"] = new JsonArray("idle.png"), ["walk"] = Files("side", 4), ["walkTurn"] = Files("turn", 4) },
    };
    private static void Write(string folder, JsonObject manifest)
    {
        File.WriteAllText(Path.Combine(folder, "character.json"), manifest.ToJsonString());
        foreach (var property in manifest["frames"]!.AsObject())
            foreach (var file in property.Value!.AsArray())
            {
                var name = file!.GetValue<string>();
                if (!name.Contains("..")) File.WriteAllText(Path.Combine(folder, name), "loader path validation only");
            }
    }
    private static void InFolder(Action<string> run)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
        var folder = Path.Combine(root, "DesktopPet-Grounded-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try { run(folder); }
        finally
        {
            var full = Path.GetFullPath(folder);
            Check(full.StartsWith(root, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(full).StartsWith("DesktopPet-Grounded-"), "Unsafe cleanup path");
            Directory.Delete(full, recursive: true);
        }
    }
}
