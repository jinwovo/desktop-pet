using DesktopPet.Interop;
using DesktopPet.WindowFall;

/// <summary>Bounds and cancellation checks without capturing or enumerating any desktop window.</summary>
public static class WindowCaptureRegressionTests
{
    public static void Run()
    {
        var passed = 0;
        var boundsCases = new (string Name, RECT Bounds, bool Accepted)[]
        {
            ("4K window stays within capture budget", new RECT { Right = 3840, Bottom = 2160 }, true),
            ("Negative monitor coordinates remain valid", new RECT { Left = -1920, Top = -1080, Right = 0, Bottom = 0 }, true),
            ("Exact 64 MiB pixel boundary is accepted", new RECT { Right = 4096, Bottom = 4096 }, true),
            ("Window above pixel budget is skipped", new RECT { Right = 4097, Bottom = 4096 }, false),
            ("Oversized native dimension is skipped", new RECT { Right = 16384, Bottom = 100 }, false),
            ("Zero width cannot allocate a bitmap", new RECT { Right = 0, Bottom = 100 }, false),
            ("Inverted native rectangle is skipped", new RECT { Left = 10, Right = 0, Bottom = 100 }, false),
            ("Extreme coordinates do not overflow dimensions", new RECT { Left = int.MinValue, Right = int.MaxValue, Bottom = 100 }, false),
        };
        foreach (var (name, bounds, expected) in boundsCases)
        {
            var accepted = WindowCaptureLimits.TryGetCaptureSize(bounds, out var width, out var height);
            Check(accepted == expected, name);
            if (accepted) Check((long)width * height * 4 <= 64L * 1024 * 1024, "Accepted capture exceeds memory budget");
            Pass(name, ref passed);
        }

        Check(WindowCaptureLimits.GetPreviewSize(3840, 2160) == (512, 288), "Preview changed aspect ratio");
        Pass("Preview preserves 16:9 aspect within 512px", ref passed);
        Check(WindowCaptureLimits.GetPreviewSize(1, 8192) == (1, 512), "Narrow preview became empty");
        Pass("Tall narrow preview retains a nonzero width", ref passed);
        Check(WindowCaptureLimits.GetPreviewSize(100, 50) == (100, 50), "Small preview was enlarged");
        Pass("Small preview is not enlarged", ref passed);
        Throws<ArgumentOutOfRangeException>(() => WindowCaptureLimits.GetPreviewSize(0, 1));
        Pass("Invalid preview dimensions are rejected", ref passed);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var cache = new WindowSnapshotCache();
        Check(cache.Refresh(new IntPtr(-1), cancelled.Token) is null, "Cancelled refresh attempted a capture");
        Check(WindowSnapshotCache.Capture(new IntPtr(-1), cancelled.Token) is null, "Cancelled static capture did not stop");
        Check(cache.TrackedHandles.Count == 0, "Cancelled refresh filled the cache");
        Pass("Cancelled capture returns before native window access", ref passed);
        Throws<ArgumentOutOfRangeException>(() => new WindowCloseWatcher(cache, TimeSpan.Zero));
        Pass("Invalid refresh interval is rejected before creating a native hook", ref passed);
        Throws<ArgumentOutOfRangeException>(() => new WindowCloseWatcher(cache, TimeSpan.FromTicks(1)));
        Throws<ArgumentOutOfRangeException>(() => new WindowCloseWatcher(cache, TimeSpan.FromTicks(TimeSpan.TicksPerMillisecond - 1)));
        Pass("Sub-millisecond refresh intervals cannot silently become one-shot timers", ref passed);
        Throws<ArgumentOutOfRangeException>(() => new WindowCloseWatcher(cache, TimeSpan.MaxValue));
        Throws<ArgumentOutOfRangeException>(() => new WindowCloseWatcher(cache, TimeSpan.FromMilliseconds(uint.MaxValue)));
        Pass("Refresh intervals above Timer capacity fail before hook creation", ref passed);
        Console.WriteLine($"{passed}/16 window capture regression checks passed (no native capture)");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Pass(string name, ref int passed)
    {
        passed++;
        Console.WriteLine($"PASS {name}");
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}");
    }
}
