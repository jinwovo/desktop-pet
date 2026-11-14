using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

[assembly: InternalsVisibleTo("DesktopPet.RegressionTests")]

namespace DesktopPet.Core;

public enum AssistantProvider { Claude, Codex }

public sealed record AssistantIntegrationStatus(
    AssistantProvider Provider, bool Detected, bool Registered, string Detail, string ConfigPath,
    bool RequiresTrustReview = false, bool Succeeded = true, bool HasOwnedRegistration = false);

/// <summary>
/// Registers only DesktopPet's user-level Stop handler. No project settings, assistant trust
/// database, managed policy or assistant configuration outside the selected home is changed.
/// </summary>
public sealed class AssistantIntegrationService
{
    public const string RegistrationId = "DesktopPet.StopHook.v1";
    private const string CommandPrefix = "powershell.exe -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -EncodedCommand ";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _hookScriptPath;
    private readonly string _claudeDirectory;
    private readonly string _codexDirectory;
    private readonly Action<string>? _beforeCommit;

    public AssistantIntegrationService(string userHome, string hookScriptPath, string? claudeDir = null, string? codexDir = null)
        : this(userHome, hookScriptPath, claudeDir, codexDir, null) { }

    // Deterministically exercise an external editor changing the file after it was read.
    internal AssistantIntegrationService(string userHome, string hookScriptPath, string? claudeDir,
        string? codexDir, Action<string>? beforeCommit)
    {
        var home = NormalizePath(userHome, nameof(userHome));
        _hookScriptPath = NormalizePath(hookScriptPath, nameof(hookScriptPath));
        _claudeDirectory = NormalizePath(claudeDir ?? Path.Combine(home, ".claude"), nameof(claudeDir));
        _codexDirectory = NormalizePath(codexDir ?? Path.Combine(home, ".codex"), nameof(codexDir));
        _beforeCommit = beforeCommit;
    }

    public AssistantIntegrationStatus GetStatus(AssistantProvider provider)
    {
        var path = GetConfigPath(provider);
        var detected = Directory.Exists(Path.GetDirectoryName(path));
        try
        {
            var root = ReadConfiguration(path, out _);
            var handlers = GetStopGroups(root, create: false)?
                .OfType<JsonObject>().SelectMany(group => (JsonArray)group["hooks"]!).OfType<JsonObject>()
                .Where(IsOwnedHandler).ToArray() ?? Array.Empty<JsonObject>();
            var registered = handlers.Length == 1 && JsonNode.DeepEquals(handlers[0], CreateHandler(provider)) && File.Exists(_hookScriptPath);
            var detail = registered
                ? provider == AssistantProvider.Codex
                    ? "연결 설정됨 · Codex의 /hooks에서 새 훅을 검토하고 신뢰해주세요."
                    : "연결 설정됨 · Claude Code 세션을 다시 시작하면 적용됩니다."
                : handlers.Length > 0 ? "기존 DesktopPet 연결을 업데이트해야 합니다."
                : detected ? "연결되지 않음" : "사용자 설정 폴더를 찾지 못했습니다.";
            if (registered && root["disableAllHooks"] is JsonValue disabled && disabled.TryGetValue<bool>(out var allDisabled) && allDisabled)
                detail += " 현재 이 설정에서 전체 훅이 비활성화되어 있습니다.";
            return new(provider, detected, registered, detail, path, registered && provider == AssistantProvider.Codex,
                HasOwnedRegistration: handlers.Length > 0);
        }
        catch (Exception ex) when (IsConfigurationFailure(ex))
        {
            return new(provider, detected, false, $"설정을 읽을 수 없습니다. 원본은 변경하지 않았습니다. {ex.Message}", path, Succeeded: false);
        }
    }

    public AssistantIntegrationStatus Connect(AssistantProvider provider) => Modify(provider, connect: true);
    public AssistantIntegrationStatus Disconnect(AssistantProvider provider) => Modify(provider, connect: false);

    public IReadOnlyList<AssistantIntegrationStatus> AutoConnectDetected() =>
        Enum.GetValues<AssistantProvider>().Select(provider =>
        {
            var status = GetStatus(provider);
            return status.Detected ? Connect(provider) : status;
        }).ToArray();

