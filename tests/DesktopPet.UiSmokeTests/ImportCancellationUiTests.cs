using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DesktopPet.App;
using DesktopPet.Core;

internal static class ImportCancellationUiTests
{
    public static void Run(string root)
    {
        var pet = new MainWindow(new CharacterLibrary(MainWindow.CharactersRoot, Path.Combine(root, "cancel-ui-custom")), inboxDirectory: Path.Combine(root, "cancel-ui-inbox"));
        Set(pet, "_settings", new PetSettings());
        Set(pet, "_brain", new PetBrain(new PlatformLayout(new[] { new Platform(0, 1920, 1080) }), 128, 122, 500, 958));
        Invoke(pet, "ApplyPresentation", CharacterPresentation.Load(Path.Combine(MainWindow.CharactersRoot, "Companion")));
        var save = (DispatcherTimer)Get(pet, "_settingsSaveTimer")!; save.Interval = TimeSpan.FromDays(1);
        var settings = new SettingsWindow(pet);
        try
        {
            var pending = new TaskCompletionSource<string?>();
            var operation = (Task)Invoke(settings, "RunCharacterImportAsync", new Func<Task<string?>>(() => pending.Task))!;
            var progress = (IProgress<CharacterImportProgress>)Get(settings, "_characterImportProgress")!;
            progress.Report(new(CharacterImportPhase.Copying, 2, 4, 100, 200));
            Pump();
            Check(((ProgressBar)settings.FindName("CharacterImportProgressBar")).Value == 50 && !((ProgressBar)settings.FindName("CharacterImportProgressBar")).IsIndeterminate,
                "Actual progress controls did not show copy progress");
            var token = ((CancellationTokenSource)Get(settings, "_characterImportCancellation")!).Token;
            ((Button)settings.FindName("CancelCharacterImportButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(token.IsCancellationRequested, "Cancel button did not signal the worker token");
            pending.SetCanceled(token); Wait(operation);
            Check(pet.CurrentCharacterName == "Companion" && ((TextBlock)settings.FindName("CharacterImportStatusText")).Text.Contains("취소") &&
                ((ProgressBar)settings.FindName("CharacterImportProgressBar")).Visibility == Visibility.Collapsed && ((Button)settings.FindName("AddArchiveButton")).IsEnabled,
                "Cancelled operation changed the active pet or retained busy controls");
            progress.Report(new(CharacterImportPhase.Validating, 4, 4, 200, 200)); Pump();
            Check(((TextBlock)settings.FindName("CharacterImportStatusText")).Text.Contains("취소"), "Stale progress overwrote cancellation status");

            pending = new TaskCompletionSource<string?>();
            operation = (Task)Invoke(settings, "RunCharacterImportAsync", new Func<Task<string?>>(() => pending.Task))!;
            ((Button)settings.FindName("CancelCharacterImportButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            pending.SetResult("Nimbus"); Wait(operation); // Atomic publication won the race; application must still respect Cancel.
            Check(pet.CurrentCharacterName == "Companion" && !(bool)Get(pet, "_settingsDirty")! &&
                ((TextBlock)settings.FindName("CharacterImportStatusText")).Text.Contains("이미 완료"), "Late cancellation switched the pet or hid the completed-install outcome");
            Console.WriteLine("PASS Import cancel controls signal worker tokens, show bounded progress, reject stale callbacks and retain the current pet if installation already committed");
        }
        finally { settings.Close(); save.Stop(); Set(pet, "_settingsDirty", false); pet.DisposeResources(); }
    }
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
    private static void Wait(Task task)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (!task.IsCompleted) { if (DateTime.UtcNow > until) throw new Exception("Cancellation UI timed out"); Pump(); }
        task.GetAwaiter().GetResult();
    }
    private static object? Get(object obj, string name) => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(obj);
    private static void Set(object obj, string name, object value) => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(obj, value);
    private static object? Invoke(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(obj, args);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
