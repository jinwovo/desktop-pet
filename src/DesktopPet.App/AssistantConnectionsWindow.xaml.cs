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
    private string _operationKey = "";
    private string? _operationDetail;
    private readonly Func<Task>? _waitForStartup;
    private readonly AssistantIntegrationHistory? _history;

    public AssistantConnectionsWindow(AssistantIntegrationService service, MainWindow? pet = null,
        Func<Task>? beforeModification = null, AssistantIntegrationHistory? history = null)
    {
        _service = service;
        _pet = pet;
        _waitForStartup = beforeModification;
        _history = history;
        InitializeComponent();
        MaxHeight = Math.Max(420, SystemParameters.WorkArea.Height - 60);
        AutoConnectCheck.IsChecked = pet?.AutoConnectAssistants ?? true;
        AutoConnectCheck.IsEnabled = pet is not null;
        _loading = false;
        if (pet is not null)
        {
            pet.AssistantNotificationReceived += OnAssistantNotification;
            pet.AutoConnectAssistantsChanged += SyncAutoConnectPreference;
        }
        UiText.LanguageChanged += RenderLanguage;
        RenderLanguage();
        Loaded += (_, _) => _operation = RefreshAsync();
        Closed += (_, _) =>
        {
            _closed = true;
            UiText.LanguageChanged -= RenderLanguage;
            if (pet is not null)
            {
                pet.AssistantNotificationReceived -= OnAssistantNotification;
                pet.AutoConnectAssistantsChanged -= SyncAutoConnectPreference;
            }
        };
    }

    private async Task RefreshAsync()
    {
        if (_busy || _closed) return;
        SetBusy(true);
        try
        {
            if (_waitForStartup is not null) await _waitForStartup();
            if (_closed) return;
            var states = await Task.Run(() => Enum.GetValues<AssistantProvider>().Select(_service.GetStatus).ToArray());
            if (_closed) return;
            foreach (var state in states) DisplayStatus(state);
        }
        catch (Exception ex) { if (!_closed) SetOperation("연결 상태를 확인하지 못했습니다.", ex.Message); }
        finally { if (!_closed) SetBusy(false); }
    }

    private void DisplayStatus(AssistantIntegrationStatus status)
    {
        _statuses[status.Provider] = status;
        RenderStatus(status);
    }

    private void RenderStatus(AssistantIntegrationStatus status)
    {
        var label = status.Provider == AssistantProvider.Claude ? ClaudeStatusText : CodexStatusText;
        label.Text = status.Succeeded ? UiText.Get(status.Detail) : UiText.Get("연결 상태를 확인하지 못했습니다.");
        label.ToolTip = status.Succeeded ? status.ConfigPath : status.ConfigPath + Environment.NewLine + status.Detail;
    }

    private void SetOperation(string key, string? detail = null)
    {
        _operationKey = key;
        _operationDetail = detail;
        RenderOperation();
    }

    private void RenderOperation()
    {
        OperationText.Text = UiText.Get(_operationKey);
        OperationText.ToolTip = _operationDetail;
    }

    private void RenderLanguage()
    {
        if (_closed) return;
        UiText.TranslateWindow(this);
        foreach (var status in _statuses.Values) RenderStatus(status);
        RenderOperation();
        OnAssistantNotification("claude");
        OnAssistantNotification("codex");
    }

    private void Connect_Click(object sender, RoutedEventArgs e) => StartOperation(sender, connect: true);
    private void Disconnect_Click(object sender, RoutedEventArgs e) => StartOperation(sender, connect: false);

    private void StartOperation(object sender, bool connect)
    {
        if (_busy || _closed || sender is not System.Windows.Controls.Button button || !Enum.TryParse<AssistantProvider>(button.Tag?.ToString(), out var provider)) return;
        _operation = PerformAsync(provider, connect);
    }

    private async Task PerformAsync(AssistantProvider provider, bool connect)
    {
        SetBusy(true);
        try
        {
            if (!connect) _pet?.SetAutoConnectAssistants(false);
            if (_waitForStartup is not null) await _waitForStartup();
            var (result, warning, detail) = await Task.Run(() => UpdateConnection(provider, connect));
            if (_closed) return;
            DisplayStatus(result);
            if (warning is not null) SetOperation(warning, detail);
            else if (result.Succeeded) SetOperation(connect
                ? "연결 설정을 저장했습니다. 위 안내에 따라 도구를 다시 시작해주세요."
                : "DesktopPet 연결만 해제했습니다.");
            else SetOperation("설정을 변경하지 못했습니다.", result.Detail);
        }
        catch (Exception ex) { if (!_closed) SetOperation("설정을 변경하지 못했습니다.", ex.Message); }
        finally { if (!_closed) SetBusy(false); }
    }

    private (AssistantIntegrationStatus Result, string? Warning, string? Detail) UpdateConnection(AssistantProvider provider, bool connect)
    {
        var before = _service.GetStatus(provider);
        var result = connect ? _service.Connect(provider) : _service.Disconnect(provider);
        if (!result.Succeeded || _history is null) return (result, null, null);
        try
        {
            if (connect && result.Registered) _history.Record(result);
            else if (!connect && !result.Registered) _history.Remove(provider, result.ConfigPath);
            return (result, null, null);
        }
        catch (Exception ex)
        {
            if (connect && !before.HasOwnedRegistration)
            {
                var rollback = _service.Disconnect(provider);
                return (rollback, rollback.Succeeded
                    ? "연결 기록을 저장하지 못해 새 연결을 되돌렸습니다."
                    : "연결 기록을 저장하지 못했습니다. 연결 상태를 확인해주세요.", ex.Message);
            }

            return (result, connect
                ? "연결은 유지되지만 연결 기록을 저장하지 못했습니다."
                : "연결은 해제됐지만 기록을 정리하지 못했습니다.", ex.Message);
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        RefreshButton.IsEnabled = !busy;
        if (!busy && _pet is not null) SyncAutoConnectPreference(_pet.AutoConnectAssistants);
        AutoConnectCheck.IsEnabled = !busy && _pet is not null;
        ClaudeConnectButton.IsEnabled = ClaudeDisconnectButton.IsEnabled = !busy;
        CodexConnectButton.IsEnabled = CodexDisconnectButton.IsEnabled = !busy;
    }

    private void OnAssistantNotification(string source)
    {
        var label = source == "claude" ? ClaudeReceiptText : CodexReceiptText;
        var arrived = _pet?.LastAssistantReceipt(source);
        label.Text = arrived is null ? "" : UiText.Format("완료 알림이 도착했어요 · {0:HH:mm}", arrived.Value);
    }

    private void SyncAutoConnectPreference(bool enabled)
    {
        if (_closed) return;
        _loading = true;
        try { AutoConnectCheck.IsChecked = enabled; }
        finally { _loading = false; }
    }

    private void AutoConnect_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loading && !_closed && !_busy) _pet?.SetAutoConnectAssistants(AutoConnectCheck.IsChecked == true);
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (!_busy && !_closed) _operation = RefreshAsync();
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
