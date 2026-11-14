using System.Text;
using System.Text.Json;
using DesktopPet.Core;

public static class AssistantIntegrationHistoryTests
{
    public static IEnumerable<(string Name, Action Run)> Create()
    {
        yield return ("Assistant history reads and unsuccessful registrations leave the filesystem untouched", () => InFixture(fixture =>
        {
            var history = fixture.History();
            Check(history.GetProfiles().Count == 0 && !File.Exists(fixture.HistoryPath), "Read created history");
            history.Record(fixture.Status(AssistantProvider.Claude) with { Registered = false });
            history.Record(fixture.Status(AssistantProvider.Codex) with { Succeeded = false });
            history.Remove(AssistantProvider.Claude, fixture.ClaudePath);
            Check(!Directory.Exists(Path.GetDirectoryName(fixture.HistoryPath)), "No-op created history directory");
        }));

        yield return ("Assistant history persists canonical profiles and deduplicates case aliases", () => InFixture(fixture =>
        {
            var history = fixture.History();
            history.Record(fixture.Status(AssistantProvider.Codex));
            history.Record(fixture.Status(AssistantProvider.Claude));
            var bytes = File.ReadAllBytes(fixture.HistoryPath);
            history.Record(fixture.Status(AssistantProvider.Claude) with { ConfigPath = fixture.ClaudePath.ToUpperInvariant() });
            Check(bytes.SequenceEqual(File.ReadAllBytes(fixture.HistoryPath)), "Duplicate registration rewrote history");
            var profiles = fixture.History().GetProfiles();
            Check(profiles.Count == 2 && profiles[0] == new AssistantIntegrationProfile(AssistantProvider.Claude, fixture.ClaudePath)
                && profiles[1] == new AssistantIntegrationProfile(AssistantProvider.Codex, fixture.CodexPath), "Profiles did not survive restart");
            history.Remove(AssistantProvider.Claude, fixture.ClaudePath.ToUpperInvariant());
            Check(fixture.History().GetProfiles().Single().Provider == AssistantProvider.Codex, "Removal changed another provider");
            bytes = File.ReadAllBytes(fixture.HistoryPath);
            history.Remove(AssistantProvider.Claude, fixture.ClaudePath);
            Check(bytes.SequenceEqual(File.ReadAllBytes(fixture.HistoryPath)), "Repeated removal rewrote history");
        }));

        yield return ("Recorded assistant overrides remain removable after environment configuration changes", () => InFixture(fixture =>
        {
            var script = Path.Combine(fixture.Root, "assistant-stop-hook.ps1");
            File.WriteAllText(script, "param($Source,$RegistrationId)");
            var home = Path.Combine(fixture.Root, "new-default-home");
            var overrides = new AssistantIntegrationService(home, script, Path.GetDirectoryName(fixture.ClaudePath), Path.GetDirectoryName(fixture.CodexPath));
            foreach (var provider in Enum.GetValues<AssistantProvider>())
            {
                var path = provider == AssistantProvider.Claude ? fixture.ClaudePath : fixture.CodexPath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, "{\"preserve\":true,\"hooks\":{\"Stop\":[{\"hooks\":[{\"type\":\"command\",\"command\":\"echo other\"}]}]}}");
                var status = overrides.Connect(provider);
                Check(status.Succeeded && status.Registered, "Fixture connection failed");
                fixture.History().Record(status);
            }
            var defaults = new AssistantIntegrationService(home, script);
            foreach (var provider in Enum.GetValues<AssistantProvider>())
                Check(defaults.Disconnect(provider).Succeeded, "Absent current defaults failed to disconnect");
            foreach (var profile in fixture.History().GetProfiles())
            {
                var recorded = new AssistantIntegrationService(home, script,
                    profile.Provider == AssistantProvider.Claude ? Path.GetDirectoryName(profile.ConfigPath) : null,
                    profile.Provider == AssistantProvider.Codex ? Path.GetDirectoryName(profile.ConfigPath) : null);
                var status = recorded.Disconnect(profile.Provider);
                Check(status.Succeeded && !status.Registered, "Could not disconnect recorded override");
                using var document = JsonDocument.Parse(File.ReadAllText(profile.ConfigPath));
                Check(document.RootElement.GetProperty("preserve").GetBoolean()
                    && document.RootElement.GetProperty("hooks").GetProperty("Stop").GetArrayLength() == 1, "Disconnect changed unrelated hooks");
                fixture.History().Remove(profile.Provider, profile.ConfigPath);
            }
            Check(fixture.History().GetProfiles().Count == 0 && !Directory.Exists(home), "Override disconnect created defaults or left history");
        }));

