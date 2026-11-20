using DesktopPet.Core;

public static class EmoteFlightTests
{
    private static readonly PetPoint Away = new(5000, 5000);
    public static IEnumerable<(string Name, Action Run)> Create()
    {
        yield return ("Airborne manual reactions preserve the complete throw trajectory and start after landing", () =>
        {
            var control = Brain(); var pet = Brain();
            Throw(control); Throw(pet);
            pet.StartEmote(PetState.Angry);
            Check(pet.State == PetState.Falling, "Manual reaction interrupted flight immediately");
            var landed = false;
            for (var i = 0; i < 240; i++)
            {
                control.Tick(1d / 60, Away); pet.Tick(1d / 60, Away);
                Check(control.X == pet.X && control.Y == pet.Y && control.HorizontalVelocityPxPerSec == pet.HorizontalVelocityPxPerSec,
                    "Manual reaction changed the physical trajectory");
                if (control.State != PetState.Falling)
                {
                    landed = true;
                    Check(pet.State == PetState.Angry && pet.StateElapsedMs < 17 && pet.Y == 510,
                        "Queued reaction did not start with its own clock on landing");
                    break;
                }
            }
            Check(landed, "Flight fixture never landed");
            Advance(pet, 90);
            Check(pet.State == PetState.Angry, "Queued reaction expired before its full duration");
            Advance(pet, 42);
            Check(!pet.State.IsEmote() && pet.RoamingPaused, "Finite reaction stuck after landing or resumed roaming");
        });
        yield return ("Manual reactions during dragging preserve grab offsets and the latest reaction wins", () =>
        {
            var pet = Brain();
            pet.BeginDrag(new PetPoint(925, 535));
            pet.StartEmote(PetState.Cry);
            Check(pet.State == PetState.Dragged, "Reaction cancelled the grab");
            pet.UpdateDrag(new PetPoint(1025, 225));
            Check(pet.X == 1000 && pet.Y == 200, "Reaction invalidated drag offsets");
            pet.StartEmote(PetState.Angry);
            pet.EndDrag(0, 0);
            Land(pet);
            Check(pet.State == PetState.Angry, "Latest manual reaction was lost or replaced by impact cry");
        });
        yield return ("Invalid reactions leave flight and its previously queued reaction intact", () =>
        {
            var pet = Brain(); Throw(pet); pet.StartEmote(PetState.Angry);
            var pose = (pet.X, pet.Y, pet.State, pet.HorizontalVelocityPxPerSec);
            try { pet.StartEmote(PetState.Idle); throw new Exception("Invalid reaction accepted"); }
            catch (ArgumentOutOfRangeException) { }
            Check(pose == (pet.X, pet.Y, pet.State, pet.HorizontalVelocityPxPerSec), "Invalid reaction changed flight");
            Land(pet);
            Check(pet.State == PetState.Angry, "Invalid request replaced the valid pending reaction");
        });
        yield return ("Persistent notification reactions take landing priority without replaying old manual reactions", () =>
        {
            var pet = Brain(); Throw(pet);
            pet.StartEmote(PetState.Cry); pet.StartNagging(PetState.Angry);
            Land(pet);
            Check(pet.State == PetState.Angry && pet.IsNagging, "Notification lost landing priority");
            pet.StopNagging();
            ShortDrop(pet); Land(pet);
            Check(!pet.State.IsEmote(), "Superseded manual reaction replayed on a later landing");
        });
        yield return ("Explicit desktop recovery clears a pending reaction before a later gentle landing", () =>
        {
            var pet = Brain(); Throw(pet); pet.StartEmote(PetState.Angry);
            pet.PlaceOnDesktop(new DesktopRect(0, 0, 2000, 600));
            Check(pet.State == PetState.Idle && pet.RoamingPaused, "Recovery retained reaction or lost session pause");
            ShortDrop(pet); Land(pet);
            Check(!pet.State.IsEmote(), "Recovery replayed an old pending reaction");
        });
    }
    private static PetBrain Brain() => new(new PlatformLayout(new[] { new Platform(0, 2000, 600) }), 100, 90, 900, 510, new Random(42))
        { SleepEnabled = false, RoamingPaused = true };
    private static void Throw(PetBrain pet)
    {
        pet.BeginDrag(new PetPoint(pet.X, pet.Y));
        pet.UpdateDrag(new PetPoint(pet.X, 200));
        pet.EndDrag(260, -180);
    }
    private static void ShortDrop(PetBrain pet)
    {
        pet.BeginDrag(new PetPoint(pet.X, pet.Y));
        pet.UpdateDrag(new PetPoint(pet.X, 480));
        pet.EndDrag(0, 0);
    }
    private static void Land(PetBrain pet)
    {
        Check(pet.State == PetState.Falling, "Fixture must begin airborne");
        for (var i = 0; i < 240 && pet.State == PetState.Falling; i++) pet.Tick(1d / 60, Away);
        Check(pet.State != PetState.Falling && pet.Y == 510, "Pet failed to complete its landing");
    }
    private static void Advance(PetBrain pet, int frames) { for (var i = 0; i < frames; i++) pet.Tick(1d / 60, Away); }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
