using System.Text.Json.Nodes;
using DesktopPet.Core;

public static class WalkRigResourceTests
{
    public static IEnumerable<(string Name, Action Run)> Create()
    {
        yield return ("Characters without a live rig keep their complete legacy presentation resources", () => InFolder(folder =>
        {
            var manifest = Manifest();
            manifest.Remove("walkRig");
            Write(folder, manifest);
            var character = CharacterLoader.Load(folder);
            Check(character.WalkRig is null, "A legacy character acquired an implicit rig");
            Check(character.GetPresentationFramePaths().OrderBy(x => x).SequenceEqual(character.AllFramePaths().OrderBy(x => x)),
                "Legacy runtime omitted raster gait or rest resources");
        }));

        yield return ("Live walk rigs resolve all six texture paths and expose the selected body angle", () => InFolder(folder =>
        {
            Write(folder, Manifest());
            var character = CharacterLoader.Load(folder);
            var rig = character.WalkRig;
            Check(rig is not null && rig.Version == 1 && rig.FootLiftPx == 4, "Rig metadata was not loaded");
            foreach (var (view, prefix) in new[] { (WalkView.Side, "side"), (WalkView.Turn, "turn") })
            {
                var angle = rig!.GetView(view);
                foreach (var (path, part) in new[] { (angle.BodyPath, "body"), (angle.NearFootPath, "near"), (angle.FarFootPath, "far") })
                {
                    Check(path == Path.Combine(folder, "rig", prefix + "_" + part + ".png") && Path.IsPathFullyQualified(path),
                        "A rig part was not resolved to the expected character-local absolute path");
                    Check(character.AllFramePaths().Contains(path, StringComparer.OrdinalIgnoreCase), "A rig part is absent from the full resource inventory");
                }
            }
        }));

        yield return ("Live resource selection skips raster gait banks but preserves rest, look and shared aliases", () => InFolder(folder =>
        {
            var manifest = Manifest();
            manifest["frames"]!["idle"] = new JsonArray("idle.png", "side_0.png");
            Write(folder, manifest);
            var character = CharacterLoader.Load(folder);
            var selected = character.GetPresentationFramePaths().ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var name in new[] { "idle.png", "side_0.png", "look_left.png", "look_right.png", "cry.png", "sleep.png" })
                Check(selected.Contains(Path.Combine(folder, name)), "A non-walk pose or shared raster alias was discarded");
            foreach (var name in new[] { "side_1.png", "turn_1.png", "settle_side_1.png", "settle_turn_7.png" })
                Check(!selected.Contains(Path.Combine(folder, name)), "Live mode decoded an unused raster gait bank");
            Check(selected.Count == 12, "Live presentation did not reduce its resource set to six textures and required non-walk poses");
            var full = character.GetPresentationFramePaths(includeRasterGait: true).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Check(full.SetEquals(character.AllFramePaths()) && full.Count > selected.Count, "Explicit raster audit mode omitted part of the full inventory");
        }));

        yield return ("Incomplete or invalid live rig definitions fail before resource decoding", () => InFolder(folder =>
        {
            foreach (var invalid in new[] { "null", "{}", "{\"version\":2}", "[]" })
            {
                var manifest = Manifest();
                manifest["walkRig"] = JsonNode.Parse(invalid);
                Write(folder, manifest);
                ExpectInvalid(() => CharacterLoader.Load(folder));
            }
            foreach (var edit in new[] { "version", "lift", "side", "turn", "body", "near", "far" })
            {
                var manifest = Manifest();
                var rig = manifest["walkRig"]!;
                if (edit == "version") rig["version"] = 0;
                else if (edit == "lift") rig["footLiftPx"] = -1;
                else if (edit is "side" or "turn") rig[edit] = null;
                else rig["side"]![edit == "body" ? "body" : edit == "near" ? "nearFoot" : "farFoot"] = null;
                Write(folder, manifest);
                ExpectInvalid(() => CharacterLoader.Load(folder));
            }
            foreach (var lift in new[] { "65", "1e309", "\"NaN\"", "null" })
            {
                var manifest = Manifest();
                manifest["walkRig"]!["footLiftPx"] = JsonNode.Parse(lift);
                Write(folder, manifest);
                ExpectInvalid(() => CharacterLoader.Load(folder));
            }
        }));

        yield return ("Rig textures cannot escape the character folder or use missing and non-PNG resources", () => InFolder(folder =>
        {
            Directory.CreateDirectory(Path.Combine(folder, "rig"));
            File.WriteAllText(Path.Combine(folder, "rig", "wrong.txt"), "not a texture");
            foreach (var unsafePath in new[] { "../outside.png", "..\\outside.png", Path.Combine(folder, "rig", "side_body.png"), "C:body.png", "rig/side_body.png:stream", "rig/missing.png", "rig/wrong.txt", "" })
            {
                var manifest = Manifest();
                manifest["walkRig"]!["side"]!["body"] = unsafePath;
                Write(folder, manifest);
                ExpectInvalid(() => CharacterLoader.Load(folder));
            }
        }));

