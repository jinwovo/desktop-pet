using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DesktopPet.App;
using DesktopPet.Core;

internal static class PreferencesBackupUiTests
{
    public static void Run(string root)
    {
        var originalLanguage = UiText.Language;
        UiText.SetLanguage("ko");
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        CancellationToken restoreToken = default;
        var dispatcherThread = Environment.CurrentManagedThreadId;
        var decodeThread = 0;
        var pet = new MainWindow(new CharacterLibrary(MainWindow.CharactersRoot, Path.Combine(root, "backup-custom")),
            inboxDirectory: Path.Combine(root, "backup-inbox"), characterLoader: (folder, token) =>
            {
                if (Path.GetFileName(folder) == "Ember")
                {
                    decodeThread = Environment.CurrentManagedThreadId;
                    restoreToken = token;
                    entered.Set();
                    Check(release.Wait(TimeSpan.FromSeconds(15)), "Backup decoder did not receive its fixture release");
                }
                return CharacterPresentation.Load(folder);
            });
        var loaded = (RoutedEventHandler)Delegate.CreateDelegate(typeof(RoutedEventHandler), pet, typeof(MainWindow).GetMethod("MainWindow_Loaded", BindingFlags.Instance | BindingFlags.NonPublic)!);
        pet.Loaded -= loaded;
        Set(pet, "_settings", new PetSettings { Visible = false, AutoConnectAssistants = false, AssistantSetupSeen = true });
        var brain = new PetBrain(new PlatformLayout(new[] { new Platform(0, 1920, 1080) }), 128, 122, 500, 958);
        Set(pet, "_brain", brain);
        Invoke(pet, "ApplyPresentation", CharacterPresentation.Load(Path.Combine(MainWindow.CharactersRoot, "Companion")));
        var save = (DispatcherTimer)Get(pet, "_settingsSaveTimer")!;
        save.Interval = TimeSpan.FromDays(1);
        var settings = new SettingsWindow(pet);
        var path = Path.Combine(root, "portable-preferences.json");
        try
        {
            Invoke(settings, "BackupPreferencesToFile", path);
            var json = PetSettingsBackup.ReadFile(path);
            Check(!json.Contains("AutoConnectAssistants") && ((TextBlock)settings.FindName("PreferencesTransferStatus")).Text.Contains("백업"), "Backup UI failed to write portable settings");
            pet.StartFocusTimer();
            var remaining = pet.FocusSnapshot.Remaining;
            var source = new PetSettings { CharacterName = "Nimbus", Visible = false, ScaleMultiplier = 1.5, MaximumFps = 30,
                ReducedMotion = true, FocusMinutes = 50, BreakMinutes = 10, AutoConnectAssistants = true };
            PetSettingsBackup.SaveFile(path, source);
            PreferencesBackupUiTests.WaitFor((Task)Invoke(settings, "RestorePreferencesFromFile", path)!);
            Check(pet.CurrentCharacterName == "Nimbus" && pet.MaximumFps == 30 && pet.ReducedMotion && pet.FocusMinutes == 50 &&
                pet.FocusSnapshot.Remaining == remaining && !pet.AutoConnectAssistants && pet.AssistantSetupSeen &&
                ((ComboBox)settings.FindName("CharacterCombo")).SelectedItem is "Nimbus", "Restore failed live wiring, reset active timer or changed assistant registration preference");
            Console.WriteLine("PASS Backup/restore UI transfers a validated character and display/timer options while preserving active countdown and local assistant choices");

            source.CharacterName = "Not installed";
            PetSettingsBackup.SaveFile(path, source);
            PreferencesBackupUiTests.WaitFor((Task)Invoke(settings, "RestorePreferencesFromFile", path)!);
            Check(pet.CurrentCharacterName == "Nimbus" && ((TextBlock)settings.FindName("PreferencesTransferStatus")).Text.Contains("현재 친구"), "Unavailable backup character did not retain current pet");
            var before = JsonSerializer.Serialize(Get(pet, "_settings"));
            var physical = (brain.X, brain.Y, brain.State);
            File.WriteAllText(path, "{\"schemaVersion\":1,\"product\":\"DesktopPet\",\"preferences\":{\"AutoConnectAssistants\":true}}");
            PreferencesBackupUiTests.WaitFor((Task)Invoke(settings, "RestorePreferencesFromFile", path)!);
            Check(before == JsonSerializer.Serialize(Get(pet, "_settings")) && physical == (brain.X, brain.Y, brain.State) &&
                ((TextBlock)settings.FindName("PreferencesTransferStatus")).Text.Contains("못했"), "Invalid backup partly changed the pet");
            foreach (var locale in new[] { "en", "zh-CN", "ko" })
            {
                UiText.SetLanguage(locale);
                Check(((TextBlock)settings.FindName("PreferencesTransferStatus")).Text == UiText.Get("복원하지 못했어요. 백업 형식과 캐릭터 파일을 확인해주세요."),
                    "Language change replaced the latest preference restore feedback with the initial hint");
            }
            Console.WriteLine("PASS Missing backup characters retain current pet and rejected connection fields cannot partly change preferences or physical pose");
            source.CharacterName = "Ember";
            PetSettingsBackup.SaveFile(path, source);
            var restoring = (Task)Invoke(settings, "RestorePreferencesFromFile", path)!;
            Check(entered.Wait(TimeSpan.FromSeconds(10)) && decodeThread != dispatcherThread &&
                !restoring.IsCompleted && !((Button)settings.FindName("RestorePreferencesButton")).IsEnabled,
                "Restore did not prepare replacement artwork on a worker or allowed duplicate input");
            settings.Close();
            Check(restoreToken.IsCancellationRequested, "Closing Settings did not cancel restore ownership");
            release.Set();
            WaitFor(restoring);
            Check(pet.CurrentCharacterName == "Nimbus" && before == JsonSerializer.Serialize(Get(pet, "_settings")),
                "A late restore applied to a closed Settings window");
            Console.WriteLine("PASS File restore prepares artwork off the UI thread and closing discards late results");
        }
        finally
        {
            release.Set();
            save.Stop(); Set(pet, "_settingsDirty", false);
            settings.Close(); pet.DisposeResources();
            UiText.SetLanguage(originalLanguage);
        }
    }
    internal static void WaitFor(Task operation)
    {
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (!operation.IsCompleted && timeout.Elapsed < TimeSpan.FromSeconds(20))
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(5);
        }
        Check(operation.IsCompleted, "Timed out waiting for the preference operation");
        operation.GetAwaiter().GetResult();
    }
    private static object? Get(object obj, string name) => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(obj);
    private static void Set(object obj, string name, object value) => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(obj, value);
    private static object? Invoke(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(obj, args);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
