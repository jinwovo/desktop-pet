using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DesktopPet.App;
using DesktopPet.Core;

internal static class MotionPlaybackUiTests
{
    public static void Run(string root, bool capture = false)
    {
        var language = UiText.Language;
        var presentation = CharacterPresentation.Load(Path.Combine(MainWindow.CharactersRoot,"Companion"));
        var window = new MotionPreviewWindow(presentation,1);
        try
        {
            window.Show(); Pump();
            var pause=(Button)(window.FindName("PausePlaybackButton")??throw new Exception("Motion preview has no playback pause action"));
            var step=(Button)(window.FindName("NextFrameButton")??throw new Exception("Motion preview has no single-frame action"));
            Check(window.IsAnimating && pause.IsEnabled && !step.IsEnabled,"Playback controls do not reflect initial running state");
            Click(pause);
            Check(!window.IsAnimating && step.IsEnabled,"Pause did not stop automatic rendering or enable frame inspection");
            var before=Snapshot(window);
            Set(window,"_lastTimestamp",Stopwatch.GetTimestamp()-60L*Stopwatch.Frequency);
            Invoke(window,"OnRendering",window,EventArgs.Empty); WaitMilliseconds(100);
            Check(Equals(before,Snapshot(window)),"Paused preview advanced from a stale render callback or wall time");

            foreach(var fps in new[]{30,60,120})
            {
                window.SetRenderOptions(fps,false); Invoke(window,"ResetScene");
                var brain=(PetBrain)Get(window,"_brain")!;
                brain.StartEmote(PetState.Cry); Invoke(window,"RenderActor",0d);
                var elapsed=brain.StateElapsedMs;
                Click(step);
                Check(Math.Abs(brain.StateElapsedMs-elapsed-1000d/fps)<0.00001 && !window.IsAnimating,
                    "Single-frame advance did not use the selected frame cap or restarted playback");
            }
            Console.WriteLine("PASS Pause blocks automatic/stale rendering and single-frame inspection advances exactly 1/30, 1/60 or 1/120 second");

            window.SetRenderOptions(60,false); Invoke(window,"ResetScene");
            var walking=(PetBrain)Get(window,"_brain")!;
            Set(walking,"_walkTargetDirection",1);
            Invoke(walking,"ChangeState",PetState.Walk); Invoke(window,"RenderActor",0d);
            var initial=Pixels(window);
            for(var i=0;i<8;i++)Click(step);
            Check(Math.Abs(walking.X-40)>0.0001 && ((WalkAppearanceController)Get(window,"_walkAppearance")!).GaitPhase>0 && !initial.AsSpan().SequenceEqual(Pixels(window)),
                $"Rendered frame inspection did not advance: x={walking.X}, phase={((WalkAppearanceController)Get(window,"_walkAppearance")!).GaitPhase}, pixelsChanged={!initial.AsSpan().SequenceEqual(Pixels(window))}");
            var paused=Snapshot(window); var pixels=Pixels(window);
            foreach(var lang in new[]{"ko","en","zh-CN"})
            {
                UiText.SetLanguage(lang);
                Check((string)pause.Content==UiText.Get("계속") && (string)step.Content==UiText.Get("다음 프레임") &&
                    ((TextBlock)window.FindName("StateText")).Text.EndsWith(UiText.Format("{0} · 일시정지", ""),StringComparison.Ordinal),
                    "Paused controls/state did not follow the UI language");
                Check(Equals(paused,Snapshot(window)) && pixels.AsSpan().SequenceEqual(Pixels(window)),"Localizing a paused preview changed its pose or simulation");
                if(capture)Program.SaveWindowSnapshot(window,"motion-inspect-"+lang+".png");
            }
            foreach(var minimize in new[]{false,true})
            {
                if(minimize)window.WindowState=WindowState.Minimized; else window.Hide();
                Pump(); Check(!window.IsAnimating&&!step.IsEnabled,"Inactive inspector retained an enabled step/render path");
                Click(step); Invoke(window,"OnRendering",window,EventArgs.Empty);
                Check(Equals(paused,Snapshot(window)),"Hidden inspector advanced a queued step");
                if(minimize)window.WindowState=WindowState.Normal; else window.Show();
                Pump(); Check(!window.IsAnimating&&step.IsEnabled&&Equals(paused,Snapshot(window)),"Showing inspector lost pause state or advanced its scene");
            }
            Click((Button)window.FindName("JumpButton"));
            var jumping=(PetBrain)Get(window,"_brain")!;
            var jumpY=jumping.Y; WaitMilliseconds(60);
            Check(jumping.State==PetState.Falling&&jumping.Y==jumpY&&!window.IsAnimating,"Paused jump advanced gravity without a frame command");
            Click(step); Check(jumping.Y<jumpY,"Single-step did not advance the prepared jump");
            Click((Button)window.FindName("DropButton"));
            Check(jumping.Y==8&&Canvas.GetTop((Image)window.FindName("Actor"))==8,"Paused drop did not redraw its explicitly moved position");
            Click(step); Check(jumping.Y>8,"Single-step did not advance the prepared fall");
            Click((Button)window.FindName("ResetSceneButton"));
            Check(!window.IsAnimating&&step.IsEnabled&&((PetBrain)Get(window,"_brain")!).X==40,"Reset resumed automatic playback or failed to reset the paused scene");
            Set(window,"_lastTimestamp",Stopwatch.GetTimestamp()-60L*Stopwatch.Frequency);
            Click(pause);
            Check(window.IsAnimating&&!step.IsEnabled&&Stopwatch.GetElapsedTime((long)Get(window,"_lastTimestamp")!).TotalSeconds<1,
                "Resume did not rebase the simulation clock");
            var running=Snapshot(window); Click(step);
            Check(Equals(running,Snapshot(window)),"A queued frame step advanced an already running preview");
            window.Close(); Click(step); Click(pause);
            Check(!window.IsAnimating&&!((GroundedRigVisual)window.FindName("MotionRig")).HasRig,"Closed inspector retained playback or rig resources");
            Console.WriteLine("PASS Actual frame stepping changes rendered gait; language, hide/minimize, reset, resume and close preserve inspector lifecycle");
            VerifyRasterClocks(root,presentation);
        }
        finally{window.Close();UiText.SetLanguage(language);}
    }

