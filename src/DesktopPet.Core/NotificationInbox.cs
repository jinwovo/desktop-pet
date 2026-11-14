using System.Diagnostics;
using System.Text.Json;

namespace DesktopPet.Core;

public sealed class PetNotification
{
    public string Title { get; set; } = "알림";
    public string Message { get; set; } = "";
    public string Mood { get; set; } = "gentle";
    public string Source { get; set; } = "";
    public string EventId { get; set; } = "";
    public DateTimeOffset? CreatedAtUtc { get; set; }
}

/// <summary>A notification stays on disk until the user acknowledges this delivery.</summary>
public sealed class InboxNotification
{
    private readonly Func<bool> _acknowledge;

    internal InboxNotification(PetNotification notification, Func<bool> acknowledge)
    {
        Notification = notification;
        _acknowledge = acknowledge;
    }

    public PetNotification Notification { get; }
    public bool Acknowledge() => _acknowledge();
}

/// <summary>
/// Reads atomically published JSON files. Subscribe before Start; unread and unacknowledged
/// deliveries survive app restarts. File reads and retries never hold the receipt-state lock.
/// </summary>
public sealed class NotificationInbox : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly FileSystemWatcher _watcher;
    private readonly Timer _sweepTimer;
    private readonly string _folder;
    private readonly object _sync = new();
    // Serializes the callback boundary with Dispose, without holding the state lock during UI dispatch.
    private readonly object _deliveryGate = new();
    private readonly Dictionary<string, DeliveryState> _delivered = new(StringComparer.OrdinalIgnoreCase);
    private int _sweeping;
    private bool _started;
    private bool _disposed;
    private bool _resourcesDisposed;

    private sealed class DeliveryState(string path)
    {
        public string Path { get; } = path;
        public object Sync { get; } = new();
        public bool Acknowledged { get; set; }
    }

    /// <summary>Runs on a background thread after Start. Dispatch UI work asynchronously and return promptly.</summary>
    public event Action<InboxNotification>? NotificationReceived;

    public static string DefaultFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopPet", "inbox");

    public NotificationInbox(string? folder = null)
    {
        _folder = Path.GetFullPath(folder ?? DefaultFolder);
        Directory.CreateDirectory(_folder);
        _watcher = new FileSystemWatcher(_folder, "*.json")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
        };
        _watcher.Created += (_, _) => RequestSweep();
        _watcher.Changed += (_, _) => RequestSweep();
        _watcher.Renamed += (_, _) => RequestSweep();
        _watcher.Error += (_, _) => RequestSweep();
        _sweepTimer = new Timer(_ => SweepFolder(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started) return;
            if (NotificationReceived is null)
                throw new InvalidOperationException("알림 수신기를 등록한 뒤 Start를 호출하세요.");

            _started = true;
            _watcher.EnableRaisingEvents = true;
            _sweepTimer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(5));
        }
    }

    private void RequestSweep()
    {
        lock (_sync)
        {
            if (_started && !_disposed)
                _sweepTimer.Change(TimeSpan.FromMilliseconds(80), TimeSpan.FromSeconds(5));
        }
    }

    private void SweepFolder()
    {
        if (Interlocked.CompareExchange(ref _sweeping, 1, 0) != 0) return;
        try
        {
            if (!CanDeliver()) return;
            try
            {
                Directory.CreateDirectory(_folder);
                foreach (var path in Directory.GetFiles(_folder, "*.json").OrderBy(p => p, StringComparer.Ordinal))
                {
                    lock (_sync)
                    {
                        if (_disposed) return;
                        if (_delivered.ContainsKey(path)) continue;
                    }
                    TryDeliver(path);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Trace.TraceWarning($"Notification sweep: {ex.Message}");
            }
        }
        finally { Volatile.Write(ref _sweeping, 0); }
    }

    private bool CanDeliver()
    {
        lock (_sync) return _started && !_disposed && NotificationReceived is not null;
    }

    private void TryDeliver(string path)
    {
        // Non-atomic senders may still be writing. Never delete a file before parsing it.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (!CanDeliver()) return;
            try
            {
                PetNotification notification;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                {
                    using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
                    notification = JsonSerializer.Deserialize<PetNotification>(reader.ReadToEnd(), JsonOptions)
                        ?? throw new JsonException("알림 내용이 null입니다.");
                }
                lock (_deliveryGate)
                {
                    Action<InboxNotification>? receiver;
                    var state = new DeliveryState(path);
                    lock (_sync)
                    {
                        if (_disposed || !_started || _delivered.ContainsKey(path)) return;
                        receiver = NotificationReceived;
                        if (receiver is null) return;
                        _delivered.Add(path, state);
                    }
                    try
                    {
                        receiver(new InboxNotification(notification, () => Acknowledge(state)));
                    }
                    catch (Exception ex)
                    {
                        lock (_sync)
                        {
                            if (_delivered.TryGetValue(path, out var current) && ReferenceEquals(current, state))
                                _delivered.Remove(path);
                        }
                        Trace.TraceError($"Notification delivery: {ex}");
                    }
                }
                return;
            }
            catch (FileNotFoundException) { return; }
            catch (DirectoryNotFoundException) { return; }
            catch (JsonException)
            {
                if (attempt == 4)
                {
                    if (CanDeliver()) Quarantine(path);
                    return;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt == 4) return; // Leave it for the next sweep.
            }
            Thread.Sleep(60);
        }
    }

    private bool Acknowledge(DeliveryState state)
    {
        // A receipt remains idempotent even after another notification reuses its file name.
        // Only acknowledgements for this receipt serialize; slow storage never holds _sync.
        lock (state.Sync)
        {
            if (state.Acknowledged) return true;
            try
            {
                File.Delete(state.Path);
                state.Acknowledged = true;
                lock (_sync)
                {
                    if (_delivered.TryGetValue(state.Path, out var current) && ReferenceEquals(current, state))
                        _delivered.Remove(state.Path);
                }
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Trace.TraceWarning($"Notification acknowledgement: {ex.Message}");
                return false;
            }
        }
    }

    private void Quarantine(string path)
    {
        var invalidFolder = Path.Combine(_folder, "invalid");
        Directory.CreateDirectory(invalidFolder);
        File.Move(path, Path.Combine(invalidFolder, $"{Path.GetFileNameWithoutExtension(path)}-{Guid.NewGuid():N}.json"));
    }

    public void Dispose()
    {
        // Mark stopped before waiting for an already-running callback, so no later delivery can win the gate.
        lock (_sync) _disposed = true;
        lock (_deliveryGate)
        {
            if (_resourcesDisposed) return;
            _resourcesDisposed = true;
            _sweepTimer.Dispose();
            _watcher.Dispose();
        }
    }
}
