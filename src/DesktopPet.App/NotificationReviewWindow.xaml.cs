namespace DesktopPet.App;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

public sealed partial class NotificationReviewWindow : Window
{
    private readonly MainWindow _pet;
    private IReadOnlyList<SavedNotificationItem> _allItems = Array.Empty<SavedNotificationItem>();
    private bool _selectionBusy;
    private SavedNotificationItem? _failedSelection;
    private IReadOnlyList<SavedNotificationItem> _visibleItems = Array.Empty<SavedNotificationItem>();
    private string _searchText = string.Empty;
    private SourceChoice _sourceFilter = SourceChoice.All;
    private bool _refreshing;
    private bool _filtering = true;
    private bool _closed;
    private bool _sourceLabelsDirty = true;
    private DispatcherOperation? _pendingRefresh;

    public IReadOnlyList<SavedNotificationItem> AllItems => _allItems;
    public IReadOnlyList<SavedNotificationItem> VisibleItems => _visibleItems;

    private enum SourceChoice
    {
        All,
        Claude,
        Codex,
        FocusTimer,
        Test,
        Other
    }

    public NotificationReviewWindow(MainWindow pet, Func<string?>? chooseExportPath = null,
        Func<IReadOnlyList<SavedNotificationItem>, string, CancellationToken, Task>? notificationExporter = null)
    {
        _pet = pet ?? throw new ArgumentNullException(nameof(pet));
        InitializeComponent();
        InitializeNotificationExport(chooseExportPath, notificationExporter);
        UiText.Attach(this);

        _pet.PendingNotificationCountChanged += OnPetCountChanged;
        UiText.LanguageChanged += OnLanguageChanged;

        Closed += OnClosed;

        RefreshSourceChoices();
        _filtering = false;

        RefreshSnapshot();
    }

    private void OnPetCountChanged(int _)
    {
        if (_closed) return;
        CoalesceRefresh();
    }

    private void OnLanguageChanged()
    {
        if (_closed) return;
        _sourceLabelsDirty = true;
        CoalesceRefresh();
    }

    private void RefreshSourceChoices()
    {
        var wasFiltering = _filtering;
        _filtering = true;
        try
        {
            var choices = new[]
            {
                new SourceChoiceItem(SourceChoice.All, UiText.Get("전체 출처")),
                new SourceChoiceItem(SourceChoice.Claude, "Claude"),
                new SourceChoiceItem(SourceChoice.Codex, "Codex"),
                new SourceChoiceItem(SourceChoice.FocusTimer, UiText.Get("집중 타이머")),
                new SourceChoiceItem(SourceChoice.Test, UiText.Get("테스트 알림")),
                new SourceChoiceItem(SourceChoice.Other, UiText.Get("기타"))
            };
            SourceFilterCombo.ItemsSource = choices;
            SourceFilterCombo.SelectedItem = choices.Single(choice => choice.Choice == _sourceFilter);
            AutomationProperties.SetName(SearchBox, UiText.Get("알림 검색"));
            AutomationProperties.SetName(SourceFilterCombo, UiText.Get("전체 출처"));
            _sourceLabelsDirty = false;
        }
        finally { _filtering = wasFiltering; }
    }

