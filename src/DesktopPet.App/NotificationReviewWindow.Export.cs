using System.Windows;

namespace DesktopPet.App;

public sealed partial class NotificationReviewWindow
{
    private Func<string?> _chooseExportPath = null!;
    private Func<IReadOnlyList<SavedNotificationItem>, string, CancellationToken, Task> _notificationExporter = null!;
    private CancellationTokenSource? _exportCancellation;
    private string _exportStatusKey = "";
    private object[] _exportStatusArguments = [];

    public Task NotificationExportOperation { get; private set; } = Task.CompletedTask;

    private void InitializeNotificationExport(Func<string?>? choosePath,
        Func<IReadOnlyList<SavedNotificationItem>, string, CancellationToken, Task>? writer)
    {
        _chooseExportPath = choosePath ?? ChooseNotificationExportPath;
        _notificationExporter = writer ?? ((snapshot, path, token) =>
            Task.Run(() => SavedNotificationJsonWriter.Save(snapshot, path, token), token));
    }

    private string? ChooseNotificationExportPath()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            AddExtension = true, DefaultExt = ".json", Filter = "JSON (*.json)|*.json",
            FileName = $"DesktopPet-notifications-{DateTime.Now:yyyyMMdd-HHmmss}.json",
            OverwritePrompt = false, Title = UiText.Get("알림 JSON 저장")
        };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private void RefreshExportUi()
    {
        if (_closed) return;
        ExportNotificationsButton.IsEnabled = _exportCancellation is null && _visibleItems.Count > 0;
        ExportNotificationsButton.Content = UiText.Get(_exportCancellation is null ? "보이는 알림 JSON 저장" : "알림 저장 중…");
        ExportStatus.Text = _exportStatusKey.Length == 0 ? "" : UiText.Format(_exportStatusKey, _exportStatusArguments);
    }

    private async void ExportNotifications_Click(object sender, RoutedEventArgs e)
    {
        if (_closed || _exportCancellation is not null || _visibleItems.Count == 0) return;
        var snapshot = _visibleItems.ToArray();
        string? path;
        try { path = _chooseExportPath(); }
        catch
        {
            if (!_closed) SetExportStatus("알림을 저장하지 못했어요. 새 파일 이름으로 다시 시도해주세요.");
            return;
        }
        if (_closed || _exportCancellation is not null || string.IsNullOrWhiteSpace(path)) return;
        var cancellation = new CancellationTokenSource();
        _exportCancellation = cancellation;
        SetExportStatus("알림 {0}개를 저장하고 있어요.", snapshot.Length);
        NotificationExportOperation = SaveNotificationExportAsync(snapshot, path, cancellation);
        await NotificationExportOperation;
    }

    private async Task SaveNotificationExportAsync(SavedNotificationItem[] snapshot, string path, CancellationTokenSource cancellation)
    {
        try
        {
            await _notificationExporter(snapshot, path, cancellation.Token);
            if (!_closed) SetExportStatus("알림 {0}개를 저장했어요.", snapshot.Length);
        }
        catch (OperationCanceledException)
        {
            if (!_closed) SetExportStatus("알림 저장을 취소했어요.");
        }
        catch
        {
            if (!_closed) SetExportStatus("알림을 저장하지 못했어요. 새 파일 이름으로 다시 시도해주세요.");
        }
        finally
        {
            if (ReferenceEquals(_exportCancellation, cancellation)) _exportCancellation = null;
            cancellation.Dispose();
            if (!_closed) RefreshExportUi();
        }
    }

    private void SetExportStatus(string key, params object[] arguments)
    {
        _exportStatusKey = key;
        _exportStatusArguments = arguments;
        RefreshExportUi();
    }

    private void CancelNotificationExport() => _exportCancellation?.Cancel();
}
