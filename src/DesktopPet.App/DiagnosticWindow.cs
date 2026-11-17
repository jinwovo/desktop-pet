using System.IO;
using System.Windows;
using System.Windows.Controls;
using DesktopPet.Core;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace DesktopPet.App;

public sealed class DiagnosticWindow : Window
{
    public string Report { get; }
    public DiagnosticWindow(string report)
    {
        Report = report;
        Title = "DesktopPet 진단 보고서";
        Width = 640; Height = 520; MinWidth = 420; MinHeight = 340;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new DockPanel { Margin = new Thickness(16) };
        var hint = new TextBlock { Text = "저장 전에 내용을 확인하세요. 경로·대화·키·훅 설정을 수집하거나 업로드하지 않습니다.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(hint, Dock.Top); panel.Children.Add(hint);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0) };
        var status = new TextBlock { Name = "DiagnosticSaveStatus", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        var save = new Button { Name = "SaveDiagnosticButton", Content = "JSON 저장", Padding = new Thickness(12, 6, 12, 6) };
        save.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "JSON (*.json)|*.json", FileName = "DesktopPet-diagnostic.json", OverwritePrompt = true };
            if (dialog.ShowDialog(this) != true) return;
            try { DiagnosticReport.SaveFile(dialog.FileName, Report); status.Text = UiText.Get("저장했어요."); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { status.Text = UiText.Get("저장하지 못했어요."); }
        };
        footer.Children.Add(status); footer.Children.Add(save);
        DockPanel.SetDock(footer, Dock.Bottom); panel.Children.Add(footer);
        panel.Children.Add(new TextBox { Name = "DiagnosticPreview", Text = Report, IsReadOnly = true, AcceptsReturn = true,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"), TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Content = panel;
        UiText.Attach(this);
    }
}