    private void CoalesceRefresh()
    {
        if (_pendingRefresh != null) return;
        _pendingRefresh = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _pendingRefresh = null;
            if (_closed) return;
            RefreshSnapshot();
        }));
    }

    private void RefreshSnapshot()
    {
        if (_closed) return;
        if (_sourceLabelsDirty) RefreshSourceChoices();
        _allItems = _pet.GetPendingNotificationsSnapshot();
        Refilter();
    }

    private void Refilter()
    {
        var oldSelected = NotificationItems.SelectedItem as SavedNotificationItem;
        var oldReceipt = oldSelected?.Receipt;
        _refreshing = true;
        try
        {
            ApplyFilters();
            NotificationItems.ItemsSource = _visibleItems;

            SavedNotificationItem? match = null;
            if (oldReceipt != null)
            {
                match = _visibleItems.FirstOrDefault(i => ReferenceEquals(i.Receipt, oldReceipt));
            }
            NotificationItems.SelectedItem = match ?? _visibleItems.FirstOrDefault();
        }
        finally
        {
            _refreshing = false;
            RenderDetails();
        }

        RenderCount();
    }

    private void Filter_Changed(object sender, TextChangedEventArgs e)
    {
        if (_filtering || _closed) return;
        _searchText = SearchBox.Text.Trim();
        Refilter();
    }

    private void SourceFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_filtering || _closed) return;
        if (SourceFilterCombo.SelectedItem is SourceChoiceItem item)
        {
            _sourceFilter = item.Choice;
        }
        Refilter();
    }

    private void ClearFilters_Click(object sender, RoutedEventArgs e)
    {
        if (_closed) return;
        _filtering = true;
        try
        {
            SearchBox.Text = string.Empty;
            SourceFilterCombo.SelectedIndex = 0;
        }
        finally
        {
            _filtering = false;
        }

        _searchText = string.Empty;
        _sourceFilter = SourceChoice.All;
        Refilter();
    }

    private void ApplyFilters()
    {
        var query = _searchText;
        var source = _sourceFilter;

        IEnumerable<SavedNotificationItem> filtered = _allItems;

        if (query.Length > 0)
        {
            filtered = filtered.Where(i =>
                ContainsOrdinalIgnoreCase(i.Title, query) ||
                ContainsOrdinalIgnoreCase(i.Message, query) ||
                ContainsOrdinalIgnoreCase(i.Source, query) ||
                ContainsOrdinalIgnoreCase(i.SourceLabel, query));
        }

        if (source != SourceChoice.All)
        {
            filtered = filtered.Where(i => ClassifySource(i.Source) == source);
        }

        _visibleItems = filtered.ToList().AsReadOnly();
    }

    private static bool ContainsOrdinalIgnoreCase(string? haystack, string needle)
    {
        if (haystack == null) return false;
        return haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static SourceChoice ClassifySource(string? source)
    {
        var raw = source?.Trim().ToLowerInvariant();
        return raw switch
        {
            "claude" => SourceChoice.Claude,
            "codex" => SourceChoice.Codex,
            "focus-timer" => SourceChoice.FocusTimer,
            "test" => SourceChoice.Test,
            "" or null => SourceChoice.Other,
            _ => SourceChoice.Other
        };
    }

    private void RenderCount()
    {
        if (_searchText.Length == 0 && _sourceFilter == SourceChoice.All)
        {
            CountText.Text = UiText.Format("보관된 알림 {0}개", _allItems.Count);
        }
        else
        {
            CountText.Text = UiText.Format("알림 {0} / {1}개 표시", _visibleItems.Count, _allItems.Count);
        }

        ClearFiltersButton.IsEnabled = SearchBox.Text.Length > 0 || _sourceFilter != SourceChoice.All;
        RefreshExportUi();
    }

    private void RefreshSelectionButton()
    {
        if (MarkSelectedReadButton == null) return;
        if (_closed)
        {
            MarkSelectedReadButton.IsEnabled = false;
            return;
        }

        var item = NotificationItems.SelectedItem as SavedNotificationItem;
        MarkSelectedReadButton.IsEnabled = !_selectionBusy && item != null && !_pet.IsAcknowledgingAll;
        MarkSelectedReadButton.Content = UiText.Get(_selectionBusy ? "선택한 알림 읽음 처리 중…" :
            _failedSelection is not null && ReferenceEquals(item?.Receipt, _failedSelection.Receipt) ? "읽음 처리 재시도" : "선택한 알림 읽음 처리");
    }

    private void RenderDetails()
    {
        var item = NotificationItems.SelectedItem as SavedNotificationItem;
        RefreshSelectionButton();
        if (item == null)
        {
            NotificationDetails.Text = _allItems.Count == 0 ? UiText.Get("보관된 알림이 없어요.") :
                _visibleItems.Count == 0 ? UiText.Get("검색 결과가 없어요.") :
                UiText.Get("알림을 선택하면 내용을 볼 수 있어요.");
            return;
        }

        var lines = new List<string>
        {
            item.DisplayTitle,
            item.SourceLabel,
            item.TimeLabel,
            item.Message
        };
        NotificationDetails.Text = string.Join(Environment.NewLine, lines);
    }

    private void NotificationItems_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_closed || _refreshing) return;
        RenderDetails();
    }

    private async void MarkSelectedRead_Click(object sender, RoutedEventArgs e)
    {
        if (_closed || _selectionBusy) return;
        if (_pet.IsAcknowledgingAll) { RefreshSelectionButton(); return; }
        var item = NotificationItems.SelectedItem as SavedNotificationItem;
        if (item == null) return;

        _selectionBusy = true;
        RefreshSelectionButton();

        try
        {
            var removed = await _pet.AcknowledgeSavedNotificationAsync(item);
            if (!_closed) _failedSelection = removed == 0 ? item : null;
        }
        catch
        {
            if (!_closed) _failedSelection = item;
        }
        finally
        {
            if (!_closed)
            {
                _selectionBusy = false;
                RefreshSnapshot();
            }
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_closed) return;
        if (e.Key == Key.Escape && ClearFiltersButton.IsEnabled)
        {
            ClearFilters_Click(sender, e);
            e.Handled = true;
        }
    }

    private void Find_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = !_closed;
        e.Handled = true;
    }

    private void Find_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (_closed) return;
        SearchBox.Focus();
        SearchBox.SelectAll();
        e.Handled = true;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _closed = true;
        CancelNotificationExport();

        if (_pendingRefresh != null)
        {
            _pendingRefresh.Abort();
            _pendingRefresh = null;
        }

        _pet.PendingNotificationCountChanged -= OnPetCountChanged;
        UiText.LanguageChanged -= OnLanguageChanged;
        Closed -= OnClosed;
    }

    private sealed class SourceChoiceItem
    {
        public SourceChoice Choice { get; }
        public string Label { get; }

        public SourceChoiceItem(SourceChoice choice, string label)
        {
            Choice = choice;
            Label = label;
        }

        public override string ToString() => Label;
    }
}
