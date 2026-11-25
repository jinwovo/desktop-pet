using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopPet.App;
using DesktopPet.Core;
using WinForms = System.Windows.Forms;

using System.Windows.Input;

internal static class GalleryShortcutUiTests
{
    public static void Run(string root, bool capture = false)
    {
        var language = UiText.Language;
        UiText.SetLanguage("en");
        var folder = Path.Combine(root, "gallery-shortcut-inbox");
        var pet = new MainWindow(new CharacterLibrary(MainWindow.CharactersRoot, Path.Combine(root, "gallery-shortcut-custom")), inboxDirectory: folder);
        pet.Loaded -= typeof(MainWindow).GetMethod("MainWindow_Loaded", BindingFlags.Instance | BindingFlags.NonPublic)!.CreateDelegate<RoutedEventHandler>(pet);
        var preferences = new PetSettings { Visible = false, QuietMode = true, AutoConnectAssistants = false };
        Set(pet, "_settings", preferences);
        var brain = new PetBrain(new PlatformLayout(new[] { new Platform(0, 1920, 1080) }), 128, 122, 500, 958);
        Set(pet, "_brain", brain);
        Set(pet, "_suppressionReasons", PetSuppressionReason.SessionLocked);
        Invoke(pet, "ApplyPresentation", CharacterPresentation.Load(Path.Combine(MainWindow.CharactersRoot, "Companion")));
        var save = (DispatcherTimer)Get(pet, "_settingsSaveTimer")!;
        save.Interval = TimeSpan.FromDays(1);
        var names = new[] { "Companion", "Nimbus", "내 친구 你好" };
        var scans = 0;
        var gallery = new CharacterGalleryWindow(pet, catalogDiscovery: _ => { Interlocked.Increment(ref scans); return names; });
        try
        {
            gallery.Show(); Wait(gallery.LoadingTask); gallery.UpdateLayout(); Pump();
            var search = (TextBox)gallery.FindName("SearchBox"); var category = (ComboBox)gallery.FindName("CategoryCombo");
            var cards = (ListBox)gallery.FindName("CharacterCards");
            var first = gallery.AllCharacters.First(item => item.Name == "Companion"); cards.SelectedItem = first; gallery.UpdateLayout(); Pump();

            var findBinding = gallery.InputBindings.OfType<KeyBinding>().FirstOrDefault(b => b.Command == ApplicationCommands.Find && b.Key == Key.F && b.Modifiers == ModifierKeys.Control);
            var clearBinding = gallery.InputBindings.OfType<KeyBinding>().FirstOrDefault(b => b.Command is RoutedUICommand cmd && cmd.Name == "ClearGalleryFiltersCommand" && b.Key == Key.L && b.Modifiers == ModifierKeys.Control);
            Check(findBinding != null && clearBinding != null, "Gallery shortcut input bindings were not registered");
            var loading = gallery.LoadingTask;

            Check(AutomationProperties.GetAcceleratorKey(search) == "Ctrl+F", "Gallery search accelerator key was not exposed");
            Check(AutomationProperties.GetHelpText(gallery) == UiText.Get("Ctrl+F로 이름 검색, Ctrl+L로 검색·분류 초기화, F5로 새로고침합니다."), "Gallery shortcut help text was not exposed");

            foreach (var locale in new[] { "en", "zh-CN", "ko" })
            {
                UiText.SetLanguage(locale); Pump(); gallery.UpdateLayout();
                var expectedHint = locale switch
                {
                    "en" => "Ctrl+F to search names, Ctrl+L to clear search and category, F5 to refresh.",
                    "zh-CN" => "Ctrl+F 搜索名称，Ctrl+L 重置搜索和类别，F5 刷新。",
                    _ => "Ctrl+F로 이름 검색, Ctrl+L로 검색·분류 초기화, F5로 새로고침합니다."
                };
                Check(((TextBlock)gallery.FindName("GalleryShortcutHint")).Text == UiText.Get("Ctrl+F 검색 · Ctrl+L 초기화 · F5 새로고침"), "Visible gallery hint did not localize");
                Check(AutomationProperties.GetHelpText(gallery) == expectedHint, $"Gallery shortcut hint did not localize: locale={locale}, actual={AutomationProperties.GetHelpText(gallery)}");
                search.Text = "Companion"; category.SelectedValue = CharacterGalleryCategory.Original;
                cards.Focus();
                ((RoutedCommand)findBinding!.Command).Execute(null, gallery);
                Check(search.IsKeyboardFocusWithin && search.SelectedText == "Companion", "Find did not focus/select the raw query");
                ((RoutedCommand)clearBinding!.Command).Execute(null, gallery);
                Check(search.Text.Length == 0 && Equals(category.SelectedValue, CharacterGalleryCategory.All) &&
                    (cards.SelectedItem as CharacterGalleryItem)?.Name == "Companion", "Clear lost the visible selection");
            }

            UiText.SetLanguage("en"); Pump();
            search.Text = "Companion"; category.SelectedValue = CharacterGalleryCategory.Original; Pump();
            Check(search.Text == "Companion" && Equals(category.SelectedValue, CharacterGalleryCategory.Original) && (cards.SelectedItem as CharacterGalleryItem)?.Name == "Companion", "Gallery filter state was not applied");

            ((RoutedCommand)findBinding!.Command).Execute(null, gallery);
            Check(search.IsKeyboardFocusWithin && search.SelectedText == search.Text, "Ctrl+F did not focus and select all search text");

            ((RoutedCommand)clearBinding!.Command).Execute(null, gallery);
            Check(search.Text.Length == 0 && Equals(category.SelectedValue, CharacterGalleryCategory.All) && (cards.SelectedItem as CharacterGalleryItem)?.Name == "Companion", "Ctrl+L did not clear filters while retaining selected Companion");

            Check(scans == 1 && ReferenceEquals(loading, gallery.LoadingTask), "Search shortcuts restarted catalog discovery");
            var find = (RoutedCommand)findBinding!.Command; var clear = (RoutedCommand)clearBinding!.Command;
            search.IsEnabled = false; Check(!find.CanExecute(null, gallery), "Disabled search retained Find");
            find.Execute(null, gallery); search.IsEnabled = true;
            Check(!clear.CanExecute(null, gallery), "Empty filters retained Clear");
            search.Text = "unchanged after close";
            Check(pet.CurrentCharacterName == "Companion" && !(bool)Get(pet, "_settingsDirty")!, "Gallery shortcuts changed current pet or marked settings dirty");

            var before = AutomationProperties.GetName(search); gallery.Close();
            Check(!find.CanExecute(null, gallery) && !clear.CanExecute(null, gallery), "Closed gallery retained commands");
            find.Execute(null, gallery); clear.Execute(null, gallery);
            Check(search.Text == "unchanged after close" && scans == 1, "Closed commands changed filters or restarted discovery"); UiText.SetLanguage("ko"); Invoke(gallery, "RefreshGalleryAccessibleNames");
            Check(AutomationProperties.GetName(search) == before && !(bool)Get(pet, "_settingsDirty")!, "Closed gallery relabeled metadata or changed preferences");

            using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
            gallery = new CharacterGalleryWindow(pet, catalogDiscovery: token =>
            {
                Interlocked.Increment(ref scans); entered.Set();
                Check(release.Wait(TimeSpan.FromSeconds(20), token), "Blocked catalog was not released"); return names;
            });
            try
            {
                gallery.Show(); Check(entered.Wait(TimeSpan.FromSeconds(10)), "Slow catalog did not start");
                loading = gallery.LoadingTask; search = (TextBox)gallery.FindName("SearchBox"); category = (ComboBox)gallery.FindName("CategoryCombo");
                search.Text = "pending query"; category.SelectedValue = CharacterGalleryCategory.Custom;
                Check(clear.CanExecute(null, gallery) && !loading.IsCompleted, "Clear was unavailable during discovery");
                clear.Execute(null, gallery); find.Execute(null, gallery);
                Check(search.Text.Length == 0 && search.IsKeyboardFocusWithin && Equals(category.SelectedValue, CharacterGalleryCategory.All) &&
                    ReferenceEquals(loading, gallery.LoadingTask) && scans == 2, "Input while loading restarted discovery or failed to reset filters");
                release.Set(); Wait(loading);
                Invoke(gallery, "Refresh_Click", gallery, new RoutedEventArgs()); Wait(gallery.LoadingTask);
                Check(scans == 3 && gallery.AllCharacters.Count == names.Length && pet.CurrentCharacterName == "Companion" && !(bool)Get(pet, "_settingsDirty")!,
                    "Existing refresh or active pet changed after shortcut use");
            }
            finally { release.Set(); gallery.Close(); }

            Console.WriteLine("PASS Gallery shortcuts focus/select all, clear filters, preserve selection and pet, and expose localized accelerator/help text");
            Console.WriteLine("PASS Search shortcuts remain responsive during discovery without rescanning, preserve explicit refresh and reject disabled/closed actions");
        }
        finally { gallery.Close(); save.Stop(); Set(pet, "_settingsDirty", false); pet.DisposeResources(); pet.Close(); UiText.SetLanguage(language); }
    }
    private static void Wait(Task task)
    {
        var timeout = Stopwatch.StartNew();
        while (!task.IsCompleted && timeout.Elapsed < TimeSpan.FromSeconds(20)) { Pump(); Thread.Sleep(2); }
        Check(task.IsCompleted, "Gallery loading timed out"); task.GetAwaiter().GetResult(); Pump();
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
