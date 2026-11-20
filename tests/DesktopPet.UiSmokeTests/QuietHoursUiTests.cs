using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DesktopPet.App;
using DesktopPet.Core;

internal static class QuietHoursUiTests
{
    public static void Run(string root)
    {
        var folder = Path.Combine(root, "quiet-hours-inbox");
        var clock = new Clock();
        var pet = new MainWindow(new CharacterLibrary(MainWindow.CharactersRoot, Path.Combine(root, "quiet-hours-custom")),
            inboxDirectory: folder, quietClock: clock);
        var preferences = new PetSettings();
        Set(pet, "_settings", preferences);
        Set(pet, "_brain", new PetBrain(new PlatformLayout(new[] { new Platform(0, 1920, 1080) }), 128, 122, 500, 958));
        Invoke(pet, "ApplyPresentation", CharacterPresentation.Load(Path.Combine(MainWindow.CharactersRoot, "Companion")));
        var save = (DispatcherTimer)Get(pet, "_settingsSaveTimer")!;
        save.Interval = TimeSpan.FromDays(1);
        var bubble = new SpeechBubbleWindow();
        Set(pet, "_bubble", bubble);
        bubble.Acknowledged += () => Invoke(pet, "AcknowledgeNotification");
        var deliveries = new ConcurrentQueue<InboxNotification>();
        using var inbox = new NotificationInbox(folder);
        inbox.NotificationReceived += deliveries.Enqueue;
        inbox.Start();
        var settings = new SettingsWindow(pet);
        void Receive()
        {
            Check(SpinWait.SpinUntil(() => !deliveries.IsEmpty, TimeSpan.FromSeconds(5)), "Quiet-hour receipt missing");
            while (deliveries.TryDequeue(out var receipt)) Invoke(pet, "OnNotification", receipt);
        }
        try
        {
            Check(!(bool)Get(pet, "_settingsDirty")!, "Loading quiet controls changed preferences");
            ((CheckBox)settings.FindName("QuietHoursCheck")).IsChecked = true;
            ((TextBox)settings.FindName("QuietStartBox")).Text = "22:00";
            ((TextBox)settings.FindName("QuietEndBox")).Text = "08:00";
            ((Button)settings.FindName("QuietHoursApplyButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(!pet.IsQuietNow && ((DispatcherTimer)Get(pet, "_quietHoursTimer")!).IsEnabled, "Quiet schedule failed to arm before its boundary");
            pet.SendTestNotification(); Receive();
            Check(bubble.IsShowing, "Schedule suppressed notification before its start");
            clock.Now = new DateTimeOffset(2026, 11, 17, 22, 0, 0, TimeSpan.Zero);
            Invoke(pet, "RefreshQuietHours");
            Check(pet.IsQuietNow && !pet.QuietMode && !bubble.IsShowing && pet.PendingNotificationCount == 1,
                "Entering schedule changed manual quiet or deleted its receipt");
            pet.SendTestNotification(); Receive();
            Check(!bubble.IsShowing && pet.PendingNotificationCount == 2 && Directory.GetFiles(folder, "*.json").Length == 2, "Scheduled quiet lost or popped up a notification");
            pet.ShowUnreadNotifications();
            Check(bubble.IsShowing, "Scheduled quiet blocked explicit unread review");
            var popup = (DispatcherTimer)Get(pet, "_notificationPopupTimer")!;
            Check(popup.IsEnabled, "Manual quiet review did not start its configured timeout");
            pet.SetAutoDismissNotifications(false);
            Check(!popup.IsEnabled && bubble.IsShowing, "Disabling timeout during quiet review left an old popup timer running");
            pet.SetAutoDismissNotifications(true);
            Check(popup.IsEnabled && bubble.IsShowing, "Re-enabling timeout during quiet review failed to re-arm the visible receipt");
            Invoke(pet, "AcknowledgeNotification"); Invoke(pet, "AcknowledgeNotification");
            Check(pet.PendingNotificationCount == 0, "Explicit quiet review did not acknowledge receipts");
            Console.WriteLine("PASS Scheduled quiet crosses midnight, closes an existing bubble, preserves receipts and permits explicit review");

            // The 15-second schedule poll has not run yet. The explicit review itself
            // crosses the boundary; it must keep the displayed receipt authoritative.
            clock.Now = new DateTimeOffset(2026, 11, 17, 21, 59, 59, TimeSpan.Zero);
            Invoke(pet, "RefreshQuietHours");
            var firstEvent = Guid.NewGuid().ToString("N");
            var firstPath = NotificationWriter.Publish(folder, new PetNotification
            {
                Title = "Boundary first", Message = "First receipt must be acknowledged first", Source = "quiet-fixture", EventId = firstEvent,
            });
            Receive();
            var firstReceipt = Get(pet, "_activeNotification");
            clock.Now = new DateTimeOffset(2026, 11, 17, 22, 0, 0, TimeSpan.Zero);
            Check(pet.IsQuietNow && !(bool)Get(pet, "_scheduledQuietActive")!, "Fixture refreshed the quiet schedule before the review boundary");
            pet.ShowUnreadNotifications();
            Check(bubble.IsShowing && (bool)Get(pet, "_reviewingNotifications")! &&
                ((TextBlock)bubble.FindName("TitleText")).Text == "Boundary first" && ReferenceEquals(firstReceipt, Get(pet, "_activeNotification")),
                "Quiet-hours refresh discarded explicit review intent or changed its first receipt");
            var secondPath = NotificationWriter.Publish(folder, new PetNotification
            {
                Title = "Boundary second", Message = "Second receipt stays queued", Source = "quiet-fixture", EventId = Guid.NewGuid().ToString("N"),
            });
            Receive();
            Check(pet.PendingNotificationCount == 2 && bubble.IsShowing &&
                ((TextBlock)bubble.FindName("TitleText")).Text == "Boundary first" &&
                ReferenceEquals(firstReceipt, Get(pet, "_activeNotification")) && File.Exists(firstPath) && File.Exists(secondPath),
                "A new quiet delivery replaced the active receipt while the bubble still displayed the first one");
            Invoke(bubble, "Acknowledge");
            Check(pet.PendingNotificationCount == 1 && !File.Exists(firstPath) && File.Exists(secondPath) && bubble.IsShowing &&
                ((TextBlock)bubble.FindName("TitleText")).Text == "Boundary second" &&
                ((InboxNotification)Get(pet, "_activeNotification")!).Notification.Title == "Boundary second",
                "Clicking the first bubble acknowledged a different receipt or failed to advance FIFO review");
            Invoke(bubble, "Acknowledge");
            Check(pet.PendingNotificationCount == 0 && !File.Exists(firstPath) && !File.Exists(secondPath) &&
                Directory.GetFiles(folder, "*.json").Length == 0 && !bubble.IsShowing,
                "Clicking both reviewed bubbles did not acknowledge exactly their two files");
            Console.WriteLine("PASS Explicit review at a quiet-hours boundary preserves displayed FIFO receipt identity when a new delivery arrives and acknowledges exactly the clicked files");

            pet.SetQuietMode(true);
            clock.Now = clock.Now.AddHours(10); // 08:00, exclusive schedule end.
            Invoke(pet, "RefreshQuietHours");
            Check(pet.IsQuietNow && pet.QuietMode && !bubble.IsShowing, "Schedule end disabled manual quiet or replayed old bubbles");
            pet.SetQuietMode(false);
            Check(!pet.IsQuietNow, "Schedule remained active at the exclusive end");
            ((TextBox)settings.FindName("QuietStartBox")).Text = "24:00";
            ((Button)settings.FindName("QuietHoursApplyButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(pet.QuietHoursStartMinute == 1320 && ((TextBlock)settings.FindName("QuietHoursStatus")).Text.Contains("HH:mm"), "Invalid schedule changed the saved interval");
            pet.SetQuietHours(false, 1320, 480);
            Check(!((DispatcherTimer)Get(pet, "_quietHoursTimer")!).IsEnabled, "Disabled quiet schedule kept polling");
            Console.WriteLine("PASS Quiet-hours controls reject invalid times, preserve manual quiet and stop polling when disabled");
        }
        finally
        {
            save.Stop(); Set(pet, "_settingsDirty", false);
            settings.Close(); pet.DisposeResources();
        }
        Check(!((DispatcherTimer)Get(pet, "_quietHoursTimer")!).IsEnabled, "Disposed pet retained quiet schedule polling");
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 11, 17, 21, 59, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
    private static object? Get(object obj, string name) => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(obj);
    private static void Set(object obj, string name, object value) => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(obj, value);
    private static object? Invoke(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(obj, args);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
