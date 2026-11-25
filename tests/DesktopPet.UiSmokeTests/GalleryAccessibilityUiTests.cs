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

internal static class GalleryAccessibilityUiTests
{
    public static void Run(string root, bool capture = false)
    {
        var language = UiText.Language;
        UiText.SetLanguage("en");
        var folder = Path.Combine(root, "gallery-accessibility-inbox");
        var pet = new MainWindow(new CharacterLibrary(MainWindow.CharactersRoot, Path.Combine(root, "gallery-accessibility-custom")), inboxDirectory: folder);
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
        var gallery = new CharacterGalleryWindow(pet, catalogDiscovery: _ => names);
        try
        {
            gallery.Show(); Wait(gallery.LoadingTask); gallery.UpdateLayout(); Pump();
            var search = (TextBox)gallery.FindName("SearchBox"); var category = (ComboBox)gallery.FindName("CategoryCombo");
            var cards = (ListBox)gallery.FindName("CharacterCards");
            var first = gallery.AllCharacters.First(item => item.Name == "Companion"); cards.SelectedItem = first; gallery.UpdateLayout(); Pump();
            var firstRow = (ListBoxItem)cards.ItemContainerGenerator.ContainerFromItem(first);
            var firstPeer = UIElementAutomationPeer.CreatePeerForElement(firstRow)!;
            Console.WriteLine("INFO Gallery row native name already equals its authored name: " + (firstPeer.GetName() == first.Name));
            foreach (var locale in new[] { "en", "zh-CN", "ko" })
            {
                UiText.SetLanguage(locale); Pump(); gallery.UpdateLayout();
                var expected = locale switch
                {
                    "en" => new[] { "Search character names", "Character category", "Characters" },
                    "zh-CN" => new[] { "搜索角色名称", "角色类别", "角色列表" },
                    _ => new[] { "캐릭터 이름 검색", "캐릭터 종류", "캐릭터 목록" }
                };
                foreach (var (element, index) in new (UIElement, int)[] { (search, 0), (category, 1), (cards, 2) })
                {
                    var peer = UIElementAutomationPeer.CreatePeerForElement(element)!;
                    Check(peer.GetName() == expected[index], $"Gallery automation name did not localize: index={index}, locale={locale}, actual={peer.GetName()}");
                }
                Check(UIElementAutomationPeer.CreatePeerForElement(search)!.GetHelpText() == UiText.Get("이름으로 검색 · 한글 또는 영어"), "Gallery search format help did not localize");
                Check(UIElementAutomationPeer.CreatePeerForElement(cards)!.GetHelpText() == UiText.Get("선택하면 미리 볼 수 있어요. 함께하기를 눌러야 현재 캐릭터를 바꿉니다."), "Gallery list help did not explain explicit application");
                Check((cards.SelectedItem as CharacterGalleryItem)?.Name == "Companion", "Relabeling changed the selected card");
                foreach (var item in gallery.AllCharacters)
                {
                    cards.ScrollIntoView(item); gallery.UpdateLayout(); Pump();
                    var row = (ListBoxItem)cards.ItemContainerGenerator.ContainerFromItem(item);
                    var peer = UIElementAutomationPeer.CreatePeerForElement(row)!;
                    Check(peer.GetName() == item.Name && peer.GetHelpText() == item.CategoryLabel, "Card automation exposed record details or translated its authored name");
                }
            }
            search.Text = "Companion"; category.SelectedValue = CharacterGalleryCategory.Original; Pump();
            var filteredSelection = (cards.SelectedItem as CharacterGalleryItem)?.Name;
            UiText.SetLanguage("en"); Pump();
            Check(search.Text == "Companion" && Equals(category.SelectedValue, CharacterGalleryCategory.Original) &&
                (cards.SelectedItem as CharacterGalleryItem)?.Name == filteredSelection && cards.Items.Count == 1 &&
                pet.CurrentCharacterName == "Companion" && !(bool)Get(pet, "_settingsDirty")!, "Gallery metadata changes applied/filter-reset the user's state");
            var before = AutomationProperties.GetName(search); gallery.Close(); UiText.SetLanguage("ko"); Invoke(gallery, "RefreshGalleryAccessibleNames");
            Check(AutomationProperties.GetName(search) == before && !(bool)Get(pet, "_settingsDirty")!, "Closed gallery relabeled metadata or changed preferences");
            Console.WriteLine("PASS Gallery search/category/list and authored Unicode card names expose localized native automation metadata in three locales");
            Console.WriteLine("PASS Gallery accessibility updates preserve filtering, selection, current pet and closed-window ownership");
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
