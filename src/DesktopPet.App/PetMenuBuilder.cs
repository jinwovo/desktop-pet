using DesktopPet.Core;
using WinForms = System.Windows.Forms;

namespace DesktopPet.App;

internal static class PetMenuBuilder
{
    internal static WinForms.ContextMenuStrip Create(MainWindow petWindow, Action showSettings, Action showAssistantConnections, Action exit)
    {
        var menu = new WinForms.ContextMenuStrip();

        var settingsItem = menu.Items.Add(UiText.Get("설정..."), null, (_, _) => showSettings());
        settingsItem.Font = new System.Drawing.Font(menu.Font, System.Drawing.FontStyle.Bold);
        menu.Items.Add(UiText.Get("AI 연결..."), null, (_, _) => showAssistantConnections());
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
        var roamingPauseItem = new WinForms.ToolStripMenuItem(UiText.Get("자동 산책 잠시 멈춤"));
        roamingPauseItem.Click += (_, _) => petWindow.SetRoamingPaused(!petWindow.RoamingPaused);
        menu.Items.Add(roamingPauseItem);
        menu.Items.Add(UiText.Get("펫 데려오기"), null, (_, _) => petWindow.TryRecoverPet());

        // Reflect current state each time the menu opens, since the settings window can change it.
        menu.Opening += (_, _) =>
        {
            visibilityItem.Checked = petWindow.IsPetVisible;
            roamingPauseItem.Checked = petWindow.RoamingPaused;
            unreadItem.Enabled = petWindow.PendingNotificationCount > 0;
            unreadItem.Text = petWindow.PendingNotificationCount > 0
                ? UiText.Format("보관된 알림 확인 ({0})", petWindow.PendingNotificationCount) : UiText.Get("보관된 알림 없음");
        };

        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(UiText.Get("앱 종료"), null, (_, _) => exit());

        return menu;
    }
}
