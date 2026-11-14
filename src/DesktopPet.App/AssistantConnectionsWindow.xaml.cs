using System.Windows;
using System.Windows.Controls;
using DesktopPet.Core;

namespace DesktopPet.App;

public partial class AssistantConnectionsWindow : Window
{
    private readonly AssistantIntegrationService _service;
    private readonly MainWindow? _pet;
    private readonly Dictionary<AssistantProvider, AssistantIntegrationStatus> _statuses = new();
    private Task _operation = Task.CompletedTask;
    private bool _loading = true;
    private bool _busy;
    private bool _closed;
    private readonly Func<Task>? _beforeModification;
    private readonly AssistantIntegrationHistory? _history;

    public AssistantConnectionsWindow(AssistantIntegrationService service, MainWindow? pet = null,
        Func<Task>? beforeModification = null, AssistantIntegrationHistory? history = null)
    {
        _service = service;
        _pet = pet;
        _beforeModification = beforeModification;
        _history = history;
        InitializeComponent();
        MaxHeight = Math.Max(420, SystemParameters.WorkArea.Height - 60);
        AutoConnectCheck.IsChecked = pet?.AutoConnectAssistants ?? true;
        AutoConnectCheck.IsEnabled = pet is not null;
        _loading = false;
        if (pet is not null) pet.AssistantNotificationReceived += OnAssistantNotification;
        Loaded += (_, _) => _operation = RefreshAsync();
        Closed += (_, _) =>
        {
            _closed = true;
            if (pet is not null) pet.AssistantNotificationReceived -= OnAssistantNotification;
        };
    }

    private async Task RefreshAsync()
    {
        if (_busy || _closed) return;
        SetBusy(true);
        try
        {
            var states = await Task.Run(() => Enum.GetValues<AssistantProvider>().Select(_service.GetStatus).ToArray());
            if (_closed) return;
            foreach (var state in states) DisplayStatus(state);
        }
        catch (Exception ex) { if (!_closed) OperationText.Text = "연결 상태를 확인하지 못했습니다. " + ex.Message; }
        finally { if (!_closed) SetBusy(false); }
    }

    private void DisplayStatus(AssistantIntegrationStatus status)
    {
        _statuses[status.Provider] = status;
        var label = status.Provider == AssistantProvider.Claude ? ClaudeStatusText : CodexStatusText;
        label.Text = status.Detail;
        label.ToolTip = status.ConfigPath;
    }

    private void Connect_Click(object sender, RoutedEventArgs e) => StartOperation(sender, connect: true);
    private void Disconnect_Click(object sender, RoutedEventArgs e) => StartOperation(sender, connect: false);

    private void StartOperation(object sender, bool connect)
    {
        if (_busy || sender is not System.Windows.Controls.Button button || !Enum.TryParse<AssistantProvider>(button.Tag?.ToString(), out var provider)) return;
        _operation = PerformAsync(provider, connect);
    }

    private async Task PerformAsync(AssistantProvider provider, bool connect)
    {
        SetBusy(true);
        string? historyWarning = null;
        try
        {
            if (!connect) _pet?.SetAutoConnectAssistants(false);
            if (_beforeModification is not null) await _beforeModification();
            var before = await Task.Run(() => _service.GetStatus(provider));
            var result = await Task.Run(() => connect ? _service.Connect(provider) : _service.Disconnect(provider));
            if (result.Succeeded)
            {
                try
                {
                    if (connect && result.Registered) _history?.Record(result);
                    else if (!connect && !result.Registered) _history?.Remove(provider, result.ConfigPath);
                }
                catch (Exception ex)
                {
                    if (connect && !before.HasOwnedRegistration)
                    {
                        var rollback = await Task.Run(() => _service.Disconnect(provider));
                        result = rollback;
                        historyWarning = rollback.Succeeded
                            ? "연결 기록을 저장하지 못해 새 연결을 되돌렸습니다. " + ex.Message
                            : "연결 기록을 저장하지 못했습니다. 연결 상태를 확인해주세요. " + ex.Message;
                    }
                    else historyWarning = connect
                        ? "연결은 유지되지만 연결 기록을 저장하지 못했습니다. " + ex.Message
                        : "연결은 해제됐지만 기록을 정리하지 못했습니다. " + ex.Message;
                }
            }
            if (_closed) return;
            DisplayStatus(result);
            OperationText.Text = historyWarning ?? (result.Succeeded
                ? connect ? "연결 설정을 저장했습니다. 위 안내에 따라 도구를 다시 시작해주세요." : "DesktopPet 연결만 해제했습니다."
                : result.Detail);
            AutoConnectCheck.IsChecked = _pet?.AutoConnectAssistants ?? AutoConnectCheck.IsChecked;
        }
        catch (Exception ex) { if (!_closed) OperationText.Text = "설정을 변경하지 못했습니다. " + ex.Message; }
        finally { if (!_closed) SetBusy(false); }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        ClaudeConnectButton.IsEnabled = ClaudeDisconnectButton.IsEnabled = !busy;
        CodexConnectButton.IsEnabled = CodexDisconnectButton.IsEnabled = !busy;
    }

    private void OnAssistantNotification(string source)
    {
        var label = source == "claude" ? ClaudeReceiptText : CodexReceiptText;
        label.Text = $"완료 알림이 도착했어요 · {DateTime.Now:HH:mm}";
    }

    private void AutoConnect_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loading) _pet?.SetAutoConnectAssistants(AutoConnectCheck.IsChecked == true);
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => _operation = RefreshAsync();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
