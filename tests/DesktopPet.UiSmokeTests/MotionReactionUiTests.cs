using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DesktopPet.App;
using DesktopPet.Core;

internal static class MotionReactionUiTests
{
    public static void Run(string root, bool capture=false)
    {
        var language=UiText.Language;
        try
        {
            UiText.SetLanguage("ko");
            var count=0;
            var snapshots=new List<(string Label,BitmapSource Image)>();
            foreach(var name in new[]{"Companion","Nimbus","Ember","치이카와","세균맨","헬로키티","폼폼푸린","시나모롤","쿠로미"})
            {
                var folder=Path.Combine(MainWindow.CharactersRoot,name);
                if(!Directory.Exists(folder))continue;
                var presentation=CharacterPresentation.Load(folder);
                var window=new MotionPreviewWindow(presentation,1,reducedMotion:true){ShowActivated=false};
                try
                {
                    window.Show(); Pump();
                    var cry=(Button)(window.FindName("CryReactionButton")??throw new Exception("Motion preview lacks reaction controls"));
                    var angry=(Button)window.FindName("AngryReactionButton");
                    Click(window,"PausePlaybackButton");
                    foreach(var (button,state) in new[]{(cry,PetState.Cry),(angry,PetState.Angry)})
                    {
                        Click(window,"ResetSceneButton");
                        var brain=Brain(window);var position=(brain.X,brain.Y);var idle=Pixels(window);
                        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        Check(brain.State==state&&brain.StateElapsedMs==0&&!window.IsAnimating,"Reaction did not start paused with its full duration");
                        var changed=false;
                        for(var i=0;i<12;i++)
                        {
                            Click(window,"NextFrameButton");
                            var frames=presentation.Definition.GetFrames(state);
                            var expected=presentation.Frames[presentation.Definition.GetFramePath(state,frames,brain.StateElapsedMs)];
                            Check(ReferenceEquals(((Image)window.FindName("Actor")).Source,expected),name+": reaction preview displayed the wrong authored frame");
                            changed|=!idle.AsSpan().SequenceEqual(Pixels(window));
                        }
                        Check(changed&&(brain.X,brain.Y)==position&&brain.State==state&&!window.IsAnimating,name+": reaction failed to change actual pixels or moved the actor");
                        if(capture)
                        {
                            Program.SaveWindowSnapshot(window,"reaction-"+name+"-"+state+".png");
                            var drawing=new DrawingVisual();
                            using(var context=drawing.RenderOpen())context.DrawRectangle(new VisualBrush((Image)window.FindName("Actor")),null,new Rect(0,0,128,128));
                            var image=new RenderTargetBitmap(128,128,96,96,PixelFormats.Pbgra32);image.Render(drawing);image.Freeze();
                            snapshots.Add((name+" · "+state,image));
                        }
                    }
                    var before=(Brain(window).State,Brain(window).StateElapsedMs);
                    window.Close(); cry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); angry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(!cry.IsEnabled&&!angry.IsEnabled&&before==(Brain(window).State,Brain(window).StateElapsedMs),"Closed reaction controls changed simulation");
                    count++;
                }
                finally{window.Close();}
            }
            Check(count>=3,"Original character reaction coverage missing");
            if(capture)SaveOverview(snapshots);
            Console.WriteLine($"PASS Cry/Angry controls start complete reactions and display actual authored PNGs without moving the actor for {count} installed curated characters");
            VerifyQueuedReactions(CharacterPresentation.Load(Path.Combine(MainWindow.CharactersRoot,"Companion")),capture);
        }
        finally{UiText.SetLanguage(language);}
    }

    private static void VerifyQueuedReactions(CharacterPresentation presentation,bool capture)
    {
        var window=new MotionPreviewWindow(presentation,1,reducedMotion:true){ShowActivated=false};
        var control=new MotionPreviewWindow(presentation,1,reducedMotion:true){ShowActivated=false};
        try
        {
            window.Show();control.Show();Pump();
            Click(window,"PausePlaybackButton");Click(control,"PausePlaybackButton");
            Click(window,"ResetSceneButton");Click(control,"ResetSceneButton");
            Click(window,"JumpButton");Click(control,"JumpButton");
            var brain=Brain(window);var comparison=Brain(control);
            Click(window,"CryReactionButton");Click(window,"AngryReactionButton");
            Check(brain.State==PetState.Falling&&brain.StateElapsedMs==0,"Airborne reaction interrupted the throw");
            foreach(var lang in new[]{"en","zh-CN","ko"})
            {
                UiText.SetLanguage(lang);
                var expected=UiText.Format("{0} · 착지 후 {1}",UiText.Get("공중"),UiText.Get("화내기"));
                Check(((TextBlock)window.FindName("StateText")).Text==UiText.Format("{0} · 일시정지",expected)&&
                    (string)((Button)window.FindName("CryReactionButton")).Content==UiText.Get("울기")&&
                    (string)((Button)window.FindName("AngryReactionButton")).Content==UiText.Get("화내기"),"Deferred reaction feedback or controls failed live localization");
                if(capture)Program.SaveWindowSnapshot(window,"reaction-queued-"+lang+".png");
            }
            var steps=0;
            while(brain.State==PetState.Falling&&steps++<400)
            {
                Click(window,"NextFrameButton");Click(control,"NextFrameButton");
                Check(Math.Abs(brain.X-comparison.X)<0.000001&&Math.Abs(brain.Y-comparison.Y)<0.000001,"Queued reaction changed the physical trajectory");
            }
            Check(steps<400&&brain.State==PetState.Angry&&brain.StateElapsedMs<1000d/60+0.001&&Get(brain,"_queuedEmote") is null,
                "Landing did not play exactly the latest queued reaction with its full duration");
            Check(!((TextBlock)window.FindName("StateText")).Text.Contains("착지 후",StringComparison.Ordinal),"Consumed reaction kept its queued label");
            Console.WriteLine("PASS Airborne reactions preserve the control trajectory, localize queued feedback and play only the latest request after landing");

            Click(window,"ResetSceneButton");brain=Brain(window);
            var start=(brain.X,brain.Y);
            brain.BeginDrag(new PetPoint(start.X+7,start.Y+9));Set(window,"_dragging",true);
            Click(window,"CryReactionButton");
            Check(brain.State==PetState.Dragged&&(bool)Get(window,"_dragging")!&&Get(brain,"_queuedEmote") is PetState.Cry,
                "Requesting a reaction released or interrupted a drag");
            brain.UpdateDrag(new PetPoint(start.X+27,start.Y-1));
            Check(brain.X==start.X+20&&brain.Y==start.Y-10,"Reaction changed the drag offsets");
            Set(window,"_dragging",false);brain.EndDrag();
            Click(window,"ResetSceneButton");
            Check(Get(Brain(window),"_queuedEmote") is null&&!((TextBlock)window.FindName("StateText")).Text.Contains("착지 후",StringComparison.Ordinal),
                "Reset retained an old deferred reaction");
            window.Hide();Pump();var state=Brain(window).State;
            Click(window,"AngryReactionButton");Check(Brain(window).State==state,"Hidden reaction callback changed simulation");
            Console.WriteLine("PASS Reaction requests preserve dragging and offsets; reset clears queued feedback and inactive controls cannot affect the scene");
        }
        finally{window.Close();control.Close();}
    }
    private static PetBrain Brain(MotionPreviewWindow w)=>(PetBrain)Get(w,"_brain")!;
    private static void SaveOverview(List<(string Label,BitmapSource Image)> snapshots)
    {
        const int columnWidth=240,rowHeight=158;
        var height=(snapshots.Count+1)/2*rowHeight;
        var drawing=new DrawingVisual();
        using(var context=drawing.RenderOpen())
        {
            context.DrawRectangle(Brushes.WhiteSmoke,null,new Rect(0,0,columnWidth*2,height));
            for(var i=0;i<snapshots.Count;i++)
            {
                var x=i%2*columnWidth;var y=i/2*rowHeight;
                context.DrawText(new FormattedText(snapshots[i].Label,System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,new Typeface("Segoe UI, Malgun Gothic"),13,Brushes.DarkSlateGray,1),new Point(x+10,y+6));
                context.DrawImage(snapshots[i].Image,new Rect(x+52,y+26,128,128));
            }
        }
        var bitmap=new RenderTargetBitmap(columnWidth*2,height,96,96,PixelFormats.Pbgra32);bitmap.Render(drawing);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output=File.Create(Path.Combine(AppContext.BaseDirectory,"reaction-overview.png"));encoder.Save(output);
    }
    private static byte[] Pixels(MotionPreviewWindow w)
    {
        var scene=(FrameworkElement)w.FindName("Scene");w.UpdateLayout();
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(scene.ActualWidth),(int)Math.Ceiling(scene.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(scene);
        var pixels=new byte[bitmap.PixelWidth*bitmap.PixelHeight*4];bitmap.CopyPixels(pixels,bitmap.PixelWidth*4,0);return pixels;
    }
    private static void Click(MotionPreviewWindow w,string name)=>((Button)w.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Pump(){var f=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>f.Continue=false));Dispatcher.PushFrame(f);}
    private static object? Get(object o,string n)=>o.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(o);
    private static void Set(object o,string n,object v)=>o.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(o,v);
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
}
