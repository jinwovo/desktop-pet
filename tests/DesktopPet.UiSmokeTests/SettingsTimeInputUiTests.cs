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

internal static class SettingsTimeInputUiTests
{
    public static void Run(string root, bool capture = false)
    {
        var language = UiText.Language;
        UiText.SetLanguage("en");
        var folder = Path.Combine(root, "settings-time-input-inbox");
        using var timer = new FocusTimer(new FixedClock());
        var pet = new MainWindow(new CharacterLibrary(MainWindow.CharactersRoot, Path.Combine(root, "time-input-custom")), focusTimer: timer, inboxDirectory: folder);
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
        try
        {
            settings.Show(); Pump();
            var inputSource = PresentationSource.FromVisual(settings)!;
            var focusBox = (TextBox)settings.FindName("FocusMinutesBox"); var breakBox = (TextBox)settings.FindName("BreakMinutesBox");
            var start = (TextBox)settings.FindName("QuietStartBox"); var end = (TextBox)settings.FindName("QuietEndBox");
            focusBox.Text = "40"; breakBox.Text = "8";
            var enter = Press(focusBox, inputSource, Key.Enter);
            Check(enter.Handled && settings.IsVisible && pet.FocusMinutes == 40 && pet.BreakMinutes == 8, "Enter in duration input did not apply its fields while keeping Settings open");
            pet.StartFocusTimer(); var active = timer.Snapshot;
            foreach (var locale in new[] { "en", "zh-CN", "ko" })
            {
                UiText.SetLanguage(locale); Pump();
                focusBox.Text = "50"; breakBox.Text = "9";
                Check(Press(breakBox, inputSource, Key.Enter).Handled && pet.FocusMinutes == 50 && pet.BreakMinutes == 9 && timer.Snapshot == active,
                    "Break Enter reset the active session or failed to update future defaults");
                focusBox.Text = "invalid"; save.Stop(); Set(pet, "_settingsDirty", false);
                Check(Press(focusBox, inputSource, Key.Enter).Handled && settings.IsVisible && pet.FocusMinutes == 50 && !(bool)Get(pet, "_settingsDirty")! &&
                    ((TextBlock)settings.FindName("FocusDurationStatus")).Text == UiText.Get("집중 1~180분, 휴식 1~60분을 입력해주세요."), "Invalid duration Enter closed the editor or bypassed existing validation");
                focusBox.Text = "50"; breakBox.Text = "9";
                ((CheckBox)settings.FindName("QuietHoursCheck")).IsChecked = true; start.Text = "23:15"; end.Text = "06:45";
                Check(Press(start, inputSource, Key.Enter).Handled && pet.QuietHoursEnabled && pet.QuietHoursStartMinute == 1395 && pet.QuietHoursEndMinute == 405,
                    "Quiet-start Enter did not apply the complete schedule draft");
                end.Text = "07:10";
                Check(Press(end, inputSource, Key.Enter).Handled && pet.QuietHoursEndMinute == 430, "Quiet-end Enter did not apply its current draft");
                start.Text = "25:00"; save.Stop(); Set(pet, "_settingsDirty", false);
                Check(Press(start, inputSource, Key.Enter).Handled && settings.IsVisible && pet.QuietHoursStartMinute == 1395 && !(bool)Get(pet, "_settingsDirty")! &&
                    ((TextBlock)settings.FindName("QuietHoursStatus")).Text == UiText.Get("00:00~23:59 사이 HH:mm 형식으로 입력해주세요."), "Invalid quiet Enter changed persisted schedule or closed Settings");
                foreach (var input in new[] { focusBox, breakBox, start, end })
                    Check(AutomationProperties.GetAcceleratorKey(input) == "Enter", "Scoped Enter shortcut missing from accessibility metadata");
                Check((string)focusBox.ToolTip == UiText.Get("Enter로 집중·휴식 시간 적용") && (string)end.ToolTip == UiText.Get("Enter로 방해 금지 예약 적용"), "Enter shortcut help did not localize");
                var before = (pet.FocusMinutes, pet.BreakMinutes, pet.QuietHoursStartMinute, pet.QuietHoursEndMinute);
                Press(focusBox, inputSource, Key.ImeProcessed); Press(start, inputSource, Key.F2);
                Check(before == (pet.FocusMinutes, pet.BreakMinutes, pet.QuietHoursStartMinute, pet.QuietHoursEndMinute) && timer.Snapshot == active,
                    "Ordinary/IME key events applied settings or changed the active timer");
            }
            var final = (pet.FocusMinutes, pet.BreakMinutes, pet.QuietHoursStartMinute, pet.QuietHoursEndMinute);
            settings.Close(); focusBox.Text = "70"; start.Text = "12:00";
            Press(focusBox, inputSource, Key.Enter); Press(start, inputSource, Key.Enter);
            Check(final == (pet.FocusMinutes, pet.BreakMinutes, pet.QuietHoursStartMinute, pet.QuietHoursEndMinute) && timer.Snapshot == active,
                "Closed editor key events applied stale drafts");
            Console.WriteLine("PASS Enter applies duration/schedule drafts in their four editors, keeps validation failures open and preserves running focus sessions across three locales");
            Console.WriteLine("PASS Enter accessibility hints localize while ordinary/IME and closed-editor input cannot apply stale values");
        }
        finally { settings.Close(); save.Stop(); Set(pet, "_settingsDirty", false); pet.DisposeResources(); pet.Close(); UiText.SetLanguage(language); }
    }
    private static KeyEventArgs Press(TextBox box, PresentationSource inputSource, Key key)
    {
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, inputSource, 0, key) { RoutedEvent = Keyboard.KeyDownEvent };
        box.RaiseEvent(args); return args;
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
