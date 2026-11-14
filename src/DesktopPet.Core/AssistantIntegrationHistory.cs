using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DesktopPet.Core;

public sealed record AssistantIntegrationProfile(AssistantProvider Provider, string ConfigPath);

/// <summary>
/// Remembers successfully connected profiles so an uninstall can disconnect them even when
/// CLAUDE_CONFIG_DIR/CODEX_HOME differs from the environment used during registration.
/// The caller supplies the storage path; no implicit user-home or AppData access occurs here.
/// </summary>
public sealed class AssistantIntegrationHistory
{
    private readonly string _historyPath;
    private readonly Action<string>? _beforeCommit;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public AssistantIntegrationHistory(string historyPath) : this(historyPath, null) { }

    internal AssistantIntegrationHistory(string historyPath, Action<string>? beforeCommit)
    {
        _historyPath = NormalizeAbsolutePath(historyPath);
        _beforeCommit = beforeCommit;
    }

    public void Record(AssistantIntegrationStatus registeredSuccess)
    {
        ArgumentNullException.ThrowIfNull(registeredSuccess);
        if (!registeredSuccess.Succeeded || !registeredSuccess.Registered) return;
        var profile = NormalizeProfile(registeredSuccess.Provider, registeredSuccess.ConfigPath);
        Mutate(profiles =>
        {
            if (profiles.Any(existing => SameProfile(existing, profile))) return false;
            profiles.Add(profile);
            return true;
        });
    }

    public IReadOnlyList<AssistantIntegrationProfile> GetProfiles() => Read(out _).ToArray();

    public void Remove(AssistantProvider provider, string configPath)
    {
        var profile = NormalizeProfile(provider, configPath);
        Mutate(profiles => profiles.RemoveAll(existing => SameProfile(existing, profile)) > 0);
    }

