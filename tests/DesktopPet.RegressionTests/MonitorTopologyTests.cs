using DesktopPet.Core;

public static class MonitorTopologyTests
{
    public static IEnumerable<(string Name, Action Run)> Create()
    {
        yield return ("XY topology selects stacked monitors independently of enumeration order", () =>
        {
            foreach (var areas in new[] { Stacked(), Stacked().Reverse().ToArray() })
            {
                var layout = PlatformLayout.FromWorkAreas(areas);
                Check(layout.GetMonitorFloorAt(164, 500).GroundY == 1080 && layout.GetMonitorFloorAt(164, 1500).GroundY == 2160,
                    "Stacked monitor was selected by X or enumeration order alone");
            }
        });
        yield return ("A lower-monitor drop no longer teleports upward onto the upper desktop", () =>
        {
            var brain = Brain(PlatformLayout.FromWorkAreas(Stacked()), 100, 1500);
            brain.BeginDrag(new(100, brain.Y)); brain.EndDrag();
            brain.Tick(1d / 60, Away());
            Check(brain.Y + 122 > 1500 && brain.Y + 122 < 2160 && brain.State == PetState.Falling, "Original lower-monitor teleport reproduced");
            Land(brain);
            Near(brain.Y + 122, 2160, "Drop did not land on the lower monitor");
        });
        yield return ("An upper-monitor floor remains stable at the shared stacked boundary", () =>
        {
            var brain = Brain(PlatformLayout.FromWorkAreas(Stacked()), 100, 1080);
            brain.BeginDrag(new(100, brain.Y)); brain.EndDrag(); brain.Tick(1d / 120, Away());
            Near(brain.Y + 122, 1080, "A sole on the upper boundary fell onto the lower monitor");
        });
        yield return ("A throw turning inside the taskbar gap cannot land on a floor it never crossed", () =>
        {
            var layout = PlatformLayout.FromWorkAreas(new[] { new DesktopRect(0, 0, 1920, 1040), new(0, 1080, 1920, 2160) });
            Check(layout.GetMonitorFloorAt(164, 1060).GroundY == 2160, "Gap selected an already-above floor");
            var brain = Brain(layout, 100, 1085);
            brain.BeginDrag(new(100, brain.Y)); brain.EndDrag(0, -300);
            var minimumFeet = 1085d;
            for (var i = 0; i < 600 && brain.State == PetState.Falling; i++)
            {
                brain.Tick(1d / 120, Away()); minimumFeet = Math.Min(minimumFeet, brain.Y + 122);
            }
            Check(minimumFeet > 1040, "Test throw crossed the upper floor or snapped upward to it");
            Near(brain.Y + 122, 2160, "Taskbar-gap throw landed on a phantom upper floor");
        });
        yield return ("Negative and vertically offset monitor rectangles retain their own floors", () =>
        {
            var layout = PlatformLayout.FromWorkAreas(new[] { new DesktopRect(-1600, -900, 0, 0), new(0, 200, 1920, 1280) });
            Check(layout.GetMonitorFloorAt(-800, -500).GroundY == 0 && layout.GetMonitorFloorAt(500, 500).GroundY == 1280,
                "Negative or offset rectangle selection failed");
            Check(layout.GetMonitorFloorAt(500, 100).GroundY == 1280, "Offset gap missed the next reachable floor");
            var brain = Brain(layout, -1500, -400);
            brain.BeginDrag(new(-1500, brain.Y)); brain.EndDrag(); Land(brain);
            Near(brain.Y + 122, 0, "Negative-monitor drop used a different monitor");
        });
        yield return ("Monitor removal recovers a pet onto the nearest remaining desktop", () =>
        {
            var layout = PlatformLayout.FromWorkAreas(Stacked());
            var brain = Brain(layout, 100, 1500);
            brain.BeginDrag(new(100, brain.Y)); brain.EndDrag();
            layout.UpdateWorkAreas(new[] { Stacked()[0] }); brain.Tick(1d / 60, Away());
            Near(brain.Y + 122, 1080, "Pet remained below all desktops after monitor removal");
            Check(brain.X >= 0 && brain.X + 128 <= 1920, "Recovery left the pet outside the retained monitor");
        });
        yield return ("Topology updates reject invalid rectangles and preserve stable copied geometry", () =>
        {
            var input = Stacked();
            var layout = PlatformLayout.FromWorkAreas(input);
            input[1] = new(0, 0, 1, 1);
            Check(layout.GetMonitorFloorAt(164, 1500).GroundY == 2160, "Caller mutation changed published topology");
            layout.UpdateWorkAreas(Array.Empty<DesktopRect>());
            foreach (var invalid in new[] { new DesktopRect(0, 0, 0, 10), new(0, double.NaN, 100, 100), new(0, 0, double.PositiveInfinity, 100) })
            {
                try { layout.UpdateWorkAreas(new[] { invalid }); throw new Exception("Invalid rectangle accepted"); }
                catch (ArgumentException) { }
            }
            Check(layout.GetMonitorFloorAt(164, 1500).GroundY == 2160, "Rejected/empty update destroyed the current topology");
        });
        yield return ("Floor-only layouts preserve legacy horizontal behavior while XY layouts validate points", () =>
        {
            var legacy = new PlatformLayout(new[] { new Platform(-1000, 0, 600), new Platform(0, 1920, 1080) });
            Check(legacy.GetMonitorFloorAt(-500, 50000).GroundY == 600 && legacy.GetMonitorFloorAt(100, -50000).GroundY == 1080,
                "Legacy floor-only behavior changed");
            var modern = PlatformLayout.FromWorkAreas(Stacked());
            try { modern.GetMonitorFloorAt(double.NaN, 20); throw new Exception("Invalid point accepted"); }
            catch (ArgumentOutOfRangeException) { }
        });
    }
    private static DesktopRect[] Stacked() => new[] { new DesktopRect(0, 0, 1920, 1080), new(0, 1080, 1920, 2160) };
    private static PetBrain Brain(PlatformLayout layout, double x, double feet) => new(layout, 128, 122, x, feet - 122, new Random(1));
    private static PetPoint Away() => new(-10000, -10000);
    private static void Land(PetBrain brain)
    {
        for (var i = 0; i < 600 && brain.State == PetState.Falling; i++) brain.Tick(1d / 120, Away());
        Check(brain.State != PetState.Falling, "Drop failed to land");
    }
    private static void Near(double value, double expected, string message) => Check(Math.Abs(value - expected) < 1e-6, message);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
