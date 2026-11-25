using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace DesktopPet.App;

public partial class SettingsWindow
{
    private void InitializeTimeInputShortcuts()
    {
        FocusMinutesBox.KeyDown += FocusTimeInput_KeyDown;
        BreakMinutesBox.KeyDown += FocusTimeInput_KeyDown;
        QuietStartBox.KeyDown += QuietTimeInput_KeyDown;
        QuietEndBox.KeyDown += QuietTimeInput_KeyDown;
    }

    private void FocusTimeInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (_closed || e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        e.Handled = true;
        FocusDurationApply_Click(sender, e);
    }

    private void QuietTimeInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (_closed || e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        e.Handled = true;
        QuietHoursApply_Click(sender, e);
    }

    private void RefreshTimeInputShortcutHelp()
    {
        if (_closed)
        {
            return;
        }

        FocusMinutesBox.ToolTip = UiText.Get("Enter로 집중·휴식 시간 적용");
        BreakMinutesBox.ToolTip = UiText.Get("Enter로 집중·휴식 시간 적용");
        QuietStartBox.ToolTip = UiText.Get("Enter로 방해 금지 예약 적용");
        QuietEndBox.ToolTip = UiText.Get("Enter로 방해 금지 예약 적용");

        AutomationProperties.SetAcceleratorKey(FocusMinutesBox, "Enter");
        AutomationProperties.SetAcceleratorKey(BreakMinutesBox, "Enter");
        AutomationProperties.SetAcceleratorKey(QuietStartBox, "Enter");
        AutomationProperties.SetAcceleratorKey(QuietEndBox, "Enter");
    }
}