    private void Mutate(Func<List<AssistantIntegrationProfile>, bool> update)
    {
        var mutexName = "Local\\DesktopPet.AssistantHistory." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_historyPath.ToUpperInvariant())));
        using var mutex = new Mutex(false, mutexName);
        var ownsMutex = false;
        try
        {
            try { ownsMutex = mutex.WaitOne(0); }
            catch (AbandonedMutexException) { ownsMutex = true; }
            if (!ownsMutex) throw new IOException("다른 작업이 AI 연결 기록을 저장하고 있습니다. 다시 시도해주세요.");
            var profiles = Read(out var original);
            if (!update(profiles)) return;
            var document = new
            {
                schemaVersion = 1,
                product = "DesktopPet",
                profiles = profiles.OrderBy(profile => profile.Provider).ThenBy(profile => profile.ConfigPath, StringComparer.OrdinalIgnoreCase)
                    .Select(profile => new { provider = profile.Provider.ToString().ToLowerInvariant(), configPath = profile.ConfigPath }).ToArray(),
            };
            var bytes = new UTF8Encoding(false).GetBytes(JsonSerializer.Serialize(document, JsonOptions) + Environment.NewLine);
            WriteAtomic(original, bytes);
        }
        finally { if (ownsMutex) mutex.ReleaseMutex(); }
    }

    private List<AssistantIntegrationProfile> Read(out byte[]? original)
    {
        ValidateHistoryPath();
        original = File.Exists(_historyPath) ? File.ReadAllBytes(_historyPath) : null;
        if (original is null) return new List<AssistantIntegrationProfile>();
        try
        {
            using var stream = new MemoryStream(original, writable: false);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            using var document = JsonDocument.Parse(reader.ReadToEnd());
            var root = document.RootElement;
            ValidateUniqueProperties(root);
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("schemaVersion", out var version) ||
                !version.TryGetInt32(out var schemaVersion) || schemaVersion != 1 ||
                !root.TryGetProperty("product", out var product) || product.GetString() != "DesktopPet" ||
                !root.TryGetProperty("profiles", out var entries) || entries.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("AI 연결 기록 형식이 올바르지 않습니다. 원본은 변경하지 않았습니다.");
            var profiles = new List<AssistantIntegrationProfile>();
            foreach (var entry in entries.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("provider", out var providerNode) ||
                    providerNode.ValueKind != JsonValueKind.String || !entry.TryGetProperty("configPath", out var configPath) ||
                    configPath.ValueKind != JsonValueKind.String)
                    throw new InvalidDataException("AI 연결 기록에 잘못된 프로필이 있습니다.");
                var provider = providerNode.GetString() switch
                {
                    "claude" => AssistantProvider.Claude,
                    "codex" => AssistantProvider.Codex,
                    _ => throw new InvalidDataException("AI 연결 기록에 지원하지 않는 도우미가 있습니다."),
                };
                var profile = NormalizeProfile(provider, configPath.GetString()!);
                if (!profiles.Any(existing => SameProfile(existing, profile))) profiles.Add(profile);
            }
            return profiles;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException)
        {
            throw new InvalidDataException("AI 연결 기록을 읽을 수 없습니다. 원본은 변경하지 않았습니다.", ex);
        }
    }

    private void WriteAtomic(byte[]? original, byte[] updated)
    {
        ValidateHistoryPath();
        Directory.CreateDirectory(Path.GetDirectoryName(_historyPath)!);
        var temporary = _historyPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var backup = _historyPath + "." + Guid.NewGuid().ToString("N") + ".bak";
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                output.Write(updated);
                output.Flush(flushToDisk: true);
            }
            _beforeCommit?.Invoke(_historyPath);
            ValidateHistoryPath();
            var current = File.Exists(_historyPath) ? File.ReadAllBytes(_historyPath) : null;
            if (!SameBytes(original, current)) throw new IOException("다른 프로그램이 AI 연결 기록을 변경했습니다. 다시 시도해주세요.");
            if (original is null) File.Move(temporary, _historyPath);
            else
            {
                File.Replace(temporary, _historyPath, backup, ignoreMetadataErrors: true);
                if (!File.ReadAllBytes(backup).AsSpan().SequenceEqual(original))
                {
                    if (File.ReadAllBytes(_historyPath).AsSpan().SequenceEqual(updated))
                        File.Replace(backup, _historyPath, backup + ".conflict", ignoreMetadataErrors: true);
                    throw new IOException("AI 연결 기록의 동시 변경을 발견했습니다. 복구 사본을 보존했습니다.");
                }
            }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static AssistantIntegrationProfile NormalizeProfile(AssistantProvider provider, string configPath)
    {
        var expectedFileName = provider switch
        {
            AssistantProvider.Claude => "settings.json",
            AssistantProvider.Codex => "hooks.json",
            _ => throw new ArgumentOutOfRangeException(nameof(provider)),
        };
        var path = NormalizeAbsolutePath(configPath);
        if (!string.Equals(Path.GetFileName(path), expectedFileName, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"이 도우미의 설정 파일 이름은 {expectedFileName}이어야 합니다.", nameof(configPath));
        return new AssistantIntegrationProfile(provider, path);
    }

    private static string NormalizeAbsolutePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) ||
            path.Any(character => character < 32 || character is '"' or '<' or '>' or '|' or '?' or '*'))
            throw new ArgumentException("절대 Windows 파일 경로를 지정해주세요.", nameof(path));
        var fullPath = Path.GetFullPath(path);
        var relativePart = fullPath[Path.GetPathRoot(fullPath)!.Length..];
        if (relativePart.Contains(':') || relativePart.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.None)
            .Any(segment => segment.EndsWith(' ') || segment.EndsWith('.')))
            throw new ArgumentException("대체 데이터 스트림 또는 공백·마침표로 끝나는 경로는 사용할 수 없습니다.", nameof(path));
        return fullPath;
    }

    private void ValidateHistoryPath()
    {
        for (string? current = _historyPath; current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("연결된 파일이나 폴더에 있는 AI 연결 기록은 자동 변경하지 않습니다.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    private static void ValidateUniqueProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!keys.Add(property.Name)) throw new InvalidDataException("AI 연결 기록에 중복 JSON 키가 있습니다.");
                ValidateUniqueProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var entry in element.EnumerateArray()) ValidateUniqueProperties(entry);
    }

    private static bool SameProfile(AssistantIntegrationProfile left, AssistantIntegrationProfile right) =>
        left.Provider == right.Provider && string.Equals(left.ConfigPath, right.ConfigPath, StringComparison.OrdinalIgnoreCase);
    private static bool SameBytes(byte[]? first, byte[]? second) => first is null ? second is null : second is not null && first.AsSpan().SequenceEqual(second);
}
