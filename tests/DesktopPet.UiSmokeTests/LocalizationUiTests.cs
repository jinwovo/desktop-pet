using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DesktopPet.App;
using DesktopPet.Core;

internal static class LocalizationUiTests
{
    public static void Run(string root)
    {
        UiText.SetLanguage("ko");
        var pet = new MainWindow(new CharacterLibrary(MainWindow.CharactersRoot, Path.Combine(root, "language-custom")), inboxDirectory: Path.Combine(root, "language-inbox"));
        var preferences = new PetSettings { AutoConnectAssistants = false, Language = "ko" };
        Set(pet, "_settings", preferences);
        var brain = new PetBrain(new PlatformLayout(new[] { new Platform(0, 1920, 1080) }), 128, 122, 500, 958);
        Set(pet, "_brain", brain);
        Invoke(pet, "ApplyPresentation", CharacterPresentation.Load(Path.Combine(MainWindow.CharactersRoot, "Companion")));
        var save = (DispatcherTimer)Get(pet, "_settingsSaveTimer")!;
        save.Interval = TimeSpan.FromDays(1);
        var settings = new SettingsWindow(pet);
        settings.Show();
        try
        {
            Check(!(bool)Get(pet, "_settingsDirty")!, "Loading language controls changed preferences");
            var visibility = pet.IsPetVisible;
            var focusBeforePause = pet.FocusSnapshot;
            pet.SetRoamingPaused(true);
            pet.SetRoamingPaused(true);
            Check(pet.RoamingPaused && brain.RoamingPaused && pet.IsPetVisible == visibility && pet.FocusSnapshot == focusBeforePause &&
                !(bool)Get(pet, "_settingsDirty")!, "Pausing roaming changed visibility, focus or persisted preferences");
            var choice = (ComboBox)settings.FindName("LanguageCombo");
            var pose = (brain.X, brain.Y, brain.State, brain.HorizontalVelocityPxPerSec);
            choice.SelectedValue = "en";
            Check(settings.Title == "DesktopPet Settings" && ((Button)settings.FindName("CloseButton")).Content is "Close" &&
                ((Button)settings.FindName("FocusStartButton")).Content is "Start focus" && pet.FocusTimerCaption.Contains("Ready to focus") &&
                UiText.Format("보관된 알림 확인 ({0})", 3) == "Review saved notifications (3)" &&
                UiText.Get("자동 산책 잠시 멈춤") == "Pause roaming", "English static/dynamic controls did not update together");
            choice.SelectedValue = "zh-CN";
            Check(settings.Title == "DesktopPet 设置" && ((Button)settings.FindName("CloseButton")).Content is "关闭" &&
                ((Button)settings.FindName("BackupPreferencesButton")).Content is "备份设置" && pet.UiLanguagePreference == "zh-CN" &&
                UiText.Get("자동 산책 잠시 멈춤") == "暂停自动散步",
                "Chinese settings did not replace the previous language");
            choice.SelectedValue = "ko";
            Check(settings.Title == "DesktopPet 설정" && ((Button)settings.FindName("CloseButton")).Content is "닫기" &&
                ((Button)settings.FindName("FocusStartButton")).Content is "집중 시작", "Switching back did not restore the original labels");
            Check(pose == (brain.X, brain.Y, brain.State, brain.HorizontalVelocityPxPerSec) && pet.CurrentCharacterName == "Companion" && !pet.AutoConnectAssistants,
                "Language change moved the pet, changed its character or enabled assistant registration");
            Check(pet.RoamingPaused && brain.RoamingPaused, "Locale change resumed roaming");
            pet.SetRoamingPaused(false);
            Check(!pet.RoamingPaused && !brain.RoamingPaused && pose == (brain.X, brain.Y, brain.State, brain.HorizontalVelocityPxPerSec),
                "Resuming roaming forced an immediate pose or position change");
            Console.WriteLine("PASS KO/EN/zh-CN language selection updates actual Settings and dynamic timer/tray text without changing motion or assistant preferences");
            settings.Close();
            UiText.SetLanguage("en");
            Check(settings.Title == "DesktopPet 설정", "Closed settings retained a global language subscription");
            Check(UiText.Get("User-created character name") == "User-created character name", "Unknown user text was translated");
            Console.WriteLine("PASS Closed language windows unsubscribe and user-entered character text stays intact");
        }
        finally
        {
            settings.Close(); UiText.SetLanguage("ko");
            save.Stop(); Set(pet, "_settingsDirty", false); pet.DisposeResources();
        }
    }
    private static object? Get(object obj, string name) => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(obj);
    private static void Set(object obj, string name, object value) => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(obj, value);
    private static object? Invoke(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(obj, args);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
