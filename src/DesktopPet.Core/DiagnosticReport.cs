using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DesktopPet.Core;

public enum CharacterStorageKind { Bundled, Imported }
public sealed record PetDiagnosticSnapshot(CharacterStorageKind CharacterStorage, int FrameWidth, int FrameHeight,
    int DecodedTextures, int AvailableCharacters, int UnreadNotifications, int MaximumFps, bool ReducedMotion,
    bool ManuallyVisible, bool TemporarilyPaused, bool QuietNow, bool HasLiveRig, string UiLanguage);

/// <summary>Fresh allowlisted facts only; never reads logs, environment variables, paths or notification bodies.</summary>
public static class DiagnosticReport
{
    public static string Create(string appVersion, PetDiagnosticSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (appVersion is null || appVersion.Length > 60 || !Regex.IsMatch(appVersion, @"^\d+\.\d+\.\d+(?:\.\d+)?(?:-[0-9A-Za-z.-]+)?\z"))
            throw new ArgumentException("Invalid application version.", nameof(appVersion));
        if (!Enum.IsDefined(snapshot.CharacterStorage) || snapshot.FrameWidth is < 1 or > 1024 || snapshot.FrameHeight is < 1 or > 1024 ||
            snapshot.DecodedTextures is < 0 or > 256 || snapshot.AvailableCharacters < 0 || snapshot.UnreadNotifications < 0 ||
            snapshot.MaximumFps is not (30 or 60 or 120) || snapshot.UiLanguage is not ("ko" or "en" or "zh-CN"))
            throw new ArgumentException("Invalid diagnostic snapshot.", nameof(snapshot));
        return JsonSerializer.Serialize(new
        {
            schemaVersion = 1, product = "DesktopPet", appVersion,
            runtimeVersion = Environment.Version.ToString(), osVersion = Environment.OSVersion.Version.ToString(),
            osArchitecture = RuntimeInformation.OSArchitecture.ToString(), processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            characterStorage = snapshot.CharacterStorage.ToString(), snapshot.FrameWidth, snapshot.FrameHeight,
            snapshot.DecodedTextures, snapshot.AvailableCharacters, snapshot.UnreadNotifications,
            snapshot.MaximumFps, snapshot.ReducedMotion, snapshot.ManuallyVisible, snapshot.TemporarilyPaused,
            snapshot.QuietNow, snapshot.HasLiveRig, snapshot.UiLanguage
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    public static void SaveFile(string path, string report)
    {
        if (string.IsNullOrWhiteSpace(report) || Encoding.UTF8.GetByteCount(report) > 64 * 1024)
            throw new InvalidDataException("Diagnostic report is empty or too large.");
        var full = Path.GetFullPath(path);
        var temporary = Path.Combine(Path.GetDirectoryName(full)!, ".desktop-pet-diagnostic-" + Guid.NewGuid().ToString("N") + ".tmp");
        var created = false;
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                created = true;
                using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                writer.Write(report);
            }
            File.Move(temporary, full, overwrite: true);
        }
        finally { if (created && File.Exists(temporary)) File.Delete(temporary); }
    }
}
