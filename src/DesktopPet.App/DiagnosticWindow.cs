using System.IO;
using System.Windows;
using System.Windows.Controls;
using DesktopPet.Core;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace DesktopPet.App;

public sealed class DiagnosticWindow : Window
{
    private readonly TextBlock _status;
    private readonly Button _save;
    private readonly Action<string, string, CancellationToken> _writeReport;
    private string _statusKey = "";
    private CancellationTokenSource? _saveCancellation;
    private bool _closed;

    public string Report { get; private set; }
    public DiagnosticWindow(string report, Func<string> refreshReport,
        Action<string, string, CancellationToken>? writeReport = null, Action<string>? copyText = null)
    {
        Report = report;
        _writeReport = writeReport ?? DiagnosticReport.SaveFile;
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
        _status = new TextBlock { Name = "DiagnosticSaveStatus", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
        _save = new Button { Name = "SaveDiagnosticButton", Content = "JSON 저장", Padding = new Thickness(12, 6, 12, 6) };
        _save.Click += async (_, _) =>
        {
            if (_closed || _saveCancellation is not null) return;
            var snapshot = Report;
            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "JSON (*.json)|*.json", FileName = "DesktopPet-diagnostic.json", OverwritePrompt = true };
            if (dialog.ShowDialog(this) == true) await SaveReportAsync(dialog.FileName, snapshot);
        };
        var refresh = new Button { Name = "RefreshDiagnosticButton", Content = "진단 새로고침",
            Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0) };
        refresh.Click += (_, _) =>
        {
            if (_closed) return;
            try { preview.Text = Report = refreshReport(); if (_saveCancellation is null) SetStatus(""); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
            { SetStatus("진단 정보를 읽지 못했어요."); }
        };
        var copy = new Button { Name = "CopyDiagnosticButton", Content = "진단 복사",
            Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0) };
        var clipboardWriter = copyText ?? System.Windows.Clipboard.SetText;
        copy.Click += (_, _) =>
        {
            if (_closed) return;
            try
            {
                clipboardWriter(Report);
                SetStatus("진단 보고서를 복사했어요.");
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                SetStatus("진단 보고서를 복사하지 못했어요. 다시 시도해주세요.");
            }
        };
        actions.Children.Add(refresh);
        actions.Children.Add(copy);
        actions.Children.Add(_save);
        footer.Children.Add(_status);
        footer.Children.Add(actions);
        DockPanel.SetDock(footer, Dock.Bottom); panel.Children.Add(footer);
        panel.Children.Add(preview);
        Content = panel;
        UiText.Attach(this);
        UiText.LanguageChanged += RenderStatus;
        Closed += (_, _) =>
        {
            _closed = true;
            _saveCancellation?.Cancel();
            UiText.LanguageChanged -= RenderStatus;
        };
    }

    private async Task SaveReportAsync(string path, string snapshot)
    {
        if (_closed || _saveCancellation is not null) return;
        using var request = new CancellationTokenSource();
        _saveCancellation = request;
        _save.IsEnabled = false;
        SetStatus("진단 보고서를 저장하고 있어요…");
        try
        {
            await Task.Run(() => _writeReport(path, snapshot, request.Token), request.Token);
            if (!_closed) SetStatus("저장했어요.");
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            if (!_closed) SetStatus("저장하지 못했어요.");
        }
        finally
        {
            _saveCancellation = null;
            if (!_closed) _save.IsEnabled = true;
        }
    }

    private void SetStatus(string key)
    {
        _statusKey = key;
        RenderStatus();
    }

    private void RenderStatus()
    {
        if (!_closed) _status.Text = UiText.Get(_statusKey);
    }
}
