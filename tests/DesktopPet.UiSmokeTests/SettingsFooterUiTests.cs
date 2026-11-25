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

internal static class SettingsFooterUiTests
{
    public static void Run(string root, bool capture = false)
    {
        var language = UiText.Language;
        UiText.SetLanguage("en");
        var folder = Path.Combine(root, "settings-footer-inbox");
        using var timer = new FocusTimer(new FixedClock());
        var pet = new MainWindow(new CharacterLibrary(MainWindow.CharactersRoot, Path.Combine(root, "footer-custom")), focusTimer: timer, inboxDirectory: folder);
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
            settings.Show(); Pump();
            var scroll = settings.FindName("SettingsScrollViewer") as ScrollViewer ?? settings.Content as ScrollViewer ?? throw new Exception("Settings body scroll viewer missing");
            var close = (Button)settings.FindName("CloseButton"); var footer = (Panel)System.Windows.Media.VisualTreeHelper.GetParent(close);
            var hint = (TextBlock)settings.FindName("SettingsNavigationHint");
            var body = (FrameworkElement)settings.Content;
            var focus = (TextBox)settings.FindName("FocusMinutesBox"); var rest = (TextBox)settings.FindName("BreakMinutesBox");
            focus.Text = "pending"; rest.Text = "9"; var draft = (focus.Text, rest.Text);
            var presentation = Get(pet, "_currentPresentation"); var snapshot = timer.Snapshot;
            foreach (var locale in new[] { "en", "zh-CN", "ko" })
            {
                UiText.SetLanguage(locale); Pump(); settings.UpdateLayout();
                Check(scroll.ScrollableHeight > 0, "Small-height fixture did not require scrolling");
                double? pinnedY = null;
                foreach (var offset in new[] { 0, scroll.ScrollableHeight / 2, scroll.ScrollableHeight })
                {
                    scroll.ScrollToVerticalOffset(offset); settings.UpdateLayout(); Pump();
                    var point = footer.TranslatePoint(new Point(0, 0), body);
                    Check(pinnedY is null || Math.Abs(pinnedY.Value - point.Y) < 1, "Settings actions moved with the body scroll position"); pinnedY = point.Y;
                    foreach (var element in footer.Children.OfType<FrameworkElement>())
                    {
                        var bounds = new Rect(element.TranslatePoint(new Point(0, 0), body), element.RenderSize);
                        Check(bounds.Left >= -1 && bounds.Top >= -1 && bounds.Right <= body.ActualWidth + 1 && bounds.Bottom <= body.ActualHeight + 1,
                            $"Settings action/hint left the visible client area: {element.Name}, y={bounds.Top}, bottom={bounds.Bottom}, client={body.ActualHeight}");
                    }
                    Check(close.IsEnabled && hint.Text == UiText.Get("Ctrl+1~4로 설정 구역 이동") &&
                        (focus.Text, rest.Text) == draft && timer.Snapshot == snapshot && ReferenceEquals(presentation, Get(pet, "_currentPresentation")) && !(bool)Get(pet, "_settingsDirty")!,
                        "Fixed actions lost localization or scrolling applied drafts/changed presentation/preferences");
                }
                scroll.ScrollToTop(); settings.UpdateLayout(); Pump();
                if (capture) Program.SaveWindowSnapshot(settings, $"settings-fixed-footer-{locale}.png");
            }
            close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(!settings.IsVisible && pet.FocusMinutes == 25 && pet.BreakMinutes == 5 && !(bool)Get(pet, "_settingsDirty")!, "Pinned Close action applied an unapplied draft or did not close");
            Console.WriteLine("PASS Settings actions and keyboard hint remain visible and fixed across top/middle/bottom scrolling in three locales at 500px height");
            Console.WriteLine("PASS Fixed Close preserves unapplied drafts/defaults while body scrolling preserves focus, presentation and preferences");
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
