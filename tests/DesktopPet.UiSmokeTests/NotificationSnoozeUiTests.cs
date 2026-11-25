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

internal static class NotificationSnoozeUiTests
{
    public static void Run(string root, bool capture = false)
    {
        var language = UiText.Language;
        UiText.SetLanguage("en");
        var folder = Path.Combine(root, "notification-snooze-inbox");
        var quietClock = new Clock();
        using var focus = new FocusTimer(new Clock());
        var pet = new MainWindow(new CharacterLibrary(MainWindow.CharactersRoot, Path.Combine(root, "snooze-custom")), focus, inboxDirectory: folder, quietClock: quietClock);
        pet.Loaded -= typeof(MainWindow).GetMethod("MainWindow_Loaded", BindingFlags.Instance | BindingFlags.NonPublic)!.CreateDelegate<RoutedEventHandler>(pet);
        var preferences = new PetSettings { Visible = true, QuietMode = false, AutoConnectAssistants = false, PauseDuringFullscreen = false, ReducedMotion = true };
        Set(pet, "_settings", preferences);
        var brain = new PetBrain(new PlatformLayout(new[] { new Platform(0, 1920, 1080) }), 128, 122, 500, 958);
        Set(pet, "_brain", brain);
        Set(pet, "_suppressionReasons", PetSuppressionReason.None);
        Invoke(pet, "ApplyPresentation", CharacterPresentation.Load(Path.Combine(MainWindow.CharactersRoot, "Companion")));
        var save = (DispatcherTimer)Get(pet, "_settingsSaveTimer")!;
        save.Interval = TimeSpan.FromDays(1);
        var bubble = new SpeechBubbleWindow(); bubble.SetReducedMotion(true); Set(pet, "_bubble", bubble);
        var acks = 0;
        void Deliver(string title)
        {
            var receipt = (InboxNotification)typeof(InboxNotification).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
                new[] { typeof(PetNotification), typeof(Func<bool>) }, null)!.Invoke(new object[] { new PetNotification { Title = title, Source = "test" }, (Func<bool>)(() => { acks++; return true; }) });
            Invoke(pet, "OnNotification", receipt); Invoke(pet, "StopRenderLoop");
        }
        var factory = typeof(App).Assembly.GetType("DesktopPet.App.PetMenuBuilder")!.GetMethod("Create", BindingFlags.Static | BindingFlags.NonPublic)!;
        WinForms.ContextMenuStrip Build() => (WinForms.ContextMenuStrip)factory.Invoke(null, new object[] { pet, (Action)(() => { }), (Action)(() => { }), (Action)(() => { }) })!;
        using var tray = Build(); using var petMenu = Build();
        void Refresh(WinForms.ContextMenuStrip menu) => typeof(WinForms.ToolStripDropDown).GetMethod("OnOpening", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(menu, new object[] { new CancelEventArgs() });
        WinForms.ToolStripMenuItem Snooze(WinForms.ContextMenuStrip menu) => menu.Items.OfType<WinForms.ToolStripMenuItem>().Single(item =>
            item.Text == UiText.Get("알림 15분 잠시 끄기") || item.Text == UiText.Get("알림 잠시 끄기 해제"));
        try
        {
            pet.StartFocusTimer(); var timerBefore = focus.Snapshot;
            save.Stop(); Set(pet, "_settingsDirty", false);
            Deliver("Before snooze"); Check(bubble.IsShowing, "Visible notification fixture did not open its bubble");
            Refresh(tray); Snooze(tray).PerformClick();
            Check(pet.NotificationsSnoozed && pet.IsQuietNow && !bubble.IsShowing &&
                !((DispatcherTimer)Get(pet, "_notificationPopupTimer")!).IsEnabled && !pet.QuietMode && !pet.QuietHoursEnabled,
                "Snooze did not dismiss only the active popup or changed persisted quiet settings");
            Deliver("During snooze"); Check(!bubble.IsShowing && pet.PendingNotificationCount == 2 && acks == 0, "Snooze lost or popped an incoming unread receipt");
            quietClock.Now += TimeSpan.FromMinutes(15) - TimeSpan.FromTicks(1);
            Check(pet.NotificationsSnoozed && pet.IsQuietNow, "Snooze expired before its deadline");
            quietClock.Now += TimeSpan.FromTicks(1);
            Check(!pet.NotificationsSnoozed && !pet.IsQuietNow && !bubble.IsShowing, "Snooze failed to expire exactly or replayed the saved backlog");
            Deliver("After snooze"); Check(bubble.IsShowing && pet.PendingNotificationCount == 3 && acks == 0, "Fresh arrivals did not resume after snooze");
            Check(focus.Snapshot == timerBefore && pet.IsPetVisible && !(bool)Get(pet, "_settingsDirty")!, "Snooze changed the focus session, visibility or saved preferences");
            pet.SnoozeNotifications(); quietClock.Now += TimeSpan.FromMinutes(10); pet.SnoozeNotifications();
            quietClock.Now += TimeSpan.FromMinutes(5);
            Check(pet.NotificationsSnoozed, "An explicit repeat snooze did not reset its 15-minute deadline");
            pet.ResumeSnoozedNotifications(); Check(!pet.NotificationsSnoozed && !bubble.IsShowing && pet.PendingNotificationCount == 3, "Manual resume replayed or acknowledged saved notifications");
            Console.WriteLine("PASS Fifteen-minute snooze dismisses automatic bubbles, retains arrivals/unread receipts, expires exactly and preserves focus/preferences");

            foreach (var locale in new[] { "en", "zh-CN", "ko" })
            {
                UiText.SetLanguage(locale);
                foreach (var menu in new[] { tray, petMenu })
                {
                    Refresh(menu); var item = Snooze(menu);
                    Check(!item.Checked && item.Text == UiText.Get("알림 15분 잠시 끄기"), "Cached menu failed to localize inactive snooze");
                    item.PerformClick(); Refresh(menu);
                    Check(item.Checked && item.Text == UiText.Get("알림 잠시 끄기 해제") && item.ToolTipText == UiText.Get("자동 말풍선만 잠시 끕니다. 알림은 보관하고 집중 타이머는 계속 진행해요."),
                        "Shared menu did not show localized active snooze state");
                    item.PerformClick(); Check(!pet.NotificationsSnoozed, "Shared resume command did not clear only the temporary snooze");
                }
            }
            pet.SetQuietMode(true); save.Stop(); Set(pet, "_settingsDirty", false);
            pet.SnoozeNotifications(); Refresh(tray); quietClock.Now += TimeSpan.FromMinutes(15);
            Snooze(tray).PerformClick();
            Check(!pet.NotificationsSnoozed, "An expired open Resume menu restarted snooze instead of honoring its displayed action");
            pet.SnoozeNotifications(); pet.ResumeSnoozedNotifications();
            Check(pet.QuietMode && pet.IsQuietNow && !(bool)Get(pet, "_settingsDirty")!, "Resuming snooze disabled manual quiet mode or saved preferences");
            pet.SetQuietMode(false); pet.SetQuietHours(true, 0, 1439); save.Stop(); Set(pet, "_settingsDirty", false);
            pet.SnoozeNotifications(); pet.ResumeSnoozedNotifications();
            Check(pet.QuietHoursEnabled && pet.IsQuietNow && !bubble.IsShowing && focus.Snapshot == timerBefore, "Resuming snooze disabled daily quiet hours or changed focus state");
            preferences.Visible = false; pet.SnoozeNotifications(); pet.ResumeSnoozedNotifications(); Check(!pet.IsPetVisible, "Snooze commands revealed a manually hidden pet");
            pet.SnoozeNotifications(); var deadline = Get(pet, "_notificationSnoozeUntilUtc");
            save.Stop(); Set(pet, "_settingsDirty", false); pet.DisposeResources();
            pet.SnoozeNotifications(); pet.ResumeSnoozedNotifications();
            Check(!pet.NotificationsSnoozed && Equals(deadline, Get(pet, "_notificationSnoozeUntilUtc")) && acks == 0, "Disposed snooze commands mutated state or acknowledged receipts");
            Console.WriteLine("PASS Both localized menus toggle session-only snooze while manual/scheduled quiet, hidden preference and disposal guards remain intact");
        }
        finally { save.Stop(); Set(pet, "_settingsDirty", false); pet.DisposeResources(); pet.Close(); UiText.SetLanguage(language); }
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 11, 25, 9, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
        public override long GetTimestamp() => 0;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
    private static object? Get(object obj, string name) => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(obj);
    private static void Set(object obj, string name, object value) => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(obj, value);
    private static object? Invoke(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(obj, args);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
