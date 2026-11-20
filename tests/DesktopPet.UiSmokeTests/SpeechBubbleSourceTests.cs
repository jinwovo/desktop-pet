using System;
using System.Windows;
using System.Windows.Controls;
using DesktopPet.App;

public static class SpeechBubbleSourceTests
{
    public static void Run()
    {
        var savedLanguage = UiText.Language;
        var bubble = new SpeechBubbleWindow();
        var acknowledgements = 0;
        bubble.Acknowledged += () => acknowledgements++;
        try
        {
            bubble.SetReducedMotion(true);
            var sourceText = (TextBlock)bubble.FindName("SourceText");
            var titleText = (TextBlock)bubble.FindName("TitleText");
            var messageText = (TextBlock)bubble.FindName("MessageText");
            var ackText = (TextBlock)bubble.FindName("AcknowledgementText");


            bubble.ShowMessage("집중 시작", "原文 body unchanged", 1, source: "claude");
            Check(sourceText.Text == "Claude" && sourceText.Visibility == Visibility.Visible, "Claude source missing");
            Check(titleText.Text == "집중 시작" && messageText.Text == "原文 body unchanged", "Claude raw text changed");

            bubble.ShowMessage("집중 시작", "原文 body unchanged", 1, source: " CODEX ");
            Check(sourceText.Text == "Codex" && sourceText.Visibility == Visibility.Visible, "Codex source missing");
            Check(titleText.Text == "집중 시작" && messageText.Text == "原文 body unchanged", "Codex raw text changed");
            bubble.ShowMessage("Codex", "body", 1, source: "codex");
            Check(sourceText.Text == "Codex" && sourceText.Visibility == Visibility.Collapsed, "Matching source and title must not render duplicate lines");

            var locales = new[] { ("ko", "집중 타이머", "테스트 알림"), ("en", "Focus timer", "Test notification"), ("zh-CN", "专注计时器", "测试通知") };
            bubble.ShowMessage("집중 시작", "原文 body unchanged", 1, source: "focus-timer");
            bubble.SetPendingCount(2);
            foreach (var (lang, focusLabel, _) in locales)
            {
                UiText.SetLanguage(lang);
                Check(sourceText.Text == focusLabel && sourceText.Visibility == Visibility.Visible, "Live focus source localization failed");
                Check(titleText.Text == "집중 시작" && messageText.Text == "原文 body unchanged", "Source translation changed raw content");
                Check(bubble.IsShowing && bubble.IsVisible && bubble.Opacity == 1 && !bubble.HasAnimatedProperties, "Source translation changed visibility or motion");
                Check(ackText.Text == UiText.Format("클릭해서 확인 · 대기 {0}건", 2), "Source translation reset pending count");
            }
            bubble.ShowMessage("집중 시작", "原文 body unchanged", 1, source: "test");
            foreach (var (lang, _, testLabel) in locales)
            {
                UiText.SetLanguage(lang);
                Check(sourceText.Text == testLabel, "Test notification source localization failed");
            }
            bubble.ShowMessage("타이틀", "본문", 1, source: "untrusted sender");
            Check(sourceText.Text == string.Empty && sourceText.Visibility == Visibility.Collapsed, "Unknown sender echoed into UI");

            bubble.ShowMessage("타이틀", "본문", 1, source: "");
            Check(string.IsNullOrEmpty(sourceText.Text) && sourceText.Visibility == Visibility.Collapsed, "Empty source not collapsed");
            bubble.ShowMessage("타이틀", "본문", 1, source: "   ");
            Check(string.IsNullOrEmpty(sourceText.Text) && sourceText.Visibility == Visibility.Collapsed, "Whitespace source not collapsed");
            bubble.ShowMessage("타이틀", "본문", 1, source: null);
            Check(string.IsNullOrEmpty(sourceText.Text) && sourceText.Visibility == Visibility.Collapsed, "Null source not collapsed");

            bubble.ShowMessage("", "원본 메시지", 1, source: "claude");
            Check(titleText.Text == UiText.Get("알림") && sourceText.Text == "Claude", "Fallback title or source failed");

            bubble.SetPendingCount(2);
            Check(sourceText.Text == "Claude" && acknowledgements == 0, "Badge update acknowledged a receipt or lost attribution");

            Console.WriteLine("PASS: SpeechBubbleSourceTests");
        }
        finally
        {
            bubble.Shutdown();
            UiText.SetLanguage(savedLanguage);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception($"FAIL: {message}");
    }
}
