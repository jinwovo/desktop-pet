using DesktopPet.Core;

public static class QuietHoursTests
{
    public static IEnumerable<(string Name, Action Run)> Create()
    {
        yield return ("Quiet-hours boundaries are inclusive at start and exclusive at end across midnight", () =>
        {
            foreach (var minute in Enumerable.Range(0, 1440))
            {
                var time = new DateTimeOffset(2026, 11, 17, minute / 60, minute % 60, 0, TimeSpan.FromHours(9));
                Check(QuietHours.IsActive(true, 1320, 480, time) == (minute >= 1320 || minute < 480), "Midnight quiet interval mismatch");
                Check(QuietHours.IsActive(true, 540, 1020, time) == (minute >= 540 && minute < 1020), "Day quiet interval mismatch");
                Check(!QuietHours.IsActive(false, 1320, 480, time) && !QuietHours.IsActive(true, 500, 500, time), "Disabled or equal interval became active");
            }
        });
        yield return ("Quiet-hours wall-clock input rejects ambiguous formats and round-trips every valid minute", () =>
        {
            for (var minute = 0; minute < 1440; minute++)
                Check(QuietHours.TryParse(QuietHours.Format(minute), out var parsed) && parsed == minute, "Minute format did not round-trip");
            foreach (var bad in new string?[] { null, "", "1:00", "24:00", "12:60", "-0:00", "+1:00", " 1:00", "12: 0", "12:00 ", "aa:bb" })
                Check(!QuietHours.TryParse(bad, out _), "Ambiguous local time accepted");
            try { QuietHours.IsActive(false, -1, 1440, DateTimeOffset.Now); throw new Exception("Invalid minute accepted"); }
            catch (ArgumentOutOfRangeException) { }
        });
        yield return ("Quiet schedule defaults stay off and normalization does not change manual quiet preference", () =>
        {
            var old = System.Text.Json.JsonSerializer.Deserialize<PetSettings>("{\"QuietMode\":true}")!;
            Check(!old.QuietHoursEnabled && old.QuietHoursStartMinute == 1320 && old.QuietHoursEndMinute == 480, "Old preferences enabled a schedule");
            old.QuietHoursStartMinute = -1; old.QuietHoursEndMinute = 9000; old.Normalize();
            Check(old.QuietHoursStartMinute == 0 && old.QuietHoursEndMinute == 1439 && old.QuietMode, "Schedule normalization overwrote manual quiet");
        });
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
