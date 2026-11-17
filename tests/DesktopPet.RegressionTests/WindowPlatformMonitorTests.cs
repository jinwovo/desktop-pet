using DesktopPet.Core;

public static class WindowPlatformMonitorTests
{
    public static IEnumerable<(string Name, Action Run)> Create()
    {
        yield return ("Platform monitor starts paused and requests fresh work only while visible", () =>
        {
            var clock = new Clock();
            var scans = 0;
            var published = new List<IReadOnlyList<Platform>>();
            using var monitor = new WindowPlatformMonitor(() => Snapshot(++scans), published.Add, clock);
            clock.Timer.FireQueued();
            Check(scans == 0 && clock.Timer.Due == Timeout.InfiniteTimeSpan, "Constructor or queued paused callback scanned");
            monitor.Resume();
            Check(scans == 0 && clock.Timer.Due == TimeSpan.Zero, "Resume blocked the caller on scanning or delayed its request");
            clock.Timer.FireQueued();
            Check(scans == 1 && published.Count == 1 && clock.Timer.Due == TimeSpan.FromMilliseconds(300), "Fresh snapshot or completion-based cadence missing");
            monitor.Pause();
            for (var i = 0; i < 10; i++) clock.Timer.FireQueued();
            Check(scans == 1 && published.Count == 1 && clock.Timer.Due == Timeout.InfiniteTimeSpan, "Hidden callbacks continued scanning");
            monitor.Resume();
            Check(clock.Timer.Due == TimeSpan.Zero, "Showing the pet did not immediately request fresh platforms");
            clock.Timer.FireQueued();
            Check(scans == 2 && published.Last()[0].GroundY == 2, "Resume reused stale platforms");
        });

        yield return ("Repeated visibility transitions do not postpone work or restart hidden timers", () =>
        {
            var clock = new Clock();
            using var monitor = new WindowPlatformMonitor(() => Snapshot(1), _ => { }, clock);
            monitor.Resume();
            var changes = clock.Timer.Changes;
            for (var i = 0; i < 20; i++) monitor.Resume();
            Check(clock.Timer.Changes == changes, "Repeated show postponed the immediate request");
            monitor.Pause();
            changes = clock.Timer.Changes;
            for (var i = 0; i < 20; i++) monitor.Pause();
            Check(clock.Timer.Changes == changes && clock.Timer.Due == Timeout.InfiniteTimeSpan, "Repeated hide armed work");
        });

        yield return ("Hide-show during a slow scan discards its snapshot and never overlaps native work", () =>
        {
            var clock = new Clock();
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var calls = 0;
            var published = new List<IReadOnlyList<Platform>>();
            using var monitor = new WindowPlatformMonitor(() =>
            {
                var call = Interlocked.Increment(ref calls);
                if (call == 1) { entered.Set(); Check(release.Wait(TimeSpan.FromSeconds(10)), "Slow scan was not released"); }
                return Snapshot(call);
            }, published.Add, clock);
            monitor.Resume();
            var slow = Task.Run(clock.Timer.FireQueued);
            try
            {
                Check(entered.Wait(TimeSpan.FromSeconds(5)), "Scan did not begin");
                monitor.Pause();
                monitor.Resume();
                // Timer callbacks already queued by the OS may still run after disabling it.
                clock.Timer.FireQueued();
                clock.Timer.FireQueued();
                Check(calls == 1, "Native scans overlapped during resume");
            }
            finally { release.Set(); Finish(slow); }
            Check(published.Count == 0 && clock.Timer.Due == TimeSpan.Zero, "Old generation published or delayed fresh scan");
            clock.Timer.FireQueued();
            Check(calls == 2 && published.Count == 1 && published[0][0].GroundY == 2, "Fresh generation was not exclusively published");
        });

        yield return ("Pause suppresses the completion and rescheduling of a blocked scan", () =>
        {
            var clock = new Clock();
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var publications = 0;
            using var monitor = new WindowPlatformMonitor(() =>
            {
                entered.Set(); Check(release.Wait(TimeSpan.FromSeconds(10)), "Scan was not released"); return Snapshot(1);
            }, _ => publications++, clock);
            monitor.Resume();
            var slow = Task.Run(clock.Timer.FireQueued);
            try { Check(entered.Wait(TimeSpan.FromSeconds(5)), "Scan did not begin"); monitor.Pause(); }
            finally { release.Set(); Finish(slow); }
            Check(publications == 0 && clock.Timer.Due == Timeout.InfiniteTimeSpan, "Paused scan published or rearmed the timer");
        });

        yield return ("Disposal returns while native scanning is blocked and forbids late publication", () =>
        {
            var clock = new Clock();
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var publications = 0;
            using var monitor = new WindowPlatformMonitor(() =>
            {
                entered.Set(); Check(release.Wait(TimeSpan.FromSeconds(10)), "Scan was not released"); return Snapshot(1);
            }, _ => publications++, clock);
            monitor.Resume();
            var slow = Task.Run(clock.Timer.FireQueued);
            try
            {
                Check(entered.Wait(TimeSpan.FromSeconds(5)), "Scan did not begin");
                Finish(Task.Run(monitor.Dispose));
                Check(clock.Timer.Disposed, "Timer was not disposed before native scan finished");
            }
            finally { release.Set(); Finish(slow); }
            var changes = clock.Timer.Changes;
            monitor.Resume(); monitor.Pause(); monitor.Dispose(); clock.Timer.FireQueued();
            Check(publications == 0 && clock.Timer.Changes == changes, "Disposed callback published, rearmed or accessed the timer");
        });

        yield return ("Pause serializes with publication and leaves no publication after returning", () =>
        {
            var clock = new Clock();
            using var publishing = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            using var pausing = new ManualResetEventSlim();
            var publications = 0;
            using var monitor = new WindowPlatformMonitor(() => Snapshot(1), _ =>
            {
                publishing.Set(); Check(release.Wait(TimeSpan.FromSeconds(10)), "Publisher was not released"); publications++;
            }, clock);
            monitor.Resume();
            var scan = Task.Run(clock.Timer.FireQueued);
            Task? pause = null;
            try
            {
                Check(publishing.Wait(TimeSpan.FromSeconds(5)), "Publication did not begin");
                pause = Task.Run(() => { pausing.Set(); monitor.Pause(); });
                Check(pausing.Wait(TimeSpan.FromSeconds(5)) && !pause.IsCompleted, "Pause returned while publication was active");
            }
            finally { release.Set(); Finish(scan); if (pause is not null) Finish(pause); }
            clock.Timer.FireQueued();
            Check(publications == 1 && clock.Timer.Due == Timeout.InfiniteTimeSpan, "Publication occurred after Pause returned");
        });

        yield return ("Scanner and publisher failures recover on the next non-overlapping interval", () =>
        {
            var clock = new Clock();
            var calls = 0;
            var published = 0;
            using var monitor = new WindowPlatformMonitor(() => ++calls == 1 ? throw new IOException("fixture") : Snapshot(calls),
                _ => { if (calls == 2) throw new InvalidOperationException("fixture"); published++; }, clock, TimeSpan.FromSeconds(2));
            monitor.Resume();
            for (var i = 0; i < 3; i++)
            {
                clock.Timer.FireQueued();
                Check(clock.Timer.Due == TimeSpan.FromSeconds(2), "Failure lost the configured retry cadence");
            }
            Check(calls == 3 && published == 1, "A failed callback disabled future scans");
        });

        yield return ("Invalid scan intervals are rejected before creating any timer", () =>
        {
            var clock = new Clock();
            foreach (var interval in new[] { TimeSpan.Zero, TimeSpan.FromMilliseconds(-1), TimeSpan.FromTicks(1), TimeSpan.FromMilliseconds(uint.MaxValue) })
            {
                try { using var _ = new WindowPlatformMonitor(() => Snapshot(1), _ => { }, clock, interval); throw new Exception("Invalid interval accepted"); }
                catch (ArgumentOutOfRangeException) { }
            }
            Check(clock.Created == 0, "Rejected configuration created a timer");
        });

        yield return ("Production platform timer performs scanning on a background thread", () =>
        {
            var callerThread = Environment.CurrentManagedThreadId;
            var scannerThread = 0;
            using var published = new ManualResetEventSlim();
            using var monitor = new WindowPlatformMonitor(() => { scannerThread = Environment.CurrentManagedThreadId; return Snapshot(1); }, _ => published.Set());
            monitor.Resume();
            Check(published.Wait(TimeSpan.FromSeconds(5)), "Production timer did not publish");
            Check(scannerThread != callerThread, "Native enumeration ran synchronously on the caller");
        });
    }

    private static IReadOnlyList<Platform> Snapshot(int sequence) => new[] { new Platform(0, 1000, sequence) };
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Finish(Task task) { Check(task.Wait(TimeSpan.FromSeconds(5)), "Blocked task timed out"); task.GetAwaiter().GetResult(); }

    private sealed class Clock : TimeProvider
    {
        public int Created;
        public ManualTimer Timer = null!;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Created++;
            return Timer = new ManualTimer(callback, state, dueTime);
        }
    }

    private sealed class ManualTimer(TimerCallback callback, object? state, TimeSpan due) : ITimer
    {
        public TimeSpan Due { get; private set; } = due;
        public int Changes { get; private set; }
        public bool Disposed { get; private set; }
        public void FireQueued() => callback(state);
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            ObjectDisposedException.ThrowIf(Disposed, this);
            Check(period == Timeout.InfiniteTimeSpan, "Monitor used overlapping periodic callbacks");
            Due = dueTime; Changes++; return true;
        }
        public void Dispose() => Disposed = true;
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
