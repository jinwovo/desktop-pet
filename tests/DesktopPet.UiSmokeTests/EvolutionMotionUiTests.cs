using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DesktopPet.App;
using DesktopPet.Core;

/// <summary>Exercises the real preview handlers with isolated preferences and no desktop pet initialization.</summary>
internal static class EvolutionMotionUiTests
{
    public static void Run(string root)
    {
        var originalLanguage = UiText.Language;
        var presentation = CharacterPresentation.Load(Path.Combine(MainWindow.CharactersRoot, "Companion"));
        var pet = new MainWindow(new CharacterLibrary(MainWindow.CharactersRoot, Path.Combine(root, "evolution-motion-custom")),
            inboxDirectory: Path.Combine(root, "evolution-motion-inbox"),
            workAreasProvider: () => new[] { new DesktopRect(0, 0, 1920, 1080) });
        var loaded = (RoutedEventHandler)Delegate.CreateDelegate(typeof(RoutedEventHandler), pet,
            typeof(MainWindow).GetMethod("MainWindow_Loaded", BindingFlags.Instance | BindingFlags.NonPublic)!);
        pet.Loaded -= loaded;
        Set(pet, "_settings", new PetSettings
        {
            AutoConnectAssistants = false, AssistantSetupSeen = true, Language = "ko",
            Visible = false, WindowFallEnabled = false,
        });
        Set(pet, "_brain", new PetBrain(new PlatformLayout(new[] { new Platform(0, 1920, 1080) }), 128, 122, 500, 958));
        Invoke(pet, "ApplyPresentation", presentation);
        var save = (DispatcherTimer)Get(pet, "_settingsSaveTimer")!;
        save.Interval = TimeSpan.FromDays(1);
        SettingsWindow? settings = null;
        MotionPreviewWindow? motion = null;
        try
        {
            UiText.SetLanguage("ko");
            settings = new SettingsWindow(pet);
            settings.Show();
            motion = new MotionPreviewWindow(presentation, 1);
            motion.Show();
            DrainDispatcher();

            // Showing a preview can already start its automatic walk before the gaze probe.
            // Exercise that starting condition deterministically instead of depending on timing.
            Invoke((PetBrain)Get(motion, "_brain")!, "ChangeState", PetState.Walk);
            Invoke(motion, "RenderActor", 0d);
            VerifyGazeSourceStability(motion, presentation);
            VerifySettingsSettling(settings);
            VerifyPreviewLifecycle(settings, motion);
            VerifyWeightedIdleHandoff(motion, presentation);
            VerifyLanguagePreservesPreview(settings);
            VerifyMotionLanguagePreservesPose(motion, presentation);
            Check(!pet.AutoConnectAssistants && Get(pet, "_inbox") is null && Get(pet, "_windowPlatformMonitor") is null,
                "Motion fixtures initialized assistant registration, a real inbox or desktop window scanning");
        }
        finally
        {
            motion?.Close();
            settings?.Close();
            save.Stop();
            Set(pet, "_settingsDirty", false);
            pet.DisposeResources();
            pet.Close();
            UiText.SetLanguage(originalLanguage);
        }
    }

    private static void VerifyGazeSourceStability(MotionPreviewWindow motion, CharacterPresentation presentation)
    {
        // This probe checks resting gaze; walk-to-rest settling is exercised separately.
        Invoke(motion, "ResetScene");
        var brain = (PetBrain)Get(motion, "_brain")!;
        var actor = (Image)motion.FindName("Actor");
        var gaze = (CursorGazeController)Get(motion, "_cursorGaze")!;
        gaze.Reset();
        Invoke(brain, "ChangeState", PetState.LookAtCursor);
        var center = brain.X + actor.Width / 2;
        void RenderAt(double offset)
        {
            Set(motion, "_cursor", new PetPoint(center + offset, brain.Y + actor.Height / 2));
            Invoke(motion, "RenderActor", 0d);
        }
        void ExpectDirection(bool right)
        {
            var frames = presentation.Definition.GetLookFrames(right);
            var expected = presentation.Frames[presentation.Definition.GetFramePath(PetState.LookAtCursor, frames, brain.StateElapsedMs)];
            Check(gaze.FacingRight == right && ReferenceEquals(actor.Source, expected),
                "Motion preview cursor gaze changed its raw directional image inside the center dead zone");
        }

        RenderAt(30); ExpectDirection(true);
        foreach (var offset in new[] { -1d, 1, -1, 0, 1 }) { RenderAt(offset); ExpectDirection(true); }
        RenderAt(-30); ExpectDirection(false);
        foreach (var offset in new[] { 1d, -1, 0, 1, -1 }) { RenderAt(offset); ExpectDirection(false); }
        RenderAt(30); ExpectDirection(true);
        Console.WriteLine("PASS Actual MotionPreview gaze keeps directional image sources stable across center jitter and turns immediately beyond either dead-zone edge");
    }

