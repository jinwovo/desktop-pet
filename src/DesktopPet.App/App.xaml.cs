using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using DesktopPet.Core;
using DesktopPet.WindowFall;
using WinForms = System.Windows.Forms;

namespace DesktopPet.App;

public partial class App : System.Windows.Application
{
    private static readonly (string Label, double Value)[] ScalePresets =
    [
        ("75%", 0.75), ("100%", 1.0), ("150%", 1.5), ("200%", 2.0),
    ];

    private static readonly (string Label, double Value)[] SpeedPresets =
    [
        ("느리게", 0.5), ("보통", 1.0), ("빠르게", 1.5), ("아주 빠르게", 2.0),
    ];

    private WinForms.NotifyIcon? _trayIcon;
    private WindowFallService? _windowFallService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var petWindow = new MainWindow();
        MainWindow = petWindow;
        petWindow.Show(); // Loaded fires synchronously here, so _dpiScale/_brain are ready right after.

        var dpiScale = VisualTreeHelper.GetDpi(petWindow).DpiScaleX;
        _windowFallService = new WindowFallService(dpiScale);

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        _trayIcon = new WinForms.NotifyIcon
        {
            Icon = File.Exists(iconPath) ? new System.Drawing.Icon(iconPath) : System.Drawing.SystemIcons.Application,
            Visible = true,
            Text = "DesktopPet",
            ContextMenuStrip = BuildTrayMenu(petWindow),
        };
    }

    private WinForms.ContextMenuStrip BuildTrayMenu(MainWindow petWindow)
    {
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("DesktopPet", null, (_, _) => { }).Enabled = false;
        menu.Items.Add(new WinForms.ToolStripSeparator());

        var characterMenu = new WinForms.ToolStripMenuItem("캐릭터");
        foreach (var name in petWindow.AvailableCharacterNames)
        {
            var item = new WinForms.ToolStripMenuItem(name)
            {
                CheckOnClick = false,
                Checked = name == petWindow.CurrentCharacterName,
            };
            item.Click += (_, _) =>
            {
                petWindow.SwitchCharacter(name);
                RefreshCheckedSiblings(characterMenu, item);
            };
            characterMenu.DropDownItems.Add(item);
        }
        menu.Items.Add(characterMenu);

        var scaleMenu = new WinForms.ToolStripMenuItem("크기");
        foreach (var (label, value) in ScalePresets)
        {
            var item = new WinForms.ToolStripMenuItem(label) { Checked = petWindow.CurrentScaleMultiplier == value };
            item.Click += (_, _) =>
            {
                petWindow.SetScaleMultiplier(value);
                RefreshCheckedSiblings(scaleMenu, item);
            };
            scaleMenu.DropDownItems.Add(item);
        }
        menu.Items.Add(scaleMenu);

        var speedMenu = new WinForms.ToolStripMenuItem("걷기 속도");
        foreach (var (label, value) in SpeedPresets)
        {
            var item = new WinForms.ToolStripMenuItem(label) { Checked = petWindow.CurrentWalkSpeedMultiplier == value };
            item.Click += (_, _) =>
            {
                petWindow.SetWalkSpeedMultiplier(value);
                RefreshCheckedSiblings(speedMenu, item);
            };
            speedMenu.DropDownItems.Add(item);
        }
        menu.Items.Add(speedMenu);

        var moodMenu = new WinForms.ToolStripMenuItem("기분 표현");
        moodMenu.DropDownItems.Add("울기 😢", null, (_, _) => petWindow.TriggerEmote(PetState.Cry));
        moodMenu.DropDownItems.Add("화내기 💢", null, (_, _) => petWindow.TriggerEmote(PetState.Angry));
        menu.Items.Add(moodMenu);

        menu.Items.Add(new WinForms.ToolStripSeparator());

        var visibilityItem = new WinForms.ToolStripMenuItem("펫 보이기") { Checked = petWindow.IsPetVisible };
        visibilityItem.Click += (_, _) =>
        {
            var next = !petWindow.IsPetVisible;
            petWindow.SetVisible(next);
            visibilityItem.Checked = next;
        };
        menu.Items.Add(visibilityItem);

        menu.Items.Add("캐릭터 폴더 열기", null, (_, _) =>
            Process.Start(new ProcessStartInfo(DesktopPet.App.MainWindow.CharactersRoot) { UseShellExecute = true }));

        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("종료", null, (_, _) => Shutdown());

        return menu;
    }

    private static void RefreshCheckedSiblings(WinForms.ToolStripMenuItem parent, WinForms.ToolStripMenuItem selected)
    {
        foreach (WinForms.ToolStripItem sibling in parent.DropDownItems)
        {
            if (sibling is WinForms.ToolStripMenuItem menuItem)
            {
                menuItem.Checked = menuItem == selected;
            }
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
