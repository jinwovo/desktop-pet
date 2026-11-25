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

internal static class SettingsAccessibilityUiTests
{
    public static void Run(string root, bool capture = false)
    {
        var language = UiText.Language;
        UiText.SetLanguage("en");
        var folder = Path.Combine(root, "settings-accessibility-inbox");
        var pet = new MainWindow(new CharacterLibrary(MainWindow.CharactersRoot, Path.Combine(root, "accessibility-custom")), inboxDirectory: folder);
        pet.Loaded -= typeof(MainWindow).GetMethod("MainWindow_Loaded", BindingFlags.Instance | BindingFlags.NonPublic)!.CreateDelegate<RoutedEventHandler>(pet);
        var preferences = new PetSettings { Visible = false, QuietMode = true, AutoConnectAssistants = false };
        Set(pet, "_settings", preferences);
        var brain = new PetBrain(new PlatformLayout(new[] { new Platform(0, 1920, 1080) }), 128, 122, 500, 958);
        Set(pet, "_brain", brain);
        Set(pet, "_suppressionReasons", PetSuppressionReason.SessionLocked);
        Invoke(pet, "ApplyPresentation", CharacterPresentation.Load(Path.Combine(MainWindow.CharactersRoot, "Companion")));
        var save = (DispatcherTimer)Get(pet, "_settingsSaveTimer")!;
        save.Interval = TimeSpan.FromDays(1);
        var settings = new SettingsWindow(pet) { ShowActivated = false, MaxHeight = 700 };
        var labels = new[]
        {
            ("LanguageCombo", "언어"), ("CharacterCombo", "캐릭터"), ("PreviewStateCombo", "동작 미리보기"),
            ("MonitorTargetCombo", "펫을 데려올 화면"), ("FrameRateCombo", "프레임 제한"),
            ("ScaleSlider", "크기"), ("SpeedSlider", "걷기 속도"),
            ("FocusMinutesBox", "집중 시간(분)"), ("BreakMinutesBox", "휴식 시간(분)"),
            ("QuietStartBox", "방해 금지 시작 시간"), ("QuietEndBox", "방해 금지 종료 시간")
        };
        try
        {
            settings.Show(); Pump();
            var focusBox = (TextBox)settings.FindName("FocusMinutesBox"); var breakBox = (TextBox)settings.FindName("BreakMinutesBox");
            var start = (TextBox)settings.FindName("QuietStartBox"); var end = (TextBox)settings.FindName("QuietEndBox");
            focusBox.Text = " 42 "; breakBox.Text = "partial"; start.Text = "23:"; end.Text = " 07:10 ";
            var draft = (focusBox.Text, breakBox.Text, start.Text, end.Text);
            var presentation = Get(pet, "_currentPresentation");
            foreach (var locale in new[] { "ko", "en", "zh-CN" })
            {
                UiText.SetLanguage(locale); Pump();
                foreach (var (name, key) in labels)
                {
                    var element = (UIElement)settings.FindName(name);
                    var peer = UIElementAutomationPeer.CreatePeerForElement(element) ?? throw new Exception("No native automation peer for " + name);
                    Check(peer.GetName() == UiText.Get(key), $"Native automation name missing/wrong: {name}, locale={locale}, actual={peer.GetName()}");
                    if (locale != "ko") Check(peer.GetName() != key, "Accessible name was not registered for translation: " + name);
                }
                var focusPeer = UIElementAutomationPeer.CreatePeerForElement(focusBox)!;
                var quietPeer = UIElementAutomationPeer.CreatePeerForElement(start)!;
                Check(focusPeer.GetHelpText() == UiText.Get("집중 1~180분, 휴식 1~60분. 시간 적용을 눌러 반영하세요.") &&
                    UIElementAutomationPeer.CreatePeerForElement(breakBox)!.GetHelpText() == focusPeer.GetHelpText() &&
                    quietPeer.GetHelpText() == UiText.Get("로컬 시간 HH:mm. 자정을 넘길 수 있고 같은 시간은 예약하지 않아요.") &&
                    UIElementAutomationPeer.CreatePeerForElement(end)!.GetHelpText() == quietPeer.GetHelpText(), "Accessible duration/time format help did not relocalize");
                Check((focusBox.Text, breakBox.Text, start.Text, end.Text) == draft && ReferenceEquals(presentation, Get(pet, "_currentPresentation")) && !(bool)Get(pet, "_settingsDirty")!,
                    "Accessible metadata relabeling changed raw drafts, active presentation or preferences");
            }
            var before = AutomationProperties.GetName(focusBox); settings.Close();
            UiText.SetLanguage("en"); Invoke(settings, "RefreshAccessibleNames");
            Check(AutomationProperties.GetName(focusBox) == before && !(bool)Get(pet, "_settingsDirty")!, "Closed settings updated accessible metadata or preferences");
            Console.WriteLine("PASS Native automation peers identify all eleven Settings inputs and localized time-format help without changing drafts, presentation or saved preferences");
        }
        finally { settings.Close(); save.Stop(); Set(pet, "_settingsDirty", false); pet.DisposeResources(); pet.Close(); UiText.SetLanguage(language); }
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
