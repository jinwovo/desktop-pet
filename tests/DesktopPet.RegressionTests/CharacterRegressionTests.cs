using System.Text.Json;
using System.Text.Json.Nodes;
using DesktopPet.Core;

public static class CharacterRegressionTests
{
    public static IEnumerable<(string Name, Action Run)> Create()
    {
        yield return ("Character metadata rejects malformed JSON and null objects", () => InFolder(folder =>
        {
            foreach (var json in new[] { "{", "null", "[]", "{\"name\":null}", "{\"frameSizePx\":null}", "{\"anchor\":null}", "{\"frames\":null}" })
            {
                File.WriteAllText(Path.Combine(folder, "character.json"), json);
                ExpectInvalid(() => CharacterLoader.Load(folder));
            }
        }));

        yield return ("Character metadata rejects unsafe dimensions, anchors and animation values", () => InFolder(folder =>
        {
            var invalid = new (string Path, JsonNode? Value)[]
            {
                ("name", JsonValue.Create(" ")),
                ("frameSizePx.width", JsonValue.Create(0)),
                ("frameSizePx.height", JsonValue.Create(-1)),
                ("frameSizePx.width", JsonValue.Create(4097)),
                ("anchor.x", JsonValue.Create(-1)),
                ("anchor.x", JsonValue.Create(129)),
                ("anchor.y", JsonValue.Create(0)),
                ("anchor.y", JsonValue.Create(129)),
                ("scale", JsonValue.Create(0)),
                ("scale", JsonValue.Create(-0.5)),
                ("scale", JsonValue.Create(1e100)),
                ("walkSpeedPxPerSec", JsonValue.Create(-1)),
                ("walkSpeedPxPerSec", JsonValue.Create(2001)),
                ("frameIntervalMs", JsonValue.Create(0)),
                ("frameIntervalMs", JsonValue.Create(-5)),
            };
            foreach (var (path, value) in invalid)
            {
                var json = ValidManifest();
                var segments = path.Split('.');
                var owner = segments.Length == 1 ? json : json[segments[0]]!;
                owner[segments[^1]] = value?.DeepClone();
                WriteManifest(folder, json);
                ExpectInvalid(() => CharacterLoader.Load(folder));
            }
        }));

        yield return ("Character with no readable frames fails before rendering", () => InFolder(folder =>
        {
            var manifest = ValidManifest();
            manifest["frames"] = new JsonObject { ["idle"] = new JsonArray("missing.png") };
            WriteManifest(folder, manifest);
            ExpectInvalid(() => CharacterLoader.Load(folder));
        }));

        yield return ("Character optional null and missing frames fall back to idle", () => InFolder(folder =>
        {
            var manifest = ValidManifest();
            manifest["frames"]!["walk"] = null;
            manifest["frames"]!["sleep"] = new JsonArray("missing.png");
            WriteManifest(folder, manifest);
            File.WriteAllText(Path.Combine(folder, "idle.png"), "file path validation only");
            var loaded = CharacterLoader.Load(folder);
            foreach (var state in Enum.GetValues<PetState>())
                Check(loaded.GetFrames(state).Single() == Path.Combine(folder, "idle.png"), $"No fallback for {state}");
        }));

        yield return ("Right-looking-only character has an idle fallback", () => InFolder(folder =>
        {
            var manifest = ValidManifest();
            manifest["frames"] = new JsonObject { ["lookRight"] = new JsonArray("right.png") };
            WriteManifest(folder, manifest);
            File.WriteAllText(Path.Combine(folder, "right.png"), "file path validation only");
            var loaded = CharacterLoader.Load(folder);
            Check(loaded.GetFrames(PetState.Idle).Single() == Path.Combine(folder, "right.png"), "Directional fallback missing");
            Check(loaded.GetLookFrames(false).Count == 1, "Left direction missing fallback");
        }));

        yield return ("Character frame paths reject traversal, rooted paths and alternate streams", () => InFolder(folder =>
        {
            var rooted = Path.Combine(folder, "idle.png");
            File.WriteAllText(rooted, "file path validation only");
            foreach (var path in new[] { "../outside.png", "..\\outside.png", rooted, "C:idle.png", "\\server\\share\\idle.png", "idle.png:hidden", "", " " })
            {
                var manifest = ValidManifest();
                manifest["frames"]!["idle"] = new JsonArray(path);
                WriteManifest(folder, manifest);
                ExpectInvalid(() => CharacterLoader.Load(folder));
            }
            var nullManifest = ValidManifest();
            nullManifest["frames"]!["idle"] = new JsonArray((JsonNode?)null);
            WriteManifest(folder, nullManifest);
            ExpectInvalid(() => CharacterLoader.Load(folder));
        }));

        yield return ("Safe nested character frames resolve to absolute paths", () => InFolder(folder =>
        {
            Directory.CreateDirectory(Path.Combine(folder, "poses"));
            File.WriteAllText(Path.Combine(folder, "poses", "idle.png"), "file path validation only");
            var manifest = ValidManifest();
            manifest["frames"]!["idle"] = new JsonArray("poses/idle.png");
            WriteManifest(folder, manifest);
            var loaded = CharacterLoader.Load(folder + Path.DirectorySeparatorChar);
            Check(loaded.GetFrames(PetState.Idle).Single() == Path.Combine(folder, "poses", "idle.png"), "Nested relative path failed");
            Check(Path.IsPathFullyQualified(loaded.FolderPath), "Character folder is not absolute");
        }));

        yield return ("Missing character manifest reports a recoverable data error", () => InFolder(folder =>
        {
            ExpectInvalid(() => CharacterLoader.Load(folder));
        }));

        yield return ("Character animation handles long or invalid elapsed time", () => InFolder(folder =>
        {
            var manifest = ValidManifest();
            manifest["frames"]!["idle"] = new JsonArray("idle.png", "idle2.png", "idle3.png");
            manifest["frameIntervalMs"] = 1;
            WriteManifest(folder, manifest);
            foreach (var file in new[] { "idle.png", "idle2.png", "idle3.png" }) File.WriteAllText(Path.Combine(folder, file), "frame");
            var loaded = CharacterLoader.Load(folder);
            var frames = loaded.GetFrames(PetState.Idle);
            foreach (var elapsed in new[] { -1d, double.NaN, double.PositiveInfinity, double.MaxValue, 1e15 })
                Check(frames.Contains(loaded.GetFramePath(PetState.Idle, frames, elapsed)), "Animation selected an invalid frame");
        }));

        yield return ("Character metadata cannot redirect through a reparse point", TestReparsePoint);
    }

