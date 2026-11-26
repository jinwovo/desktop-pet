using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Threading;
using DesktopPet.App;
using DesktopPet.Core;

internal static class SettingsSaveRecoveryUiTests
{
    public static void Run(string root)
    {
        var path = Path.Combine(root, "save-recovery", "settings.json");
        var settings = new PetSettings();
        settings.Save(path);
        var pet = new MainWindow(null, settingsPath: path);
        const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(MainWindow).GetField("_settings", Fields)!.SetValue(pet, settings);
        var timer = (DispatcherTimer)typeof(MainWindow).GetField("_settingsSaveTimer", Fields)!.GetValue(pet)!;
        var save = typeof(MainWindow).GetMethod("SaveSettings", Fields)!;
        var errors = 0;
        pet.OperationFailed += _ => errors++;
        try
        {
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                pet.SetFocusDurations(42, 7);
                save.Invoke(pet, null);
                Check(timer.IsEnabled && timer.Interval == TimeSpan.FromSeconds(2) && errors == 0,
                    "A temporary lock did not schedule a quiet first retry");
                Check(PetSettings.Load(path).FocusMinutes == 25, "A failed write changed the saved settings");
            }
            var clock = Stopwatch.StartNew();
            while (timer.IsEnabled && clock.Elapsed < TimeSpan.FromSeconds(7))
            {
                var frame = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
                Thread.Sleep(10);
            }
            Check(!timer.IsEnabled && PetSettings.Load(path).FocusMinutes == 42 && errors == 0,
                "The dispatcher retry did not save the pending preferences after the lock cleared");

            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                pet.SetFocusDurations(61, 9);
                foreach (var delay in new[] { 2, 4, 8 })
                {
                    save.Invoke(pet, null);
                    Check(timer.IsEnabled && timer.Interval == TimeSpan.FromSeconds(delay) && errors == 0,
                        "Retry delay or pre-exhaustion feedback was incorrect");
                }
                save.Invoke(pet, null);
                Check(!timer.IsEnabled && errors == 1, "Exhausted retries did not stop and report one error");
                pet.DisposeResources();
                Check(!timer.IsEnabled && errors == 1, "Disposal restarted retries or repeated the error");
            }
            Check(PetSettings.Load(path).FocusMinutes == 42 && PetSettings.Load(path + ".bak").FocusMinutes == 25,
                "Exhausted retries damaged either the primary or the backup");
            Console.WriteLine("PASS Settings retry saves after a temporary file lock, caps attempts and stops at disposal");
        }
        finally
        {
            pet.DisposeResources();
            pet.Close();
        }
    }

    private static void Check(bool valid, string message)
    {
        if (!valid) throw new Exception(message);
    }
}
