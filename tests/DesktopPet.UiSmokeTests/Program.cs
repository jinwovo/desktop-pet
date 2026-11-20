using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DesktopPet.App;
using DesktopPet.Core;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var folder = Path.Combine(AppContext.BaseDirectory, "smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var brokenName = "SmokeBroken-" + Guid.NewGuid().ToString("N");
        var brokenFolder = Path.Combine(MainWindow.CharactersRoot, brokenName);
        MainWindow? pet = null;
        try
        {
            if (args.Contains("--rig-only"))
            {
                GroundedRigVisualRegressionTests.Run(MainWindow.CharactersRoot, capture: args.Contains("--capture"));
                return 0;
            }
            if (args.Contains("--bubble-only"))
            {
                SpeechBubbleLocalizationTests.Run();
                SpeechBubbleMotionTests.Run(folder);
                Console.WriteLine("All speech bubble checks passed");
                return 0;
            }
            var archiveIndex = Array.IndexOf(args, "--verify-archive");
            if (archiveIndex >= 0)
            {
                Check(archiveIndex + 1 < args.Length, "Missing character ZIP path");
                ArchiveImportUiTests.VerifyPack(args[archiveIndex + 1], folder);
                return 0;
            }
            if (args.Contains("--zip-only"))
            {
                ArchiveImportUiTests.Run(folder);
                return 0;
            }
            if (args.Contains("--fullscreen-only"))
            {
                FullscreenUiTests.Run(folder);
                return 0;
            }
            if (args.Contains("--gallery-capture"))
            {
                CharacterGalleryRegressionTests.CaptureInstalledGallery(folder);
                return 0;
            }
            if (args.Contains("--gallery-only"))
            {
                CharacterGalleryRegressionTests.Run(folder, args.Contains("--capture"));
                Console.WriteLine("Character gallery UI regression checks passed");
                return 0;
            }
            Directory.CreateDirectory(brokenFolder);
            WindowCaptureRegressionTests.Run();
            pet = new MainWindow(new CharacterLibrary(MainWindow.CharactersRoot, Path.Combine(folder, "custom-characters")));
            Check(!pet.ShowActivated, "Pet can activate while showing");
            var activePresentation = CharacterPresentation.Load(Path.Combine(MainWindow.CharactersRoot, "Companion"));
            var character = activePresentation.Definition;
            Set(pet, "_settings", new PetSettings());
            Set(pet, "_brain", new PetBrain(new PlatformLayout(new[] { new Platform(0, 1920, 1080) }), 128, 122, 500, 958));
            Invoke(pet, "ApplyPresentation", activePresentation);
            var settings = new SettingsWindow(pet);
            Check(((ComboBox)settings.FindName("CharacterCombo")).SelectedItem is "Companion", "Character selection failed");
            Check(!(bool)Get(pet, "_settingsDirty")!, "Constructing settings changed preferences");
            settings.Show();
            var previewTimer = (DispatcherTimer)Get(settings, "_previewTimer")!;
            Check(previewTimer.IsEnabled, "Preview animation did not start");
            var previewState = (ComboBox)settings.FindName("PreviewStateCombo");
            foreach (var state in new[] { PetState.Walk, PetState.Cry })
            {
                previewState.SelectedValue = state;
                Invoke(settings, "RenderPreview");
                var presentation = (CharacterPresentation)Get(settings, "_previewPresentation")!;
                if (state == PetState.Walk && presentation.Definition.WalkRig is not null)
                {
                    var rig = (GroundedRigVisual)settings.FindName("PreviewRig");
                    Check(rig.HasRig && rig.Visibility == Visibility.Visible && rig.PoseUpdateCount > 0 &&
                        ((Image)settings.FindName("PreviewImage")).Visibility == Visibility.Hidden,
                        "Walking preview failed to use its cached live rig");
                }
                else
                {
                    var actualFrame = ((Image)settings.FindName("PreviewImage")).Source;
                    var candidates = state == PetState.Walk
                        ? Enum.GetValues<WalkView>().SelectMany(presentation.Definition.GetWalkFrames)
                        : presentation.Definition.GetFrames(state);
                    Check(candidates.Any(path => ReferenceEquals(presentation.Frames[path], actualFrame)), "Preview selected wrong state frames");
                }
            }
            Check(!(bool)Get(pet, "_settingsDirty")!, "Preview changed live pet preferences");
            previewState.SelectedValue = PetState.Walk;
            Thread.Sleep(120);
            Invoke(settings, "RenderPreview");
            var phaseBefore = (double)Get(settings, "_previewAnimationMs")!;
            var testPreferences = (PetSettings)Get(pet, "_settings")!;
            testPreferences.WalkSpeedMultiplier = 3;
            Invoke(settings, "RenderPreview");
            Check((double)Get(settings, "_previewAnimationMs")! - phaseBefore < 100, "Speed change retroactively rescaled animation phase");
            testPreferences.WalkSpeedMultiplier = 1;
            Console.WriteLine("PASS Speed changes preserve settings preview animation phase");
            var previewAppearance = (WalkAppearanceController)Get(settings, "_walkAppearance")!;
            previewAppearance.Reset();
            for (var i = 0; i < 200; i++)
            {
                Set(settings, "_previewLastElapsedMs", ((System.Diagnostics.Stopwatch)Get(settings, "_previewClock")!).Elapsed.TotalMilliseconds - 20);
                Invoke(settings, "RenderPreview");
                Check(previewAppearance.View != WalkView.Front, "Settings preview still faces front during the walking demonstration");
            }
            Console.WriteLine("PASS Settings walking preview never reintroduces frontal walking artwork");
            if (args.Contains("--capture")) SaveWindowSnapshot(settings, "settings-v4.png");
            settings.Close();
            Check(!previewTimer.IsEnabled, "Closed settings retained animation timer");
            Console.WriteLine("PASS Settings window initializes and opens without changing preferences");
            Console.WriteLine("PASS Preview plays selected states without changing pet and stops on close");

            foreach (var name in pet.AvailableCharacterNames.Where(n => n != brokenName))
                Check(CharacterPresentation.Load(Path.Combine(MainWindow.CharactersRoot, name)).Frames.Count > 0, "Bundled presentation failed");
            Console.WriteLine("PASS All bundled characters decode with their declared frame dimensions");
            GroundedSpriteRegressionTests.Run(MainWindow.CharactersRoot);
            var baselineIndex = Array.IndexOf(args, "--baseline-characters");
            Check(baselineIndex < 0 || baselineIndex + 1 < args.Length, "Missing --baseline-characters directory");
            GroundedRigVisualRegressionTests.Run(MainWindow.CharactersRoot, baselineIndex < 0 ? null : args[baselineIndex + 1], args.Contains("--capture"));
            VerifyPoseTransitions(character);

            var motion = new MotionPreviewWindow(CharacterPresentation.Load(Path.Combine(MainWindow.CharactersRoot, "Companion")), 1);
            motion.Show();
            Check(motion.IsAnimating, "Motion preview did not start");
            motion.SetSpeedMultiplier(2);
            Check(((PetBrain)Get(motion, "_brain")!).WalkSpeedPxPerSec == character.WalkSpeedPxPerSec * 2, "Open motion preview ignored speed change");
            VerifyMotionWalking(motion);
            if (args.Contains("--capture")) SaveWindowSnapshot(motion, "motion-preview-v4.png");
            Invoke(motion, "JumpButton_Click", null!, new RoutedEventArgs());
            Check(((PetBrain)Get(motion, "_brain")!).State == PetState.Falling, "Preview jump failed");
            Invoke(motion, "DropButton_Click", null!, new RoutedEventArgs());
            Check(((PetBrain)Get(motion, "_brain")!).State == PetState.Falling, "Preview drop failed");
            Invoke(motion, "ResetButton_Click", null!, new RoutedEventArgs());
            Check(((PetBrain)Get(motion, "_brain")!).State == PetState.Idle, "Preview reset failed");
            motion.Close();
            Check(!motion.IsAnimating && !(bool)Get(pet, "_settingsDirty")!, "Preview retained loop or changed preferences");
            Console.WriteLine("PASS Isolated motion preview jumps, drops, resets and stops without changing live pet");

            var overlay = new DesktopPet.WindowFall.FallOverlayWindow();
            var overlayHandle = new System.Windows.Interop.WindowInteropHelper(overlay).Handle;
            Check(!overlay.ShowActivated && (DesktopPet.Interop.NativeMethods.GetWindowLongPtr(overlayHandle,
                DesktopPet.Interop.NativeMethods.GWL_EXSTYLE) & DesktopPet.Interop.NativeMethods.WS_EX_NOACTIVATE) != 0,
                "Window-fall overlay can take keyboard focus");
            overlay.Shutdown();
            Console.WriteLine("PASS Window-fall overlay preserves keyboard focus");

            var activeCharacter = Get(pet, "_character");
            File.WriteAllText(Path.Combine(brokenFolder, "character.json"), "{");
            ExpectInvalid(() => pet.SwitchCharacter(brokenName));
            Check(ReferenceEquals(Get(pet, "_character"), activeCharacter), "Invalid metadata replaced active character");
            var preferences = (PetSettings)Get(pet, "_settings")!;
            preferences.CharacterName = brokenName;
            var fallback = (CharacterPresentation)Invoke(pet, "LoadInitialPresentation", new List<string> { brokenName, "Companion" })!;
            Check(fallback.Definition.Name == "Companion", "Invalid selected character blocked startup fallback");
            preferences.CharacterName = "Companion";
            Console.WriteLine("PASS Invalid selected character falls back at startup and leaves active character unchanged on switch");

            var manifest = new
            {
                name = brokenName, frameSizePx = new { width = 128, height = 128 },
                anchor = new { x = 64, y = 122 }, frames = new { idle = new[] { "idle.png" }, walk = new[] { "IDLE.PNG" } },
            };
            File.WriteAllText(Path.Combine(brokenFolder, "character.json"), JsonSerializer.Serialize(manifest));
            File.WriteAllText(Path.Combine(brokenFolder, "idle.png"), "invalid image bytes");
            ExpectInvalid(() => pet.SwitchCharacter(brokenName));
            Check(ReferenceEquals(Get(pet, "_character"), activeCharacter), "Corrupt image replaced active character");
            Console.WriteLine("PASS Undecodable image is rejected before changing active presentation");
            File.Copy(character.GetFrames(PetState.Idle)[0], Path.Combine(brokenFolder, "idle.png"), overwrite: true);
            Check(CharacterPresentation.Load(brokenFolder).Frames.Count == 1, "Windows path case alias was decoded twice");
            Console.WriteLine("PASS Frame path case aliases share one decoded image");
            VerifyImageHeaderGuard(brokenFolder, character.GetFrames(PetState.Idle)[0]);

            var cursor = System.Windows.Forms.Cursor.Position;
            var lookBrain = new PetBrain(new PlatformLayout(new[] { new Platform(-10000, 10000, cursor.Y + 72) }),
                128, 122, cursor.X + 40, cursor.Y - 50);
            lookBrain.Tick(0.01, new PetPoint(cursor.X, cursor.Y));
            Set(pet, "_brain", lookBrain);
            Invoke(pet, "CompositionTarget_Rendering", null!, EventArgs.Empty);
            Check(lookBrain.State == PetState.LookAtCursor, "Left-looking state not entered");
            Check(((System.Windows.Media.ScaleTransform)pet.FindName("FlipTransform")).ScaleX == 1, "Directional look image mirrored twice");
            Check(((string)Get(pet, "_lastFramePath")!).EndsWith("look_left.png", StringComparison.OrdinalIgnoreCase), "Wrong look frame");
            Console.WriteLine("PASS Left-looking artwork stays unmirrored in the render handler");

            VerifyDesktopWalking(pet, character, cursor.X, cursor.Y);
            if (args.Contains("--capture")) WalkPreviewCapture.Save(MainWindow.CharactersRoot, AppContext.BaseDirectory);

            var idleBrain = new PetBrain(new PlatformLayout(new[] { new Platform(-10000, 10000, cursor.Y + 1122) }),
                128, 122, cursor.X + 1000, cursor.Y + 1000) { CalmBehavior = true };
            Set(pet, "_brain", idleBrain);
            ((WalkAppearanceController)Get(pet, "_walkAppearance")!).Reset();
            Set(pet, "_lastGaitPositionX", idleBrain.X);
            Set(pet, "_idleAppearanceElapsedMs", 4190d);
            Set(pet, "_lastTickTimestamp", System.Diagnostics.Stopwatch.GetTimestamp() - System.Diagnostics.Stopwatch.Frequency / 50);
            Invoke(pet, "CompositionTarget_Rendering", null!, EventArgs.Empty);
            Check(idleBrain.State == PetState.Idle && ((string)Get(pet, "_lastFramePath")!).EndsWith("blink.png"),
                "Sparse blinking was reset by the behavior decision clock");
            Console.WriteLine("PASS Public mascot actually blinks using its independent idle appearance clock");

            var bubble = new SpeechBubbleWindow();
            var bubbleHandle = new System.Windows.Interop.WindowInteropHelper(bubble).Handle;
            Check((DesktopPet.Interop.NativeMethods.GetWindowLongPtr(bubbleHandle, DesktopPet.Interop.NativeMethods.GWL_EXSTYLE) &
                   DesktopPet.Interop.NativeMethods.WS_EX_NOACTIVATE) != 0, "Bubble can take keyboard focus");
            Console.WriteLine("PASS Pet and bubble preserve the working application's keyboard focus");
            Set(pet, "_bubble", bubble);
            bubble.Acknowledged += () => Invoke(pet, "AcknowledgeNotification");
            preferences.AutoDismissNotifications = false;
            File.WriteAllText(Path.Combine(folder, "001.json"), JsonSerializer.Serialize(new PetNotification { Title = "첫 번째", Message = "first" }));
            File.WriteAllText(Path.Combine(folder, "002.json"), JsonSerializer.Serialize(new PetNotification { Title = "두 번째", Message = "second" }));
            var deliveries = new ConcurrentQueue<InboxNotification>();
            using var inbox = new NotificationInbox(folder);
            inbox.NotificationReceived += deliveries.Enqueue;
            inbox.Start();
            Check(SpinWait.SpinUntil(() => deliveries.Count == 2, TimeSpan.FromSeconds(5)), "Inbox timeout");
            foreach (var delivery in deliveries) Invoke(pet, "OnNotification", delivery);
            Check(((TextBlock)bubble.FindName("TitleText")).Text == "첫 번째", "Second notification overwrote first");
            Check(((TextBlock)bubble.FindName("AcknowledgementText")).Text.Contains("1건"), "Queue count missing");
            Invoke(bubble, "Acknowledge");
            Check(((TextBlock)bubble.FindName("TitleText")).Text == "두 번째", "Queue did not advance");
            Check(!File.Exists(Path.Combine(folder, "001.json")) && File.Exists(Path.Combine(folder, "002.json")), "Wrong file acknowledged");
            Invoke(bubble, "Acknowledge");
            Check(!bubble.IsShowing && Get(pet, "_activeNotification") is null, "Final acknowledgement left active UI");
            Check(Directory.GetFiles(folder, "*.json").Length == 0, "Acknowledged files remain");
            Console.WriteLine("PASS Bubble clicks advance the queue and acknowledge exactly one file");
            deliveries.Clear();
            preferences.AutoDismissNotifications = true;
            InboxNotification Deliver(string name, PetNotification notification)
            {
                File.WriteAllText(Path.Combine(folder, name + ".json"), JsonSerializer.Serialize(notification));
                Check(SpinWait.SpinUntil(() => !deliveries.IsEmpty, TimeSpan.FromSeconds(5)), "Notification delivery timeout");
                Check(deliveries.TryDequeue(out var delivery), "Notification dequeue failed");
                Invoke(pet, "OnNotification", delivery!);
                return delivery!;
            }
            Deliver("003", new PetNotification { Title = "Claude Code", Message = "첫 작업 완료", Source = "claude", EventId = "a" });
            Deliver("004", new PetNotification { Title = "Codex", Message = "다음 작업 완료", Source = "codex", EventId = "b" });
            Check(((TextBlock)bubble.FindName("TitleText")).Text == "Codex" && pet.PendingNotificationCount == 2,
                "Default burst did not show latest completion while retaining unread receipts");
            Check(((DispatcherTimer)Get(pet, "_notificationPopupTimer")!).IsEnabled && !idleBrain.IsNagging,
                "Calm completion did not schedule dismissal or started nagging");
            if (args.Contains("--capture")) SaveWindowSnapshot(bubble, "completion-v4.png");
            Invoke(pet, "OnNotificationPopupElapsed");
            Check(!bubble.IsShowing && pet.PendingNotificationCount == 2 && File.Exists(Path.Combine(folder, "003.json")),
                "Auto-hide incorrectly acknowledged unread messages");
            pet.ShowUnreadNotifications();
            Check(((TextBlock)bubble.FindName("TitleText")).Text == "Claude Code", "Unread review lost arrival order");
            Invoke(bubble, "Acknowledge");
            Invoke(bubble, "Acknowledge");
            Console.WriteLine("PASS Calm burst displays latest completion, auto-hides and preserves FIFO unread review");

            preferences.Visible = false;
            Deliver("005", new PetNotification { Title = "Hidden", EventId = "hidden" });
            Check(!preferences.Visible && !bubble.IsShowing && pet.PendingNotificationCount == 1, "Completion revealed hidden pet");
            preferences.Visible = true;
            preferences.QuietMode = true;
            Deliver("006", new PetNotification { Title = "Quiet", EventId = "quiet" });
            Check(!bubble.IsShowing && pet.PendingNotificationCount == 2, "Quiet mode showed an automatic popup");
            pet.ShowUnreadNotifications();
            Check(bubble.IsShowing, "Quiet mode blocked explicit unread review");
            Invoke(bubble, "Acknowledge");
            Invoke(bubble, "Acknowledge");
            preferences.QuietMode = false;
            Console.WriteLine("PASS Hidden and quiet preferences suppress automatic popups and preserve explicit review");

            Deliver("quiet-before", new PetNotification { Title = "Before quiet" });
            pet.ShowUnreadNotifications();
            pet.SetQuietMode(true);
            pet.SetQuietMode(false);
            // This fixture must never persist into the actual user's preferences while pumping the dispatcher.
            ((DispatcherTimer)Get(pet, "_settingsSaveTimer")!).Stop();
            Set(pet, "_settingsDirty", false);
            Deliver("quiet-after", new PetNotification { Title = "After quiet" });
            Check(bubble.IsShowing && ((TextBlock)bubble.FindName("TitleText")).Text == "After quiet",
                "Ending unread review with quiet mode blocked later automatic completions");
            Invoke(bubble, "Acknowledge");
            Invoke(bubble, "Acknowledge");
            Console.WriteLine("PASS Automatic completion popups resume after quiet mode ends a manual unread review");

            Deliver("007", new PetNotification { Title = "Unique", Source = "codex", EventId = "same" });
            // Capture receipt before locking its file, then simulate a second delivery of the same event.
            File.WriteAllText(Path.Combine(folder, "008.json"), JsonSerializer.Serialize(new PetNotification { Title = "Duplicate", Source = "codex", EventId = "same" }));
            Check(SpinWait.SpinUntil(() => !deliveries.IsEmpty, TimeSpan.FromSeconds(5)), "Duplicate delivery timeout");
            Check(deliveries.TryDequeue(out var duplicate), "Duplicate dequeue failed");
            using (var locked = new FileStream(Path.Combine(folder, "008.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Invoke(pet, "OnNotification", duplicate!);
                Check(pet.PendingNotificationCount == 1 && ((TextBlock)bubble.FindName("TitleText")).Text == "Unique",
                    "Locked duplicate entered visible queue");
            }
            Invoke(pet, "RetryDuplicateCleanup");
            Check(!File.Exists(Path.Combine(folder, "008.json")), "Duplicate receipt cleanup was not retried");
            Invoke(bubble, "Acknowledge");
            Console.WriteLine("PASS Duplicate notification stays suppressed while locked and its receipt is cleaned after unlock");

            VerifyConnections(folder, pet, args.Contains("--capture"));
            VerifyCharacterImport(pet, folder, args.Contains("--capture"));
            FocusTimerUiTests.Run(folder);
            QuietHoursUiTests.Run(folder);
            RenderOptionsUiTests.Run(folder);
            PreferencesBackupUiTests.Run(folder);
            LocalizationUiTests.Run(folder);
            EvolutionMotionUiTests.Run(folder);
            SpeechBubbleLocalizationTests.Run();
            SpeechBubbleMotionTests.Run(folder);
            GalleryLocaleRegressionTests.Run(folder);
            DiagnosticsUiTests.Run(folder);
            PetRecoveryUiTests.Run(folder);
            ImportCancellationUiTests.Run(folder);
            InstallationCheckUiTests.Run(folder);
            FullscreenUiTests.Run(folder);
            ArchiveImportUiTests.Run(folder);
            PlatformMonitorUiTests.Run(pet);
            CharacterGalleryRegressionTests.Run(folder, args.Contains("--capture"));
            if (args.Contains("--personal-pack")) VerifyPersonalSelection(pet);
            Console.WriteLine("All UI smoke checks passed");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            if (pet is not null)
            {
                ((DispatcherTimer)Get(pet, "_settingsSaveTimer")!).Stop();
                Set(pet, "_settingsDirty", false);
            }
            pet?.DisposeResources();
            app.Shutdown();
            var root = Path.GetFullPath(AppContext.BaseDirectory);
            var target = Path.GetFullPath(folder);
            if (target.StartsWith(root, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(target).StartsWith("smoke-"))
                Directory.Delete(target, recursive: true);
            var characterRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(MainWindow.CharactersRoot)) + Path.DirectorySeparatorChar;
            var characterTarget = Path.GetFullPath(brokenFolder);
            if (characterTarget.StartsWith(characterRoot, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(characterTarget).StartsWith("SmokeBroken-") && Directory.Exists(characterTarget))
                Directory.Delete(characterTarget, recursive: true);
        }
    }

    private static object? Get(object obj, string name) => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(obj);
    private static void Set(object obj, string name, object value) => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(obj, value);
    private static object? Invoke(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(obj, args);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void ExpectInvalid(Action action)
    {
        try { action(); } catch (InvalidDataException) { return; }
        throw new Exception("Invalid character was accepted");
    }

    private static void WaitForOperation(AssistantConnectionsWindow window)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!((Task)Get(window, "_operation")!).IsCompleted)
        {
            Check(DateTime.UtcNow < deadline, "Connection window operation timed out");
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(5);
        }
        ((Task)Get(window, "_operation")!).GetAwaiter().GetResult();
    }

    private static void VerifyMotionWalking(MotionPreviewWindow window)
    {
        var presentation = (CharacterPresentation)Get(window, "_presentation")!;
        var scale = (double)Get(window, "_scale")!;
        var ground = ((Canvas)window.FindName("Scene")).ActualHeight - 12;
        var brain = new PetBrain(new PlatformLayout(new[] { new Platform(-5000, 5000, ground) }),
            presentation.Definition.FrameWidth * scale, presentation.Definition.AnchorY * scale,
            400, ground - presentation.Definition.AnchorY * scale)
        { WalkSpeedPxPerSec = presentation.Definition.WalkSpeedPxPerSec * 2, SleepEnabled = false };
        Set(window, "_brain", brain);
        Set(window, "_lastGaitPositionX", brain.X);
        Invoke(brain, "ChangeState", PetState.Walk);
        Set(brain, "_nextStateDecisionAtMs", 100000d);
        Set(brain, "_walkTargetDirection", -1d);
        typeof(PetBrain).GetProperty("FacingRight")!.SetValue(brain, false);
        Set(brain, "_walkVelocityX", -brain.WalkSpeedPxPerSec);
        var appearance = (WalkAppearanceController)Get(window, "_walkAppearance")!;
        appearance.Reset(false);
        var views = new HashSet<WalkView>();
        for (var i = 0; i < 200; i++)
        {
            var previousX = brain.X;
            var previousPhase = appearance.GaitPhase;
            brain.Tick(0.02, new PetPoint(-10000, -10000));
            Invoke(window, "RenderActor", 0.02);
            var expectedPhase = GroundedGait.NormalizePhase(previousPhase + Math.Abs(brain.X - previousX) / scale / presentation.Definition.WalkCycleDistancePx!.Value);
            Check(Math.Abs(expectedPhase - appearance.GaitPhase) < 1e-8,
                "Motion preview gait did not advance by measured movement divided by sprite scale and stride");
            views.Add(appearance.View);
            Check(!((PoseTransitionPresenter)Get(window, "_poseTransition")!).IsTransitioning,
                "A moving grounded foot was duplicated into a sliding crossfade layer");
            var expectedFlip = appearance.View == WalkView.Front ? 1 : -1;
            Check(((System.Windows.Media.ScaleTransform)window.FindName("ActorFlip")).ScaleX == expectedFlip,
                "Motion preview mirrored front view or showed backward-facing profile");
            if (presentation.Definition.WalkRig is not null)
            {
                var live = (GroundedRigVisual)window.FindName("MotionRig");
                Check(live.HasRig && live.Visibility == Visibility.Visible && live.PoseUpdateCount > 0 &&
                    ((Image)window.FindName("Actor")).Visibility == Visibility.Hidden,
                    "Motion preview selected a raster fallback instead of its cached live rig");
            }
            else Check(presentation.Definition.GetWalkFrames(appearance.View).Any(path =>
                ReferenceEquals(presentation.Frames[path], ((Image)window.FindName("Actor")).Source)),
                "Motion preview selected artwork from a different walk view");
        }
        Check(views.SetEquals(new[] { WalkView.Side, WalkView.Turn }), "Motion preview showed a frontal walk or omitted a moving view");
        var stationaryPhase = appearance.GaitPhase;
        Invoke(window, "RenderActor", 0.02);
        Check(appearance.GaitPhase == stationaryPhase, "Motion preview cycles the feet when position has not moved");
        Invoke(brain, "ChangeState", PetState.Idle);
        Invoke(window, "RenderActor", 0d);
        Check(appearance.IsSettling && ((System.Windows.Media.ScaleTransform)window.FindName("ActorFlip")).ScaleX == -1,
            "Motion preview snapped frontal before the raised foot settled");
        for (var i = 0; i < 10; i++) Invoke(window, "RenderActor", 0.02);
        Check(((System.Windows.Media.ScaleTransform)window.FindName("ActorFlip")).ScaleX == 1,
            "Motion preview flipped the frontal resting artwork after walking left");
        Console.WriteLine("PASS Motion preview uses only side and quarter views while moving, then frontal rest");
    }

    private static void VerifyDesktopWalking(MainWindow pet, CharacterDefinition character, int cursorX, int cursorY)
    {
        var brain = new PetBrain(new PlatformLayout(new[] { new Platform(-20000, 20000, cursorY + 1122) }),
            128, 122, cursorX + 1000, cursorY + 1000) { WalkSpeedPxPerSec = character.WalkSpeedPxPerSec, SleepEnabled = false };
        Set(pet, "_brain", brain);
        Set(pet, "_lastGaitPositionX", brain.X);
        Invoke(brain, "ChangeState", PetState.Walk);
        Set(brain, "_nextStateDecisionAtMs", 100000d);
        var appearance = (WalkAppearanceController)Get(pet, "_walkAppearance")!;
        var cachedTextureCount = ((System.Collections.IDictionary)Get(pet, "_frameCache")!).Count;
        foreach (var right in new[] { true, false })
        {
            appearance.Reset(right);
            Set(brain, "_walkTargetDirection", right ? 1d : -1d);
            Set(brain, "_walkVelocityX", (right ? 1 : -1) * character.WalkSpeedPxPerSec);
            var views = new HashSet<WalkView>();
            for (var i = 0; i < 200; i++)
            {
                var previousX = brain.X;
                var previousPhase = appearance.GaitPhase;
                Set(pet, "_lastTickTimestamp", System.Diagnostics.Stopwatch.GetTimestamp() - System.Diagnostics.Stopwatch.Frequency / 50);
                Invoke(pet, "CompositionTarget_Rendering", null!, EventArgs.Empty);
                var expectedPhase = GroundedGait.NormalizePhase(previousPhase + Math.Abs(brain.X - previousX) / (pet.Width / character.FrameWidth) / character.WalkCycleDistancePx!.Value);
                Check(Math.Abs(expectedPhase - appearance.GaitPhase) < 1e-8,
                    "Desktop gait still uses an elapsed-time or endpoint-velocity estimate instead of measured displacement");
                Check((string)Get(pet, "_lastFramePath")! == character.GetWalkFramePath(appearance.View, expectedPhase),
                    "Desktop renderer sampled the wrong distance-gait frame");
                views.Add(appearance.View);
                Check(!((PoseTransitionPresenter)Get(pet, "_poseTransition")!).IsTransitioning,
                    "Desktop grounded stance has a moving outgoing ghost layer");
                Check(appearance.View != WalkView.Front, "Desktop pet faces front while sliding sideways");
                Check(brain.State == PetState.Walk, "Walking fixture unexpectedly left its wide supported platform");
                var placement = SpritePixelPlacement.Calculate(brain.X, brain.Y + brain.NagHopOffset, (double)Get(pet, "_dpiScale")!);
                var residual = (System.Windows.Media.TranslateTransform)pet.FindName("ResidualOffset");
                Check(Math.Abs(residual.X - placement.OffsetX) < 1e-10 && Math.Abs(residual.Y - placement.OffsetY) < 1e-10 &&
                    (int)Get(pet, "_lastNativeX")! == placement.PixelX && (int)Get(pet, "_lastNativeY")! == placement.PixelY,
                    "Desktop renderer dropped the subpixel offset or disagreed with its cached native placement plan");
                Check(character.GetWalkFrames(appearance.View).Contains((string)Get(pet, "_lastFramePath")!),
                    "Desktop renderer did not select the current directional walk artwork");
                if (character.WalkRig is not null)
                {
                    var live = (GroundedRigVisual)pet.FindName("PetRig");
                    Check(live.HasRig && live.Visibility == Visibility.Visible && live.PoseUpdateCount > 0 &&
                        ((Image)pet.FindName("PetImage")).Visibility == Visibility.Hidden,
                        "Desktop renderer selected raster frames instead of the live rig");
                    Check(((System.Collections.IDictionary)Get(pet, "_frameCache")!).Count == cachedTextureCount,
                        "Desktop live walking lazily decoded an unused raster gait texture");
                }
                var expectedFlip = right || appearance.View == WalkView.Front ? 1 : -1;
                Check(((System.Windows.Media.ScaleTransform)pet.FindName("FlipTransform")).ScaleX == expectedFlip,
                    "Desktop walk faces opposite the actual direction, or its frontal pose is mirrored");
            }
            Check(views.SetEquals(new[] { WalkView.Side, WalkView.Turn }), "Desktop walk omitted side or quarter poses");
        }
        Console.WriteLine("PASS Actual desktop rendering never faces front during lateral walking in either direction");

        appearance.Reset(true);
        Set(brain, "_walkTargetDirection", 1d);
        Set(brain, "_walkVelocityX", character.WalkSpeedPxPerSec);
        for (var i = 0; i < 20; i++)
        {
            Set(pet, "_lastTickTimestamp", System.Diagnostics.Stopwatch.GetTimestamp() - System.Diagnostics.Stopwatch.Frequency / 50);
            Invoke(pet, "CompositionTarget_Rendering", null!, EventArgs.Empty);
        }
        Check(appearance.View == WalkView.Side, "Reversal fixture was not side-facing");
        Set(brain, "_walkTargetDirection", -1d);
        Set(brain, "_walkVelocityX", -character.WalkSpeedPxPerSec);
        var sawTurn = false;
        for (var i = 0; i < 40; i++)
        {
            Set(pet, "_lastTickTimestamp", System.Diagnostics.Stopwatch.GetTimestamp() - System.Diagnostics.Stopwatch.Frequency / 50);
            Invoke(pet, "CompositionTarget_Rendering", null!, EventArgs.Empty);
            Check(appearance.View != WalkView.Front && !appearance.FacingRight, "Moving reversal displayed front or faced against actual movement");
            if (appearance.View == WalkView.Turn) sawTurn = true;
        }
        Check(sawTurn && appearance.View == WalkView.Side && !appearance.FacingRight,
            "Desktop turn-around did not finish through the new directional quarter view");
        Console.WriteLine("PASS Desktop reversals match actual travel through a quarter view without frontal sliding");
        Invoke(brain, "ChangeState", PetState.Idle);
        var restingX = brain.X;
        Set(pet, "_lastTickTimestamp", System.Diagnostics.Stopwatch.GetTimestamp() - System.Diagnostics.Stopwatch.Frequency / 50);
        Invoke(pet, "CompositionTarget_Rendering", null!, EventArgs.Empty);
        Check(appearance.IsSettling, "Desktop did not settle the raised foot before facing front");
        for (var i = 0; i < 10; i++)
        {
            Set(pet, "_lastTickTimestamp", System.Diagnostics.Stopwatch.GetTimestamp() - System.Diagnostics.Stopwatch.Frequency / 50);
            Invoke(pet, "CompositionTarget_Rendering", null!, EventArgs.Empty);
            Check(brain.X == restingX, "Settling slides the body along the floor");
        }
        Check(!brain.FacingRight && ((System.Windows.Media.ScaleTransform)pet.FindName("FlipTransform")).ScaleX == 1,
            "Left walking ended with an abrupt horizontal flip of the frontal resting artwork");
        Console.WriteLine("PASS Left walking returns to the same frontal resting orientation without a leaf-crown flip");
    }

    private static void VerifyPersonalSelection(MainWindow pet)
    {
        foreach (var name in new[] { "치이카와", "세균맨", "헬로키티", "폼폼푸린", "시나모롤", "쿠로미" })
        {
            Check(pet.AvailableCharacterNames.Contains(name), "Requested personal character is absent: " + name);
            pet.SwitchCharacter(name);
            Check(pet.CurrentCharacterName == name && ((CharacterDefinition)Get(pet, "_character")!).HasDirectionalWalk,
                "Personal character did not activate its directional presentation: " + name);
            Check(((CharacterDefinition)Get(pet, "_character")!).WalkRig is not null &&
                ((System.Collections.IDictionary)Get(pet, "_frameCache")!).Count == 19,
                "Personal character did not use its bounded 19-texture live presentation: " + name);
            Check(ReferenceEquals(((PetBrain)Get(pet, "_brain")!).BehaviorProfile,
                ((CharacterDefinition)Get(pet, "_character")!).BehaviorProfile), "Switching a personal character did not apply its validated behavior: " + name);
        }
        pet.SwitchCharacter("Companion");
        ((DispatcherTimer)Get(pet, "_settingsSaveTimer")!).Stop();
        Set(pet, "_settingsDirty", false);
        Console.WriteLine("PASS All six optional fan-art characters activate directional live rigs with 19 decoded textures each without changing existing character folders");
    }

    private static void VerifyImageHeaderGuard(string folder, string validFrame)
    {
        var target = Path.Combine(folder, "idle.png");
        try
        {
            var header = new byte[24];
            new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(header, 0);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(8, 4), 13);
            "IHDR"u8.CopyTo(header.AsSpan(12, 4));
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(16, 4), 16384);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(20, 4), 16384);
            File.WriteAllBytes(target, header);
            try { CharacterPresentation.Load(folder); throw new Exception("Oversized PNG header reached image decoding"); }
            catch (InvalidDataException ex) { Check(ex.Message.Contains("헤더 크기"), "Oversized PNG was not rejected before decode"); }
            Console.WriteLine("PASS Actual PNG dimensions are checked before decoding a falsely small declared frame");
        }
        finally { File.Copy(validFrame, target, overwrite: true); }
    }

    private static void WaitForTask(Task task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!task.IsCompleted)
        {
            Check(DateTime.UtcNow < deadline, "Character import timed out");
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(5);
        }
        task.GetAwaiter().GetResult();
    }

    private static void VerifyCharacterImport(MainWindow pet, string fixture, bool capture)
    {
        var source = Path.Combine(fixture, "추가한 친구");
        Directory.CreateDirectory(source);
        var definition = CharacterLoader.Load(Path.Combine(MainWindow.CharactersRoot, "Companion"));
        foreach (var path in definition.AllFramePaths()) File.Copy(path, Path.Combine(source, Path.GetFileName(path)));
        var manifest = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(definition.FolderPath, "character.json")))!;
        manifest["name"] = "추가한 친구";
        File.WriteAllText(Path.Combine(source, "character.json"), manifest.ToJsonString());
        var window = new SettingsWindow(pet);
        window.Show();
        try
        {
            WaitForTask((Task)Invoke(window, "ImportCharacterFolderAsync", source)!);
            ((DispatcherTimer)Get(pet, "_settingsSaveTimer")!).Stop(); Set(pet, "_settingsDirty", false);
            Check(pet.CurrentCharacterName == "추가한 친구" &&
                ((ComboBox)window.FindName("CharacterCombo")).SelectedItem is "추가한 친구" &&
                pet.ResolveCharacterFolder("추가한 친구").StartsWith(pet.CustomCharactersRoot, StringComparison.OrdinalIgnoreCase),
                "Imported character did not activate from the injected user library");
            if (capture) SaveWindowSnapshot(window, "settings-v6.png");
            pet.SwitchCharacter("Companion");
            ((DispatcherTimer)Get(pet, "_settingsSaveTimer")!).Stop(); Set(pet, "_settingsDirty", false);
            WaitForTask((Task)Invoke(window, "ImportCharacterFolderAsync", source)!);
            Check(pet.CurrentCharacterName == "Companion" && ((TextBlock)window.FindName("CharacterImportStatusText")).Text.Contains("못했"),
                "Duplicate import overwrote an existing character or active selection");
            var corrupt = Path.Combine(fixture, "손상된 친구");
            Directory.CreateDirectory(corrupt);
            foreach (var path in Directory.GetFiles(source)) File.Copy(path, Path.Combine(corrupt, Path.GetFileName(path)));
            File.WriteAllText(Path.Combine(corrupt, "idle.png"), "not a PNG");
            WaitForTask((Task)Invoke(window, "ImportCharacterFolderAsync", corrupt)!);
            Check(!pet.AvailableCharacterNames.Contains("손상된 친구") && pet.CurrentCharacterName == "Companion",
                "Invalid PNG import published a broken character or changed the active pet");
            Console.WriteLine("PASS Character import UI activates a validated Unicode pack and preserves selection on duplicate or corrupt input");
        }
        finally
        {
            ((DispatcherTimer)Get(pet, "_settingsSaveTimer")!).Stop(); Set(pet, "_settingsDirty", false);
            window.Close();
        }
    }

    private static void VerifyPoseTransitions(CharacterDefinition character)
    {
        var presentation = CharacterPresentation.Load(character.FolderPath, includeRasterGait: true);
        var previous = new Image();
        var image = new Image();
        var grid = new Grid();
        grid.Children.Add(previous); grid.Children.Add(image);
        var window = new Window { Content = grid, Width = 160, Height = 180, ShowActivated = false };
        var presenter = new PoseTransitionPresenter(image, previous);
        var idle = character.GetFrames(PetState.Idle);
        var side = character.GetWalkFrames(WalkView.Side);
        var turn = character.GetWalkFrames(WalkView.Turn);
        window.Show();
        try
        {
            presenter.SetFrame(presentation.Frames[idle[0]], 1, idle);
            presenter.SetFrame(presentation.Frames[side[0]], 1, side, allowTransition: false);
            Check(!presenter.IsTransitioning && previous.Source is null, "Departure retained a frontal ghost");
            presenter.SetFrame(presentation.Frames[turn[0]], 1, turn);
            var outgoing = previous.Source;
            Check(presenter.IsTransitioning && outgoing is not null && !previous.IsHitTestVisible && !previous.Focusable,
                "Pose dissolve did not create a noninteractive previous sprite");
            presenter.SetFrame(presentation.Frames[turn[1]], 1, turn);
            Check(ReferenceEquals(previous.Source, outgoing), "Ordinary gait frame restarted the pose dissolve");
            presenter.SetFrame(presentation.Frames[side[2]], -1, side);
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (presenter.IsTransitioning && DateTime.UtcNow < deadline)
            {
                var frame = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
                Thread.Sleep(5);
            }
            Check(!presenter.IsTransitioning && previous.Source is null && image.Opacity == 1 &&
                ((System.Windows.Media.ScaleTransform)image.RenderTransform).ScaleX == -1,
                "Rapid pose change retained stale imagery or lost final orientation");
            presenter.SetFrame(presentation.Frames[turn[2]], 1, turn);
            presenter.Reset();
            Check(!presenter.IsTransitioning && previous.Visibility == Visibility.Collapsed, "Reset retained an animated ghost");
            Console.WriteLine("PASS Pose dissolves preserve gait updates, suppress departing front ghosts and clean up rapid changes");
        }
        finally { presenter.Reset(); window.Close(); }
    }

    private static void VerifyConnections(string folder, MainWindow pet, bool capture)
    {
        var home = Path.Combine(folder, "connection-home");
        Directory.CreateDirectory(home);
        var hook = Path.Combine(home, "assistant-stop-hook.ps1");
        File.WriteAllText(hook, "param($Source,$RegistrationId)");
        var history = new AssistantIntegrationHistory(Path.Combine(home, "history.json"));
        var service = new AssistantIntegrationService(home, hook);
        var window = new AssistantConnectionsWindow(service, history: history);
        window.Show();
        try
        {
            WaitForOperation(window);
            Invoke(window, "Connect_Click", window.FindName("ClaudeConnectButton"), new RoutedEventArgs());
            WaitForOperation(window);
            Check(service.GetStatus(AssistantProvider.Claude).Registered && history.GetProfiles().Count == 1,
                "Claude UI did not register and journal fixture profile");
            Invoke(window, "Connect_Click", window.FindName("CodexConnectButton"), new RoutedEventArgs());
            WaitForOperation(window);
            Check(service.GetStatus(AssistantProvider.Codex).RequiresTrustReview && history.GetProfiles().Count == 2,
                "Codex UI did not expose required trust review");
            if (capture) SaveWindowSnapshot(window, "connections-v4.png");
            Invoke(window, "Disconnect_Click", window.FindName("ClaudeDisconnectButton"), new RoutedEventArgs());
            WaitForOperation(window);
            Check(!service.GetStatus(AssistantProvider.Claude).Registered && service.GetStatus(AssistantProvider.Codex).Registered &&
                history.GetProfiles().Count == 1, "Disconnect changed another provider or left stale journal entry");
            Console.WriteLine("PASS Connection UI connects, journals, displays trust review and disconnects only the selected provider");
            File.WriteAllText(Path.Combine(home, "history.json"), "{");
            Invoke(window, "Disconnect_Click", window.FindName("CodexDisconnectButton"), new RoutedEventArgs());
            WaitForOperation(window);
            Check(!service.GetStatus(AssistantProvider.Codex).Registered &&
                ((TextBlock)window.FindName("CodexStatusText")).Text == service.GetStatus(AssistantProvider.Codex).Detail &&
                ((TextBlock)window.FindName("OperationText")).Text.Contains("해제됐지만"),
                "History failure left a stale connected label or hid the successful disconnect");
            Console.WriteLine("PASS Connection UI reports actual disconnection separately from journal cleanup failure");
        }
        finally { window.Close(); }

        Check(service.Connect(AssistantProvider.Claude).Registered, "Close-race fixture registration failed");
        var pendingStartup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var preferences = (PetSettings)Get(pet, "_settings")!;
        preferences.AutoConnectAssistants = true;
        var closingWindow = new AssistantConnectionsWindow(service, pet, () => pendingStartup.Task);
        closingWindow.Show();
        try
        {
            WaitForOperation(closingWindow);
            Invoke(closingWindow, "Disconnect_Click", closingWindow.FindName("ClaudeDisconnectButton"), new RoutedEventArgs());
            Check(!preferences.AutoConnectAssistants && service.GetStatus(AssistantProvider.Claude).Registered,
                "Disconnect did not disable auto-connect before waiting for startup");
            ((DispatcherTimer)Get(pet, "_settingsSaveTimer")!).Stop();
            Set(pet, "_settingsDirty", false);
            closingWindow.Close();
            pendingStartup.SetResult();
            WaitForOperation(closingWindow);
            Check(!service.GetStatus(AssistantProvider.Claude).Registered && !preferences.AutoConnectAssistants,
                "Closing connection UI lost the pending disconnection or auto-connect preference");
            Console.WriteLine("PASS Disconnect waits for initial setup and survives immediate window closure with auto-connect disabled");
        }
        finally { closingWindow.Close(); pendingStartup.TrySetResult(); }

        Check(service.Connect(AssistantProvider.Claude).Registered, "Upgrade fixture registration failed");
        var updatedHook = Path.Combine(home, "assistant-stop-hook-updated.ps1");
        File.WriteAllText(updatedHook, "param($Source,$RegistrationId)");
        var updatedService = new AssistantIntegrationService(home, updatedHook);
        Check(updatedService.GetStatus(AssistantProvider.Claude) is { Registered: false, HasOwnedRegistration: true },
            "Old app registration was mistaken for an unconnected provider");
        var upgradeWindow = new AssistantConnectionsWindow(updatedService, history: history);
        upgradeWindow.Show();
        try
        {
            WaitForOperation(upgradeWindow);
            Invoke(upgradeWindow, "Connect_Click", upgradeWindow.FindName("ClaudeConnectButton"), new RoutedEventArgs());
            WaitForOperation(upgradeWindow);
            Check(updatedService.GetStatus(AssistantProvider.Claude).Registered &&
                ((TextBlock)upgradeWindow.FindName("OperationText")).Text.Contains("유지되지만"),
                "Journal failure after upgrade removed the pre-existing DesktopPet connection");
            Console.WriteLine("PASS Journal failure during a connection upgrade preserves the existing owned registration");
        }
        finally { upgradeWindow.Close(); }
    }

    private static void SaveWindowSnapshot(Window window, string fileName)
    {
        window.UpdateLayout();
        var content = (FrameworkElement)window.Content;
        var width = (int)Math.Ceiling(content.ActualWidth) + 32;
        var height = (int)Math.Ceiling(content.ActualHeight) + 32;
        var drawing = new System.Windows.Media.DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            context.DrawRectangle(window.Background, null, new Rect(0, 0, width, height));
            context.DrawRectangle(new System.Windows.Media.VisualBrush(content), null,
                new Rect(16, 16, content.ActualWidth, content.ActualHeight));
        }
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(AppContext.BaseDirectory, fileName));
        encoder.Save(stream);
    }
}
