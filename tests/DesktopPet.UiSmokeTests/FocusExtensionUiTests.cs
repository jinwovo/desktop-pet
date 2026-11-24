using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DesktopPet.App;
using DesktopPet.Core;
using WinForms = System.Windows.Forms;

internal static class FocusExtensionUiTests
{
    public static void Run(string root, bool capture=false)
    {
        var language=UiText.Language;
        UiText.SetLanguage("ko");
        var inbox=Path.Combine(root,"extend-inbox");Directory.CreateDirectory(inbox);
        var clock=new Clock();using var timer=new FocusTimer(clock);
        var pet=new MainWindow(new CharacterLibrary(MainWindow.CharactersRoot,Path.Combine(root,"extend-custom")),timer,inbox);
        pet.Loaded-=typeof(MainWindow).GetMethod("MainWindow_Loaded",BindingFlags.Instance|BindingFlags.NonPublic)!.CreateDelegate<RoutedEventHandler>(pet);
        var preferences=new PetSettings{Visible=true,QuietMode=true,CharacterName="Companion",ReducedMotion=true};
        Set(pet,"_settings",preferences);Set(pet,"_brain",new PetBrain(new PlatformLayout(new[]{new Platform(0,1920,1080)}),128,122,500,958));
        Invoke(pet,"ApplyPresentation",CharacterPresentation.Load(Path.Combine(MainWindow.CharactersRoot,"Companion")));
        var save=(DispatcherTimer)Get(pet,"_settingsSaveTimer")!;save.Interval=TimeSpan.FromDays(1);
        var polling=(DispatcherTimer)Get(pet,"_focusTickTimer")!;
        var settings=new SettingsWindow(pet){ShowActivated=false,MaxHeight=700};
        var extend=(Button)settings.FindName("FocusExtendButton");
        using var tray=Build(pet);using var petMenu=Build(pet);
        try
        {
            settings.Show();Pump();
            Check(!extend.IsEnabled&&!pet.FocusCanExtend&&!pet.ExtendFocusTimer(),"Stopped focus accepts extra time");
            Click(extend);Check(timer.Snapshot.Status==FocusTimerStatus.Stopped,"Queued stopped button started a timer");
            Click(settings,"FocusStartButton");clock.Advance(TimeSpan.FromMinutes(3));Click(extend);
            Check(timer.Snapshot.Remaining==TimeSpan.FromMinutes(27)&&timer.Snapshot.Status==FocusTimerStatus.Running&&polling.IsEnabled,
                "Running extension ignored elapsed time or stopped polling");
            Check(((TextBlock)settings.FindName("FocusStatusText")).Text.Contains("27:00"),"Settings did not immediately display added time");
            Click(settings,"FocusPauseButton");clock.Advance(TimeSpan.FromHours(2));Click(extend);
            Check(timer.Snapshot.Remaining==TimeSpan.FromMinutes(32)&&timer.Snapshot.Status==FocusTimerStatus.Paused&&!polling.IsEnabled,
                "Paused extension consumed paused time or restarted polling");
            VerifyPreferences(pet,preferences);
            Console.WriteLine("PASS Settings adds five minutes to the active session, accounts for elapsed time and preserves pause, polling and preferences");

            foreach(var menu in new[]{tray,petMenu})
            {
                var item=OpenFocus(menu);
                var before=timer.Snapshot.Remaining;var command=item.DropDownItems.OfType<WinForms.ToolStripItem>().Single(x=>x.Text==UiText.Get("5분 추가"));
                Check(command.Enabled,"Shared focus menu did not enable current-session extension");command.PerformClick();
                Check(timer.Snapshot.Remaining==before+TimeSpan.FromMinutes(5)&&timer.Snapshot.Status==FocusTimerStatus.Paused&&!polling.IsEnabled,
                    "Shared tray/pet extension command was missing or duplicated");
            }
            foreach(var lang in new[]{"ko","en","zh-CN"})
            {
                UiText.SetLanguage(lang);settings.UpdateLayout();
                Check((string)extend.Content==UiText.Get("5분 추가")&&(string)extend.ToolTip==UiText.Get("이번 세션에만 5분을 추가해요. 기본 시간은 바뀌지 않아요."),
                    "Settings extension label/tooltip did not relocalize");
                foreach(var cached in new[]{tray,petMenu})
                    Check(OpenFocus(cached).DropDownItems.OfType<WinForms.ToolStripItem>().Any(x=>x.Text==UiText.Get("5분 추가")),"Reopened cached menu kept an old-language extension label");
                using var localized=Build(pet);var submenu=OpenFocus(localized);
                var item=submenu.DropDownItems.OfType<WinForms.ToolStripItem>().Single(x=>x.Text==UiText.Get("5분 추가"));
                Check(item.Enabled&&item.ToolTipText==UiText.Get("이번 세션에만 5분을 추가해요. 기본 시간은 바뀌지 않아요."),"Menu extension localization failed");
                var before=timer.Snapshot.Remaining;item.PerformClick();
                Check(timer.Snapshot.Remaining==before+TimeSpan.FromMinutes(5),"Localized extension command did not add exactly five minutes");
                extend.BringIntoView();Pump();settings.UpdateLayout();
                Check(extend.ActualWidth>0&&extend.ActualHeight>0,"Extension button has no visible layout");
                if(capture)SaveCard(settings,lang);
            }
            VerifyPreferences(pet,preferences);
            Console.WriteLine("PASS Shared tray/pet menus execute one extension per click and KO/EN/zh-CN settings and menu labels stay localized");

            Click(settings,"FocusRestartButton");Check(timer.Snapshot.Remaining==TimeSpan.FromMinutes(25),"Restart kept a temporary extension");
            clock.Advance(TimeSpan.FromMinutes(25));Click(extend); // Cached enabled button races the deadline.
            Check(timer.Snapshot.Status==FocusTimerStatus.Completed&&!extend.IsEnabled&&!polling.IsEnabled&&Receipts(inbox)==1,
                "Stale extension revived an expired session or hid its completion receipt");
            Check(!pet.ExtendFocusTimer()&&Receipts(inbox)==1,"Repeated expired command duplicated the receipt");
            var completedMenu=OpenFocus(petMenu);
            Check(!completedMenu.DropDownItems.OfType<WinForms.ToolStripItem>().Single(x=>x.Text==UiText.Get("5분 추가")).Enabled,
                "Completed shared menu retained an enabled extension");
            Click(settings,"FocusStartButton");Click(extend);
            Check(timer.Snapshot.Session==FocusSessionKind.Break&&timer.Snapshot.Remaining==TimeSpan.FromMinutes(10),"Manual break extension used focus defaults");
            Click(settings,"FocusRestartButton");Check(timer.Snapshot.Remaining==TimeSpan.FromMinutes(5),"Break restart kept extra time");
            Click(settings,"FocusStopButton");
            Check(!extend.IsEnabled&&timer.Snapshot.Status==FocusTimerStatus.Stopped&&Receipts(inbox)==1,"Stopping an extension produced a completion");
            Console.WriteLine("PASS Deadline races complete once without resurrection; manual breaks extend and restart with their original defaults");

            timer.ConfigureDurations(TimeSpan.FromHours(23)+TimeSpan.FromMinutes(55),TimeSpan.FromMinutes(5));
            pet.StartFocusTimer();Click(extend);
            Check(timer.Snapshot.Remaining==TimeSpan.FromDays(1)&&!extend.IsEnabled&&!pet.FocusCanExtend,"UI failed to enforce the 24-hour session cap");
            var capped=timer.Snapshot;Click(extend);Check(timer.Snapshot==capped,"Queued cap-disabled button changed the countdown");
            clock.Advance(TimeSpan.FromDays(1));
            Check(!pet.ExtendFocusTimer()&&timer.Snapshot.Status==FocusTimerStatus.Completed&&Receipts(inbox)==2,
                "Expired capped session did not emit its one completion");
            pet.StopFocusTimer();timer.ConfigureDurations(TimeSpan.FromMinutes(25),TimeSpan.FromMinutes(5));pet.StartFocusTimer();
            var closedState=timer.Snapshot;settings.Close();Click(extend);
            Check(timer.Snapshot==closedState,"Closed settings callback extended a live session");
            var staleCommands=new[]{tray,petMenu}.Select(menu=>OpenFocus(menu).DropDownItems.OfType<WinForms.ToolStripItem>().Single(x=>x.Text==UiText.Get("5분 추가"))).ToArray();
            Check(staleCommands.All(x=>x.Enabled),"Disposed-callback fixture must start with two enabled stale commands");
            pet.DisposeResources();var disposed=timer.Snapshot;
            Check(!pet.FocusCanExtend&&!pet.ExtendFocusTimer(),"Disposed pet accepts extension");
            foreach(var item in staleCommands)item.PerformClick();
            Check(timer.Snapshot==disposed&&Receipts(inbox)==2,"Disposed menu callback changed timer or notifications");
            VerifyPreferences(pet,preferences);
            Console.WriteLine("PASS Extension cap, closed settings and disposed menu callbacks preserve timers, durable receipts and user preferences");
        }
        finally{settings.Close();save.Stop();Set(pet,"_settingsDirty",false);pet.DisposeResources();pet.Close();UiText.SetLanguage(language);}
    }

