using System.Text.Json;

namespace DesktopPet.Core;

/// <summary>User-adjustable, persisted preferences — not derived from any character.json.</summary>
public sealed class PetSettings
{
    public string CharacterName { get; set; } = "Chiikawa";
    public double ScaleMultiplier { get; set; } = 1.0;
    public double WalkSpeedMultiplier { get; set; } = 1.0;
    public bool Visible { get; set; } = true;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesktopPet", "settings.json");

    public static PetSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var loaded = JsonSerializer.Deserialize<PetSettings>(json);
                if (loaded is not null)
                {
                    return loaded;
                }
            }
        }
        catch
        {
            // Corrupt or unreadable settings file: start from defaults rather than failing to launch.
        }

        return new PetSettings();
    }

    public void Save()
    {
        var dir = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
    }
}
