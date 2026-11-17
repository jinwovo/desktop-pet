using System.Text.Json;
using DesktopPet.Core;

public static class DiagnosticReportTests
{
    private static PetDiagnosticSnapshot Snapshot() => new(CharacterStorageKind.Imported, 128, 128, 19, 9, 2, 60, false, true, false, false, true, "en");
    public static IEnumerable<(string Name, Action Run)> Create()
    {
        yield return ("Diagnostic schema contains only allowlisted runtime facts and bounded presentation state", () =>
        {
            var json = DiagnosticReport.Create("0.30.0.0", Snapshot());
            using var document = JsonDocument.Parse(json);
            var fields = document.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var expected = new[] { "schemaVersion", "product", "appVersion", "runtimeVersion", "osVersion", "osArchitecture", "processArchitecture",
                "characterStorage", "FrameWidth", "FrameHeight", "DecodedTextures", "AvailableCharacters", "UnreadNotifications", "MaximumFps",
                "ReducedMotion", "ManuallyVisible", "TemporarilyPaused", "QuietNow", "HasLiveRig", "UiLanguage" };
            Check(fields.SetEquals(expected), "Diagnostic schema gained an unreviewed data field");
            Check(document.RootElement.GetProperty("characterStorage").GetString() == "Imported" && json.Length < 4096, "Diagnostic data is unexpectedly large or has no storage category");
            foreach (var forbidden in new[] { "UserName", "MachineName", "UserProfile", "ConfigPath", "ApiKey", "CharacterName", "Message", "Transcript", "Connection" })
                Check(!fields.Contains(forbidden), "Diagnostic exported private data");
        });
        yield return ("Diagnostic input cannot smuggle paths, names or arbitrary strings into the report", () =>
        {
            foreach (var version in new[] { "", "C:\\private\\profile", "1.0.0\n", "1.0.0 token", new string('1', 100) })
                RejectArgument(() => DiagnosticReport.Create(version, Snapshot()));
            RejectArgument(() => DiagnosticReport.Create("1.0.0", Snapshot() with { UiLanguage = "PRIVATE_CANARY" }));
            RejectArgument(() => DiagnosticReport.Create("1.0.0", Snapshot() with { FrameWidth = int.MaxValue }));
            RejectArgument(() => DiagnosticReport.Create("1.0.0", Snapshot() with { CharacterStorage = (CharacterStorageKind)123 }));
        });
    }
    private static void RejectArgument(Action run) { try { run(); } catch (ArgumentException) { return; } throw new Exception("Unreviewed diagnostic input accepted"); }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
