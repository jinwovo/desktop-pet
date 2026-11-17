using System.IO.Compression;
using System.Text.Json;

namespace DesktopPet.Core;

/// <summary>A catalog entry tied to one open, read-locked archive.</summary>
public sealed class ArchiveCharacter
{
    internal ArchiveCharacter(string prefix, string name, string displayName)
    {
        Prefix = prefix;
        Name = name;
        DisplayName = displayName;
    }

    internal string Prefix { get; }
    public string Name { get; }
    public string DisplayName { get; }
    public string Label => Name == DisplayName ? Name : $"{DisplayName} ({Name})";
}

/// <summary>
/// Reads a bounded ZIP once and keeps its file handle locked through selection and import.
/// Only a selected manifest and its referenced PNGs are extracted; CharacterLibrary performs
/// its normal staged validation and atomic publication before the character becomes visible.
/// </summary>
public sealed class CharacterArchive : IDisposable
{
    public const long MaxArchiveBytes = 64L * 1024 * 1024;
    public const long MaxExpandedBytes = 256L * 1024 * 1024;
    public const int MaxEntryCount = 2048;
    public const int MaxManifestBytes = 256 * 1024;
    private readonly ZipArchive _archive;
    private readonly Dictionary<string, ZipArchiveEntry> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<ArchiveCharacter, PreparedCharacter> _prepared = new();
    private bool _disposed;
    private sealed record PreparedCharacter(byte[] Manifest, IReadOnlyDictionary<string, ZipArchiveEntry> Frames);

