namespace DesktopPet.App;

using System;
using System.Globalization;
using DesktopPet.Core;

public sealed class SavedNotificationItem
{
    internal InboxNotification Receipt { get; }
    public string Title { get; }
    public string Message { get; }
    public string Source { get; }
    public DateTimeOffset? CreatedAtUtc { get; }

    internal SavedNotificationItem(InboxNotification receipt)
    {
        Receipt = receipt;
        var n = receipt.Notification;
        Title = n.Title ?? string.Empty;
        Message = n.Message ?? string.Empty;
        Source = n.Source ?? string.Empty;
        CreatedAtUtc = n.CreatedAtUtc;
    }

    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? UiText.Get("알림") : Title;

    public string SourceLabel
    {
        get
        {
            var raw = Source?.Trim().ToLowerInvariant();
            return raw switch
            {
                "claude" => "Claude",
                "codex" => "Codex",
                "focus-timer" => UiText.Get("집중 타이머"),
                "test" => UiText.Get("테스트 알림"),
                "" or null => UiText.Get("기타"),
                _ => Source ?? UiText.Get("기타")
            };
        }
    }

    public string TimeLabel
    {
        get
        {
            if (!CreatedAtUtc.HasValue) return "—";
            return CreatedAtUtc.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        }
    }
}