        yield return ("Malformed assistant history fails closed and preserves original bytes", () => InFixture(fixture =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fixture.HistoryPath)!);
            foreach (var json in new[]
            {
                "{", "null", "[]", "{}", "{\"schemaVersion\":1,\"schemaVersion\":1}",
                "{\"schemaVersion\":2,\"product\":\"DesktopPet\",\"profiles\":[]}",
                "{\"schemaVersion\":1,\"product\":7,\"profiles\":[]}",
                "{\"schemaVersion\":1,\"product\":\"Other\",\"profiles\":[]}",
                "{\"schemaVersion\":1,\"product\":\"DesktopPet\",\"profiles\":[null]}",
                "{\"schemaVersion\":1,\"product\":\"DesktopPet\",\"profiles\":[{\"provider\":\"other\",\"configPath\":\"relative\"}]}",
                "{\"schemaVersion\":1,\"product\":\"DesktopPet\",\"profiles\":[{\"provider\":\"codex\",\"configPath\":\"relative\"}]}"
            })
            {
                File.WriteAllText(fixture.HistoryPath, json, new UTF8Encoding(true));
                var bytes = File.ReadAllBytes(fixture.HistoryPath);
                Throws<InvalidDataException>(() => fixture.History().GetProfiles());
                Throws<InvalidDataException>(() => fixture.History().Record(fixture.Status(AssistantProvider.Claude)));
                Throws<InvalidDataException>(() => fixture.History().Remove(AssistantProvider.Claude, fixture.ClaudePath));
                Check(bytes.SequenceEqual(File.ReadAllBytes(fixture.HistoryPath)), "Malformed history was changed");
            }
            Check(Directory.GetFiles(Path.GetDirectoryName(fixture.HistoryPath)!, "*.tmp").Length == 0, "Malformed input leaked temporary file");
        }));

        yield return ("Assistant history rejects mismatched filenames and ambiguous Windows paths", () => InFixture(fixture =>
        {
            foreach (var path in new[] { "relative\\settings.json", Path.Combine(fixture.Root, "wrong.json"),
                Path.Combine(fixture.Root, "profile. ", "settings.json"), fixture.ClaudePath + ":stream" })
                Throws<ArgumentException>(() => fixture.History().Record(fixture.Status(AssistantProvider.Claude) with { ConfigPath = path }));
            Throws<ArgumentOutOfRangeException>(() => fixture.History().Remove((AssistantProvider)99, fixture.ClaudePath));
            Check(!File.Exists(fixture.HistoryPath), "Invalid profile created history");
        }));

        yield return ("Assistant history stores an exact previous-byte backup on replacement", () => InFixture(fixture =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fixture.HistoryPath)!);
            File.WriteAllText(fixture.HistoryPath, "{\"schemaVersion\":1,\"product\":\"DesktopPet\",\"profiles\":[]}", new UTF8Encoding(true));
            var original = File.ReadAllBytes(fixture.HistoryPath);
            fixture.History().Record(fixture.Status(AssistantProvider.Claude));
            var backup = Directory.GetFiles(Path.GetDirectoryName(fixture.HistoryPath)!, "*.bak").Single();
            Check(original.SequenceEqual(File.ReadAllBytes(backup)), "Backup changed encoding or original bytes");
        }));

        yield return ("Assistant history preserves concurrent edits and concurrent first-file creation", () => InFixture(fixture =>
        {
            const string external = "{\"schemaVersion\":1,\"product\":\"DesktopPet\",\"profiles\":[],\"external\":true}";
            foreach (var preexisting in new[] { false, true })
            {
                if (preexisting) File.WriteAllText(fixture.HistoryPath, "{\"schemaVersion\":1,\"product\":\"DesktopPet\",\"profiles\":[]}");
                var history = new AssistantIntegrationHistory(fixture.HistoryPath, path => File.WriteAllText(path, external));
                Throws<IOException>(() => history.Record(fixture.Status(AssistantProvider.Claude)));
                Check(File.ReadAllText(fixture.HistoryPath) == external, "Concurrent writer was overwritten");
                Check(Directory.GetFiles(Path.GetDirectoryName(fixture.HistoryPath)!, "*.tmp").Length == 0, "Conflict leaked temporary file");
            }
        }));

        yield return ("Concurrent assistant history writers fail cleanly without losing the first update", () => InFixture(fixture =>
        {
            var rejected = false;
            var firstWriter = new AssistantIntegrationHistory(fixture.HistoryPath, _ =>
            {
                rejected = Task.Run(() =>
                {
                    try { fixture.History().Record(fixture.Status(AssistantProvider.Codex)); }
                    catch (IOException) { return true; }
                    return false;
                }).GetAwaiter().GetResult();
            });
            firstWriter.Record(fixture.Status(AssistantProvider.Claude));
            Check(rejected && fixture.History().GetProfiles().Single().Provider == AssistantProvider.Claude,
                "Parallel writer bypassed the mutex or lost the successful update");
        }));

        yield return ("Linked assistant history file is rejected without changing the target", () => InFixture(fixture =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fixture.HistoryPath)!);
            var target = Path.Combine(fixture.Root, "history-target.json");
            const string original = "{\"schemaVersion\":1,\"product\":\"DesktopPet\",\"profiles\":[]}";
            File.WriteAllText(target, original);
            CreateLinkOrSkip(fixture.HistoryPath, target, directory: false);
            try
            {
                Throws<InvalidDataException>(() => fixture.History().GetProfiles());
                Throws<InvalidDataException>(() => fixture.History().Record(fixture.Status(AssistantProvider.Claude)));
                Check(File.ReadAllText(target) == original, "Changed linked target");
            }
            finally { File.Delete(fixture.HistoryPath); }
        }));

        yield return ("Linked assistant history parent is rejected before creating files", () => InFixture(fixture =>
        {
            var target = Path.Combine(fixture.Root, "linked-parent-target");
            Directory.CreateDirectory(target);
            var link = Path.GetDirectoryName(fixture.HistoryPath)!;
            CreateLinkOrSkip(link, target, directory: true);
            try
            {
                Throws<InvalidDataException>(() => fixture.History().Record(fixture.Status(AssistantProvider.Claude)));
                Check(Directory.GetFiles(target).Length == 0, "Wrote through linked parent");
            }
            finally { Directory.Delete(link); }
        }));

        yield return ("Assistant history rechecks link substitution immediately before commit", () => InFixture(fixture =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fixture.HistoryPath)!);
            var target = Path.Combine(fixture.Root, "substituted-target.json");
            const string original = "{\"schemaVersion\":1,\"product\":\"DesktopPet\",\"profiles\":[]}";
            File.WriteAllText(target, original);
            var probe = Path.Combine(fixture.Root, "link-probe");
            CreateLinkOrSkip(probe, target, directory: false);
            File.Delete(probe);
            File.WriteAllText(fixture.HistoryPath, original);
            var history = new AssistantIntegrationHistory(fixture.HistoryPath, path =>
            {
                File.Delete(path);
                File.CreateSymbolicLink(path, target);
            });
            try
            {
                Throws<InvalidDataException>(() => history.Record(fixture.Status(AssistantProvider.Claude)));
                Check(File.ReadAllText(target) == original, "Precommit link target was changed");
                Check(Directory.GetFiles(Path.GetDirectoryName(fixture.HistoryPath)!, "*.tmp").Length == 0, "Link conflict leaked temporary file");
            }
            finally { File.Delete(fixture.HistoryPath); }
        }));
    }

    private static void CreateLinkOrSkip(string path, string target, bool directory)
    {
        try
        {
            if (directory) Directory.CreateSymbolicLink(path, target);
            else File.CreateSymbolicLink(path, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            throw new RegressionTestSkippedException("This Windows environment cannot create a symbolic-link fixture: " + ex.Message);
        }
    }

    private static void InFixture(Action<Fixture> run)
    {
        var root = Path.Combine(Path.GetTempPath(), "DesktopPet-AssistantHistory-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { run(new Fixture(root)); }
        finally
        {
            var target = Path.GetFullPath(root);
            var temporaryRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            Check(target.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(target).StartsWith("DesktopPet-AssistantHistory-", StringComparison.Ordinal), "Unsafe cleanup target");
            Directory.Delete(target, recursive: true);
        }
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }

    private sealed record Fixture(string Root)
    {
        public string HistoryPath => Path.Combine(Root, "app-data", "assistant-connections.json");
        public string ClaudePath => Path.Combine(Root, "custom-claude-home", "settings.json");
        public string CodexPath => Path.Combine(Root, "custom-codex-home", "hooks.json");
        public AssistantIntegrationHistory History() => new(HistoryPath);
        public AssistantIntegrationStatus Status(AssistantProvider provider) => new(provider, true, true, "fixture",
            provider == AssistantProvider.Claude ? ClaudePath : CodexPath);
    }
}
