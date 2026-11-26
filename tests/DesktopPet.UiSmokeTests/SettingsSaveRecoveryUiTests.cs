using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DesktopPet.App;
using DesktopPet.Core;

internal static class SettingsSaveRecoveryUiTests
{
    public static void Run(string root)
    {
        VerifyStartupRepairsPrimary(root);
        var path = Path.Combine(root, "save-recovery", "settings.json");
        var settings = new PetSettings();
        settings.Save(path);
        var pet = new MainWindow(new CharacterLibrary(MainWindow.CharactersRoot, Path.Combine(root, "save-recovery-custom")), settingsPath: path);
        typeof(MainWindow).GetField("_brain", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(pet,
            new PetBrain(new PlatformLayout(new[] { new Platform(0, 1920, 1080) }), 128, 122, 500, 958));
        typeof(MainWindow).GetMethod("ApplyPresentation", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(pet, new object[] { CharacterPresentation.Load(Path.Combine(MainWindow.CharactersRoot, "Companion")) });
        const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(MainWindow).GetField("_settings", Fields)!.SetValue(pet, settings);
        var timer = (DispatcherTimer)typeof(MainWindow).GetField("_settingsSaveTimer", Fields)!.GetValue(pet)!;
        var save = typeof(MainWindow).GetMethod("SaveSettings", Fields)!;
        var ui = new SettingsWindow(pet);
        var retry = (Button)ui.FindName("RetrySettingsSaveButton");
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
                Check(retry.Visibility == Visibility.Visible, "Settings hid the retry action after exhausting automatic attempts");
            }
            Check(PetSettings.Load(path).FocusMinutes == 42 && PetSettings.Load(path + ".bak").FocusMinutes == 25,
                "Exhausted retries damaged either the primary or the backup");
            retry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(PetSettings.Load(path).FocusMinutes == 61 && retry.Visibility == Visibility.Collapsed && errors == 1,
                "Manual retry did not save pending changes or clear its action");
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                pet.SetFocusDurations(64, 9);
                pet.DisposeResources();
                Check(!timer.IsEnabled && errors == 2, "Disposal did not attempt the final save or restarted retries");
            }
            Console.WriteLine("PASS Settings retry saves after a temporary file lock, caps attempts and stops at disposal");
        }
        finally
        {
            ui.Close();
            pet.DisposeResources();
            pet.Close();
        }
    }

    private static void VerifyStartupRepairsPrimary(string root)
    {
        var folder = Path.Combine(root, "startup-recovery");
        var path = Path.Combine(folder, "settings.json");
        var settings = new PetSettings { Visible = false, FocusMinutes = 37, AutoConnectAssistants = false };
        settings.Save(path);
        settings.FocusMinutes = 49;
        settings.Save(path);
        File.WriteAllText(path, "{");
        var backup = File.ReadAllBytes(path + ".bak");
        var pet = new MainWindow(new CharacterLibrary(MainWindow.CharactersRoot, Path.Combine(folder, "characters")),
            inboxDirectory: Path.Combine(folder, "inbox"),
            workAreasProvider: () => new[] { new DesktopRect(0, 0, 1920, 1080) }, settingsPath: path);
        try
        {
            pet.Show();
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed < TimeSpan.FromSeconds(8))
            {
                var frame = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
                var restored = PetSettings.Load(out var fromBackup, path);
                if (!fromBackup && restored.FocusMinutes == 37) break;
                Thread.Sleep(10);
            }
            var result = PetSettings.Load(out var stillUsingBackup, path);
            Check(!stillUsingBackup && result.FocusMinutes == 37 && !result.AutoConnectAssistants &&
                File.ReadAllBytes(path + ".bak").SequenceEqual(backup), "Startup did not repair primary while keeping recovery data");
        }
        finally { pet.DisposeResources(); pet.Close(); }
        Console.WriteLine("PASS Successful startup schedules a repair of the primary settings without rotating the recovery copy");
    }

    private static void Check(bool valid, string message)
    {
        if (!valid) throw new Exception(message);
    }
}