    private static void VerifyRasterClocks(string root,CharacterPresentation source)
    {
        var folder=Path.Combine(root,"inspector-raster"); Directory.CreateDirectory(folder);
        File.Copy(source.Definition.GetFrames(PetState.Idle)[0],Path.Combine(folder,"idle.png"));
        File.Copy(source.Definition.GetFrames(PetState.Cry)[0],Path.Combine(folder,"cry.png"));
        File.WriteAllText(Path.Combine(folder,"character.json"),JsonSerializer.Serialize(new
        {name="Inspector Sprite",frameSizePx=new{width=source.Definition.FrameWidth,height=source.Definition.FrameHeight},anchor=new{x=source.Definition.FrameWidth/2,y=source.Definition.AnchorY},frames=new{idle=new[]{"idle.png"},cry=new[]{"cry.png"}}}));
        var window=new MotionPreviewWindow(CharacterPresentation.Load(folder),1);
        try
        {
            window.Show(); Pump();
            var pause=(Button)window.FindName("PausePlaybackButton"); var step=(Button)window.FindName("NextFrameButton");
            Click(pause);
            var brain=(PetBrain)Get(window,"_brain")!;
            brain.StartEmote(PetState.Cry); Invoke(window,"RenderActor",0d);
            var transition=(PoseTransitionPresenter)Get(window,"_poseTransition")!;
            Check(transition.IsTransitioning,"Raster fixture did not create a pose dissolve while paused");
            Click(step); Click(step); WaitMilliseconds(20);
            var actor=(Image)window.FindName("Actor"); var previous=(Image)window.FindName("PreviousActor");
            var opacity=actor.Opacity; var pixels=Pixels(window);
            Check(opacity>0.05&&opacity<0.95&&transition.IsTransitioning,"Frame stepping did not advance a paused raster dissolve");
            WaitMilliseconds(140);
            Check(Math.Abs(actor.Opacity-opacity)<0.000001 && pixels.AsSpan().SequenceEqual(Pixels(window)),"Raster clocks continued blending while simulation was paused");
            window.Hide(); Pump(); window.Show(); Pump();
            Check(!window.IsAnimating&&Math.Abs(actor.Opacity-opacity)<0.000001,"Hiding a paused raster preview changed its inspected blend");
            for(var i=0;i<10&&transition.IsTransitioning;i++)Click(step);
            Check(!transition.IsTransitioning&&previous.Source is null&&actor.Opacity==1,"Manual dissolve completion retained an old sprite/clock");
            Invoke(brain,"ChangeState",PetState.Idle); Invoke(window,"RenderActor",0d);
            Click(step); Check(transition.IsTransitioning,"New paused transition was not inspectable");
            Click(pause);
            Until(()=>!transition.IsTransitioning);
            Check(previous.Source is null&&actor.Opacity==1,"Resuming did not finish/release a paused transition");
            brain.StartEmote(PetState.Cry); Invoke(window,"RenderActor",0d);
            Check(transition.IsTransitioning,"Running raster fixture did not begin another dissolve");
            Click(pause); WaitMilliseconds(20);
            var pausedOpacity=actor.Opacity;
            WaitMilliseconds(120);
            Check(transition.IsTransitioning&&Math.Abs(actor.Opacity-pausedOpacity)<0.000001,"Pausing an active raster dissolve did not stop its clocks");
            Click(step); Pump();
            Check(actor.Opacity>pausedOpacity,"Stepping a previously running dissolve moved backwards or did not progress");
            Click(pause); Until(()=>!transition.IsTransitioning);
            Click(pause); window.SetRenderOptions(120,true);
            brain.StartEmote(PetState.Cry); Invoke(window,"RenderActor",0d); Click(step);
            Check(!transition.IsTransitioning&&!window.IsAnimating,"Reduced-motion inspection created an unwanted dissolve or resumed playback");
            Console.WriteLine("PASS Raster pose clocks freeze across wall time/hiding, advance with manual frames, complete on resume and honor reduced motion");
        }
        finally{window.Close();}
    }

