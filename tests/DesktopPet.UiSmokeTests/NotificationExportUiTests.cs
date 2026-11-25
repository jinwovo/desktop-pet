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

internal static class NotificationExportUiTests
{
    public static void Run(string root, bool capture = false)
    {
        var language = UiText.Language;
        UiText.SetLanguage("en");
        var folder = Path.Combine(root, "notification-export-inbox");
        var pet = new MainWindow(new CharacterLibrary(MainWindow.CharactersRoot, Path.Combine(root, "export-custom")), inboxDirectory: folder);
        pet.Loaded -= typeof(MainWindow).GetMethod("MainWindow_Loaded", BindingFlags.Instance | BindingFlags.NonPublic)!.CreateDelegate<RoutedEventHandler>(pet);
        var preferences = new PetSettings { Visible = false, QuietMode = true, AutoConnectAssistants = false };
        Set(pet, "_settings", preferences);
        var brain = new PetBrain(new PlatformLayout(new[] { new Platform(0, 1920, 1080) }), 128, 122, 500, 958);
        Set(pet, "_brain", brain);
        Set(pet, "_suppressionReasons", PetSuppressionReason.SessionLocked);
        Invoke(pet, "ApplyPresentation", CharacterPresentation.Load(Path.Combine(MainWindow.CharactersRoot, "Companion")));
        var save = (DispatcherTimer)Get(pet, "_settingsSaveTimer")!;
        save.Interval = TimeSpan.FromDays(1);
        NotificationReviewWindow? window = null;
        var acknowledgements = 0;
        void Deliver(PetNotification notification)
        {
            var receipt = (InboxNotification)typeof(InboxNotification).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(PetNotification), typeof(Func<bool>) }, null)!.Invoke(new object[] { notification, (Func<bool>)(() => { acknowledgements++; return true; }) });
            Invoke(pet, "OnNotification", receipt);
        }
        try
        {
            Deliver(new PetNotification { Title = "First · 한글 你好 \"quoted\"", Message = "Line one\n<raw>&second", Source = "codex", CreatedAtUtc = new DateTimeOffset(2026, 11, 24, 10, 5, 0, TimeSpan.Zero) });
            Deliver(new PetNotification { Title = "Second", Message = "Body two", Source = "test" });
            var snapshot = pet.GetPendingNotificationsSnapshot();
            SavedNotificationExportWriterTests.Run(root, snapshot);
            var chosen = Path.Combine(root, "visible-notifications.json");
            var changeFilterInsidePicker = true;
            window = new NotificationReviewWindow(pet, () =>
            {
                if (changeFilterInsidePicker) ((TextBox)window!.FindName("SearchBox")).Text = "Body two";
                return chosen;
            });
            window.Show(); Pump();
            var search = (TextBox)window.FindName("SearchBox");
            var button = (Button)window.FindName("ExportNotificationsButton");
            var status = (TextBox)window.FindName("ExportStatus");
            search.Text = "First"; Click(button); Wait(window.NotificationExportOperation);
            using (var saved = System.Text.Json.JsonDocument.Parse(File.ReadAllText(chosen)))
            {
                var items = saved.RootElement.GetProperty("notifications");
                Check(items.GetArrayLength() == 1 && items[0].GetProperty("title").GetString() == snapshot[0].Title && search.Text == "Body two",
                    "Export captured its filtered payload after the filename picker changed the view");
            }
            changeFilterInsidePicker = false;
            var original = File.ReadAllBytes(chosen); Click(button); Wait(window.NotificationExportOperation);
            Check(File.ReadAllBytes(chosen).SequenceEqual(original) && status.Text == UiText.Get("알림을 저장하지 못했어요. 새 파일 이름으로 다시 시도해주세요."),
                "Existing JSON was overwritten or failure feedback was absent");
            foreach (var locale in new[] { "en", "zh-CN", "ko" })
            {
                UiText.SetLanguage(locale); Pump();
                Check(status.Text == UiText.Get("알림을 저장하지 못했어요. 새 파일 이름으로 다시 시도해주세요.") && button.IsEnabled,
                    "Export error feedback/retry did not relocalize");
                window.Width = window.MinWidth; window.Height = window.MinHeight; window.UpdateLayout();
                Check(button.ActualWidth > 0 && button.TranslatePoint(new Point(0, 0), window).X >= 0 &&
                    ((Button)window.FindName("CloseButton")).TranslatePoint(new Point(72, 0), window).X < window.ActualWidth,
                    "Export footer overflowed the minimum-size window");
                if (capture) Program.SaveWindowSnapshot(window, $"notification-export-error-{locale}.png");
            }
            chosen = Path.Combine(root, "visible-retry.json"); Click(button); Wait(window.NotificationExportOperation);
            Check(File.Exists(chosen) && status.Text == UiText.Format("알림 {0}개를 저장했어요.", 1),
                $"Explicit new-filename export retry failed: exists={File.Exists(chosen)}, visible={window.VisibleItems.Count}, enabled={button.IsEnabled}, status={status.Text}");
            search.Text = "no matching notifications";
            Check(!button.IsEnabled && window.VisibleItems.Count == 0, "Export remained actionable for an empty filtered view");
            Check(acknowledgements == 0 && pet.PendingNotificationCount == 2 && !pet.IsPetVisible && !(bool)Get(pet, "_settingsDirty")!,
                "Export acknowledged notifications, revealed the pet or changed preferences");
            window.Close();

            var writerEntered = false;
            window = new NotificationReviewWindow(pet, () => Path.Combine(root, "cancelled-visible.json"), async (_, _, token) =>
            {
                writerEntered = true;
                await Task.Delay(Timeout.Infinite, token);
            });
            window.Show(); Pump(); button = (Button)window.FindName("ExportNotificationsButton"); status = (TextBox)window.FindName("ExportStatus");
            Click(button); Check(writerEntered && !button.IsEnabled && !window.NotificationExportOperation.IsCompleted, "Export did not enter asynchronous busy state");
            foreach (var locale in new[] { "en", "zh-CN", "ko" })
            {
                UiText.SetLanguage(locale); Pump();
                Check((string)button.Content == UiText.Get("알림 저장 중…") && status.Text == UiText.Format("알림 {0}개를 저장하고 있어요.", 2), "Busy export label/count did not relocalize");
            }
            var closedText = status.Text; var operation = window.NotificationExportOperation; window.Close(); Wait(operation);
            Check(!operation.IsFaulted && status.Text == closedText && !File.Exists(Path.Combine(root, "cancelled-visible.json")),
                "Closing failed to cancel export or updated closed UI");

            var writerCalls = 0;
            window = new NotificationReviewWindow(pet, () => { window!.Close(); return Path.Combine(root, "closed-picker.json"); }, (_, _, _) => { writerCalls++; return Task.CompletedTask; });
            window.Show(); Pump(); Click((Button)window.FindName("ExportNotificationsButton"));
            Check(writerCalls == 0 && !File.Exists(Path.Combine(root, "closed-picker.json")), "Closing inside the filename picker started a stale export");
            Console.WriteLine("PASS Visible JSON snapshots precede the picker, retain raw fields/unread receipts, protect existing files and offer localized retries");
            Console.WriteLine("PASS JSON save remains asynchronous, preserves minimum-size controls and cancels closed owners without late UI updates");
        }
        finally
        {
            window?.Close();
            if (window is not null) Wait(window.NotificationExportOperation);
            save.Stop(); Set(pet, "_settingsDirty", false); pet.DisposeResources(); pet.Close(); UiText.SetLanguage(language);
        }
    }
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Wait(Task operation)
    {
        var timeout = Stopwatch.StartNew();
        while (!operation.IsCompleted && timeout.Elapsed < TimeSpan.FromSeconds(20)) { Pump(); Thread.Sleep(2); }
        Check(operation.IsCompleted, "Notification export operation timed out"); operation.GetAwaiter().GetResult(); Pump();
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
