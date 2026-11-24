using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DesktopPet.App;
using DesktopPet.Core;

internal static class SettingsDraftLocaleUiTests
{
    public static void Run(string root)
    {
        var language=UiText.Language;UiText.SetLanguage("ko");
        var inbox=Path.Combine(root,"draft-inbox");Directory.CreateDirectory(inbox);
        using var timer=new FocusTimer(new Clock());
        var pet=new MainWindow(new CharacterLibrary(MainWindow.CharactersRoot,Path.Combine(root,"draft-custom")),timer,inbox);
        pet.Loaded-=typeof(MainWindow).GetMethod("MainWindow_Loaded",BindingFlags.Instance|BindingFlags.NonPublic)!.CreateDelegate<RoutedEventHandler>(pet);
        var preferences=new PetSettings{Visible=true,QuietMode=true,CharacterName="Companion",Language="ko",ReducedMotion=true};
        Set(pet,"_settings",preferences);Set(pet,"_brain",new PetBrain(new PlatformLayout(new[]{new Platform(0,1920,1080)}),128,122,500,958));
        Invoke(pet,"ApplyPresentation",CharacterPresentation.Load(Path.Combine(MainWindow.CharactersRoot,"Companion")));
        var save=(DispatcherTimer)Get(pet,"_settingsSaveTimer")!;save.Interval=TimeSpan.FromDays(1);
        var settings=new SettingsWindow(pet){ShowActivated=false};
        var names=new[]{"FocusMinutesBox","BreakMinutesBox","QuietStartBox","QuietEndBox"};
        var boxes=names.Select(x=>(TextBox)settings.FindName(x)).ToArray();
        void Draft(params string[] values){for(var i=0;i<boxes.Length;i++)boxes[i].Text=values[i];}
        try
        {
            settings.Show();Pump();pet.StartFocusTimer();var active=timer.Snapshot;
            foreach(var draft in new[]{new[]{"37","9","23:15","07:35"},new[]{"","abc","2:","??"},new[]{" 50 "," 10 ","00:00","00:00"}})
            {
                Draft(draft);
                ((CheckBox)settings.FindName("QuietHoursCheck")).IsChecked=true;
                foreach(var locale in new[]{"en","zh-CN","ko"})
                {
                    ((ComboBox)settings.FindName("LanguageCombo")).SelectedValue=locale;
                    Check(boxes.Select(x=>x.Text).SequenceEqual(draft),"Language selection overwrote unapplied timer/quiet-hour drafts");
                    Check(((CheckBox)settings.FindName("QuietHoursCheck")).IsChecked==true&&!preferences.QuietHoursEnabled,"Language selection discarded or applied the pending quiet-hours enable choice");
                    Check(pet.UiLanguagePreference==locale&&preferences.FocusMinutes==25&&preferences.BreakMinutes==5
                        &&preferences.QuietHoursStartMinute==1320&&preferences.QuietHoursEndMinute==480&&timer.Snapshot==active,
                        "Relocalizing draft fields applied a draft or reset the current countdown");
                }
            }
            Draft("37","9","23:15","07:35");
            ((Button)settings.FindName("FocusDurationApplyButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Invoke(settings,"QuietHoursApply_Click",settings,new RoutedEventArgs());
            Check(preferences.FocusMinutes==37&&preferences.BreakMinutes==9&&preferences.QuietHoursEnabled&&preferences.QuietHoursStartMinute==1395&&preferences.QuietHoursEndMinute==455
                &&timer.Snapshot==active,"Preserved drafts could not be explicitly applied without changing the active session");
            Console.WriteLine("PASS Real KO/EN/zh-CN language selection preserves valid, partial, invalid and whitespace drafts without applying them; explicit Apply still works");

            Draft("unfinished","!","bad","");
            var path=Path.Combine(root,"draft-restore.json");
            PetSettingsBackup.SaveFile(path,new PetSettings{CharacterName="Companion",Visible=false,Language="en",FocusMinutes=50,BreakMinutes=10,
                QuietHoursStartMinute=60,QuietHoursEndMinute=120,AutoConnectAssistants=false});
            Invoke(settings,"RestorePreferencesFromFile",path);
            Check(boxes.Select(x=>x.Text).SequenceEqual(new[]{"50","10","01:00","02:00"})&&pet.FocusMinutes==50&&timer.Snapshot==active,
                "Explicit backup restore retained stale drafts instead of restored preferences");
            Check(((CheckBox)settings.FindName("QuietHoursCheck")).IsChecked==false&&!pet.QuietHoursEnabled,"Explicit restore retained a stale quiet-hours choice");
            Draft("77","bad","2","");((CheckBox)settings.FindName("QuietHoursCheck")).IsChecked=true;
            File.WriteAllText(path,"not valid JSON");Invoke(settings,"RestorePreferencesFromFile",path);
            Check(boxes.Select(x=>x.Text).SequenceEqual(new[]{"77","bad","2",""})&&pet.FocusMinutes==50&&timer.Snapshot==active,
                "Rejected backup replaced unapplied drafts or the active session");
            Check(((CheckBox)settings.FindName("QuietHoursCheck")).IsChecked==true&&!pet.QuietHoursEnabled,"Rejected backup replaced or applied the pending schedule choice");
            settings.Close();var closed=boxes.Select(x=>x.Text).ToArray();UiText.SetLanguage("zh-CN");
            Check(boxes.Select(x=>x.Text).SequenceEqual(closed)&&Directory.GetFiles(inbox,"*.json").Length==0,"Closed locale callback changed drafts or published a completion");
            Console.WriteLine("PASS Explicit valid restores replace drafts, invalid restores preserve them, and closed language callbacks leave inputs and timers intact");
        }
        finally{settings.Close();save.Stop();Set(pet,"_settingsDirty",false);pet.DisposeResources();pet.Close();UiText.SetLanguage(language);}
    }
    private sealed class Clock:TimeProvider{public override long TimestampFrequency=>TimeSpan.TicksPerSecond;public override long GetTimestamp()=>0;}
    private static object? Get(object o,string n)=>o.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(o);
    private static void Set(object o,string n,object value)=>o.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(o,value);
    private static void Invoke(object o,string n,params object[] args)=>o.GetType().GetMethod(n,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(o,args);
    private static void Pump(){var f=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>f.Continue=false));Dispatcher.PushFrame(f);}
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
}