    private CharacterArchive(ZipArchive archive)
    {
        _archive = archive;
        ValidateEntries();
        var candidates = new List<ArchiveCharacter>();
        foreach (var pair in _files.Where(pair =>
                     Path.GetFileName(pair.Key).Equals("character.json", StringComparison.OrdinalIgnoreCase)))
        {
            if (candidates.Count >= 64) throw new InvalidDataException("ZIP에는 캐릭터를 64개까지 넣을 수 있습니다.");
            var manifest = ReadBounded(pair.Value, MaxManifestBytes);
            CharacterDefinitionDto dto;
            try
            {
                using var reader = new StreamReader(new MemoryStream(manifest), detectEncodingFromByteOrderMarks: true);
                dto = JsonSerializer.Deserialize<CharacterDefinitionDto>(reader.ReadToEnd(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? throw new InvalidDataException("캐릭터 설정이 비어 있습니다.");
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException)
            {
                throw new InvalidDataException("ZIP의 캐릭터 설정을 읽을 수 없습니다.", ex);
            }
            var prefix = pair.Key[..^"character.json".Length];
            var name = prefix.Length == 0 ? dto.Name : prefix.TrimEnd('/').Split('/')[^1];
            CharacterLibrary.ValidateName(name);
            var displayName = string.IsNullOrWhiteSpace(dto.Name) ? name : dto.Name;
            if (displayName.Length > 120 || displayName.Any(char.IsControl))
                throw new InvalidDataException("캐릭터 표시 이름이 너무 길거나 올바르지 않습니다.");
            var frames = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            long bytes = manifest.Length;
            foreach (var reference in CharacterLibrary.GetDeclaredFrameNames(dto))
            {
                if (string.IsNullOrWhiteSpace(reference)) throw new InvalidDataException("PNG 참조가 비어 있습니다.");
                // Windows PowerShell/.NET Framework archives may also use backslashes.
                var relative = reference.Replace('\\', '/');
                ValidateEntryPath(relative);
                if (!Path.GetExtension(relative).Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                    !_files.TryGetValue(prefix + relative, out var frame))
                    throw new InvalidDataException("ZIP에 설정에서 참조한 PNG가 없습니다.");
                if (!frames.TryAdd(relative, frame)) continue;
                if (frames.Count + 1 > CharacterLibrary.MaxFileCount || frame.Length > CharacterLibrary.MaxTotalBytes - bytes)
                    throw new InvalidDataException("선택한 캐릭터의 파일 수 또는 크기가 너무 큽니다.");
                bytes += frame.Length;
            }
            var candidate = new ArchiveCharacter(prefix, name, displayName);
            candidates.Add(candidate);
            _prepared.Add(candidate, new PreparedCharacter(manifest, frames));
        }
        if (candidates.Count == 0) throw new InvalidDataException("ZIP에서 character.json을 찾지 못했습니다.");
        Characters = Array.AsReadOnly(candidates.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public IReadOnlyList<ArchiveCharacter> Characters { get; }

    public static CharacterArchive Open(string path)
    {
        CharacterLibrary.EnsureNoReparsePoints(Path.GetFullPath(path));
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        ZipArchive? archive = null;
        try
        {
            if (stream.Length > MaxArchiveBytes) throw new InvalidDataException("ZIP 파일은 64 MiB 이하여야 합니다.");
            archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            return new CharacterArchive(archive);
        }
        catch
        {
            archive?.Dispose();
            stream.Dispose();
            throw;
        }
    }

    public string Import(ArchiveCharacter character, CharacterLibrary library, Action<string>? validate = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(library);
        if (!_prepared.TryGetValue(character, out var prepared))
            throw new ArgumentException("다른 ZIP에서 선택한 캐릭터입니다.", nameof(character));
        var tempRoot = Path.Combine(Path.GetTempPath(), "DesktopPet-Zip-" + Guid.NewGuid().ToString("N"));
        CharacterLibrary.EnsureNoReparsePoints(tempRoot);
        Directory.CreateDirectory(tempRoot);
        try
        {
            var folder = Path.Combine(tempRoot, character.Name);
            Directory.CreateDirectory(folder);
            CharacterLibrary.EnsureNoReparsePoints(folder);
            File.WriteAllBytes(Path.Combine(folder, "character.json"), prepared.Manifest);
            long remaining = CharacterLibrary.MaxTotalBytes - prepared.Manifest.Length;
            foreach (var pair in prepared.Frames)
            {
                var destination = Path.Combine(folder, pair.Key.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                CharacterLibrary.EnsureNoReparsePoints(destination);
                using var input = pair.Value.Open();
                using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                var copied = CopyBounded(input, output, Math.Min(pair.Value.Length, remaining));
                if (copied != pair.Value.Length) throw new InvalidDataException("ZIP 파일 길이가 일치하지 않습니다.");
                remaining -= copied;
            }
            return library.ImportFolder(folder, validate);
        }
        finally { CharacterLibrary.CleanupStage(tempRoot); }
    }

    private void ValidateEntries()
    {
        if (_archive.Entries.Count > MaxEntryCount) throw new InvalidDataException("ZIP 항목이 2048개를 초과합니다.");
        var paths = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        long expanded = 0;
        foreach (var entry in _archive.Entries)
        {
            var normalized = entry.FullName.Replace('\\', '/');
            var directory = normalized.EndsWith('/');
            var path = directory ? normalized[..^1] : normalized;
            ValidateEntryPath(path);
            var kind = (entry.ExternalAttributes >> 16) & 0xF000;
            if ((entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0 ||
                kind != 0 && kind != (directory ? 0x4000 : 0x8000))
                throw new InvalidDataException("ZIP의 링크나 특수 파일은 지원하지 않습니다.");
            if (!paths.TryAdd(path, directory)) throw new InvalidDataException("ZIP에 중복 경로가 있습니다.");
            if (entry.Length < 0 || entry.Length > MaxExpandedBytes - expanded || directory && entry.Length != 0)
                throw new InvalidDataException("ZIP 압축 해제 크기가 제한을 초과합니다.");
            expanded += entry.Length;
            if (!directory) _files.Add(path, entry);
        }
        foreach (var path in paths.Keys)
        {
            var end = path.LastIndexOf('/');
            while (end >= 0)
            {
                if (paths.TryGetValue(path[..end], out var directory) && !directory)
                    throw new InvalidDataException("ZIP의 파일과 폴더 경로가 충돌합니다.");
                end = path.LastIndexOf('/', end - 1);
            }
        }
    }

    private static void ValidateEntryPath(string path)
    {
        if (path.Length > 512 || path.Contains('\\')) throw new InvalidDataException("ZIP 경로가 올바르지 않습니다.");
        CharacterLibrary.ValidateRelativePath(path);
    }

    private static byte[] ReadBounded(ZipArchiveEntry entry, int limit)
    {
        if (entry.Length > limit) throw new InvalidDataException("캐릭터 설정 파일이 너무 큽니다.");
        using var input = entry.Open();
        using var output = new MemoryStream();
        var copied = CopyBounded(input, output, Math.Min(entry.Length, limit));
        if (copied != entry.Length) throw new InvalidDataException("ZIP 설정 파일 길이가 일치하지 않습니다.");
        return output.ToArray();
    }

    private static long CopyBounded(Stream input, Stream output, long limit)
    {
        var buffer = new byte[81920];
        long copied = 0;
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) != 0)
        {
            if (read > limit - copied) throw new InvalidDataException("ZIP 압축 해제 크기가 제한을 초과합니다.");
            output.Write(buffer, 0, read);
            copied += read;
        }
        return copied;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _archive.Dispose();
    }
}
