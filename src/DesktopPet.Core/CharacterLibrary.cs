using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DesktopPet.Core;

/// <summary>
/// Combines bundled characters and a caller-selected user library. No directory or user setting
/// is created until ImportFolder succeeds far enough to stage a validated character package.
/// </summary>
public sealed class CharacterLibrary
{
    public const int MaxFileCount = 256;
    public const long MaxTotalBytes = 64L * 1024 * 1024;
    public const long MaxDecodedBytes = 64L * 1024 * 1024;
    private const string StagePrefix = ".desktop-pet-import-";
    private const int MaxDirectoryCount = 256;

    public CharacterLibrary(string bundledRoot, string customRoot)
    {
        BundledRoot = NormalizeRoot(bundledRoot, nameof(bundledRoot));
        CustomRoot = NormalizeRoot(customRoot, nameof(customRoot));
        if (IsWithin(BundledRoot, CustomRoot) || IsWithin(CustomRoot, BundledRoot))
            throw new ArgumentException("기본 캐릭터와 사용자 캐릭터 폴더는 서로 겹칠 수 없습니다.");
    }

    public string BundledRoot { get; }
    public string CustomRoot { get; }

    public IReadOnlyList<string> GetNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddNames(BundledRoot, names);
        AddNames(CustomRoot, names);
        return Array.AsReadOnly(names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public string Resolve(string name)
    {
        ValidateName(name);
        foreach (var root in new[] { BundledRoot, CustomRoot })
        {
            EnsureNoReparsePoints(root);
            var folder = FindFolder(root, name);
            if (folder is null || !File.Exists(Path.Combine(folder, "character.json"))) continue;
            EnsureNoReparsePoints(folder);
            EnsureNoReparsePoints(Path.Combine(folder, "character.json"));
            return folder;
        }
        throw new InvalidDataException($"캐릭터 '{name}'을(를) 찾을 수 없습니다.");
    }

    /// <summary>
    /// Imports only character.json and its referenced PNG frames. The optional validator runs
    /// against the staged copy, allowing a UI to decode every frame before the folder is published.
    /// </summary>
    public string ImportFolder(string source, Action<string>? validate = null)
    {
        var sourceRoot = NormalizeRoot(source, nameof(source));
        if (IsWithin(sourceRoot, CustomRoot) || IsWithin(CustomRoot, sourceRoot) ||
            string.Equals(sourceRoot, BundledRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("가져올 캐릭터는 사용자 캐릭터 보관 폴더 밖에서 선택해주세요.");
        var name = Path.GetFileName(sourceRoot);
        ValidateName(name);
        EnsureNoReparsePoints(sourceRoot);
        if (!Directory.Exists(sourceRoot)) throw new DirectoryNotFoundException("가져올 캐릭터 폴더를 찾을 수 없습니다.");
        ValidateSourceTree(sourceRoot);
        var definition = CharacterLoader.Load(sourceRoot);
        ValidateDeclaredFrames(sourceRoot);
        var frames = definition.AllFramePaths().ToArray();
        ValidateDecodedBudget(definition, frames.Length);
        var files = new[] { Path.Combine(sourceRoot, "character.json") }.Concat(frames).ToArray();
        if (files.Length > MaxFileCount) throw new InvalidDataException("캐릭터 파일은 256개 이하여야 합니다.");
        foreach (var frame in frames)
            if (!string.Equals(Path.GetExtension(frame), ".png", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("준비된 캐릭터 폴더에는 PNG 프레임을 사용해주세요.");

        var mutexName = "Local\\DesktopPet.CharacterLibrary." + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(CustomRoot.ToUpperInvariant())));
        using var mutex = new Mutex(false, mutexName);
        var ownsMutex = false;
        string? stage = null;
        try
        {
            try { ownsMutex = mutex.WaitOne(0); }
            catch (AbandonedMutexException) { ownsMutex = true; }
            if (!ownsMutex) throw new IOException("다른 캐릭터를 가져오는 중입니다. 잠시 후 다시 시도해주세요.");
            EnsureNoReparsePoints(BundledRoot);
            EnsureNoReparsePoints(CustomRoot);
            EnsureNameAvailable(name);
            Directory.CreateDirectory(CustomRoot);
            EnsureNoReparsePoints(CustomRoot);
            stage = Path.Combine(CustomRoot, StagePrefix + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            EnsureNoReparsePoints(stage);
            long copiedBytes = 0;
            foreach (var path in files)
            {
                EnsureNoReparsePoints(path);
                var relative = Path.GetRelativePath(sourceRoot, path);
                ValidateRelativePath(relative);
                var destination = Path.Combine(stage, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                EnsureNoReparsePoints(destination);
                using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (input.Length > MaxTotalBytes - copiedBytes)
                    throw new InvalidDataException("캐릭터 파일의 합계는 64 MiB 이하여야 합니다.");
                using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                input.CopyTo(output);
                copiedBytes += input.Length;
            }

            // Validate the copied manifest, not an earlier view of the caller's mutable source.
            ValidatePreparedStage(stage);
            validate?.Invoke(stage);
            ValidatePreparedStage(stage);
            EnsureNoReparsePoints(CustomRoot);
            EnsureNameAvailable(name);
            Directory.Move(stage, Path.Combine(CustomRoot, name));
            stage = null;
            return name;
        }
        finally
        {
            if (stage is not null) CleanupStage(stage);
            if (ownsMutex) mutex.ReleaseMutex();
        }
    }

    private static void ValidateDecodedBudget(CharacterDefinition definition, int frameCount)
    {
        var decodedBytes = checked((long)definition.FrameWidth * definition.FrameHeight * 4 * frameCount);
        if (decodedBytes > MaxDecodedBytes)
            throw new InvalidDataException("캐릭터 프레임을 펼친 메모리 크기는 64 MiB 이하여야 합니다.");
    }

    private static void ValidatePreparedStage(string stage)
    {
        var actual = ValidateSourceTree(stage);
        var definition = CharacterLoader.Load(stage);
        ValidateDeclaredFrames(stage);
        ValidateDecodedBudget(definition, definition.AllFramePaths().Count());
        var expected = new HashSet<string>(definition.AllFramePaths().Append(Path.Combine(stage, "character.json")), StringComparer.OrdinalIgnoreCase);
        if (!expected.SetEquals(actual))
            throw new InvalidDataException("준비된 캐릭터에는 설정과 참조된 PNG 파일만 포함할 수 있습니다.");
    }

    private static void ValidateDeclaredFrames(string folder)
    {
        CharacterDefinitionDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<CharacterDefinitionDto>(File.ReadAllText(Path.Combine(folder, "character.json")),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex) { throw new InvalidDataException("캐릭터 설정을 읽을 수 없습니다.", ex); }
        var frames = dto?.Frames ?? throw new InvalidDataException("캐릭터 프레임 설정을 준비해주세요.");
        var lists = new[] { frames.Idle, frames.Walk, frames.WalkFront, frames.WalkTurn, frames.WalkSettleSide, frames.WalkSettleTurn, frames.Sit,
            frames.LookLeft, frames.LookRight, frames.Drag, frames.Fall, frames.Cry, frames.Angry, frames.Sleep };
        IEnumerable<string?> names = lists.SelectMany(list => list ?? Enumerable.Empty<string>());
        if (dto?.WalkRig is { } rig)
        {
            names = names.Concat(new[] { rig.Side?.Body, rig.Side?.NearFoot, rig.Side?.FarFoot,
                rig.Turn?.Body, rig.Turn?.NearFoot, rig.Turn?.FarFoot });
        }
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name) || !string.Equals(Path.GetExtension(name), ".png", StringComparison.OrdinalIgnoreCase) ||
                CharacterLoader.ResolveExisting(folder, new[] { name }).Count != 1)
                throw new InvalidDataException("설정에서 참조한 PNG 프레임을 모두 준비해주세요.");
        }
    }

    private void EnsureNameAvailable(string name)
    {
        if (FindFolder(BundledRoot, name) is not null || FindFolder(CustomRoot, name) is not null ||
            File.Exists(Path.Combine(BundledRoot, name)) || File.Exists(Path.Combine(CustomRoot, name)))
            throw new InvalidDataException($"'{name}' 이름의 캐릭터가 이미 있습니다. 다른 폴더 이름으로 준비해주세요.");
    }

    private static string? FindFolder(string root, string name) => Directory.Exists(root)
        ? Directory.EnumerateDirectories(root).FirstOrDefault(folder =>
            string.Equals(Path.GetFileName(folder), name, StringComparison.OrdinalIgnoreCase))
        : null;

    private static void AddNames(string root, HashSet<string> names)
    {
        EnsureNoReparsePoints(root);
        if (!Directory.Exists(root)) return;
        foreach (var folder in Directory.EnumerateDirectories(root))
        {
            var name = Path.GetFileName(folder);
            if (!IsValidName(name)) continue;
            var manifest = Path.Combine(folder, "character.json");
            if (!File.Exists(manifest)) continue;
            try { EnsureNoReparsePoints(manifest); }
            catch (InvalidDataException) { continue; }
            names.Add(name);
        }
    }

    private static IReadOnlyList<string> ValidateSourceTree(string root)
    {
        EnsureNoReparsePoints(root);
        var directories = new Queue<string>();
        directories.Enqueue(root);
        var directoryCount = 1;
        var fileCount = 0;
        long totalBytes = 0;
        var files = new List<string>();
        while (directories.Count > 0)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(directories.Dequeue()))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("링크가 포함된 캐릭터 폴더는 가져올 수 없습니다.");
                ValidateRelativePath(Path.GetRelativePath(root, entry));
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (++directoryCount > MaxDirectoryCount)
                        throw new InvalidDataException("캐릭터의 하위 폴더가 너무 많습니다.");
                    directories.Enqueue(entry);
                }
                else
                {
                    files.Add(entry);
                    if (++fileCount > MaxFileCount) throw new InvalidDataException("캐릭터 파일은 256개 이하여야 합니다.");
                    var length = new FileInfo(entry).Length;
                    if (length > MaxTotalBytes - totalBytes)
                        throw new InvalidDataException("캐릭터 파일의 합계는 64 MiB 이하여야 합니다.");
                    totalBytes += length;
                }
            }
        }
        return files;
    }

    private static void CleanupStage(string stage)
    {
        try
        {
            if (!Directory.Exists(stage)) return;
            // A validation callback may fail or leave a link. Never recurse through such a path.
            EnsureNoReparsePoints(stage);
            DeleteStageContents(stage);
            Directory.Delete(stage);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            System.Diagnostics.Trace.TraceWarning($"Character import staging cleanup: {ex.Message}");
        }
    }

    private static void DeleteStageContents(string folder)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(folder))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                if ((attributes & FileAttributes.Directory) != 0) Directory.Delete(entry);
                else File.Delete(entry);
            }
            else if ((attributes & FileAttributes.Directory) != 0)
            {
                DeleteStageContents(entry);
                Directory.Delete(entry);
            }
            else File.Delete(entry);
        }
    }

    private static void EnsureNoReparsePoints(string path)
    {
        var current = path;
        while (!string.IsNullOrEmpty(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("캐릭터 폴더는 링크를 통과할 수 없습니다.");
            current = Path.GetDirectoryName(current);
        }
    }

    private static string NormalizeRoot(string path, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new ArgumentException("캐릭터 폴더의 절대 경로를 지정해주세요.", parameterName);
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var root = Path.GetPathRoot(fullPath)!;
        if (string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("드라이브 전체를 캐릭터 폴더로 사용할 수 없습니다.", parameterName);
        foreach (var segment in fullPath[root.Length..].Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
            if (!IsValidPathSegment(segment)) throw new ArgumentException("모호한 Windows 폴더 경로는 사용할 수 없습니다.", parameterName);
        return fullPath;
    }

    private static void ValidateRelativePath(string path)
    {
        if (Path.IsPathRooted(path) || path.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }).Any(segment => !IsValidPathSegment(segment)))
            throw new InvalidDataException("캐릭터 파일은 안전한 하위 경로에 있어야 합니다.");
    }

    private static void ValidateName(string name)
    {
        if (!IsValidName(name)) throw new InvalidDataException("캐릭터 이름은 안전한 폴더 이름 하나여야 합니다.");
    }

    private static bool IsValidName(string? name) => IsValidPathSegment(name) && !name!.StartsWith(StagePrefix, StringComparison.OrdinalIgnoreCase);

    private static bool IsValidPathSegment(string? segment)
    {
        if (string.IsNullOrWhiteSpace(segment) || segment is "." or ".." || segment.Trim() != segment ||
            segment.EndsWith('.') || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
        var stem = segment.Split('.')[0];
        return !(stem.Equals("CON", StringComparison.OrdinalIgnoreCase) || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
            stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
            stem[3] is >= '1' and <= '9');
    }

    private static bool IsWithin(string path, string root) => string.Equals(path, root, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
