using System.Text.Json.Nodes;
using DesktopPet.Core;

public static class TimedAnimationTests
{
    public static IEnumerable<(string Name, Action Run)> Create()
    {
        yield return ("Sparse blinking keeps eyes open between short closed-eye frames", () => InFolder(folder =>
        {
            Write(folder, "{\"idle\":[4200,120,3300,110]}");
            var character = CharacterLoader.Load(folder);
            var frames = character.GetFrames(PetState.Idle);
            foreach (var (elapsed, expected) in new (double, int)[]
            {
                (0, 0), (4199.999, 0), (4200, 1), (4319.999, 1), (4320, 2),
                (7619.999, 2), (7620, 3), (7729.999, 3), (7730, 0), (7730 + 4200, 1),
            })
                Check(character.GetFramePath(PetState.Idle, frames, elapsed) == frames[expected], $"Wrong blink frame at {elapsed}ms");
        }));

        yield return ("Weighted animation handles very long, negative and non-finite elapsed times", () => InFolder(folder =>
        {
            Write(folder, "{\"idle\":[4200,120,3300,110]}");
            var character = CharacterLoader.Load(folder);
            var frames = character.GetFrames(PetState.Idle);
            foreach (var elapsed in new[] { -1d, double.NaN, double.NegativeInfinity, double.PositiveInfinity })
                Check(character.GetFramePath(PetState.Idle, frames, elapsed) == frames[0], "Invalid clock did not use initial frame");
            foreach (var elapsed in new[] { double.MaxValue, 1e20, 7730d * 1_000_000_000 + 4200 })
                Check(frames.Contains(character.GetFramePath(PetState.Idle, frames, elapsed)), "Long clock produced an invalid index");
            Check(character.GetFramePath(PetState.Idle, frames, 7730d * 1_000_000_000 + 4200) == frames[1], "Long sparse-blink cycle lost its boundary");
        }));

        yield return ("Unspecified animation timings retain the legacy per-state interval multiplier", () => InFolder(folder =>
        {
            Write(folder, "{\"idle\":[4200,120,3300,110]}");
            var character = CharacterLoader.Load(folder);
            var walk = character.GetFrames(PetState.Walk);
            Check(character.GetFramePath(PetState.Walk, walk, 99.9) == walk[0], "Walk interval changed");
            Check(character.GetFramePath(PetState.Walk, walk, 100) == walk[1], "Walk interval changed");
            Write(folder, null);
            character = CharacterLoader.Load(folder);
            var idle = character.GetFrames(PetState.Idle);
            Check(character.FrameDurationsByState.Count == 0, "Missing metadata created a weighted cycle");
            Check(character.GetFramePath(PetState.Idle, idle, 449.9) == idle[0], "Legacy idle multiplier changed");
            Check(character.GetFramePath(PetState.Idle, idle, 450) == idle[1], "Legacy idle multiplier changed");
        }));

        yield return ("Invalid weighted frame schedules fail before rendering", () => InFolder(folder =>
        {
            foreach (var json in new[]
            {
                "{\"idle\":null}", "{\"idle\":[]}", "{\"idle\":[1,2]}",
                "{\"idle\":[4200,0,3300,110]}", "{\"idle\":[4200,-1,3300,110]}",
                "{\"idle\":[1e308,1e308,1,1]}", "{\"idle\":[1e309,1,1,1]}",
                "{\"unknown\":[1]}", "{\"sleep\":[1]}", "{\"idle\":[1,1,1,1],\"IDLE\":[1,1,1,1]}",
            })
            {
                Write(folder, json);
                try { CharacterLoader.Load(folder); }
                catch (InvalidDataException) { continue; }
                throw new Exception($"Invalid timing metadata was accepted: {json}");
            }
        }));

        yield return ("Timing validation uses resolved frame count, including repeated image paths", () => InFolder(folder =>
        {
            Write(folder, "{\"idle\":[4200,120,3300,110]}");
            var character = CharacterLoader.Load(folder);
            Check(character.GetFrames(PetState.Idle).Count == 4, "Repeated animation frames were deduplicated");
            Check(character.AllFramePaths().Count() == 2, "Decoded file list should still deduplicate image paths");
            File.Delete(Path.Combine(folder, "blink.png"));
            try { CharacterLoader.Load(folder); }
            catch (InvalidDataException) { return; }
            throw new Exception("Schedule remained valid after a referenced frame disappeared");
        }));
    }

    private static void Write(string folder, string? timing)
    {
        var manifest = JsonNode.Parse("""
            { "name":"Timing", "frameSizePx":{"width":128,"height":128},
              "anchor":{"x":64,"y":122}, "frameIntervalMs":100,
              "frames":{"idle":["idle.png","blink.png","idle.png","blink.png"],"walk":["idle.png","blink.png"]} }
            """)!;
        if (timing is not null) manifest["frameDurationsMs"] = JsonNode.Parse(timing);
        File.WriteAllText(Path.Combine(folder, "character.json"), manifest.ToJsonString());
        File.WriteAllText(Path.Combine(folder, "idle.png"), "loader path validation only");
        File.WriteAllText(Path.Combine(folder, "blink.png"), "loader path validation only");
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void InFolder(Action<string> run)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
        var folder = Path.Combine(root, "DesktopPet-Timing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try { run(folder); }
        finally
        {
            var target = Path.GetFullPath(folder);
            Check(target.StartsWith(root, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(target).StartsWith("DesktopPet-Timing-"), "Unsafe cleanup path");
            Directory.Delete(target, recursive: true);
        }
    }
}
