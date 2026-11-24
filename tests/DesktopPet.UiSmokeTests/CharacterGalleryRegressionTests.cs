using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DesktopPet.App;
using DesktopPet.Core;

internal static class CharacterGalleryRegressionTests
{
    public static void CaptureInstalledGallery(string root)
    {
        var pet = new MainWindow(new CharacterLibrary(MainWindow.CharactersRoot, Path.Combine(root, "gallery-capture-custom")),
            inboxDirectory: Path.Combine(root, "gallery-capture-inbox"));
        Set(pet, "_settings", new PetSettings { CharacterName = "Companion", QuietMode = true });
        Set(pet, "_brain", new PetBrain(new PlatformLayout(new[] { new Platform(0, 1920, 1080) }), 128, 122, 500, 958));
        Invoke(pet, "ApplyPresentation", CharacterPresentation.Load(Path.Combine(MainWindow.CharactersRoot, "Companion")));
        var gallery = new CharacterGalleryWindow(pet);
        try
        {
            gallery.Show();
            Wait(gallery.LoadingTask);
            Check(gallery.AllCharacters.Count > 0 && gallery.AllCharacters.All(item => item.IsAvailable),
                "Installed gallery capture includes unavailable characters");
            var cards = (ListBox)gallery.FindName("CharacterCards");
            cards.SelectedItem = gallery.AllCharacters.FirstOrDefault(item => item.Name == "시나모롤") ?? gallery.AllCharacters[0];
            gallery.Width = 800;
            gallery.Height = 800;
            Save(gallery, "character-gallery-v060.png");
            Check(pet.CurrentCharacterName == "Companion" && !(bool)Get(pet, "_settingsDirty")!, "Capturing installed artwork changed the live character/preferences");
            Console.WriteLine($"CAPTURE Actual installed gallery ({gallery.AllCharacters.Count} characters): " +
                Path.Combine(AppContext.BaseDirectory, "character-gallery-v060.png"));
        }
        finally
        {
            gallery.Close();
            ((DispatcherTimer)Get(pet, "_settingsSaveTimer")!).Stop();
            Set(pet, "_settingsDirty", false);
            pet.DisposeResources();
        }
    }

