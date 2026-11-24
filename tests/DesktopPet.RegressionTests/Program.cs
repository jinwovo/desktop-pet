using System.Collections.Concurrent;
using System.Text.Json;
using DesktopPet.Core;

// No test-package dependencies: run with dotnet run --project tests/DesktopPet.RegressionTests.
var tests = new (string Name, Action Run)[]
{
    ("Startup waits for subscription; backlog is ordered and durable", () => InTemporaryFolder(folder =>
    {
        WriteNotification(folder, "002", "second");
        WriteNotification(folder, "001", "first");
        using var inbox = new NotificationInbox(folder);
        Check(Directory.GetFiles(folder, "*.json").Length == 2, "Constructor consumed backlog");
        var received = Subscribe(inbox);
        inbox.Start();
        inbox.Start(); // Idempotent.
        Until(() => received.Count == 2);
        Check(received.Select(n => n.Notification.Message).SequenceEqual(new[] { "first", "second" }), "Wrong order");
        Check(Directory.GetFiles(folder, "*.json").Length == 2, "Deleted before acknowledgement");
        Check(received.First().Acknowledge(), "Acknowledgement failed");
        Check(!File.Exists(Path.Combine(folder, "001.json")), "Acknowledged file remains");
    })),
    ("Start without a receiver leaves messages untouched", () => InTemporaryFolder(folder =>
    {
        WriteNotification(folder, "001", "saved");
        using var inbox = new NotificationInbox(folder);
        Throws<InvalidOperationException>(inbox.Start);
        Check(File.Exists(Path.Combine(folder, "001.json")), "Lost unobserved notification");
    })),
    ("Unacknowledged notification returns after restart", () => InTemporaryFolder(folder =>
    {
        WriteNotification(folder, "001", "survives");
        using (var first = new NotificationInbox(folder))
        {
            var received = Subscribe(first);
            first.Start();
            Until(() => received.Count == 1);
        }
        using var second = new NotificationInbox(folder);
        var replayed = Subscribe(second);
        second.Start();
        Until(() => replayed.Count == 1);
        Check(replayed.Single().Acknowledge(), "Could not acknowledge replay");
    })),
    ("Watcher updates do not redeliver the same file", () => InTemporaryFolder(folder =>
    {
        WriteNotification(folder, "001", "once");
        using var inbox = new NotificationInbox(folder);
        var received = Subscribe(inbox);
        inbox.Start();
        Until(() => received.Count == 1);
        File.SetLastWriteTimeUtc(Path.Combine(folder, "001.json"), DateTime.UtcNow.AddSeconds(1));
        WriteNotification(folder, "002", "barrier");
        Until(() => received.Count == 2);
        Check(received.Count(n => n.Notification.Message == "once") == 1, "Duplicate delivery");
    })),
    ("Atomic rename is detected and malformed files are quarantined", () => InTemporaryFolder(folder =>
    {
        File.WriteAllText(Path.Combine(folder, "001.json"), "{broken");
        File.WriteAllText(Path.Combine(folder, "002.json"), "null");
        using var inbox = new NotificationInbox(folder);
        var received = Subscribe(inbox);
        inbox.Start();
        var temporary = Path.Combine(folder, "003.tmp");
        File.WriteAllText(temporary, "{\"TITLE\":\"작업 완료\",\"MESSAGE\":\"한글 알림\",\"MOOD\":\"cry\"}");
        File.Move(temporary, Path.Combine(folder, "003.json"));
        Until(() => received.Count == 1 && Directory.Exists(Path.Combine(folder, "invalid")) &&
                    Directory.GetFiles(Path.Combine(folder, "invalid")).Length == 2);
        Check(received.Single().Notification.Message == "한글 알림", "Unicode/case-insensitive JSON failed");
        Check(received.Single().Notification.Mood == "cry", "Mood not preserved");
    })),
    ("Locked files are retried; failed acknowledgements preserve delivery", () => InTemporaryFolder(folder =>
    {
        WriteNotification(folder, "001", "locked");
        var path = Path.Combine(folder, "001.json");
        using var inbox = new NotificationInbox(folder);
        var received = Subscribe(inbox);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            inbox.Start();
            Thread.Sleep(400);
            Check(received.IsEmpty, "Read a locked file");
        }
        Until(() => received.Count == 1);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            Check(!received.Single().Acknowledge(), "Deleted a locked notification");
        Check(File.Exists(path), "Failed acknowledgement lost file");
        Check(received.Single().Acknowledge(), "Retry acknowledgement failed");
    })),
    ("PowerShell UTF-8 BOM notification remains readable", () => InTemporaryFolder(folder =>
    {
        File.WriteAllText(Path.Combine(folder, "001.json"), "{\"message\":\"BOM 알림\"}", new System.Text.UTF8Encoding(true));
        using var inbox = new NotificationInbox(folder);
        var received = Subscribe(inbox);
        inbox.Start();
        Until(() => received.Count == 1);
        Check(received.Single().Notification.Message == "BOM 알림", "BOM notification unreadable");
    })),
    ("Concurrent senders are delivered once; disposal leaves unread files", () => InTemporaryFolder(folder =>
    {
        var inbox = new NotificationInbox(folder);
        var received = Subscribe(inbox);
        inbox.Start();
        Parallel.For(0, 50, i => WriteNotification(folder, i.ToString("D3"), i.ToString()));
        Until(() => received.Count == 50);
        Check(received.Select(n => n.Notification.Message).Distinct().Count() == 50, "Missing or duplicated message");
        inbox.Dispose();
        inbox.Dispose();
        WriteNotification(folder, "999", "after disposal");
        Check(Directory.GetFiles(folder, "*.json").Length == 51, "Disposal deleted pending files");
        Throws<ObjectDisposedException>(inbox.Start);
    })),
    ("Corrupt settings fall back; unsafe values are normalized", () => InTemporaryFolder(folder =>
    {
        var path = Path.Combine(folder, "settings.json");
        File.WriteAllText(path, "{");
        Check(PetSettings.Load(path).ScaleMultiplier == 1, "Corrupt settings did not fall back");
        File.WriteAllText(path, "{\"CharacterName\":\"../outside\",\"ScaleMultiplier\":-20,\"WalkSpeedMultiplier\":100}");
        var loaded = PetSettings.Load(path);
        Check(loaded.CharacterName == PetSettings.DefaultCharacterName, "Unsafe character name accepted");
        Check(loaded.ScaleMultiplier == 0.5 && loaded.WalkSpeedMultiplier == 3, "Range validation failed");
        loaded.ScaleMultiplier = double.NaN;
        loaded.WalkSpeedMultiplier = double.PositiveInfinity;
        loaded.Save(path);
        var roundtrip = PetSettings.Load(path);
        Check(roundtrip.ScaleMultiplier == 1 && roundtrip.WalkSpeedMultiplier == 1, "Non-finite settings persisted");
    })),
    ("Atomic settings writes preserve previous file on failure", () => InTemporaryFolder(folder =>
    {
        var path = Path.Combine(folder, "settings.json");
        var settings = new PetSettings { CharacterName = "Usagi", ScaleMultiplier = 1.5 };
        settings.Save(path);
        var previous = File.ReadAllText(path);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            settings.ScaleMultiplier = 2;
            try
            {
                settings.Save(path);
                throw new Exception("Expected a locked-file save to fail");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        Check(File.ReadAllText(path) == previous, "Failed save damaged existing settings");
        Check(Directory.GetFiles(folder).Length == 1, "Temporary settings file leaked");
        settings.Save(path);
        Check(PetSettings.Load(path).ScaleMultiplier == 2, "Successful save failed");
    })),
    ("Resizing preserves feet and center without artificial falling", () =>
    {
        var brain = Brain();
        brain.UpdateFrameSize(200, 190);
        Check(brain.X == 50 && brain.Y == 410, "Resize changed feet or center");
        brain.Tick(0.01, Away());
        Check(brain.State != PetState.Falling, "Resize caused falling");
    }),
    ("Disabling sleep wakes a sleeping pet", () =>
    {
        var brain = Brain();
        for (var i = 0; i < 20_000 && brain.State != PetState.Sleep; i++) brain.Tick(0.01, Away());
        Check(brain.State == PetState.Sleep, "Pet never fell asleep");
        brain.SleepEnabled = false;
        brain.Tick(0.01, Away());
        Check(brain.State == PetState.Idle, "Sleep toggle did not wake pet");
    }),
    ("Notification during drag preserves drag and resumes on landing", () =>
    {
        var brain = Brain();
        brain.BeginDrag(new PetPoint(100, 510));
        brain.UpdateDrag(new PetPoint(100, 200));
        brain.StartNagging(PetState.Angry);
        Check(brain.State == PetState.Dragged && brain.IsNagging, "Notification interrupted drag");
        Check(brain.NagHopOffset == 0, "Nagging moved dragged pet");
        brain.EndDrag();
        for (var i = 0; i < 300 && brain.State == PetState.Falling; i++) brain.Tick(0.01, Away());
        Check(brain.State == PetState.Angry && brain.IsNagging, "Notification did not resume after landing");
        brain.Tick(10, Away());
        Check(brain.IsNagging && brain.State == PetState.Angry, "Nagging timed out");
        brain.StopNagging();
        Check(!brain.IsNagging && brain.State == PetState.Idle, "Acknowledgement did not stop nagging");
    }),
    ("Acknowledging an airborne notification preserves falling", () =>
    {
        var brain = Brain();
        brain.BeginDrag(new PetPoint(100, 510));
        brain.UpdateDrag(new PetPoint(100, 200));
        brain.EndDrag(300, 0);
        brain.StartNagging(PetState.Cry);
        Check(brain.State == PetState.Falling, "Notification interrupted flight");
        brain.StopNagging();
        Check(brain.State == PetState.Falling && !brain.IsNagging, "Acknowledgement cancelled physics");
    }),
    ("Fast wall throws land instead of tunnelling below the desktop", () =>
    {
        foreach (var startX in new[] { 880.0, 10.0 })
        {
            var brain = new PetBrain(new PlatformLayout(new[] { new Platform(0, 1000, 600) }), 100, 90, startX, 509);
            brain.BeginDrag(new PetPoint(startX, 509));
            brain.EndDrag(startX > 100 ? 1700 : -1700, 1000);
            brain.Tick(0.1, Away());
            Check(brain.Y + 90 == 600 && brain.State != PetState.Falling, "Wall throw missed the floor");
            Check(brain.X >= 0 && brain.X <= 900, "Wall throw left desktop");
        }
    }),
    ("Display resize or removal recovers a pet below the new floor", () =>
    {
        foreach (var floor in new[] { new Platform(0, 1000, 400), new Platform(1200, 2000, 400), new Platform(0, 50, 400) })
        {
            var layout = new PlatformLayout(new[] { new Platform(0, 1000, 600) });
            var brain = new PetBrain(layout, 100, 90, 100, 510);
            layout.UpdateMonitors(new[] { floor });
            brain.Tick(0.01, Away());
            brain.Tick(0.01, Away());
            Check(brain.Y + 90 == 400 && brain.State != PetState.Falling, "Display change left pet below desktop");
            Check(brain.X + 50 >= floor.Left && brain.X + 50 <= floor.Right, "Display change left pet off screen");
        }
    }),
}.Concat(CharacterRegressionTests.Create())
 .Concat(InputRegressionTests.Create())
 .Concat(InboxResponsivenessTests.Create())
 .Concat(MotionRegressionTests.Create())
 .Concat(AssistantIntegrationTests.Create())
 .Concat(AssistantIntegrationHistoryTests.Create())
 .Concat(TimedAnimationTests.Create())
 .Concat(DirectionalWalkTests.Create())
 .Concat(CharacterLibraryTests.Create())
 .Concat(GroundedGaitTests.Create())
 .Concat(WalkRigResourceTests.Create())
 .Concat(FocusTimerTests.Create())
 .Concat(WindowPlatformMonitorTests.Create())
 .Concat(FullscreenPolicyTests.Create())
 .Concat(MonitorTopologyTests.Create())
 .Concat(BehaviorProfileTests.Create())
 .Concat(CharacterArchiveTests.Create())
 .Concat(CharacterPackExportTests.Create())
 .Concat(QuietHoursTests.Create())
 .Concat(FramePacerTests.Create())
 .Concat(PetSettingsBackupTests.Create())
 .Concat(DiagnosticReportTests.Create())
 .Concat(PetRecoveryTests.Create())
 .Concat(RoamingPauseTests.Create())
 .Concat(EmoteFlightTests.Create())
 .Concat(CharacterImportCancellationTests.Create())
 .Concat(RigSecondaryMotionTests.Create())
 .Concat(CursorGazeControllerTests.Create()).ToArray();

