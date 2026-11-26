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
    public string Report { get; private set; }
    public DiagnosticWindow(string report, Func<string> refreshReport)
    {
        Report = report;
        Title = "DesktopPet 진단 보고서";
        Width = 640; Height = 520; MinWidth = 420; MinHeight = 340;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new DockPanel { Margin = new Thickness(16) };
        var hint = new TextBlock { Text = "저장 전에 내용을 확인하세요. 경로·대화·키·훅 설정을 수집하거나 업로드하지 않습니다.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(hint, Dock.Top); panel.Children.Add(hint);
        var footer = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        var preview = new TextBox { Name = "DiagnosticPreview", Text = Report, IsReadOnly = true, AcceptsReturn = true,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"), TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var status = new TextBlock { Name = "DiagnosticSaveStatus", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
        var save = new Button { Name = "SaveDiagnosticButton", Content = "JSON 저장", Padding = new Thickness(12, 6, 12, 6) };
        save.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "JSON (*.json)|*.json", FileName = "DesktopPet-diagnostic.json", OverwritePrompt = true };
            if (dialog.ShowDialog(this) != true) return;
            try { DiagnosticReport.SaveFile(dialog.FileName, Report); status.Text = UiText.Get("저장했어요."); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { status.Text = UiText.Get("저장하지 못했어요."); }
        };
        var refresh = new Button { Name = "RefreshDiagnosticButton", Content = "진단 새로고침",
            Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0) };
        refresh.Click += (_, _) =>
        {
            try { preview.Text = Report = refreshReport(); status.Text = ""; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
            { status.Text = UiText.Get("진단 정보를 읽지 못했어요."); }
        };
        actions.Children.Add(refresh);
        actions.Children.Add(save);
        footer.Children.Add(status);
        footer.Children.Add(actions);
        DockPanel.SetDock(footer, Dock.Bottom); panel.Children.Add(footer);
        panel.Children.Add(preview);
        Content = panel;
        UiText.Attach(this);
    }
}
