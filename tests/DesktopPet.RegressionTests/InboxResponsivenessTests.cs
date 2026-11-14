using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using DesktopPet.Core;

public static class InboxResponsivenessTests
{
    public static IEnumerable<(string Name, Action Run)> Create()
    {
        yield return ("Acknowledgement does not wait for malformed notification backlog", () => InFolder(folder =>
        {
            Write(folder, "000", "first");
            WriteMalformedBacklog(folder);
            using var inbox = new NotificationInbox(folder);
            var received = Subscribe(inbox);
            inbox.Start();
            Until(() => received.Count == 1);
            // The sweep now proceeds into the malformed backlog, whose retry budget exceeds 2 seconds.
            var acknowledgement = Task.Run(() => received.First().Acknowledge());
            Check(acknowledgement.Wait(TimeSpan.FromMilliseconds(750)), "Acknowledgement waited for background retry sleeps");
            Check(acknowledgement.Result, "Acknowledgement failed");
            Check(!File.Exists(Path.Combine(folder, "000.json")), "Acknowledged notification remains");
        }));

        yield return ("Disposal skips unread backlog without late notification callbacks", () => InFolder(folder =>
        {
            Write(folder, "000", "first");
            WriteMalformedBacklog(folder);
            Write(folder, "999", "must remain unread");
            var inbox = new NotificationInbox(folder);
            var received = Subscribe(inbox);
            try
            {
                inbox.Start();
                Until(() => received.Count == 1);
                var elapsed = Stopwatch.StartNew();
                inbox.Dispose();
                Check(elapsed.Elapsed < TimeSpan.FromMilliseconds(750), "Disposal waited for background retry sleeps");
                Thread.Sleep(350);
                Check(received.Count == 1, "A notification was delivered after disposal returned");
                Check(File.Exists(Path.Combine(folder, "000.json")) && File.Exists(Path.Combine(folder, "999.json")),
                    "Disposal removed unacknowledged or unread notifications");
            }
            finally { inbox.Dispose(); }
        }));

        yield return ("Concurrent acknowledgements are idempotent after a file name is reused", () => InFolder(folder =>
        {
            Write(folder, "000", "first");
            using var inbox = new NotificationInbox(folder);
            var received = Subscribe(inbox);
            inbox.Start();
            Until(() => received.Count == 1);
            var first = received.First();
            var results = new ConcurrentBag<bool>();
            Parallel.For(0, 20, _ => results.Add(first.Acknowledge()));
            Check(results.All(success => success), "Concurrent acknowledgement failed");
            Write(folder, "000", "second");
            Until(() => received.Count == 2);
            Check(first.Acknowledge(), "Previously acknowledged receipt is no longer idempotent");
            Check(File.Exists(Path.Combine(folder, "000.json")), "Old receipt deleted a newer notification");
            Check(received.Last().Acknowledge(), "New receipt could not be acknowledged");
        }));

        yield return ("Disposal waits only for an active callback and prevents the next callback", () => InFolder(folder =>
        {
            Write(folder, "000", "first");
            Write(folder, "001", "second");
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var callbacks = 0;
            var inbox = new NotificationInbox(folder);
            Task? disposal = null;
            inbox.NotificationReceived += delivery =>
            {
                Interlocked.Increment(ref callbacks);
                // Receivers may acknowledge synchronously; this must not contend with the sweep state lock.
                delivery.Acknowledge();
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(5));
            };
            try
            {
                inbox.Start();
                Check(entered.Wait(TimeSpan.FromSeconds(5)), "Callback did not start");
                disposal = Task.Run(inbox.Dispose);
                Check(!disposal.Wait(TimeSpan.FromMilliseconds(80)), "Disposal returned while a callback was active");
                release.Set();
                Check(disposal.Wait(TimeSpan.FromSeconds(2)), "Disposal did not finish after the callback returned");
                Thread.Sleep(150);
                Check(Volatile.Read(ref callbacks) == 1, "Next callback ran after disposal");
            }
            finally
            {
                release.Set();
                disposal?.Wait(TimeSpan.FromSeconds(5));
                inbox.Dispose();
            }
        }));
    }

    private static ConcurrentQueue<InboxNotification> Subscribe(NotificationInbox inbox)
    {
        var received = new ConcurrentQueue<InboxNotification>();
        inbox.NotificationReceived += received.Enqueue;
        return received;
    }

    private static void WriteMalformedBacklog(string folder)
    {
        for (var i = 1; i <= 12; i++) File.WriteAllText(Path.Combine(folder, $"{i:D3}.json"), "{");
    }

    private static void Write(string folder, string id, string message)
    {
        var temporary = Path.Combine(folder, id + ".tmp");
        File.WriteAllText(temporary, JsonSerializer.Serialize(new PetNotification { Message = message }));
        File.Move(temporary, Path.Combine(folder, id + ".json"));
    }

    private static void Until(Func<bool> condition)
    {
        Check(SpinWait.SpinUntil(condition, TimeSpan.FromSeconds(5)), "Timed out waiting for notification delivery");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void InFolder(Action<string> run)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
        var folder = Path.Combine(root, "DesktopPet-InboxResponsiveness-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try { run(folder); }
        finally
        {
            var target = Path.GetFullPath(folder);
            Check(target.StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
                  Path.GetFileName(target).StartsWith("DesktopPet-InboxResponsiveness-", StringComparison.Ordinal), "Unsafe cleanup path");
            Directory.Delete(target, recursive: true);
        }
    }
}