    private static void VerifySettingsSettling(SettingsWindow settings)
    {
        var choice = (ComboBox)settings.FindName("PreviewStateCombo");
        var appearance = (WalkAppearanceController)Get(settings, "_walkAppearance")!;
        choice.SelectedValue = PetState.Walk;
        AdvanceSettings(settings, 20);
        Check(appearance.DisplayWalkPose && appearance.GaitPhase > 0, "Settings walking fixture did not advance its gait");
        var phase = appearance.GaitPhase;
        choice.SelectedValue = PetState.Idle;
        Check(appearance.IsSettling && appearance.DisplayWalkPose && appearance.GaitPhase == phase,
            "Selecting idle reset the swatch gait instead of settling its existing raised foot");
        var sawFinal = false;
        for (var i = 0; i < 20 && appearance.DisplayWalkPose; i++)
        {
            AdvanceSettings(settings, 20);
            sawFinal |= appearance.IsSettling && appearance.SettleProgress == 1;
        }
        Check(sawFinal && !appearance.DisplayWalkPose && appearance.SettleProgress == 1,
            "Settings walk-to-idle transition skipped its exact final rest pose or never released the live rig");
        Check(((Image)settings.FindName("PreviewImage")).Visibility == Visibility.Visible &&
              ((GroundedRigVisual)settings.FindName("PreviewRig")).Visibility == Visibility.Collapsed,
            "Settled Settings preview did not hand back to the resting image");
        Console.WriteLine("PASS Settings Walk-to-Idle preserves the current gait, lowers the raised foot through settling and hands off its final resting pose");
    }

    private static void VerifyPreviewLifecycle(SettingsWindow settings, MotionPreviewWindow motion)
    {
        ((ComboBox)settings.FindName("PreviewStateCombo")).SelectedValue = PetState.Walk;
        AdvanceSettings(settings, 20);
        foreach (var minimize in new[] { false, true })
        {
            if (minimize) settings.WindowState = WindowState.Minimized;
            else settings.Hide();
            DrainDispatcher();
            var timer = (DispatcherTimer)Get(settings, "_previewTimer")!;
            Check(!timer.IsEnabled && !(bool)Get(settings, "_previewRenderingSubscribed")!,
                "A hidden or minimized Settings preview retained its timer/render subscription");
            var phase = ((WalkAppearanceController)Get(settings, "_walkAppearance")!).GaitPhase;
            var virtualPosition = (double)Get(settings, "_previewVirtualPositionPx")!;
            var stateElapsed = (double)Get(settings, "_previewStateElapsedMs")!;
            var clock = (Stopwatch)Get(settings, "_previewClock")!;
            Set(settings, "_previewLastElapsedMs", clock.Elapsed.TotalMilliseconds - 60_000);
            Invoke(settings, "OnPreviewRendering", settings, EventArgs.Empty);
            Check(phase == ((WalkAppearanceController)Get(settings, "_walkAppearance")!).GaitPhase &&
                  virtualPosition == (double)Get(settings, "_previewVirtualPositionPx")! &&
                  stateElapsed == (double)Get(settings, "_previewStateElapsedMs")!,
                "Direct rendering callbacks advanced an inactive Settings preview");
            if (minimize) settings.WindowState = WindowState.Normal;
            else settings.Show();
            DrainDispatcher();
            Check(timer.IsEnabled && (bool)Get(settings, "_previewRenderingSubscribed")!, "Settings preview did not resume");
            Check(clock.Elapsed.TotalMilliseconds - (double)Get(settings, "_previewLastElapsedMs")! < 1000,
                "Settings preview resumed with a stale elapsed-time baseline");
        }

        foreach (var minimize in new[] { false, true })
        {
            if (minimize) motion.WindowState = WindowState.Minimized;
            else motion.Hide();
            DrainDispatcher();
            Check(!motion.IsAnimating, "A hidden or minimized motion preview retained its render subscription");
            var brain = (PetBrain)Get(motion, "_brain")!;
            var appearance = (WalkAppearanceController)Get(motion, "_walkAppearance")!;
            var before = (brain.X, brain.Y, brain.State, brain.StateElapsedMs, appearance.GaitPhase);
            Set(motion, "_lastTimestamp", Stopwatch.GetTimestamp() - 60L * Stopwatch.Frequency);
            Invoke(motion, "OnRendering", motion, EventArgs.Empty);
            Check(before == (brain.X, brain.Y, brain.State, brain.StateElapsedMs, appearance.GaitPhase),
                "Direct rendering callbacks advanced an inactive motion preview");
            if (minimize) motion.WindowState = WindowState.Normal;
            else motion.Show();
            DrainDispatcher();
            Check(motion.IsAnimating && Stopwatch.GetElapsedTime((long)Get(motion, "_lastTimestamp")!).TotalSeconds < 1,
                "Motion preview did not resume with a fresh simulation baseline");
        }
        Console.WriteLine("PASS Hidden and minimized Settings/Motion previews stop their work, reject stale callbacks and rebase elapsed-time clocks when resumed");
    }

