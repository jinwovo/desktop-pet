using System.IO.Compression;
using DesktopPet.Core;

public static class CharacterImportCancellationTests
{
    public static IEnumerable<(string Name, Action Run)> Create()
    {
        yield return ("Pre-cancelled folder and ZIP operations create no library or open archive handle", () => Fixture((root, source, library, zip) =>
        {
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            Cancelled(() => library.ImportFolder(source, cancellationToken: cancellation.Token));
            Cancelled(() => CharacterArchive.Open(zip, cancellation.Token).Dispose());
            Check(!Directory.Exists(library.CustomRoot), "Pre-cancelled import created library content");
            using (File.Open(zip, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        }));
        yield return ("Cancelling folder copying or validation removes the unpublished stage and releases the writer mutex", () => Fixture((root, source, library, zip) =>
        {
            Directory.CreateDirectory(library.CustomRoot);
            var canary = Path.Combine(library.CustomRoot, "keep.json"); File.WriteAllText(canary, "existing user data");
            using (var cancellation = new CancellationTokenSource())
            {
                var progress = new InlineProgress(value => { if (value.Phase == CharacterImportPhase.Copying && value.CompletedFiles >= 1) cancellation.Cancel(); });
                Cancelled(() => library.ImportFolder(source, cancellationToken: cancellation.Token, progress: progress));
            }
            using (var cancellation = new CancellationTokenSource())
                Cancelled(() => library.ImportFolder(source, _ => cancellation.Cancel(), cancellation.Token));
            Check(Directory.GetDirectories(library.CustomRoot).Length == 0 && File.ReadAllText(canary) == "existing user data", "Cancellation published a character, leaked a stage or changed user data");
            var events = new List<CharacterImportProgress>();
            Check(library.ImportFolder(source, progress: new InlineProgress(events.Add)) == "Pack" && events.Any(e => e.Phase == CharacterImportPhase.Validating),
                "Retry remained locked or progress omitted validation");
            Check(events.All(e => e.CompletedFiles >= 0 && e.CompletedFiles <= e.TotalFiles && e.CopiedBytes >= 0 && e.CopiedBytes <= e.TotalBytes), "Progress exceeded its source bounds");
        }));
        yield return ("ZIP cancellation during extraction and final validation preserves installed content and permits retry", () => Fixture((root, source, library, zip) =>
        {
            using var archive = CharacterArchive.Open(zip);
            foreach (var phase in new[] { CharacterImportPhase.Extracting, CharacterImportPhase.Validating })
            {
                using var cancellation = new CancellationTokenSource();
                var progress = new InlineProgress(value => { if (value.Phase == phase && value.CompletedFiles >= 2) cancellation.Cancel(); });
                Cancelled(() => archive.Import(archive.Characters[0], library, cancellationToken: cancellation.Token, progress: progress));
                Check(library.GetNames().Count == 0 && (!Directory.Exists(library.CustomRoot) || Directory.GetDirectories(library.CustomRoot).Length == 0),
                    "Cancelled ZIP published a partial character or retained staging");
            }
            Check(archive.Import(archive.Characters[0], library) == "Pack" && library.GetNames().Count == 1, "Retry after ZIP cancellation failed");
        }));
        yield return ("Folder manifests share the archive's bounded metadata limit before parsing or publication", () => Fixture((root, source, library, zip) =>
        {
            var path = Path.Combine(source, "character.json");
            File.WriteAllText(path, new string(' ', CharacterLibrary.MaxManifestBytes) + File.ReadAllText(path));
            try { CharacterLoader.Load(source); throw new Exception("Oversized metadata parsed"); } catch (InvalidDataException) { }
            try { library.ImportFolder(source); throw new Exception("Oversized metadata imported"); } catch (InvalidDataException) { }
            Check(!Directory.Exists(library.CustomRoot), "Oversized manifest created a library");
        }));
    }
    private sealed class InlineProgress(Action<CharacterImportProgress> report) : IProgress<CharacterImportProgress>
    {
        public void Report(CharacterImportProgress value) => report(value);
    }
    private static void Cancelled(Action run) { try { run(); } catch (OperationCanceledException) { return; } throw new Exception("Cancellation did not stop unpublished work"); }
    private static void Fixture(Action<string, string, CharacterLibrary, string> run)
    {
        var temp = Path.GetFullPath(Path.GetTempPath());
        var root = Path.Combine(temp, "DesktopPet-CancelImport-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "source", "Pack"); Directory.CreateDirectory(Path.Combine(source, "frames"));
        File.WriteAllText(Path.Combine(source, "character.json"), "{\"name\":\"Pack\",\"anchor\":{\"x\":64,\"y\":122},\"frames\":{\"idle\":[\"frames/idle.png\"],\"walk\":[\"frames/walk.png\"]}}");
        File.WriteAllBytes(Path.Combine(source, "frames", "idle.png"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(source, "frames", "walk.png"), new byte[] { 2 });
        var zip = Path.Combine(root, "pack.zip"); ZipFile.CreateFromDirectory(source, zip);
        var library = new CharacterLibrary(Path.Combine(root, "bundled"), Path.Combine(root, "custom"));
        try { run(root, source, library, zip); }
        finally
        {
            var full = Path.GetFullPath(root);
            Check(full.StartsWith(Path.TrimEndingDirectorySeparator(temp) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(full).StartsWith("DesktopPet-CancelImport-"), "Unsafe fixture cleanup");
            Directory.Delete(full, true);
        }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
