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

    [JsonPropertyName("walkFront")]
    public List<string> WalkFront { get; set; } = new();

    [JsonPropertyName("walkTurn")]
    public List<string> WalkTurn { get; set; } = new();

    [JsonPropertyName("walkSettleSide")]
    public List<string> WalkSettleSide { get; set; } = new();

    [JsonPropertyName("walkSettleTurn")]
    public List<string> WalkSettleTurn { get; set; } = new();

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

    [JsonPropertyName("sleep")]
    public List<string> Sleep { get; set; } = new();
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

    [JsonPropertyName("walkCycleDistancePx")]
    public double? WalkCycleDistancePx { get; set; }

    [JsonPropertyName("frames")]
    public CharacterFramesDto Frames { get; set; } = new();

    [JsonPropertyName("frameIntervalMs")]
    public int FrameIntervalMs { get; set; } = 250;

    [JsonPropertyName("frameDurationsMs")]
    public Dictionary<string, List<double>?>? FrameDurationsMs { get; set; }

    [JsonPropertyName("frontFacingRest")]
    public bool FrontFacingRest { get; set; }

    private WalkRigDto? _walkRig;
    [JsonPropertyName("walkRig")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WalkRigDto? WalkRig
    {
        get => _walkRig;
        set { _walkRig = value; WalkRigSpecified = true; }
    }
    [JsonIgnore]
    public bool WalkRigSpecified { get; private set; }
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
    /// <summary>One complete gait cycle's travel in unscaled sprite pixels; null keeps legacy timing.</summary>
    public double? WalkCycleDistancePx { get; init; }
    public WalkRigDefinition? WalkRig { get; init; }
    public int FrameIntervalMs { get; init; } = 250;
    /// <summary>Keep explicitly frontal resting artwork unmirrored after either walking direction.</summary>
    public bool FrontFacingRest { get; init; }
    public required IReadOnlyDictionary<PetState, IReadOnlyList<string>> FramesByState { get; init; }
    public IReadOnlyList<string> LookLeftFrames { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> LookRightFrames { get; init; } = Array.Empty<string>();
    /// <summary>Optional matching gait cycles: front is unmirrored; turn and walk face right.</summary>
    public IReadOnlyList<string> WalkFrontFrames { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> WalkTurnFrames { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> WalkSettleSideFrames { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> WalkSettleTurnFrames { get; init; } = Array.Empty<string>();
    public bool HasDirectionalWalk => WalkRig is not null || WalkFrontFrames.Count > 0 || WalkTurnFrames.Count > 0;
    /// <summary>Optional absolute milliseconds per frame. Unspecified states keep the legacy interval scaling.</summary>
    public IReadOnlyDictionary<PetState, IReadOnlyList<double>> FrameDurationsByState { get; init; } =
        new Dictionary<PetState, IReadOnlyList<double>>();

    public IReadOnlyList<string> GetFrames(PetState state)
    {
        if (FramesByState.TryGetValue(state, out var frames) && frames.Count > 0) return frames;
        if (FramesByState.TryGetValue(PetState.Idle, out var idle) && idle.Count > 0) return idle;
        throw new InvalidDataException($"캐릭터 '{Name}'에 사용할 수 있는 기본 프레임이 없습니다.");
    }

    /// <summary>Frames to show while looking at the cursor, directional (left/right of the pet).</summary>
    public IReadOnlyList<string> GetLookFrames(bool cursorIsOnRight)
    {
        var directional = cursorIsOnRight ? LookRightFrames : LookLeftFrames;
        return directional.Count > 0 ? directional : GetFrames(PetState.Idle);
    }

    /// <summary>All views share the same gait phase; older characters retain their original walk.</summary>
    public IReadOnlyList<string> GetWalkFrames(WalkView view)
    {
        IReadOnlyList<string> directional = view switch
        {
            WalkView.Front => WalkFrontFrames.Count > 0 ? WalkFrontFrames : WalkTurnFrames,
            WalkView.Turn => WalkTurnFrames,
            _ => Array.Empty<string>(),
        };
        return directional.Count > 0 ? directional : GetFrames(PetState.Walk);
    }

    public bool UsesFrontalWalkFrames(WalkView view) => WalkFrontFrames.Count > 0 && view == WalkView.Front;

    /// <summary>Sample a distance-driven gait. Optional settle images are half-lowered/full-lowered levels.</summary>
    public string GetWalkFramePath(WalkView view, double phase, double settleProgress = 0)
    {
        var frames = GetWalkFrames(view);
        var index = Math.Min(frames.Count - 1, (int)Math.Floor(GroundedGait.NormalizePhase(phase) * frames.Count));
        var settle = view == WalkView.Turn
            ? WalkTurnFrames.Count == 0 && WalkSettleTurnFrames.Count == 0 ? WalkSettleSideFrames : WalkSettleTurnFrames
            : view == WalkView.Side ? WalkSettleSideFrames : Array.Empty<string>();
        if (double.IsFinite(settleProgress) && settleProgress > 0 && settle.Count == frames.Count * 2)
        {
            var level = (int)Math.Ceiling(Math.Clamp(settleProgress, 0, 1) * 2) - 1;
            return settle[level * frames.Count + index];
        }
        return frames[index];
    }

    public bool UsesFrontalRestFrames(PetState state) => FrontFacingRest &&
        state is PetState.Idle or PetState.Sit or PetState.Cry or PetState.Angry or PetState.Sleep;

    /// <summary>
    /// Multiplier on <see cref="FrameIntervalMs"/> per state. One interval suits a walk cycle,
    /// but reusing it for the two-frame idle would make the character breathe frantically.
    /// </summary>
    private static double FrameIntervalScale(PetState state) => state switch
    {
        PetState.Sleep => 9.0, // slow breathing; anything faster looks like panting
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

        if (FrameDurationsByState.TryGetValue(state, out var durations) && durations.Count == frames.Count)
        {
            var total = 0d;
            foreach (var duration in durations) total += duration;
            var position = double.IsFinite(stateElapsedMs) && stateElapsedMs >= 0 ? stateElapsedMs % total : 0;
            var boundary = 0d;
            for (var i = 0; i < durations.Count; i++)
            {
                boundary += durations[i];
                if (position < boundary) return frames[i];
            }
            return frames[^1];
        }

        var interval = FrameIntervalMs * FrameIntervalScale(state);
        // Modulo before converting to int avoids overflow during very long-lived notifications.
        var index = interval > 0 && double.IsFinite(stateElapsedMs) && stateElapsedMs >= 0
            ? (int)(Math.Floor(stateElapsedMs / interval) % frames.Count)
            : 0;
        return frames[index];
    }

    /// <summary>
    /// Every referenced image, including raster fallback cycles and optional rig parts. Import
    /// validates the complete set; runtime decoding can use GetPresentationFramePaths instead.
    /// </summary>
    public IEnumerable<string> AllFramePaths() =>
        FramesByState.Values.SelectMany(f => f)
            .Concat(LookLeftFrames)
            .Concat(LookRightFrames)
            .Concat(WalkFrontFrames)
            .Concat(WalkTurnFrames)
            .Concat(WalkSettleSideFrames)
            .Concat(WalkSettleTurnFrames)
            .Concat(WalkRig?.AllFramePaths() ?? Enumerable.Empty<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A live rig renders the walk from six layers. Keep raster gait frames available to full
    /// validation/audits, while retaining any walk-file aliases also used by resting states.
    /// </summary>
    public IEnumerable<string> GetPresentationFramePaths(bool includeRasterGait = false) =>
        includeRasterGait || WalkRig is null ? AllFramePaths() :
        FramesByState.Where(entry => entry.Key != PetState.Walk).SelectMany(entry => entry.Value)
            .Concat(LookLeftFrames).Concat(LookRightFrames).Concat(WalkRig.AllFramePaths())
            .Distinct(StringComparer.OrdinalIgnoreCase);
}
