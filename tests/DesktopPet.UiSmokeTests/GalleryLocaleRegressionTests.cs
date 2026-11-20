using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopPet.App;
using DesktopPet.Core;

internal static class GalleryLocaleRegressionTests
{
    public static void Run(string root)
    {
        var fixture = Path.Combine(root, "gallery-locale");
        var bundled = Path.Combine(fixture, "bundled");
        var custom = Path.Combine(fixture, "custom");
        CreateCharacter(bundled, "Companion");
        CreateCharacter(custom, "사용자 Locale");
        CreateCharacter(custom, "Broken Locale", brokenWalk: true);
        var priorLanguage = UiText.Language;
        UiText.SetLanguage("ko");
        var pet = new MainWindow(new CharacterLibrary(bundled, custom), inboxDirectory: Path.Combine(fixture, "inbox"));
        var preferences = new PetSettings { Language = "ko" };
        Set(pet, "_settings", preferences);
        Set(pet, "_brain", new PetBrain(new PlatformLayout(new[] { new Platform(0, 1920, 1080) }), 128, 122, 500, 958));
        Invoke(pet, "ApplyPresentation", CharacterPresentation.Load(Path.Combine(bundled, "Companion")));
        var save = (DispatcherTimer)Get(pet, "_settingsSaveTimer")!;
        save.Interval = TimeSpan.FromDays(1);
        var gallery = new CharacterGalleryWindow(pet);
        try
        {
            gallery.Show();
            Wait(gallery.LoadingTask);
            var cards = (ListBox)gallery.FindName("CharacterCards");
            var search = (TextBox)gallery.FindName("SearchBox");
            var category = (ComboBox)gallery.FindName("CategoryCombo");
            search.Text = "Locale";
            category.SelectedValue = CharacterGalleryCategory.Custom;
            var selected = gallery.AllCharacters.Single(item => item.Name == "사용자 Locale");
            cards.SelectedItem = selected;
            var all = gallery.AllCharacters;
            var loadingTask = gallery.LoadingTask;
            var image = selected.Thumbnail;
            var active = Get(pet, "_character");
            var before = JsonSerializer.Serialize(preferences);
            foreach (var language in new[] { "en", "zh-CN", "ko" })
            {
                UiText.SetLanguage(language);
                gallery.UpdateLayout();
                Pump();
                Check(search.Text == "Locale" && category.SelectedValue is CharacterGalleryCategory.Custom && ReferenceEquals(cards.SelectedItem, selected),
                    "Changing gallery language lost its search, category or selected character");
                Check(((TextBlock)gallery.FindName("SelectedNameText")).Text == selected.Name &&
                    ((TextBlock)gallery.FindName("SelectedDetailText")).Text == UiText.Get("직접 추가한 친구"),
                    "Language refresh erased a user character name or failed to localize its description");
                Check(((TextBlock)gallery.FindName("CountText")).Text == UiText.Format("{0}종 중 {1}종", all.Count, cards.Items.Count) &&
                    ((TextBlock)gallery.FindName("StatusText")).Text == UiText.Format("현재 친구: {0}", "Companion"),
                    "Language refresh reset the live character count or current-character status to their initial blank strings");
                var selectedCategory = category.SelectedItem ?? throw new Exception("Gallery category selection disappeared");
                var categoryLabel = (string)selectedCategory.GetType().GetProperty("Label")!.GetValue(selectedCategory)!;
                Check(categoryLabel == UiText.Get("추가한 캐릭터"), "Gallery category choices retained the previous language");
                Check(TextBlocks(cards).Any(block => block.Text == UiText.Get("추가한 캐릭터")),
                    "Visible gallery cards retained their previous category-label language");
                Check(ReferenceEquals(all, gallery.AllCharacters) && ReferenceEquals(loadingTask, gallery.LoadingTask) && ReferenceEquals(image, selected.Thumbnail) &&
                    ReferenceEquals(active, Get(pet, "_character")) && before == JsonSerializer.Serialize(preferences) && !(bool)Get(pet, "_settingsDirty")!,
                    "Changing gallery language reloaded artwork, applied a character or modified preferences");
            }
            Console.WriteLine("PASS Gallery KO/EN/zh-CN changes preserve search/category/card selection, user names, live counts and cached artwork");

            cards.SelectedItem = gallery.AllCharacters.Single(item => item.Name == "Broken Locale");
            Wait((Task)Invoke(gallery, "PreviewSelectedAsync")!);
            Check(Get(gallery, "_motionPreview") is null, "An invalid walking resource opened a preview");
            foreach (var language in new[] { "en", "zh-CN" })
            {
                UiText.SetLanguage(language);
                Check(((TextBlock)gallery.FindName("StatusText")).Text == UiText.Get("움직임을 읽지 못했어요. 캐릭터 파일을 확인해주세요.") &&
                    ((TextBlock)gallery.FindName("SelectedNameText")).Text == "Broken Locale" && ReferenceEquals(active, Get(pet, "_character")),
                    "Changing language lost a failed-preview status or changed the active character");
            }
            Console.WriteLine("PASS Gallery preview failure status is retranslated while preserving the failed selection and active pet");

            gallery.Close();
            VerifyRunningPreviewCancellation(pet);
        }
        finally
        {
            gallery.Close();
            save.Stop();
            Set(pet, "_settingsDirty", false);
            pet.DisposeResources();
            UiText.SetLanguage(priorLanguage);
        }
    }