    private static object Snapshot(MotionPreviewWindow w)
    {
        var b=(PetBrain)Get(w,"_brain")!;var a=(WalkAppearanceController)Get(w,"_walkAppearance")!;
        return(b.X,b.Y,b.State,b.StateElapsedMs,a.GaitPhase,((GroundedRigVisual)w.FindName("MotionRig")).PoseUpdateCount,Get(w,"_idleAppearanceElapsedMs"));
    }
    private static byte[] Pixels(MotionPreviewWindow w)
    {
        var scene=(FrameworkElement)w.FindName("Scene");w.UpdateLayout();
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(scene.ActualWidth),(int)Math.Ceiling(scene.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(scene);
        var bytes=new byte[bitmap.PixelWidth*bitmap.PixelHeight*4];bitmap.CopyPixels(bytes,bitmap.PixelWidth*4,0);return bytes;
    }
    private static void Click(Button b)=>b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void WaitMilliseconds(int ms){var watch=Stopwatch.StartNew();while(watch.ElapsedMilliseconds<ms){Pump();Thread.Sleep(2);}}
    private static void Until(Func<bool> done){var watch=Stopwatch.StartNew();while(!done()&&watch.Elapsed<TimeSpan.FromSeconds(3)){Pump();Thread.Sleep(2);}Check(done(),"Playback clock did not complete");}
    private static void Pump(){var f=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>f.Continue=false));Dispatcher.PushFrame(f);}
    private static object? Get(object o,string n)=>o.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(o);
    private static void Set(object o,string n,object v)=>o.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(o,v);
    private static object? Invoke(object o,string n,params object[] args)=>o.GetType().GetMethod(n,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(o,args);
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
}