    public static void Run(string root, bool capture = false)
    {
        var fixture = Path.Combine(root, "gallery");
        var bundled = Path.Combine(fixture, "bundled");
        var custom = Path.Combine(fixture, "custom");
        foreach (var name in new[] { "Companion", "Ember", "Nimbus", "치이카와" }) CreateCharacter(bundled, name);
        CreateCharacter(custom, "사용자 친구");
        var brokenWalk = CreateCharacter(custom, "BrokenWalk", separateWalk: true);
        File.WriteAllText(Path.Combine(brokenWalk, "walk.png"), "corrupt walk frame");
        var brokenIdle = CreateCharacter(custom, "BrokenIdle");
        File.WriteAllText(Path.Combine(brokenIdle, "idle.png"), "corrupt idle frame");
        VerifyThumbnailDecoder(bundled, fixture);

        var pet = new MainWindow(new CharacterLibrary(bundled, custom), inboxDirectory: Path.Combine(fixture, "inbox"));
        var preferences = new PetSettings { CharacterName = "Companion", Visible = true, QuietMode = true };
        Set(pet, "_settings", preferences);
        Set(pet, "_brain", new PetBrain(new PlatformLayout(new[] { new Platform(0, 1920, 1080) }), 512, 250, 500, 830));
        Invoke(pet, "ApplyPresentation", CharacterPresentation.Load(Path.Combine(bundled, "Companion")));
        // The real startup/settings loader is never invoked. Even a slow async preview must not
        // allow the production preference debounce to write this fixture into the user's profile.
        var saveTimer = (DispatcherTimer)Get(pet, "_settingsSaveTimer")!;
        saveTimer.Interval = TimeSpan.FromDays(1);
        var gallery = new CharacterGalleryWindow(pet);
        MotionPreviewWindow? motion = null;
        try
        {
            var originalCharacter = Get(pet, "_character");
            var originalBrain = Get(pet, "_brain");
            var originalPreferences = JsonSerializer.Serialize(preferences);
            void Unchanged(string operation)
            {
                Check(ReferenceEquals(Get(pet, "_character"), originalCharacter) &&
                    ReferenceEquals(Get(pet, "_brain"), originalBrain) &&
                    JsonSerializer.Serialize(preferences) == originalPreferences &&
                    !(bool)Get(pet, "_settingsDirty")!, operation + " changed the live pet or preferences");
            }

            gallery.Show();
            Wait(gallery.LoadingTask);
            var cards = (ListBox)gallery.FindName("CharacterCards");
            var search = (TextBox)gallery.FindName("SearchBox");
            var category = (ComboBox)gallery.FindName("CategoryCombo");
            var apply = (Button)gallery.FindName("ApplyButton");
            Check(gallery.AllCharacters.Count == 7, "Gallery omitted fixture characters or introduced unrelated installed characters");
            Check(gallery.AllCharacters.Single(item => item.Name == "BrokenWalk").IsAvailable &&
                !gallery.AllCharacters.Single(item => item.Name == "BrokenIdle").IsAvailable,
                "Thumbnail loading decoded unrelated walk frames or accepted corrupt idle artwork");
            Check(gallery.AllCharacters.Where(item => item.Thumbnail is not null).All(item =>
                item.Thumbnail!.IsFrozen && item.Thumbnail.PixelWidth <= 128 && item.Thumbnail.PixelHeight <= 128),
                "Gallery retained mutable or unbounded card bitmaps");
            using (File.Open(Path.Combine(bundled, "Companion", "idle.png"), FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            Unchanged("Opening the gallery");
            Console.WriteLine("PASS Gallery isolates thumbnail decoding, rejects corrupt idle art and releases frozen bounded image streams");

            category.SelectedValue = CharacterGalleryCategory.Original;
            Check(Names(cards).SetEquals(new[] { "Companion", "Ember", "Nimbus" }), "Original category includes other characters or omits a bundled original");
            category.SelectedValue = CharacterGalleryCategory.FanArt;
            Check(Names(cards).SetEquals(new[] { "치이카와" }), "Fan-art category does not use the character catalog");
            category.SelectedValue = CharacterGalleryCategory.Custom;
            Check(Names(cards).SetEquals(new[] { "사용자 친구", "BrokenWalk", "BrokenIdle" }), "Custom category includes another library root");
            category.SelectedValue = CharacterGalleryCategory.All;
            foreach (var query in new[] { "치이카와", "CHIIKAWA", "  chiikawa  " })
            {
                search.Text = query;
                Check(Names(cards).SetEquals(new[] { "치이카와" }), "Korean/English alias or case-insensitive trimmed search failed: " + query);
            }
            search.Text = "사용자 친구";
            Check(Names(cards).SetEquals(new[] { "사용자 친구" }), "Unicode custom character search failed");
            search.Text = "definitely-no-character";
            Check(cards.Items.Count == 0 && cards.SelectedItem is null && !apply.IsEnabled, "Empty search retained an applicable stale character");
            Unchanged("Search and category filters");
            search.Text = "";
            cards.SelectedItem = gallery.AllCharacters.Single(item => item.Name == "Ember");
            Check(((Image)gallery.FindName("PreviewImage")).Source is not null && apply.IsEnabled, "Card selection did not prepare its static preview and explicit apply action");
            Unchanged("Selecting a card");
            Console.WriteLine("PASS Gallery searches Korean/English aliases and custom names, filters categories and keeps card selection preview-only");

            Click(gallery, "PreviewButton");
            Wait(gallery.PreviewOperation);
            motion = (MotionPreviewWindow?)Get(gallery, "_motionPreview");
            Check(motion is { IsVisible: true, IsAnimating: true } && ReferenceEquals(motion.Owner, gallery), "Preview action did not open its isolated animated window");
            Unchanged("Animated preview");
            motion!.Close();
            Check(!motion.IsAnimating, "Closed gallery motion preview retained its render loop");
            cards.SelectedItem = gallery.AllCharacters.Single(item => item.Name == "BrokenWalk");
            Click(gallery, "PreviewButton");
            Wait(gallery.PreviewOperation);
            Check(!string.IsNullOrWhiteSpace(((TextBlock)gallery.FindName("StatusText")).Text), "Failed preview did not explain its corrupt frame");
            Unchanged("Corrupt animation preview");
            Console.WriteLine("PASS Gallery animation preview runs separately, stops on close and preserves the live pet when a non-thumbnail frame is corrupt");

            var applied = new List<string>();
            gallery.CharacterApplied += applied.Add;
            cards.SelectedItem = gallery.AllCharacters.Single(item => item.Name == "Ember");
            Click(gallery, "ApplyButton");
            Check(pet.CurrentCharacterName == "Ember" && preferences.CharacterName == "Ember" && applied.SequenceEqual(new[] { "Ember" }),
                "Explicit gallery apply did not activate exactly the selected validated character");
            saveTimer.Stop();
            Set(pet, "_settingsDirty", false);
            var appliedCharacter = Get(pet, "_character");
            var appliedPreferences = JsonSerializer.Serialize(preferences);
            cards.SelectedItem = gallery.AllCharacters.Single(item => item.Name == "Nimbus");
            File.WriteAllText(Path.Combine(bundled, "Nimbus", "idle.png"), "replaced after thumbnail loading");
            Click(gallery, "ApplyButton");
            Check(ReferenceEquals(Get(pet, "_character"), appliedCharacter) && pet.CurrentCharacterName == "Ember" &&
                JsonSerializer.Serialize(preferences) == appliedPreferences && applied.Count == 1 && !(bool)Get(pet, "_settingsDirty")!,
                "Apply used a stale thumbnail/presentation or changed live state after the selected files became corrupt");
            Console.WriteLine("PASS Explicit gallery apply reloads selected files and preserves active character/preferences when those files become corrupt");

            if (capture)
            {
                search.Text = "";
                cards.SelectedItem = gallery.AllCharacters.Single(item => item.Name == "Ember");
                Save(gallery, "gallery-fixture.png");
            }
            cards.SelectedItem = gallery.AllCharacters.Single(item => item.Name == "Ember");
            Click(gallery, "PreviewButton");
            Wait(gallery.PreviewOperation);
            motion = (MotionPreviewWindow?)Get(gallery, "_motionPreview");
            Check(motion is { IsAnimating: true }, "Final lifecycle fixture preview did not start");
            gallery.Close();
            Check(!motion!.IsAnimating && !motion.IsVisible, "Closing gallery retained its child motion preview or animation loop");
            Console.WriteLine("PASS Closing the gallery closes its owned animation preview");

            var settings = new SettingsWindow(pet);
            try
            {
                settings.Show();
                Click(settings, "GalleryButton");
                var ownedGallery = (CharacterGalleryWindow)Get(settings, "_characterGallery")!;
                Wait(ownedGallery.LoadingTask);
                Click(settings, "GalleryButton");
                Check(ReferenceEquals(ownedGallery, Get(settings, "_characterGallery")) && ReferenceEquals(ownedGallery.Owner, settings) &&
                    pet.CurrentCharacterName == "Ember" && !(bool)Get(pet, "_settingsDirty")!,
                    "Settings gallery entry opened duplicate windows or automatically changed the active character");
                ((ListBox)ownedGallery.FindName("CharacterCards")).SelectedItem = ownedGallery.AllCharacters.Single(item => item.Name == "사용자 친구");
                Click(ownedGallery, "ApplyButton");
                saveTimer.Stop();
                Set(pet, "_settingsDirty", false);
                Check(pet.CurrentCharacterName == "사용자 친구" && ((ComboBox)settings.FindName("CharacterCombo")).SelectedItem is "사용자 친구",
                    "Applying a gallery character left the parent settings selection stale");

                var ownedSearch = (TextBox)ownedGallery.FindName("SearchBox");
                var ownedCategory = (ComboBox)ownedGallery.FindName("CategoryCombo");
                var ownedCards = (ListBox)ownedGallery.FindName("CharacterCards");
                ownedSearch.Text = "사용자";
                ownedCategory.SelectedValue = CharacterGalleryCategory.Custom;
                ownedCards.SelectedItem = ownedGallery.AllCharacters.Single(item => item.Name == "사용자 친구");
                var refreshApplications = 0;
                ownedGallery.CharacterApplied += _ => refreshApplications++;
                var imported = CreateCharacter(Path.Combine(fixture, "import-source"), "사용자 새 친구");
                Wait((Task)Invoke(settings, "ImportCharacterFolderAsync", imported)!);
                Wait(ownedGallery.LoadingTask);
                saveTimer.Stop();
                Set(pet, "_settingsDirty", false);
                Check(ownedGallery.AllCharacters.Any(item => item.Name == "사용자 새 친구" && item.IsAvailable && item.Category == CharacterGalleryCategory.Custom) &&
                    Names(ownedCards).SetEquals(new[] { "사용자 친구", "사용자 새 친구" }), "Successful Settings import left the open gallery stale");
                Check(ownedSearch.Text == "사용자" && ownedCategory.SelectedValue is CharacterGalleryCategory.Custom &&
                    ownedCards.SelectedItem is CharacterGalleryItem { Name: "사용자 친구" } && refreshApplications == 0 &&
                    pet.CurrentCharacterName == "사용자 새 친구" && ((ComboBox)settings.FindName("CharacterCombo")).SelectedItem is "사용자 새 친구",
                    "Refresh reset search/selection, applied a card or reversed the explicitly imported character");
                Console.WriteLine("PASS Importing through Settings refreshes its open gallery while preserving search/category/card selection and avoiding an extra apply");

                var activeBeforeRefresh = Get(pet, "_character");
                var preferencesBeforeRefresh = JsonSerializer.Serialize(preferences);
                ownedGallery.RefreshCharacters();
                var olderLoad = ownedGallery.LoadingTask;
                CreateCharacter(custom, "재검색 최신 친구");
                ownedGallery.RefreshCharacters();
                var latestLoad = ownedGallery.LoadingTask;
                Wait(latestLoad);
                Wait(olderLoad);
                Check(ownedGallery.AllCharacters.Any(item => item.Name == "재검색 최신 친구" && item.IsAvailable) &&
                    ownedSearch.Text == "사용자" && ownedCards.SelectedItem is CharacterGalleryItem { Name: "사용자 친구" } &&
                    ReferenceEquals(Get(pet, "_character"), activeBeforeRefresh) && JsonSerializer.Serialize(preferences) == preferencesBeforeRefresh &&
                    refreshApplications == 0 && !(bool)Get(pet, "_settingsDirty")!, "Rapid refresh published an older catalog or changed the active pet/preferences");
                Console.WriteLine("PASS Rapid gallery refresh retains the newest library snapshot and preserves filters and active pet preferences");
                settings.Close();
                Check(!ownedGallery.IsVisible, "Closing settings retained its owned gallery");
                Console.WriteLine("PASS Settings opens one owned gallery, synchronizes explicit application and closes the gallery with its parent");
            }
            finally { settings.Close(); }

            var closingGallery = new CharacterGalleryWindow(pet);
            try
            {
                closingGallery.Show();
                closingGallery.RefreshCharacters();
                var pendingLoad = closingGallery.LoadingTask;
                closingGallery.Close();
                var closedSnapshot = closingGallery.AllCharacters;
                Wait(pendingLoad);
                closingGallery.RefreshCharacters();
                Check(ReferenceEquals(closingGallery.AllCharacters, closedSnapshot) && !closingGallery.IsVisible,
                    "An outstanding or newly requested load mutated a closed gallery");
                Console.WriteLine("PASS Closing a gallery cancels outstanding loading and ignores later refresh requests");
            }
            finally { closingGallery.Close(); }
        }
        finally
        {
            motion?.Close();
            gallery.Close();
            saveTimer.Stop();
            Set(pet, "_settingsDirty", false);
            pet.DisposeResources();
        }
    }

    private static void VerifyThumbnailDecoder(string bundled, string fixture)
    {
        var thumbnail = CharacterPresentation.LoadThumbnail(Path.Combine(bundled, "Companion"), 96);
        Check(thumbnail.IsFrozen && thumbnail.PixelWidth == 96 && thumbnail.PixelHeight == 48,
            "Thumbnail decoder did not bound both axes while retaining aspect ratio");
        var portrait = CreateCharacter(Path.Combine(fixture, "portrait"), "Portrait", width: 256, height: 512);
        var tallThumbnail = CharacterPresentation.LoadThumbnail(portrait, 96);
        Check(tallThumbnail.IsFrozen && tallThumbnail.PixelWidth == 48 && tallThumbnail.PixelHeight == 96,
            "Thumbnail decoder did not bound a portrait image's longest axis");
        var invalid = CreateCharacter(Path.Combine(fixture, "header"), "MismatchedHeader");
        var path = Path.Combine(invalid, "idle.png");
        var bytes = File.ReadAllBytes(path);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16, 4), 32768);
        File.WriteAllBytes(path, bytes);
        try { CharacterPresentation.LoadThumbnail(invalid, 96); }
        catch (InvalidDataException) { return; }
        throw new Exception("Thumbnail loader accepted a PNG header whose dimensions exceed its manifest");
    }