        yield return ("Live rigs require a valid physical cycle distance instead of falling back to time-based feet", () => InFolder(folder =>
        {
            foreach (var kind in new[] { "omitted", "null", "zero" })
            {
                var manifest = Manifest();
                if (kind == "omitted") manifest.Remove("walkCycleDistancePx");
                else manifest["walkCycleDistancePx"] = kind == "null" ? null : JsonValue.Create(0);
                Write(folder, manifest);
                ExpectInvalid(() => CharacterLoader.Load(folder));
            }
        }));

        yield return ("Shared live rig textures decode once despite reused side and turn path aliases", () => InFolder(folder =>
        {
            var manifest = Manifest();
            manifest["walkRig"]!["turn"]!["nearFoot"] = "rig/SIDE_NEAR.PNG";
            manifest["walkRig"]!["turn"]!["farFoot"] = "rig/SIDE_FAR.PNG";
            Write(folder, manifest);
            var character = CharacterLoader.Load(folder);
            var resources = character.GetPresentationFramePaths().ToArray();
            Check(resources.Length == resources.Distinct(StringComparer.OrdinalIgnoreCase).Count() && resources.Length == 9,
                "A shared foot texture was listed for duplicate decoding");
        }));

        yield return ("Prepared character import includes live rig textures in its validated published package", () => InFolder(folder =>
        {
            var source = Path.Combine(folder, "source", "LivePet");
            Write(source, Manifest());
            File.WriteAllText(Path.Combine(source, "unrelated.txt"), "ignored source extra");
            var library = new CharacterLibrary(Path.Combine(folder, "bundled"), Path.Combine(folder, "custom"));
            var name = library.ImportFolder(source);
            var imported = CharacterLoader.Load(library.Resolve(name));
            Check(imported.WalkRig is not null, "Import dropped the live rig declaration");
            var files = Directory.GetFiles(imported.FolderPath, "*", SearchOption.AllDirectories);
            Check(files.Length == imported.AllFramePaths().Count() + 1 && !files.Any(path => Path.GetFileName(path) == "unrelated.txt"),
                "Import omitted rig textures or copied unrelated source content");
            Check(imported.GetPresentationFramePaths().All(File.Exists), "Published live resources reference missing textures");
        }));
    }

    private static JsonObject Manifest() => new()
    {
        ["name"] = "Live rig fixture", ["frameSizePx"] = new JsonObject { ["width"] = 128, ["height"] = 128 },
        ["anchor"] = new JsonObject { ["x"] = 64, ["y"] = 122 }, ["walkCycleDistancePx"] = 30,
        ["frames"] = new JsonObject
        {
            ["idle"] = new JsonArray("idle.png"), ["walk"] = Files("side", 4), ["walkTurn"] = Files("turn", 4),
            ["walkSettleSide"] = Files("settle_side", 8), ["walkSettleTurn"] = Files("settle_turn", 8),
            ["lookLeft"] = new JsonArray("look_left.png"), ["lookRight"] = new JsonArray("look_right.png"),
            ["cry"] = new JsonArray("cry.png"), ["sleep"] = new JsonArray("sleep.png"),
        },
        ["walkRig"] = new JsonObject
        {
            ["version"] = 1, ["footLiftPx"] = 4,
            ["side"] = Angle("side"), ["turn"] = Angle("turn"),
        },
    };
    private static JsonObject Angle(string name) => new()
    {
        ["body"] = "rig/" + name + "_body.png", ["nearFoot"] = "rig/" + name + "_near.png", ["farFoot"] = "rig/" + name + "_far.png",
    };
    private static JsonArray Files(string prefix, int count) => new(Enumerable.Range(0, count).Select(i => (JsonNode?)JsonValue.Create($"{prefix}_{i}.png")).ToArray());
    private static void Write(string folder, JsonObject manifest)
    {
        Directory.CreateDirectory(Path.Combine(folder, "rig"));
        File.WriteAllText(Path.Combine(folder, "character.json"), manifest.ToJsonString());
        foreach (var prefix in new[] { "side", "turn" })
        foreach (var part in new[] { "body", "near", "far" })
            File.WriteAllText(Path.Combine(folder, "rig", prefix + "_" + part + ".png"), "loader-only texture path fixture");
        foreach (var entry in manifest["frames"]!.AsObject())
        foreach (var frame in entry.Value!.AsArray()) File.WriteAllText(Path.Combine(folder, frame!.GetValue<string>()), "loader-only raster fixture");
    }
    private static void ExpectInvalid(Action action)
    {
        try { action(); } catch (InvalidDataException) { return; }
        throw new Exception("Unsafe or incomplete live rig was accepted");
    }
    private static void Check(bool valid, string message) { if (!valid) throw new Exception(message); }
    private static void Near(double actual, double expected, double tolerance, string message) => Check(Math.Abs(actual - expected) <= tolerance, message);
    private static void InFolder(Action<string> run)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
        var folder = Path.Combine(root, "DesktopPet-LiveRig-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try { run(folder); }
        finally
        {
            var safe = Path.GetFullPath(folder);
            Check(safe.StartsWith(root, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(safe).StartsWith("DesktopPet-LiveRig-"), "Unsafe cleanup path");
            Directory.Delete(safe, recursive: true);
        }
    }
}
