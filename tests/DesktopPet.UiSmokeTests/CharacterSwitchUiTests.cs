using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DesktopPet.App;
using DesktopPet.Core;

internal static class CharacterSwitchUiTests
{
    public static void Run(string root)
    {
        var uiThread = Environment.CurrentManagedThreadId;
        var entered = Enumerable.Range(0, 8).Select(_ => new ManualResetEventSlim()).ToArray();
        var release = Enumerable.Range(0, 8).Select(_ => new ManualResetEventSlim()).ToArray();
        var tokens = new CancellationToken[8];
        var decoderThread = 0;
        var calls = 0;
        var operations = new List<Task>();
        var pet = new MainWindow(new CharacterLibrary(MainWindow.CharactersRoot, Path.Combine(root, "switch-custom")),
            inboxDirectory: Path.Combine(root, "switch-inbox"), characterLoader: (folder, token) =>
            {
                var call = Interlocked.Increment(ref calls);
                decoderThread = Environment.CurrentManagedThreadId;
                tokens[call] = token;
                entered[call].Set();
                if (call is 1 or 4 or 5 or 7 && decoderThread != uiThread && !release[call].Wait(TimeSpan.FromSeconds(15)))
                    throw new TimeoutException("Character decoder was never released");
                if (call == 3 || call == 1 && decoderThread != uiThread) throw new IOException("Controlled character decoder failure");
                // Model a decoder finishing even after its cancellation request.
                return CharacterPresentation.Load(folder);
            });
        var loaded = typeof(MainWindow).GetMethod("MainWindow_Loaded", BindingFlags.Instance | BindingFlags.NonPublic)!;
        pet.Loaded -= loaded.CreateDelegate<RoutedEventHandler>(pet);
        Set(pet, "_settings", new PetSettings { CharacterName="Companion", QuietMode=true });
        Set(pet, "_brain", new PetBrain(new PlatformLayout(new[] { new Platform(0, 1920, 1080) }), 128, 122, 500, 958));
        Invoke(pet, "ApplyPresentation", CharacterPresentation.Load(Path.Combine(MainWindow.CharactersRoot, "Companion")));
        var timer = (DispatcherTimer)Get(pet, "_settingsSaveTimer")!;
        timer.Interval = TimeSpan.FromDays(1);
        try
        {
            var old = pet.SwitchCharacterAsync("Nimbus");
            operations.Add(old);
            Until(() => entered[1].IsSet);
            Check(decoderThread != uiThread && !old.IsCompleted, "Character decoding still runs on the UI thread");
            var input = false;
            pet.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => input = true));
            Until(() => input);
            var brain = Get(pet, "_brain");
            pet.SetScaleMultiplier(1.5);
            Set(pet, "_settingsDirty", false);
            var latest = pet.SwitchCharacterAsync("Ember");
            operations.Add(latest);
            Wait(latest);
            Check(latest.Result && tokens[1].IsCancellationRequested && pet.CurrentCharacterName == "Ember" &&
                ReferenceEquals(brain, Get(pet, "_brain")) && pet.CurrentScaleMultiplier == 1.5,
                "Newest character switch did not preserve the live brain/current scale or cancel the old worker");
            var active = pet.CurrentPresentation;
            var preferences = JsonSerializer.Serialize(Get(pet, "_settings"));
            release[1].Set(); Wait(old);
            Check(!old.Result && ReferenceEquals(active, pet.CurrentPresentation) && preferences == JsonSerializer.Serialize(Get(pet, "_settings")),
                "A stale decoder failure changed the newer character/preferences");
            Console.WriteLine("PASS Character decode runs off UI, keeps input responsive and ignores a stale failure after a newer selection");

            var failed = pet.SwitchCharacterAsync("Nimbus");
            try { Wait(failed); throw new Exception("Current decoder failure was swallowed"); }
            catch (IOException) { }
            Check(ReferenceEquals(active, pet.CurrentPresentation) && preferences == JsonSerializer.Serialize(Get(pet, "_settings")),
                "A current decoder failure partially changed the active presentation/preferences");

            using var cancellation = new CancellationTokenSource();
            var cancelled = pet.SwitchCharacterAsync("Nimbus", cancellation.Token);
            operations.Add(cancelled); Until(() => entered[4].IsSet);
            using var alreadyCancelled = new CancellationTokenSource();
            alreadyCancelled.Cancel();
            try { Wait(pet.SwitchCharacterAsync("Ember", alreadyCancelled.Token)); throw new Exception("Already cancelled request succeeded"); }
            catch (OperationCanceledException) { }
            Check(!tokens[4].IsCancellationRequested, "An already cancelled caller invalidated an active switch");
            cancellation.Cancel(); release[4].Set(); Wait(cancelled);
            Check(!cancelled.Result && ReferenceEquals(active, pet.CurrentPresentation) && preferences == JsonSerializer.Serialize(Get(pet, "_settings")),
                "A cancelled decode applied after completion");
            Console.WriteLine("PASS Failed/cancelled character preparation is atomic and a cancelled caller cannot invalidate an active request");

