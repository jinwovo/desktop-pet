using DesktopPet.Core;

public static class FramePacerTests
{
    public static IEnumerable<(string Name, Action Run)> Create()
    {
        yield return ("Frame caps retain their target rate on 60, 120, 144 and 165 Hz displays", () =>
        {
            foreach (var hz in new[] { 60, 120, 144, 165 })
            foreach (var cap in new[] { 30, 60, 120 })
            {
                var pacer = new FramePacer { MaximumFps = cap };
                var count = 0;
                for (var frame = 0; frame < hz * 10; frame++) if (pacer.ShouldRender(frame * 1000d / hz)) count++;
                Check(Math.Abs(count - Math.Min(hz, cap) * 10) <= 1, $"{cap} cap at {hz} Hz accepted {count} frames");
            }
        });
        yield return ("Frame pacing skips duplicates, handles resume/clock reset and rejects poisoned timestamps", () =>
        {
            var pacer = new FramePacer();
            Check(pacer.ShouldRender(0) && !pacer.ShouldRender(0) && !pacer.ShouldRender(1), "Duplicate or early rendering accepted");
            Check(pacer.ShouldRender(100000) && !pacer.ShouldRender(100000), "Resume caught up with a burst");
            Check(pacer.ShouldRender(5), "Clock reset never rendered again");
            foreach (var bad in new[] { double.NaN, double.PositiveInfinity, -1, double.MaxValue })
                Check(!pacer.ShouldRender(bad), "Invalid timestamp reached rendering");
            pacer.MaximumFps = 30;
            Check(pacer.ShouldRender(5) && !pacer.ShouldRender(10) && pacer.ShouldRender(40), "New frame cap failed to reset its phase");
            try { pacer.MaximumFps = 1; throw new Exception("Invalid cap accepted"); } catch (ArgumentOutOfRangeException) { }
        });
        yield return ("Legacy preferences keep full motion with a bounded default frame cap", () =>
        {
            var settings = System.Text.Json.JsonSerializer.Deserialize<PetSettings>("{}")!;
            Check(!settings.ReducedMotion && settings.MaximumFps == 60, "Legacy defaults changed accessibility mode");
            settings.MaximumFps = int.MaxValue; settings.Normalize();
            Check(settings.MaximumFps == 60, "Unsafe frame cap persisted");
        });
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
