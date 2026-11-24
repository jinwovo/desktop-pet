using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopPet.App;
using DesktopPet.Core;
using WinForms = System.Windows.Forms;

internal static class NotificationSearchUiTests
{
    public static void Run(string root, bool capture = false)
    {
        var originalLanguage = UiText.Language;
        UiText.SetLanguage("en");
        var folder = Path.Combine(root, "notification-search-inbox");
        Directory.CreateDirectory(folder);

        var pet = new MainWindow(new CharacterLibrary(MainWindow.CharactersRoot, Path.Combine(root, "search-custom")), inboxDirectory: folder);
        pet.Loaded -= typeof(MainWindow).GetMethod("MainWindow_Loaded", BindingFlags.Instance | BindingFlags.NonPublic)!.CreateDelegate<RoutedEventHandler>(pet);
        var preferences = new PetSettings { Visible = false, QuietMode = true, AutoConnectAssistants = false };
        Set(pet, "_settings", preferences);
        var brain = new PetBrain(new PlatformLayout(new[] { new Platform(0, 1920, 1080) }), 128, 122, 500, 958);
        Set(pet, "_brain", brain);
        Set(pet, "_suppressionReasons", PetSuppressionReason.SessionLocked);
        Invoke(pet, "ApplyPresentation", CharacterPresentation.Load(Path.Combine(MainWindow.CharactersRoot, "Companion")));
        var saveTimer = (DispatcherTimer)Get(pet, "_settingsSaveTimer")!;
        saveTimer.Interval = TimeSpan.FromDays(1);

        var queue = new ConcurrentQueue<InboxNotification>();
        using var inbox = new NotificationInbox(folder);
        inbox.NotificationReceived += queue.Enqueue;
        inbox.Start();

        var factory = typeof(App).Assembly.GetType("DesktopPet.App.PetMenuBuilder")!.GetMethod("Create", BindingFlags.Static | BindingFlags.NonPublic)!;
        WinForms.ContextMenuStrip BuildMenu() => (WinForms.ContextMenuStrip)factory.Invoke(null, new object[] { pet, (Action)(() => { }), (Action)(() => { }), (Action)(() => { }) })!;
        using var tray = BuildMenu();
        using var petMenu = BuildMenu();
        WinForms.ToolStripMenuItem OpenItem(WinForms.ContextMenuStrip menu) => menu.Items.OfType<WinForms.ToolStripMenuItem>().Single(item => item.Text == UiText.Get("알림 목록..."));
        NotificationReviewWindow Window() => (NotificationReviewWindow)(Get(pet, "_notificationReviewWindow") ?? throw new Exception("Notification browser missing"));
        void Deliver(InboxNotification receipt) => Invoke(pet, "OnNotification", receipt);
        var acknowledgements = 0;
        InboxNotification Receipt(PetNotification notification) => (InboxNotification)typeof(InboxNotification)
            .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(PetNotification), typeof(Func<bool>) }, null)!
            .Invoke(new object[] { notification, (Func<bool>)(() => { acknowledgements++; return true; }) });

        try
        {
            OpenItem(petMenu).PerformClick(); Pump();
            var review = Window();
            Check(review.IsVisible && review.Owner is null && review.AllItems.Count == 0, "Empty browser did not open independently");

            var searchBox = (TextBox)review.FindName("SearchBox");
            var sourceFilter = (ComboBox)review.FindName("SourceFilterCombo");
            var clearButton = (Button)review.FindName("ClearFiltersButton");
            var listBox = (ListBox)review.FindName("NotificationItems");
            var details = (TextBox)review.FindName("NotificationDetails");
            var countText = (TextBox)review.FindName("CountText");

            Check(sourceFilter.Items.Count == 6, "Source filter must have 6 fixed choices");
            Check(sourceFilter.Items.Cast<object>().Select(item => item.GetType().GetProperty("Choice")!.GetValue(item)!.ToString()).SequenceEqual(
                new[] { "All", "Claude", "Codex", "FocusTimer", "Test", "Other" }), "Source filters lost their stable identities/order");

            Deliver(Receipt(new PetNotification { Source = " ClAuDe ", Title = "Claude Title", Message = "Claude body content", EventId = "1" }));
            Deliver(Receipt(new PetNotification { Source = " Codex ", Title = "Codex Title", Message = "Codex body with regex .*+?", EventId = "2" }));
            Deliver(Receipt(new PetNotification { Source = "focus-timer", Title = "Focus Title", Message = "Focus session complete", EventId = "3" }));
            Deliver(Receipt(new PetNotification { Source = "test", Title = "Test Title", Message = "Test notification body", EventId = "4" }));
            Deliver(Receipt(new PetNotification { Source = " my-tool ", Title = "Custom Title", Message = "Custom body 中文 한국어", EventId = "5" }));
            Deliver(Receipt(new PetNotification { Source = "", Title = "Empty Source", Message = "No source provided", EventId = "6" }));
            Pump(); review.UpdateLayout();

            Check(review.AllItems.Count == 6, "All 6 notifications should be present");
            Check(review.VisibleItems.Count == 6, "All 6 visible initially");
            Check(countText.Text == UiText.Format("보관된 알림 {0}개", 6), "Initial count shows all");
            listBox.SelectedIndex = -1;
            Check(details.Text == UiText.Get("알림을 선택하면 내용을 볼 수 있어요."), "Deselecting a visible row reported a false empty/search result");

            searchBox.Text = "CLAUDE";
            Pump(); review.UpdateLayout();
            Check(review.VisibleItems.Count == 1, "Search 'claude' matches 1");
            Check(review.VisibleItems[0].Title == "Claude Title", "Correct item matched");
            Check(countText.Text == UiText.Format("알림 {0} / {1}개 표시", 1, 6), "Filtered count localized");
            Check(listBox.Items.Count == 1 && ReferenceEquals(listBox.Items[0], review.VisibleItems[0]), "Visible list did not follow its filtered snapshot");
            searchBox.Text = "body content";
            Check(review.VisibleItems.Count == 1 && review.VisibleItems[0].Title == "Claude Title", "Message search failed");

            searchBox.Text = "  ";
            Pump(); review.UpdateLayout();
            Check(review.VisibleItems.Count == 6, "Whitespace-only query shows all rows");
            Check(searchBox.Text == "  ", "Raw draft preserved in search box");

            searchBox.Text = "regex .*+?";
            Pump(); review.UpdateLayout();
            Check(review.VisibleItems.Count == 1, "Regex metacharacters treated literal");
            Check(review.VisibleItems[0].Title == "Codex Title", "Matched Codex body with regex chars");

            searchBox.Text = "中文";
            Pump(); review.UpdateLayout();
            Check(review.VisibleItems.Count == 1, "Multilingual content search works");
            Check(review.VisibleItems[0].Title == "Custom Title", "Matched Korean/Chinese in body");

            searchBox.Text = " my-tool ";
            Pump(); review.UpdateLayout();
            Check(review.VisibleItems.Count == 1, "Raw source with whitespace matches");
            Check(review.VisibleItems[0].Title == "Custom Title", "Matched raw source");

            searchBox.Text = "";
            sourceFilter.SelectedIndex = 1;
            Pump(); review.UpdateLayout();
            Check(review.VisibleItems.Count == 1, "Source filter Claude matches 1");
            Check(review.VisibleItems[0].Title == "Claude Title", "Correct source filtered");

            sourceFilter.SelectedIndex = 2;
            Pump(); review.UpdateLayout();
            Check(review.VisibleItems.Count == 1, "Source filter Codex matches 1");
            Check(review.VisibleItems[0].Title == "Codex Title", "Correct source filtered");

            sourceFilter.SelectedIndex = 3;
            Pump(); review.UpdateLayout();
            Check(review.VisibleItems.Count == 1, "Source filter FocusTimer matches 1");
            Check(review.VisibleItems[0].Title == "Focus Title", "Correct source filtered");

            sourceFilter.SelectedIndex = 4;
            Pump(); review.UpdateLayout();
            Check(review.VisibleItems.Count == 1, "Source filter Test matches 1");
            Check(review.VisibleItems[0].Title == "Test Title", "Correct source filtered");

            sourceFilter.SelectedIndex = 5;
            Pump(); review.UpdateLayout();
            Check(review.VisibleItems.Count == 2, "Source filter Other matches custom + empty source");
            Check(review.VisibleItems.Any(i => i.Title == "Custom Title") && review.VisibleItems.Any(i => i.Title == "Empty Source"), "Both unknown and empty classified as Other");

            searchBox.Text = "title";
            sourceFilter.SelectedIndex = 1;
            Pump(); review.UpdateLayout();
            Check(review.VisibleItems.Count == 1, "AND filter: search + source Claude");
            Check(review.VisibleItems[0].Title == "Claude Title", "AND filter correct");

            searchBox.Text = "title";
            sourceFilter.SelectedIndex = 2;
            Pump(); review.UpdateLayout();
            Check(review.VisibleItems.Count == 1, "AND filter: search + source Codex");
            Check(review.VisibleItems[0].Title == "Codex Title", "AND filter correct");

            searchBox.Text = "nonexistent";
            Pump(); review.UpdateLayout();
            Check(review.VisibleItems.Count == 0, "No match shows zero visible");
            Check(countText.Text == UiText.Format("알림 {0} / {1}개 표시", 0, 6), "No-match count localized");
            Check(details.Text == UiText.Get("검색 결과가 없어요."), "No-match details localized");

            clearButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            listBox.SelectedIndex = 1;
            var selectedReceipt = GetReceipt((SavedNotificationItem)listBox.SelectedItem);
            searchBox.Text = "Title";
            Pump(); review.UpdateLayout();
            Check(ReferenceEquals(GetReceipt((SavedNotificationItem)listBox.SelectedItem), selectedReceipt), "Search lost a still-visible selected receipt");
            sourceFilter.SelectedIndex = 1;
            Check(((SavedNotificationItem)listBox.SelectedItem).Title == "Claude Title", "Excluded selection did not fall back to the first visible receipt");

            searchBox.Text = "";
            sourceFilter.SelectedIndex = 0;
            Pump(); review.UpdateLayout();
            Check(review.VisibleItems.Count == 6, "Clearing search restores all");
            Check(clearButton.IsEnabled == false, "Clear button disabled when no filters");

            searchBox.Text = "test";
            sourceFilter.SelectedIndex = 4;
            Pump(); review.UpdateLayout();
            Check(clearButton.IsEnabled == true, "Clear button enabled when filters active");
            clearButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump(); review.UpdateLayout();
            Check(searchBox.Text == "" && sourceFilter.SelectedIndex == 0 && review.VisibleItems.Count == 6, "Clear button restores full view");

            var focusIndex = 3;
            sourceFilter.SelectedIndex = focusIndex;
            searchBox.Text = "  ";
            Pump(); review.UpdateLayout();
            var selectedReceiptBefore = GetReceipt((SavedNotificationItem)listBox.SelectedItem);

            foreach (var locale in new[] { "en", "zh-CN", "ko" })
            {
                UiText.SetLanguage(locale); Pump(); review.UpdateLayout();
                Check(sourceFilter.SelectedIndex == focusIndex, $"Source filter index preserved in {locale}");
                Check(searchBox.Text == "  ", $"Raw search text preserved in {locale}");
                Check(review.VisibleItems.Count == 1, $"Focus filter still matches 1 in {locale}");
                Check(review.VisibleItems[0].Title == "Focus Title", $"Correct item in {locale}");
                Check(countText.Text == UiText.Format("알림 {0} / {1}개 표시", 1, 6), $"Count localized in {locale}");
                Check((string)sourceFilter.Items[focusIndex].GetType().GetProperty("Label")!.GetValue(sourceFilter.Items[focusIndex])! == UiText.Get("집중 타이머"), $"Source label localized in {locale}");
                Check(ReferenceEquals(GetReceipt((SavedNotificationItem)listBox.SelectedItem), selectedReceiptBefore), "Locale refresh lost the filtered selection");
                Check(AutomationProperties.GetName(searchBox) == UiText.Get("알림 검색"), $"Search automation name localized in {locale}");
                Check(AutomationProperties.GetName(sourceFilter) == UiText.Get("전체 출처"), $"Filter automation name localized in {locale}");
                if (capture) Program.SaveWindowSnapshot(review, $"notification-search-focus-{locale}.png");
            }

            UiText.SetLanguage("en"); Pump(); review.UpdateLayout();
            searchBox.Text = "Focus Timer";
            Pump(); review.UpdateLayout();
            Check(review.VisibleItems.Count == 1, "English sourceLabel query matches");
            Check(review.VisibleItems[0].Title == "Focus Title", "Matched English sourceLabel");

            UiText.SetLanguage("ko"); Pump(); review.UpdateLayout();
            Check(review.VisibleItems.Count == 0, "Korean locale: English sourceLabel query no match");
            Check(countText.Text == UiText.Format("알림 {0} / {1}개 표시", 0, 6), "No-match count in Korean");

            UiText.SetLanguage("zh-CN"); Pump(); review.UpdateLayout();
            Check(review.VisibleItems.Count == 0, "Chinese locale: English sourceLabel query no match");

            UiText.SetLanguage("en"); Pump(); review.UpdateLayout();
            searchBox.Text = "";
            sourceFilter.SelectedIndex = 3;
            searchBox.Text = "session";
            Pump(); review.UpdateLayout();

            Deliver(Receipt(new PetNotification { Source = "focus-timer", Title = "Arrival Match", Message = "New session", EventId = "7" }));
            Deliver(Receipt(new PetNotification { Source = "claude", Title = "Arrival NoMatch", Message = "New session", EventId = "8" }));
            Pump(); review.UpdateLayout();
            Check(review.AllItems.Count == 8, "All items updated with arrivals");
            Check(review.VisibleItems.Count == 2 && listBox.Items.Count == 2, "Only matching arrivals should be visible in the filtered list");
            Check(review.VisibleItems.Any(i => i.Title == "Arrival Match") && review.VisibleItems.Any(i => i.Title == "Focus Title"), "Correct items visible");
            Check(listBox.SelectedItem != null && ReferenceEquals(GetReceipt((SavedNotificationItem)listBox.SelectedItem), selectedReceiptBefore), "Selection sticky after filtered arrivals");
            Check(acknowledgements == 0 && !pet.IsPetVisible && Get(pet, "_bubble") is null && !(bool)Get(pet, "_settingsDirty")!, "Search/arrivals changed read state or pet preferences");
            Console.WriteLine("PASS Literal body/source search and combined source filters preserve raw drafts, selected receipts and live localized counts");

            var ackBefore = acknowledgements;
            var bubbleBefore = Get(pet, "_bubble");
            var petVisibleBefore = pet.IsPetVisible;
            var settingsDirtyBefore = (bool)Get(pet, "_settingsDirty")!;
            var poseBefore = (brain.X, brain.Y, brain.State, brain.StateElapsedMs);

            clearButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump(); review.UpdateLayout();
            Check(review.VisibleItems.Count == 8, "Clear restores all after arrivals");
            Check(acknowledgements == ackBefore, "No acknowledgements triggered");
            Check(Get(pet, "_bubble") == bubbleBefore, "No bubble shown");
            Check(pet.IsPetVisible == petVisibleBefore, "Pet visibility unchanged");
            Check((bool)Get(pet, "_settingsDirty")! == settingsDirtyBefore, "Settings not dirtied");
            Check((brain.X, brain.Y, brain.State, brain.StateElapsedMs) == poseBefore, "Pet motion unchanged");

            searchBox.Text = "find selection";
            Check(ApplicationCommands.Find.CanExecute(null, review), "Find command was disabled on the open browser");
            ApplicationCommands.Find.Execute(null, review); Pump();
            Check(searchBox.IsKeyboardFocused && searchBox.SelectedText == "find selection", "Ctrl+F command did not focus/select the search draft");

            searchBox.Text = "focus";
            sourceFilter.SelectedIndex = 3;
            Pump(); review.UpdateLayout();
            Check(clearButton.IsEnabled == true, "Filters active");
            var escapeArgs = new KeyEventArgs(InputManager.Current.PrimaryKeyboardDevice, PresentationSource.FromVisual(review), 0, Key.Escape)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent
            };
            review.RaiseEvent(escapeArgs);
            Pump(); review.UpdateLayout();
            Check(escapeArgs.Handled == true, "Escape handled when filters active");
            Check(searchBox.Text == "" && sourceFilter.SelectedIndex == 0, "Escape clears filters");
            Check(review.IsVisible == true, "Window still visible after Escape with filters");

            escapeArgs = new KeyEventArgs(InputManager.Current.PrimaryKeyboardDevice, PresentationSource.FromVisual(review), 0, Key.Escape)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent
            };
            review.RaiseEvent(escapeArgs);
            Pump(); review.UpdateLayout();
            Check(escapeArgs.Handled == false, "Escape not handled when no filters");
            Check(review.IsVisible == true, "Window still visible (no auto-close assertion)");
            searchBox.Text = "  ";
            Check(clearButton.IsEnabled, "Whitespace draft could not be cleared");
            var whitespaceEscape = new KeyEventArgs(InputManager.Current.PrimaryKeyboardDevice, PresentationSource.FromVisual(review), 0, Key.Escape)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            review.RaiseEvent(whitespaceEscape);
            Check(whitespaceEscape.Handled && searchBox.Text == "" && review.IsVisible, "Escape closed the browser instead of clearing the raw whitespace draft");

            Deliver(Receipt(new PetNotification { Source = "custom", Title = new string('W', 180), Message = "Full long title remains in details", EventId = "long-title" }));
            Pump();
            foreach (var locale in new[] { "en", "zh-CN", "ko" })
            {
                UiText.SetLanguage(locale); Pump();
                review.Width = review.MinWidth; review.Height = review.MinHeight; review.UpdateLayout();
                Check(searchBox.ActualWidth >= 80 && searchBox.TranslatePoint(new Point(searchBox.ActualWidth, 0), review).X <= sourceFilter.TranslatePoint(new Point(0, 0), review).X &&
                    clearButton.TranslatePoint(new Point(clearButton.ActualWidth, 0), review).X < review.ActualWidth,
                    "Search/filter controls overlapped or overflowed at minimum window size");
                listBox.SelectedIndex = 8; listBox.ScrollIntoView(listBox.SelectedItem); review.UpdateLayout(); Pump();
                var row = (ListBoxItem)listBox.ItemContainerGenerator.ContainerFromIndex(8);
                var title = Descendants<TextBlock>(row).Single(block => block.Text == new string('W', 180));
                Check(title.TextTrimming == TextTrimming.CharacterEllipsis && title.ActualWidth > 0 && title.ActualWidth <= listBox.ActualWidth &&
                    details.Text.Contains(new string('W', 180)), "Long title was unbounded in the row or lost from selectable full details");
                if (capture) Program.SaveWindowSnapshot(review, $"notification-search-long-title-{locale}.png");
            }
            Console.WriteLine("PASS Native Find/Escape commands, deselection hints and minimum-size long-title layout remain usable in all three locales");

            review.Close();
            Pump();
            Check(Get(pet, "_notificationReviewWindow") is null, "Closed browser released from Main");

            searchBox.Text = "should not work";
            sourceFilter.SelectedIndex = 1;
            clearButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump();
            Check(review.AllItems.Count == 9, "Closed browser AllItems unchanged");
            Check(review.VisibleItems.Count == 9, "Closed browser VisibleItems unchanged (frozen)");

            pet.ShowNotificationReview(); Pump();
            var reopened = Window();
            Check(!ReferenceEquals(reopened, review), "Reopened gets new window instance");
            Check(reopened.AllItems.Count == 9, "Reopened has current snapshot");

            saveTimer.Stop(); Set(pet, "_settingsDirty", false); pet.DisposeResources(); Pump();
            Check(!reopened.IsVisible && Get(pet, "_notificationReviewWindow") is null, "Main disposal closes browser");
            Check(pet.GetPendingNotificationsSnapshot().Count == 0, "Disposed Main exposed stale notifications");

            pet.ShowNotificationReview();
            Check(Get(pet, "_notificationReviewWindow") is null, "Disposed Main cannot open browser");

            Console.WriteLine("PASS Notification search/filter behavior verified across locales, arrivals, and lifecycle");
        }
        finally
        {
            saveTimer.Stop(); Set(pet, "_settingsDirty", false); pet.DisposeResources(); pet.Close(); UiText.SetLanguage(originalLanguage);
        }
    }

    private static object GetReceipt(SavedNotificationItem item) => typeof(SavedNotificationItem).GetProperty("Receipt", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(item)!;

    private static IEnumerable<T> Descendants<T>(DependencyObject node) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static object? Get(object obj, string name) => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(obj);
    private static void Set(object obj, string name, object value) => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(obj, value);
    private static object? Invoke(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(obj, args);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

}