            var superseded = pet.SwitchCharacterAsync("Nimbus");
            operations.Add(superseded); Until(() => entered[5].IsSet);
            pet.SwitchCharacter("Ember");
            var synchronous = pet.CurrentPresentation;
            release[5].Set(); Wait(superseded);
            Check(!superseded.Result && tokens[5].IsCancellationRequested && ReferenceEquals(synchronous, pet.CurrentPresentation),
                "Synchronous selection failed to supersede pending asynchronous preparation");

            var closing = pet.SwitchCharacterAsync("Nimbus");
            operations.Add(closing); Until(() => entered[7].IsSet);
            timer.Stop(); Set(pet, "_settingsDirty", false);
            pet.DisposeResources();
            release[7].Set(); Wait(closing);
            Check(!closing.Result && tokens[7].IsCancellationRequested && ReferenceEquals(synchronous, pet.CurrentPresentation) &&
                !(bool)Get(pet, "_settingsDirty")!, "Disposal allowed a late character apply or preference write");
            var afterDisposal = pet.SwitchCharacterAsync("Ember"); Wait(afterDisposal);
            Check(!afterDisposal.Result && calls == 7, "Disposed pet started new decoding work");
            Console.WriteLine("PASS Explicit synchronous selection supersedes old work; disposal cancels pending decoding and prevents late application");
        }
        finally
        {
            foreach (var gate in release) gate.Set();
            foreach (var operation in operations) { try { Wait(operation); } catch { } }
            timer.Stop(); Set(pet, "_settingsDirty", false); pet.DisposeResources(); pet.Close();
            foreach (var gate in entered.Concat(release)) gate.Dispose();
        }
        VerifyUiPaths(root);
        VerifyImportCancelsPendingSelection(root);
    }

    private static void VerifyImportCancelsPendingSelection(string root)
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        CancellationToken decoderToken = default;
        var pet = new MainWindow(new CharacterLibrary(MainWindow.CharactersRoot, Path.Combine(root, "switch-cancel-import-custom")),
            inboxDirectory: Path.Combine(root, "switch-cancel-import-inbox"), characterLoader: (folder, token) =>
            {
                decoderToken = token; entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Pending selection was never released");
                return CharacterPresentation.Load(folder);
            });
        pet.Loaded -= typeof(MainWindow).GetMethod("MainWindow_Loaded", BindingFlags.Instance | BindingFlags.NonPublic)!.CreateDelegate<RoutedEventHandler>(pet);
        Set(pet, "_settings", new PetSettings { CharacterName="Companion", QuietMode=true });
        Set(pet, "_brain", new PetBrain(new PlatformLayout(new[] { new Platform(0, 1920, 1080) }), 128, 122, 500, 958));
        Invoke(pet, "ApplyPresentation", CharacterPresentation.Load(Path.Combine(MainWindow.CharactersRoot, "Companion")));
        var timer = (DispatcherTimer)Get(pet, "_settingsSaveTimer")!;
        timer.Interval = TimeSpan.FromDays(1);
        var settings = new SettingsWindow(pet);
        Task? selection = null, import = null;
        var pending = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            settings.Show();
            var combo = (ComboBox)settings.FindName("CharacterCombo");
            combo.SelectedItem = "Nimbus";
            selection = settings.CharacterSelectionOperation; Until(() => entered.IsSet);
            import = (Task)typeof(SettingsWindow).GetMethod("RunCharacterImportAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(settings, new object[] { new Func<Task<string?>>(() => pending.Task) })!;
            Check(decoderToken.IsCancellationRequested && combo.SelectedItem is "Companion", "Starting an import left an obsolete selection pending in the combo");
            pending.SetResult(null); release.Set(); Wait(import); Wait(selection);
            Check(pet.CurrentCharacterName == "Companion" && combo.SelectedItem is "Companion" && combo.IsEnabled &&
                !(bool)Get(pet, "_settingsDirty")! && ReferenceEquals(pet.CurrentPresentation, Get(settings, "_previewPresentation")),
                "Cancelling an import revived a previous selection or left a mismatched preview/preferences");
            Console.WriteLine("PASS Starting then cancelling import cancels an older pending selection and keeps combo, preview, pet and preferences consistent");
        }
        finally
        {
            pending.TrySetResult(null); release.Set(); settings.Close();
            if (selection is not null) { try { Wait(selection); } catch { } }
            if (import is not null) { try { Wait(import); } catch { } }
            timer.Stop(); Set(pet, "_settingsDirty", false); pet.DisposeResources(); pet.Close();
        }
    }

    private static void VerifyUiPaths(string root)
    {
        var entered = Enumerable.Range(0, 7).Select(_ => new ManualResetEventSlim()).ToArray();
        var release = Enumerable.Range(0, 7).Select(_ => new ManualResetEventSlim()).ToArray();
        var tokens = new CancellationToken[7];
        var calls = 0;
        var operations = new List<Task>();
        var library = new CharacterLibrary(MainWindow.CharactersRoot, Path.Combine(root, "switch-ui-custom"));
        var priorLanguage = UiText.Language;
        var pet = new MainWindow(library, inboxDirectory: Path.Combine(root, "switch-ui-inbox"), characterLoader: (folder, token) =>
        {
            var call = Interlocked.Increment(ref calls);
            tokens[call] = token; entered[call].Set();
            if (call is 1 or 3 or 6 && !release[call].Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("UI selection was never released");
            return CharacterPresentation.Load(folder); // Deliberately finish after a cancelled request.
        });
        pet.Loaded -= typeof(MainWindow).GetMethod("MainWindow_Loaded", BindingFlags.Instance | BindingFlags.NonPublic)!.CreateDelegate<RoutedEventHandler>(pet);
        Set(pet, "_settings", new PetSettings { CharacterName="Companion", QuietMode=true });
        Set(pet, "_brain", new PetBrain(new PlatformLayout(new[] { new Platform(0, 1920, 1080) }), 128, 122, 500, 958));
        Invoke(pet, "ApplyPresentation", CharacterPresentation.Load(Path.Combine(MainWindow.CharactersRoot, "Companion")));
        var timer = (DispatcherTimer)Get(pet, "_settingsSaveTimer")!;
        timer.Interval = TimeSpan.FromDays(1);
        var settings = new SettingsWindow(pet);
        try
        {
            settings.Show();
            Check(ReferenceEquals(pet.CurrentPresentation, Get(settings, "_previewPresentation")), "Opening Settings decoded the active character again");
            var combo = (ComboBox)settings.FindName("CharacterCombo");
            combo.SelectedItem = "Nimbus";
            var old = settings.CharacterSelectionOperation;
            operations.Add(old); Until(() => entered[1].IsSet);
            Check(pet.CurrentCharacterName == "Companion" && !old.IsCompleted, "Settings applied a partially prepared character");
            foreach (var language in new[] { "en", "zh-CN", "ko" })
            {
                UiText.SetLanguage(language);
                Check(combo.SelectedItem is "Nimbus" && ReferenceEquals(pet.CurrentPresentation, Get(settings, "_previewPresentation")) &&
                    ((TextBlock)settings.FindName("CharacterHint")).Text == UiText.Get("캐릭터를 불러오고 있어요…") && calls == 1,
                    "Language change lost a pending selection/loading hint or started another decode");
            }
            combo.SelectedItem = "Ember";
            operations.Add(settings.CharacterSelectionOperation);
            Wait(settings.CharacterSelectionOperation);
            release[1].Set(); Wait(old);
            Check(tokens[1].IsCancellationRequested && pet.CurrentCharacterName == "Ember" && combo.SelectedItem is "Ember" &&
                ReferenceEquals(pet.CurrentPresentation, Get(settings, "_previewPresentation")) && calls == 2,
                "Rapid Settings selections left a stale character/preview or decoded the presentation twice");
            var idle = pet.CurrentPresentation.Frames.Keys.First();
            using (File.Open(idle, FileMode.Open, FileAccess.Read, FileShare.None))
                Invoke(settings, "UpdatePreview");
            Check(ReferenceEquals(pet.CurrentPresentation, Get(settings, "_previewPresentation")), "Settings preview reopened a cached image file");
            Console.WriteLine("PASS Rapid Settings selections use the newest frozen presentation once and reuse it while its PNG is exclusively locked");

            Invoke(settings, "Gallery_Click", settings, new RoutedEventArgs());
            var gallery = (CharacterGalleryWindow)Get(settings, "_characterGallery")!;
            Wait(gallery.LoadingTask);
            ((ListBox)gallery.FindName("CharacterCards")).SelectedItem = gallery.AllCharacters.Single(item => item.Name == "Nimbus");
            var applied = 0;
            gallery.CharacterApplied += _ => applied++;
            ((Button)gallery.FindName("ApplyButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var closingApply = gallery.ApplyOperation;
            operations.Add(closingApply); Until(() => entered[3].IsSet);
            Check(!((Button)gallery.FindName("ApplyButton")).IsEnabled && pet.CurrentCharacterName == "Ember", "Gallery did not keep a pending apply separate from active state");
            gallery.Close(); release[3].Set(); Wait(closingApply);
            Check(tokens[3].IsCancellationRequested && applied == 0 && pet.CurrentCharacterName == "Ember", "Closed gallery applied a late decoder result");

            Invoke(settings, "Gallery_Click", settings, new RoutedEventArgs());
            gallery = (CharacterGalleryWindow)Get(settings, "_characterGallery")!;
            Wait(gallery.LoadingTask);
            ((ListBox)gallery.FindName("CharacterCards")).SelectedItem = gallery.AllCharacters.Single(item => item.Name == "Nimbus");
            ((Button)gallery.FindName("ApplyButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            operations.Add(gallery.ApplyOperation); Wait(gallery.ApplyOperation);
            Check(pet.CurrentCharacterName == "Nimbus" && combo.SelectedItem is "Nimbus" &&
                ReferenceEquals(pet.CurrentPresentation, Get(settings, "_previewPresentation")) && calls == 4,
                "Gallery apply failed to synchronize the parent Settings without another decode");
            Console.WriteLine("PASS Gallery apply stays asynchronous, closing cancels its decoder, and a successful apply shares the same Settings presentation");

            var pendingImport = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var deferred = (Task)typeof(SettingsWindow).GetMethod("RunCharacterImportAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(settings, new object[] { new Func<Task<string?>>(() => pendingImport.Task) })!;
            operations.Add(deferred);
            ((ListBox)gallery.FindName("CharacterCards")).SelectedItem = gallery.AllCharacters.Single(item => item.Name == "Ember");
            ((Button)gallery.FindName("ApplyButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            operations.Add(gallery.ApplyOperation); Wait(gallery.ApplyOperation);
            pendingImport.SetResult("Nimbus"); Wait(deferred);
            Check(pet.CurrentCharacterName == "Ember" && combo.SelectedItem is "Ember" && calls == 5 &&
                ((TextBlock)settings.FindName("CharacterImportStatusText")).Text == UiText.Get("설치는 이미 완료됐어요. 현재 친구는 바꾸지 않았습니다."),
                "An import finishing after a newer explicit gallery choice overrode the selected character");
            Console.WriteLine("PASS Completing an older import preserves a newer explicit gallery selection and reports installation without activation");

            var source = Path.Combine(root, "switch-import-source", "Switch Imported");
            Directory.CreateDirectory(source);
            File.Copy(Path.Combine(MainWindow.CharactersRoot, "Companion", "idle.png"), Path.Combine(source, "idle.png"));
            File.WriteAllText(Path.Combine(source, "character.json"), JsonSerializer.Serialize(new
            {
                name="Switch Imported", frameSizePx=new { width=128, height=128 }, anchor=new { x=64, y=122 }, frames=new { idle=new[] { "idle.png" } }
            }));
            var import = (Task)typeof(SettingsWindow).GetMethod("ImportCharacterFolderAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(settings, new object[] { source })!;
            operations.Add(import); Until(() => entered[6].IsSet);
            Check(library.GetNames().Contains("Switch Imported") && pet.CurrentCharacterName == "Ember" &&
                !((Button)settings.FindName("AddCharacterButton")).IsEnabled, "Import did not preserve active state while preparing installed images");
            settings.Close(); release[6].Set(); Wait(import);
            Check(tokens[6].IsCancellationRequested && pet.CurrentCharacterName == "Ember" && calls == 6,
                "Closing Settings after installation allowed its pending character activation");
            Console.WriteLine("PASS Post-import activation decodes asynchronously; closing Settings preserves the committed install and cancels late activation");
        }
        finally
        {
            foreach (var gate in release) gate.Set();
            settings.Close();
            foreach (var operation in operations) { try { Wait(operation); } catch { } }
            timer.Stop(); Set(pet, "_settingsDirty", false); pet.DisposeResources(); pet.Close();
            foreach (var gate in entered.Concat(release)) gate.Dispose();
            UiText.SetLanguage(priorLanguage);
        }
    }

    private static void Until(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (!condition() && watch.Elapsed < TimeSpan.FromSeconds(15)) { Pump(); Thread.Sleep(1); }
        Check(condition(), "Character switch operation timed out");
    }
    private static void Wait(Task task) { Until(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue=false));
        Dispatcher.PushFrame(frame);
    }
    private static object? Get(object o, string name) => o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(o);
    private static void Set(object o, string name, object? value) => o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(o,value);
    private static void Invoke(object o, string name, params object[] args) => o.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(o,args);
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
