using System.Text.Json.Nodes;
using DesktopPet.Core;

public static class CharacterLibraryTests
{
    public static IEnumerable<(string Name, Action Run)> Create()
    {
        yield return ("Character library discovery and failed lookups never create a custom directory", () => InFixture(fixture =>
        {
            var library = fixture.Library();
            Check(library.GetNames().Count == 0, "An empty library unexpectedly discovered a character");
            ExpectFailure(() => library.Resolve("Missing"));
            Check(!Directory.Exists(fixture.Custom), "Read-only library operations created a user directory");
            Check(Path.IsPathFullyQualified(library.BundledRoot) && Path.IsPathFullyQualified(library.CustomRoot), "Library roots were not normalized");
        }));

        yield return ("Bundled characters win over custom shadows while safe custom characters remain discoverable", () => InFixture(fixture =>
        {
            var bundled = fixture.Character(fixture.Bundled, "Companion");
            fixture.Character(fixture.Custom, "Companion", "Shadow");
            var custom = fixture.Character(fixture.Custom, "Alpha");
            fixture.Character(fixture.Bundled, "Zulu");
            fixture.Character(fixture.Custom, ".desktop-pet-import-abandoned");
            var library = fixture.Library();
            Check(library.GetNames().SequenceEqual(new[] { "Alpha", "Companion", "Zulu" }), "Catalog included a duplicate, hidden stage or inconsistent ordering");
            Check(SamePath(library.Resolve("companion"), bundled), "A custom shadow replaced a bundled character");
            Check(SamePath(library.Resolve("ALPHA"), custom), "Custom resolution did not accept a case alias");
        }));

        yield return ("Character name resolution rejects traversal, alternate streams and reserved aliases", () => InFixture(fixture =>
        {
            var library = fixture.Library();
            foreach (var name in new[] { "", " ", ".", "..", "../Companion", "x\\Companion", fixture.Bundled, "Companion:stream", "Companion.", "Companion ", "CON", "nul.txt", ".desktop-pet-import-reserved" })
                ExpectFailure(() => library.Resolve(name));
            Check(!Directory.Exists(fixture.Custom), "Unsafe resolution created a user directory");
        }));

        yield return ("Prepared character import copies only metadata and referenced PNGs and preserves source bytes", () => InFixture(fixture =>
        {
            var source = fixture.Source("My Pet", "Display name differs from folder");
            File.WriteAllText(Path.Combine(source, "unrelated.cmd"), "unrelated content");
            File.WriteAllText(Path.Combine(source, "poses", "unused.png"), "unreferenced bytes");
            var before = Snapshot(source);
            var library = fixture.Library();
            var imported = library.ImportFolder(source);
            Check(imported == "My Pet", "Import name did not use the prepared source folder leaf");
            var target = library.Resolve(imported);
            Check(SamePath(target, Path.Combine(fixture.Custom, imported)), "Import was published outside the custom library");
            Check(Directory.GetFiles(target, "*", SearchOption.AllDirectories).Length == 3, "Import copied unrelated content or omitted a referenced asset");
            foreach (var relative in new[] { "character.json", "poses/idle.png", "poses/walk.png" })
                Check(File.ReadAllBytes(Path.Combine(target, relative)).SequenceEqual(File.ReadAllBytes(Path.Combine(source, relative))), "Import changed source metadata or frame bytes");
            Check(SameSnapshot(before, Snapshot(source)), "Import modified the prepared source folder");
            Check(CharacterLoader.Load(target).Name == "Display name differs from folder", "Imported metadata cannot be loaded");
        }));

        yield return ("Custom import rejects bundled shadows and existing custom folders without overwriting content", () => InFixture(fixture =>
        {
            fixture.Character(fixture.Bundled, "Companion");
            var bundledSource = fixture.Source("companion");
            var library = fixture.Library();
            ExpectFailure(() => library.ImportFolder(bundledSource));
            Check(!Directory.Exists(fixture.Custom), "Rejected bundled shadow created a user library");
            var source = fixture.Source("Personal");
            library.ImportFolder(source);
            var target = library.Resolve("Personal");
            File.WriteAllText(Path.Combine(target, "keep.txt"), "existing custom data");
            var before = Snapshot(target);
            ExpectFailure(() => library.ImportFolder(source));
            Check(SameSnapshot(before, Snapshot(target)), "Repeated import overwrote or removed existing custom files");
            var occupied = Path.Combine(fixture.Custom, "Occupied");
            Directory.CreateDirectory(occupied);
            File.WriteAllText(Path.Combine(occupied, "notes.txt"), "not an app character");
            ExpectFailure(() => library.ImportFolder(fixture.Source("occupied")));
            Check(File.ReadAllText(Path.Combine(occupied, "notes.txt")) == "not an app character", "Import overwrote an unrelated same-name folder");
        }));

        yield return ("Import validator sees a complete unpublished stage and failed validation leaves no partial character", () => InFixture(fixture =>
        {
            var source = fixture.Source("Rejected");
            Directory.CreateDirectory(fixture.Custom);
            File.WriteAllText(Path.Combine(fixture.Custom, "keep.txt"), "unrelated user file");
            var before = Snapshot(fixture.Custom);
            var calls = 0;
            ExpectFailure(() => fixture.Library().ImportFolder(source, stage =>
            {
                calls++;
                Check(!Directory.Exists(Path.Combine(fixture.Custom, "Rejected")), "Import published the final folder before image validation");
                Check(stage.StartsWith(fixture.Custom + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Validation stage is outside the target library");
                Check(CharacterLoader.Load(stage).Name == "Rejected", "Validator received an incomplete staged character");
                throw new InvalidDataException("Synthetic bitmap decode failure");
            }));
            Check(calls == 1, "Import failed before reaching its actual staged validation callback");
            Check(SameSnapshot(before, Snapshot(fixture.Custom)), "Failed validation changed unrelated files or left staged content");
            Check(Directory.GetDirectories(fixture.Custom).Length == 0 && fixture.Library().GetNames().Count == 0, "Failed validation left a partial visible character or empty stage");
        }));

        yield return ("Successful validation occurs before atomic publication and leaves no stage directory", () => InFixture(fixture =>
        {
            var source = fixture.Source("Accepted");
            var library = fixture.Library();
            var calls = 0;
            var name = library.ImportFolder(source, stage =>
            {
                calls++;
                Check(!library.GetNames().Contains("Accepted"), "Catalog exposed the import before validation completed");
                Check(CharacterLoader.Load(stage).GetFrames(PetState.Walk).Count == 1, "Stage omitted a referenced gait frame");
            });
            Check(calls == 1 && name == "Accepted" && library.GetNames().SequenceEqual(new[] { "Accepted" }), "Validated character was not published exactly once");
            Check(Directory.GetDirectories(fixture.Custom).Select(Path.GetFileName).SequenceEqual(new[] { "Accepted" }), "Successful import left a staging directory");
        }));

        yield return ("Prepared stages are checked again after validation before metadata or file changes can be published", () => InFixture(fixture =>
        {
            foreach (var change in new[] { "decoded-budget", "unexpected-file", "missing-frame", "non-png-reference" })
            {
                var source = fixture.Source("Changed-" + change);
                var callbackRan = false;
                ExpectFailure(() => fixture.Library().ImportFolder(source, stage =>
                {
                    callbackRan = true;
                    var path = Path.Combine(stage, "character.json");
                    if (change == "unexpected-file") File.WriteAllText(Path.Combine(stage, "unexpected.ps1"), "unexpected stage content");
                    else if (change == "missing-frame") File.Delete(Path.Combine(stage, "poses", "idle.png"));
                    else
                    {
                        var manifest = JsonNode.Parse(File.ReadAllText(path))!;
                        if (change == "decoded-budget")
                        {
                            manifest["frameSizePx"]!["width"] = 4096;
                            manifest["frameSizePx"]!["height"] = 4096;
                        }
                        else manifest["frames"]!["idle"] = new JsonArray("character.json");
                        File.WriteAllText(path, manifest.ToJsonString());
                    }
                }));
                Check(callbackRan, "Fixture did not reach staged validation");
                Check(!Directory.Exists(Path.Combine(fixture.Custom, "Changed-" + change)), "Modified staged data was published");
                Check(Directory.GetDirectories(fixture.Custom).Length == 0, "Rejected modified stage left a partial character");
                Check(CharacterLoader.Load(source).FrameWidth == 128 && Directory.GetFiles(source, "*", SearchOption.AllDirectories).Length == 3,
                    "Rejecting staged changes modified the original prepared character");
            }
        }));

        yield return ("Malformed character metadata and unsafe or non-PNG frame references fail without publishing", () => InFixture(fixture =>
        {
            foreach (var kind in new[] { "json", "traversal", "rooted", "stream", "non-png" })
            {
                var source = fixture.Source("Invalid-" + kind);
                var path = Path.Combine(source, "character.json");
                if (kind == "json") File.WriteAllText(path, "{");
                else
                {
                    var manifest = JsonNode.Parse(File.ReadAllText(path))!;
                    var frame = kind switch
                    {
                        "traversal" => "../outside.png",
                        "rooted" => Path.Combine(source, "poses", "idle.png"),
                        "stream" => "poses/idle.png:stream",
                        _ => "poses/idle.txt",
                    };
                    if (kind == "non-png") File.WriteAllText(Path.Combine(source, frame), "untrusted referenced text");
                    manifest["frames"]!["idle"] = new JsonArray(frame);
                    File.WriteAllText(path, manifest.ToJsonString());
                }
                ExpectFailure(() => fixture.Library().ImportFolder(source));
                Check(!Directory.Exists(Path.Combine(fixture.Custom, "Invalid-" + kind)), "Invalid import was published");
            }
            Check(fixture.Library().GetNames().Count == 0, "Invalid imported data entered the library catalog");
        }));

        yield return ("Import bounds include unreferenced files and reject an oversized prepared folder", () => InFixture(fixture =>
        {
            var source = fixture.Source("TooLarge");
            using (var oversized = new FileStream(Path.Combine(source, "unreferenced.bin"), FileMode.CreateNew))
                oversized.SetLength(CharacterLibrary.MaxTotalBytes + 1);
            ExpectFailure(() => fixture.Library().ImportFolder(source));
            Check(!Directory.Exists(Path.Combine(fixture.Custom, "TooLarge")), "Oversized character was published");
            Check(new FileInfo(Path.Combine(source, "unreferenced.bin")).Length == CharacterLibrary.MaxTotalBytes + 1, "Rejected oversized source was changed");
        }));

        yield return ("Import bounds reject excessive file counts before copying unrelated content", () => InFixture(fixture =>
        {
            var source = fixture.Source("TooMany");
            for (var i = 0; i <= CharacterLibrary.MaxFileCount; i++)
                File.WriteAllText(Path.Combine(source, $"extra-{i:D4}.txt"), "x");
            ExpectFailure(() => fixture.Library().ImportFolder(source));
            Check(!Directory.Exists(Path.Combine(fixture.Custom, "TooMany")), "A source with excessive entries was published");
        }));

        yield return ("Import rejects a decoded image budget overflow before invoking bitmap validation", () => InFixture(fixture =>
        {
            var source = fixture.Source("DecodedBudget");
            var path = Path.Combine(source, "character.json");
            var manifest = JsonNode.Parse(File.ReadAllText(path))!;
            manifest["frameSizePx"]!["width"] = 4096;
            manifest["frameSizePx"]!["height"] = 4096;
            File.WriteAllText(path, manifest.ToJsonString());
            var called = false;
            ExpectFailure(() => fixture.Library().ImportFolder(source, _ => called = true));
            Check(!called, "Import invoked expensive decoding before rejecting the declared decoded-memory budget");
            Check(!Directory.Exists(Path.Combine(fixture.Custom, "DecodedBudget")), "A character exceeding the decoded budget was published");
        }));

        yield return ("Linked prepared source roots are rejected without reading through or changing their target", () => InFixture(fixture =>
        {
            var target = fixture.Source("Original");
            var linked = Path.Combine(fixture.Root, "LinkedSource");
            CreateDirectoryLink(linked, target);
            var before = Snapshot(target);
            ExpectFailure(() => fixture.Library().ImportFolder(linked));
            Check(SameSnapshot(before, Snapshot(target)), "Rejecting a linked source changed its target");
        }));

        yield return ("An unreferenced linked source entry is rejected rather than copied or ignored unsafely", () => InFixture(fixture =>
        {
            var source = fixture.Source("ContainsLink");
            var outside = Path.Combine(fixture.Root, "unrelated-target");
            Directory.CreateDirectory(outside);
            File.WriteAllText(Path.Combine(outside, "keep.txt"), "unchanged");
            CreateDirectoryLink(Path.Combine(source, "unreferenced-link"), outside);
            ExpectFailure(() => fixture.Library().ImportFolder(source));
            Check(File.ReadAllText(Path.Combine(outside, "keep.txt")) == "unchanged", "Linked entry rejection changed unrelated target data");
            Check(!Directory.Exists(Path.Combine(fixture.Custom, "ContainsLink")), "A source containing a link was published");
        }));
    }

    private static void CreateDirectoryLink(string path, string target)
    {
        try { Directory.CreateSymbolicLink(path, target); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            throw new RegressionTestSkippedException("This environment cannot create a symbolic-link fixture: " + ex.Message);
        }
    }

    private static Dictionary<string, byte[]> Snapshot(string folder) => Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(folder, path), File.ReadAllBytes, StringComparer.OrdinalIgnoreCase);

    private static bool SameSnapshot(Dictionary<string, byte[]> left, Dictionary<string, byte[]> right) => left.Count == right.Count &&
        left.All(pair => right.TryGetValue(pair.Key, out var bytes) && pair.Value.AsSpan().SequenceEqual(bytes));
    private static bool SamePath(string left, string right) => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
    private static void ExpectFailure(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException) { return; }
        throw new Exception("Unsafe or invalid character library operation unexpectedly succeeded");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    private static void InFixture(Action<Fixture> run)
    {
        using var fixture = new Fixture();
        run(fixture);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _temporaryRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
        public string Root { get; }
        public string Bundled => Path.Combine(Root, "bundled");
        public string Custom => Path.Combine(Root, "custom");
        public Fixture()
        {
            Root = Path.Combine(_temporaryRoot, "DesktopPet-Library-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Bundled);
        }
        public CharacterLibrary Library() => new(Bundled, Custom);
        public string Source(string name, string? displayName = null) => Character(Path.Combine(Root, "sources"), name, displayName);
        public string Character(string root, string name, string? displayName = null)
        {
            var folder = Path.Combine(root, name);
            Directory.CreateDirectory(Path.Combine(folder, "poses"));
            var manifest = new JsonObject
            {
                ["name"] = displayName ?? name,
                ["frameSizePx"] = new JsonObject { ["width"] = 128, ["height"] = 128 },
                ["anchor"] = new JsonObject { ["x"] = 64, ["y"] = 122 },
                ["frames"] = new JsonObject
                {
                    ["idle"] = new JsonArray("poses/idle.png"),
                    ["walk"] = new JsonArray("poses/walk.png"),
                },
            };
            File.WriteAllText(Path.Combine(folder, "character.json"), manifest.ToJsonString());
            File.WriteAllText(Path.Combine(folder, "poses", "idle.png"), "test image decode handled by optional validator");
            File.WriteAllText(Path.Combine(folder, "poses", "walk.png"), "separate gait fixture");
            return folder;
        }
        public void Dispose()
        {
            var target = Path.GetFullPath(Root);
            Check(target.StartsWith(_temporaryRoot, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(target).StartsWith("DesktopPet-Library-"), "Unsafe fixture cleanup path");
            Directory.Delete(target, recursive: true);
        }
    }
}
