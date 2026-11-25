using System.IO;
using System.Reflection;
using System.Windows;
using DesktopPet.App;
using DesktopPet.Core;

internal static class WalkEntryUiTests
{
    public static void Run(string root, bool capture = false)
    {
        var verified = 0;
        foreach (var name in CharacterLoader.ListAvailableCharacterNames(MainWindow.CharactersRoot))
        {
            var presentation = CharacterPresentation.Load(Path.Combine(MainWindow.CharactersRoot, name));
            if (presentation.Definition.WalkRig is not { } rigDefinition) continue;
            var motion = new MotionPreviewWindow(presentation, 1);
            try
            {
                motion.Show(); Invoke(motion, "PausePlayback_Click", motion, new RoutedEventArgs()); Invoke(motion, "ResetScene");
                var brain = (PetBrain)Get(motion, "_brain")!;
                var appearance = (WalkAppearanceController)Get(motion, "_walkAppearance")!;
                var visual = (GroundedRigVisual)motion.FindName("MotionRig");
                var cycle = presentation.Definition.WalkCycleDistancePx!.Value;
                appearance.Update(PetState.Walk, true, 60, 60, 0.2, 60, cycle * 0.8, cycle);
                Invoke(brain, "ChangeState", PetState.Walk); Invoke(motion, "RenderActor", 0d);
                Invoke(brain, "ChangeState", PetState.Idle); Invoke(motion, "RenderActor", 0d);
                Invoke(motion, "RenderActor", 0.18); Invoke(motion, "RenderActor", 0d);
                Check(!appearance.DisplayWalkPose && visual.Pose.FootA.Lift == 0, "Fixture did not finish settling");
                var phase = appearance.GaitPhase;
                Invoke(brain, "ChangeState", PetState.Walk); Invoke(motion, "RenderActor", 0d);
                var anchor = new System.Windows.Point(presentation.Definition.FrameWidth / 2d, presentation.Definition.AnchorY);
                var firstLift = anchor.Y - visual.NearFootMatrix.Transform(anchor).Y;
                Console.WriteLine($"INFO {name}: restart first-frame foot lift={firstLift:F4}px, phase={phase:F3}");
                Check(Math.Abs(firstLift) < 1e-9 && appearance.GaitPhase == phase,
                    name + ": restarting raised a fully lowered foot immediately or reset its distance phase");
                Invoke(motion, "RenderActor", 1d / 60);
                Check(visual.Pose.FootA.Lift > 0 && visual.Pose.FootA.Lift < rigDefinition.FootLiftPx * 0.2,
                    name + ": first timed frame still lifted the foot in one jump");
                var before = visual.Pose.FootA.Lift;
                Invoke(brain, "ChangeState", PetState.Idle); Invoke(motion, "RenderActor", 0d);
                Check(Math.Abs(visual.Pose.FootA.Lift - before) < 1e-9, "Stopping during entry changed the raised foot without elapsed time");
                Invoke(motion, "RenderActor", 0.02); before = visual.Pose.FootA.Lift;
                Invoke(brain, "ChangeState", PetState.Walk); Invoke(motion, "RenderActor", 0d);
                Check(Math.Abs(visual.Pose.FootA.Lift - before) < 1e-9, "Resuming partial settling jumped the raised foot");
                Invoke(motion, "RenderActor", 0.09);
                var expected = GroundedGait.Sample(appearance.GaitPhase, cycle, rigDefinition.FootLiftPx);
                Check(visual.Pose == expected && appearance.GaitPhase == phase, "Entry did not finish on the exact unchanged gait");
                motion.UpdateLayout();
                foreach (var facingRight in new[] { true, false })
                {
                    double? plantedWorldX = null;
                    for (var i = 0; i <= 20; i++)
                    {
                        visual.UpdatePose(WalkView.Side, 0.1 + i * 0.01, 0, facingRight, 0, entryWeight: i / 20d);
                        var displayed = visual.DisplayMatrix.Transform(visual.NearFootMatrix.Transform(anchor));
                        var worldX = displayed.X + (facingRight ? 1 : -1) * cycle * i * 0.01 * visual.Width / presentation.Definition.FrameWidth;
                        plantedWorldX ??= worldX;
                        Check(Math.Abs(worldX - plantedWorldX.Value) < 1e-8,
                            $"Entry easing moved a planted foot: {name}, right={facingRight}, sample={i}, first={plantedWorldX.Value:R}, current={worldX:R}, width={visual.Width:R}, actual={visual.ActualWidth:R}");
                    }
                }
                visual.UpdatePose(WalkView.Side, phase, 0, true, 0, reducedMotion: true, entryWeight: 0);
                Check(visual.Pose == expected, "Reduced motion retained entry easing");
                visual.UpdatePose(WalkView.Side, phase, 0, true, 0, entryWeight: double.NaN);
                Check(visual.Pose == expected, "Invalid entry input corrupted the native pose");
                if (capture && name == "Companion") Program.SaveWindowSnapshot(motion, "walk-entry-finished.png");
                verified++;
            }
            finally { motion.Close(); }
        }
        Check(verified > 0, "No live rigs exercised");
        Console.WriteLine($"PASS Native restart and interrupted settling lift feet continuously without changing distance phase, with exact bounded completion for {verified} characters");
        Console.WriteLine("PASS Entry easing keeps both left/right planted contacts fixed, skips extra easing for reduced motion and tolerates invalid weights");
    }
    private static object? Get(object obj, string name) => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(obj);
    private static object? Invoke(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(obj, args);
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
}
