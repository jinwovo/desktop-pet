using System.Diagnostics;
using System.IO.Compression;

namespace DesktopPet.Core;

public sealed record CharacterExportProgress(int CompletedFiles, int TotalFiles);

/// <summary>Export one validated snapshot, never local preferences or unrelated folder contents.</summary>
public static class CharacterPackExporter
{
    public static string ExportFolder(string source, string destination, Action<string>? validate = null,
        CancellationToken cancellationToken = default, IProgress<CharacterExportProgress>? progress = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
        destination = Path.GetFullPath(destination);
        if (!Directory.Exists(source) || source.Equals(Path.GetPathRoot(source), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Use an existing character folder, not a filesystem root.", nameof(source));
        CharacterLibrary.EnsureNoReparsePoints(source);
        var parent = Path.GetDirectoryName(destination)!;
        if (!Path.GetExtension(destination).Equals(".zip", StringComparison.OrdinalIgnoreCase) || !Directory.Exists(parent))
            throw new ArgumentException("Choose a ZIP in an existing destination folder.", nameof(destination));
        CharacterLibrary.EnsureNoReparsePoints(destination);
        if (File.Exists(destination) || Directory.Exists(destination)) throw new IOException("Choose a new ZIP filename.");
        if (destination.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Save the ZIP outside the character folder.", nameof(destination));

        var tempRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        var scratch = Path.Combine(tempRoot, "DesktopPet-Export-" + Guid.NewGuid().ToString("N"));
        string? partial = null;
        var ownsScratch = false;
        try
        {
            CharacterLibrary.EnsureNoReparsePoints(scratch);
            if (Directory.Exists(scratch) || File.Exists(scratch)) throw new IOException("Export staging path is occupied.");
            Directory.CreateDirectory(scratch);
            ownsScratch = true;
            var library = new CharacterLibrary(Path.Combine(scratch, "bundled"), Path.Combine(scratch, "packs"));
            var name = library.ImportFolder(source, validate, cancellationToken);
            var snapshot = library.Resolve(name);
            var definition = CharacterLoader.Load(snapshot, cancellationToken);
            var files = definition.AllFramePaths().Append(Path.Combine(snapshot, "character.json"))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => (Path: path, Relative: Path.GetRelativePath(snapshot, path).Replace('\\', '/')))
                .OrderBy(file => file.Relative, StringComparer.Ordinal).ToArray();
            progress?.Report(new CharacterExportProgress(0, files.Length));
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = Path.Combine(parent, ".DesktopPet-Export-" + Guid.NewGuid().ToString("N") + ".tmp");
            CharacterLibrary.EnsureNoReparsePoints(candidate);
            using (var stream = new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                partial = candidate; // Only clean up a partial file successfully created by this call.
                var buffer = new byte[81920];
                long copied = 0;
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
                {
                    for (var index = 0; index < files.Length; index++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var file = files[index];
                        CharacterLibrary.EnsureNoReparsePoints(file.Path);
                        var entry = archive.CreateEntry(name + "/" + file.Relative, CompressionLevel.Optimal);
                        entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
                        using (var input = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read))
                        using (var output = entry.Open())
                        {
                            int read;
                            while ((read = input.Read(buffer, 0, buffer.Length)) != 0)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                if (read > CharacterLibrary.MaxTotalBytes - copied) throw new InvalidDataException("Character export exceeds 64 MiB.");
                                output.Write(buffer, 0, read);
                                copied += read;
                            }
                        }
                        progress?.Report(new CharacterExportProgress(index + 1, files.Length));
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                }
                stream.Flush(flushToDisk: true);
                if (stream.Length > CharacterArchive.MaxArchiveBytes) throw new InvalidDataException("Character ZIP exceeds 64 MiB.");
            }
            CharacterLibrary.EnsureNoReparsePoints(destination);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(partial, destination); // Same-parent atomic publication, without replacement.
            partial = null;
            return destination;
        }
        finally
        {
            if (partial is not null)
            {
                try
                {
                    if (Path.GetDirectoryName(partial)!.Equals(parent, StringComparison.OrdinalIgnoreCase))
                    {
                        CharacterLibrary.EnsureNoReparsePoints(partial);
                        if (File.Exists(partial)) File.Delete(partial);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
                { Trace.TraceWarning("Could not clean up a partial character export."); }
            }
            if (ownsScratch)
            {
                try
                {
                    if (scratch.StartsWith(tempRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                        Path.GetFileName(scratch).StartsWith("DesktopPet-Export-", StringComparison.Ordinal))
                    {
                        CharacterLibrary.EnsureNoReparsePoints(scratch);
                        CharacterLibrary.CleanupStage(scratch);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
                { Trace.TraceWarning("Could not clean up character export staging."); }
            }
        }
    }
}
