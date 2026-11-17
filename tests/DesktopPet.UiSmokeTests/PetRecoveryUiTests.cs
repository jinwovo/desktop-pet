using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DesktopPet.App;
using DesktopPet.Core;

internal static class PetRecoveryUiTests
{
    public static void Run(string root)
    {
        IReadOnlyList<DesktopRect> areas = new[] { new DesktopRect(-1280, 0, 0, 1024), new DesktopRect(0, 0, 1920, 1080) };
        var folder = Path.Combine(root, "recovery-inbox");
        var pet = new MainWindow(new CharacterLibrary(MainWindow.CharactersRoot, Path.Combine(root, "recovery-custom")), inboxDirectory: folder,
            workAreasProvider: () => areas);
        var loaded = (RoutedEventHandler)Delegate.CreateDelegate(typeof(RoutedEventHandler), pet, typeof(MainWindow).GetMethod("MainWindow_Loaded", BindingFlags.Instance | BindingFlags.NonPublic)!);
        pet.Loaded -= loaded;
        var preferences = new PetSettings { Visible = false, QuietMode = true, PauseDuringFullscreen = false };
        Set(pet, "_settings", preferences);
        var layout = PlatformLayout.FromWorkAreas(areas);
        Set(pet, "_platformLayout", layout);
        var brain = new PetBrain(layout, 128, 122, -5000, -5000);
        Set(pet, "_brain", brain);
        Invoke(pet, "ApplyPresentation", CharacterPresentation.Load(Path.Combine(MainWindow.CharactersRoot, "Companion")));
        var save = (DispatcherTimer)Get(pet, "_settingsSaveTimer")!;
        save.Interval = TimeSpan.FromDays(1);
        var queue = new ConcurrentQueue<InboxNotification>();
        using var inbox = new NotificationInbox(folder);
        inbox.NotificationReceived += queue.Enqueue; inbox.Start();
        var settings = new SettingsWindow(pet);
        try
        {
            pet.SendTestNotification();
            Check(SpinWait.SpinUntil(() => !queue.IsEmpty, TimeSpan.FromSeconds(5)), "Recovery fixture receipt missing");
            while (queue.TryDequeue(out var delivery)) Invoke(pet, "OnNotification", delivery);
            pet.StartFocusTimer();
            var focus = pet.FocusSnapshot;
            var picker = (ComboBox)settings.FindName("MonitorTargetCombo");
            picker.SelectedIndex = 1;
            ((Button)settings.FindName("RecoverPetButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(brain.X == 896 && brain.Y == 958 && brain.State == PetState.Idle && pet.IsPetVisible &&
                pet.PendingNotificationCount == 1 && File.Exists(Directory.GetFiles(folder, "*.json").Single()) && pet.FocusSnapshot == focus && pet.QuietMode,
                "Recovery failed to show/land the pet or changed inbox/focus/quiet state");
            var pose = (brain.X, brain.Y);
            areas = new[] { areas[0] };
            ((Button)settings.FindName("RecoverPetButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(pose == (brain.X, brain.Y) && picker.Items.Count == 1 && ((TextBlock)settings.FindName("RecoveryStatusText")).Text.Contains("다시 선택"),
                "Stale display selection moved the pet or failed to refresh choices");
            picker.SelectedIndex = 0;
            ((Button)settings.FindName("RecoverPetButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(brain.X == -704 && brain.Y + 122 == 1024, "Negative monitor recovery lost its selected floor");
            Console.WriteLine("PASS Recovery selects real X/Y monitor geometry, restores a hidden pet, retains notifications/focus/quiet and rejects stale display selections");
        }
        finally
        {
            save.Stop(); Set(pet, "_settingsDirty", false); settings.Close(); pet.DisposeResources(); pet.Close();
        }
    }
    private static object? Get(object obj, string name) => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(obj);
    private static void Set(object obj, string name, object value) => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(obj, value);
    private static object? Invoke(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(obj, args);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
