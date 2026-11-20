using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using DesktopPet.App;
using DesktopPet.Core;
using WinForms = System.Windows.Forms;

internal static class NotificationBatchUiTests
{
    public static void Run(string root)
    {
        var language = UiText.Language;
        UiText.SetLanguage("en");
        var folder = Path.Combine(root, "notification-batch-inbox");
        var pet = new MainWindow(new CharacterLibrary(MainWindow.CharactersRoot, Path.Combine(root, "batch-custom")), inboxDirectory: folder);
        var loaded = (RoutedEventHandler)Delegate.CreateDelegate(typeof(RoutedEventHandler), pet, typeof(MainWindow).GetMethod("MainWindow_Loaded", BindingFlags.Instance | BindingFlags.NonPublic)!);
        pet.Loaded -= loaded;
        Set(pet, "_settings", new PetSettings { Visible = false, QuietMode = true, AutoDismissNotifications = true, AutoConnectAssistants = false });
        Set(pet, "_brain", new PetBrain(new PlatformLayout(new[] { new Platform(0, 1920, 1080) }), 128, 122, 500, 958));
        Set(pet, "_suppressionReasons", PetSuppressionReason.SessionLocked);
        Invoke(pet, "ApplyPresentation", CharacterPresentation.Load(Path.Combine(MainWindow.CharactersRoot, "Companion")));
        var save = (DispatcherTimer)Get(pet, "_settingsSaveTimer")!; save.Interval = TimeSpan.FromDays(1);
        var queue = new ConcurrentQueue<InboxNotification>();
        using var inbox = new NotificationInbox(folder);
        inbox.NotificationReceived += queue.Enqueue; inbox.Start();
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        using var lateEntered = new ManualResetEventSlim(); using var lateRelease = new ManualResetEventSlim();
        using var visibleEntered = new ManualResetEventSlim(); using var visibleRelease = new ManualResetEventSlim();
        var tasks = new List<Task<int>>();
        var uiThread = Environment.CurrentManagedThreadId; var workerThread = uiThread;
        var gateCalls = 0; var newCalls = 0; var acknowledgedNewCalls = 0;
        var events = new List<int>(); var errors = new List<string>();
        pet.PendingNotificationCountChanged += _ => events.Add(Environment.CurrentManagedThreadId);
        pet.OperationFailed += errors.Add;
        var constructor = typeof(InboxNotification).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(PetNotification), typeof(Func<bool>) }, null)!;
        InboxNotification Receipt(string id, Func<bool> acknowledge) => (InboxNotification)constructor.Invoke(new object[] { new PetNotification { Source = "bulk", EventId = id, Title = id }, acknowledge });
        void Deliver(InboxNotification receipt) => Invoke(pet, "OnNotification", receipt);
        var factory = typeof(App).Assembly.GetType("DesktopPet.App.PetMenuBuilder")!.GetMethod("Create", BindingFlags.Static | BindingFlags.NonPublic)!;
        using var menu = (WinForms.ContextMenuStrip)factory.Invoke(null, new object[] { pet, (Action)(() => { }), (Action)(() => { }), (Action)(() => { }) })!;
        void RefreshMenu() => typeof(WinForms.ToolStripDropDown).GetMethod("OnOpening", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(menu, new object[] { new CancelEventArgs() });
        WinForms.ToolStripMenuItem MarkAll() => menu.Items.OfType<WinForms.ToolStripMenuItem>().Single(item => item.Text is "Mark all notifications read" or "Marking notifications read...");
        try
        {
            Deliver(Receipt("gate", () =>
            {
                Interlocked.Increment(ref gateCalls); workerThread = Environment.CurrentManagedThreadId; entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Bulk worker release timed out");
                return true;
            }));
            var lockedPath = NotificationWriter.Publish(folder, new PetNotification { Source = "bulk", EventId = "locked", Title = "Locked receipt" });
            Check(SpinWait.SpinUntil(() => !queue.IsEmpty, TimeSpan.FromSeconds(10)), "Locked receipt fixture missing");
            Check(queue.TryDequeue(out var lockedReceipt), "Locked receipt dequeue failed"); Deliver(lockedReceipt!);
            using (var locked = new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var operation = pet.AcknowledgeAllNotificationsAsync(); tasks.Add(operation);
                Check(entered.Wait(TimeSpan.FromSeconds(10)) && pet.IsAcknowledgingAll && !operation.IsCompleted && workerThread != uiThread,
                    "Bulk acknowledgement blocked the UI or ran receipt IO on its thread");
                var dispatcherWorked = false;
                Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => dispatcherWorked = true)); Pump();
                Check(dispatcherWorked, "Bulk acknowledgement stopped dispatcher work");
                RefreshMenu(); Check(!MarkAll().Enabled && MarkAll().Text == "Marking notifications read...", "Busy bulk menu remained actionable or lost its status");
                Check(pet.AcknowledgeAllNotificationsAsync().GetAwaiter().GetResult() == 0 && gateCalls == 1, "Reentrant bulk request started another worker");

                Deliver(Receipt("new-individual", () => { acknowledgedNewCalls++; return true; }));
                Invoke(pet, "AcknowledgeNotification");
                Check(acknowledgedNewCalls == 1 && operation.IsCompleted == false, "A newly arriving receipt could not be acknowledged independently");
                Invoke(pet, "AcknowledgeNotification"); // The active old receipt is owned by the blocked worker.
                Check(gateCalls == 1, "Individual acknowledgement competed for a receipt owned by the worker");
                Deliver(Receipt("new-retained", () => { newCalls++; return true; }));
                release.Set(); Wait(operation);
                Check(operation.Result == 1 && pet.PendingNotificationCount == 2 && newCalls == 0 && File.Exists(lockedPath) && !pet.IsAcknowledgingAll,
                    "Bulk action deleted a new/failed receipt, reported wrong count or retained busy state");
                Check(errors.SequenceEqual(new[] { "Could not mark 1 notifications read. Retry from saved notifications." }), "Partial failure was not reported once in the selected language");
            }
            RefreshMenu(); Check(MarkAll().Enabled, "Completed bulk action did not permit a retry");
            MarkAll().PerformClick(); WaitUntil(() => !pet.IsAcknowledgingAll);
            Check(pet.PendingNotificationCount == 0 && newCalls == 1 && !File.Exists(lockedPath) && errors.Count == 1, "Shared menu retry did not acknowledge exactly the remaining receipts");
            RefreshMenu(); Check(!MarkAll().Enabled, "Empty inbox still offered an enabled bulk action");
            Check(events.All(thread => thread == uiThread), "Worker updated notification UI events off the dispatcher");

            Deliver(Receipt("visible-gate", () =>
            {
                visibleEntered.Set();
                if (!visibleRelease.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Visible batch worker release timed out");
                return true;
            }));
            var visibleOperation = pet.AcknowledgeAllNotificationsAsync(); tasks.Add(visibleOperation);
            Check(visibleEntered.Wait(TimeSpan.FromSeconds(10)), "Visible batch worker did not start");
            var bubble = new SpeechBubbleWindow(); bubble.SetReducedMotion(true); Set(pet, "_bubble", bubble);
            var preferences = (PetSettings)Get(pet, "_settings")!;
            preferences.Visible = true; preferences.QuietMode = false; preferences.PauseDuringFullscreen = false;
            Set(pet, "_suppressionReasons", PetSuppressionReason.None);
            var visibleReceipt = Receipt("new-visible", () => true);
            Deliver(visibleReceipt); Invoke(pet, "StopRenderLoop");
            Check(bubble.IsShowing && ReferenceEquals(Get(pet, "_activeNotification"), visibleReceipt), "Concurrent popup fixture was not displayed");
            Invoke(pet, "OnNotificationPopupElapsed");
            Check(!bubble.IsShowing, "Concurrent popup did not auto-dismiss");
            visibleRelease.Set(); Wait(visibleOperation);
            Check(visibleOperation.Result == 1 && pet.PendingNotificationCount == 1 && ReferenceEquals(Get(pet, "_activeNotification"), visibleReceipt) && !bubble.IsShowing,
                "Bulk completion discarded a new active receipt or reopened its expired popup");
            preferences.Visible = false; preferences.QuietMode = true;
            Set(pet, "_suppressionReasons", PetSuppressionReason.SessionLocked);
            var cleanupVisible = pet.AcknowledgeAllNotificationsAsync(); tasks.Add(cleanupVisible); Wait(cleanupVisible);
            Check(pet.PendingNotificationCount == 0 && cleanupVisible.Result == 1, "Concurrent popup receipt could not be acknowledged later");

            Deliver(Receipt("dispose-gate", () =>
            {
                lateEntered.Set();
                if (!lateRelease.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Disposed bulk worker release timed out");
                return true;
            }));
            var closing = pet.AcknowledgeAllNotificationsAsync(); tasks.Add(closing);
            Check(lateEntered.Wait(TimeSpan.FromSeconds(10)), "Closing bulk worker never started");
            var beforeCloseEvents = events.Count;
            save.Stop(); Set(pet, "_settingsDirty", false); pet.DisposeResources();
            lateRelease.Set(); Wait(closing);
            Check(closing.Result == 0 && events.Count == beforeCloseEvents && errors.Count == 1 && !pet.IsAcknowledgingAll,
                "Late bulk completion updated disposed UI or retained its snapshot");
            Console.WriteLine("PASS Bulk acknowledgement stays responsive, preserves concurrent arrivals/failed receipts, supports shared-menu retry and ignores late UI completion after disposal");
        }
        finally
        {
            release.Set(); lateRelease.Set(); visibleRelease.Set(); save.Stop(); Set(pet, "_settingsDirty", false); pet.DisposeResources();
            foreach (var task in tasks) Wait(task);
            pet.Close(); UiText.SetLanguage(language);
        }
    }
    private static void Wait(Task task) { WaitUntil(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static void WaitUntil(Func<bool> complete)
    {
        var timeout = Stopwatch.StartNew();
        while (!complete() && timeout.Elapsed < TimeSpan.FromSeconds(20)) { Pump(); Thread.Sleep(2); }
        Check(complete(), "Bulk UI operation timed out");
    }
    private static void Pump()
    {
        var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false)); Dispatcher.PushFrame(frame);
    }
    private static object? Get(object obj, string name) => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(obj);
    private static void Set(object obj, string name, object value) => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(obj, value);
    private static object? Invoke(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(obj, args);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
