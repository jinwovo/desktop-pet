using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DesktopPet.App;
using DesktopPet.Core;
using WinForms=System.Windows.Forms;

internal static class FocusCountUiTests
{
    public static void Run(string root,bool capture=false)
    {
        var language=UiText.Language;UiText.SetLanguage("ko");
        var inbox=Path.Combine(root,"count-inbox");Directory.CreateDirectory(inbox);
        var clock=new Clock();using var timer=new FocusTimer(clock,TimeSpan.FromMinutes(1),TimeSpan.FromMinutes(1));
        var pet=new MainWindow(new CharacterLibrary(MainWindow.CharactersRoot,Path.Combine(root,"count-custom")),timer,inbox);
        pet.Loaded-=typeof(MainWindow).GetMethod("MainWindow_Loaded",BindingFlags.Instance|BindingFlags.NonPublic)!.CreateDelegate<RoutedEventHandler>(pet);
        var preferences=new PetSettings{Visible=true,QuietMode=true,CharacterName="Companion",ReducedMotion=true,FocusMinutes=1,BreakMinutes=1};
        Set(pet,"_settings",preferences);Set(pet,"_brain",new PetBrain(new PlatformLayout(new[]{new Platform(0,1920,1080)}),128,122,500,958));
        Invoke(pet,"ApplyPresentation",CharacterPresentation.Load(Path.Combine(MainWindow.CharactersRoot,"Companion")));
        var save=(DispatcherTimer)Get(pet,"_settingsSaveTimer")!;save.Interval=TimeSpan.FromDays(1);
        var settings=new SettingsWindow(pet){ShowActivated=false,MaxHeight=700};
        var reset=(Button)settings.FindName("FocusResetCountButton");var label=(TextBlock)settings.FindName("FocusCountText");
        using var tray=Build(pet);using var petMenu=Build(pet);
        void Advance(TimeSpan elapsed){clock.Advance(elapsed);Invoke(pet,"UpdateFocusTimer");}
        void CompleteFocus(){pet.StopFocusTimer();pet.StartFocusTimer();Advance(TimeSpan.FromMinutes(1));}
        try
        {
            settings.Show();Pump();
            Check(!reset.IsEnabled&&!pet.FocusCanResetCount&&!pet.ResetFocusCount()&&label.Text==UiText.Format("완료한 집중 {0}회 · 이번 실행",0),"Initial run has a nonzero/resettable count");
            pet.StartFocusTimer();Advance(TimeSpan.FromMinutes(1));
            Check(timer.Snapshot.CompletedFocusSessions==1&&reset.IsEnabled&&label.Text==pet.FocusCountCaption&&Receipts(inbox)==1,"Completed focus did not update its count/receipt");
            pet.RestartFocusTimer();Advance(TimeSpan.FromSeconds(15));pet.PauseOrResumeFocusTimer();pet.ExtendFocusTimer();
            var paused=timer.Snapshot;var receiptFiles=Directory.GetFiles(inbox,"*.json").ToDictionary(path=>path,File.ReadAllBytes);
            foreach(var lang in new[]{"ko","en","zh-CN"})
            {
                UiText.SetLanguage(lang);settings.UpdateLayout();
                Check(label.Text==UiText.Format("완료한 집중 {0}회 · 이번 실행",1)&&(string)reset.Content==UiText.Get("횟수 초기화"),"Focus count did not relocalize");
                foreach(var menu in new[]{tray,petMenu})
                {
                    var focus=OpenFocus(menu);
                    Check(focus.DropDownItems[1].Text==pet.FocusCountCaption&&!focus.DropDownItems[1].Enabled&&ResetItem(focus).Enabled,"Shared menu count is stale or interactive");
                    Check(ResetItem(focus).ToolTipText==UiText.Get("앱을 닫으면 사라지는 횟수예요. 초기화해도 타이머와 알림은 유지해요."),"Shared reset tooltip did not localize");
                }
                Check(timer.Snapshot==paused,"Count localization advanced or changed the paused timer");
                reset.BringIntoView();Pump();settings.UpdateLayout();
                if(capture)FocusExtensionUiTests.SaveCard(settings,lang,"focus-count");
            }
            Invoke(pet,"SynchronizeFocusTimer"); // Reconcile the current language before testing a count-only change.
            var changes=0;pet.FocusTimerChanged+=()=>changes++;
            Click(reset);
            Check(timer.Snapshot==(paused with{CompletedFocusSessions=0})&&changes==1&&label.Text==pet.FocusCountCaption&&!reset.IsEnabled,
                "A count-only reset did not refresh Settings immediately or altered the timer");
            Check(!((DispatcherTimer)Get(pet,"_focusTickTimer")!).IsEnabled,"Count reset restarted paused polling");
            foreach(var entry in receiptFiles)Check(File.ReadAllBytes(entry.Key).AsSpan().SequenceEqual(entry.Value),"Count reset acknowledged or rewrote a pending receipt");
            Check(Receipts(inbox)==receiptFiles.Count,"Count reset added/removed receipts");
            Click(reset);Check(changes==1,"An already-zero reset emitted another UI update");
            Console.WriteLine("PASS Completed focus count localizes in Settings/shared menus; count-only reset refreshes immediately and preserves paused extensions and unread receipts");

            pet.PauseOrResumeFocusTimer();Advance(paused.Remaining);
            Check(timer.Snapshot.CompletedFocusSessions==1,"Completion after reset did not count from zero");
            pet.StartFocusTimer();Check(timer.Snapshot.Session==FocusSessionKind.Break,"Break was not selected manually");Advance(TimeSpan.FromMinutes(1));
            Check(timer.Snapshot.CompletedFocusSessions==1,"A break incremented completed focus count");
            foreach(var menu in new[]{tray,petMenu})
            {
                CompleteFocus();var before=timer.Snapshot;var files=Receipts(inbox);
                ResetItem(OpenFocus(menu)).PerformClick();
                Check(timer.Snapshot==(before with{CompletedFocusSessions=0})&&Receipts(inbox)==files&&label.Text==pet.FocusCountCaption,"Tray/pet count reset changed completion state or notifications");
            }
            Check(!(bool)Get(pet,"_settingsDirty")!&&preferences.FocusMinutes==1&&preferences.BreakMinutes==1,"Count reset saved or changed timer defaults");
            Console.WriteLine("PASS Only focus completions increase the app-run count; both tray and pet reset commands preserve completed sessions and saved defaults");

            CompleteFocus();pet.RestartFocusTimer();var beforeClose=timer.Snapshot;
            var stale=new[]{tray,petMenu}.Select(menu=>ResetItem(OpenFocus(menu))).ToArray();Check(stale.All(x=>x.Enabled),"Need enabled stale reset commands");
            settings.Close();Click(reset);Check(timer.Snapshot==beforeClose,"Closed settings reset the live counter");
            pet.DisposeResources();var disposed=timer.Snapshot;var pending=Receipts(inbox);
            Check(!pet.FocusCanResetCount&&!pet.ResetFocusCount(),"Disposed pet accepts count reset");foreach(var item in stale)item.PerformClick();
            Check(timer.Snapshot==disposed&&Receipts(inbox)==pending,"Disposed menu reset changed count or receipts");
            Console.WriteLine("PASS Count reset ignores closed Settings and both enabled stale menu callbacks after disposal");
        }
        finally{settings.Close();save.Stop();Set(pet,"_settingsDirty",false);pet.DisposeResources();pet.Close();UiText.SetLanguage(language);}
    }
    private static int Receipts(string path)=>Directory.GetFiles(path,"*.json").Length;
    private static WinForms.ContextMenuStrip Build(MainWindow pet)=>(WinForms.ContextMenuStrip)typeof(App).Assembly.GetType("DesktopPet.App.PetMenuBuilder")!
        .GetMethod("Create",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,new object[]{pet,(Action)(()=>{}),(Action)(()=>{}),(Action)(()=>{})})!;
    private static WinForms.ToolStripMenuItem OpenFocus(WinForms.ContextMenuStrip menu)
    {
        var item=menu.Items.OfType<WinForms.ToolStripMenuItem>().Single(x=>x.DropDownItems.Count>0&&!x.DropDownItems[0].Enabled);
        item.ShowDropDown();item.HideDropDown();return item;
    }
    private static WinForms.ToolStripItem ResetItem(WinForms.ToolStripMenuItem menu)=>menu.DropDownItems.OfType<WinForms.ToolStripItem>().Single(x=>x.Text==UiText.Get("횟수 초기화"));
    private sealed class Clock:TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency=>TimeSpan.TicksPerSecond;
        public override long GetTimestamp()=>_ticks;
        public void Advance(TimeSpan value)=>_ticks+=value.Ticks;
    }
    private static void Click(Button b)=>b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static object? Get(object o,string n)=>o.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(o);
    private static void Set(object o,string n,object value)=>o.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(o,value);
    private static void Invoke(object o,string n,params object[] args)=>o.GetType().GetMethod(n,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(o,args);
    private static void Pump(){var f=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>f.Continue=false));Dispatcher.PushFrame(f);}
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
}
