using System.Text.Json;

namespace DesktopPet.Core;

/// <summary>User-adjustable, persisted preferences — not derived from any character.json.</summary>
public sealed class PetSettings
{
    public const string DefaultCharacterName = "Companion";
    public string CharacterName { get; set; } = DefaultCharacterName;
    public double ScaleMultiplier { get; set; } = 1.0;
    public double WalkSpeedMultiplier { get; set; } = 1.0;
    public bool Visible { get; set; } = true;
    public bool PauseDuringFullscreen { get; set; } = true;

    /// <summary>Animate other applications' windows falling into the taskbar when they close.</summary>
    public bool WindowFallEnabled { get; set; }

    /// <summary>Let the pet hop between windows instead of turning around at every edge.</summary>
    public bool HopBetweenWindowsEnabled { get; set; }

    /// <summary>Doze off when left alone for a while.</summary>
    public bool SleepEnabled { get; set; } = true;
    public bool CalmBehavior { get; set; } = true;
    public bool ReducedMotion { get; set; }
    public int MaximumFps { get; set; } = 60;
    public bool AutoDismissNotifications { get; set; } = true;
    public bool QuietMode { get; set; }
    public bool QuietHoursEnabled { get; set; }
    public int QuietHoursStartMinute { get; set; } = 22 * 60;
    public int QuietHoursEndMinute { get; set; } = 8 * 60;
    public int NotificationDisplaySeconds { get; set; } = 8;
    public int FocusMinutes { get; set; } = 25;
    public int BreakMinutes { get; set; } = 5;
    public bool AutoConnectAssistants { get; set; } = true;
    public bool AssistantSetupSeen { get; set; }

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesktopPet", "settings.json");

    public static PetSettings Load(string? filePath = null)
    {
        try
        {
            var path = filePath ?? FilePath;
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<PetSettings>(json);
                if (loaded is not null)
                {
                    loaded.Normalize();
                    return loaded;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Corrupt or unreadable settings file: start from defaults rather than failing to launch.
        }

        return new PetSettings();
    }

    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(CharacterName) || CharacterName is "." or ".." ||
            CharacterName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            CharacterName = DefaultCharacterName;
        ScaleMultiplier = double.IsFinite(ScaleMultiplier) ? Math.Clamp(ScaleMultiplier, 0.5, 2.5) : 1.0;
        WalkSpeedMultiplier = double.IsFinite(WalkSpeedMultiplier) ? Math.Clamp(WalkSpeedMultiplier, 0.25, 3.0) : 1.0;
        NotificationDisplaySeconds = Math.Clamp(NotificationDisplaySeconds, 3, 30);
        FocusMinutes = Math.Clamp(FocusMinutes, 1, 180);
        BreakMinutes = Math.Clamp(BreakMinutes, 1, 60);
        QuietHoursStartMinute = Math.Clamp(QuietHoursStartMinute, 0, 1439);
        QuietHoursEndMinute = Math.Clamp(QuietHoursEndMinute, 0, 1439);
        if (MaximumFps is not (30 or 60 or 120)) MaximumFps = 60;
    }

    public void Save(string? filePath = null)
    {
        Normalize();
        var path = Path.GetFullPath(filePath ?? FilePath);
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        var temporaryPath = Path.Combine(dir, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
