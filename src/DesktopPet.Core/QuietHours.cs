namespace DesktopPet.Core;

/// <summary>Daily local wall-clock schedule; start inclusive, end exclusive, equal times disabled.</summary>
public static class QuietHours
{
    public static bool IsActive(bool enabled, int startMinute, int endMinute, DateTimeOffset localTime)
    {
        if (startMinute is < 0 or >= 1440) throw new ArgumentOutOfRangeException(nameof(startMinute));
        if (endMinute is < 0 or >= 1440) throw new ArgumentOutOfRangeException(nameof(endMinute));
        if (!enabled || startMinute == endMinute) return false;
        var minute = localTime.Hour * 60 + localTime.Minute;
        return startMinute < endMinute ? minute >= startMinute && minute < endMinute : minute >= startMinute || minute < endMinute;
    }

    public static string Format(int minute) => $"{minute / 60:00}:{minute % 60:00}";
    public static bool TryParse(string? text, out int minute)
    {
        minute = 0;
        if (text is null || text.Length != 5 || text[2] != ':' ||
            !char.IsAsciiDigit(text[0]) || !char.IsAsciiDigit(text[1]) || !char.IsAsciiDigit(text[3]) || !char.IsAsciiDigit(text[4]) ||
            !int.TryParse(text.AsSpan(0, 2), out var hour) || !int.TryParse(text.AsSpan(3, 2), out var part) ||
            hour is < 0 or > 23 || part is < 0 or > 59) return false;
        minute = hour * 60 + part;
        return true;
    }
}
