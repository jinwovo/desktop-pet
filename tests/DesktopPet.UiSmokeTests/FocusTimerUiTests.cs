using System.Collections.Concurrent;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using DesktopPet.App;
using DesktopPet.Core;

internal static class FocusTimerUiTests
{
    public static void Run(string root)
    {
        var folder = Path.Combine(root, "focus-inbox");
        var clock = new Clock();
        using var timer = new FocusTimer(clock, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5));
        var pet = new MainWindow(new CharacterLibrary(MainWindow.CharactersRoot, Path.Combine(root, "focus-characters")), timer, folder);
        var preferences = new PetSettings { Visible = true, QuietMode = true };
        Set(pet, "_settings", preferences);
        Set(pet, "_brain", new PetBrain(new PlatformLayout(new[] { new Platform(0, 1920, 1080) }), 128, 122, 500, 958));
        Invoke(pet, "ApplyPresentation", CharacterPresentation.Load(Path.Combine(MainWindow.CharactersRoot, "Companion")));
        var bubble = new SpeechBubbleWindow();
        Set(pet, "_bubble", bubble);
        var deliveries = new ConcurrentQueue<InboxNotification>();
        using var inbox = new NotificationInbox(folder);
        inbox.NotificationReceived += deliveries.Enqueue;
        inbox.Start();
        var settings = new SettingsWindow(pet);
        try
        {
            void Click(string name) => ((Button)settings.FindName(name)).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            void Advance(double seconds) { clock.Advance(seconds); Invoke(pet, "UpdateFocusTimer"); }
            void Receive()
            {
                Check(SpinWait.SpinUntil(() => !deliveries.IsEmpty, TimeSpan.FromSeconds(5)), "Local receipt never reached the inbox watcher");
                while (deliveries.TryDequeue(out var delivery)) Invoke(pet, "OnNotification", delivery);
            }
            var dispatcher = (DispatcherTimer)Get(pet, "_focusTickTimer")!;
            Click("FocusStartButton");
            Check(timer.Snapshot.Status == FocusTimerStatus.Running && dispatcher.IsEnabled, "Settings did not start the focus clock");
            Advance(3);
            Check(((TextBlock)settings.FindName("FocusStatusText")).Text.Contains("00:07"), "Countdown label did not follow the injected clock");
            Click("FocusPauseButton");
            Check(!dispatcher.IsEnabled, "Paused timer kept polling");
            Advance(3600);
            Check(timer.Snapshot.Remaining == TimeSpan.FromSeconds(7), "Paused wall time consumed the session");
            Click("FocusPauseButton");
            Check(dispatcher.IsEnabled && timer.Snapshot.Status == FocusTimerStatus.Running, "Settings did not resume the session");
            Console.WriteLine("PASS Focus controls start, pause and resume the real timer without polling while paused");

            Advance(8);
            Receive();
            Check(timer.Snapshot.Status == FocusTimerStatus.Completed && !dispatcher.IsEnabled && pet.FocusStartCaption == "휴식 시작",
                "Focus completion did not stop polling or offer a manual break");
            Check(pet.PendingNotificationCount == 1 && !bubble.IsShowing && Directory.GetFiles(folder, "*.json").Length == 1,
                "Focus completion bypassed quiet mode or lost its durable receipt");
            Advance(7200);
            Check(Directory.GetFiles(folder, "*.json").Length == 1, "A late timer tick duplicated its completion");
            Console.WriteLine("PASS Focus completion publishes exactly one durable receipt through the existing quiet inbox");

            preferences.Visible = false;
            preferences.QuietMode = false;
            Invoke(settings, "TestNotification_Click", settings, new RoutedEventArgs());
            Receive();
            Check(pet.PendingNotificationCount == 2 && !bubble.IsShowing && !preferences.Visible,
                "Test notification resurfaced a hidden pet or bypassed the unread queue");
            preferences.Visible = true; // Keep this fixture window unshown: never invoke its real startup/settings loader.
            pet.ShowUnreadNotifications();
            Check(bubble.IsShowing, "Stored local notifications cannot be explicitly reviewed");
            Invoke(pet, "AcknowledgeNotification");
            Invoke(pet, "AcknowledgeNotification");
            Check(pet.PendingNotificationCount == 0 && Directory.GetFiles(folder, "*.json").Length == 0,
                "Local timer/demo receipts were not acknowledged using the regular inbox flow");
            Console.WriteLine("PASS Test notification honors hidden mode and both local receipts use normal review and acknowledgement");

            Click("FocusStartButton");
            Check(timer.Snapshot.Session == FocusSessionKind.Break, "Completed focus did not start its manual break");
            Click("FocusRestartButton");
            Check(timer.Snapshot.Remaining == TimeSpan.FromSeconds(5), "Restart did not retain the active break duration");
            Click("FocusStopButton");
            Advance(100);
            Check(timer.Snapshot.Status == FocusTimerStatus.Stopped && !dispatcher.IsEnabled && Directory.GetFiles(folder, "*.json").Length == 0,
                "Stopping a timer emitted a completion or retained its dispatcher");
            settings.Close();
            Set(pet, "_settingsDirty", false);
            pet.DisposeResources();
            pet.SendTestNotification();
            pet.StartFocusTimer();
            Advance(100);
            Check(Directory.GetFiles(folder, "*.json").Length == 0, "Disposed pet published a late notification");
            Console.WriteLine("PASS Manual break, restart, stop and disposal cannot leave a late timer notification");
        }
        finally
        {
            settings.Close();
            ((DispatcherTimer)Get(pet, "_settingsSaveTimer")!).Stop();
            Set(pet, "_settingsDirty", false);
            pet.DisposeResources();
        }
    }

    private sealed class Clock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(double seconds) => _ticks += TimeSpan.FromSeconds(seconds).Ticks;
    }
    private static object? Get(object obj, string name) => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(obj);
    private static void Set(object obj, string name, object value) => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(obj, value);
    private static object? Invoke(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(obj, args);
    private static void Check(bool valid, string message) { if (!valid) throw new Exception(message); }
}
