namespace DesktopPet.App;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

public sealed partial class NotificationReviewWindow : Window
{
    private readonly MainWindow _pet;
    private IReadOnlyList<SavedNotificationItem> _allItems = Array.Empty<SavedNotificationItem>();
    private bool _refreshing;
    private bool _closed;
    private DispatcherOperation? _pendingRefresh;

    public IReadOnlyList<SavedNotificationItem> AllItems => _allItems;

    public NotificationReviewWindow(MainWindow pet)
    {
        _pet = pet ?? throw new ArgumentNullException(nameof(pet));
        InitializeComponent();
        UiText.Attach(this);

        _pet.PendingNotificationCountChanged += OnPetCountChanged;
        UiText.LanguageChanged += OnLanguageChanged;

        Closed += OnClosed;
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
        CoalesceRefresh();
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
        var oldSelected = NotificationItems.SelectedItem as SavedNotificationItem;
        var oldReceipt = oldSelected?.Receipt;
        _refreshing = true;
        try
        {
            _allItems = _pet.GetPendingNotificationsSnapshot();
            NotificationItems.ItemsSource = _allItems;
            SavedNotificationItem? match = null;
            if (oldReceipt != null)
            {
                match = _allItems.FirstOrDefault(i => ReferenceEquals(i.Receipt, oldReceipt));
            }
            NotificationItems.SelectedItem = match ?? _allItems.FirstOrDefault();
        }
        finally
        {
            _refreshing = false;
            RenderDetails();
        }

        RenderCount();
    }

    private void RenderCount()
    {
        CountText.Text = UiText.Format("보관된 알림 {0}개", _allItems.Count);
    }

    private void RenderDetails()
    {
        var item = NotificationItems.SelectedItem as SavedNotificationItem;
        if (item == null)
        {
            NotificationDetails.Text = UiText.Get("보관된 알림이 없어요.");
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

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _closed = true;

        if (_pendingRefresh != null)
        {
            _pendingRefresh.Abort();
            _pendingRefresh = null;
        }

        _pet.PendingNotificationCountChanged -= OnPetCountChanged;
        UiText.LanguageChanged -= OnLanguageChanged;
        Closed -= OnClosed;
    }
}
