using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DesktopPet.App;
using DesktopPet.Core;
using Microsoft.Win32;

internal static class FullscreenUiTests
{
    public static void Run(string root)
    {
        var inboxFolder = Path.Combine(root, "suppression-inbox");
        var clock = new Clock();
        var focus = new FocusTimer(clock, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5));
        var pet = new MainWindow(new CharacterLibrary(MainWindow.CharactersRoot, Path.Combine(root, "suppression-characters")), focus, inboxFolder);
        var loaded = (RoutedEventHandler)Delegate.CreateDelegate(typeof(RoutedEventHandler), pet,
            typeof(MainWindow).GetMethod("MainWindow_Loaded", BindingFlags.NonPublic | BindingFlags.Instance)!);
        pet.Loaded -= loaded; // All inputs are fixtures; never initialize a real profile or assistant hooks.
        var preferences = new PetSettings { Visible = true, QuietMode = false };
        Set(pet, "_settings", preferences);
        Set(pet, "_brain", new PetBrain(new PlatformLayout(new[] { new Platform(0, 1920, 1080) }), 128, 122, 500, 958));
        Invoke(pet, "ApplyPresentation", CharacterPresentation.Load(Path.Combine(MainWindow.CharactersRoot, "Companion")));
        var save = (DispatcherTimer)Get(pet, "_settingsSaveTimer")!;
        save.Interval = TimeSpan.FromDays(1);
        var bubble = new SpeechBubbleWindow();
        var visibilityChanges = new List<bool>();
        pet.PresentationVisibilityChanged += visibilityChanges.Add;
        Set(pet, "_bubble", bubble);
        var deliveries = new ConcurrentQueue<InboxNotification>();
        using var inbox = new NotificationInbox(inboxFolder);
        inbox.NotificationReceived += deliveries.Enqueue;
        inbox.Start();
        void Receive()
        {
            Check(SpinWait.SpinUntil(() => !deliveries.IsEmpty, TimeSpan.FromSeconds(5)), "Fixture inbox did not receive a notification");
            while (deliveries.TryDequeue(out var delivery)) Invoke(pet, "OnNotification", delivery);
        }
        void Suppress(PetSuppressionReason reasons) => Invoke(pet, "SetTemporarySuppression", reasons);
        try
        {
            var before = JsonSerializer.Serialize(preferences);
            Suppress(PetSuppressionReason.Fullscreen);
            pet.SendTestNotification(); Receive();
            pet.ShowUnreadNotifications();
            Check(pet.IsPetVisible && pet.IsTemporarilyPaused && pet.Visibility == Visibility.Hidden && !bubble.IsShowing &&
                !(bool)Get(pet, "_renderLoopRunning")! && pet.PendingNotificationCount == 1 && Directory.GetFiles(inboxFolder, "*.json").Length == 1,
                "Fullscreen pause resurfaced the pet/bubble or consumed an unread receipt");
            Check(JsonSerializer.Serialize(preferences) == before && !(bool)Get(pet, "_settingsDirty")!, "Temporary suppression changed persisted preferences");
            Suppress(PetSuppressionReason.None);
            Check(bubble.IsShowing && (bool)Get(pet, "_renderLoopRunning")!, "Clearing temporary suppression did not restore the eligible pet");
            Check(visibilityChanges.SequenceEqual(new[] { false, true }), "Optional overlays did not receive effective hide/resume transitions");
            Invoke(pet, "AcknowledgeNotification");
            Console.WriteLine("PASS Fullscreen pause suppresses rendering/bubbles and retains durable notifications without modifying user visibility");

            Invoke(pet, "OnSessionSwitch", pet, new SessionSwitchEventArgs(SessionSwitchReason.SessionLock));
            PumpUntil(() => pet.IsTemporarilyPaused);
            pet.SetVisible(false);
            save.Stop(); Set(pet, "_settingsDirty", false);
            pet.SendTestNotification(); Receive();
            pet.ShowUnreadNotifications();
            Check(!pet.IsPetVisible && !bubble.IsShowing, "Unread review changed manual visibility while presentation was locked");
            Invoke(pet, "OnSessionSwitch", pet, new SessionSwitchEventArgs(SessionSwitchReason.SessionUnlock));
            PumpUntil(() => !pet.IsTemporarilyPaused);
            Check(!pet.IsPetVisible && pet.Visibility == Visibility.Hidden && !(bool)Get(pet, "_renderLoopRunning")!, "Unlock resurrected a manually hidden pet");
            pet.SetVisible(true); save.Stop(); Set(pet, "_settingsDirty", false);
            Invoke(pet, "AcknowledgeNotification");
            Console.WriteLine("PASS Native session-event dispatch preserves manual hidden preference across lock and unlock");

            Invoke(pet, "OnSessionSwitch", pet, new SessionSwitchEventArgs(SessionSwitchReason.SessionLock));
            Invoke(pet, "OnPowerModeChanged", pet, new PowerModeChangedEventArgs(PowerModes.Suspend));
            PumpUntil(() => (PetSuppressionReason)Get(pet, "_suppressionReasons")! == (PetSuppressionReason.SessionLocked | PetSuppressionReason.Suspended));
            Invoke(pet, "OnSessionSwitch", pet, new SessionSwitchEventArgs(SessionSwitchReason.SessionUnlock));
            PumpUntil(() => (PetSuppressionReason)Get(pet, "_suppressionReasons")! == PetSuppressionReason.Suspended);
            Check(pet.Visibility == Visibility.Hidden, "Unlock ignored remaining power suspension");
            pet.StartFocusTimer(); clock.Advance(11); Invoke(pet, "UpdateFocusTimer"); Receive();
            Check(pet.PendingNotificationCount == 1 && !bubble.IsShowing && focus.Snapshot.Status == FocusTimerStatus.Completed,
                "Logical focus completion was lost or displayed during suspension");
            Invoke(pet, "OnPowerModeChanged", pet, new PowerModeChangedEventArgs(PowerModes.Resume));
            PumpUntil(() => !pet.IsTemporarilyPaused);
            Check(bubble.IsShowing && pet.IsPetVisible, "Resume did not retain the completed focus receipt");
            Invoke(pet, "AcknowledgeNotification");
            Console.WriteLine("PASS Lock/power reasons compose independently and focus completion remains durable while presentation is paused");

            var settings = new SettingsWindow(pet);
            try
            {
                Suppress(PetSuppressionReason.Fullscreen);
                ((CheckBox)settings.FindName("FullscreenCheck")).IsChecked = false;
                Check(!pet.PauseDuringFullscreen && !pet.IsTemporarilyPaused && pet.IsPetVisible,
                    "Fullscreen checkbox failed to opt out without changing manual visibility");
            }
            finally { settings.Close(); save.Stop(); Set(pet, "_settingsDirty", false); }
            pet.DisposeResources();
            Invoke(pet, "OnPowerModeChanged", pet, new PowerModeChangedEventArgs(PowerModes.Suspend));
            var dispatched = Dispatcher.CurrentDispatcher.InvokeAsync(() => true).Task;
            PumpUntil(() => dispatched.IsCompleted);
            Check(!(bool)Get(pet, "_renderLoopRunning")! && !((DispatcherTimer)Get(pet, "_attentionTimer")!).IsEnabled,
                "Disposed session callback restarted visual work");
            Console.WriteLine("PASS Fullscreen settings opt-out applies immediately and disposed session callbacks cannot restart polling");
        }
        finally
        {
            save.Stop(); Set(pet, "_settingsDirty", false); pet.DisposeResources(); pet.Close();
        }
    }
    private static void PumpUntil(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(8);
        while (!condition())
        {
            Check(DateTime.UtcNow < until, "Dispatcher state transition timed out");
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(2);
        }
    }
    private sealed class Clock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(double seconds) => _ticks += TimeSpan.FromSeconds(seconds).Ticks;
    }
    private static object? Get(object target, string name) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
    private static object? Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
