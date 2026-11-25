using DesktopPet.App;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;

internal static class SavedNotificationExportWriterTests
{
    public static void Run(string root, IReadOnlyList<SavedNotificationItem> snapshot)
    {
        if (snapshot.Count != 2) throw new Exception("fixture requires two entries");

        string path = Path.Combine(root, "export.json");
        SavedNotificationJsonWriter.Save(snapshot, path, CancellationToken.None);
        var existing = File.ReadAllBytes(path);
        Expect<IOException>(() => SavedNotificationJsonWriter.Save(snapshot, path, CancellationToken.None));
        if (!File.ReadAllBytes(path).SequenceEqual(existing)) throw new Exception("existing export was overwritten");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        var cancelledPath = Path.Combine(root, "writer-cancelled.json");
        Expect<OperationCanceledException>(() => SavedNotificationJsonWriter.Save(snapshot, cancelledPath, cancelled.Token));
        if (File.Exists(cancelledPath)) throw new Exception("canceled save must not create destination");

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var rootEl = doc.RootElement;
        if (rootEl.GetProperty("schemaVersion").GetInt32() != 1) throw new Exception("schemaVersion");
        var notifs = rootEl.GetProperty("notifications");
        if (notifs.GetArrayLength() != 2) throw new Exception("notificationsCount");

        for (int i = 0; i < 2; i++)
        {
            var el = notifs[i];
            if (el.ValueKind != JsonValueKind.Object) throw new Exception("item not object");
            if (el.EnumerateObject().Count() != 4) throw new Exception("extra properties");
            foreach (var prop in new[] { "title", "message", "source", "createdAtUtc" })
                if (!el.TryGetProperty(prop, out _)) throw new Exception($"missing {prop}");

            var item = snapshot[i];
            if (el.GetProperty("title").GetString() != item.Title) throw new Exception("title mismatch");
            if (el.GetProperty("message").GetString() != item.Message) throw new Exception("message mismatch");
            if (el.GetProperty("source").GetString() != item.Source) throw new Exception("source mismatch");

            var created = el.GetProperty("createdAtUtc");
            if (item.CreatedAtUtc.HasValue)
            {
                if (created.ValueKind != JsonValueKind.String) throw new Exception("createdAtUtc not string");
                var dt = DateTimeOffset.Parse(created.GetString()!);
                if (dt.UtcDateTime != item.CreatedAtUtc.Value.UtcDateTime) throw new Exception("createdAtUtc mismatch");
            }
            else
            {
                if (created.ValueKind != JsonValueKind.Null) throw new Exception("createdAtUtc not null");
            }
        }

        string[] tmpBefore = Directory.GetFiles(root, "DesktopPet-export-*.tmp");

        using var cts = new CancellationTokenSource();
        var cancelList = new CancelOnEnumerateList(snapshot, cts);
        var midPath = Path.Combine(root, "writer-midwrite.json");
        Expect<OperationCanceledException>(() => SavedNotificationJsonWriter.Save(cancelList, midPath, cts.Token));
        if (File.Exists(midPath)) throw new Exception("midwrite cancel must not publish");
        string[] tmpAfter = Directory.GetFiles(root, "DesktopPet-export-*.tmp");
        if (tmpAfter.Length != tmpBefore.Length) throw new Exception("temp files not cleaned");

        var publicationFailed = false;
        try
        {
            SavedNotificationJsonWriter.Save(snapshot, root, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { publicationFailed = true; }
        if (!publicationFailed || Directory.GetFiles(root, "DesktopPet-export-*.tmp").Length != tmpBefore.Length)
            throw new Exception("failed publication did not clean its owned temporary file");
        Console.WriteLine("PASS JSON writer preserves exactly four raw fields, protects existing files and cleans owned temp files on pre/mid-write cancellation and failed publication");
    }

    static void Expect<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        catch (Exception ex) when (typeof(TException).IsAssignableFrom(ex.GetType())) { return; }
        throw new Exception($"expected {typeof(TException).Name}");
    }

    sealed class CancelOnEnumerateList : IReadOnlyList<SavedNotificationItem>
    {
        readonly IReadOnlyList<SavedNotificationItem> _inner;
        readonly CancellationTokenSource _cts;
        public CancelOnEnumerateList(IReadOnlyList<SavedNotificationItem> inner, CancellationTokenSource cancellation)
        {
            _inner = inner;
            _cts = cancellation;
        }
        public int Count => _inner.Count;
        public SavedNotificationItem this[int index] => _inner[index];
        public IEnumerator<SavedNotificationItem> GetEnumerator()
        {
            _cts.Cancel();
            foreach (var item in _inner) yield return item;
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
