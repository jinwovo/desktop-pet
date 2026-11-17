using DesktopPet.Core;

public static class PetRecoveryTests
{
    public static IEnumerable<(string Name, Action Run)> Create()
    {
        yield return ("Explicit monitor recovery clears a throw and lands on negative or stacked desktop floors", () =>
        {
            foreach (var area in new[] { new DesktopRect(-1280, 0, 0, 1024), new DesktopRect(0, 1080, 1920, 2160) })
            {
                var brain = new PetBrain(PlatformLayout.FromWorkAreas(new[] { area }), 128, 122, 300, 100);
                brain.BeginDrag(new PetPoint(300, 100)); brain.EndDrag(900, -900);
                brain.PlaceOnDesktop(area);
                Check(brain.State == PetState.Idle && brain.X == area.Left + (area.Width - 128) / 2 && brain.Y + 122 == area.Bottom &&
                    brain.HorizontalVelocityPxPerSec == 0 && !brain.IsNagging, "Recovery retained old momentum or used the wrong floor");
                var position = (brain.X, brain.Y);
                brain.Tick(0.1, new PetPoint(-100000, -100000));
                Check(position == (brain.X, brain.Y), "Recovered pet slid or fell on the next tick");
            }
        });
        yield return ("Recovery rejects invalid monitor geometry before changing pose and clears persistent nagging", () =>
        {
            var area = new DesktopRect(0, 0, 1920, 1080);
            var brain = new PetBrain(PlatformLayout.FromWorkAreas(new[] { area }), 128, 122, 500, 958);
            brain.StartNagging(PetState.Cry);
            var pose = (brain.X, brain.Y, brain.State);
            foreach (var bad in new[] { new DesktopRect(0, 0, 0, 0), new DesktopRect(double.NaN, 0, 100, 100), new DesktopRect(20, 0, 10, 100) })
            {
                try { brain.PlaceOnDesktop(bad); throw new Exception("Invalid recovery geometry accepted"); } catch (ArgumentOutOfRangeException) { }
                Check(pose == (brain.X, brain.Y, brain.State) && brain.IsNagging, "Invalid recovery changed presentation");
            }
            brain.PlaceOnDesktop(area);
            Check(!brain.IsNagging && brain.NagHopOffset == 0 && brain.State == PetState.Idle, "Recovery retained nagging bounce");
        });
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
