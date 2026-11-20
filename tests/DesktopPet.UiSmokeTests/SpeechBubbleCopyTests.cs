using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DesktopPet.App;

public static class SpeechBubbleCopyTests
{
    public static void Run(bool capture = false)
    {
        var originalLanguage = UiText.Language;
        var attempts = new List<string>();
        var mode = 0;
        var bubble = new SpeechBubbleWindow(text =>
        {
            attempts.Add(text);
            if (mode == 1) throw new ExternalException("Synthetic busy clipboard");
            return mode != 2;
        });
        var acknowledgements = 0;
        bubble.Acknowledged += () => acknowledgements++;
        try
        {
            UiText.SetLanguage("ko");
            bubble.SetReducedMotion(true);
            var button = (Button)bubble.FindName("CopyButton");
            bubble.ShowMessage("Keep title", "원문\nbody", 1, source: "codex");
            bubble.SetPendingCount(2);
            var focused = Keyboard.FocusedElement;
            button.ApplyTemplate();
            var surface = (Border)button.Template.FindName("CopySurface", button);
            surface.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.MouseDownEvent });
            button.ReleaseMouseCapture();
            Check(acknowledgements == 0 && bubble.IsShowing, "Copy-button mouse down acknowledged the notification");
            Click(button);
            Check(attempts.SequenceEqual(new[] { "Keep title" + Environment.NewLine + "원문\nbody" }), "Copy changed the sender's text");
            Check((string)button.Content == "복사했어요" && acknowledgements == 0 && bubble.IsShowing && bubble.IsVisible,
                "Copy dismissed or acknowledged the notification");
            Check(ReferenceEquals(focused, Keyboard.FocusedElement), "Copy button changed keyboard focus");
            if (capture) Program.SaveWindowSnapshot(bubble, "notification-copy-ko.png");
            Check(((TextBlock)bubble.FindName("TitleText")).Text == "Keep title" && ((TextBlock)bubble.FindName("MessageText")).Text == "원문\nbody" &&
                ((TextBlock)bubble.FindName("SourceText")).Text == "Codex" && ((TextBlock)bubble.FindName("AcknowledgementText")).Text.Contains("2"),
                "Copy changed the notification or unread count");
            UiText.SetLanguage("en");
            Check((string)button.Content == "Copied", "Copy success did not follow English locale");
            if (capture) Program.SaveWindowSnapshot(bubble, "notification-copy-en.png");
            UiText.SetLanguage("zh-CN");
            Check((string)button.Content == "已复制" && attempts.Count == 1 && acknowledgements == 0, "Locale change copied again or acknowledged");
            if (capture) Program.SaveWindowSnapshot(bubble, "notification-copy-zh.png");

            UiText.SetLanguage("en"); mode = 1; Click(button);
            Check((string)button.Content == "Copy again" && bubble.IsShowing && acknowledgements == 0, "Busy clipboard was not recoverable");
            mode = 2; Click(button);
            Check((string)button.Content == "Copy again" && acknowledgements == 0, "Failed copy was reported as success");
            mode = 0; Click(button);
            Check((string)button.Content == "Copied" && attempts.Count == 4 && acknowledgements == 0, "Retry did not copy without acknowledgement");
            bubble.ShowMessage("Only title", "", 1);
            Check((string)button.Content == "Copy", "New notification retained stale success feedback");
            Click(button);
            Check(attempts.Last() == "Only title", "Empty body added an artificial newline");
            var before = attempts.Count;
            bubble.Dismiss(); Click(button);
            bubble.Shutdown(); Click(button);
            Check(attempts.Count == before && acknowledgements == 0, "Hidden or closed notification copied or acknowledged again");
            Console.WriteLine("PASS Copying preserves unread notifications, handles busy clipboard retries, localizes feedback and never accesses the real clipboard in tests");
        }
        finally { bubble.Shutdown(); UiText.SetLanguage(originalLanguage); }
    }
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