    private static void TestReparsePoint() => InFolder(folder =>
    {
        var outside = Path.Combine(folder, "outside");
        var character = Path.Combine(folder, "character");
        Directory.CreateDirectory(outside);
        Directory.CreateDirectory(character);
        File.WriteAllText(Path.Combine(outside, "idle.png"), "frame");
        var link = Path.Combine(character, "linked");
        try
        {
            Directory.CreateSymbolicLink(link, outside);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            throw new RegressionTestSkippedException($"symbolic-link setup unavailable: {ex.GetType().Name}");
        }
        try
        {
            var manifest = ValidManifest();
            manifest["frames"]!["idle"] = new JsonArray("linked/idle.png");
            WriteManifest(character, manifest);
            ExpectInvalid(() => CharacterLoader.Load(character));
        }
        finally { Directory.Delete(link); }
    });

    private static JsonObject ValidManifest() => JsonNode.Parse("""
        { "name": "Test", "frameSizePx": { "width": 128, "height": 128 },
          "anchor": { "x": 64, "y": 122 }, "frames": { "idle": ["idle.png"] } }
        """)!.AsObject();

    private static void WriteManifest(string folder, JsonObject manifest) =>
        File.WriteAllText(Path.Combine(folder, "character.json"), manifest.ToJsonString());

    private static void ExpectInvalid(Action action)
    {
        try { action(); }
        catch (InvalidDataException ex)
        {
            Check(!string.IsNullOrWhiteSpace(ex.Message), "Character error has no explanation");
            return;
        }
        throw new Exception("Invalid character was accepted");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void InFolder(Action<string> run)
    {
        var root = Path.GetFullPath(Path.GetTempPath());
        var folder = Path.Combine(root, "DesktopPet-Character-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try { run(folder); }
        finally
        {
            var target = Path.GetFullPath(folder);
            Check(target.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                  Path.GetFileName(target).StartsWith("DesktopPet-Character-", StringComparison.Ordinal), "Unsafe character-test cleanup path");
            Directory.Delete(target, recursive: true);
        }
    }
}

public sealed class RegressionTestSkippedException(string message) : Exception(message);
