using System.Collections.Concurrent;
using DesktopPet.Core;

public static class FocusTimerTests
{
    public static IEnumerable<(string Name, Action Run)> Create()
    {
        yield return ("Focus timer starts idle with a 25-minute focus and manual 5-minute break", () =>
        {
            var clock = new Clock();
            using var timer = new FocusTimer(clock);
            Check(timer.Snapshot.Status == FocusTimerStatus.Stopped && timer.Snapshot.Remaining == TimeSpan.FromMinutes(25), "Timer started automatically or changed its focus default");
            timer.Start(FocusSessionKind.Break);
            Check(timer.Snapshot.Status == FocusTimerStatus.Running && timer.Snapshot.Remaining == TimeSpan.FromMinutes(5), "Manual break did not use its five-minute default");
        });

        yield return ("A delayed resume completes exactly one session without auto-running unattended follow-up cycles", () =>
        {
            var clock = new Clock();
            using var timer = Timer(clock);
            var completions = new List<FocusSessionCompletion>();
            timer.SessionCompleted += completions.Add;
            timer.Start();
            clock.Advance(TimeSpan.FromHours(8));
            for (var i = 0; i < 20; i++) timer.Tick();
            Check(completions.Count == 1 && timer.Snapshot.Status == FocusTimerStatus.Completed && timer.Snapshot.Remaining == TimeSpan.Zero,
                "Missed ticks generated duplicate or auto-cycled completions");
            Check(timer.Snapshot.Session == FocusSessionKind.Focus && timer.Snapshot.CompletedFocusSessions == 1, "Completed focus was not retained for manual next-session selection");
            timer.Start(FocusSessionKind.Break);
            clock.Advance(TimeSpan.FromSeconds(5));
            timer.Tick();
            Check(completions.Count == 2 && completions[1].Session == FocusSessionKind.Break && timer.Snapshot.CompletedFocusSessions == 1,
                "A manually started break was not completed exactly once");
        });

        yield return ("Pause freezes remaining time and resume excludes the entire paused interval", () =>
        {
            var clock = new Clock();
            using var timer = Timer(clock);
            timer.Start();
            clock.Advance(TimeSpan.FromSeconds(3));
            timer.Pause();
            Check(timer.Snapshot.Status == FocusTimerStatus.Paused && timer.Snapshot.Remaining == TimeSpan.FromSeconds(7), "Pause lost elapsed active time");
            clock.Advance(TimeSpan.FromHours(2));
            timer.Tick();
            Check(timer.Snapshot.Remaining == TimeSpan.FromSeconds(7), "Paused time reduced the remaining interval");
            timer.Resume();
            clock.Advance(TimeSpan.FromSeconds(6));
            timer.Tick();
            Check(timer.Snapshot.Remaining == TimeSpan.FromSeconds(1), "Resume counted paused time or reset the session");
            clock.Advance(TimeSpan.FromSeconds(1));
            timer.Tick();
            Check(timer.Snapshot.Status == FocusTimerStatus.Completed, "Resumed timer missed its exact completion boundary");
        });

        yield return ("Pause after an elapsed deadline completes instead of hiding a due notification", () =>
        {
            var clock = new Clock();
            using var timer = Timer(clock);
            var count = 0;
            timer.SessionCompleted += _ => count++;
            timer.Start();
            clock.Advance(TimeSpan.FromSeconds(11));
            timer.Pause();
            timer.Resume();
            timer.Tick();
            Check(timer.Snapshot.Status == FocusTimerStatus.Completed && count == 1, "Late pause swallowed or duplicated the completion");
        });

        yield return ("Restart resets the current session and stop cancels it without a completion alert", () =>
        {
            var clock = new Clock();
            using var timer = Timer(clock);
            var count = 0;
            timer.SessionCompleted += _ => count++;
            timer.Start(FocusSessionKind.Break);
            clock.Advance(TimeSpan.FromSeconds(2));
            timer.Pause();
            timer.Restart();
            Check(timer.Snapshot.Session == FocusSessionKind.Break && timer.Snapshot.Status == FocusTimerStatus.Running &&
                timer.Snapshot.Remaining == TimeSpan.FromSeconds(5), "Restart changed the session kind or retained old elapsed time");
            timer.Stop();
            clock.Advance(TimeSpan.FromDays(1));
            timer.Tick();
            Check(count == 0 && timer.Snapshot.Status == FocusTimerStatus.Stopped, "Stopped timer emitted a completion");
        });

        yield return ("Wall-clock corrections do not change the monotonic countdown", () =>
        {
            var clock = new Clock();
            using var timer = Timer(clock);
            timer.Start();
            clock.ShiftWallClock(TimeSpan.FromDays(10));
            timer.Tick();
            Check(timer.Snapshot.Remaining == TimeSpan.FromSeconds(10), "Forward wall-clock correction ended focus early");
            clock.ShiftWallClock(TimeSpan.FromDays(-20));
            clock.Advance(TimeSpan.FromSeconds(2));
            timer.Tick();
            Check(timer.Snapshot.Remaining == TimeSpan.FromSeconds(8), "Backward wall-clock correction lengthened the focus interval");
        });

        yield return ("A regressed custom timestamp cannot add time or fire a premature completion", () =>
        {
            var clock = new Clock();
            using var timer = Timer(clock);
            timer.Start();
            clock.Advance(TimeSpan.FromSeconds(2));
            timer.Tick();
            clock.MoveTimestamp(TimeSpan.FromSeconds(-1));
            timer.Tick();
            Check(timer.Snapshot.Remaining == TimeSpan.FromSeconds(8), "Regressed timestamp changed remaining time");
            clock.MoveTimestamp(TimeSpan.FromSeconds(3));
            timer.Tick();
            Check(timer.Snapshot.Remaining == TimeSpan.FromSeconds(6), "Timestamp recovery counted the regressed interval twice");
        });

        yield return ("Disposal cancels a running focus timer and suppresses every later tick", () =>
        {
            var clock = new Clock();
            var timer = Timer(clock);
            var count = 0;
            timer.SessionCompleted += _ => count++;
            timer.Start();
            timer.Dispose();
            timer.Dispose();
            clock.Advance(TimeSpan.FromDays(1));
            timer.Tick();
            Check(count == 0 && timer.Snapshot.Status == FocusTimerStatus.Stopped, "A disposed timer emitted an alert");
            Expect<ObjectDisposedException>(() => timer.Start());
        });

        yield return ("Invalid focus intervals and session kinds fail before changing timer state", () =>
        {
            foreach (var value in new[] { TimeSpan.Zero, TimeSpan.FromTicks(-1), TimeSpan.FromDays(2) })
            {
                Expect<ArgumentOutOfRangeException>(() => new FocusTimer(focusDuration: value));
                Expect<ArgumentOutOfRangeException>(() => new FocusTimer(breakDuration: value));
            }
            using var timer = new FocusTimer();
            Expect<ArgumentOutOfRangeException>(() => timer.Start((FocusSessionKind)999));
            Check(timer.Snapshot.Status == FocusTimerStatus.Stopped, "Invalid start changed the timer state");
        });

        yield return ("Local notification publishing stays durable and uses existing inbox acknowledgement", () => InFolder(folder =>
        {
            var first = NotificationWriter.Publish(folder, new PetNotification { Source = "focus-timer", EventId = "focus-1", Mood = "gentle", Title = "집중 완료" });
            var second = NotificationWriter.Publish(folder, new PetNotification { Source = "test", EventId = "test-1", Mood = "gentle", Title = "테스트 알림" });
            Check(first != second && Directory.GetFiles(folder, "*.tmp").Length == 0 && Directory.GetFiles(folder, "*.json").Length == 2,
                "Publishing reused a filename or left partial JSON");
            var deliveries = new ConcurrentQueue<InboxNotification>();
            using var inbox = new NotificationInbox(folder);
            inbox.NotificationReceived += deliveries.Enqueue;
            inbox.Start();
            Check(SpinWait.SpinUntil(() => deliveries.Count == 2, TimeSpan.FromSeconds(5)), "Published timer/test receipts were not delivered");
            var focus = deliveries.Single(delivery => delivery.Notification.Source == "focus-timer");
            Check(focus.Notification.Mood == "gentle" && focus.Acknowledge(), "Timer receipt lost its gentle mood or acknowledgement");
            Check(!File.Exists(first) && File.Exists(second), "Acknowledgement deleted another notification or lost unread receipts");
        }));

        yield return ("Notification storage failure preserves unrelated data without partial receipts", () => InFolder(folder =>
        {
            var occupied = Path.Combine(folder, "occupied");
            File.WriteAllText(occupied, "keep");
            Expect<IOException>(() => NotificationWriter.Publish(occupied, new PetNotification()));
            Expect<ArgumentException>(() => NotificationWriter.Publish("relative-inbox", new PetNotification()));
            Check(File.ReadAllText(occupied) == "keep" && Directory.GetFiles(folder, "*.tmp").Length == 0,
                "Storage failure overwrote existing content or left a partial receipt");
        }));
    }

    private static FocusTimer Timer(Clock clock) => new(clock, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5));
    private sealed class Clock : TimeProvider
    {
        private long _timestamp;
        private DateTimeOffset _utc = new(2026, 11, 14, 0, 0, 0, TimeSpan.Zero);
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;
        public override DateTimeOffset GetUtcNow() => _utc;
        public void Advance(TimeSpan elapsed) { _timestamp += elapsed.Ticks; _utc += elapsed; }
        public void ShiftWallClock(TimeSpan difference) => _utc += difference;
        public void MoveTimestamp(TimeSpan difference) => _timestamp += difference.Ticks;
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}");
    }
    private static void InFolder(Action<string> action)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
        var folder = Path.Combine(root, "DesktopPet-Focus-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try { action(folder); }
        finally
        {
            var full = Path.GetFullPath(folder);
            Check(full.StartsWith(root, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(full).StartsWith("DesktopPet-Focus-"), "Unsafe fixture cleanup");
            Directory.Delete(full, recursive: true);
        }
    }
}
