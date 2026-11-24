using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopPet.App;
using DesktopPet.Core;
using WinForms = System.Windows.Forms;

internal static class NotificationReviewUiTests
{
    public static void Run(string root, bool capture = false)
    {
        var language = UiText.Language;
        UiText.SetLanguage("en");
        var folder = Path.Combine(root, "notification-review-inbox");
        var pet = new MainWindow(new CharacterLibrary(MainWindow.CharactersRoot, Path.Combine(root, "review-custom")), inboxDirectory: folder);
        pet.Loaded -= typeof(MainWindow).GetMethod("MainWindow_Loaded", BindingFlags.Instance | BindingFlags.NonPublic)!.CreateDelegate<RoutedEventHandler>(pet);
        var preferences = new PetSettings { Visible = false, QuietMode = true, AutoConnectAssistants = false };
        Set(pet, "_settings", preferences);
        var brain = new PetBrain(new PlatformLayout(new[] { new Platform(0, 1920, 1080) }), 128, 122, 500, 958);
        Set(pet, "_brain", brain);
        Set(pet, "_suppressionReasons", PetSuppressionReason.SessionLocked);
        Invoke(pet, "ApplyPresentation", CharacterPresentation.Load(Path.Combine(MainWindow.CharactersRoot, "Companion")));
        var save = (DispatcherTimer)Get(pet, "_settingsSaveTimer")!;
        save.Interval = TimeSpan.FromDays(1);
        var queue = new ConcurrentQueue<InboxNotification>();
        using var inbox = new NotificationInbox(folder);
        inbox.NotificationReceived += queue.Enqueue;
        inbox.Start();
        var factory = typeof(App).Assembly.GetType("DesktopPet.App.PetMenuBuilder")!.GetMethod("Create", BindingFlags.Static | BindingFlags.NonPublic)!;
        WinForms.ContextMenuStrip BuildMenu() => (WinForms.ContextMenuStrip)factory.Invoke(null, new object[] { pet, (Action)(() => { }), (Action)(() => { }), (Action)(() => { }) })!;
        using var tray = BuildMenu();
        using var petMenu = BuildMenu();
        WinForms.ToolStripMenuItem OpenItem(WinForms.ContextMenuStrip menu) => menu.Items.OfType<WinForms.ToolStripMenuItem>().Single(item => item.Text == UiText.Get("알림 목록..."));
        NotificationReviewWindow Window() => (NotificationReviewWindow)(Get(pet, "_notificationReviewWindow") ?? throw new Exception("Notification browser missing"));
        void Deliver(InboxNotification receipt) => Invoke(pet, "OnNotification", receipt);
        var acknowledgements = 0;
        InboxNotification Receipt(PetNotification notification) => (InboxNotification)typeof(InboxNotification)
            .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(PetNotification), typeof(Func<bool>) }, null)!
            .Invoke(new object[] { notification, (Func<bool>)(() => { acknowledgements++; return true; }) });
        var pose = (brain.X, brain.Y, brain.State, brain.StateElapsedMs);
        try
        {
            OpenItem(tray).PerformClick(); Pump();
            var empty = Window();
            Check(empty.IsVisible && empty.Owner is null && empty.AllItems.Count == 0 && !empty.Topmost && empty.ShowInTaskbar,
                "Empty inbox did not open independently of the hidden pet");
            Check(((TextBox)empty.FindName("NotificationDetails")).Text == "No saved notifications.", "Empty notification hint missing");
            OpenItem(petMenu).PerformClick();
            Check(ReferenceEquals(empty, Window()), "Shared menus opened duplicate browser windows");
            empty.WindowState = WindowState.Minimized; pet.ShowNotificationReview();
            Check(empty.WindowState == WindowState.Normal, "Opening an existing minimized browser did not restore it");
            empty.Close();
            Check(Get(pet, "_notificationReviewWindow") is null, "Closed notification browser was retained by Main");

            var path = NotificationWriter.Publish(folder, new PetNotification
            {
                Source = "claude", EventId = "review-file", Title = "알림", Message = "Original 사용자 내용 · 你好\n<raw>&\"text\"",
                CreatedAtUtc = new DateTimeOffset(2026, 11, 24, 20, 30, 0, TimeSpan.Zero)
            });
            Check(SpinWait.SpinUntil(() => !queue.IsEmpty, TimeSpan.FromSeconds(10)), "Local notification was not delivered");
            Check(queue.TryDequeue(out var fileReceipt), "Local receipt dequeue failed");
            Deliver(fileReceipt!);
            var bytes = File.ReadAllBytes(path);
            var codex = Receipt(new PetNotification { Source = " Codex ", Title = "집중 타이머", Message = "닫기", EventId = "" });
            Deliver(codex);
            Deliver(Receipt(new PetNotification { Source = "focus-timer", Title = "", Message = "Session complete" }));
            var customData = new PetNotification { Source = " my-tool ", Title = "Custom sender", Message = "Custom body", EventId = null! };
            var custom = Receipt(customData); Deliver(custom);
            Deliver(Receipt(new PetNotification { Source = null!, Title = null!, Message = null!, EventId = null! }));
            OpenItem(petMenu).PerformClick(); Pump();
            var review = Window();
            var list = (ListBox)review.FindName("NotificationItems");
            var details = (TextBox)review.FindName("NotificationDetails");
            var count = (TextBox)review.FindName("CountText");
            Check(!ReferenceEquals(empty, review) && review.AllItems.Count == 5 && details.IsReadOnly && details.IsTabStop,
                "Browser failed to reopen, clone pending items or allow native text selection");
            Check(review.AllItems[0].Title == "알림" && review.AllItems[0].SourceLabel == "Claude" && review.AllItems[0].CreatedAtUtc == fileReceipt!.Notification.CreatedAtUtc,
                "Browser changed original user text or timestamp");
            Check(review.AllItems[1].SourceLabel == "Codex" && review.AllItems[1].TimeLabel == "—" && review.AllItems[3].SourceLabel == " my-tool " &&
                review.AllItems[4].DisplayTitle == "Notification" && review.AllItems[4].SourceLabel == "Other" && review.AllItems[4].Message == "",
                "Source normalization, unknown source, absent time or nullable receipt fallback failed");
            Check(typeof(SavedNotificationItem).GetMethod("Acknowledge") is null && typeof(SavedNotificationItem).GetProperty("Receipt") is null,
                "Read-only public snapshot exposed receipt acknowledgement");
            var oldSnapshot = pet.GetPendingNotificationsSnapshot().Single(item => ReferenceEquals(ReceiptOf(item), custom));
            customData.Title = "Updated title"; customData.Message = "Updated body";
            Check(oldSnapshot.Title == "Custom sender" && oldSnapshot.Message == "Custom body" &&
                pet.GetPendingNotificationsSnapshot().Single(item => ReferenceEquals(ReceiptOf(item), custom)).Title == "Updated title",
                "Snapshot retained mutable notification fields");
            list.SelectedIndex = 1;
            foreach (var locale in new[] { "en", "zh-CN", "ko" })
            {
                UiText.SetLanguage(locale); Pump(); review.UpdateLayout();
                Check(review.Title == UiText.Get("보관된 알림 목록") && count.Text == UiText.Format("보관된 알림 {0}개", 5), "Notification title/count did not localize");
                Check(ReferenceEquals(ReceiptOf((SavedNotificationItem)list.SelectedItem), codex) && details.Text == string.Join(Environment.NewLine, "집중 타이머", "Codex", "—", "닫기"),
                    "Locale refresh lost selection or translated the user's original title/message");
                Check(review.AllItems[2].SourceLabel == UiText.Get("집중 타이머") && review.AllItems[4].SourceLabel == UiText.Get("기타"), "Known/empty source badges did not relocalize");
                var row = list.ItemContainerGenerator.ContainerFromIndex(1) as ListBoxItem;
                Check(row is not null && Descendants<TextBlock>(row).Any(text => text.Text == "집중 타이머"), "Realized list row translated user text");
                foreach (var menu in new[] { tray, petMenu })
                {
                    typeof(WinForms.ToolStripDropDown).GetMethod("OnOpening", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(menu, new object[] { new CancelEventArgs() });
                    OpenItem(menu).PerformClick(); Check(ReferenceEquals(review, Window()), "Cached localized menu lost its browser command");
                }
                if (capture) Program.SaveWindowSnapshot(review, $"notification-review-{locale}.png");
            }
            Check(File.ReadAllBytes(path).SequenceEqual(bytes) && acknowledgements == 0 && pet.PendingNotificationCount == 5 &&
                !pet.IsPetVisible && !pet.IsVisible && Get(pet, "_bubble") is null && !(bool)Get(pet, "_settingsDirty")! &&
                pose == (brain.X, brain.Y, brain.State, brain.StateElapsedMs), "Browsing acknowledged files, changed preferences, moved or revealed the pet");
            Console.WriteLine("PASS Saved notifications open independently, preserve receipts/user text and selected item in all three locales");

            DispatcherOperation? pending = null;
            for (var index = 0; index < 100; index++)
            {
                Deliver(Receipt(new PetNotification { Source = "test", Title = $"Arrival {index}", Message = "Burst notification" }));
                var next = (DispatcherOperation)Get(review, "_pendingRefresh")!;
                Check(pending is null || ReferenceEquals(pending, next), "Notification burst queued a full refresh for every receipt");
                pending = next;
            }
            Check(pending?.Status == DispatcherOperationStatus.Pending && review.AllItems.Count == 5, "Burst refresh was not coalesced on the dispatcher");
            UiText.SetLanguage("en"); UiText.SetLanguage("zh-CN");
            Check(ReferenceEquals(pending, Get(review, "_pendingRefresh")) && review.AllItems.Count == 5,
                "Locale changes rebuilt a snapshot already pending from an arrival burst");
            Pump(); review.UpdateLayout();
            Check(review.AllItems.Count == 105 && count.Text == UiText.Format("보관된 알림 {0}개", 105) &&
                ReferenceEquals(ReceiptOf((SavedNotificationItem)list.SelectedItem), codex), "Live refresh lost receipts, count or selected receipt");
            Check(VirtualizingPanel.GetIsVirtualizing(list) && VirtualizingPanel.GetVirtualizationMode(list) == VirtualizationMode.Recycling &&
                Enumerable.Range(0, list.Items.Count).Count(index => list.ItemContainerGenerator.ContainerFromIndex(index) is not null) < 40,
                "Notification list realized all offscreen rows");
            var cleared = pet.AcknowledgeAllNotificationsAsync(); Wait(cleared);
            Check(cleared.Result == 105 && acknowledgements == 104 && !File.Exists(path) && review.AllItems.Count == 0 &&
                details.Text == UiText.Get("보관된 알림이 없어요."), "External explicit acknowledgement did not update the open browser");
            Console.WriteLine("PASS Arrival bursts coalesce, selected receipts survive refresh, rows virtualize and explicit acknowledgement updates the list");

            Deliver(Receipt(new PetNotification { Title = "Close pending" }));
            var closingOperation = (DispatcherOperation)Get(review, "_pendingRefresh")!;
            var closedText = details.Text;
            review.Close();
            Check(closingOperation.Status == DispatcherOperationStatus.Aborted && Get(pet, "_notificationReviewWindow") is null,
                "Closing retained Main ownership or queued dispatcher work");
            UiText.SetLanguage("en"); Deliver(Receipt(new PetNotification { Title = "After close" })); Pump();
            Check(details.Text == closedText && review.AllItems.Count == 0 && Get(review, "_pendingRefresh") is null, "Closed browser received live or locale updates");
            pet.ShowNotificationReview(); Pump();
            var reopened = Window(); Check(!ReferenceEquals(reopened, review) && reopened.AllItems.Count == 2, "Reopening did not get a fresh current snapshot");
            save.Stop(); Set(pet, "_settingsDirty", false); pet.DisposeResources(); Pump();
            Check(!reopened.IsVisible && Get(pet, "_notificationReviewWindow") is null && pet.GetPendingNotificationsSnapshot().Count == 0,
                "Main disposal retained the browser or exposed stale pending notifications");
            pet.ShowNotificationReview();
            Check(Get(pet, "_notificationReviewWindow") is null, "Disposed Main opened a browser");
            Console.WriteLine("PASS Browser close/dispose cancels queued work, releases subscriptions and reopens with a fresh snapshot");
        }
        finally
        {
            save.Stop(); Set(pet, "_settingsDirty", false); pet.DisposeResources(); pet.Close(); UiText.SetLanguage(language);
        }
    }

    private static object ReceiptOf(SavedNotificationItem item) => typeof(SavedNotificationItem).GetProperty("Receipt", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(item)!;
    private static IEnumerable<T> Descendants<T>(DependencyObject node) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private static void Wait(Task task)
    {
        var timeout = Stopwatch.StartNew();
        while (!task.IsCompleted && timeout.Elapsed < TimeSpan.FromSeconds(10)) { Pump(); Thread.Sleep(2); }
        Check(task.IsCompleted, "Notification operation timed out"); task.GetAwaiter().GetResult(); Pump();
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
