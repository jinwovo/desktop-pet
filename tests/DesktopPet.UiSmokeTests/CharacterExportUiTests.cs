using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DesktopPet.App;
using DesktopPet.Core;

internal static class CharacterExportUiTests
{
    public static void Run(string root, bool capture = false)
    {
        var custom = Path.Combine(root, "export-custom");
        Create(custom, "Export Friend", false);
        Create(custom, "Broken Export", true);
        var pet = new MainWindow(new CharacterLibrary(MainWindow.CharactersRoot, custom), inboxDirectory: Path.Combine(root, "export-inbox"));
        pet.Loaded -= typeof(MainWindow).GetMethod("MainWindow_Loaded", BindingFlags.Instance | BindingFlags.NonPublic)!.CreateDelegate<RoutedEventHandler>(pet);
        Set(pet, "_settings", new PetSettings { CharacterName="Companion", QuietMode=true });
        Set(pet, "_brain", new PetBrain(new PlatformLayout(new[] { new Platform(0,1920,1080) }),128,122,500,958));
        typeof(MainWindow).GetMethod("ApplyPresentation", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(pet,
            new object[] { CharacterPresentation.Load(Path.Combine(MainWindow.CharactersRoot,"Companion")) });
        var timer = (DispatcherTimer)Get(pet, "_settingsSaveTimer")!;
        timer.Interval = TimeSpan.FromDays(1);
        var output = Path.Combine(root, "export-output"); Directory.CreateDirectory(output);
        string? destination = Path.Combine(output,"shared.zip");
        var calls = 0;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        CancellationToken workerToken = default;
        IProgress<CharacterExportProgress>? lateProgress = null;
        var priorLanguage = UiText.Language;
        var gallery = new CharacterGalleryWindow(pet, chooseExportPath: _ => destination,
            exporter: async (name, path, token, progress) =>
            {
                calls++;
                if (Path.GetFileName(path) is "cancel.zip" or "close.zip")
                {
                    workerToken = token; lateProgress = progress;
                    await Task.Run(() =>
                    {
                        progress!.Report(new(1,3)); entered.Set();
                        if (!release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Export worker never released");
                        token.ThrowIfCancellationRequested();
                    });
                }
                return await pet.ExportCharacterAsync(name,path,token,progress);
            });
        try
        {
            gallery.Show(); Wait(gallery.LoadingTask);
            var cards = (ListBox)gallery.FindName("CharacterCards");
            var export = (Button)gallery.FindName("ExportButton");
            var cancel = (Button)gallery.FindName("CancelExportButton");
            var status = (TextBlock)gallery.FindName("StatusText");
            var presentation = pet.CurrentPresentation;
            var settings = JsonSerializer.Serialize(Get(pet,"_settings"));
            void Unchanged()
            {
                Check(ReferenceEquals(presentation,pet.CurrentPresentation) && settings==JsonSerializer.Serialize(Get(pet,"_settings")) &&
                    !(bool)Get(pet,"_settingsDirty")!,"Export changed the current pet or preferences");
            }
            cards.SelectedItem = gallery.AllCharacters.Single(item=>item.Name=="Export Friend");
            Click(export); Wait(gallery.ExportOperation); Unchanged();
            Check(File.Exists(destination) && status.Text==UiText.Get("캐릭터 ZIP을 저장했어요.") && export.IsEnabled && cancel.Visibility==Visibility.Collapsed,
                "Gallery export did not publish a ZIP or restore its controls");
            using(var zip=ZipFile.OpenRead(destination))
                Check(zip.Entries.Count==3 && !zip.Entries.Any(e=>e.Name==".env"),"Export included unrelated local files");
            using(var archive=CharacterArchive.Open(destination))
            {
                var library=new CharacterLibrary(Path.Combine(root,"export-bundled"),Path.Combine(root,"export-installed"));
                var imported=archive.Import(archive.Characters.Single(),library,stage=>{_=CharacterPresentation.Load(stage,includeRasterGait:true);});
                Check(imported=="Export Friend" && CharacterPresentation.Load(library.Resolve(imported)).Frames.Count>0,"Exported ZIP cannot be decoded after re-import");
            }
            Console.WriteLine("PASS Actual gallery export produces a re-importable decoded ZIP, excludes local files and never applies a character");

            var original = File.ReadAllBytes(destination);
            Click(export); Wait(gallery.ExportOperation);
            Check(File.ReadAllBytes(destination).SequenceEqual(original) && status.Text==UiText.Get("ZIP을 저장하지 못했어요. 새 파일 이름과 캐릭터 그림을 확인해주세요."),
                "Export replaced an existing ZIP or failed without retry guidance");
            destination=Path.Combine(output,"broken.zip");
            cards.SelectedItem=gallery.AllCharacters.Single(item=>item.Name=="Broken Export");
            Check(export.IsEnabled,"Corrupt non-thumbnail fixture was rejected before full export validation");
            Click(export); Wait(gallery.ExportOperation); Unchanged();
            Check(!File.Exists(destination) && export.IsEnabled && status.Text==UiText.Get("ZIP을 저장하지 못했어요. 새 파일 이름과 캐릭터 그림을 확인해주세요."),
                "Corrupt declared PNG was exported or left the gallery stuck");
            Console.WriteLine("PASS Gallery export preserves existing ZIPs and rejects corrupt non-thumbnail PNGs before publication");

            cards.SelectedItem=gallery.AllCharacters.Single(item=>item.Name=="Export Friend");
            destination=Path.Combine(output,"cancel.zip");
            Click(export); Until(()=>entered.IsSet);
            var operation=gallery.ExportOperation;
            Check(!export.IsEnabled && cancel.IsVisible && cancel.IsEnabled && !operation.IsCompleted,"Pending export did not expose responsive cancel controls");
            Click(export);
            Check(ReferenceEquals(operation,gallery.ExportOperation)&&calls==4,"Repeated export input started a competing writer");
            cards.SelectedItem=gallery.AllCharacters.Single(item=>item.Name=="Broken Export");
            Check(!workerToken.IsCancellationRequested && !export.IsEnabled,"Browsing another card cancelled an explicit export or unlocked another writer");
            foreach(var language in new[]{"ko","en","zh-CN"})
            {
                UiText.SetLanguage(language); Pump();
                Check((string)export.Content==UiText.Get("ZIP으로 내보내기") && (string)cancel.Content==UiText.Get("내보내기 취소") &&
                    status.Text==UiText.Format("{0} ZIP 내보내는 중… {1}/{2}","Export Friend",1,3),"Export labels/progress lost their live localization or the original export target");
                if(capture)
                {
                    gallery.Width=760; gallery.Height=740; export.BringIntoView(); gallery.UpdateLayout();
                    Program.SaveWindowSnapshot(gallery,"gallery-export-"+language+".png");
                }
            }
            Click(cancel); Check(workerToken.IsCancellationRequested&&!cancel.IsEnabled,"Cancel did not signal the running export");
            release.Set(); Wait(operation); Unchanged();
            Check(!File.Exists(destination)&&cancel.Visibility==Visibility.Collapsed&&export.IsEnabled&&status.Text==UiText.Get("ZIP 내보내기를 취소했어요."),"Cancelled export published output or did not recover controls");
            lateProgress!.Report(new(3,3)); Pump();
            Check(status.Text==UiText.Get("ZIP 내보내기를 취소했어요."),"Late progress overwrote cancellation feedback");
            Console.WriteLine("PASS Busy exports remain responsive, reject duplicate writers, localize live progress and discard late progress after cancellation");

            entered.Reset(); release.Reset();
            cards.SelectedItem=gallery.AllCharacters.Single(item=>item.Name=="Export Friend");
            destination=Path.Combine(output,"close.zip"); Click(export); Until(()=>entered.IsSet);
            gallery.Close(); release.Set(); Wait(gallery.ExportOperation); Unchanged();
            Check(workerToken.IsCancellationRequested&&!File.Exists(destination),"Closing the gallery did not cancel its pending export");
            var chooserCalls=0;
            var cancelledPicker=new CharacterGalleryWindow(pet,chooseExportPath:_=>{chooserCalls++;return null;});
            try
            {
                cancelledPicker.Show(); Wait(cancelledPicker.LoadingTask);
                ((ListBox)cancelledPicker.FindName("CharacterCards")).SelectedItem=cancelledPicker.AllCharacters.Single(item=>item.Name=="Export Friend");
                var before=Directory.GetFiles(output).Length;
                Click((Button)cancelledPicker.FindName("ExportButton")); Wait(cancelledPicker.ExportOperation);
                Check(chooserCalls==1&&Directory.GetFiles(output).Length==before,"Cancelling the save picker started file work");
            }
            finally{cancelledPicker.Close();}
            CharacterGalleryWindow? closingPicker=null;
            closingPicker=new CharacterGalleryWindow(pet,chooseExportPath:_=>{closingPicker!.Close();return Path.Combine(output,"closed-picker.zip");});
            closingPicker.Show(); Wait(closingPicker.LoadingTask);
            ((ListBox)closingPicker.FindName("CharacterCards")).SelectedItem=closingPicker.AllCharacters.Single(item=>item.Name=="Export Friend");
            Click((Button)closingPicker.FindName("ExportButton")); Wait(closingPicker.ExportOperation);
            Check(!File.Exists(Path.Combine(output,"closed-picker.zip")),"Picker completion started export after gallery closure");
            Console.WriteLine("PASS Closing the gallery cancels export; cancelled/closed save pickers never start output work");
        }
        finally
        {
            release.Set(); gallery.Close(); Wait(gallery.ExportOperation);
            timer.Stop(); Set(pet,"_settingsDirty",false); pet.DisposeResources(); pet.Close(); UiText.SetLanguage(priorLanguage);
        }
    }
    private static void Create(string root,string name,bool broken)
    {
        var path=Path.Combine(root,name); Directory.CreateDirectory(Path.Combine(path,"poses"));
        File.Copy(Path.Combine(MainWindow.CharactersRoot,"Companion","idle.png"),Path.Combine(path,"poses","idle.png"));
        File.Copy(Path.Combine(path,"poses","idle.png"),Path.Combine(path,"poses","walk.png"));
        if(broken)File.WriteAllText(Path.Combine(path,"poses","walk.png"),"not a PNG");
        File.WriteAllText(Path.Combine(path,".env"),"DO_NOT_EXPORT=true");
        File.WriteAllText(Path.Combine(path,"character.json"),JsonSerializer.Serialize(new
        {name,frameSizePx=new{width=128,height=128},anchor=new{x=64,y=122},frames=new{idle=new[]{"poses/idle.png"},walk=new[]{"poses/walk.png"}}}));
    }
    private static void Click(Button button)=>button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Until(Func<bool> done)
    {var w=Stopwatch.StartNew();while(!done()&&w.Elapsed<TimeSpan.FromSeconds(20)){Pump();Thread.Sleep(1);}Check(done(),"Export UI operation timed out");}
    private static void Wait(Task task){Until(()=>task.IsCompleted);task.GetAwaiter().GetResult();}
    private static void Pump(){var f=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>f.Continue=false));Dispatcher.PushFrame(f);}
    private static object? Get(object o,string n)=>o.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(o);
    private static void Set(object o,string n,object v)=>o.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(o,v);
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
}
