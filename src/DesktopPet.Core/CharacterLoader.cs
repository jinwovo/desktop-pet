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
        var jsonPath = Path.Combine(characterFolder, "character.json");
        var json = File.ReadAllText(jsonPath);
        var dto = JsonSerializer.Deserialize<CharacterDefinitionDto>(json, JsonOptions)
            ?? throw new InvalidDataException($"'{jsonPath}'을(를) 읽을 수 없습니다.");

        var framesByState = new Dictionary<PetState, IReadOnlyList<string>>
        {
            [PetState.Idle] = ResolveExisting(characterFolder, dto.Frames.Idle),
            [PetState.Walk] = ResolveExisting(characterFolder, dto.Frames.Walk),
            [PetState.Sit] = ResolveExisting(characterFolder, dto.Frames.Sit),
            [PetState.Dragged] = ResolveExisting(characterFolder, dto.Frames.Drag),
            [PetState.Falling] = ResolveExisting(characterFolder, dto.Frames.Fall),
            [PetState.Cry] = ResolveExisting(characterFolder, dto.Frames.Cry),
            [PetState.Angry] = ResolveExisting(characterFolder, dto.Frames.Angry),
        };

        // LookAtCursor is directional (left/right of the pet); UI picks the right list at render time.
        var lookLeft = ResolveExisting(characterFolder, dto.Frames.LookLeft);
        var lookRight = ResolveExisting(characterFolder, dto.Frames.LookRight);
        framesByState[PetState.LookAtCursor] = lookLeft.Count > 0 ? lookLeft : framesByState[PetState.Idle];

        // idle must always have at least one frame; if missing, fall back to any other available frame.
        if (framesByState[PetState.Idle].Count == 0)
        {
            var anyFrame = framesByState.Values.FirstOrDefault(f => f.Count > 0);
            if (anyFrame is not null)
            {
                framesByState[PetState.Idle] = anyFrame;
            }
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
            FrameIntervalMs = dto.FrameIntervalMs,
            FramesByState = framesByState,
            LookLeftFrames = lookLeft,
            LookRightFrames = lookRight,
        };
    }

    /// <summary>Exposed so the UI layer can fetch the right-looking frame set without re-reading JSON.</summary>
    public static IReadOnlyList<string> ResolveExisting(string characterFolder, IEnumerable<string> fileNames) =>
        fileNames
            .Select(f => Path.Combine(characterFolder, f))
            .Where(File.Exists)
            .ToList();
}
