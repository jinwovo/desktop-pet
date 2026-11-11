namespace DesktopPet.Core;

public enum PetState
{
    Idle,
    Walk,
    Sit,
    LookAtCursor,
    Dragged,
    Falling,

    /// <summary>Reaction to a rough landing — plays for a while, then settles back to Idle.</summary>
    Cry,

    /// <summary>Reaction to being poked repeatedly — same idea, different mood.</summary>
    Angry,

    /// <summary>Dozed off after a long stretch of being left alone. Any interaction wakes it.</summary>
    Sleep,
}

public static class PetStateExtensions
{
    /// <summary>
    /// Emotes hold the pet in place and suppress the normal Idle/Walk/Sit wandering until they
    /// time out, so a crying pet doesn't wander off mid-sob.
    /// </summary>
    public static bool IsEmote(this PetState state) => state is PetState.Cry or PetState.Angry;
}