    private static void VerifyRunningPreviewCancellation(MainWindow pet)
    {
        using var started = new ManualResetEventSlim();
        CancellationToken decoderToken = default;
        var sawCancellation = false;
        var gallery = new CharacterGalleryWindow(pet, (folder, token) =>
        {
            decoderToken = token;
            using var cancelled = new ManualResetEventSlim();
            using var registration = token.Register(cancelled.Set);
            started.Set();
            if (!cancelled.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Preview decoder never received gallery lifetime cancellation");
            sawCancellation = token.IsCancellationRequested;
            // The same consumer API used by production must observe the captured token even
            // after Close has cancelled and disposed the gallery's CancellationTokenSource.
            return CharacterPresentation.Load(folder, cancellationToken: token);
        });
        try
        {
            gallery.Show();
            Wait(gallery.LoadingTask);
            var cards = (ListBox)gallery.FindName("CharacterCards");
            cards.SelectedItem = gallery.AllCharacters.Single(item => item.Name == "사용자 Locale");
            var operation = (Task)Invoke(gallery, "PreviewSelectedAsync")!;
            Check(started.Wait(TimeSpan.FromSeconds(10)), "Preview worker did not enter the decoder");
            gallery.Close();
            var closedCount = ((TextBlock)gallery.FindName("CountText")).Text;
            var closedStatus = ((TextBlock)gallery.FindName("StatusText")).Text;
            Wait(operation);
            Check(decoderToken.CanBeCanceled && decoderToken.IsCancellationRequested && sawCancellation &&
                !gallery.IsVisible && Get(gallery, "_motionPreview") is null && !(bool)Get(gallery, "_previewBusy")!,
                "Closing the gallery failed to cancel an already running preview decoder or opened a late window");
            UiText.SetLanguage(UiText.Language == "en" ? "zh-CN" : "en");
            Check(((TextBlock)gallery.FindName("CountText")).Text == closedCount && ((TextBlock)gallery.FindName("StatusText")).Text == closedStatus,
                "A closed gallery remained subscribed to language updates");
            Console.WriteLine("PASS Closing a gallery cancels its already running preview decoder and detaches language updates without late UI");
        }
        finally { gallery.Close(); }
    }

    private static void CreateCharacter(string root, string name, bool brokenWalk = false)
    {
        var folder = Path.Combine(root, name);
        Directory.CreateDirectory(folder);
        File.Copy(Path.Combine(MainWindow.CharactersRoot, "Companion", "idle.png"), Path.Combine(folder, "idle.png"));
        if (brokenWalk) File.WriteAllText(Path.Combine(folder, "walk.png"), "corrupt preview PNG");
        File.WriteAllText(Path.Combine(folder, "character.json"), JsonSerializer.Serialize(new
        {
            name, frameSizePx = new { width = 128, height = 128 }, anchor = new { x = 64, y = 122 },
            frames = new { idle = new[] { "idle.png" }, walk = new[] { brokenWalk ? "walk.png" : "idle.png" } }
        }));
    }

    private static IEnumerable<TextBlock> TextBlocks(DependencyObject node)
    {
        if (node is TextBlock text) yield return text;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            foreach (var child in TextBlocks(VisualTreeHelper.GetChild(node, i))) yield return child;
    }
    private static void Wait(Task task)
    {
        var timeout = Stopwatch.StartNew();
        while (!task.IsCompleted && timeout.Elapsed < TimeSpan.FromSeconds(20)) { Pump(); Thread.Sleep(2); }
        Check(task.IsCompleted, "Gallery locale/cancellation UI operation timed out");
        task.GetAwaiter().GetResult();
    }
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
    private static object? Get(object obj, string name) => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(obj);
    private static void Set(object obj, string name, object value) => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(obj, value);
    private static object? Invoke(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(obj, args);
    private static void Check(bool valid, string message) { if (!valid) throw new Exception(message); }
}
