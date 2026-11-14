using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DesktopPet.Core;

public static class AssistantIntegrationTests
{
    public static IEnumerable<(string Name, Action Run)> Create()
    {
        yield return ("Assistant detection is read-only and automatic connection ignores absent providers", () => InFixture(fixture =>
        {
            var service = fixture.Service();
            Check(!service.GetStatus(AssistantProvider.Claude).Detected && !service.GetStatus(AssistantProvider.Codex).Detected, "Detected absent assistants");
            var statuses = service.AutoConnectDetected();
            Check(statuses.Count == 2 && statuses.All(status => !status.Registered && status.Succeeded), "Unexpected automatic registration");
            Check(!Directory.Exists(fixture.ClaudeDirectory) && !Directory.Exists(fixture.CodexDirectory), "Read-only detection created assistant directories");
        }));

        yield return ("Assistant connection preserves unrelated settings and hooks with an exact backup", () => InFixture(fixture =>
        {
            foreach (var provider in Enum.GetValues<AssistantProvider>())
            {
                var path = fixture.ConfigPath(provider);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                const string original = "{\"model\":\"keep-model\",\"permissions\":{\"allow\":[\"keep\"]},\"hooks\":{\"SessionStart\":[{\"hooks\":[{\"type\":\"command\",\"command\":\"echo hello\"}]}],\"Stop\":[{\"matcher\":\"\",\"vendor\":7,\"hooks\":[{\"type\":\"command\",\"command\":\"echo keep\"}]}]}}";
                File.WriteAllText(path, original, new UTF8Encoding(true));
                var originalBytes = File.ReadAllBytes(path);
                var status = fixture.Service().Connect(provider);
                Check(status.Succeeded && status.Registered && status.Detected, "Connection failed: " + status.Detail);
                var updated = Read(path);
                Check(updated["model"]!.GetValue<string>() == "keep-model" && updated["permissions"]!["allow"]![0]!.GetValue<string>() == "keep", "Changed unrelated preferences");
                Check(updated["hooks"]!["SessionStart"]![0]!["hooks"]![0]!["command"]!.GetValue<string>() == "echo hello", "Changed unrelated event hooks");
                Check(updated["hooks"]!["Stop"]![0]!["vendor"]!.GetValue<int>() == 7 && updated["hooks"]!["Stop"]![0]!["hooks"]![0]!["command"]!.GetValue<string>() == "echo keep", "Changed another Stop handler");
                var backup = Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".desktop-pet-*.bak").Single();
                Check(File.ReadAllBytes(backup).SequenceEqual(originalBytes), "Backup does not preserve original bytes and BOM");
                Check(status.RequiresTrustReview == (provider == AssistantProvider.Codex), "Codex trust requirement was lost");
            }
        }));

        yield return ("Assistant connection and disconnection are idempotent", () => InFixture(fixture =>
        {
            foreach (var provider in Enum.GetValues<AssistantProvider>())
            {
                var service = fixture.Service();
                Check(service.Connect(provider).Registered, "Initial connection failed");
                var path = fixture.ConfigPath(provider);
                var bytes = File.ReadAllBytes(path);
                var countBefore = Directory.GetFiles(Path.GetDirectoryName(path)!).Length;
                Check(service.Connect(provider).Registered, "Repeated connection failed");
                Check(File.ReadAllBytes(path).SequenceEqual(bytes) && Directory.GetFiles(Path.GetDirectoryName(path)!).Length == countBefore, "Idempotent connect rewrote configuration");
                var disconnected = service.Disconnect(provider);
                Check(disconnected.Succeeded && !disconnected.Registered, "Disconnect failed");
                bytes = File.ReadAllBytes(path);
                countBefore = Directory.GetFiles(Path.GetDirectoryName(path)!).Length;
                Check(service.Disconnect(provider).Succeeded, "Repeated disconnect failed");
                Check(File.ReadAllBytes(path).SequenceEqual(bytes) && Directory.GetFiles(Path.GetDirectoryName(path)!).Length == countBefore, "Idempotent disconnect rewrote configuration");
            }
        }));

