using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using DesktopPet.App;
using DesktopPet.Core;
using WinForms = System.Windows.Forms;

internal static class PetContextMenuUiTests
{
    public static void Run(string root, bool capture)
    {
        var language = UiText.Language;
        UiText.SetLanguage("ko");
        var pet = new MainWindow(new CharacterLibrary(MainWindow.CharactersRoot, Path.Combine(root, "context-menu-custom")), inboxDirectory: Path.Combine(root, "context-menu-inbox"));
        var loaded = (RoutedEventHandler)Delegate.CreateDelegate(typeof(RoutedEventHandler), pet, typeof(MainWindow).GetMethod("MainWindow_Loaded", BindingFlags.NonPublic | BindingFlags.Instance)!);
        pet.Loaded -= loaded;
        var preferences = new PetSettings { Visible = true, AutoConnectAssistants = false, PauseDuringFullscreen = false };
        Set(pet, "_settings", preferences);
        var brain = new PetBrain(new PlatformLayout(new[] { new Platform(0, 1920, 1080) }), 128, 122, 500, 958);
        Set(pet, "_brain", brain);
        Invoke(pet, "ApplyPresentation", CharacterPresentation.Load(Path.Combine(MainWindow.CharactersRoot, "Companion")));
        var save = (DispatcherTimer)Get(pet, "_settingsSaveTimer")!; save.Interval = TimeSpan.FromDays(1);
        var settings = 0; var connections = 0; var exits = 0; var requests = 0;
        var factory = typeof(App).Assembly.GetType("DesktopPet.App.PetMenuBuilder")!.GetMethod("Create", BindingFlags.Static | BindingFlags.NonPublic)!;
        WinForms.ContextMenuStrip Build() => (WinForms.ContextMenuStrip)factory.Invoke(null, new object[] { pet, (Action)(() => settings++), (Action)(() => connections++), (Action)(() => exits++) })!;
        using var menu = Build();
        pet.ContextMenuRequested += () => { requests++; menu.Show(new System.Drawing.Point(24, 24)); };
        var actor = (FrameworkElement)pet.FindName("ActorVisualRoot");
        void RightUp() => actor.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right) { RoutedEvent = Mouse.MouseUpEvent });
        WinForms.ToolStripMenuItem Item(WinForms.ContextMenuStrip target, string text) => target.Items.OfType<WinForms.ToolStripMenuItem>().Single(i => i.Text == text);
        try
        {
            var pose = (brain.X, brain.Y, brain.State, brain.StateElapsedMs, brain.HorizontalVelocityPxPerSec);
            RightUp();
            Check(requests == 1 && menu.Visible && pose == (brain.X, brain.Y, brain.State, brain.StateElapsedMs, brain.HorizontalVelocityPxPerSec) && !(bool)Get(pet, "_settingsDirty")!,
                "Routed pet right-click failed to open the shared menu or changed motion/preferences");
            if (capture)
            {
                using var bitmap = new System.Drawing.Bitmap(menu.Width, menu.Height);
                menu.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, menu.Width, menu.Height));
                bitmap.Save(Path.Combine(AppContext.BaseDirectory, "pet-context-menu.png"), System.Drawing.Imaging.ImageFormat.Png);
            }
            Item(menu, UiText.Get("자동 산책 잠시 멈춤")).PerformClick();
            Check(pet.RoamingPaused && pose == (brain.X, brain.Y, brain.State, brain.StateElapsedMs, brain.HorizontalVelocityPxPerSec), "Shared pause command reset motion");
            menu.Close(); RightUp();
            Check(requests == 2 && Item(menu, UiText.Get("자동 산책 잠시 멈춤")).Checked, "Reopened pet menu lost current pause state");
            Item(menu, UiText.Get("설정...")).PerformClick(); Item(menu, UiText.Get("AI 연결...")).PerformClick(); Item(menu, UiText.Get("앱 종료")).PerformClick();
            Check(settings == 1 && connections == 1 && exits == 1, "Shared menu callbacks were duplicated or missing");
            menu.Close();
            foreach (var flag in new[] { "_isPressed", "_isDragging" }) { Set(pet, flag, true); RightUp(); Set(pet, flag, false); }
            preferences.Visible = false; RightUp(); preferences.Visible = true;
            Set(pet, "_suppressionReasons", PetSuppressionReason.SessionLocked); RightUp(); Set(pet, "_suppressionReasons", PetSuppressionReason.None);
            Check(requests == 2 && !menu.Visible, "Context menu opened during a drag, while hidden or while presentation was suppressed");
            foreach (var (locale, settingsLabel, pauseLabel) in new[] { ("en", "Settings...", "Pause roaming"), ("zh-CN", "设置...", "暂停自动散步") })
            {
                UiText.SetLanguage(locale);
                using var localized = Build();
                Check(Item(localized, settingsLabel) is not null && Item(localized, pauseLabel) is not null, "Shared context menu was not localized");
            }
            save.Stop(); Set(pet, "_settingsDirty", false); pet.DisposeResources(); RightUp();
            Check(requests == 2, "Disposed pet still requested menus");
            Console.WriteLine("PASS Pet right-click opens the shared localized action menu, reflects roaming state and preserves drag, pose and callback ownership");
        }
        finally { menu.Close(); save.Stop(); Set(pet, "_settingsDirty", false); pet.DisposeResources(); pet.Close(); UiText.SetLanguage(language); }
    }
    private static object? Get(object obj, string name) => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(obj);
    private static void Set(object obj, string name, object value) => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(obj, value);
    private static object? Invoke(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(obj, args);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
