using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;

namespace DesktopPet.App;

internal static class MotionFramePngWriter
{
    public static void Save(BitmapSource source, string path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!source.IsFrozen) throw new ArgumentException("The captured frame must be frozen.", nameof(source));
        var target = Path.GetFullPath(path);
        if (!string.Equals(Path.GetExtension(target), ".png", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose a PNG filename.", nameof(path));
        var parent = Path.GetDirectoryName(target)!;
        if (!Directory.Exists(parent)) throw new DirectoryNotFoundException("The destination folder does not exist.");
        if (File.Exists(target) || Directory.Exists(target)) throw new IOException("Choose a new filename.");
        cancellationToken.ThrowIfCancellationRequested();
        var partial = Path.Combine(parent, ".desktop-pet-frame-" + Guid.NewGuid().ToString("N") + ".partial");
        var owned = false;
        try
        {
            using (var stream = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                owned = true;
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(source));
                encoder.Save(stream);
                stream.Flush(true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(partial, target); // Publish once; never replace an existing/concurrent file.
            owned = false;
        }
        finally
        {
            if (owned)
                try { File.Delete(partial); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { Trace.TraceWarning("Could not remove a temporary scene PNG."); }
        }
    }
}