        yield return ("Assistant removal deletes only the reserved handler even inside a shared matcher group", () => InFixture(fixture =>
        {
            var service = fixture.Service();
            Check(service.Connect(AssistantProvider.Claude).Registered, "Connection failed");
            var path = fixture.ConfigPath(AssistantProvider.Claude);
            var root = Read(path);
            var own = Handlers(root).Single().DeepClone();
            var foreign = JsonNode.Parse("{\"type\":\"command\",\"command\":\"echo DesktopPet.StopHook.v1\",\"timeout\":19}")!;
            root["hooks"]!["Stop"] = new JsonArray(new JsonObject
            {
                ["matcher"] = "keep-matcher", ["vendor"] = "keep-group", ["hooks"] = new JsonArray(foreign.DeepClone(), own),
            });
            File.WriteAllText(path, root.ToJsonString());
            Check(service.Disconnect(AssistantProvider.Claude).Succeeded, "Removal failed");
            var result = Read(path);
            Check(Handlers(result).Count() == 1 && JsonNode.DeepEquals(Handlers(result).Single(), foreign), "Deleted or changed the unrelated handler");
            Check(result["hooks"]!["Stop"]![0]!["vendor"]!.GetValue<string>() == "keep-group", "Removed foreign matcher metadata");
        }));

        yield return ("Duplicate DesktopPet registrations are collapsed without duplicating other hooks", () => InFixture(fixture =>
        {
            var service = fixture.Service();
            Check(service.Connect(AssistantProvider.Codex).Registered, "Connection failed");
            var path = fixture.ConfigPath(AssistantProvider.Codex);
            var root = Read(path);
            var duplicate = Handlers(root).Single().DeepClone();
            ((JsonArray)root["hooks"]!["Stop"]!).Add(new JsonObject { ["hooks"] = new JsonArray(duplicate) });
            File.WriteAllText(path, root.ToJsonString());
            Check(service.Connect(AssistantProvider.Codex).Registered && Handlers(Read(path)).Count() == 1, "Duplicate handlers remain");
        }));

        yield return ("An unrelated encoded command that prints our marker is not removed", () => InFixture(fixture =>
        {
            var service = fixture.Service();
            Check(service.Connect(AssistantProvider.Codex).Registered, "Connection failed");
            var path = fixture.ConfigPath(AssistantProvider.Codex);
            var root = Read(path);
            var ownCommand = Handlers(root).Single()["command"]!.GetValue<string>();
            var prefix = ownCommand[..(ownCommand.LastIndexOf(' ') + 1)];
            var foreign = new JsonObject
            {
                ["type"] = "command", ["statusMessage"] = AssistantIntegrationService.RegistrationId,
                ["command"] = prefix + Convert.ToBase64String(Encoding.Unicode.GetBytes("Write-Output \"-RegistrationId 'DesktopPet.StopHook.v1'\"")),
            };
            ((JsonArray)root["hooks"]!["Stop"]!).Add(new JsonObject { ["hooks"] = new JsonArray(foreign.DeepClone()) });
            File.WriteAllText(path, root.ToJsonString());
            Check(service.Disconnect(AssistantProvider.Codex).Succeeded, "Removal failed");
            Check(Handlers(Read(path)).Count() == 1 && JsonNode.DeepEquals(Handlers(Read(path)).Single(), foreign), "Removed an unrelated command containing the marker as data");
        }));

        yield return ("Malformed assistant JSON and invalid Stop structures fail closed", () => InFixture(fixture =>
        {
            var path = fixture.ConfigPath(AssistantProvider.Claude);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            foreach (var json in new[] { "{", "null", "[]", "{\"hooks\":null}", "{\"hooks\":{\"Stop\":{}}}", "{\"hooks\":{\"Stop\":[{\"hooks\":[null]}]}}", "{\"a\":1,\"a\":2}" })
            {
                File.WriteAllText(path, json);
                var service = fixture.Service();
                Check(!service.GetStatus(AssistantProvider.Claude).Succeeded, "Malformed status reported success");
                Check(!service.Connect(AssistantProvider.Claude).Succeeded && !service.Disconnect(AssistantProvider.Claude).Succeeded, "Malformed mutation reported success");
                Check(File.ReadAllText(path) == json, "Malformed configuration was overwritten");
            }
        }));

