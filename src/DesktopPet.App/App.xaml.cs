using System.IO;
using System.Windows;
using System.Windows.Media;
using DesktopPet.Core;
using DesktopPet.WindowFall;
using WinForms = System.Windows.Forms;

namespace DesktopPet.App;

public partial class App : System.Windows.Application
{
    private WinForms.NotifyIcon? _trayIcon;
    private WindowFallService? _windowFallService;
    private SettingsWindow? _settingsWindow;
    private double _dpiScale = 1.0;
    private System.Threading.Mutex? _instanceMutex;
    private bool _ownsInstanceMutex;
    private AssistantIntegrationService? _assistantService;
    private AssistantConnectionsWindow? _assistantWindow;
    private bool _assistantSetupRunning;
    private Task _assistantSetupTask = Task.CompletedTask;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Contains("--check-install", StringComparer.Ordinal))
        {
            var result = InstallationCheck.Inspect(AppContext.BaseDirectory);
            Console.WriteLine(result.ToJson());
            Shutdown(result.ResourcesValid ? 0 : 2);
            return;
        }
        if (e.Args.Contains("--disconnect-assistants", StringComparer.Ordinal))
        {
            try
            {
                var service = CreateAssistantIntegrationService();
                var history = CreateAssistantIntegrationHistory();
                var results = new List<AssistantIntegrationStatus>();
                foreach (var provider in Enum.GetValues<AssistantProvider>())
                {
                    var result = service.Disconnect(provider);
                    results.Add(result);
                    if (result.Succeeded && !result.Registered) history.Remove(provider, result.ConfigPath);
                }
                foreach (var profile in history.GetProfiles())
                {
                    var directory = Path.GetDirectoryName(profile.ConfigPath)!;
                    var recorded = new AssistantIntegrationService(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        Path.Combine(AppContext.BaseDirectory, "Hooks", "assistant-stop-hook.ps1"),
                        profile.Provider == AssistantProvider.Claude ? directory : null,
                        profile.Provider == AssistantProvider.Codex ? directory : null);
                    var result = recorded.Disconnect(profile.Provider);
                    results.Add(result);
                    if (result.Succeeded && !result.Registered) history.Remove(profile.Provider, profile.ConfigPath);
                }
                Shutdown(results.All(result => result.Succeeded && !result.Registered) ? 0 : 2);
            }
            catch { Shutdown(2); }
            return;
        }
        if (e.Args.Contains("--version", StringComparer.Ordinal))
        {
            Console.WriteLine(typeof(App).Assembly.GetName().Version?.ToString() ?? "1.0.0");
            Shutdown();
            return;
        }
        _instanceMutex = new System.Threading.Mutex(true, @"Local\DesktopPet.App", out _ownsInstanceMutex);
        if (!_ownsInstanceMutex)
        {
            Shutdown();
            return;
        }
        base.OnStartup(e);

        var petWindow = new MainWindow();
        MainWindow = petWindow;
        petWindow.Show(); // Loaded fires synchronously here, so the brain is ready right after.
        if (!petWindow.IsReady) return;

        _dpiScale = VisualTreeHelper.GetDpi(petWindow).DpiScaleX;
        if (petWindow.CanUseWindowFall && petWindow.IsPresentationActive)
        {
            _windowFallService = new WindowFallService(_dpiScale);
        }

        petWindow.WindowFallEnabledChanged += OnWindowFallEnabledChanged;
        petWindow.PresentationVisibilityChanged += shown => OnWindowFallEnabledChanged(shown && petWindow.CanUseWindowFall);

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        _trayIcon = new WinForms.NotifyIcon
        {
            Icon = File.Exists(iconPath) ? new System.Drawing.Icon(iconPath) : System.Drawing.SystemIcons.Application,
            Visible = true,
            Text = "DesktopPet",
            ContextMenuStrip = BuildTrayMenu(petWindow),
        };

        // Double-clicking the tray icon is the conventional shortcut to a settings window.
        _trayIcon.DoubleClick += (_, _) => ShowSettings(petWindow);
        UiText.LanguageChanged += RefreshTrayLanguage;
        petWindow.OperationFailed += message => _trayIcon?.ShowBalloonTip(
            5000, "DesktopPet", message, WinForms.ToolTipIcon.Warning);
        petWindow.PendingNotificationCountChanged += count =>
        {
            if (_trayIcon is not null) _trayIcon.Text = count > 0 ? UiText.Format("DesktopPet · 알림 {0}개", count) : "DesktopPet";
        };
        petWindow.AutoConnectAssistantsChanged += enabled => { if (enabled) StartAssistantSetup(petWindow); };
        StartAssistantSetup(petWindow);
    }

    /// <summary>
    /// Tray menu is deliberately short: character, size and speed all live in the settings window,
    /// and duplicating them here just means two places to keep in sync.
    /// </summary>
    private void RefreshTrayLanguage()
    {
        if (_trayIcon is null || MainWindow is not MainWindow pet) return;
        var previous = _trayIcon.ContextMenuStrip;
        _trayIcon.ContextMenuStrip = BuildTrayMenu(pet);
        _trayIcon.Text = pet.PendingNotificationCount > 0 ? UiText.Format("DesktopPet · 알림 {0}개", pet.PendingNotificationCount) : "DesktopPet";
        previous?.Dispose();
    }

    private WinForms.ContextMenuStrip BuildTrayMenu(MainWindow petWindow)
    {
        var menu = new WinForms.ContextMenuStrip();

        var settingsItem = menu.Items.Add(UiText.Get("설정..."), null, (_, _) => ShowSettings(petWindow));
        settingsItem.Font = new System.Drawing.Font(menu.Font, System.Drawing.FontStyle.Bold);
        menu.Items.Add(UiText.Get("AI 연결..."), null, (_, _) => ShowAssistantConnections());
        var unreadItem = menu.Items.Add(UiText.Get("보관된 알림 확인"), null, (_, _) => petWindow.ShowUnreadNotifications());

        menu.Items.Add(new WinForms.ToolStripSeparator());

        var focusMenu = new WinForms.ToolStripMenuItem(UiText.Get("집중 타이머"));
        var focusStatus = focusMenu.DropDownItems.Add(petWindow.FocusTimerCaption);
        focusStatus.Enabled = false;
        var focusStart = focusMenu.DropDownItems.Add(UiText.Get("집중 시작"), null, (_, _) => petWindow.StartFocusTimer());
        var focusPause = focusMenu.DropDownItems.Add(UiText.Get("일시정지"), null, (_, _) => petWindow.PauseOrResumeFocusTimer());
        var focusRestart = focusMenu.DropDownItems.Add(UiText.Get("다시 시작"), null, (_, _) => petWindow.RestartFocusTimer());
        var focusStop = focusMenu.DropDownItems.Add(UiText.Get("종료"), null, (_, _) => petWindow.StopFocusTimer());
        focusMenu.DropDownOpening += (_, _) =>
        {
            focusStatus.Text = petWindow.FocusTimerCaption;
            focusStart.Text = petWindow.FocusStartCaption;
            focusStart.Enabled = petWindow.FocusCanStart;
            focusPause.Text = petWindow.FocusSnapshot.Status == FocusTimerStatus.Paused ? UiText.Get("계속") : UiText.Get("일시정지");
            focusPause.Enabled = petWindow.FocusCanPause;
            focusRestart.Enabled = focusStop.Enabled = petWindow.FocusCanStop;
        };
        menu.Items.Add(focusMenu);

        var moodMenu = new WinForms.ToolStripMenuItem(UiText.Get("기분 표현"));
        moodMenu.DropDownItems.Add(UiText.Get("울기 😢"), null, (_, _) => petWindow.TriggerEmote(PetState.Cry));
        moodMenu.DropDownItems.Add(UiText.Get("화내기 💢"), null, (_, _) => petWindow.TriggerEmote(PetState.Angry));
        menu.Items.Add(moodMenu);

        var visibilityItem = new WinForms.ToolStripMenuItem(UiText.Get("펫 보이기"));
        visibilityItem.Click += (_, _) => petWindow.SetVisible(!petWindow.IsPetVisible);
        menu.Items.Add(visibilityItem);
        menu.Items.Add(UiText.Get("펫 데려오기"), null, (_, _) => petWindow.TryRecoverPet());

        // Reflect current state each time the menu opens, since the settings window can change it.
        menu.Opening += (_, _) =>
        {
            visibilityItem.Checked = petWindow.IsPetVisible;
            unreadItem.Enabled = petWindow.PendingNotificationCount > 0;
            unreadItem.Text = petWindow.PendingNotificationCount > 0
                ? UiText.Format("보관된 알림 확인 ({0})", petWindow.PendingNotificationCount) : UiText.Get("보관된 알림 없음");
        };

        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(UiText.Get("앱 종료"), null, (_, _) => Shutdown());

        return menu;
    }

    private void ShowSettings(MainWindow petWindow)
    {
        // Reuse the existing window instead of stacking duplicates.
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(petWindow);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    internal static AssistantIntegrationService CreateAssistantIntegrationService() => new(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Path.Combine(AppContext.BaseDirectory, "Hooks", "assistant-stop-hook.ps1"),
        NullIfEmpty(Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR")),
        NullIfEmpty(Environment.GetEnvironmentVariable("CODEX_HOME")));

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    internal static AssistantIntegrationHistory CreateAssistantIntegrationHistory() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopPet", "assistant-connections.json"));

    private void StartAssistantSetup(MainWindow petWindow)
    {
        if (!_assistantSetupRunning) _assistantSetupTask = InitializeAssistantLinksAsync(petWindow);
    }

    private async Task InitializeAssistantLinksAsync(MainWindow petWindow)
    {
        if (_assistantSetupRunning) return;
        _assistantSetupRunning = true;
        try
        {
            _assistantService ??= CreateAssistantIntegrationService();
            foreach (var provider in Enum.GetValues<AssistantProvider>())
            {
                if (!petWindow.AutoConnectAssistants) break;
                var before = await Task.Run(() => _assistantService.GetStatus(provider));
                if (!petWindow.AutoConnectAssistants || !before.Detected) continue;
                var result = await Task.Run(() => _assistantService.Connect(provider));
                if (result.Succeeded && result.Registered)
                {
                    try { CreateAssistantIntegrationHistory().Record(result); }
                    catch
                    {
                        if (!before.HasOwnedRegistration) await Task.Run(() => _assistantService.Disconnect(provider));
                        throw;
                    }
                }
            }
        }
        catch (Exception ex) { System.Diagnostics.Trace.TraceError($"Assistant setup: {ex.Message}"); }
        finally
        {
            _assistantSetupRunning = false;
            if (!Dispatcher.HasShutdownStarted && !petWindow.AssistantSetupSeen)
            {
                petWindow.MarkAssistantSetupSeen();
                ShowAssistantConnections();
            }
        }
    }

    public void ShowAssistantConnections()
    {
        if (_assistantWindow is { IsLoaded: true }) { _assistantWindow.Activate(); return; }
        try
        {
            _assistantService ??= CreateAssistantIntegrationService();
            _assistantWindow = new AssistantConnectionsWindow(_assistantService, MainWindow as MainWindow,
                () => _assistantSetupTask, CreateAssistantIntegrationHistory());
            _assistantWindow.Closed += (_, _) => _assistantWindow = null;
            _assistantWindow.Show();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show("AI 연결 화면을 열지 못했습니다. " + ex.Message, "DesktopPet", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Starts or stops the window-fall feature. Disposing it when off matters: it's the part that
    /// periodically captures other applications' windows, which is by far the app's biggest cost.
    /// </summary>
    private void OnWindowFallEnabledChanged(bool enabled)
    {
        if (enabled && MainWindow is MainWindow { IsPresentationActive: true })
        {
            _windowFallService ??= new WindowFallService(_dpiScale);
        }
        else
        {
            _windowFallService?.Dispose();
            _windowFallService = null;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        UiText.LanguageChanged -= RefreshTrayLanguage;
        _assistantWindow?.Close();
        (MainWindow as MainWindow)?.DisposeResources();
        _windowFallService?.Dispose();
        _trayIcon?.Dispose();
        if (_ownsInstanceMutex) _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
