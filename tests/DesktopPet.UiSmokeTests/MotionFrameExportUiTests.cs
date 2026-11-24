using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DesktopPet.App;
using DesktopPet.Core;

internal static class MotionFrameExportUiTests
{
    public static void Run(string root, bool capture = false)
    {
        var language = UiText.Language;
        var output = Path.Combine(root, "scene-png"); Directory.CreateDirectory(output);
        try
        {
            var count = 0;
            foreach (var name in new[] { "Companion", "Nimbus", "Ember", "치이카와", "세균맨", "헬로키티", "폼폼푸린", "시나모롤", "쿠로미" })
            {
                var folder = Path.Combine(MainWindow.CharactersRoot, name);
                if (!Directory.Exists(folder)) continue;
                string? destination = null;
                var window = new MotionPreviewWindow(CharacterPresentation.Load(folder), 1, reducedMotion: true, chooseFramePath: () => destination);
                try
                {
                    window.Show(); Pump(); Click(window, "PausePlaybackButton");
                    foreach (var pose in new[] { "cry", "walk" })
                    {
                        Click(window, "ResetSceneButton");
                        if (pose == "cry") Click(window, "CryReactionButton");
                        else
                        {
                            Set(Brain(window), "_walkTargetDirection", 1);
                            Invoke(Brain(window), "ChangeState", PetState.Walk);
                            Invoke(window, "RenderActor", 0d);
                            for (var i = 0; i < 8; i++) Click(window, "NextFrameButton");
                            Check(((FrameworkElement)window.FindName("MotionRig")).Visibility == Visibility.Visible, "Walk export is not exercising the live rig");
                        }
                        var before = Capture(window); var state = State(window);
                        destination = Path.Combine(output, $"scene-{count}-{pose}.png");
                        Click(window, "SaveSceneButton"); Wait(window.FrameSaveOperation);
                        Check(File.Exists(destination), "Scene export did not create a PNG");
                        using var input = File.OpenRead(destination);
                        var decoded = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                        SamePixels(before, decoded);
                        Check(state.Equals(State(window)) && !window.IsAnimating, "Saving changed a paused scene or simulation");
                        if (capture && name == "Companion" && pose == "walk") File.Copy(destination, Path.Combine(AppContext.BaseDirectory, "scene-export-walk.png"), true);
                    }
                    count++;
                }
                finally { window.Close(); }
            }
            Check(count >= 3, "Original character scene export coverage missing");
            Check(!Directory.EnumerateFiles(output, "*.partial").Any(), "Successful export leaked partial files");
            Console.WriteLine($"PASS Scene PNG round-trips actual paused Cry and live-rig walk pixels for {count} curated characters without changing simulation");
            VerifyLifecycle(output, capture);
            VerifyRasterBlend(root, output);
            VerifyWriter(output);
        }
        finally { UiText.SetLanguage(language); }
    }