    private static string CreateCharacter(string root, string name, bool separateWalk = false, int width = 512, int height = 256)
    {
        var folder = Path.Combine(root, name);
        Directory.CreateDirectory(folder);
        var pixels = new byte[width * height * 4];
        for (var y = 12; y < height - 6; y++)
        for (var x = width / 4; x < width * 3 / 4; x++)
        {
            var offset = (y * width + x) * 4;
            pixels[offset] = 180;
            pixels[offset + 1] = 210;
            pixels[offset + 2] = 245;
            pixels[offset + 3] = 255;
        }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(Path.Combine(folder, "idle.png"))) encoder.Save(stream);
        if (separateWalk) File.Copy(Path.Combine(folder, "idle.png"), Path.Combine(folder, "walk.png"));
        File.WriteAllText(Path.Combine(folder, "character.json"), JsonSerializer.Serialize(new
        {
            name, frameSizePx = new { width, height }, anchor = new { x = width / 2, y = height - 6 },
            frames = new { idle = new[] { "idle.png" }, walk = new[] { separateWalk ? "walk.png" : "idle.png" } }
        }));
        return folder;
    }

    private static HashSet<string> Names(ListBox cards) => cards.Items.Cast<CharacterGalleryItem>().Select(item => item.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
    private static void Click(Window window, string name)
    {
        ((Button)window.FindName(name)).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        if (window is CharacterGalleryWindow gallery && name == "ApplyButton") Wait(gallery.ApplyOperation);
    }
    private static void Wait(Task task)
    {
        var timeout = Stopwatch.StartNew();
        while (!task.IsCompleted && timeout.Elapsed < TimeSpan.FromSeconds(15))
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(2);
        }
        Check(task.IsCompleted, "Gallery async UI operation timed out");
        task.GetAwaiter().GetResult();
    }
    private static void Save(Window window, string filename)
    {
        window.UpdateLayout();
        var content = (FrameworkElement)window.Content;
        const int padding = 16;
        var width = (int)Math.Ceiling(content.ActualWidth) + padding * 2;
        var height = (int)Math.Ceiling(content.ActualHeight) + padding * 2;
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            context.DrawRectangle(window.Background, null, new Rect(0, 0, width, height));
            context.DrawRectangle(new VisualBrush(content), null, new Rect(padding, padding, content.ActualWidth, content.ActualHeight));
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(AppContext.BaseDirectory, filename));
        encoder.Save(stream);
    }
    private static object? Get(object instance, string name) => instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance);
    private static void Set(object instance, string name, object value) => instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(instance, value);
    private static object? Invoke(object instance, string name, params object[] args) => instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(instance, args);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
