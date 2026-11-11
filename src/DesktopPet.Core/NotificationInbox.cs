using System.Text.Json;

namespace DesktopPet.Core;

/// <summary>A request from an outside process for the pet to get the user's attention.</summary>
public sealed class PetNotification
{
    public string Title { get; set; } = "알림";
    public string Message { get; set; } = "";

    /// <summary>"shout" (default) or "cry" — which emote the pet plays while nagging.</summary>
    public string Mood { get; set; } = "shout";
}

/// <summary>
/// Watches a drop folder for notification files so anything on the machine — a Claude Code hook,
/// a build script, a scheduled task — can make the pet speak up by writing a small JSON file.
///
/// A drop folder is used rather than a socket or named pipe because the senders are typically
/// one-line shell commands: writing a file needs no client library, no connection handling, and
/// works even when the pet app isn't running yet (the message is picked up on next start).
/// </summary>
public sealed class NotificationInbox : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly Timer _sweepTimer;
    private readonly string _folder;

    /// <summary>Raised on a background thread — marshal to the UI thread before touching windows.</summary>
    public event Action<PetNotification>? NotificationReceived;

    public static string DefaultFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DesktopPet", "inbox");

    public NotificationInbox(string? folder = null)
    {
        _folder = folder ?? DefaultFolder;
        Directory.CreateDirectory(_folder);

        _watcher = new FileSystemWatcher(_folder, "*.json")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            EnableRaisingEvents = true,
        };
        _watcher.Created += (_, e) => TryConsume(e.FullPath);
        _watcher.Changed += (_, e) => TryConsume(e.FullPath);
        // Senders are expected to write to a temp name and rename into place (so the file is never
        // observed half-written) — and a rename raises Renamed, not Created.
        _watcher.Renamed += (_, e) => TryConsume(e.FullPath);

        // Backstop sweep: FileSystemWatcher can silently miss events if its buffer overflows, and
        // a dropped notification is exactly the thing the user is waiting on. The watcher handles
        // the normal path, so this only needs to be often enough to not feel broken.
        _sweepTimer = new Timer(_ => SweepFolder(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

        // Anything dropped while the app was closed still deserves to be shown.
        SweepFolder();
    }

    private void SweepFolder()
    {
        try
        {
            foreach (var existing in Directory.GetFiles(_folder, "*.json"))
            {
                TryConsume(existing);
            }
        }
        catch (DirectoryNotFoundException)
        {
            Directory.CreateDirectory(_folder);
        }
    }

    // The watcher events and the sweep timer can race for the same file; without this both could
    // read it before either deletes it, and the user would get the same message twice.
    private readonly object _consumeLock = new();

    private void TryConsume(string path)
    {
        lock (_consumeLock)
        {
            ConsumeCore(path);
        }
    }

    private void ConsumeCore(string path)
    {
        // The writer may still have the file open; retry briefly rather than dropping the message.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return;
                }

                var json = File.ReadAllText(path);
                File.Delete(path);

                var notification = JsonSerializer.Deserialize<PetNotification>(
                    json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (notification is not null)
                {
                    NotificationReceived?.Invoke(notification);
                }
                return;
            }
            catch (IOException)
            {
                Thread.Sleep(60);
            }
            catch (JsonException)
            {
                TryDelete(path); // malformed; don't retry forever
                return;
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(60);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* best effort */ }
    }

    public void Dispose()
    {
        _sweepTimer.Dispose();
        _watcher.Dispose();
    }
}
