namespace DesktopPet.Core;

/// <summary>
/// Every surface the pet can stand on: monitor floors (desktop work areas, essentially fixed) plus
/// the live top edges of other visible windows (refreshed frequently by the UI layer, since windows
/// move/open/close constantly). Reads and the occasional writer (a background poller) can run on
/// different threads — each list is swapped in as a whole, so no lock is needed for readers.
/// </summary>
public sealed class PlatformLayout
{
    private sealed record MonitorTopology(IReadOnlyList<Platform> Floors, IReadOnlyList<DesktopRect> Areas);
    private volatile MonitorTopology _monitors;
    private volatile IReadOnlyList<Platform> _windowPlatforms = Array.Empty<Platform>();

    public PlatformLayout(IReadOnlyList<Platform> monitorFloors)
    {
        ArgumentNullException.ThrowIfNull(monitorFloors);
        _monitors = LegacyTopology(monitorFloors);
    }

    private static MonitorTopology LegacyTopology(IReadOnlyList<Platform> floors) => new(
        floors.Count > 0 ? floors.ToArray() : new[] { new Platform(0, 1920, 1080) }, Array.Empty<DesktopRect>());

    public static PlatformLayout FromWorkAreas(IReadOnlyList<DesktopRect> workAreas)
    {
        var layout = new PlatformLayout(Array.Empty<Platform>());
        layout.UpdateWorkAreas(workAreas);
        return layout;
    }

    /// <summary>Atomically replace geometry and floors; retain the last layout on an empty OS report.</summary>
    public void UpdateWorkAreas(IReadOnlyList<DesktopRect> workAreas)
    {
        ArgumentNullException.ThrowIfNull(workAreas);
        if (workAreas.Count == 0) return;
        var areas = workAreas.ToArray();
        if (areas.Any(area => !area.IsValid)) throw new ArgumentException("Monitor work areas must be finite positive rectangles.", nameof(workAreas));
        _monitors = new(areas.Select(area => new Platform(area.Left, area.Right, area.Bottom)).ToArray(), areas);
    }

    public void UpdateMonitors(IReadOnlyList<Platform> monitorFloors)
    {
        if (monitorFloors.Count > 0)
        {
            _monitors = LegacyTopology(monitorFloors);
        }
    }

    /// <summary>Replaces the live set of window-top platforms. Monitor floors are untouched.</summary>
    public void UpdateWindowPlatforms(IReadOnlyList<Platform> windowPlatforms) => _windowPlatforms = windowPlatforms;

    private IEnumerable<Platform> AllPlatforms => _windowPlatforms.Concat(_monitors.Floors);

    /// <summary>
    /// Nearby surfaces to consider for a hop. This is a geometric shortlist; the brain still
    /// checks flight time, air drag, ledge clearance and intermediate landings before launch.
    /// </summary>
    public IReadOnlyList<Platform> GetHopCandidates(
        double fromX, double direction, double currentGroundY, double maxRise, double maxDrop,
        double maxGap, double petWidth)
    {
        return AllPlatforms
            .Where(p =>
            {
                var deltaY = p.GroundY - currentGroundY;
                var inHeightRange = deltaY < -6 && -deltaY <= maxRise || deltaY > 4 && deltaY <= maxDrop;
                if (!inHeightRange || p.Right - p.Left < petWidth * 0.8) return false;
                var nearGap = direction > 0 ? p.Left - fromX : fromX - p.Right;
                var extendsForward = direction > 0 ? p.Right > fromX + 2 : p.Left < fromX - 2;
                return nearGap <= maxGap && extendsForward;
            })
            .Distinct()
            .OrderBy(p => Math.Abs(p.GroundY - currentGroundY))
            .ToArray();
    }

    /// <summary>The monitor floor at x — used as the ultimate fallback and to bound X while falling.</summary>
    public Platform GetMonitorFloorAt(double x)
    {
        return FloorAtX(_monitors.Floors, x);
    }

    private static Platform FloorAtX(IReadOnlyList<Platform> floors, double x)
    {
        foreach (var m in floors)
        {
            if (x >= m.Left && x < m.Right)
            {
                return m;
            }
        }

        return floors
            .OrderBy(m => DistanceToRange(x, m.Left, m.Right))
            .First();
    }

