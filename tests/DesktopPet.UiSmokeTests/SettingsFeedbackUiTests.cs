using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DesktopPet.App;
using DesktopPet.Core;

internal static class SettingsFeedbackUiTests
{
    private const string FocusError="집중 1~180분, 휴식 1~60분을 입력해주세요.";
    private const string FocusApplied="다음 시작·다시 시작부터 적용해요. 진행 중인 시간은 유지합니다.";
    private const string QuietError="00:00~23:59 사이 HH:mm 형식으로 입력해주세요.";
    private const string QuietApplied="예약을 적용했어요. 미확인 알림은 계속 보관합니다.";
    private const string QuietEqual="같은 시작·종료 시간은 예약을 활성화하지 않아요.";
    private const string QuietHelp="로컬 시간 HH:mm. 자정을 넘길 수 있고 같은 시간은 예약하지 않아요.";
    public static void Run(string root,bool capture=false)
    {
        var language=UiText.Language;UiText.SetLanguage("ko");
        var inbox=Path.Combine(root,"feedback-inbox");Directory.CreateDirectory(inbox);
        using var timer=new FocusTimer(new Clock());
        var pet=new MainWindow(new CharacterLibrary(MainWindow.CharactersRoot,Path.Combine(root,"feedback-custom")),timer,inbox);
        pet.Loaded-=typeof(MainWindow).GetMethod("MainWindow_Loaded",BindingFlags.Instance|BindingFlags.NonPublic)!.CreateDelegate<RoutedEventHandler>(pet);
        var preferences=new PetSettings{Visible=true,QuietMode=true,CharacterName="Companion",Language="ko",ReducedMotion=true};
        Set(pet,"_settings",preferences);Set(pet,"_brain",new PetBrain(new PlatformLayout(new[]{new Platform(0,1920,1080)}),128,122,500,958));
        Invoke(pet,"ApplyPresentation",CharacterPresentation.Load(Path.Combine(MainWindow.CharactersRoot,"Companion")));
        var save=(DispatcherTimer)Get(pet,"_settingsSaveTimer")!;save.Interval=TimeSpan.FromDays(1);
        var settings=new SettingsWindow(pet){ShowActivated=false,MaxHeight=700};
        var focus=(TextBlock)settings.FindName("FocusDurationStatus");var quiet=(TextBlock)settings.FindName("QuietHoursStatus");
        void Draft(string a,string b,string c,string d)
        {foreach(var pair in new[]{("FocusMinutesBox",a),("BreakMinutesBox",b),("QuietStartBox",c),("QuietEndBox",d)})((TextBox)settings.FindName(pair.Item1)).Text=pair.Item2;}
        void Apply(){Invoke(settings,"FocusDurationApply_Click",settings,new RoutedEventArgs());Invoke(settings,"QuietHoursApply_Click",settings,new RoutedEventArgs());}
        void Locales(string expectedFocus,string expectedQuiet,string stage)
        {
            foreach(var locale in new[]{"en","zh-CN","ko"})
            {
                ((ComboBox)settings.FindName("LanguageCombo")).SelectedValue=locale;settings.UpdateLayout();
                Check(focus.Text==UiText.Get(expectedFocus)&&quiet.Text==UiText.Get(expectedQuiet),"Language refresh discarded or failed to relocalize timer validation/apply feedback");
                if(capture){focus.BringIntoView();Pump();settings.UpdateLayout();FocusExtensionUiTests.SaveCard(settings,locale,"settings-feedback-"+stage);}
            }
        }
        try
        {
            settings.Show();Pump();pet.StartFocusTimer();pet.PauseOrResumeFocusTimer();var active=timer.Snapshot;
            Check(focus.Text==""&&quiet.Text==UiText.Get(QuietHelp),"Initial feedback is not the normal helper");
            Draft("abc","5","25:99","08:00");Apply();Locales(FocusError,QuietError,"error");
            Check(pet.FocusMinutes==25&&pet.BreakMinutes==5&&timer.Snapshot==active,"Invalid/translated feedback changed preferences or the active session");
            Draft("37","9","23:15","07:35");((CheckBox)settings.FindName("QuietHoursCheck")).IsChecked=true;Apply();Locales(FocusApplied,QuietApplied,"applied");
            Check(pet.FocusMinutes==37&&pet.BreakMinutes==9&&timer.Snapshot==active,"Successful feedback reset the active countdown");
            Draft("37","9","09:30","09:30");Apply();Locales(FocusApplied,QuietEqual,"equal");
            Check(timer.Snapshot==active,"Equal-range feedback advanced the paused timer");
            Console.WriteLine("PASS Timer validation, successful Apply and equal-range feedback survive real KO/EN/zh-CN language selection without changing the active session");

            var path=Path.Combine(root,"feedback-restore.json");
            PetSettingsBackup.SaveFile(path,new PetSettings{CharacterName="Companion",Visible=false,Language="en",FocusMinutes=50,BreakMinutes=10});
            PreferencesBackupUiTests.WaitFor((Task)Invoke(settings, "RestorePreferencesFromFile", path)!);
            Check(focus.Text==""&&quiet.Text==UiText.Get(QuietHelp)&&pet.FocusMinutes==50&&timer.Snapshot==active,"Validated restore kept stale Apply feedback");
            Locales("",QuietHelp,"restored");
            Draft("bad","10","invalid","08:00");Apply();File.WriteAllText(path,"invalid JSON");PreferencesBackupUiTests.WaitFor((Task)Invoke(settings, "RestorePreferencesFromFile", path)!);
            Check(focus.Text==UiText.Get(FocusError)&&quiet.Text==UiText.Get(QuietError)&&pet.FocusMinutes==50,"Rejected restore erased validation feedback");
            Locales(FocusError,QuietError,"rejected");
            settings.Close();var closed=(focus.Text,quiet.Text);var before=pet.FocusMinutes;
            Draft("99","5","01:00","02:00");Apply();UiText.SetLanguage("en");
            Check(closed==(focus.Text,quiet.Text)&&pet.FocusMinutes==before&&timer.Snapshot==active&&Directory.GetFiles(inbox,"*.json").Length==0,
                "Closed settings feedback callbacks applied input or published a completion");
            Console.WriteLine("PASS Validated restore clears old feedback, rejected restore preserves it and closed Apply/locale callbacks leave settings intact");
        }
        finally{settings.Close();save.Stop();Set(pet,"_settingsDirty",false);pet.DisposeResources();pet.Close();UiText.SetLanguage(language);}
    }
    private sealed class Clock:TimeProvider{public override long TimestampFrequency=>TimeSpan.TicksPerSecond;public override long GetTimestamp()=>0;}
    private static object? Get(object o,string n)=>o.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(o);
    private static void Set(object o,string n,object value)=>o.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(o,value);
    private static object? Invoke(object o,string n,params object[] args)=>o.GetType().GetMethod(n,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(o,args);
    private static void Pump(){var f=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>f.Continue=false));Dispatcher.PushFrame(f);}
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
}
