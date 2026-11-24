using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Line = System.Windows.Shapes.Line;
using System.Windows.Threading;
using DesktopPet.App;
using DesktopPet.Core;

internal static class MotionGuideUiTests
{
    public static void Run(string root, bool capture=false)
    {
        var language=UiText.Language;
        try
        {
            var count=0;
            foreach(var name in new[]{"Companion","Nimbus","Ember","치이카와","세균맨","헬로키티","폼폼푸린","시나모롤","쿠로미"})
            {
                var folder=Path.Combine(MainWindow.CharactersRoot,name);if(!Directory.Exists(folder))continue;
                BitmapSource? saved=null;
                var presentation=CharacterPresentation.Load(folder);
                var window=new MotionPreviewWindow(presentation,1,reducedMotion:true,chooseFramePath:()=>"unused.png",
                    frameWriter:(frame,_,_)=>{saved=frame;return Task.CompletedTask;});
                try
                {
                    window.Show();Pump();Click(window,"PausePlaybackButton");
                    var toggle=(CheckBox)window.FindName("FrameGuidesToggle");var guides=(Canvas)window.FindName("FrameGuides");
                    Check(toggle.IsChecked!=true&&guides.Visibility==Visibility.Collapsed&&!guides.IsHitTestVisible,"Guides should start off and never intercept pet input");
                    var before=Pixels(window);var state=State(window);
                    toggle.IsChecked=true;
                    Check(guides.Visibility==Visibility.Visible&&state.Equals(State(window)),"Showing guides changed the simulation or live-rig pose");
                    Check(!before.AsSpan().SequenceEqual(Pixels(window)),"Guide toggle did not draw any pixels");
                    VerifyPosition(window,presentation);
                    Click(window,"SaveSceneButton");
                    Check(saved is {IsFrozen:true}&&Pixels(window).AsSpan().SequenceEqual(Bytes(saved)),"Scene PNG snapshot omitted visible guides");
                    toggle.IsChecked=false;
                    Check(before.AsSpan().SequenceEqual(Pixels(window))&&state.Equals(State(window)),"Hiding guides changed or retained pixels on the inspected pose");
                    foreach(var direction in new[]{1,-1})
                    {
                        Click(window,"ResetSceneButton");Set(Brain(window),"_walkTargetDirection",direction);
                        Invoke(Brain(window),"ChangeState",PetState.Walk);Invoke(window,"RenderActor",0d);
                        for(var i=0;i<10;i++)Click(window,"NextFrameButton");
                        state=State(window);toggle.IsChecked=true;VerifyPosition(window,presentation);
                        Check(state.Equals(State(window)),"Walk guide visibility advanced the gait");
                        Click(window,"NextFrameButton");VerifyPosition(window,presentation);
                        toggle.IsChecked=false;
                    }
                    toggle.IsChecked=true;
                    if(name=="Companion")
                    {
                        foreach(var lang in new[]{"ko","en","zh-CN"})
                        {
                            UiText.SetLanguage(lang);state=State(window);window.UpdateLayout();
                            Check((string)toggle.Content==UiText.Get("기준선")&&(string)toggle.ToolTip==UiText.Get("점선은 프레임, 십자는 선언된 기준점입니다."),"Guide labels did not relocalize");
                            VerifyPosition(window,presentation);Check(state.Equals(State(window)),"Guide localization changed the pose");
                            if(capture)Program.SaveWindowSnapshot(window,"motion-guides-"+lang+".png");
                        }
                    }
                    Click(window,"ResetSceneButton");VerifyPosition(window,presentation);
                    Check(toggle.IsChecked==true&&guides.Visibility==Visibility.Visible&&!window.IsAnimating,"Reset lost guide/pause preferences");
                    foreach(var minimize in new[]{false,true})
                    {
                        if(minimize)window.WindowState=WindowState.Minimized;else window.Hide();Pump();state=State(window);
                        toggle.RaiseEvent(new RoutedEventArgs(ToggleButton.CheckedEvent));
                        Check(!toggle.IsEnabled&&state.Equals(State(window)),"Inactive guide input changed the simulation");
                        if(minimize)window.WindowState=WindowState.Normal;else window.Show();Pump();VerifyPosition(window,presentation);
                    }
                    Brain(window).BeginDrag(new PetPoint(Brain(window).X+10,Brain(window).Y+10));Click(window,"CryReactionButton");
                    state=State(window);toggle.IsChecked=false;toggle.IsChecked=true;
                    Check(Brain(window).State==PetState.Dragged&&state.Equals(State(window)),"Guides released a drag or consumed a queued reaction");
                    Brain(window).EndDrag(180,-360);Invoke(window,"RenderActor",0d);state=State(window);
                    toggle.IsChecked=false;toggle.IsChecked=true;
                    Check(Brain(window).State==PetState.Falling&&state.Equals(State(window)),"Guides changed a throw");
                    window.Close();state=State(window);toggle.RaiseEvent(new RoutedEventArgs(ToggleButton.CheckedEvent));
                    Check(!toggle.IsEnabled&&state.Equals(State(window)),"Closed guide callback changed simulation");
                    count++;
                }
                finally{window.Close();}
            }
            Check(count>=3,"Original character guide coverage missing");
            Console.WriteLine($"PASS Optional guides draw/export without changing motion for {count} curated characters, follow both walk directions and preserve drag, throw, reset and lifecycle");
            VerifyNonSquare(root);
        }
        finally{UiText.SetLanguage(language);}
    }
    private static void VerifyNonSquare(string root)
    {
        var folder=Path.Combine(root,"guide-wide");Directory.CreateDirectory(folder);
        var pixels=new byte[160*80*4];for(var i=0;i<pixels.Length;i+=4){pixels[i]=50;pixels[i+1]=100;pixels[i+2]=200;pixels[i+3]=255;}
        var source=BitmapSource.Create(160,80,96,96,PixelFormats.Bgra32,null,pixels,160*4);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(source));using(var file=File.Create(Path.Combine(folder,"idle.png")))encoder.Save(file);
        File.WriteAllText(Path.Combine(folder,"character.json"),System.Text.Json.JsonSerializer.Serialize(new
        {name="Wide Guides",frameSizePx=new{width=160,height=80},anchor=new{x=18,y=70},frames=new{idle=new[]{"idle.png"}}}));
        var presentation=CharacterPresentation.Load(folder);var window=new MotionPreviewWindow(presentation,1,reducedMotion:true);
        try
        {
            window.Show();Pump();Click(window,"PausePlaybackButton");((CheckBox)window.FindName("FrameGuidesToggle")).IsChecked=true;
            VerifyPosition(window,presentation);((ScaleTransform)window.FindName("ActorFlip")).ScaleX=-1;
            VerifyPosition(window,presentation);
            Check(Math.Abs(((Line)window.FindName("AnchorVertical")).X1-10.8)<0.00001,"Asymmetric metadata anchor was replaced with the frame center");
            Check(Math.Abs(((FrameworkElement)window.FindName("FrameGuides")).Height-48)<0.00001,"Non-square guide frame was stretched");
            Console.WriteLine("PASS Non-square guides preserve frame proportions and the declared asymmetric anchor in frame coordinates");
        }
        finally{window.Close();}
    }
    private static void VerifyPosition(MotionPreviewWindow window,CharacterPresentation presentation)
    {
        var guides=(Canvas)window.FindName("FrameGuides");var actor=(Image)window.FindName("Actor");var baseline=(Line)window.FindName("FrameBaseline");
        Check(Math.Abs(Canvas.GetLeft(guides)-Brain(window).X)<0.00001&&Math.Abs(Canvas.GetTop(guides)-Brain(window).Y)<0.00001,"Guides lag behind the displayed actor");
        var scale=96d/Math.Max(presentation.Definition.FrameWidth,presentation.Definition.FrameHeight);
        Check(guides.Width==actor.Width&&guides.Height==actor.Height&&Math.Abs(baseline.Y1-presentation.Definition.AnchorY*scale)<0.00001,"Frame/anchor guide geometry does not match metadata");
    }
    private static object State(MotionPreviewWindow w)
    {
        var b=Brain(w);var a=(WalkAppearanceController)Get(w,"_walkAppearance")!;
        return(b.X,b.Y,b.State,b.StateElapsedMs,a.GaitPhase,((GroundedRigVisual)w.FindName("MotionRig")).PoseUpdateCount);
    }
    private static byte[] Pixels(MotionPreviewWindow w)
    {
        w.UpdateLayout();var scene=(FrameworkElement)w.FindName("Scene");
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(scene.ActualWidth),(int)Math.Ceiling(scene.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(scene);return Bytes(bitmap);
    }
    private static byte[] Bytes(BitmapSource b){var bytes=new byte[b.PixelWidth*b.PixelHeight*4];b.CopyPixels(bytes,b.PixelWidth*4,0);return bytes;}
    private static PetBrain Brain(MotionPreviewWindow w)=>(PetBrain)Get(w,"_brain")!;
    private static object? Get(object o,string n)=>o.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(o);
    private static void Set(object o,string n,object value)=>o.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(o,value);
    private static void Invoke(object o,string n,params object[] args)=>o.GetType().GetMethod(n,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(o,args);
    private static void Click(MotionPreviewWindow w,string n)=>((Button)w.FindName(n)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Pump(){var f=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>f.Continue=false));Dispatcher.PushFrame(f);}
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
}
