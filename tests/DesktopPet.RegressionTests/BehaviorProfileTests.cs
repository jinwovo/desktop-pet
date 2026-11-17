using System.Text.Json;
using System.Text.Json.Nodes;
using DesktopPet.Core;

public static class BehaviorProfileTests
{
    public static IEnumerable<(string Name, Action Run)> Create()
    {
        yield return ("Behavior profiles reject invalid weights, durations, sleep limits and descriptions", () =>
        {
            var invalid = new Action[] {
                () => _ = new CharacterBehaviorProfile(walkWeight: -1), () => _ = new CharacterBehaviorProfile(walkWeight: double.NaN),
                () => _ = new CharacterBehaviorProfile(idleWeight: double.PositiveInfinity), () => _ = new CharacterBehaviorProfile(sitWeight: 11),
                () => _ = new CharacterBehaviorProfile(0, 0, 0), () => _ = new CharacterBehaviorProfile(restDurationMultiplier: 0.24),
                () => _ = new CharacterBehaviorProfile(walkDurationMultiplier: 4.1), () => _ = new CharacterBehaviorProfile(restDurationMultiplier: double.NaN),
                () => _ = new CharacterBehaviorProfile(sleepAfterIdleMs: 14999), () => _ = new CharacterBehaviorProfile(sleepAfterIdleMs: 1800001),
                () => _ = new CharacterBehaviorProfile(description: new string('x', 81)), () => _ = new CharacterBehaviorProfile(description: "a\nb") };
            foreach (var action in invalid)
            {
                try { action(); throw new Exception("Invalid profile accepted"); }
                catch (ArgumentException) { }
            }
        });

        yield return ("Missing, null and empty behavior metadata preserve legacy defaults", () => InFolder(folder =>
        {
            foreach (var value in new[] { "missing", "null", "{}" })
            {
                Write(folder, value);
                var profile = CharacterLoader.Load(folder).BehaviorProfile;
                Check(profile.WalkWeight == 1 && profile.IdleWeight == 1 && profile.SitWeight == 1 &&
                    profile.RestDurationMultiplier == 1 && profile.WalkDurationMultiplier == 1 && profile.SleepAfterIdleMs == 90000,
                    "Legacy metadata changed behavior or missing JSON arguments became zero");
            }
        }));

        yield return ("Behavior JSON validation completes before a character can be activated", () => InFolder(folder =>
        {
            foreach (var value in new[] { "{\"walkWeight\":-1}", "{\"walkWeight\":0,\"idleWeight\":0,\"sitWeight\":0}",
                "{\"sleepAfterIdleMs\":1}", "{\"walkDurationMultiplier\":99}", "{\"walkWeight\":1e999}", "[]", "42" })
            {
                Write(folder, value);
                try { CharacterLoader.Load(folder); throw new Exception("Invalid behavior JSON accepted"); }
                catch (InvalidDataException) { }
            }
            var authored = new CharacterBehaviorProfile(0.5, 1.3, 1.8, 1.4, 0.75, 60000, "A calm friend");
            Write(folder, JsonSerializer.Serialize(authored));
            var loaded = CharacterLoader.Load(folder).BehaviorProfile;
            Check(loaded.SleepAfterIdleMs == 60000 && loaded.RestDurationMultiplier == 1.4 && loaded.Description == "A calm friend", "Authored JSON failed to round-trip");
        }));

        yield return ("Default behavior keeps both calm and lively choice thresholds", () =>
        {
            foreach (var (calm, roll, expected) in new[] { (false, 0.69, PetState.Walk), (false, 0.71, PetState.Idle),
                (false, 0.89, PetState.Sit), (true, 0.41, PetState.Walk), (true, 0.43, PetState.Idle), (true, 0.83, PetState.Sit) })
            {
                var brain = Brain(new CharacterBehaviorProfile(), new SequenceRandom(0.5, roll, 0.75, 0.5));
                brain.CalmBehavior = calm; Advance(brain, 1.5);
                Check(brain.State == expected, "Default calm/lively decision threshold changed");
            }
        });

        yield return ("Authored behavior weights control autonomous choices in both comfort modes", () =>
        {
            foreach (var calm in new[] { false, true })
            {
                var sitting = Brain(new CharacterBehaviorProfile(0, 0, 1), new SequenceRandom()); sitting.CalmBehavior = calm;
                var walking = Brain(new CharacterBehaviorProfile(1, 0, 0), new SequenceRandom()); walking.CalmBehavior = calm;
                Advance(sitting, 1.5); Advance(walking, 1.5);
                Check(sitting.State == PetState.Sit && walking.State == PetState.Walk, "Validated weights were not used by the real brain");
            }
        });

        yield return ("Rest and walking duration multipliers change actual transition times without changing speed", () =>
        {
            var ordinaryRest = Brain(new CharacterBehaviorProfile(0, 0, 1), new SequenceRandom());
            var longerRest = Brain(new CharacterBehaviorProfile(0, 0, 1, restDurationMultiplier: 2), new SequenceRandom());
            Advance(ordinaryRest, 2); Advance(longerRest, 2);
            Check(ordinaryRest.State == PetState.Sit && longerRest.State == PetState.Idle, "Rest multiplier did not delay the first actual decision");
            Advance(longerRest, 1); Check(longerRest.State == PetState.Sit, "Scaled rest never finished");
            var ordinaryWalk = Brain(new CharacterBehaviorProfile(), new SequenceRandom(0.5, 0, 0.75, 0.5));
            var longerWalk = Brain(new CharacterBehaviorProfile(walkDurationMultiplier: 2), new SequenceRandom(0.5, 0, 0.75, 0.5));
            Advance(ordinaryWalk, 1.5); Advance(longerWalk, 1.5);
            Check(ordinaryWalk.State == PetState.Walk && longerWalk.State == PetState.Walk, "Fixture did not start walking");
            Advance(ordinaryWalk, 5); Advance(longerWalk, 5);
            Check(ordinaryWalk.State == PetState.Sit && longerWalk.State == PetState.Walk && ordinaryWalk.WalkSpeedPxPerSec == longerWalk.WalkSpeedPxPerSec,
                "Walk multiplier altered speed instead of the autonomous walking duration");
        });

        yield return ("Per-character sleep thresholds respect the user's sleep opt-out", () =>
        {
            var earlier = Brain(new CharacterBehaviorProfile(0, 1, 0, sleepAfterIdleMs: 15000), new SequenceRandom());
            var legacy = Brain(new CharacterBehaviorProfile(0, 1, 0), new SequenceRandom());
            var disabled = Brain(new CharacterBehaviorProfile(0, 1, 0, sleepAfterIdleMs: 15000), new SequenceRandom()); disabled.SleepEnabled = false;
            Advance(earlier, 16); Advance(legacy, 16); Advance(disabled, 16);
            Check(earlier.State == PetState.Sleep && legacy.State == PetState.Idle && disabled.State == PetState.Idle,
                "Sleep timing ignored character configuration or user preference");
        });

        yield return ("Changing behavior while walking preserves physical pose, velocity and current direction", () =>
        {
            var brain = Brain(new CharacterBehaviorProfile(1, 0, 0), new SequenceRandom());
            Advance(brain, 2);
            var pose = (brain.X, brain.Y, brain.FacingRight, brain.HorizontalVelocityPxPerSec, brain.State);
            brain.BehaviorProfile = new CharacterBehaviorProfile(0, 0, 1, 4, 0.25, 30000);
            Check(pose == (brain.X, brain.Y, brain.FacingRight, brain.HorizontalVelocityPxPerSec, brain.State), "Profile switch reset physical walking state");
            brain.Tick(1d / 60, Away());
            Check(brain.State == PetState.Walk && brain.X > pose.X && brain.X - pose.X < 2, "Profile switch caused a jump or abrupt stop");
        });
    }

