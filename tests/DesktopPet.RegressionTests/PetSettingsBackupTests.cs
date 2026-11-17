using System.Text.Json;
using DesktopPet.Core;

public static class PetSettingsBackupTests
{
    public static IEnumerable<(string Name, Action Run)> Create()
    {
        yield return ("Portable backups transfer behavior and timers while retaining machine-local assistant choices", () =>
        {
            var original = new PetSettings { CharacterName = "Nimbus", ScaleMultiplier = 1.5, FocusMinutes = 45, BreakMinutes = 10,
                ReducedMotion = true, MaximumFps = 30, QuietHoursEnabled = true, AutoConnectAssistants = true, AssistantSetupSeen = false };
            var json = PetSettingsBackup.WriteJson(original);
            Check(!json.Contains("AutoConnectAssistants") && !json.Contains("AssistantSetupSeen"), "Backup exported assistant connection preferences");
            var baseline = new PetSettings { AutoConnectAssistants = false, AssistantSetupSeen = true };
            var copy = PetSettingsBackup.ReadJson(json, baseline);
            Check(copy.CharacterName == "Nimbus" && copy.ScaleMultiplier == 1.5 && copy.FocusMinutes == 45 && copy.BreakMinutes == 10 &&
                copy.ReducedMotion && copy.MaximumFps == 30 && copy.QuietHoursEnabled && !copy.AutoConnectAssistants && copy.AssistantSetupSeen,
                "Backup lost portable values or changed local assistant preferences");
            Check(baseline.FocusMinutes == 25 && baseline.ScaleMultiplier == 1, "Import modified baseline before commit");
        });
        yield return ("Backup parsing rejects unknown, duplicate, excluded and mistyped fields before changing the baseline", () =>
        {
            var baseline = new PetSettings { ScaleMultiplier = 1.5 };
            foreach (var json in new[] { "{}", "null", "[]", "{\"schemaVersion\":2,\"product\":\"DesktopPet\",\"preferences\":{}}",
                         Wrap("\"AutoConnectAssistants\":true"), Wrap("\"ScaleMultiplier\":1,\"ScaleMultiplier\":2"),
                         Wrap("\"Unknown\":true"), Wrap("\"Visible\":\"yes\""), Wrap("\"ScaleMultiplier\":{}"),
                         "{\"schemaVersion\":1,\"schemaVersion\":1,\"product\":\"DesktopPet\",\"preferences\":{}}" })
                Reject(() => PetSettingsBackup.ReadJson(json, baseline));
            Check(baseline.ScaleMultiplier == 1.5, "Invalid backup mutated the baseline");
            Reject(() => PetSettingsBackup.ReadJson(new string('x', PetSettingsBackup.MaxBytes + 1), baseline));
            var normalized = PetSettingsBackup.ReadJson(Wrap("\"MaximumFps\":999,\"FocusMinutes\":9000,\"ScaleMultiplier\":-1"), baseline);
            Check(normalized.MaximumFps == 60 && normalized.FocusMinutes == 180 && normalized.ScaleMultiplier == 0.5, "Imported settings bypassed safe normalization");
        });
        yield return ("Partial portable backups preserve omitted preferences and bounded file reads support UTF-8 BOM", () => Fixture(folder =>
        {
            var baseline = new PetSettings { FocusMinutes = 40, QuietMode = true };
            var json = Wrap("\"MaximumFps\":30");
            var path = Path.Combine(folder, "backup.json");
            File.WriteAllText(path, json, new System.Text.UTF8Encoding(true));
            var result = PetSettingsBackup.ReadJson(PetSettingsBackup.ReadFile(path), baseline);
            Check(result.MaximumFps == 30 && result.FocusMinutes == 40 && result.QuietMode, "Partial backup reset omitted preferences");
            using (var stream = File.Create(path)) stream.SetLength(PetSettingsBackup.MaxBytes + 1);
            Reject(() => PetSettingsBackup.ReadFile(path));
        }));
        yield return ("Backup atomic replacement preserves locked destination bytes and removes only its own staging file", () => Fixture(folder =>
        {
            var path = Path.Combine(folder, "backup.json");
            PetSettingsBackup.SaveFile(path, new PetSettings { FocusMinutes = 35 });
            var original = File.ReadAllBytes(path);
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                Reject(() => PetSettingsBackup.SaveFile(path, new PetSettings { FocusMinutes = 50 }));
            Check(original.SequenceEqual(File.ReadAllBytes(path)) && Directory.GetFiles(folder).Length == 1, "Failed replacement changed old backup or leaked a temporary file");
            PetSettingsBackup.SaveFile(path, new PetSettings { FocusMinutes = 50 });
            Check(PetSettingsBackup.ReadJson(PetSettingsBackup.ReadFile(path), new()).FocusMinutes == 50, "Unlocked atomic replacement failed");
        }));
    }
    private static string Wrap(string properties) => "{\"schemaVersion\":1,\"product\":\"DesktopPet\",\"preferences\":{" + properties + "}}";
    private static void Reject(Action run)
    {
        try { run(); } catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException) { return; }
        throw new Exception("Invalid backup unexpectedly succeeded");
    }
    private static void Fixture(Action<string> run)
    {
        var temp = Path.GetFullPath(Path.GetTempPath());
        var root = Path.Combine(temp, "DesktopPet-Backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { run(root); }
        finally
        {
            var full = Path.GetFullPath(root);
            Check(full.StartsWith(Path.TrimEndingDirectorySeparator(temp) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(full).StartsWith("DesktopPet-Backup-"), "Unsafe fixture cleanup");
            Directory.Delete(full, true);
        }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
