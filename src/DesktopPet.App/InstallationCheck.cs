using System.IO;
using System.Text.Json;
using DesktopPet.Core;

namespace DesktopPet.App;

public sealed record InstallationCheckResult(string AppVersion, bool ResourcesValid, int CharacterCount, int ValidatedCharacters,
    bool HasOriginalCollection, bool HasHookBridge, IReadOnlyList<string> Errors)
{
    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
}

/// <summary>Read-only CLI check. No settings, user libraries, assistant profiles, inbox, mutex or windows are opened.</summary>
public static class InstallationCheck
{
    public static InstallationCheckResult Inspect(string baseDirectory)
    {
        var version = typeof(App).Assembly.GetName().Version!.ToString();
        var errors = new HashSet<string>(StringComparer.Ordinal);
        var characters = Array.Empty<string>();
        var validated = 0;
        var originals = false;
        var bridge = false;
        try
        {
            var root = Path.GetFullPath(baseDirectory);
            var hook = Path.Combine(root, "Hooks", "assistant-stop-hook.ps1");
            bridge = File.Exists(hook) && IsUnlinked(hook);
            if (!bridge) errors.Add("MissingOrLinkedHookBridge");
            var library = Path.Combine(root, "Assets", "Characters");
            if (!Directory.Exists(library) || !IsUnlinked(library)) errors.Add("MissingOrLinkedCharacterLibrary");
            else
            {
                characters = Directory.EnumerateDirectories(library).Take(65).ToArray();
                if (characters.Length > 64) errors.Add("TooManyCharacters");
                else
                {
                    var names = characters.Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    originals = new[] { "Companion", "Nimbus", "Ember" }.All(names.Contains);
                    if (!originals) errors.Add("MissingOriginalCollection");
                    foreach (var character in characters)
                    {
                        try { _ = CharacterPresentation.Load(character, includeRasterGait: true); validated++; }
                        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException)
                        { errors.Add("InvalidCharacterResources"); }
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            errors.Add("UnreadableInstallation"); // Do not include the local path or exception text.
        }
        return new(version, errors.Count == 0 && originals && bridge && characters.Length > 0, characters.Length, validated,
            originals, bridge, errors.OrderBy(value => value, StringComparer.Ordinal).ToArray());
    }
    private static bool IsUnlinked(string path)
    {
        for (var current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
        return true;
    }
}
