using System.Text.Json.Serialization;

namespace DesktopPet.Core;

/// <summary>Validated, immutable multipliers for autonomous choices; never modifies gait or physics.</summary>
public sealed class CharacterBehaviorProfile
{
    public static CharacterBehaviorProfile Default { get; } = new();
    [JsonPropertyName("walkWeight")] public double WalkWeight { get; }
    [JsonPropertyName("idleWeight")] public double IdleWeight { get; }
    [JsonPropertyName("sitWeight")] public double SitWeight { get; }
    [JsonPropertyName("restDurationMultiplier")] public double RestDurationMultiplier { get; }
    [JsonPropertyName("walkDurationMultiplier")] public double WalkDurationMultiplier { get; }
    [JsonPropertyName("sleepAfterIdleMs")] public int SleepAfterIdleMs { get; }
    [JsonPropertyName("description")] public string Description { get; }

    [JsonConstructor]
    public CharacterBehaviorProfile(double walkWeight = 1, double idleWeight = 1, double sitWeight = 1,
        double restDurationMultiplier = 1, double walkDurationMultiplier = 1, int sleepAfterIdleMs = 90_000, string? description = null)
    {
        static bool Weight(double value) => double.IsFinite(value) && value >= 0 && value <= 10;
        static bool Duration(double value) => double.IsFinite(value) && value >= 0.25 && value <= 4;
        if (!Weight(walkWeight) || !Weight(idleWeight) || !Weight(sitWeight) || walkWeight + idleWeight + sitWeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(walkWeight), "Behavior weights must be finite, between 0 and 10, and have a positive sum.");
        if (!Duration(restDurationMultiplier) || !Duration(walkDurationMultiplier))
            throw new ArgumentOutOfRangeException(nameof(restDurationMultiplier), "Behavior duration multipliers must be between 0.25 and 4.");
        if (sleepAfterIdleMs is < 15_000 or > 1_800_000)
            throw new ArgumentOutOfRangeException(nameof(sleepAfterIdleMs));
        description = description?.Trim() ?? "";
        if (description.Length > 80 || description.Any(char.IsControl)) throw new ArgumentException("Behavior description must be a short single line.", nameof(description));
        WalkWeight = walkWeight; IdleWeight = idleWeight; SitWeight = sitWeight;
        RestDurationMultiplier = restDurationMultiplier; WalkDurationMultiplier = walkDurationMultiplier;
        SleepAfterIdleMs = sleepAfterIdleMs;
        Description = description;
    }
}
