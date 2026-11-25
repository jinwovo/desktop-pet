using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;

namespace DesktopPet.App;

public partial class CharacterGalleryWindow
{
    private static readonly RoutedUICommand ClearGalleryFiltersCommand =
        new("Clear gallery filters", nameof(ClearGalleryFiltersCommand), typeof(CharacterGalleryWindow));

    private bool GalleryHasFilters =>
        SearchBox.Text.Length > 0 ||
        CategoryCombo.SelectedValue is CharacterGalleryCategory cat && cat != CharacterGalleryCategory.All;

    private void InitializeGalleryShortcuts()
    {
        var findBinding = new KeyBinding(ApplicationCommands.Find, Key.F, ModifierKeys.Control);
        var clearBinding = new KeyBinding(ClearGalleryFiltersCommand, Key.L, ModifierKeys.Control);

        InputBindings.Add(findBinding);
        InputBindings.Add(clearBinding);

        CommandBindings.Add(new CommandBinding(ApplicationCommands.Find, Find_Executed, Find_CanExecute));
        CommandBindings.Add(new CommandBinding(ClearGalleryFiltersCommand, Clear_Executed, Clear_CanExecute));
    }

    private void Find_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = !_closed && SearchBox.IsEnabled && SearchBox.IsVisible;
        e.Handled = true;
    }

    private void Find_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        e.Handled = true;
        if (_closed || !SearchBox.IsEnabled || !SearchBox.IsVisible)
        {
            return;
        }

        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void Clear_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = !_closed && GalleryHasFilters;
        e.Handled = true;
    }

    private void Clear_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        e.Handled = true;
        if (_closed || !GalleryHasFilters)
        {
            return;
        }

        bool oldFiltering = _filtering;
        _filtering = true;
        try
        {
            SearchBox.Clear();
            CategoryCombo.SelectedValue = CharacterGalleryCategory.All;
        }
        finally
        {
            _filtering = oldFiltering;
        }

        FilterCharacters();
    }

    private void RefreshGalleryShortcutHelp()
    {
        if (_closed)
        {
            return;
        }

        AutomationProperties.SetAcceleratorKey(SearchBox, "Ctrl+F");
        AutomationProperties.SetHelpText(this, UiText.Get("Ctrl+F로 이름 검색, Ctrl+L로 검색·분류 초기화, F5로 새로고침합니다."));
    }
}