    private static void VerifyWeightedIdleHandoff(MotionPreviewWindow motion, CharacterPresentation presentation)
    {
        Invoke(motion, "ResetScene");
        var brain = (PetBrain)Get(motion, "_brain")!;
        var appearance = (WalkAppearanceController)Get(motion, "_walkAppearance")!;
        var rig = (GroundedRigVisual)motion.FindName("MotionRig");
        var definition = presentation.Definition;
        Check(definition.FrameDurationsByState.ContainsKey(PetState.Idle), "Idle handoff fixture has no weighted blink timeline");
        Invoke(brain, "ChangeState", PetState.Walk);
        Invoke(motion, "RenderActor", 0.02);
        Set(motion, "_idleAppearanceElapsedMs", 4190d);
        Invoke(brain, "ChangeState", PetState.Idle);
        Invoke(motion, "RenderActor", 0d);
        Check(appearance.IsSettling, "Idle clock fixture did not enter settling");
        var idleClock = (double)Get(motion, "_idleAppearanceElapsedMs")!;
        Invoke(motion, "RenderActor", 0.02);
        var advancedClock = (double)Get(motion, "_idleAppearanceElapsedMs")!;
        Check(advancedClock > idleClock && advancedClock is >= 4200 and < 4320,
            "Weighted idle timeline did not advance through its blink during settling");
        var expectedBlink = definition.GetFramePath(PetState.Idle, definition.GetFrames(PetState.Idle), advancedClock);
        Check(Path.GetFileName(expectedBlink) == "blink.png" && ReferenceEquals(Get(rig, "_restFrame"), presentation.Frames[expectedBlink]),
            "Settling chose StateElapsedMs instead of the independent weighted idle clock");
        var sawFinal = false;
        for (var i = 0; i < 20 && appearance.DisplayWalkPose; i++)
        {
            Invoke(motion, "RenderActor", 0.02);
            var expected = presentation.Frames[definition.GetFramePath(PetState.Idle, definition.GetFrames(PetState.Idle),
                (double)Get(motion, "_idleAppearanceElapsedMs")!)];
            if (appearance.IsSettling)
            {
                Check(ReferenceEquals(Get(rig, "_restFrame"), expected), "Settling diverged from the weighted resting timeline");
                sawFinal |= appearance.SettleProgress == 1;
            }
            else Check(ReferenceEquals(((Image)motion.FindName("Actor")).Source, expected),
                "Raster handoff changed to a different idle clock after the exact final rest blend");
        }
        Check(sawFinal && !appearance.DisplayWalkPose, "Weighted idle handoff did not finish");
        Console.WriteLine("PASS Actual MotionPreview settling and raster handoff share the advancing weighted idle/blink timeline");
    }

    private static void VerifyLanguagePreservesPreview(SettingsWindow settings)
    {
        ((ComboBox)settings.FindName("PreviewStateCombo")).SelectedValue = PetState.Walk;
        AdvanceSettings(settings, 20);
        var presentation = Get(settings, "_previewPresentation");
        var appearance = (WalkAppearanceController)Get(settings, "_walkAppearance")!;
        var phase = appearance.GaitPhase;
        var stateElapsed = (double)Get(settings, "_previewStateElapsedMs")!;
        foreach (var language in new[] { "en", "zh-CN", "ko" })
        {
            UiText.SetLanguage(language);
            Check(ReferenceEquals(presentation, Get(settings, "_previewPresentation")) &&
                  appearance.GaitPhase == phase && (double)Get(settings, "_previewStateElapsedMs")! == stateElapsed &&
                  ((ComboBox)settings.FindName("PreviewStateCombo")).SelectedValue is PetState.Walk,
                "Changing UI language decoded/reset the same character or changed its active preview state");
        }
        Console.WriteLine("PASS EN/zh-CN/KO relabeling preserves the same Settings presentation, walk phase, state and animation clock");
    }

