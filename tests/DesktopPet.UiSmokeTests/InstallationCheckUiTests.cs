using System.IO;
using System.Text.Json;
using DesktopPet.App;

internal static class InstallationCheckUiTests
{
    public static void Run(string root)
    {
        var installation = Path.Combine(root, "private-installation-canary");
        var png = Path.Combine(MainWindow.CharactersRoot, "Companion", "idle.png");
        foreach (var name in new[] { "Companion", "Nimbus", "Ember" })
        {
            var character = Path.Combine(installation, "Assets", "Characters", name);
            Directory.CreateDirectory(character);
            File.Copy(png, Path.Combine(character, "idle.png"));
            File.WriteAllText(Path.Combine(character, "character.json"), "{\"name\":\"" + name + "\",\"anchor\":{\"x\":64,\"y\":122},\"frames\":{\"idle\":[\"idle.png\"]}}");
        }
        Directory.CreateDirectory(Path.Combine(installation, "Hooks"));
        var hook = Path.Combine(installation, "Hooks", "assistant-stop-hook.ps1");
        File.WriteAllText(hook, "fixture hook; never executed");
        var before = Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var good = InstallationCheck.Inspect(installation);
        Check(good.ResourcesValid && good.CharacterCount == 3 && good.ValidatedCharacters == 3 && good.HasOriginalCollection && good.HasHookBridge,
            "Valid installation failed the read-only check");
        Check(before.SetEquals(Directory.GetFiles(root, "*", SearchOption.AllDirectories)) && !good.ToJson().Contains(installation), "Installation check wrote files or exposed local paths");
        File.WriteAllText(Path.Combine(installation, "Assets", "Characters", "Nimbus", "idle.png"), "corrupt PNG");
        using (var report = JsonDocument.Parse(good.ToJson()))
        {
            Check(report.RootElement.GetProperty("ProcessArchitecture").GetString() ==
                    System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant() &&
                report.RootElement.GetProperty("OSArchitecture").GetString() ==
                    System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(),
                "Installation report did not distinguish the executable process from the host architecture");
        }
        var broken = InstallationCheck.Inspect(installation);
        Check(!broken.ResourcesValid && broken.ValidatedCharacters == 2 && broken.Errors.Contains("InvalidCharacterResources") && !broken.ToJson().Contains("private-installation-canary"),
            "Corrupt installation was accepted or exposed its path");
        var privateCard = Path.Combine(installation, "Assets", "Characters", "private-character-canary");
        Directory.CreateDirectory(privateCard);
        File.WriteAllText(Path.Combine(privateCard, "character.json"), "{");
        var missingOriginal = Path.Combine(installation, "Assets", "Characters", "Ember");
        File.Delete(Path.Combine(missingOriginal, "idle.png"));
        File.Delete(Path.Combine(missingOriginal, "character.json"));
        Directory.Delete(missingOriginal);
        var details = InstallationCheck.Inspect(installation);
        Check(!details.ResourcesValid && !details.HasOriginalCollection &&
            details.InvalidOriginalCharacters.SequenceEqual(new[] { "Nimbus" }) &&
            details.MissingOriginalCharacters.SequenceEqual(new[] { "Ember" }) &&
            details.InvalidOptionalCharacterCount == 1 &&
            !details.ToJson().Contains("private-character-canary") && !details.ToJson().Contains(installation),
            "Installation detail did not distinguish package names safely");
        File.Delete(hook);
        var missing = InstallationCheck.Inspect(installation);
        Check(!missing.HasHookBridge && missing.Errors.Contains("MissingOrLinkedHookBridge"), "Missing hook bridge passed installation check");
        Console.WriteLine("PASS Read-only installation check validates all original resources, detects corrupt PNGs/missing bridge and emits sanitized JSON without creating profile data");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