    private static PetBrain Brain(CharacterBehaviorProfile profile, Random rng) => new(new PlatformLayout(new[] { new Platform(0, 100000, 1000) }), 128, 122, 1000, 878, rng, profile);
    private static PetPoint Away() => new(-10000, -10000);
    private static void Advance(PetBrain brain, double seconds) { for (var i = 0; i < (int)Math.Round(seconds * 60); i++) brain.Tick(1d / 60, Away()); }
    private sealed class SequenceRandom(params double[] sequence) : Random
    {
        private int _position;
        public override double NextDouble() => _position < sequence.Length ? sequence[_position++] : sequence.Length == 0 ? 0.5 : 0.99;
    }
    private static void Write(string folder, string behavior)
    {
        var node = new JsonObject { ["name"] = "Fixture", ["anchor"] = new JsonObject { ["x"] = 64, ["y"] = 122 },
            ["frames"] = new JsonObject { ["idle"] = new JsonArray("idle.png") } };
        if (behavior != "missing") node["behavior"] = JsonNode.Parse(behavior);
        File.WriteAllText(Path.Combine(folder, "character.json"), node.ToJsonString());
        File.WriteAllBytes(Path.Combine(folder, "idle.png"), new byte[] { 1 }); // Loader validates metadata/references, not bitmap decoding.
    }
    private static void InFolder(Action<string> run)
    {
        var root = Path.GetFullPath(Path.GetTempPath());
        var folder = Path.Combine(root, "DesktopPet-Behavior-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try { run(folder); }
        finally
        {
            var full = Path.GetFullPath(folder);
            Check(full.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                Path.GetFileName(full).StartsWith("DesktopPet-Behavior-", StringComparison.Ordinal), "Unsafe fixture cleanup");
            Directory.Delete(full, true);
        }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
