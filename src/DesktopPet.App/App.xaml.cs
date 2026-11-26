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
    private System.Drawing.Icon? _trayImage;
    private WindowFallService? _windowFallService;
    private SettingsWindow? _settingsWindow;
    private double _dpiScale = 1.0;
    private System.Threading.Mutex? _instanceMutex;
    private AssistantIntegrationService? _assistantService;
    private AssistantConnectionsWindow? _assistantWindow;
    private bool _assistantSetupRunning;
    private Task _assistantSetupTask = Task.CompletedTask;

    protected override void OnStartup(StartupEventArgs e)
    {
        var command = e.Args.Length == 1 ? e.Args[0] : null;
        if (e.Args.Length > 0 && command is not ("--check-install" or "--disconnect-assistants" or "--version" or "--help"))
        {
            Console.Error.WriteLine("DesktopPet accepts one maintenance option. Run DesktopPet.App.exe --help.");
            Shutdown(64);
            return;
        }

        if (command == "--help")
        {
            Console.WriteLine("""
                DesktopPet.App.exe [option]
                  --help                   Show this help.
                  --version                Print the numeric application version.
                  --check-install          Check packaged resources and print a JSON report.
                  --disconnect-assistants  Disconnect DesktopPet hooks from assistant profiles.

                With no option, DesktopPet starts normally.
                Exit codes: 0 success, 2 maintenance check failed, 64 invalid command.
                """);
            Shutdown();
            return;
        }
        if (command == "--check-install")
        {
            var result = InstallationCheck.Inspect(AppContext.BaseDirectory);
            Console.WriteLine(result.ToJson());
            Shutdown(result.ResourcesValid ? 0 : 2);
            return;
        }
        if (command == "--disconnect-assistants")
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
        if (command == "--version")
        {
            Console.WriteLine(typeof(App).Assembly.GetName().Version?.ToString() ?? "2.0.0");
            Shutdown();
            return;
        }
        _instanceMutex = TryAcquireInstanceMutex(@"Local\DesktopPet.App");
        if (_instanceMutex is null)
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
        _trayImage = LoadTrayIcon(iconPath);
        _trayIcon = new WinForms.NotifyIcon
        {
            Icon = _trayImage,
            Visible = true,
            Text = "DesktopPet",
            ContextMenuStrip = BuildTrayMenu(petWindow),
        };

        // Double-clicking the tray icon is the conventional shortcut to a settings window.
        _trayIcon.DoubleClick += (_, _) => ShowSettings(petWindow);
        petWindow.ContextMenuRequested += () => _trayIcon?.ContextMenuStrip?.Show(WinForms.Cursor.Position);
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

    private WinForms.ContextMenuStrip BuildTrayMenu(MainWindow petWindow) => PetMenuBuilder.Create(
        petWindow, () => ShowSettings(petWindow), ShowAssistantConnections, () => Shutdown());

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
            await RunStartupAssistantLinksAsync(_assistantService, CreateAssistantIntegrationHistory(),
                () => !Dispatcher.HasShutdownStarted && petWindow.AutoConnectAssistants);
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

    internal static async Task RunStartupAssistantLinksAsync(AssistantIntegrationService service,
        AssistantIntegrationHistory history, Func<bool> shouldConnect)
    {
        foreach (var provider in Enum.GetValues<AssistantProvider>())
        {
            if (!shouldConnect()) return;
            try
            {
                var before = await Task.Run(() => service.GetStatus(provider));
                if (!shouldConnect()) return;
                if (!before.Detected || !before.Succeeded) continue;
                await Task.Run(() =>
                {
                    var result = service.Connect(provider);
                    if (!result.Succeeded || !result.Registered) return;
                    try { history.Record(result); }
                    catch
                    {
                        if (!before.HasOwnedRegistration) service.Disconnect(provider);
                        throw;
                    }
                });
            }
            catch (Exception ex) { System.Diagnostics.Trace.TraceError($"Assistant setup ({provider}): {ex.Message}"); }
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

    internal static System.Drawing.Icon LoadTrayIcon(string path)
    {
        try { return new System.Drawing.Icon(path); }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException)
        {
            return (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
        }
    }

    internal static System.Threading.Mutex? TryAcquireInstanceMutex(string name)
    {
        var mutex = new System.Threading.Mutex(false, name);
        try
        {
            if (mutex.WaitOne(0)) return mutex;
        }
        catch (AbandonedMutexException)
        {
            // WaitOne transfers ownership to this process even when the previous owner exited.
            return mutex;
        }
        catch
        {
            mutex.Dispose();
            throw;
        }
        mutex.Dispose();
        return null;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        UiText.LanguageChanged -= RefreshTrayLanguage;
        _assistantWindow?.Close();
        (MainWindow as MainWindow)?.DisposeResources();
        _windowFallService?.Dispose();
        _trayIcon?.Dispose();
        _trayImage?.Dispose();
        _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