        yield return ("External edits between read and commit are preserved and reported as conflicts", () => InFixture(fixture =>
        {
            var path = fixture.ConfigPath(AssistantProvider.Claude);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{\"before\":1}");
            const string edited = "{\"changedByAnotherEditor\":true}";
            var service = fixture.Service(configPath => File.WriteAllText(configPath, edited));
            var status = service.Connect(AssistantProvider.Claude);
            Check(!status.Succeeded && !status.Registered, "Concurrent edit was not detected");
            Check(File.ReadAllText(path) == edited, "Overwrote external editor's changes");
            Check(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp").Length == 0, "Failed commit leaked a temporary file");
        }));

        yield return ("A linked assistant settings file is reported without changing its target", () => InFixture(fixture =>
        {
            var path = fixture.ConfigPath(AssistantProvider.Claude);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var target = Path.Combine(fixture.Root, "linked-settings-target.json");
            const string original = "{\"preserveLinkedTarget\":true}";
            File.WriteAllText(target, original);
            try { File.CreateSymbolicLink(path, target); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                throw new RegressionTestSkippedException("This Windows environment cannot create a symbolic-link fixture: " + ex.Message);
            }
            var service = fixture.Service();
            Check(!service.GetStatus(AssistantProvider.Claude).Succeeded, "Linked settings status did not report an error");
            Check(!service.Connect(AssistantProvider.Claude).Succeeded && !service.Disconnect(AssistantProvider.Claude).Succeeded, "Linked settings mutation reported success");
            Check(File.ReadAllText(target) == original && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0, "Changed the linked target or replaced the link");
        }));

        yield return ("Locked assistant settings remain intact when atomic replacement fails", () => InFixture(fixture =>
        {
            var path = fixture.ConfigPath(AssistantProvider.Claude);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            const string original = "{\"existing\":\"keep\"}";
            File.WriteAllText(path, original);
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                Check(!fixture.Service().Connect(AssistantProvider.Claude).Succeeded, "Locked replacement reported success");
            Check(File.ReadAllText(path) == original, "Failed replacement damaged original");
        }));

        yield return ("Assistant directory overrides and inline Codex settings are preserved", () => InFixture(fixture =>
        {
            var alternateClaude = Path.Combine(fixture.Root, "claude-config-override");
            var alternateCodex = Path.Combine(fixture.Root, "codex-home-override");
            Directory.CreateDirectory(alternateCodex);
            const string inline = "model = 'existing'\n[[hooks.Stop]]\n[[hooks.Stop.hooks]]\ntype = 'command'\ncommand = 'echo existing'\n";
            File.WriteAllText(Path.Combine(alternateCodex, "config.toml"), inline);
            var service = new AssistantIntegrationService(fixture.Home, fixture.ScriptPath, alternateClaude, alternateCodex);
            Check(service.Connect(AssistantProvider.Claude).ConfigPath == Path.Combine(alternateClaude, "settings.json"), "Claude override ignored");
            Check(service.Connect(AssistantProvider.Codex).Registered, "Codex override ignored");
            Check(File.ReadAllText(Path.Combine(alternateCodex, "config.toml")) == inline, "Rewrote Codex inline configuration");
            Check(!Directory.Exists(fixture.ClaudeDirectory) && !Directory.Exists(fixture.CodexDirectory), "Overrides wrote to default home directories");
        }));

        yield return ("Registration cannot point to a missing deployment script", () => InFixture(fixture =>
        {
            var service = new AssistantIntegrationService(fixture.Home, Path.Combine(fixture.Root, "missing.ps1"));
            Check(!service.Connect(AssistantProvider.Claude).Succeeded, "Missing script registered");
            Check(!Directory.Exists(fixture.ClaudeDirectory), "Failed connect created a settings folder");
            Check(service.Disconnect(AssistantProvider.Claude).Succeeded, "Disconnect unnecessarily requires deployment script");
        }));

        yield return ("Hook command safely preserves spaces, Korean, apostrophes and shell metacharacters", () => InFixture(fixture =>
        {
            foreach (var provider in Enum.GetValues<AssistantProvider>())
            {
                Check(fixture.Service().Connect(provider).Registered, "Connection failed");
                var handler = Handlers(Read(fixture.ConfigPath(provider))).Single();
                var command = handler["command"]!.GetValue<string>();
                Check(!command.Contains(fixture.ScriptPath, StringComparison.Ordinal), "User path leaked into outer shell syntax");
                var start = new ProcessStartInfo("powershell.exe")
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                };
                foreach (var argument in command.Split(' ').Skip(1)) start.ArgumentList.Add(argument);
                using var process = Process.Start(start) ?? throw new Exception("Could not start fixture hook");
                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(5000))
                {
                    process.Kill(entireProcessTree: true); // Only the process created for this fixture.
                    throw new TimeoutException("Fixture hook did not finish");
                }
                var stdout = stdoutTask.GetAwaiter().GetResult();
                var stderr = stderrTask.GetAwaiter().GetResult();
                Check(process.ExitCode == 0, "Fixture hook failed: " + stderr);
                using var document = JsonDocument.Parse(stdout);
                Check(document.RootElement.GetProperty("script").GetString() == fixture.ScriptPath, "Script path changed during shell parsing");
                Check(document.RootElement.GetProperty("source").GetString() == provider.ToString().ToLowerInvariant(), "Source argument changed");
                Check(document.RootElement.GetProperty("registrationId").GetString() == AssistantIntegrationService.RegistrationId, "Registration marker changed");
                if (provider == AssistantProvider.Codex)
                    Check(handler["commandWindows"]!.GetValue<string>() == command && handler["statusMessage"]!.GetValue<string>() == AssistantIntegrationService.RegistrationId, "Codex Windows command or marker missing");
            }
            try
            {
                _ = new AssistantIntegrationService(fixture.Home, fixture.ScriptPath + "\"invalid");
                throw new Exception("Invalid Windows quote accepted");
            }
            catch (ArgumentException) { }
        }));
    }

    private static IEnumerable<JsonObject> Handlers(JsonObject root) =>
        ((JsonArray)root["hooks"]!["Stop"]!).OfType<JsonObject>().SelectMany(group => (JsonArray)group["hooks"]!).OfType<JsonObject>();
    private static JsonObject Read(string path) => (JsonObject)JsonNode.Parse(File.ReadAllText(path))!;
    private static void Check(bool valid, string message) { if (!valid) throw new Exception(message); }

    private static void InFixture(Action<Fixture> run)
    {
        var root = Path.Combine(Path.GetTempPath(), "DesktopPet-AssistantIntegration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { run(new Fixture(root)); }
        finally
        {
            var target = Path.GetFullPath(root);
            var temporaryRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            Check(target.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(target).StartsWith("DesktopPet-AssistantIntegration-", StringComparison.Ordinal), "Unsafe fixture cleanup target");
            Directory.Delete(target, recursive: true);
        }
    }

    private sealed class Fixture
    {
        public Fixture(string root)
        {
            Root = root;
            Home = Path.Combine(root, "fixture-home");
            Directory.CreateDirectory(Home);
            var scriptDirectory = Path.Combine(root, "앱 폴더 O'Brien $money `tick %percent% & more");
            Directory.CreateDirectory(scriptDirectory);
            ScriptPath = Path.Combine(scriptDirectory, "assistant-stop-hook.ps1");
            File.WriteAllText(ScriptPath, "param([string]$Source,[string]$RegistrationId)\n[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding $false\n[pscustomobject]@{ source=$Source; registrationId=$RegistrationId; script=$PSCommandPath } | ConvertTo-Json -Compress\n", new UTF8Encoding(true));
        }
        public string Root { get; }
        public string Home { get; }
        public string ScriptPath { get; }
        public string ClaudeDirectory => Path.Combine(Home, ".claude");
        public string CodexDirectory => Path.Combine(Home, ".codex");
        public string ConfigPath(AssistantProvider provider) => Path.Combine(provider == AssistantProvider.Claude ? ClaudeDirectory : CodexDirectory,
            provider == AssistantProvider.Claude ? "settings.json" : "hooks.json");
        public AssistantIntegrationService Service(Action<string>? beforeCommit = null) =>
            new(Home, ScriptPath, null, null, beforeCommit);
    }
}
