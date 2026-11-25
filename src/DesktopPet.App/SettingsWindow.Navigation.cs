using System.Windows;
using System.Windows.Input;

namespace DesktopPet.App
{
    public partial class SettingsWindow
    {
        private static readonly RoutedUICommand SettingsSectionCommand = new(
            "SettingsSection",
            nameof(SettingsSectionCommand),
            typeof(SettingsWindow));

        private void InitializeSettingsNavigation()
        {
            InputBindings.Add(new KeyBinding(SettingsSectionCommand, Key.D1, ModifierKeys.Control) { CommandParameter = "character" });
            InputBindings.Add(new KeyBinding(SettingsSectionCommand, Key.D2, ModifierKeys.Control) { CommandParameter = "motion" });
            InputBindings.Add(new KeyBinding(SettingsSectionCommand, Key.D3, ModifierKeys.Control) { CommandParameter = "focus" });
            InputBindings.Add(new KeyBinding(SettingsSectionCommand, Key.D4, ModifierKeys.Control) { CommandParameter = "notifications" });

            CommandBindings.Add(new CommandBinding(SettingsSectionCommand, OnSettingsSectionExecuted, OnSettingsSectionCanExecute));
        }

        private void OnSettingsSectionCanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            var target = GetSettingsNavigationTarget(e.Parameter);
            e.CanExecute = !_closed && target != null && target.IsEnabled && target.IsVisible && target.Focusable;
            e.Handled = true;
        }

        private void OnSettingsSectionExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            e.Handled = true;
            var target = GetSettingsNavigationTarget(e.Parameter);
            if (target == null || _closed || !target.IsEnabled || !target.IsVisible || !target.Focusable)
            {
                return;
            }

            target.BringIntoView();
            target.Focus();
        }

        private System.Windows.FrameworkElement? GetSettingsNavigationTarget(object? parameter) => parameter switch
        {
            "character" => CharacterCombo,
            "motion" => ScaleSlider,
            "focus" => FocusMinutesBox,
            "notifications" => QuietHoursCheck,
            _ => null
        };

        private void RefreshSettingsNavigationHelp()
        {
            if (_closed)
            {
                return;
            }

            System.Windows.Automation.AutomationProperties.SetHelpText(
                this,
                UiText.Get("Ctrl+1 캐릭터, Ctrl+2 크기·속도, Ctrl+3 집중, Ctrl+4 알림 설정으로 이동합니다."));
        }
    }
}