    /// <summary>
    /// Select by desktop X/Y. For airborne feet, callers sample just above the sole so a pet
    /// resting on a stacked monitor's bottom edge stays on that monitor. In a taskbar/gap,
    /// prefer an X-aligned floor still below the point; only recover upward if none remains.
    /// Legacy floor-only layouts retain their original X-only behavior.
    /// </summary>
    public Platform GetMonitorFloorAt(double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y)) throw new ArgumentOutOfRangeException(nameof(x));
        var topology = _monitors;
        if (topology.Areas.Count == 0) return FloorAtX(topology.Floors, x);
        for (var i = 0; i < topology.Areas.Count; i++)
            if (topology.Areas[i].Contains(x, y)) return topology.Floors[i];

        var best = -1;
        var distance = double.PositiveInfinity;
        for (var i = 0; i < topology.Areas.Count; i++)
        {
            var area = topology.Areas[i];
            if (x < area.Left || x >= area.Right || y >= area.Bottom) continue;
            var candidate = area.DistanceSquared(x, y);
            if (candidate < distance) { best = i; distance = candidate; }
        }
        if (best >= 0) return topology.Floors[best];
        for (var i = 0; i < topology.Areas.Count; i++)
        {
            var candidate = topology.Areas[i].DistanceSquared(x, y);
            if (best < 0 || candidate < distance) { best = i; distance = candidate; }
        }
        return topology.Floors[best];
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
    /// A lower platform the pet could hop down onto, just past the edge it's walking toward.
    ///
    /// Used at the edge of a window so the pet can drop to the next window (or the desktop)
    /// instead of always turning around. Only platforms *below* the current one qualify — hopping
    /// upward would need a jump arc the pet can't reliably make, and hopping sideways at the same
    /// height tends to look like it clipped through the window edge.
    /// </summary>
    /// <param name="direction">-1 walking left, +1 walking right.</param>
    /// <param name="maxGap">How far past the edge the landing spot may start.</param>
    /// <param name="petWidth">Platforms narrower than this aren't worth aiming for.</param>
    public Platform? FindHopDownTarget(
        double fromX, double direction, double currentGroundY, double maxGap, double petWidth)
    {
        Platform? best = null;
        foreach (var p in AllPlatforms)
        {
            // Must be lower than where we are now, but not a sheer drop across the whole screen.
            var drop = p.GroundY - currentGroundY;
            if (drop <= 4 || drop > 520)
            {
                continue;
            }

            if (p.Right - p.Left < petWidth * 0.8)
            {
                continue;
            }

            // Horizontal gap from the pet to the near edge of the candidate, in travel direction.
            var gap = direction > 0 ? p.Left - fromX : fromX - p.Right;

            // Negative gap means it already extends under us, which is a fine place to land too.
            if (gap > maxGap || gap < -petWidth * 2)
            {
                continue;
            }

            // Prefer the highest qualifying platform: the shortest, most plausible hop.
            if (best is null || p.GroundY < best.Value.GroundY)
            {
                best = p;
            }
        }

        return best;
    }

    /// <summary>
    /// A higher platform the pet could jump up onto, just past the edge it's walking toward.
    /// Mirror of <see cref="FindHopDownTarget"/>, but bounded by how high the pet can actually
    /// jump — aiming at a ledge it can't reach just looks like it bonked into thin air.
    /// </summary>
    public Platform? FindHopUpTarget(
        double fromX, double direction, double currentGroundY, double maxRise, double maxGap, double petWidth)
    {
        Platform? best = null;
        foreach (var p in AllPlatforms)
        {
            var rise = currentGroundY - p.GroundY;
            if (rise <= 6 || rise > maxRise)
            {
                continue;
            }

            if (p.Right - p.Left < petWidth * 0.8)
            {
                continue;
            }

            var gap = direction > 0 ? p.Left - fromX : fromX - p.Right;
            if (gap > maxGap || gap < -petWidth * 2)
            {
                continue;
            }

            // Prefer the lowest qualifying ledge: the easiest step up.
            if (best is null || p.GroundY > best.Value.GroundY)
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