    private static void VerifyLifecycle(string output, bool capture)
    {
        var presentation = CharacterPresentation.Load(Path.Combine(MainWindow.CharactersRoot, "Companion"));
        var writes = 0; var picks = 0;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken workerToken = default;
        BitmapSource? saved = null;
        MotionPreviewWindow window = null!;
        window = new MotionPreviewWindow(presentation, 1, chooseFramePath: () =>
        {
            picks++;
            // A modal picker pumps messages. Its later pose must not replace the clicked frame.
            Click(window, "AngryReactionButton");
            return Path.Combine(output, "blocked.png");
        }, frameWriter: async (source, _, token) => { writes++; saved = source; workerToken = token; await gate.Task; throw new IOException("Late worker failure"); });
        try
        {
            window.Show(); Pump();
            var expected = Capture(window);
            Click(window, "SaveSceneButton");
            Check(writes == 1 && picks == 1 && saved?.IsFrozen == true && window.IsAnimating, "Scene save blocked playback or did not freeze its snapshot");
            SamePixels(expected, saved!);
            var button = (Button)window.FindName("SaveSceneButton");
            Check(!button.IsEnabled, "Save stayed enabled during background work");
            Click(window, "SaveSceneButton");
            Check(writes == 1 && picks == 1, "Duplicate save opened another picker/worker");
            Click(window, "PausePlaybackButton"); Click(window, "NextFrameButton");
            foreach (var lang in new[] { "ko", "en", "zh-CN" })
            {
                UiText.SetLanguage(lang); window.UpdateLayout();
                Check((string)button.Content == UiText.Get("저장 중…") && (string)button.ToolTip == UiText.Get("장면을 저장하는 중이에요."), "Busy export status did not relocalize");
                var scene = (FrameworkElement)window.FindName("Scene");
                if (capture) Program.SaveWindowSnapshot(window, "scene-export-" + lang + ".png");
                Check(scene.ActualHeight > 200 && button.ActualWidth + button.Margin.Left + button.Margin.Right >= button.DesiredSize.Width, $"Export controls cramped the scene or clipped their label: {lang}, scene={scene.ActualHeight}");
            }
            var status = button.ToolTip;
            window.Close();
            Check(workerToken.IsCancellationRequested && !button.IsEnabled, "Closing export owner did not cancel and disable commands");
            gate.SetResult(); Wait(window.FrameSaveOperation);
            Check(Equals(status, button.ToolTip) && !button.IsEnabled, "Late export failure changed a closed window");
        }
        finally { window.Close(); gate.TrySetResult(); Wait(window.FrameSaveOperation); }

        foreach (var closePicker in new[] { false, true })
        {
            MotionPreviewWindow picker = null!;
            picker = new MotionPreviewWindow(presentation, 1,
                chooseFramePath: () => { if (closePicker) picker.Close(); return closePicker ? Path.Combine(output,"closed.png") : null; },
                frameWriter: (_, _, _) => { writes++; return Task.CompletedTask; });
            try { picker.Show(); Pump(); var before = writes; Click(picker,"SaveSceneButton"); Check(writes == before, "Cancelled/closed picker started output"); }
            finally { picker.Close(); }
        }

        var inactiveCalls = 0;
        var inactive = new MotionPreviewWindow(presentation, 1, chooseFramePath: () => { inactiveCalls++; return null; });
        try
        {
            inactive.Show(); Pump(); inactive.Hide(); Pump(); Click(inactive,"SaveSceneButton");
            inactive.Show(); Pump(); inactive.WindowState=WindowState.Minimized; Pump(); Click(inactive,"SaveSceneButton");
            inactive.Close(); Click(inactive,"SaveSceneButton");
            Check(inactiveCalls==0,"Inactive/closed save callback opened a picker");
        }
        finally { inactive.Close(); }

        var protectedPath = Path.Combine(output, "protected.png"); File.WriteAllText(protectedPath, "KEEP EXISTING FILE");
        var invalidPath = protectedPath;
        var retry = new MotionPreviewWindow(presentation, 1, chooseFramePath: () => invalidPath);
        try
        {
            retry.Show(); Pump(); Click(retry, "PausePlaybackButton");
            foreach (var lang in new[] { "ko", "en", "zh-CN" })
            {
                UiText.SetLanguage(lang); Click(retry,"SaveSceneButton"); Wait(retry.FrameSaveOperation);
                var button = (Button)retry.FindName("SaveSceneButton");
                Check((string)button.Content == UiText.Get("PNG 다시 저장") && button.IsEnabled, "Existing file did not show a localized retry");
                Check(File.ReadAllText(protectedPath) == "KEEP EXISTING FILE", "Scene save replaced an existing file");
            }
            invalidPath = Path.Combine(output,"wrong.txt"); Click(retry,"SaveSceneButton"); Wait(retry.FrameSaveOperation);
            Check(!File.Exists(invalidPath), "Scene save accepted a non-PNG filename");
            invalidPath = Path.Combine(output,"retry.png"); Click(retry,"SaveSceneButton"); Wait(retry.FrameSaveOperation);
            Check(File.Exists(invalidPath) && (string)((Button)retry.FindName("SaveSceneButton")).Content == UiText.Get("PNG 저장됨"), "Failed export could not be retried");
        }
        finally { retry.Close(); }
        Check(!Directory.EnumerateFiles(output,"*.partial").Any(), "Failed export leaked partial files");
        Console.WriteLine("PASS Scene snapshot precedes the modal picker; save remains responsive, localizes busy/error states, protects existing files and cancels closed owners");
    }

    private static void VerifyRasterBlend(string root, string output)
    {
        var folder=Path.Combine(root,"scene-raster");Directory.CreateDirectory(folder);
        var source=CharacterPresentation.Load(Path.Combine(MainWindow.CharactersRoot,"Companion"));
        File.Copy(source.Definition.GetFrames(PetState.Idle)[0],Path.Combine(folder,"idle.png"));
        File.Copy(source.Definition.GetFrames(PetState.Cry)[0],Path.Combine(folder,"cry.png"));
        File.WriteAllText(Path.Combine(folder,"character.json"),System.Text.Json.JsonSerializer.Serialize(new
        {name="PNG Raster",frameSizePx=new{width=128,height=128},anchor=new{x=64,y=122},frames=new{idle=new[]{"idle.png"},cry=new[]{"cry.png"}}}));
        var target=Path.Combine(output,"blend.png");
        var window=new MotionPreviewWindow(CharacterPresentation.Load(folder),1,chooseFramePath:()=>target);
        try
        {
            window.Show();Pump();Click(window,"PausePlaybackButton");Click(window,"CryReactionButton");
            Click(window,"NextFrameButton");Click(window,"NextFrameButton");Pump();
            var actor=(Image)window.FindName("Actor");var previous=(Image)window.FindName("PreviousActor");
            Check(actor.Opacity>0&&actor.Opacity<1&&previous.Source is not null,"Raster export did not exercise a blended pose");
            var flip=(ScaleTransform)window.FindName("ActorFlip");flip.ScaleX=-1;
            var expected=Capture(window);var opacity=actor.Opacity;var state=State(window);
            Click(window,"SaveSceneButton");Wait(window.FrameSaveOperation);
            using var input=File.OpenRead(target);
            SamePixels(expected,BitmapDecoder.Create(input,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad).Frames[0]);
            Check(Math.Abs(actor.Opacity-opacity)<0.000001&&state.Equals(State(window))&&previous.Source is not null,"Export advanced or replaced the inspected blend");
            Console.WriteLine("PASS Scene PNG preserves both raster dissolve layers and the current facing transform");
        }
        finally{window.Close();}
    }