    private static void VerifyPreferences(MainWindow pet,PetSettings settings)
    {
        Check(settings.FocusMinutes==25&&settings.BreakMinutes==5&&pet.FocusMinutes==25&&pet.BreakMinutes==5&&!(bool)Get(pet,"_settingsDirty")!,
            "One-session extension changed or scheduled persistence of default preferences");
    }
    private static int Receipts(string path)=>Directory.GetFiles(path,"*.json").Length;
    private static WinForms.ContextMenuStrip Build(MainWindow pet)=>(WinForms.ContextMenuStrip)typeof(App).Assembly.GetType("DesktopPet.App.PetMenuBuilder")!
        .GetMethod("Create",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,new object[]{pet,(Action)(()=>{}),(Action)(()=>{}),(Action)(()=>{})})!;
    private static WinForms.ToolStripMenuItem OpenFocus(WinForms.ContextMenuStrip menu)
    {
        // The live builder's real DropDownOpening handler updates a cached menu on each opening.
        var item=menu.Items.OfType<WinForms.ToolStripMenuItem>().Single(x=>x.DropDownItems.Count>0&&!x.DropDownItems[0].Enabled);
        item.ShowDropDown();
        item.HideDropDown();
        return item;
    }
    internal static void SaveCard(SettingsWindow window,string language,string prefix="focus-extension")
    {
        DependencyObject node=(TextBlock)window.FindName("FocusStatusText");
        while(node is not Border)node=VisualTreeHelper.GetParent(node);
        var card=(FrameworkElement)node;
        var width=(int)Math.Ceiling(card.ActualWidth);var height=(int)Math.Ceiling(card.ActualHeight);
        var drawing=new DrawingVisual();
        using(var context=drawing.RenderOpen())context.DrawRectangle(new VisualBrush(card),null,new Rect(0,0,width,height));
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(drawing);
        var pixels=new byte[width*height*4];bitmap.CopyPixels(pixels,width*4,0);
        Check(pixels.Where((value,index)=>index%4==3&&value>0).Any(),"Focus card capture is transparent instead of showing the actual controls");
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output=File.Create(Path.Combine(AppContext.BaseDirectory,prefix+"-"+language+".png"));encoder.Save(output);
    }
    private sealed class Clock:TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency=>TimeSpan.TicksPerSecond;
        public override long GetTimestamp()=>_ticks;
        public void Advance(TimeSpan elapsed)=>_ticks+=elapsed.Ticks;
    }
    private static void Click(SettingsWindow w,string name)=>Click((Button)w.FindName(name));
    private static void Click(Button button)=>button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static object? Get(object o,string n)=>o.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(o);
    private static void Set(object o,string n,object v)=>o.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(o,v);
    private static void Invoke(object o,string n,params object[] args)=>o.GetType().GetMethod(n,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(o,args);
    private static void Pump(){var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);}
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
}
