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

internal static class NotificationSelectionUiTests
{
    public static void Run(string root, bool capture = false)
    {
        var language = UiText.Language;
        UiText.SetLanguage("en");
        var folder = Path.Combine(root, "notification-selection-inbox");
        var pet = new MainWindow(new CharacterLibrary(MainWindow.CharactersRoot, Path.Combine(root, "selection-custom")), inboxDirectory: folder);
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
        NotificationReviewWindow Window() => (NotificationReviewWindow)(Get(pet, "_notificationReviewWindow") ?? throw new Exception("Notification browser missing"));
        void Deliver(InboxNotification receipt) => Invoke(pet, "OnNotification", receipt);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var lateEntered = new ManualResetEventSlim();
        using var lateRelease = new ManualResetEventSlim();
        var uiThread = Environment.CurrentManagedThreadId;
        var ackThread = uiThread; var a = 0; var b = 0; var c = 0;
        var errors = new List<string>(); pet.OperationFailed += errors.Add;
        InboxNotification Receipt(string title, Func<bool> ack) => (InboxNotification)typeof(InboxNotification)
            .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(PetNotification), typeof(Func<bool>) }, null)!
            .Invoke(new object[] { new PetNotification { Source = "test", EventId = "", Title = title, Message = title }, ack });
        NotificationReviewWindow? review = null;
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        bool Busy(NotificationReviewWindow window) => (bool)Get(window, "_selectionBusy")!;
        try
        {
            Deliver(Receipt("A", () => { a++; return true; }));
            Deliver(Receipt("B", () => { Interlocked.Increment(ref b); ackThread = Environment.CurrentManagedThreadId; entered.Set(); return release.Wait(TimeSpan.FromSeconds(20)); }));
            pet.ShowNotificationReview(); Pump(); review = Window();
            var list = (ListBox)review.FindName("NotificationItems");
            var button = (Button)review.FindName("MarkSelectedReadButton");
            var details = (TextBox)review.FindName("NotificationDetails");
            var itemB = review.AllItems.Single(item => item.Title == "B"); list.SelectedItem = itemB; Click(button);
            Check(entered.Wait(TimeSpan.FromSeconds(10)) && ackThread != uiThread && pet.IsAcknowledgingAll && Busy(review) && !button.IsEnabled,
                "Selected receipt IO ran on UI thread, lost its busy gate or left the button enabled");
            var dispatched = false; Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => dispatched = true)); Pump();
            Check(dispatched, "Selected receipt IO blocked dispatcher work");
            Deliver(Receipt("C", () => { c++; return true; })); Pump();
            list.SelectedItem = review.AllItems.Single(item => item.Title == "A");
            foreach (var locale in new[] { "en", "zh-CN", "ko" })
            {
                UiText.SetLanguage(locale); Pump(); Click(button);
                Check(!button.IsEnabled && (string)button.Content == UiText.Get("선택한 알림 읽음 처리 중…") && a == 0 && b == 1 && c == 0,
                    "Busy locale/selection/duplicate input retargeted the captured receipt or lost its label");
                if (capture) Program.SaveWindowSnapshot(review, $"notification-selection-busy-{locale}.png");
            }
            release.Set(); WaitUntil(() => !pet.IsAcknowledgingAll && !Busy(review)); Pump();
            Check(pet.GetPendingNotificationsSnapshot().Select(item => item.Title).SequenceEqual(new[] { "A", "C" }) && a == 0 && b == 1 && c == 0,
                "Selected acknowledgement removed a different receipt, duplicate blank event ID or new arrival");
            Check(pet.AcknowledgeSavedNotificationAsync(itemB).GetAwaiter().GetResult() == 0 && pet.AcknowledgeSavedNotificationAsync(null).GetAwaiter().GetResult() == 0,
                "Stale/null selection could acknowledge another receipt");
            Console.WriteLine("PASS Selected receipt IO stays responsive and preserves captured identity, arrivals, blank IDs, locale state and duplicate-click gates");

            var path = NotificationWriter.Publish(folder, new PetNotification { Source = "test", Title = "Locked", Message = "Locked receipt" });
            Check(SpinWait.SpinUntil(() => !queue.IsEmpty, TimeSpan.FromSeconds(10)), "Actual selected file fixture missing");
            Check(queue.TryDequeue(out var locked), "Actual selected file dequeue failed"); Deliver(locked!); Pump();
            list.SelectedItem = review.AllItems.Single(item => item.Title == "Locked");
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Click(button); WaitUntil(() => !pet.IsAcknowledgingAll && !Busy(review)); Pump();
                Check(File.Exists(path) && pet.PendingNotificationCount == 3 && errors.Count == 1 &&
                    (string)button.Content == UiText.Get("읽음 처리 재시도"), "Failed selected deletion lost its file/row or did not offer retry");
            }
            Click(button); WaitUntil(() => !pet.IsAcknowledgingAll && !Busy(review)); Pump();
            Check(!File.Exists(path) && pet.PendingNotificationCount == 2 && a == 0 && c == 0, "Selected retry did not remove only the failed receipt");

            Deliver(Receipt("D", () => { lateEntered.Set(); return lateRelease.Wait(TimeSpan.FromSeconds(20)); })); Pump();
            list.SelectedItem = review.AllItems.Single(item => item.Title == "D"); Click(button);
            Check(lateEntered.Wait(TimeSpan.FromSeconds(10)), "Late selected worker did not start");
            var closedText = details.Text; var closedContent = button.Content;
            review.Close(); pet.ShowNotificationReview(); var fresh = Window();
            Deliver(Receipt("E", () => throw new Exception("New arrival was incorrectly acknowledged"))); Pump();
            lateRelease.Set(); WaitUntil(() => !pet.IsAcknowledgingAll); Pump();
            Check(details.Text == closedText && Equals(button.Content, closedContent) && !ReferenceEquals(review, fresh) &&
                fresh.AllItems.Select(item => item.Title).SequenceEqual(new[] { "A", "C", "E" }), "Closed completion mutated its old UI or a reopened browser lost arrivals");
            Check(a == 0 && c == 0 && !pet.IsPetVisible && !(bool)Get(pet, "_settingsDirty")!, "Selected acknowledgement changed unrelated receipts or preferences");
            save.Stop(); Set(pet, "_settingsDirty", false); pet.DisposeResources();
            Invoke(pet, "AcknowledgeNotification");
            Check(a == 0 && c == 0, "Disposed popup callback still acknowledged a receipt");
            Check(!fresh.IsVisible && pet.AcknowledgeSavedNotificationAsync(itemB).GetAwaiter().GetResult() == 0, "Disposed Main accepted a selected receipt");
            Console.WriteLine("PASS Locked selected files remain retryable; closed/reopened windows retain receipt ownership and ignore late UI completion");
        }
        finally
        {
            release.Set(); lateRelease.Set();
            WaitUntil(() => !pet.IsAcknowledgingAll);
            save.Stop(); Set(pet, "_settingsDirty", false); pet.DisposeResources(); pet.Close(); UiText.SetLanguage(language);
        }
    }
    private static void WaitUntil(Func<bool> done)
    {
        var timeout = Stopwatch.StartNew();
        while (!done() && timeout.Elapsed < TimeSpan.FromSeconds(20)) { Pump(); Thread.Sleep(2); }
        Check(done(), "Selected receipt UI operation timed out");
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
