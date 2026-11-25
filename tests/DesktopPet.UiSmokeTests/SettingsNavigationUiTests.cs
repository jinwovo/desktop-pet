using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopPet.App;
using DesktopPet.Core;
using WinForms = System.Windows.Forms;

internal static class SettingsNavigationUiTests
{
    public static void Run(string root, bool capture = false)
    {
        var language = UiText.Language;
        UiText.SetLanguage("en");
        var folder = Path.Combine(root, "settings-navigation-inbox");
        using var timer = new FocusTimer(new FixedClock());
        var pet = new MainWindow(new CharacterLibrary(MainWindow.CharactersRoot, Path.Combine(root, "navigation-custom")), focusTimer: timer, inboxDirectory: folder);
        pet.Loaded -= typeof(MainWindow).GetMethod("MainWindow_Loaded", BindingFlags.Instance | BindingFlags.NonPublic)!.CreateDelegate<RoutedEventHandler>(pet);
        var preferences = new PetSettings { Visible = false, QuietMode = true, AutoConnectAssistants = false };
        Set(pet, "_settings", preferences);
        var brain = new PetBrain(new PlatformLayout(new[] { new Platform(0, 1920, 1080) }), 128, 122, 500, 958);
        Set(pet, "_brain", brain);
        Set(pet, "_suppressionReasons", PetSuppressionReason.SessionLocked);
        Invoke(pet, "ApplyPresentation", CharacterPresentation.Load(Path.Combine(MainWindow.CharactersRoot, "Companion")));
        var save = (DispatcherTimer)Get(pet, "_settingsSaveTimer")!;
        save.Interval = TimeSpan.FromDays(1);
        var settings = new SettingsWindow(pet) { ShowActivated = true, MaxHeight = 500 };
        try
        {
            settings.Show(); settings.Activate(); Pump();
            var scroll = (ScrollViewer)settings.Content;
            var focusBox = (TextBox)settings.FindName("FocusMinutesBox"); var breakBox = (TextBox)settings.FindName("BreakMinutesBox");
            focusBox.Text = " 39 "; breakBox.Text = "pending";
            var draft = (focusBox.Text, breakBox.Text); var focus = timer.Snapshot;
            var presentation = Get(pet, "_currentPresentation");
            var routes = new[] { (Key.D1, "character", "CharacterCombo"), (Key.D2, "motion", "ScaleSlider"),
                (Key.D3, "focus", "FocusMinutesBox"), (Key.D4, "notifications", "QuietHoursCheck") };
            RoutedCommand? command = null;
            foreach (var locale in new[] { "en", "zh-CN", "ko" })
            {
                UiText.SetLanguage(locale); Pump();
                foreach (var (key, parameter, targetName) in routes)
                {
                    var binding = settings.InputBindings.OfType<KeyBinding>().Single(item => item.Key == key && item.Modifiers == ModifierKeys.Control);
                    command = (RoutedCommand)binding.Command;
                    var target = (FrameworkElement)settings.FindName(targetName);
                    Check(Equals(binding.CommandParameter, parameter) && command.CanExecute(parameter, settings), "Settings section route is missing or unavailable");
                    command.Execute(parameter, settings); settings.UpdateLayout(); Pump();
                    var point = target.TranslatePoint(new Point(0, 0), scroll);
                    Check(target.IsKeyboardFocusWithin && point.Y >= -4 && point.Y + target.ActualHeight <= scroll.ViewportHeight + 4,
                        $"Settings section did not focus/scroll into view: {targetName}, y={point.Y}, height={target.ActualHeight}, viewport={scroll.ViewportHeight}");
                    Check((focusBox.Text, breakBox.Text) == draft && timer.Snapshot == focus && ReferenceEquals(presentation, Get(pet, "_currentPresentation")) && !(bool)Get(pet, "_settingsDirty")!,
                        "Section navigation applied drafts, changed focus duration/presentation or scheduled a save");
                }
                Check(scroll.VerticalOffset > 0 && ((TextBlock)settings.FindName("SettingsNavigationHint")).Text == UiText.Get("Ctrl+1~4로 설정 구역 이동") &&
                    AutomationProperties.GetHelpText(settings) == UiText.Get("Ctrl+1 캐릭터, Ctrl+2 크기·속도, Ctrl+3 집중, Ctrl+4 알림 설정으로 이동합니다."), "Section navigation hint/window accessibility help did not localize");
                if (capture) Program.SaveWindowSnapshot(settings, $"settings-navigation-{locale}.png");
            }
            var before = Keyboard.FocusedElement;
            Check(!command!.CanExecute("unknown", settings), "Unknown section parameter was actionable");
            command.Execute("unknown", settings); Check(ReferenceEquals(before, Keyboard.FocusedElement), "Unknown section command moved keyboard focus");
            var character = (ComboBox)settings.FindName("CharacterCombo"); character.IsEnabled = false;
            Check(!command.CanExecute("character", settings), "Disabled character selector was actionable");
            command.Execute("character", settings); Check(ReferenceEquals(before, Keyboard.FocusedElement), "Forced disabled navigation moved focus"); character.IsEnabled = true;
            focusBox.Visibility = Visibility.Collapsed;
            Check(!command.CanExecute("focus", settings), "Hidden editor was actionable"); command.Execute("focus", settings); focusBox.Visibility = Visibility.Visible;
            var closedHint = AutomationProperties.GetHelpText(settings); settings.Close();
            foreach (var (_, parameter, _) in routes)
            {
                Check(!command.CanExecute(parameter, settings), "Closed Settings retained section shortcuts"); command.Execute(parameter, settings);
            }
            UiText.SetLanguage("en"); Invoke(settings, "RefreshSettingsNavigationHelp");
            Check(AutomationProperties.GetHelpText(settings) == closedHint && (focusBox.Text, breakBox.Text) == draft && !(bool)Get(pet, "_settingsDirty")!,
                "Closed section commands changed drafts, metadata or preferences");
            Console.WriteLine("PASS Ctrl+1–4 focuses and scrolls all four Settings sections into view without applying drafts or changing presentation in three locales");
            Console.WriteLine("PASS Section hints localize while unknown, disabled, hidden and closed navigation commands remain inactive");
        }
        finally { settings.Close(); save.Stop(); Set(pet, "_settingsDirty", false); pet.DisposeResources(); pet.Close(); UiText.SetLanguage(language); }
    }
    private sealed class FixedClock : TimeProvider { public override long GetTimestamp() => 0; }
    private static void Pump()
    {
        var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false)); Dispatcher.PushFrame(frame);
    }
    private static object? Get(object obj, string name) => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(obj);
    private static void Set(object obj, string name, object value) => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(obj, value);
    private static object? Invoke(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(obj, args);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
