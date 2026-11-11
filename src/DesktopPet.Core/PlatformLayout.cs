namespace DesktopPet.Core;

/// <summary>
/// Every surface the pet can stand on: monitor floors (desktop work areas, essentially fixed) plus
/// the live top edges of other visible windows (refreshed frequently by the UI layer, since windows
/// move/open/close constantly). Reads and the occasional writer (a background poller) can run on
/// different threads — each list is swapped in as a whole, so no lock is needed for readers.
/// </summary>
public sealed class PlatformLayout
{
    private volatile IReadOnlyList<Platform> _monitorFloors;
    private volatile IReadOnlyList<Platform> _windowPlatforms = Array.Empty<Platform>();

    public PlatformLayout(IReadOnlyList<Platform> monitorFloors)
    {
        _monitorFloors = monitorFloors.Count > 0 ? monitorFloors : new[] { new Platform(0, 1920, 1080) };
    }

    public void UpdateMonitors(IReadOnlyList<Platform> monitorFloors)
    {
        if (monitorFloors.Count > 0)
        {
            _monitorFloors = monitorFloors;
        }
    }

    /// <summary>Replaces the live set of window-top platforms. Monitor floors are untouched.</summary>
    public void UpdateWindowPlatforms(IReadOnlyList<Platform> windowPlatforms) => _windowPlatforms = windowPlatforms;

    private IEnumerable<Platform> AllPlatforms => _windowPlatforms.Concat(_monitorFloors);

    /// <summary>The monitor floor at x — used as the ultimate fallback and to bound X while falling.</summary>
    public Platform GetMonitorFloorAt(double x)
    {
        foreach (var m in _monitorFloors)
        {
            if (x >= m.Left && x < m.Right)
            {
                return m;
            }
        }

        return _monitorFloors
            .OrderBy(m => DistanceToRange(x, m.Left, m.Right))
            .First();
    }

    /// <summary>
    /// While falling from <paramref name="yOld"/> to <paramref name="yNew"/> this tick: the
    /// highest (smallest GroundY) platform at <paramref name="x"/> that was crossed during the
    /// step, i.e. the first solid surface hit — or null if nothing was crossed yet (keep falling).
    /// </summary>
    public Platform? FindLandingPlatform(double x, double yOld, double yNew)
    {
        Platform? best = null;
        foreach (var p in AllPlatforms)
        {
            if (x < p.Left || x >= p.Right)
            {
                continue;
            }

            if (p.GroundY < yOld - 0.5 || p.GroundY > yNew + 0.5)
            {
                continue; // not reached yet, or already behind/above us
            }

            if (best is null || p.GroundY < best.Value.GroundY)
            {
                best = p;
            }
        }

        return best;
    }

    /// <summary>
    /// While walking/idling at <paramref name="y"/>: is there still a platform holding the pet up
    /// at <paramref name="x"/>? Null means whatever it was standing on moved/closed — time to fall.
    /// </summary>
    public Platform? GetSupportPlatform(double x, double y)
    {
        Platform? best = null;
        foreach (var p in AllPlatforms)
        {
            if (x < p.Left || x >= p.Right)
            {
                continue;
            }

            if (Math.Abs(p.GroundY - y) > 2.0)
            {
                continue;
            }

            if (best is null || p.GroundY < best.Value.GroundY)
            {
                best = p;
            }
        }

        return best;
    }

    private static double DistanceToRange(double x, double left, double right) =>
        x < left ? left - x : x > right ? x - right : 0;
}
