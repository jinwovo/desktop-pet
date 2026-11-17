using System.Text.Json;
using DesktopPet.Core;

public static class FullscreenPolicyTests
{
    public static IEnumerable<(string Name, Action Run)> Create()
    {
        var monitor = new DesktopRect(0, 0, 1920, 1080);
        ForegroundWindowSnapshot Window(DesktopRect bounds) => new(20, true, false, false, bounds);
        yield return ("Fullscreen policy recognizes only a foreground window covering the pet's monitor", () =>
        {
            Check(FullscreenPolicy.ShouldPause(Window(monitor), monitor, 10), "Same-monitor fullscreen was missed");
            Check(!FullscreenPolicy.ShouldPause(Window(new(1920, 0, 3840, 1080)), monitor, 10), "Another monitor's fullscreen suppressed this pet");
            Check(!FullscreenPolicy.ShouldPause(Window(new(0, 0, 1920, 1040)), monitor, 10), "A maximized work-area window was treated as fullscreen");
            Check(!FullscreenPolicy.ShouldPause(Window(new(50, 50, 900, 700)), monitor, 10), "A normal window was treated as fullscreen");
        });
        yield return ("Fullscreen policy ignores own windows, shell, minimized, invisible and unavailable foregrounds", () =>
        {
            var ordinary = Window(monitor);
            foreach (var foreground in new[] { ordinary with { ProcessId = 10 }, ordinary with { ProcessId = 0 },
                ordinary with { Shell = true }, ordinary with { Minimized = true }, ordinary with { Visible = false } })
                Check(!FullscreenPolicy.ShouldPause(foreground, monitor, 10), "Ineligible window suppressed the pet");
        });
        yield return ("Fullscreen geometry supports negative monitors and bounded physical-pixel rounding", () =>
        {
            var negative = new DesktopRect(-1920, -1080, 0, 0);
            Check(FullscreenPolicy.ShouldPause(Window(new(-1918, -1078, -2, -2)), negative, 10), "Physical-pixel tolerance or negative monitor failed");
            Check(!FullscreenPolicy.ShouldPause(Window(new(-1917, -1078, -2, -2)), negative, 10), "Tolerance grew beyond two pixels");
            Check(FullscreenPolicy.ShouldPause(Window(new(-1920, -1080, 1920, 1080)), monitor, 10), "A covering multi-display window was not recognized");
        });
        yield return ("Invalid fullscreen rectangles cannot suppress the pet", () =>
        {
            foreach (var rect in new[] { new DesktopRect(0, 0, 0, 0), new(20, 20, 1, 1), new(double.NaN, 0, 1920, 1080),
                new(0, 0, double.PositiveInfinity, 1080), new(-double.MaxValue, 0, double.MaxValue, 1080) })
            {
                Check(!FullscreenPolicy.ShouldPause(Window(rect), monitor, 10), "Invalid foreground was accepted");
                Check(!FullscreenPolicy.ShouldPause(Window(monitor), rect, 10), "Invalid monitor was accepted");
            }
        });
        yield return ("Old settings default fullscreen cooperation on without changing manual hidden preference", () =>
        {
            var old = JsonSerializer.Deserialize<PetSettings>("{\"Visible\":false}")!;
            old.Normalize();
            Check(old.PauseDuringFullscreen && !old.Visible, "Upgrading settings changed manual visibility");
            old.PauseDuringFullscreen = false;
            var restored = JsonSerializer.Deserialize<PetSettings>(JsonSerializer.Serialize(old))!;
            Check(!restored.PauseDuringFullscreen && !restored.Visible, "Fullscreen opt-out or hidden preference did not round-trip");
        });
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
