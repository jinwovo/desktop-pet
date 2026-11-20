using System.Reflection;
using System.Windows.Controls;
using DesktopPet.App;

public static class SpeechBubbleLocalizationTests
{
    public static void Run()
    {
        var originalLanguage = UiText.Language;
        var baselineSubscribers = SubscriberCount();
        SpeechBubbleWindow? bubble = null;
        try
        {
            UiText.SetLanguage("ko");
            bubble = new SpeechBubbleWindow();
            Check(!bubble.IsShowing && !bubble.IsVisible, "Constructing the bubble left a visible notification");
            Check(SubscriberCount() == baselineSubscribers + 1, "Bubble did not own exactly one language-change subscription");
            var title = (TextBlock)bubble.FindName("TitleText");
            var message = (TextBlock)bubble.FindName("MessageText");
            var acknowledgement = (TextBlock)bubble.FindName("AcknowledgementText");
            const string rawMessage = "집중 시작\nKeep this user's message 原文 그대로.";

            bubble.ShowMessage("  ", rawMessage, 1);
            bubble.SetPendingCount(1);
            foreach (var (language, expectedTitle, pending, empty) in Languages())
            {
                UiText.SetLanguage(language);
                Check(title.Text == expectedTitle && acknowledgement.Text == pending, "Fallback title or pending acknowledgement missed a live language change");
                Check(message.Text == rawMessage, "Localizing bubble chrome changed the notification message");
                bubble.SetPendingCount(0);
                Check(acknowledgement.Text == empty, "Zero-pending acknowledgement remained in another language");
                bubble.SetPendingCount(1);
            }
            Console.WriteLine("PASS Bubble fallback title and zero/one pending acknowledgements update in KO, EN and Chinese");

            // The title deliberately matches an application translation key: it is still user text.
            const string rawTitle = "집중 시간 완료";
            bubble.ShowMessage(rawTitle, rawMessage, 1);
            foreach (var (language, _, pending, _) in Languages())
            {
                UiText.SetLanguage(language);
                Check(title.Text == rawTitle && message.Text == rawMessage && acknowledgement.Text == pending,
                    "Language switching translated or replaced a caller-supplied title/message");
            }
            bubble.ShowMessage("알림", rawMessage, 1);
            UiText.SetLanguage("en");
            Check(title.Text == "알림", "An explicit user title was mistaken for the localized blank-title fallback");
            Console.WriteLine("PASS Bubble language switching preserves sender titles and messages even when they match UI keys");

            bubble.Dismiss();
            Check(!bubble.IsShowing && !bubble.IsVisible, "Dismissed bubble stayed visible");
            bubble.ShowMessage(string.Empty, rawMessage, 1);
            bubble.SetPendingCount(0);
            UiText.SetLanguage("zh-CN");
            Check(bubble.IsShowing && title.Text == "通知" && message.Text == rawMessage && acknowledgement.Text == "点击确认",
                "Reopened bubble lost its localized chrome or sender message");
            UiText.SetLanguage("ko");
            var closedTitle = title.Text;
            var closedAcknowledgement = acknowledgement.Text;
            bubble.Shutdown();
            Check(!bubble.IsShowing && !bubble.IsVisible && SubscriberCount() == baselineSubscribers,
                "Shutting down the bubble retained a visible window or static language subscription");
            UiText.SetLanguage("en");
            Check(title.Text == closedTitle && acknowledgement.Text == closedAcknowledgement && message.Text == rawMessage,
                "Closed bubble was changed by a later language event");
            bubble.Shutdown();
            Check(SubscriberCount() == baselineSubscribers, "Repeated shutdown changed another window's language subscriptions");
            Console.WriteLine("PASS Hidden/reopened bubbles retain localization and shutdown releases language subscriptions exactly once");
        }
        finally
        {
            bubble?.Shutdown();
            UiText.SetLanguage(originalLanguage);
        }
    }

    private static IEnumerable<(string Language, string Title, string Pending, string Empty)> Languages()
    {
        yield return ("ko", "알림", "클릭해서 확인 · 대기 1건", "클릭해서 확인");
        yield return ("en", "Notification", "Click to acknowledge · 1 pending", "Click to acknowledge");
        yield return ("zh-CN", "通知", "点击确认 · 待处理 1 条", "点击确认");
        yield return ("ko", "알림", "클릭해서 확인 · 대기 1건", "클릭해서 확인");
    }

    private static int SubscriberCount()
    {
        var field = typeof(UiText).GetField("LanguageChanged", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new Exception("Cannot inspect the language event lifetime");
        return (field.GetValue(null) as Delegate)?.GetInvocationList().Length ?? 0;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
