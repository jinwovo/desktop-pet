using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DesktopPet.Core;

/// <summary>Portable preferences only. Assistant registration/trust and local paths never transfer.</summary>
public static class PetSettingsBackup
{
    public const int MaxBytes = 64 * 1024;
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        nameof(PetSettings.CharacterName), nameof(PetSettings.Language), nameof(PetSettings.ScaleMultiplier), nameof(PetSettings.WalkSpeedMultiplier),
        nameof(PetSettings.Visible), nameof(PetSettings.PauseDuringFullscreen), nameof(PetSettings.WindowFallEnabled),
        nameof(PetSettings.HopBetweenWindowsEnabled), nameof(PetSettings.SleepEnabled), nameof(PetSettings.CalmBehavior),
        nameof(PetSettings.ReducedMotion), nameof(PetSettings.MaximumFps), nameof(PetSettings.AutoDismissNotifications),
        nameof(PetSettings.QuietMode), nameof(PetSettings.QuietHoursEnabled), nameof(PetSettings.QuietHoursStartMinute),
        nameof(PetSettings.QuietHoursEndMinute), nameof(PetSettings.NotificationDisplaySeconds),
        nameof(PetSettings.FocusMinutes), nameof(PetSettings.BreakMinutes)
    };

    public static string WriteJson(PetSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var preferences = new JsonObject();
        foreach (var name in Allowed)
            preferences[name] = JsonSerializer.SerializeToNode(typeof(PetSettings).GetProperty(name)!.GetValue(settings));
        var root = new JsonObject { ["schemaVersion"] = 1, ["product"] = "DesktopPet", ["preferences"] = preferences };
        var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        EnsureSize(json);
        return json;
    }

    public static PetSettings ReadJson(string json, PetSettings baseline)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        EnsureSize(json);
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            CheckObject(root, new HashSet<string>(["schemaVersion", "product", "preferences"], StringComparer.Ordinal));
            if (root.GetProperty("schemaVersion").GetInt32() != 1 || root.GetProperty("product").GetString() != "DesktopPet")
                throw new InvalidDataException("지원하지 않는 설정 백업 형식입니다.");
            var preferences = root.GetProperty("preferences");
            CheckObject(preferences, Allowed);
            var merged = JsonSerializer.SerializeToNode(baseline)!.AsObject();
            foreach (var property in preferences.EnumerateObject()) merged[property.Name] = JsonNode.Parse(property.Value.GetRawText());
            var result = merged.Deserialize<PetSettings>() ?? throw new InvalidDataException("설정 백업이 비어 있습니다.");
            result.Normalize();
            return result;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        {
            throw new InvalidDataException("설정 백업을 읽을 수 없습니다.", ex);
        }
    }

    public static string ReadFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaxBytes) throw new InvalidDataException("설정 백업은 64 KiB 이하여야 합니다.");
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
        var json = reader.ReadToEnd();
        EnsureSize(json);
        return json;
    }

    public static void SaveFile(string path, PetSettings settings)
        => WriteFile(Path.GetFullPath(path), WriteJson(settings), CancellationToken.None);

    public static Task SaveFileAsync(string path, PetSettings settings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var full = Path.GetFullPath(path);
        var json = WriteJson(settings);
        return Task.Run(() => WriteFile(full, json, cancellationToken), cancellationToken);
    }

    private static void WriteFile(string full, string json, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var temporary = Path.Combine(Path.GetDirectoryName(full)!, ".desktop-pet-preferences-" + Guid.NewGuid().ToString("N") + ".tmp");
        var created = false;
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                created = true;
                using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                writer.Write(json);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, full, overwrite: true);
        }
        finally { if (created && File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void EnsureSize(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || Encoding.UTF8.GetByteCount(json) > MaxBytes)
            throw new InvalidDataException("설정 백업이 비어 있거나 64 KiB를 초과합니다.");
    }
    private static void CheckObject(JsonElement element, HashSet<string> allowed)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new InvalidDataException("설정 백업 객체가 올바르지 않습니다.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!allowed.Contains(property.Name) || !seen.Add(property.Name))
                throw new InvalidDataException("설정 백업에 지원하지 않거나 중복된 항목이 있습니다.");
    }
}
