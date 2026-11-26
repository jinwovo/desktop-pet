using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopPet.App;
using DesktopPet.Core;

internal static class DiagnosticsUiTests
{
    public static void Run(string root)
    {
        var pet = new MainWindow(new CharacterLibrary(MainWindow.CharactersRoot, Path.Combine(root, "private-library-canary")), inboxDirectory: Path.Combine(root, "private-inbox-canary"));
        Set(pet, "_settings", new PetSettings { AutoConnectAssistants = false });
        Set(pet, "_brain", new PetBrain(new PlatformLayout(new[] { new Platform(0, 1920, 1080) }), 128, 122, 500, 958));
        Invoke(pet, "ApplyPresentation", CharacterPresentation.Load(Path.Combine(MainWindow.CharactersRoot, "Companion")));
        var settings = new SettingsWindow(pet);
        settings.Show();
        try
        {
            var before = JsonSerializer.Serialize(Get(pet, "_settings"));
            ((Button)settings.FindName("DiagnosticsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var window = (DiagnosticWindow)Get(settings, "_diagnosticWindow")!;
            Check(window.IsVisible && window.Owner == settings && Descendants<TextBox>(window).Single().IsReadOnly,
                "Diagnostics did not show a read-only owned preview");
            Check(window.Report == Descendants<TextBox>(window).Single().Text && !window.Report.Contains(root, StringComparison.OrdinalIgnoreCase) &&
                !window.Report.Contains("private-library-canary") && !window.Report.Contains("private-inbox-canary") && !window.Report.Contains("Companion"),
                "Report preview included local paths or character identity");
            var path = Path.Combine(root, "diagnostic-output.json");
            DiagnosticReport.SaveFile(path, window.Report);
            Check(File.ReadAllText(path) == window.Report && before == JsonSerializer.Serialize(Get(pet, "_settings")) && !(bool)Get(pet, "_settingsDirty")!,
                "Diagnostic export changed user preferences or the reviewed content");
            ((Button)settings.FindName("DiagnosticsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(ReferenceEquals(window, Get(settings, "_diagnosticWindow")), "Diagnostics opened duplicate windows");
            var previous = window.Report;
            ((PetSettings)Get(pet, "_settings")!).MaximumFps = 30;
            Check(window.Report == previous, "Diagnostic JSON was replaced before an explicit refresh");
            Descendants<Button>(window).Single(button => button.Name == "RefreshDiagnosticButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            using (var report = JsonDocument.Parse(window.Report))
                Check(report.RootElement.GetProperty("MaximumFps").GetInt32() == 30 && window.Report != previous &&
                    window.Report == Descendants<TextBox>(window).Single().Text, "Refresh did not update the reviewed report from the current pet");
            settings.Close();
            Check(!window.IsVisible && Get(settings, "_diagnosticWindow") is null, "Closing Settings retained the diagnostic window");
            Console.WriteLine("PASS Diagnostics previews exact allowlisted JSON, exports without local paths/character identity and closes with Settings without changing preferences");
        }
        finally
        {
            settings.Close(); ((DispatcherTimer)Get(pet, "_settingsSaveTimer")!).Stop(); Set(pet, "_settingsDirty", false); pet.DisposeResources();
        }
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject node) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private static object? Get(object obj, string name) => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(obj);
    private static void Set(object obj, string name, object value) => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(obj, value);
    private static object? Invoke(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(obj, args);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
