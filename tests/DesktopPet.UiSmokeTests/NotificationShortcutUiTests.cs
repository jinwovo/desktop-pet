using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopPet.App;
using DesktopPet.Core;
using WinForms = System.Windows.Forms;

internal static class NotificationShortcutUiTests
{
    public static void Run(string root, bool capture = false)
    {
        var language = UiText.Language;
        UiText.SetLanguage("en");
        var folder = Path.Combine(root, "notification-shortcut-inbox");
        var pet = new MainWindow(new CharacterLibrary(MainWindow.CharactersRoot, Path.Combine(root, "shortcut-custom")), inboxDirectory: folder);
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
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var firstCalls = 0; var secondCalls = 0; var pickerCalls = 0;
        void Deliver(string title, Func<bool> acknowledge)
        {
            var receipt = (InboxNotification)typeof(InboxNotification).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
                new[] { typeof(PetNotification), typeof(Func<bool>) }, null)!.Invoke(new object[] { new PetNotification { Title = title, Source = "test" }, acknowledge });
            Invoke(pet, "OnNotification", receipt);
        }
        try
        {
            Deliver("First marker one", () => { firstCalls++; return true; });
            Deliver("Second marker two", () => { secondCalls++; entered.Set(); return release.Wait(TimeSpan.FromSeconds(20)); });
            var path = Path.Combine(root, "keyboard-export.json");
            window = new NotificationReviewWindow(pet, () => { pickerCalls++; return path; }); window.Show(); Pump();
            var search = (TextBox)window.FindName("SearchBox"); var list = (ListBox)window.FindName("NotificationItems");
            RoutedCommand Binding(Key key, ModifierKeys modifiers) => (RoutedCommand)window.InputBindings.OfType<KeyBinding>().Single(binding => binding.Key == key && binding.Modifiers == modifiers).Command;
            var saveCommand = Binding(Key.S, ModifierKeys.Control);
            var read = Binding(Key.R, ModifierKeys.Control | ModifierKeys.Shift); var clear = Binding(Key.L, ModifierKeys.Control);
            Check(ReferenceEquals(saveCommand, ApplicationCommands.Save) && window.InputBindings.OfType<KeyBinding>().Any(binding => binding.Key == Key.F) &&
                !window.InputBindings.OfType<KeyBinding>().Any(binding => binding.Key == Key.Delete), "Shortcuts replaced Find or captured normal Delete editing");
            foreach (var locale in new[] { "en", "zh-CN", "ko" })
            {
                UiText.SetLanguage(locale); Pump(); search.Text = "First marker";
                Check(clear.CanExecute(null, window) && list.Items.Count == 1, "Clear shortcut did not become available for an active query");
                clear.Execute(null, window);
                Check(search.Text == "" && list.Items.Count == 2 && !clear.CanExecute(null, window), "Ctrl+L did not clear both criteria or retained stale availability");
                foreach (var (name, key) in new[] { ("ExportNotificationsButton", "보이는 알림을 JSON으로 저장 (Ctrl+S)"),
                    ("MarkSelectedReadButton", "선택한 알림만 읽음 처리 (Ctrl+Shift+R)"), ("ClearFiltersButton", "검색어와 출처 필터 초기화 (Ctrl+L)") })
                {
                    var button = (Button)window.FindName(name);
                    Check((string)button.ToolTip == UiText.Get(key) && AutomationProperties.GetHelpText(button) == UiText.Get(key), "Shortcut tooltip/accessibility help did not localize");
                }
            }
            search.Text = "no matching receipts";
            Check(!saveCommand.CanExecute(null, window) && !read.CanExecute(null, window), "Empty visible results retained save/read commands");
            saveCommand.Execute(null, window); read.Execute(null, window);
            Check(pickerCalls == 0 && firstCalls == 0 && secondCalls == 0, "Forced empty-result command opened a picker or acknowledged a receipt");
            clear.Execute(null, window);
            Check(saveCommand.CanExecute(null, window), "Ctrl+S disabled for a nonempty visible snapshot");
            saveCommand.Execute(null, window); WaitUntil(() => window.NotificationExportOperation.IsCompleted); window.NotificationExportOperation.GetAwaiter().GetResult();
            using (var data = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path)))
                Check(data.RootElement.GetProperty("notifications").GetArrayLength() == 2 && pickerCalls == 1, "Ctrl+S did not export the visible snapshot once");
            list.SelectedItem = window.AllItems.Single(item => item.Title == "Second marker two");
            Check(read.CanExecute(null, window), "Selected-read shortcut unavailable for a pending receipt");
            read.Execute(null, window); Check(entered.Wait(TimeSpan.FromSeconds(10)) && !read.CanExecute(null, window), "Read shortcut did not honor the worker busy guard");
            read.Execute(null, window); Check(secondCalls == 1 && firstCalls == 0, "Forced busy read command started duplicate or unrelated receipt IO");
            release.Set(); WaitUntil(() => !pet.IsAcknowledgingAll && !(bool)Get(window, "_selectionBusy")!); Pump();
            Check(pet.PendingNotificationCount == 1 && firstCalls == 0 && secondCalls == 1, "Ctrl+Shift+R removed more than its selected receipt");
            window.Close();
            Check(!saveCommand.CanExecute(null, window) && !read.CanExecute(null, window) && !clear.CanExecute(null, window), "Closed owner retained keyboard actions");
            saveCommand.Execute(null, window); read.Execute(null, window); clear.Execute(null, window);
            Check(pickerCalls == 1 && firstCalls == 0, "Forced closed command opened a picker or acknowledged another receipt");

            var writers = 0;
            window = new NotificationReviewWindow(pet, () => Path.Combine(root, "busy-shortcut.json"), async (_, _, token) =>
            { writers++; await Task.Delay(Timeout.Infinite, token); }); window.Show(); Pump();
            saveCommand.Execute(null, window); Check(writers == 1 && !saveCommand.CanExecute(null, window), "Save shortcut ignored an active export");
            saveCommand.Execute(null, window); Check(writers == 1, "Forced save shortcut started another writer");
            var pending = window.NotificationExportOperation; window.Close(); WaitUntil(() => pending.IsCompleted); pending.GetAwaiter().GetResult();
            Check(!pet.IsPetVisible && !(bool)Get(pet, "_settingsDirty")!, "Keyboard actions revealed the pet or changed saved preferences");
            Console.WriteLine("PASS Ctrl+S/Ctrl+Shift+R/Ctrl+L route existing actions, localize their accessible help and preserve ordinary Find/Delete bindings");
            Console.WriteLine("PASS Keyboard commands recheck active export/receipt and closed-owner guards even when execution is forced");
        }
        finally
        {
            release.Set(); window?.Close();
            WaitUntil(() => !pet.IsAcknowledgingAll && (window?.NotificationExportOperation.IsCompleted ?? true));
            save.Stop(); Set(pet, "_settingsDirty", false); pet.DisposeResources(); pet.Close(); UiText.SetLanguage(language);
        }
    }
    private static void WaitUntil(Func<bool> done)
    {
        var timeout = Stopwatch.StartNew();
        while (!done() && timeout.Elapsed < TimeSpan.FromSeconds(20)) { Pump(); Thread.Sleep(2); }
        Check(done(), "Keyboard action operation timed out");
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