if (args.Contains("--character-export-only")) tests = CharacterPackExportTests.Create().ToArray();
var failures = 0;
var skipped = 0;
foreach (var (name, run) in tests)
{
    try { run(); Console.WriteLine($"PASS {name}"); }
    catch (RegressionTestSkippedException ex) { skipped++; Console.WriteLine($"SKIP {name}: {ex.Message}"); }
    catch (Exception ex) { failures++; Console.Error.WriteLine($"FAIL {name}\n{ex}"); }
}
Console.WriteLine($"{tests.Length - failures - skipped}/{tests.Length} passed; {skipped} skipped");
return failures == 0 ? 0 : 1;

static PetPoint Away() => new(-10_000, -10_000);
static PetBrain Brain() => new(new PlatformLayout(new[] { new Platform(0, 1000, 600) }), 100, 90, 100, 510, new Random(42));
static ConcurrentQueue<InboxNotification> Subscribe(NotificationInbox inbox)
{
    var received = new ConcurrentQueue<InboxNotification>();
    inbox.NotificationReceived += received.Enqueue;
    return received;
}
static void WriteNotification(string folder, string id, string message)
{
    var temporary = Path.Combine(folder, id + ".tmp");
    File.WriteAllText(temporary, JsonSerializer.Serialize(new PetNotification { Message = message }));
    File.Move(temporary, Path.Combine(folder, id + ".json"));
}
static void Until(Func<bool> condition)
{
    if (!SpinWait.SpinUntil(condition, TimeSpan.FromSeconds(10))) throw new Exception("Timed out");
}
static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
static void Throws<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}");
}
static void InTemporaryFolder(Action<string> run)
{
    var folder = Path.Combine(Path.GetTempPath(), "DesktopPet-Regression-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(folder);
    try { run(folder); }
    finally
    {
        var expectedRoot = Path.GetFullPath(Path.GetTempPath());
        var target = Path.GetFullPath(folder);
        Check(target.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase) &&
              Path.GetFileName(target).StartsWith("DesktopPet-Regression-", StringComparison.Ordinal), "Unsafe cleanup path");
        Directory.Delete(target, recursive: true);
    }
}
