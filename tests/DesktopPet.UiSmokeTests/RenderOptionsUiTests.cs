using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopPet.App;
using DesktopPet.Core;

internal static class RenderOptionsUiTests
{
    public static void Run(string root)
    {
        var pet = new MainWindow(new CharacterLibrary(MainWindow.CharactersRoot, Path.Combine(root, "render-options-custom")), inboxDirectory: Path.Combine(root, "render-options-inbox"));
        var preferences = new PetSettings { HopBetweenWindowsEnabled = true, WindowFallEnabled = true };
        Set(pet, "_settings", preferences);
        var brain = new PetBrain(new PlatformLayout(new[] { new Platform(0, 1920, 1080) }), 128, 122, 500, 958) { HopBetweenPlatforms = true };
        Set(pet, "_brain", brain);
        Invoke(pet, "ApplyPresentation", CharacterPresentation.Load(Path.Combine(MainWindow.CharactersRoot, "Companion")));
        var save = (DispatcherTimer)Get(pet, "_settingsSaveTimer")!;
        save.Interval = TimeSpan.FromDays(1);
        var settings = new SettingsWindow(pet);
        settings.Show();
        var captureRequests = new List<bool>();
        pet.WindowFallEnabledChanged += captureRequests.Add;
        try
        {
            Check(!(bool)Get(pet, "_settingsDirty")!, "Rendering controls changed preferences during load");
            ((ComboBox)settings.FindName("FrameRateCombo")).SelectedValue = "30";
            ((CheckBox)settings.FindName("ReducedMotionCheck")).IsChecked = true;
            Check(pet.MaximumFps == 30 && pet.ReducedMotion && !brain.HopBetweenPlatforms && !pet.CanUseWindowFall &&
                preferences.HopBetweenWindowsEnabled && preferences.WindowFallEnabled && captureRequests.Last() == false,
                "Reduced motion lost user preferences or left optional hopping/capture enabled");
            Invoke(settings, "MotionPreviewButton_Click", settings, new RoutedEventArgs());
            var motion = (MotionPreviewWindow)Get(settings, "_motionPreview")!;
            Check(((FramePacer)Get(motion, "_framePacer")!).MaximumFps == 30 && (bool)Get(motion, "_reducedMotion")!, "New motion preview ignored render preferences");
            var pose = (brain.X, brain.Y, brain.State, brain.HorizontalVelocityPxPerSec);
            pet.SetRenderOptions(120, false);
            Check(pose == (brain.X, brain.Y, brain.State, brain.HorizontalVelocityPxPerSec) && brain.HopBetweenPlatforms && pet.CanUseWindowFall &&
                ((FramePacer)Get(motion, "_framePacer")!).MaximumFps == 120 && !(bool)Get(motion, "_reducedMotion")!,
                "Changing render options reset movement or failed to update an open preview");
            Console.WriteLine("PASS Render controls preserve physical pose and saved effects while reduced motion suppresses hopping/capture and updates open previews");

            pet.SetRenderOptions(60, false);
            Set(pet, "_lastTickTimestamp", System.Diagnostics.Stopwatch.GetTimestamp());
            Invoke(pet, "CompositionTarget_Rendering", pet, RenderingAt(TimeSpan.Zero));
            var accepted = Get(pet, "_lastTickTimestamp");
            Invoke(pet, "CompositionTarget_Rendering", pet, RenderingAt(TimeSpan.FromMilliseconds(1)));
            Check(Equals(accepted, Get(pet, "_lastTickTimestamp")), "Desktop render handler ignored the frame cap");
            Check(((FramePacer)Get(settings, "_framePacer")!).MaximumFps == 60, "Settings preview ignored changed frame cap");
            Console.WriteLine("PASS Actual desktop render callback skips early frames and shares the selected cap with Settings preview");
        }
        finally
        {
            save.Stop(); Set(pet, "_settingsDirty", false);
            settings.Close(); pet.DisposeResources();
        }
    }
    private static object? Get(object obj, string name) => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(obj);
    private static void Set(object obj, string name, object value) => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(obj, value);
    private static object? Invoke(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(obj, args);
    private static RenderingEventArgs RenderingAt(TimeSpan time) => (RenderingEventArgs)Activator.CreateInstance(typeof(RenderingEventArgs),
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, new object[] { time }, null)!;
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
