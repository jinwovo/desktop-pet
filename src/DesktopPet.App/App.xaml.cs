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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var petWindow = new MainWindow();
        MainWindow = petWindow;
        petWindow.Show(); // Loaded fires synchronously here, so the brain is ready right after.

        _dpiScale = VisualTreeHelper.GetDpi(petWindow).DpiScaleX;
        if (petWindow.WindowFallEnabled)
        {
            _windowFallService = new WindowFallService(_dpiScale);
        }

        petWindow.WindowFallEnabledChanged += OnWindowFallEnabledChanged;

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
    }

    /// <summary>
    /// Tray menu is deliberately short: character, size and speed all live in the settings window,
    /// and duplicating them here just means two places to keep in sync.
    /// </summary>
    private WinForms.ContextMenuStrip BuildTrayMenu(MainWindow petWindow)
    {
        var menu = new WinForms.ContextMenuStrip();

        var settingsItem = menu.Items.Add("설정...", null, (_, _) => ShowSettings(petWindow));
        settingsItem.Font = new System.Drawing.Font(menu.Font, System.Drawing.FontStyle.Bold);

        menu.Items.Add(new WinForms.ToolStripSeparator());

        var moodMenu = new WinForms.ToolStripMenuItem("기분 표현");
        moodMenu.DropDownItems.Add("울기 😢", null, (_, _) => petWindow.TriggerEmote(PetState.Cry));
        moodMenu.DropDownItems.Add("화내기 💢", null, (_, _) => petWindow.TriggerEmote(PetState.Angry));
        menu.Items.Add(moodMenu);

        var visibilityItem = new WinForms.ToolStripMenuItem("펫 보이기");
        visibilityItem.Click += (_, _) => petWindow.SetVisible(!petWindow.IsPetVisible);
        menu.Items.Add(visibilityItem);

        // Reflect current state each time the menu opens, since the settings window can change it.
        menu.Opening += (_, _) => visibilityItem.Checked = petWindow.IsPetVisible;

        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("종료", null, (_, _) => Shutdown());

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

    /// <summary>
    /// Starts or stops the window-fall feature. Disposing it when off matters: it's the part that
    /// periodically captures other applications' windows, which is by far the app's biggest cost.
    /// </summary>
    private void OnWindowFallEnabledChanged(bool enabled)
    {
        if (enabled)
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
        (MainWindow as MainWindow)?.DisposeResources();
        _windowFallService?.Dispose();
        _trayIcon?.Dispose();
        base.OnExit(e);
    }
}
