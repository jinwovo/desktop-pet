using System.Text.Json.Serialization;

namespace DesktopPet.Core;

public sealed class FrameSizeDto
{
    [JsonPropertyName("width")]
    public int Width { get; set; } = 128;

    [JsonPropertyName("height")]
    public int Height { get; set; } = 128;
}

public sealed class AnchorDto
{
    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }
}

public sealed class CharacterFramesDto
{
    [JsonPropertyName("idle")]
    public List<string> Idle { get; set; } = new();

    [JsonPropertyName("walk")]
    public List<string> Walk { get; set; } = new();

    [JsonPropertyName("sit")]
    public List<string> Sit { get; set; } = new();

    [JsonPropertyName("lookLeft")]
    public List<string> LookLeft { get; set; } = new();

    [JsonPropertyName("lookRight")]
    public List<string> LookRight { get; set; } = new();

    [JsonPropertyName("drag")]
    public List<string> Drag { get; set; } = new();

    [JsonPropertyName("fall")]
    public List<string> Fall { get; set; } = new();

    [JsonPropertyName("cry")]
    public List<string> Cry { get; set; } = new();

    [JsonPropertyName("angry")]
    public List<string> Angry { get; set; } = new();
}

/// <summary>Raw deserialized contents of a character.json file.</summary>
public sealed class CharacterDefinitionDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "Unknown";

    [JsonPropertyName("scale")]
    public double Scale { get; set; } = 1.0;

    [JsonPropertyName("frameSizePx")]
    public FrameSizeDto FrameSizePx { get; set; } = new();

    [JsonPropertyName("anchor")]
    public AnchorDto Anchor { get; set; } = new();

    [JsonPropertyName("walkSpeedPxPerSec")]
    public double WalkSpeedPxPerSec { get; set; } = 60;

    [JsonPropertyName("frames")]
    public CharacterFramesDto Frames { get; set; } = new();

    [JsonPropertyName("frameIntervalMs")]
    public int FrameIntervalMs { get; set; } = 250;
}

/// <summary>
/// A loaded character with frame file paths resolved to absolute paths on disk and
/// a fallback applied for any animation state that has no frames of its own.
/// </summary>
public sealed class CharacterDefinition
{
    public required string Name { get; init; }
    public required string FolderPath { get; init; }
    public double Scale { get; init; } = 1.0;
    public int FrameWidth { get; init; } = 128;
    public int FrameHeight { get; init; } = 128;
    public int AnchorX { get; init; }
    public int AnchorY { get; init; }
    public double WalkSpeedPxPerSec { get; init; } = 60;
    public int FrameIntervalMs { get; init; } = 250;
    public required IReadOnlyDictionary<PetState, IReadOnlyList<string>> FramesByState { get; init; }
    public IReadOnlyList<string> LookLeftFrames { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> LookRightFrames { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> GetFrames(PetState state) =>
        FramesByState.TryGetValue(state, out var frames) && frames.Count > 0
            ? frames
            : FramesByState[PetState.Idle];

    /// <summary>Frames to show while looking at the cursor, directional (left/right of the pet).</summary>
    public IReadOnlyList<string> GetLookFrames(bool cursorIsOnRight)
    {
        var directional = cursorIsOnRight ? LookRightFrames : LookLeftFrames;
        return directional.Count > 0 ? directional : GetFrames(PetState.Idle);
    }

    /// <summary>
    /// Multiplier on <see cref="FrameIntervalMs"/> per state. One interval suits a walk cycle,
    /// but reusing it for the two-frame idle would make the character breathe frantically.
    /// </summary>
    private static double FrameIntervalScale(PetState state) => state switch
    {
        PetState.Idle or PetState.Sit or PetState.LookAtCursor => 4.5,
        PetState.Cry => 2.2,
        PetState.Angry => 1.6,
        _ => 1.0,
    };

    /// <summary>Picks the frame file for a looping animation, given how long the state has been active.</summary>
    public string GetFramePath(PetState state, IReadOnlyList<string> frames, double stateElapsedMs)
    {
        if (frames.Count == 0)
        {
            frames = GetFrames(PetState.Idle);
        }

        var interval = FrameIntervalMs * FrameIntervalScale(state);
        var index = interval > 0
            ? (int)(stateElapsedMs / interval) % frames.Count
            : 0;
        return frames[index];
    }

    /// <summary>
    /// Every distinct frame path this character can show. Decoding a PNG the first time it's used
    /// mid-animation causes a visible stutter, so the UI layer should load all of these up front.
    /// </summary>
    public IEnumerable<string> AllFramePaths() =>
        FramesByState.Values.SelectMany(f => f)
            .Concat(LookLeftFrames)
            .Concat(LookRightFrames)
            .Distinct();
}
