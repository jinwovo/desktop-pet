using DesktopPet.Core;

public static class InputRegressionTests
{
    public static IEnumerable<(string Name, Action Run)> Create()
    {
        yield return ("Throw velocity is consistent across mouse sample frequencies", () =>
        {
            var results = new List<PetPoint>();
            foreach (var hz in new[] { 60, 125, 250 })
            {
                var tracker = new DragVelocityTracker();
                tracker.Reset(new PetPoint(0, 0), 0);
                for (var i = 1; i <= hz; i++) tracker.Sample(new PetPoint(500.0 * i / hz, -200.0 * i / hz), (double)i / hz);
                results.Add(tracker.Velocity);
            }
            Check(results.All(v => Math.Abs(v.X - 500) < 0.1 && Math.Abs(v.Y + 200) < 0.1), "Mouse report frequency changed throw velocity");
        });
        yield return ("Holding before release cancels residual throw velocity", () =>
        {
            var tracker = new DragVelocityTracker();
            tracker.Reset(default, 1);
            tracker.Sample(new PetPoint(40, 0), 1.02);
            Check(tracker.Velocity.X > 100, "Flick velocity missing");
            tracker.Sample(new PetPoint(40, 0), 1.3);
            Check(tracker.Velocity == default, "Stationary release still throws");
        });
        yield return ("Throw filter rejects invalid samples and bounds speed", () =>
        {
            var tracker = new DragVelocityTracker();
            tracker.Reset(default, 1);
            tracker.Sample(new PetPoint(double.NaN, 1), 1.1);
            tracker.Sample(new PetPoint(1, 1), double.PositiveInfinity);
            Check(tracker.Velocity == default, "Invalid sample poisoned velocity");
            tracker.Sample(new PetPoint(1e9, -1e9), 1.2);
            Check(Math.Abs(tracker.Velocity.X) <= 1700 && Math.Abs(tracker.Velocity.Y) <= 1700, "Unbounded throw");
            tracker.Sample(default, 0.5);
            Check(tracker.Velocity == default, "Backwards timestamp retained momentum");
            tracker.Reset(new PetPoint(double.NaN, 0), 0);
            tracker.Sample(default, 1);
            Check(tracker.Velocity == default, "Invalid reset poisoned later samples");
        });
        yield return ("Slow releases drop consistently in desktop and motion preview", () =>
        {
            var tracker = new DragVelocityTracker();
            tracker.Reset(default, 0);
            tracker.Sample(new PetPoint(1, 0), 0.1);
            Check(tracker.Velocity.X > 0 && tracker.ReleaseVelocity == default, "Slow release retained throw momentum");
            tracker.Sample(new PetPoint(51, 0), 0.2);
            Check(tracker.ReleaseVelocity.X > 90, "Flick was treated as a simple drop");
        });
    }

    private static void Check(bool valid, string message) { if (!valid) throw new Exception(message); }
}
