using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopPet.App;
using DesktopPet.Core;

internal static class ArchiveImportUiTests
{
    public static void VerifyPack(string path, string root)
    {
        var library = new CharacterLibrary(Path.Combine(root, "archive-empty-bundled"), Path.Combine(root, "archive-installed"));
        using var archive = CharacterArchive.Open(path);
        foreach (var candidate in archive.Characters)
        {
            var name = archive.Import(candidate, library, folder => { _ = CharacterPresentation.Load(folder, includeRasterGait: true); });
            Check(CharacterPresentation.Load(library.Resolve(name)).Frames.Count > 0, "Installed release pack cannot render");
        }
        Check(library.GetNames().Count == archive.Characters.Count, "Release archive lost a candidate");
        Console.WriteLine($"PASS Actual character release ZIP: {archive.Characters.Count} packs imported, full PNG sets decoded and installed presentations reloaded");
    }

    public static void Run(string root)
    {
        var fixture = Path.Combine(root, "zip-ui");
        Directory.CreateDirectory(fixture);
        var library = new CharacterLibrary(MainWindow.CharactersRoot, Path.Combine(fixture, "custom"));
        var pet = new MainWindow(library, inboxDirectory: Path.Combine(fixture, "inbox"));
        var preferences = new PetSettings { CharacterName = "Companion", QuietMode = true };
        Set(pet, "_settings", preferences);
        Set(pet, "_brain", new PetBrain(new PlatformLayout(new[] { new Platform(0, 1920, 1080) }), 128, 122, 500, 958));
        Invoke(pet, "ApplyPresentation", CharacterPresentation.Load(Path.Combine(MainWindow.CharactersRoot, "Companion")));
        var save = (DispatcherTimer)Get(pet, "_settingsSaveTimer")!;
        save.Interval = TimeSpan.FromDays(1); // This fixture must never write production preferences.
        var settings = new SettingsWindow(pet);
        settings.Show();
        try
        {
            Invoke(settings, "Gallery_Click", null!, new RoutedEventArgs());
            var gallery = (CharacterGalleryWindow)Get(settings, "_characterGallery")!;
            Wait(gallery.LoadingTask);
            ((TextBox)gallery.FindName("SearchBox")).Text = "ZIP";
            var single = Zip(fixture, ("ZIP 친구", false));
            Wait((Task)Invoke(settings, "ImportCharacterArchiveAsync", single)!);
            Wait(gallery.LoadingTask);
            Check(pet.CurrentCharacterName == "ZIP 친구" && gallery.AllCharacters.Any(c => c.Name == "ZIP 친구") &&
                  ((TextBox)gallery.FindName("SearchBox")).Text == "ZIP", "ZIP import failed to activate or refresh the filtered gallery");
            var profile = ((PetBrain)Get(pet, "_brain")!).BehaviorProfile;
            Check(profile.SleepAfterIdleMs == 60000 && ((TextBlock)settings.FindName("CharacterHint")).Text.Contains("ZIP 성향"),
                "Imported personality was not applied or shown");
            Check(((Button)settings.FindName("AddArchiveButton")).IsEnabled && ((Button)settings.FindName("AddCharacterButton")).IsEnabled,
                "Import left source buttons disabled");
            Console.WriteLine("PASS Actual ZIP installation validates images, activates the selected personality and refreshes a filtered open gallery");

            var collection = Zip(fixture, ("ZIP Alpha", false), ("ZIP Beta", false));
            WithPicker(() => (Task)Invoke(settings, "ImportCharacterArchiveAsync", collection)!, picker =>
            {
                var list = Descendants<ListBox>(picker).Single();
                Check(list.Items.Count == 2 && pet.CurrentCharacterName == "ZIP 친구", "Opening collection changed active pet");
                list.SelectedIndex = 1;
                Descendants<Button>(picker).Single(b => b.Name == "InstallArchiveCharacter").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            });
            Check(pet.CurrentCharacterName == "ZIP Beta" && !library.GetNames().Contains("ZIP Alpha"), "Collection installed the wrong or unselected pack");
            Console.WriteLine("PASS ZIP collection picker installs only the explicitly selected pack");

            pet.SwitchCharacter("Companion");
            save.Stop(); Set(pet, "_settingsDirty", false);
            var before = JsonSerializer.Serialize(preferences);
            WithPicker(() => (Task)Invoke(settings, "ImportCharacterArchiveAsync", collection)!, picker => picker.DialogResult = false);
            Check(JsonSerializer.Serialize(preferences) == before && !(bool)Get(pet, "_settingsDirty")! && !library.GetNames().Contains("ZIP Alpha"),
                "Cancelling ZIP selection changed preferences or installed a pack");
            Wait((Task)Invoke(settings, "ImportCharacterArchiveAsync", single)!);
            Check(pet.CurrentCharacterName == "Companion" && ((TextBlock)settings.FindName("CharacterImportStatusText")).Text.Contains("못했"),
                "Duplicate ZIP import replaced active character");
            Wait((Task)Invoke(settings, "ImportCharacterArchiveAsync", Zip(fixture, ("ZIP Broken", true)))!);
            Check(!library.GetNames().Contains("ZIP Broken") && pet.CurrentCharacterName == "Companion" && JsonSerializer.Serialize(preferences) == before,
                "Corrupt ZIP PNG published or changed preferences");
            Console.WriteLine("PASS ZIP cancellation, duplicate names and corrupt PNGs preserve existing characters and preferences");

            var pending = new TaskCompletionSource<string?>();
            var operation = (Task)Invoke(settings, "RunCharacterImportAsync", new Func<Task<string?>>(() => pending.Task))!;
            Check(!((Button)settings.FindName("AddArchiveButton")).IsEnabled && !((Button)settings.FindName("AddCharacterButton")).IsEnabled,
                "Pending import did not prevent concurrent source selection");
            settings.Close();
            pending.SetResult("ZIP Beta");
            Wait(operation);
            Check(pet.CurrentCharacterName == "Companion" && JsonSerializer.Serialize(preferences) == before,
                "An import completion after settings closure changed the active pet");
            using (File.Open(single, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            Console.WriteLine("PASS Closing Settings during import ignores late application and ZIP handles are released after completion/cancellation");
        }
        finally
        {
            save.Stop(); Set(pet, "_settingsDirty", false);
            settings.Close();
            pet.DisposeResources();
        }
    }

    private static string Zip(string root, params (string Name, bool Corrupt)[] characters)
    {
        var path = Path.Combine(root, Guid.NewGuid().ToString("N") + ".zip");
        var png = File.ReadAllBytes(Path.Combine(MainWindow.CharactersRoot, "Companion", "idle.png"));
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var character in characters)
        {
            var manifest = JsonSerializer.Serialize(new
            {
                name = character.Name, frameSizePx = new { width = 128, height = 128 }, anchor = new { x = 64, y = 122 },
                frames = new { idle = new[] { "idle.png" } }, behavior = new { sleepAfterIdleMs = 60000, description = "ZIP 성향" }
            });
            using (var stream = archive.CreateEntry(character.Name + "/character.json").Open()) stream.Write(Encoding.UTF8.GetBytes(manifest));
            using (var stream = archive.CreateEntry(character.Name + "/idle.png").Open()) stream.Write(character.Corrupt ? new byte[] { 1 } : png);
        }
        return path;
    }
    private static void WithPicker(Func<Task> start, Action<CharacterArchiveWindow> select)
    {
        var picked = false;
        Exception? failure = null;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) =>
        {
            var picker = Application.Current.Windows.OfType<CharacterArchiveWindow>().FirstOrDefault(w => w.IsVisible);
            if (picker is null) return;
            timer.Stop();
            try { select(picker); picked = true; }
            catch (Exception ex) { failure = ex; picker.DialogResult = false; }
        };
        timer.Start();
        try { Wait(start()); Check(picked, "ZIP selection window never appeared: " + failure?.Message); }
        finally { timer.Stop(); }
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var item in Descendants<T>(child)) yield return item;
        }
    }
    private static void Wait(Task task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!task.IsCompleted)
        {
            Check(DateTime.UtcNow < deadline, "ZIP UI operation timed out");
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(5);
        }
        task.GetAwaiter().GetResult();
    }
    private static object? Get(object instance, string field) => instance.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance);
    private static void Set(object instance, string field, object? value) => instance.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(instance, value);
    private static object? Invoke(object instance, string method, params object[] args) => instance.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(instance, args);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
