using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using DesktopPet.Core;

public static class CharacterArchiveTests
{
    public static IEnumerable<(string Name, Action Run)> Create()
    {
        yield return ("ZIP root packs and Unicode wrapped packs import only declared resources", () => Fixture(root =>
        {
            foreach (var prefix in new[] { "", "collection/한글 친구/" })
            {
                var zip = Zip(root, Pack(prefix, "한글 친구").Append(("ignored.cmd", Encoding.UTF8.GetBytes("never execute")))) ;
                using var archive = CharacterArchive.Open(zip);
                var library = Library(root, Guid.NewGuid().ToString("N"));
                var candidate = archive.Characters.Single();
                var name = archive.Import(candidate, library, stage => Check(File.Exists(Path.Combine(stage, "poses", "idle.png")), "Validator missed PNG"));
                Check(name == "한글 친구" && library.GetNames().SequenceEqual(new[] { name }), "Unicode or root name changed");
                Check(Directory.GetFiles(library.Resolve(name), "*", SearchOption.AllDirectories).Length == 2, "Unreferenced content was installed");
            }
        }));

        yield return ("ZIP collections install only the explicitly selected character and preserve existing names", () => Fixture(root =>
        {
            using var archive = CharacterArchive.Open(Zip(root, Pack("Alpha/", "Display A").Concat(Pack("Beta/", "Display B"))));
            var library = Library(root);
            Check(archive.Characters.Count == 2 && archive.Characters[1].Label == "Display B (Beta)", "Collection labels lost identity");
            archive.Import(archive.Characters[1], library);
            Check(library.GetNames().SequenceEqual(new[] { "Beta" }), "Unselected character was installed");
            var manifest = File.ReadAllBytes(Path.Combine(library.Resolve("Beta"), "character.json"));
            Reject(() => archive.Import(archive.Characters[1], library));
            Check(manifest.SequenceEqual(File.ReadAllBytes(Path.Combine(library.Resolve("Beta"), "character.json"))), "Duplicate replaced existing bytes");
        }));

        yield return ("ZIP preflight rejects unsafe paths even in unselected or unrelated entries", () => Fixture(root =>
        {
            foreach (var path in new[] { "../outside.png", "/root.png", "C:/file.png", "a\\..\\b.png", "a//b.png", "a/./b.png", "a/../b.png",
                         "a:b.png", "trailing./x.png", "space /x.png", "CON.png", "a/NUL.txt", "a/COM1.png", "", "folder/../" })
                Reject(() => CharacterArchive.Open(Zip(root, Pack("Safe/", "Safe").Append((path, new byte[] { 1 })))).Dispose());
            Check(!Directory.Exists(Library(root).CustomRoot), "Unsafe ZIP created a user library");
        }));

        yield return ("ZIP duplicates and file-directory collisions fail before extraction", () => Fixture(root =>
        {
            foreach (var extras in new[]
                     {
                         new[] { ("Safe/poses/IDLE.png", new byte[] { 1 }) },
                         new[] { ("Safe/poses", new byte[] { 1 }) },
                         new[] { ("same/", Array.Empty<byte>()), ("SAME/", Array.Empty<byte>()) },
                         new[] { ("thing", new byte[] { 1 }), ("thing/", Array.Empty<byte>()) }
                     })
                Reject(() => CharacterArchive.Open(Zip(root, Pack("Safe/", "Safe").Concat(extras))).Dispose());
        }));

        yield return ("ZIP links and special Unix entries are rejected without touching targets", () => Fixture(root =>
        {
            foreach (var attributes in new[] { unchecked((int)0xA1FF0000), unchecked((int)0x21FF0000), (int)FileAttributes.ReparsePoint })
            {
                var zip = Zip(root, Pack("Safe/", "Safe"));
                using (var stream = File.Open(zip, FileMode.Open, FileAccess.ReadWrite))
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Update))
                    archive.GetEntry("Safe/poses/idle.png")!.ExternalAttributes = attributes;
                Reject(() => CharacterArchive.Open(zip).Dispose());
            }
        }));

        yield return ("ZIP archive, entry count and manifest bounds are enforced", () => Fixture(root =>
        {
            var huge = Path.Combine(root, "huge.zip");
            using (var stream = File.Create(huge)) stream.SetLength(CharacterArchive.MaxArchiveBytes + 1);
            Reject(() => CharacterArchive.Open(huge).Dispose());
            var entries = Enumerable.Range(0, CharacterArchive.MaxEntryCount + 1).Select(i => ($"{i}.txt", Array.Empty<byte>()));
            Reject(() => CharacterArchive.Open(Zip(root, entries)).Dispose());
            Reject(() => CharacterArchive.Open(Zip(root, new[] { ("character.json", new byte[CharacterArchive.MaxManifestBytes + 1]) })).Dispose());
        }));

        yield return ("ZIP declared expanded budgets reject oversized collections and individual packs", () => Fixture(root =>
        {
            var zip = Zip(root, Pack("Safe/", "Safe"));
            PatchExpandedSize(zip, "Safe/poses/idle.png", (uint)CharacterArchive.MaxExpandedBytes + 1);
            Reject(() => CharacterArchive.Open(zip).Dispose());
            zip = Zip(root, Pack("Safe/", "Safe"));
            PatchExpandedSize(zip, "Safe/poses/idle.png", (uint)CharacterLibrary.MaxTotalBytes);
            Reject(() => CharacterArchive.Open(zip).Dispose());
        }));

        yield return ("ZIP dishonest sizes remain bounded and cannot bypass the final PNG validator", () => Fixture(root =>
        {
            var zip = Zip(root, Pack("Safe/", "Safe", png: new byte[32000]));
            PatchExpandedSize(zip, "Safe/poses/idle.png", 1);
            using var archive = CharacterArchive.Open(zip);
            var library = Library(root);
            var validated = false;
            Reject(() => archive.Import(archive.Characters[0], library, folder =>
            {
                // .NET's ZIP stream caps output at the declared length. The bitmap validator
                // must still reject the resulting truncated PNG before atomic publication.
                Check(new FileInfo(Path.Combine(folder, "poses", "idle.png")).Length == 1, "ZIP stream exceeded its declared bound");
                validated = true;
                throw new InvalidDataException("Truncated PNG");
            }));
            Check(validated, "Fixture did not exercise final validation");
            Check(library.GetNames().Count == 0 && Directory.GetFileSystemEntries(library.CustomRoot).Length == 0,
                "Dishonest ZIP published a partial character or left a stage");
        }));

        yield return ("ZIP missing, unsafe and non-PNG manifest references fail closed", () => Fixture(root =>
        {
            foreach (var reference in new[] { "missing.png", "../outside.png", "C:/outside.png", "poses/idle.png:other", "poses/idle.exe" })
            {
                var pack = Pack("Safe/", "Safe", reference: reference);
                Reject(() => CharacterArchive.Open(Zip(root, pack)).Dispose());
            }
            Reject(() => CharacterArchive.Open(Zip(root, new[] { ("readme.txt", new byte[] { 1 }) })).Dispose());
            Reject(() => CharacterArchive.Open(Zip(root, new[] { ("character.json", Encoding.UTF8.GetBytes("null")) })).Dispose());
        }));

        yield return ("ZIP invalid profiles and failed bitmap validators leave existing library content intact", () => Fixture(root =>
        {
            var library = Library(root);
            using (var good = CharacterArchive.Open(Zip(root, Pack("Existing/", "Existing")))) good.Import(good.Characters[0], library);
            var badManifest = Manifest("Bad");
            badManifest["behavior"] = new JsonObject { ["walkWeight"] = -1 };
            Reject(() => CharacterArchive.Open(Zip(root, new[] { ("Bad/character.json", Encoding.UTF8.GetBytes(badManifest.ToJsonString())), ("Bad/poses/idle.png", new byte[] { 1 }) })).Dispose());
            using var badPng = CharacterArchive.Open(Zip(root, Pack("BadPng/", "BadPng")));
            Reject(() => badPng.Import(badPng.Characters[0], library, _ => throw new InvalidDataException("invalid PNG")));
            Check(library.GetNames().SequenceEqual(new[] { "Existing" }) && Directory.GetDirectories(library.CustomRoot).Length == 1,
                "Failed ZIP import changed existing content or left staging folders");
        }));

        yield return ("ZIP catalog holds a read lock through import and rejects foreign or disposed selections", () => Fixture(root =>
        {
            var zip = Zip(root, Pack("Safe/", "Safe"));
            var archive = CharacterArchive.Open(zip);
            using var foreign = CharacterArchive.Open(Zip(root, Pack("Other/", "Other")));
            try
            {
                Reject(() => File.WriteAllBytes(zip, new byte[] { 1 }));
                Reject(() => archive.Import(foreign.Characters[0], Library(root)));
                archive.Import(archive.Characters[0], Library(root));
            }
            finally { archive.Dispose(); }
            Reject(() => archive.Import(archive.Characters[0], Library(root)));
            File.WriteAllBytes(zip, new byte[] { 1 }); // Handle must be released after selection/import.
        }));

        yield return ("ZIP UTF-8 BOM manifests retain compatibility with PowerShell-generated packs", () => Fixture(root =>
        {
            var manifest = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(Manifest("BOM").ToJsonString())).ToArray();
            using var archive = CharacterArchive.Open(Zip(root, new[] { ("character.json", manifest), ("poses/idle.png", new byte[] { 1 }) }));
            var library = Library(root);
            archive.Import(archive.Characters[0], library);
            Check(CharacterLoader.Load(library.Resolve("BOM")).Name == "BOM", "BOM manifest did not load after import");
        }));

        yield return ("ZIP Windows separators normalize before traversal and duplicate checks", () => Fixture(root =>
        {
            var entries = Pack("Safe/", "Safe", reference: "poses\\idle.png").Select(p => (p.Path.Replace('/', '\\'), p.Bytes));
            using var archive = CharacterArchive.Open(Zip(root, entries));
            var library = Library(root);
            archive.Import(archive.Characters[0], library);
            Check(CharacterLoader.Load(library.Resolve("Safe")).GetFrames(PetState.Idle).Count == 1, "Windows ZIP did not round-trip");
            Reject(() => CharacterArchive.Open(Zip(root, entries.Append(("Safe/poses/IDLE.png", new byte[] { 1 })))).Dispose());
        }));
    }

    private static CharacterLibrary Library(string root, string suffix = "default") => new(Path.Combine(root, "bundled"), Path.Combine(root, "custom-" + suffix));
    private static JsonObject Manifest(string name, string reference = "poses/idle.png") => new()
    {
        ["name"] = name, ["anchor"] = new JsonObject { ["x"] = 64, ["y"] = 122 },
        ["frames"] = new JsonObject { ["idle"] = new JsonArray(reference) }
    };
    private static IEnumerable<(string Path, byte[] Bytes)> Pack(string prefix, string name, string reference = "poses/idle.png", byte[]? png = null) =>
        new[] { (prefix + "character.json", Encoding.UTF8.GetBytes(Manifest(name, reference).ToJsonString())), (prefix + "poses/idle.png", png ?? new byte[] { 1 }) };
    private static string Zip(string root, IEnumerable<(string Path, byte[] Bytes)> entries)
    {
        var path = Path.Combine(root, Guid.NewGuid().ToString("N") + ".zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var entry in entries)
        {
            // ZipArchive itself refuses empty names, so use a slash for that invalid-path case.
            using var output = archive.CreateEntry(entry.Path.Length == 0 ? "/" : entry.Path).Open();
            output.Write(entry.Bytes);
        }
        return path;
    }
    private static void PatchExpandedSize(string path, string name, uint size)
    {
        var bytes = File.ReadAllBytes(path);
        for (var i = 0; i + 46 < bytes.Length; i++)
        {
            if (BitConverter.ToUInt32(bytes, i) != 0x02014b50) continue;
            var length = BitConverter.ToUInt16(bytes, i + 28);
            if (Encoding.UTF8.GetString(bytes, i + 46, length) != name) continue;
            BitConverter.GetBytes(size).CopyTo(bytes, i + 24);
            File.WriteAllBytes(path, bytes);
            return;
        }
        throw new Exception("Missing central directory fixture entry");
    }
    private static void Reject(Action run)
    {
        try { run(); }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or ObjectDisposedException) { return; }
        throw new Exception("Unsafe operation unexpectedly succeeded");
    }
    private static void Fixture(Action<string> run)
    {
        var temp = Path.GetFullPath(Path.GetTempPath());
        var folder = Path.Combine(temp, "DesktopPet-ArchiveTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try { run(folder); }
        finally
        {
            var full = Path.GetFullPath(folder);
            Check(full.StartsWith(Path.TrimEndingDirectorySeparator(temp) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                  Path.GetFileName(full).StartsWith("DesktopPet-ArchiveTest-", StringComparison.Ordinal), "Unsafe fixture cleanup");
            Directory.Delete(full, true);
        }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
