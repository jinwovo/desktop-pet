using System.Text.Json.Serialization;

namespace DesktopPet.Core;

public sealed class WalkRigViewDto
{
    [JsonPropertyName("body")]
    public string? Body { get; set; }
    [JsonPropertyName("nearFoot")]
    public string? NearFoot { get; set; }
    [JsonPropertyName("farFoot")]
    public string? FarFoot { get; set; }
}

public sealed class WalkRigDto
{
    [JsonPropertyName("version")]
    public int Version { get; set; }
    [JsonPropertyName("footLiftPx")]
    public double? FootLiftPx { get; set; }
    [JsonPropertyName("side")]
    public WalkRigViewDto? Side { get; set; }
    [JsonPropertyName("turn")]
    public WalkRigViewDto? Turn { get; set; }
}

/// <summary>Full-canvas neutral layers. Feet start centered on the character's ground anchor.</summary>
public sealed record WalkRigViewDefinition(string BodyPath, string NearFootPath, string FarFootPath)
{
    public IEnumerable<string> AllFramePaths()
    {
        yield return BodyPath;
        yield return NearFootPath;
        yield return FarFootPath;
    }
}

public sealed record WalkRigDefinition(int Version, double FootLiftPx, WalkRigViewDefinition Side, WalkRigViewDefinition Turn)
{
    public WalkRigViewDefinition GetView(WalkView view) => view == WalkView.Turn ? Turn : Side;
    public IEnumerable<string> AllFramePaths() => Side.AllFramePaths().Concat(Turn.AllFramePaths());
}