    private AssistantIntegrationStatus Modify(AssistantProvider provider, bool connect)
    {
        var path = GetConfigPath(provider);
        var directory = Path.GetDirectoryName(path)!;
        var mutexName = "Local\\DesktopPet.AssistantConfig." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant())));
        using var mutex = new Mutex(false, mutexName);
        var ownsMutex = false;
        try
        {
            try { ownsMutex = mutex.WaitOne(0); }
            catch (AbandonedMutexException) { ownsMutex = true; }
            if (!ownsMutex) throw new IOException("다른 DesktopPet 작업이 설정을 변경하고 있습니다. 다시 시도해주세요.");
            if (connect && !File.Exists(_hookScriptPath)) throw new FileNotFoundException("알림 연결 스크립트가 없습니다.", _hookScriptPath);
            var root = ReadConfiguration(path, out var original);
            var groups = GetStopGroups(root, create: connect);
            if (groups is null) return GetStatus(provider);
            var owned = groups.OfType<JsonObject>().SelectMany(group => (JsonArray)group["hooks"]!)
                .OfType<JsonObject>().Where(IsOwnedHandler).ToArray();
            var desired = connect ? CreateHandler(provider) : null;
            if (connect && owned.Length == 1 && JsonNode.DeepEquals(owned[0], desired) || !connect && owned.Length == 0)
                return GetStatus(provider);

            for (var groupIndex = groups.Count - 1; groupIndex >= 0; groupIndex--)
            {
                var group = (JsonObject)groups[groupIndex]!;
                var handlers = (JsonArray)group["hooks"]!;
                var removed = false;
                for (var i = handlers.Count - 1; i >= 0; i--)
                    if (handlers[i] is JsonObject handler && IsOwnedHandler(handler)) { handlers.RemoveAt(i); removed = true; }
                // Remove only an empty group that we changed and whose sole field is our handler array.
                if (removed && handlers.Count == 0 && group.Count == 1) groups.RemoveAt(groupIndex);
            }
            if (connect) groups.Add(new JsonObject { ["hooks"] = new JsonArray(desired) });

            Directory.CreateDirectory(directory);
            var bytes = new UTF8Encoding(false).GetBytes(root.ToJsonString(JsonOptions) + Environment.NewLine);
            WriteAtomic(path, original, bytes);
            return GetStatus(provider);
        }
        catch (Exception ex) when (IsConfigurationFailure(ex))
        {
            var status = GetStatus(provider);
            return status with { Detail = $"{(connect ? "연결" : "해제")}하지 못했습니다. {ex.Message}", Succeeded = false };
        }
        finally { if (ownsMutex) mutex.ReleaseMutex(); }
    }

    private void WriteAtomic(string path, byte[]? original, byte[] updated)
    {
        var temporary = path + ".desktop-pet-" + Guid.NewGuid().ToString("N") + ".tmp";
        var backup = path + ".desktop-pet-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + "-" + Guid.NewGuid().ToString("N") + ".bak";
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                output.Write(updated);
                output.Flush(flushToDisk: true);
            }
            _beforeCommit?.Invoke(path);
            var current = File.Exists(path) ? File.ReadAllBytes(path) : null;
            if (!SameBytes(original, current)) throw new IOException("저장 중 다른 프로그램이 설정을 변경했습니다. 변경을 보존했으니 다시 시도해주세요.");
            if (original is null) File.Move(temporary, path); // No overwrite: a concurrent new file wins.
            else
            {
                File.Replace(temporary, path, backup, ignoreMetadataErrors: true);
                // Replace captures the exact previous file atomically. Detect an external rename
                // in the small interval after our comparison; keep both versions recoverable.
                if (!File.ReadAllBytes(backup).AsSpan().SequenceEqual(original))
                {
                    if (File.ReadAllBytes(path).AsSpan().SequenceEqual(updated))
                        File.Replace(backup, path, backup + ".conflict", ignoreMetadataErrors: true);
                    throw new IOException($"다른 프로그램의 동시 변경을 발견했습니다. 복구 사본을 확인해주세요: {backup}");
                }
            }
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private JsonObject CreateHandler(AssistantProvider provider)
    {
        var source = provider == AssistantProvider.Claude ? "claude" : "codex";
        // The outer command contains no user-supplied shell metacharacters. UTF-16LE is the
        // Windows PowerShell EncodedCommand contract; the inner script uses literal strings.
        var script = $"& {QuotePowerShellLiteral(_hookScriptPath)} -Source '{source}' -RegistrationId '{RegistrationId}'";
        var command = CommandPrefix + Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var handler = new JsonObject { ["type"] = "command", ["command"] = command, ["timeout"] = 5 };
        if (provider == AssistantProvider.Codex)
        {
            handler["commandWindows"] = command;
            handler["statusMessage"] = RegistrationId;
        }
        return handler;
    }

    private static bool IsOwnedHandler(JsonObject handler)
    {
        if (handler["type"]?.GetValueKind() != JsonValueKind.String || handler["type"]!.GetValue<string>() != "command") return false;
        if (handler["command"] is not JsonValue value || !value.TryGetValue<string>(out var command)) return false;
        if (command.StartsWith(CommandPrefix, StringComparison.Ordinal))
        {
            try
            {
                var script = Encoding.Unicode.GetString(Convert.FromBase64String(command[CommandPrefix.Length..]));
                return Regex.IsMatch(script,
                    @"^& '(?:[^']|'')+' -Source '(?:claude|codex)' -RegistrationId 'DesktopPet\.StopHook\.v1'$",
                    RegexOptions.CultureInvariant);
            }
            catch (FormatException) { return false; }
        }
        // Recognize our earlier plain -File registrations without matching unrelated text or paths.
        return command.StartsWith("powershell", StringComparison.OrdinalIgnoreCase) &&
            Regex.IsMatch(command, @"(?:^|\s)-RegistrationId\s+(?:DesktopPet\.StopHook\.v1|'DesktopPet\.StopHook\.v1'|""DesktopPet\.StopHook\.v1"")(?:\s|$)", RegexOptions.CultureInvariant);
    }

    private static JsonObject ReadConfiguration(string path, out byte[]? original)
    {
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("연결된 설정 파일은 자동 변경하지 않습니다.");
        original = File.Exists(path) ? File.ReadAllBytes(path) : null;
        if (original is null) return new JsonObject();
        using var stream = new MemoryStream(original, writable: false);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var json = reader.ReadToEnd();
        using var document = JsonDocument.Parse(json);
        ValidateUniqueProperties(document.RootElement);
        var root = JsonNode.Parse(json) as JsonObject ?? throw new InvalidDataException("설정 파일의 최상위 값은 JSON 객체여야 합니다.");
        GetStopGroups(root, create: false); // Validate the structures we will traverse before mutation.
        return root;
    }

    private static JsonArray? GetStopGroups(JsonObject root, bool create)
    {
        if (!root.TryGetPropertyValue("hooks", out var hooksNode))
        {
            if (!create) return null;
            root["hooks"] = hooksNode = new JsonObject();
        }
        if (hooksNode is not JsonObject hooks) throw new InvalidDataException("hooks 설정이 JSON 객체가 아닙니다.");
        if (!hooks.TryGetPropertyValue("Stop", out var stopNode))
        {
            if (!create) return null;
            hooks["Stop"] = stopNode = new JsonArray();
        }
        if (stopNode is not JsonArray groups) throw new InvalidDataException("Stop 설정이 JSON 배열이 아닙니다.");
        foreach (var groupNode in groups)
        {
            if (groupNode is not JsonObject group || group["hooks"] is not JsonArray handlers || handlers.Any(node => node is not JsonObject))
                throw new InvalidDataException("Stop 훅 그룹의 형식을 확인해주세요.");
        }
        return groups;
    }

    private static void ValidateUniqueProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!keys.Add(property.Name)) throw new InvalidDataException($"중복 JSON 키가 있습니다: {property.Name}");
                ValidateUniqueProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) ValidateUniqueProperties(child);
    }

    private string GetConfigPath(AssistantProvider provider) => provider switch
    {
        AssistantProvider.Claude => Path.Combine(_claudeDirectory, "settings.json"),
        AssistantProvider.Codex => Path.Combine(_codexDirectory, "hooks.json"),
        _ => throw new ArgumentOutOfRangeException(nameof(provider)),
    };

    private static string NormalizePath(string path, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Any(character => character < 32 || character is '"' or '<' or '>' or '|'))
            throw new ArgumentException("유효한 Windows 경로를 지정해주세요.", parameterName);
        return Path.GetFullPath(path);
    }

    private static string QuotePowerShellLiteral(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    private static bool SameBytes(byte[]? first, byte[]? second) => first is null ? second is null : second is not null && first.AsSpan().SequenceEqual(second);
    private static bool IsConfigurationFailure(Exception exception) =>
        exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException;
}
