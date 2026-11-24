using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DesktopPet.App;
using DesktopPet.Core;

internal static class FocusPresetUiTests
{
    private const string DraftHint="입력칸만 채웠어요. 시간 적용을 눌러 반영하세요.";
    private const string Hint="입력칸만 채웁니다. 현재 타이머와 기본 시간은 바뀌지 않아요.";
    public static void Run(string root,bool capture=false)
    {
        var language=UiText.Language;UiText.SetLanguage("ko");
        var inbox=Path.Combine(root,"preset-inbox");Directory.CreateDirectory(inbox);
        using var timer=new FocusTimer(new Clock());
        var pet=new MainWindow(new CharacterLibrary(MainWindow.CharactersRoot,Path.Combine(root,"preset-custom")),timer,inbox);
        pet.Loaded-=typeof(MainWindow).GetMethod("MainWindow_Loaded",BindingFlags.Instance|BindingFlags.NonPublic)!.CreateDelegate<RoutedEventHandler>(pet);
        var preferences=new PetSettings{Visible=true,QuietMode=true,CharacterName="Companion",Language="ko",ReducedMotion=true};
        Set(pet,"_settings",preferences);Set(pet,"_brain",new PetBrain(new PlatformLayout(new[]{new Platform(0,1920,1080)}),128,122,500,958));
        Invoke(pet,"ApplyPresentation",CharacterPresentation.Load(Path.Combine(MainWindow.CharactersRoot,"Companion")));
        var save=(DispatcherTimer)Get(pet,"_settingsSaveTimer")!;save.Interval=TimeSpan.FromDays(1);
        var settings=new SettingsWindow(pet){ShowActivated=false,MaxHeight=700};
        try
        {
            settings.Show();Pump();
            var preset25=(Button)(settings.FindName("FocusPreset25Button")??throw new Exception("Duration preset controls missing"));
            var preset50=(Button)settings.FindName("FocusPreset50Button");
            var focus=(TextBox)settings.FindName("FocusMinutesBox");var rest=(TextBox)settings.FindName("BreakMinutesBox");
            var status=(TextBlock)settings.FindName("FocusDurationStatus");
            var stopped=timer.Snapshot;Click(preset50);
            Check(focus.Text=="50"&&rest.Text=="10"&&timer.Snapshot==stopped&&pet.FocusMinutes==25&&pet.BreakMinutes==5&&!(bool)Get(pet,"_settingsDirty")!,
                "Preset implicitly applied defaults, started the timer or scheduled saving");
            Click((Button)settings.FindName("FocusDurationApplyButton"));
            Check(pet.FocusMinutes==50&&pet.BreakMinutes==10&&timer.Snapshot.Remaining==TimeSpan.FromMinutes(50),"Explicit preset Apply did not update stopped defaults");
            pet.StartFocusTimer();pet.ExtendFocusTimer();var running=timer.Snapshot;Set(pet,"_settingsDirty",false);save.Stop();Click(preset25);
            Check(focus.Text=="25"&&rest.Text=="5"&&timer.Snapshot==running&&pet.FocusMinutes==50&&!(bool)Get(pet,"_settingsDirty")!,"Running preset altered the current session/defaults");
            pet.PauseOrResumeFocusTimer();var paused=timer.Snapshot;
            foreach(var lang in new[]{"en","zh-CN","ko"})
            {
                UiText.SetLanguage(lang);Click(preset50);
                Check(status.Text==UiText.Get(DraftHint)&&(string)preset25.ToolTip==UiText.Get(Hint)&&(string)preset50.ToolTip==UiText.Get(Hint),"Preset draft feedback/tooltips did not localize");
                Check(timer.Snapshot==paused&&pet.FocusMinutes==50&&pet.BreakMinutes==10&&!(bool)Get(pet,"_settingsDirty")!,"Paused preset changed preferences, countdown or save state");
                Click(preset25);var draft=(focus.Text,rest.Text);UiText.SetLanguage(lang=="ko"?"en":"ko");
                Check((focus.Text,rest.Text)==draft&&status.Text==UiText.Get(DraftHint),"Relocalizing a preset lost the pending draft or feedback");
                UiText.SetLanguage(lang);preset25.BringIntoView();Pump();settings.UpdateLayout();
                if(capture)FocusExtensionUiTests.SaveCard(settings,lang,"focus-presets");
            }
            Check(timer.Snapshot==paused&&focus.Text=="25"&&rest.Text=="5","Preset locale loop moved the paused clock");
            Click((Button)settings.FindName("FocusDurationApplyButton"));
            Check(pet.FocusMinutes==25&&pet.BreakMinutes==5&&timer.Snapshot==paused,"Applying a preset reset an already-extended paused session");
            pet.RestartFocusTimer();Check(timer.Snapshot.Remaining==TimeSpan.FromMinutes(25),"Explicit restart did not adopt the applied preset");
            Console.WriteLine("PASS 25/5 and 50/10 fill only drafts across stopped/running/paused timers; explicit Apply/restart controls defaults without changing active extensions");

            focus.Text="invalid";rest.Text="5";Click((Button)settings.FindName("FocusDurationApplyButton"));
            Check(status.Text==UiText.Get("집중 1~180분, 휴식 1~60분을 입력해주세요."),"Invalid field fixture failed");
            var before=timer.Snapshot;Set(pet,"_settingsDirty",false);save.Stop();Click(preset50);
            Check(status.Text==UiText.Get(DraftHint)&&focus.Text=="50"&&rest.Text=="10"&&timer.Snapshot==before&&pet.FocusMinutes==25
                &&!(bool)Get(pet,"_settingsDirty")!,"Preset did not replace stale validation with unapplied-draft feedback");
            settings.Close();var closed=(focus.Text,rest.Text,status.Text);Click(preset25);
            Check(closed==(focus.Text,rest.Text,status.Text)&&timer.Snapshot==before&&Directory.GetFiles(inbox,"*.json").Length==0,"Closed preset callback changed inputs/timer or published completion");
            Console.WriteLine("PASS Presets replace stale validation with localized draft feedback and closed callbacks cannot apply inputs");
        }
        finally{settings.Close();save.Stop();Set(pet,"_settingsDirty",false);pet.DisposeResources();pet.Close();UiText.SetLanguage(language);}
    }
    private sealed class Clock:TimeProvider{public override long TimestampFrequency=>TimeSpan.TicksPerSecond;public override long GetTimestamp()=>0;}
    private static void Click(Button b)=>b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static object? Get(object o,string n)=>o.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(o);
    private static void Set(object o,string n,object value)=>o.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(o,value);
    private static void Invoke(object o,string n,params object[] args)=>o.GetType().GetMethod(n,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(o,args);
    private static void Pump(){var f=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>f.Continue=false));Dispatcher.PushFrame(f);}
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
}