    private static void VerifyWriter(string output)
    {
        var method = typeof(MotionPreviewWindow).Assembly.GetType("DesktopPet.App.MotionFramePngWriter")!.GetMethod("Save")!;
        var bitmap = BitmapSource.Create(4,4,96,96,PixelFormats.Bgra32,null,new byte[64],16);
        var path = Path.Combine(output,"cancelled.png");
        Exception? failure = null;
        try { method.Invoke(null, new object[]{bitmap,path,CancellationToken.None}); }
        catch (TargetInvocationException ex) { failure=ex.InnerException; }
        Check(failure is ArgumentException && !File.Exists(path), "Unfrozen frame reached the worker encoder");
        bitmap.Freeze();
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); failure=null;
        try { method.Invoke(null, new object[]{bitmap,path,cancelled.Token}); }
        catch (TargetInvocationException ex) { failure=ex.InnerException; }
        Check(failure is OperationCanceledException && !File.Exists(path) && !Directory.EnumerateFiles(output,"*.partial").Any(), "Pre-cancelled save produced output or leaked a partial");
        failure=null;
        var tooLong=Path.Combine(output,new string('x',300)+".png");
        try { method.Invoke(null,new object[]{bitmap,tooLong,CancellationToken.None}); }
        catch (TargetInvocationException ex) { failure=ex.InnerException; }
        Check(failure is IOException && !Directory.EnumerateFiles(output,"*.partial").Any(),"Failed PNG publication leaked its private partial");
        Console.WriteLine("PASS PNG writer rejects unfrozen/cancelled work and cleans partial output after a failed publication");
    }

    private static BitmapSource Capture(MotionPreviewWindow window)
    {
        window.UpdateLayout(); var scene=(FrameworkElement)window.FindName("Scene");
        var image=new RenderTargetBitmap((int)Math.Ceiling(scene.ActualWidth),(int)Math.Ceiling(scene.ActualHeight),96,96,PixelFormats.Pbgra32);
        image.Render(scene); image.Freeze(); return image;
    }
    private static void SamePixels(BitmapSource first, BitmapSource second)
    {
        Check(first.PixelWidth==second.PixelWidth && first.PixelHeight==second.PixelHeight,"Saved image includes something outside the scene");
        var a=new FormatConvertedBitmap(first,PixelFormats.Pbgra32,null,0);var b=new FormatConvertedBitmap(second,PixelFormats.Pbgra32,null,0);
        var stride=first.PixelWidth*4;var x=new byte[stride*first.PixelHeight];var y=new byte[x.Length];a.CopyPixels(x,stride,0);b.CopyPixels(y,stride,0);
        for(var i=0;i<x.Length;i++)Check(Math.Abs(x[i]-y[i])<=1,"Saved scene changed captured pixels");
    }
    private static (PetState,double,double,double) State(MotionPreviewWindow w)=>(Brain(w).State,Brain(w).X,Brain(w).Y,Brain(w).StateElapsedMs);
    private static PetBrain Brain(MotionPreviewWindow w)=>(PetBrain)w.GetType().GetField("_brain",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(w)!;
    private static void Set(object o,string n,object value)=>o.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(o,value);
    private static void Invoke(object o,string n,params object[] args)=>o.GetType().GetMethod(n,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(o,args);
    private static void Click(MotionPreviewWindow w,string name)=>((Button)w.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Wait(Task task){var clock=Stopwatch.StartNew();while(!task.IsCompleted&&clock.Elapsed<TimeSpan.FromSeconds(20)){Pump();Thread.Sleep(1);}Check(task.IsCompleted,"Scene save timed out");task.GetAwaiter().GetResult();}
    private static void Pump(){var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);}
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
}
