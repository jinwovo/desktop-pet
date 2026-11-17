using System.Text.Json;

namespace DesktopPet.Core;

public static class CharacterLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Lists every subfolder of <paramref name="charactersRoot"/> that has a character.json.</summary>
    public static IReadOnlyList<string> ListAvailableCharacterNames(string charactersRoot)
    {
        if (!Directory.Exists(charactersRoot))
        {
            return Array.Empty<string>();
        }

        return Directory.GetDirectories(charactersRoot)
            .Where(dir => File.Exists(Path.Combine(dir, "character.json")))
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static CharacterDefinition Load(string characterFolder)
    {
        try
        {
            return LoadCore(Path.GetFullPath(characterFolder));
        }
        catch (InvalidDataException) { throw; }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new InvalidDataException($"캐릭터 '{characterFolder}'을(를) 불러올 수 없습니다: {ex.Message}", ex);
        }
    }

    private static CharacterDefinition LoadCore(string characterFolder)
    {
        var jsonPath = Path.Combine(characterFolder, "character.json");
        EnsureNoReparsePoints(characterFolder, jsonPath);
        var dto = JsonSerializer.Deserialize<CharacterDefinitionDto>(File.ReadAllText(jsonPath), JsonOptions)
            ?? throw new InvalidDataException($"'{jsonPath}'의 캐릭터 내용은 null일 수 없습니다.");
        ValidateMetadata(dto, jsonPath);

        var framesByState = new Dictionary<PetState, IReadOnlyList<string>>
        {
            [PetState.Idle] = ResolveExisting(characterFolder, dto.Frames.Idle),
            [PetState.Walk] = ResolveExisting(characterFolder, dto.Frames.Walk),
            [PetState.Sit] = ResolveExisting(characterFolder, dto.Frames.Sit),
            [PetState.Dragged] = ResolveExisting(characterFolder, dto.Frames.Drag),
            [PetState.Falling] = ResolveExisting(characterFolder, dto.Frames.Fall),
            [PetState.Cry] = ResolveExisting(characterFolder, dto.Frames.Cry),
            [PetState.Angry] = ResolveExisting(characterFolder, dto.Frames.Angry),
            [PetState.Sleep] = ResolveExisting(characterFolder, dto.Frames.Sleep),
        };

        // LookAtCursor is directional (left/right of the pet); UI picks the right list at render time.
        var lookLeft = ResolveExisting(characterFolder, dto.Frames.LookLeft);
        var lookRight = ResolveExisting(characterFolder, dto.Frames.LookRight);
        var walkFront = ResolveWalkVariant(characterFolder, dto.Frames.WalkFront, framesByState[PetState.Walk], "walkFront", jsonPath);
        var walkTurn = ResolveWalkVariant(characterFolder, dto.Frames.WalkTurn, framesByState[PetState.Walk], "walkTurn", jsonPath);
        var walkSettleSide = ResolveWalkSettle(characterFolder, dto.Frames.WalkSettleSide, framesByState[PetState.Walk], "walkSettleSide", jsonPath);
        var walkSettleTurn = ResolveWalkSettle(characterFolder, dto.Frames.WalkSettleTurn,
            walkTurn.Count > 0 ? walkTurn : framesByState[PetState.Walk], "walkSettleTurn", jsonPath);
        framesByState[PetState.LookAtCursor] = lookLeft.Count > 0 ? lookLeft : framesByState[PetState.Idle];

        // idle must always have at least one frame; if missing, fall back to any other available frame.
        if (framesByState[PetState.Idle].Count == 0)
        {
            var anyFrame = framesByState.Values.Concat(new[] { lookRight }).FirstOrDefault(f => f.Count > 0)
                ?? throw new InvalidDataException($"'{jsonPath}'에 사용할 수 있는 프레임 파일이 없습니다.");
            framesByState[PetState.Idle] = anyFrame;
        }

        return new CharacterDefinition
        {
            Name = dto.Name,
            FolderPath = characterFolder,
            Scale = dto.Scale,
            FrameWidth = dto.FrameSizePx.Width,
            FrameHeight = dto.FrameSizePx.Height,
            AnchorX = dto.Anchor.X,
            AnchorY = dto.Anchor.Y,
            WalkSpeedPxPerSec = dto.WalkSpeedPxPerSec,
            WalkCycleDistancePx = dto.WalkCycleDistancePx,
            WalkRig = ResolveWalkRig(characterFolder, dto.WalkRig, jsonPath),
            BehaviorProfile = dto.Behavior ?? CharacterBehaviorProfile.Default,
            FrameIntervalMs = dto.FrameIntervalMs,
            FrontFacingRest = dto.FrontFacingRest,
            FramesByState = framesByState,
            LookLeftFrames = lookLeft,
            LookRightFrames = lookRight,
            WalkFrontFrames = walkFront,
            WalkTurnFrames = walkTurn,
            WalkSettleSideFrames = walkSettleSide,
            WalkSettleTurnFrames = walkSettleTurn,
            FrameDurationsByState = ResolveDurations(dto.FrameDurationsMs, framesByState, jsonPath),
        };
    }

    private static IReadOnlyList<string> ResolveWalkVariant(string folder, IEnumerable<string>? fileNames,
        IReadOnlyList<string> walk, string name, string jsonPath)
    {
        var frames = ResolveExisting(folder, fileNames);
        if (frames.Count > 0 && (walk.Count == 0 || frames.Count != walk.Count))
            throw new InvalidDataException($"'{jsonPath}': {name}의 프레임 개수는 walk와 같아야 합니다.");
        return frames;
    }

    private static WalkRigDefinition? ResolveWalkRig(string folder, WalkRigDto? rig, string jsonPath)
    {
        if (rig is null) return null;
        string ResolvePart(string? name)
        {
            if (string.IsNullOrWhiteSpace(name) || !string.Equals(Path.GetExtension(name), ".png", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"'{jsonPath}': walkRig의 각 부분에는 PNG 파일이 필요합니다.");
            var paths = ResolveExisting(folder, new[] { name });
            if (paths.Count != 1)
                throw new InvalidDataException($"'{jsonPath}': walkRig 부분 파일을 찾을 수 없습니다: {name}.");
            return paths[0];
        }
        WalkRigViewDefinition ResolveView(WalkRigViewDto view) =>
            new(ResolvePart(view.Body), ResolvePart(view.NearFoot), ResolvePart(view.FarFoot));
        return new WalkRigDefinition(rig.Version, rig.FootLiftPx!.Value, ResolveView(rig.Side!), ResolveView(rig.Turn!));
    }

    private static IReadOnlyList<string> ResolveWalkSettle(string folder, IEnumerable<string>? fileNames,
        IReadOnlyList<string> walk, string name, string jsonPath)
    {
        var frames = ResolveExisting(folder, fileNames);
        if (frames.Count > 0 && (walk.Count == 0 || frames.Count != walk.Count * 2))
            throw new InvalidDataException($"'{jsonPath}': {name}의 프레임 개수는 걷기 프레임의 2배여야 합니다.");
        return frames;
    }

    private static IReadOnlyDictionary<PetState, IReadOnlyList<double>> ResolveDurations(
        Dictionary<string, List<double>?>? specified, IReadOnlyDictionary<PetState, IReadOnlyList<string>> frames, string jsonPath)
    {
        var result = new Dictionary<PetState, IReadOnlyList<double>>();
        if (specified is null) return result;
        foreach (var (name, durations) in specified)
        {
            var state = name.ToLowerInvariant() switch
            {
                "idle" => PetState.Idle,
                "walk" => PetState.Walk,
                "sit" => PetState.Sit,
                "lookatcursor" => PetState.LookAtCursor,
                "drag" or "dragged" => PetState.Dragged,
                "fall" or "falling" => PetState.Falling,
                "cry" => PetState.Cry,
                "angry" => PetState.Angry,
                "sleep" => PetState.Sleep,
                _ => throw new InvalidDataException($"'{jsonPath}': 알 수 없는 frameDurationsMs 상태입니다: {name}."),
            };
            if (result.ContainsKey(state))
                throw new InvalidDataException($"'{jsonPath}': frameDurationsMs 상태가 중복되었습니다: {name}.");
            if (durations is null || durations.Count == 0 || durations.Count != frames[state].Count)
                throw new InvalidDataException($"'{jsonPath}': {name}의 frameDurationsMs 개수는 실제 프레임 개수와 같아야 합니다.");
            var total = 0d;
            foreach (var duration in durations)
            {
                if (!double.IsFinite(duration) || duration <= 0 || duration > double.MaxValue - total)
                    throw new InvalidDataException($"'{jsonPath}': frameDurationsMs는 유한한 양수이며 합계가 유한해야 합니다: {name}.");
                total += duration;
                if (!double.IsFinite(total))
                    throw new InvalidDataException($"'{jsonPath}': frameDurationsMs 합계가 너무 큽니다: {name}.");
            }
            result.Add(state, durations.ToArray());
        }
        return result;
    }

    /// <summary>Resolves optional frames within the character folder. Missing files use animation fallback.</summary>
    public static IReadOnlyList<string> ResolveExisting(string characterFolder, IEnumerable<string>? fileNames)
    {
        if (fileNames is null) return Array.Empty<string>();
        var root = Path.GetFullPath(characterFolder);
        var rootPrefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
        var result = new List<string>();
        foreach (var fileName in fileNames)
        {
            if (string.IsNullOrWhiteSpace(fileName) || Path.IsPathRooted(fileName))
                throw new InvalidDataException($"캐릭터 프레임은 폴더 안의 상대 경로여야 합니다: '{fileName}'.");
            string path;
            try
            {
                path = Path.GetFullPath(Path.Combine(root, fileName));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
                throw new InvalidDataException($"잘못된 캐릭터 프레임 경로입니다: '{fileName}'.", ex);
            }
            if (!path.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) || fileName.Contains(':'))
                throw new InvalidDataException($"캐릭터 프레임이 캐릭터 폴더 밖을 가리킵니다: '{fileName}'.");
            EnsureNoReparsePoints(root, path);
            if (File.Exists(path)) result.Add(path);
        }
        return result;
    }

    private static void ValidateMetadata(CharacterDefinitionDto dto, string jsonPath)
    {
        void Require(bool valid, string reason)
        {
            if (!valid) throw new InvalidDataException($"'{jsonPath}': {reason}");
        }

        Require(!string.IsNullOrWhiteSpace(dto.Name), "name은 비어 있을 수 없습니다.");
        Require(dto.FrameSizePx is not null, "frameSizePx는 null일 수 없습니다.");
        Require(dto.Anchor is not null, "anchor는 null일 수 없습니다.");
        Require(dto.Frames is not null, "frames는 null일 수 없습니다.");
        var size = dto.FrameSizePx!;
        var anchor = dto.Anchor!;
        Require(size.Width is > 0 and <= 4096 && size.Height is > 0 and <= 4096,
            "프레임의 가로·세로 크기는 1~4096이어야 합니다.");
        Require(anchor.X >= 0 && anchor.X <= size.Width && anchor.Y > 0 && anchor.Y <= size.Height,
            "anchor.x는 프레임 안에, anchor.y는 1~프레임 높이 안에 있어야 합니다.");
        Require(double.IsFinite(dto.Scale) && dto.Scale > 0 &&
                size.Width * dto.Scale <= 4096 && size.Height * dto.Scale <= 4096,
            "scale은 유한한 양수이며 적용 후 가로·세로 크기가 4096 이하여야 합니다.");
        Require(double.IsFinite(dto.WalkSpeedPxPerSec) && dto.WalkSpeedPxPerSec >= 0 && dto.WalkSpeedPxPerSec <= 2000,
            "walkSpeedPxPerSec은 0~2000의 유한한 값이어야 합니다.");
        Require(dto.WalkCycleDistancePx is null || double.IsFinite(dto.WalkCycleDistancePx.Value) &&
            dto.WalkCycleDistancePx.Value > 0 && dto.WalkCycleDistancePx.Value <= size.Width * 4,
            "walkCycleDistancePx는 프레임 너비의 4배 이하인 유한한 양수여야 합니다.");
        if (dto.WalkRigSpecified)
        {
            Require(dto.WalkRig is not null, "walkRig는 null일 수 없습니다.");
            var rig = dto.WalkRig!;
            Require(rig.Version == 1, "지원하지 않는 walkRig 버전입니다.");
            Require(dto.WalkCycleDistancePx is not null, "walkRig에는 walkCycleDistancePx가 필요합니다.");
            Require(rig.FootLiftPx is double lift && double.IsFinite(lift) && lift >= 0 && lift <= size.Height / 2d,
                "walkRig.footLiftPx는 프레임 높이의 절반 이하인 유한한 양수 또는 0이어야 합니다.");
            Require(rig.Side is not null && rig.Turn is not null, "walkRig에는 side와 turn이 모두 필요합니다.");
        }
        Require(dto.FrameIntervalMs > 0, "frameIntervalMs는 양수여야 합니다.");
    }

    private static void EnsureNoReparsePoints(string root, string path)
    {
        var current = root;
        RejectReparsePoint(current);
        foreach (var segment in Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            current = Path.Combine(current, segment);
            RejectReparsePoint(current);
        }
    }

    private static void RejectReparsePoint(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"캐릭터 파일에는 심볼릭 링크나 재분석 지점을 사용할 수 없습니다: '{path}'.");
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
    }
}
