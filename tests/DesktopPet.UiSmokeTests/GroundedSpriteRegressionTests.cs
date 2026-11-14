using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopPet.App;
using DesktopPet.Core;

internal static class GroundedSpriteRegressionTests
{
    public static void Run(string charactersRoot)
    {
        var verified = 0;
        foreach (var name in CharacterLoader.ListAvailableCharacterNames(charactersRoot))
        {
            var folder = Path.Combine(charactersRoot, name);
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "character.json")));
            if (!document.RootElement.TryGetProperty("groundedWalk", out var audit)) continue;
            var presentation = CharacterPresentation.Load(folder, includeRasterGait: true);
            var character = presentation.Definition;
            var distance = character.WalkCycleDistancePx!.Value;
            var contacts = audit.GetProperty("contacts").EnumerateArray().ToArray();
            Check(contacts.Length == 32, name + ": expected 32 independently sampled poses");
            for (var i = 0; i < contacts.Length; i++)
            {
                var expected = GroundedGait.Sample(i / 32d, distance);
                Near(contacts[i].GetProperty("near").GetProperty("x").GetDouble(), expected.FootA.X, 1e-8, "Generated near-foot trajectory diverged from runtime math");
                Near(contacts[i].GetProperty("far").GetProperty("x").GetDouble(), expected.FootB.X, 1e-8, "Generated far-foot trajectory diverged from runtime math");
                Near(contacts[i].GetProperty("near").GetProperty("lift").GetDouble(), expected.FootA.Lift, 1e-8, "Generated foot lift diverged from runtime math");
                Near(contacts[i].GetProperty("bodyBob").GetDouble(), expected.BodyBob, 1e-8, "Body motion diverged from runtime math");
            }
            foreach (var view in new[] { WalkView.Side, WalkView.Turn })
            {
                var nearWorld = new List<double>();
                var farWorld = new List<double>();
                for (var i = 0; i < 32; i++)
                {
                    var phase = i / 32d;
                    var pose = GroundedGait.Sample(phase, distance);
                    var path = character.GetWalkFramePath(view, phase);
                    var pixels = Pixels(presentation.Frames[path]);
                    Check(!HasSolidBelowFloor(pixels), name + ": animated foot penetrates the floor");
                    // Audit actual PNG pixels, not only the JSON landmarks or kinematic formulas.
                    // Audit mid-stance when the other foot clears both sole rows. Near lift-off/
                    // landing, its antialiased heel can share these pixels and bias a sole centroid.
                    if (pose.FootA.IsPlanted && pose.FootB.Lift >= 3.5)
                        nearWorld.Add(distance * phase + ContactCenter(pixels));
                    if (pose.FootB.IsPlanted && pose.FootA.Lift >= 3.5)
                        farWorld.Add(distance * phase + ContactCenter(pixels));
                    var settled = Pixels(presentation.Frames[character.GetWalkFramePath(view, phase, 1)]);
                    Check(!HasSolidBelowFloor(settled) && HasContact(settled), name + ": settling did not end on the floor");
                }
                foreach (var measured in new[] { nearWorld, farWorld })
                {
                    Check(measured.Count >= 3, name + ": insufficient isolated raster contact samples");
                    var drift = measured.Max() - measured.Min();
                    Check(drift <= 1.25, $"{name}/{view}: planted PNG footprint drifted {drift:0.###} source pixels");
                }
            }
            verified++;
        }
        Check(verified > 0, "No grounded sprite was audited");
        Console.WriteLine($"PASS Actual isolated mid-stance PNG footprints stay within 1.25px for {verified} grounded characters, with runtime-matching trajectories");
    }

    private static byte[] Pixels(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var bytes = new byte[128 * 128 * 4];
        converted.CopyPixels(bytes, 128 * 4, 0);
        return bytes;
    }

    private static bool HasSolidBelowFloor(byte[] pixels)
    {
        for (var y = 122; y < 128; y++)
            for (var x = 0; x < 128; x++)
                if (pixels[(y * 128 + x) * 4 + 3] >= 100) return true;
        return false;
    }

    private static bool HasContact(byte[] pixels) => Enumerable.Range(0, 128).Any(x => pixels[(121 * 128 + x) * 4 + 3] >= 100);

    private static double ContactCenter(byte[] pixels)
    {
        double weightedX = 0, weight = 0;
        for (var y = 120; y <= 121; y++)
        for (var x = 0; x < 128; x++)
        {
            var alpha = pixels[(y * 128 + x) * 4 + 3];
            if (alpha < 25) continue;
            weightedX += x * alpha;
            weight += alpha;
        }
        Check(weight > 0, "A planted foot is missing from the actual floor row");
        return weightedX / weight;
    }

    private static void Near(double actual, double expected, double tolerance, string message) => Check(Math.Abs(actual - expected) <= tolerance, message);
    private static void Check(bool valid, string message) { if (!valid) throw new Exception(message); }
}