    private static void VerifyMotionLanguagePreservesPose(MotionPreviewWindow motion, CharacterPresentation presentation)
    {
        var brain = (PetBrain)Get(motion, "_brain")!;
        Invoke(brain, "ChangeState", PetState.Walk);
        brain.Tick(0.12, new PetPoint(-10000, -10000));
        Invoke(motion, "RenderActor", 0.12);
        var gait = (WalkAppearanceController)Get(motion, "_walkAppearance")!;
        var before = (brain.X, brain.Y, brain.State, brain.StateElapsedMs, brain.HorizontalVelocityPxPerSec, gait.GaitPhase,
            (double)Get(motion, "_walkElapsedMs")!, ((GroundedRigVisual)motion.FindName("MotionRig")).PoseUpdateCount);
        foreach (var (language, title, reset, jump, drop, statePrefix) in new[]
        {
            ("en", "DesktopPet Motion Preview · ", "Reset", "Jump", "Drop from above", "Walking"),
            ("zh-CN", "DesktopPet 动作预览 · ", "重置", "跳跃", "从上方放下", "行走中"),
            ("ko", "DesktopPet 움직임 시험 · ", "처음으로", "점프", "위에서 놓기", "걷는 중"),
        })
        {
            UiText.SetLanguage(language);
            Check(motion.Title == title + presentation.Definition.Name &&
                ((Button)motion.FindName("ResetSceneButton")).Content is string resetLabel && resetLabel == reset &&
                ((Button)motion.FindName("JumpButton")).Content is string jumpLabel && jumpLabel == jump &&
                ((Button)motion.FindName("DropButton")).Content is string dropLabel && dropLabel == drop &&
                ((TextBlock)motion.FindName("StateText")).Text.StartsWith(statePrefix, StringComparison.Ordinal),
                "Motion preview title, controls or live state did not follow the selected language");
            Check(before == (brain.X, brain.Y, brain.State, brain.StateElapsedMs, brain.HorizontalVelocityPxPerSec, gait.GaitPhase,
                (double)Get(motion, "_walkElapsedMs")!, ((GroundedRigVisual)motion.FindName("MotionRig")).PoseUpdateCount) &&
                ReferenceEquals(presentation, Get(motion, "_presentation")), "Localizing the motion window restarted simulation, redrew the rig or decoded textures");
            if (Environment.GetCommandLineArgs().Contains("--capture")) Program.SaveWindowSnapshot(motion, "motion-localized-" + language + ".png");
        }
        var languageEvent = typeof(UiText).GetField("LanguageChanged", BindingFlags.Static | BindingFlags.NonPublic)!;
        var subscribers = ((Delegate?)languageEvent.GetValue(null))?.GetInvocationList().Length ?? 0;
        motion.Close();
        Check((((Delegate?)languageEvent.GetValue(null))?.GetInvocationList().Length ?? 0) == subscribers - 2,
            "Closed motion window retained static or dynamic localization subscriptions");
        var closedTitle = motion.Title;
        UiText.SetLanguage("en");
        Check(motion.Title == closedTitle, "Closed motion window retained language updates");
        Console.WriteLine("PASS Motion window localizes its title, buttons and live walking labels in EN/zh-CN/KO without changing character, pose, gait or clocks");
    }

    private static void AdvanceSettings(SettingsWindow settings, double milliseconds)
    {
        var clock = (Stopwatch)Get(settings, "_previewClock")!;
        Set(settings, "_previewLastElapsedMs", clock.Elapsed.TotalMilliseconds - milliseconds);
        Invoke(settings, "RenderPreview");
    }

    private static void DrainDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static object? Get(object obj, string name) => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(obj);
    private static void Set(object obj, string name, object value) => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(obj, value);
    private static object? Invoke(object obj, string name, params object?[] args) => obj.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(obj, args);
    private static void Check(bool valid, string message) { if (!valid) throw new Exception(message); }
}
