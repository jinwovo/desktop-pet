using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopPet.App;
using DesktopPet.Core;

internal static class SpeechBubbleMotionTests
{
    public static void Run(string root)
    {
        var pet = new MainWindow(new CharacterLibrary(MainWindow.CharactersRoot, Path.Combine(root, "motion-custom")), inboxDirectory: Path.Combine(root, "motion-inbox"));
        var preferences = new PetSettings { AutoConnectAssistants = false };
        Set(pet, "_settings", preferences);
        var brain = new PetBrain(new PlatformLayout(new[] { new Platform(0, 1920, 1080) }), 128, 122, 500, 958);
        Set(pet, "_brain", brain);
        Invoke(pet, "ApplyPresentation", CharacterPresentation.Load(Path.Combine(MainWindow.CharactersRoot, "Companion")));
        var save = (DispatcherTimer)Get(pet, "_settingsSaveTimer")!;
        save.Interval = TimeSpan.FromDays(1);
        var bubble = new SpeechBubbleWindow();
        Set(pet, "_bubble", bubble);
        try
        {
            pet.SetRenderOptions(60, true);
            bubble.ShowMessage("raw title", "raw message", 1);
            Check(bubble.IsShowing && bubble.IsVisible && bubble.Opacity == 1 && !Animated(bubble), "Reduced motion still animates notification appearance");
            Check(((TextBlock)bubble.FindName("TitleText")).Text == "raw title" && ((TextBlock)bubble.FindName("MessageText")).Text == "raw message", "Motion policy changed user text");
            bubble.DismissSoftly();
            Check(!bubble.IsShowing && !bubble.IsVisible && !Animated(bubble), "Reduced notification dismissal must hide immediately");

            pet.SetRenderOptions(60, false);
            bubble.ShowMessage("second", "message", 1);
            Check(bubble.IsShowing && bubble.IsVisible && Animated(bubble), "Normal appearance fade was not restored");
            pet.SetRenderOptions(60, true);
            Check(bubble.IsShowing && bubble.IsVisible && bubble.Opacity == 1 && !Animated(bubble), "Enabling reduced motion failed to finish appearance immediately");

            pet.SetRenderOptions(60, false);
            bubble.ShowMessage("third", "message", 1);
            bubble.DismissSoftly();
            Check(!bubble.IsShowing && bubble.IsVisible && Animated(bubble), "Normal dismissal must retain its fade");
            pet.SetRenderOptions(60, true);
            Check(!bubble.IsShowing && !bubble.IsVisible && !Animated(bubble), "Enabling reduced motion left a dismissed popup fading on screen");
            pet.SetRenderOptions(60, false);
            bubble.ShowMessage("restored", "same message", 1);
            Check(Animated(bubble), "Restore fixture must have an active fade");
            pet.RestorePreferencesJson(PetSettingsBackup.WriteJson(new PetSettings { ReducedMotion = true, AutoConnectAssistants = false }));
            Check(bubble.IsShowing && bubble.IsVisible && !Animated(bubble) && bubble.Opacity == 1,
                "Restoring reduced motion did not apply to the already open notification");
            Console.WriteLine("PASS Reduced motion disables notification appearance/dismissal fades and updates in-flight fades without changing user text");
        }
        finally
        {
            save.Stop(); Set(pet, "_settingsDirty", false);
            pet.DisposeResources(); bubble.Shutdown();
        }
    }
    private static bool Animated(SpeechBubbleWindow bubble) => bubble.HasAnimatedProperties;

    private static object? Get(object obj, string name) => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(obj);
    private static void Set(object obj, string name, object value) => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(obj, value);
    private static object? Invoke(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(obj, args);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
