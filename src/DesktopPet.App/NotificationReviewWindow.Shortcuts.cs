using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace DesktopPet.App;

public sealed partial class NotificationReviewWindow
{
    private static readonly RoutedUICommand MarkSavedReadCommand =
        new("선택한 알림만 읽음 처리 (Ctrl+Shift+R)", nameof(MarkSavedReadCommand), typeof(NotificationReviewWindow));

    private static readonly RoutedUICommand ClearNotificationFiltersCommand =
        new("검색어와 출처 필터 초기화 (Ctrl+L)", nameof(ClearNotificationFiltersCommand), typeof(NotificationReviewWindow));

    private void InitializeNotificationShortcuts()
    {
        if (_closed) return;

        InputBindings.Add(new KeyBinding(ApplicationCommands.Save, Key.S, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(MarkSavedReadCommand, Key.R, ModifierKeys.Control | ModifierKeys.Shift));
        InputBindings.Add(new KeyBinding(ClearNotificationFiltersCommand, Key.L, ModifierKeys.Control));

        CommandBindings.Add(new CommandBinding(ApplicationCommands.Save, OnSaveExecuted, OnSaveCanExecute));
        CommandBindings.Add(new CommandBinding(MarkSavedReadCommand, OnMarkSavedReadExecuted, OnMarkSavedReadCanExecute));
        CommandBindings.Add(new CommandBinding(ClearNotificationFiltersCommand, OnClearFiltersExecuted, OnClearFiltersCanExecute));
    }

    private void OnSaveCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = !_closed && _exportCancellation is null && _visibleItems.Count > 0;
        e.Handled = true;
    }

    private void OnSaveExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (_closed || _exportCancellation is not null || _visibleItems.Count == 0)
        {
            e.Handled = true;
            return;
        }

        ExportNotifications_Click(ExportNotificationsButton, e);
        e.Handled = true;
    }

    private void OnMarkSavedReadCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = !_closed && !_selectionBusy && _pet is not null && !_pet.IsAcknowledgingAll &&
                       NotificationItems.SelectedItem is SavedNotificationItem;
        e.Handled = true;
    }

    private void OnMarkSavedReadExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (_closed || _selectionBusy || _pet is null || _pet.IsAcknowledgingAll ||
            NotificationItems.SelectedItem is not SavedNotificationItem)
        {
            e.Handled = true;
            return;
        }

        MarkSelectedRead_Click(MarkSelectedReadButton, e);
        e.Handled = true;
    }

    private void OnClearFiltersCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = !_closed && ClearFiltersButton.IsEnabled;
        e.Handled = true;
    }

    private void OnClearFiltersExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (_closed || !ClearFiltersButton.IsEnabled)
        {
            e.Handled = true;
            return;
        }

        ClearFilters_Click(ClearFiltersButton, e);
        e.Handled = true;
    }

    private void RefreshNotificationShortcutHelp()
    {
        if (_closed) return;

        ExportNotificationsButton.ToolTip = UiText.Get("보이는 알림을 JSON으로 저장 (Ctrl+S)");
        AutomationProperties.SetHelpText(ExportNotificationsButton, UiText.Get("보이는 알림을 JSON으로 저장 (Ctrl+S)"));

        MarkSelectedReadButton.ToolTip = UiText.Get("선택한 알림만 읽음 처리 (Ctrl+Shift+R)");
        AutomationProperties.SetHelpText(MarkSelectedReadButton, UiText.Get("선택한 알림만 읽음 처리 (Ctrl+Shift+R)"));

        ClearFiltersButton.ToolTip = UiText.Get("검색어와 출처 필터 초기화 (Ctrl+L)");
        AutomationProperties.SetHelpText(ClearFiltersButton, UiText.Get("검색어와 출처 필터 초기화 (Ctrl+L)"));
    }
}
