using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopPet.App;
using DesktopPet.Core;

internal static class GroundedRigVisualRegressionTests
{
    public static void Run(string charactersRoot, string? baselineCharactersRoot = null, bool capture = false)
    {
        var verified = 0;
        CharacterPresentation? benchmark = null;
        foreach (var name in CharacterLoader.ListAvailableCharacterNames(charactersRoot))
        {
            var folder = Path.Combine(charactersRoot, name);
            var definition = CharacterLoader.Load(folder);
            if (definition.WalkRig is null) continue;
            var presentation = CharacterPresentation.Load(folder);
            Check(presentation.Frames.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(definition.GetPresentationFramePaths()),
                name + ": live mode decoded extra raster gait textures or omitted a required rest resource");
            Check(presentation.Frames.Values.All(image => image.IsFrozen), name + ": live textures are mutable thread-owned bitmaps");
            VerifyActualContacts(presentation);
            VerifyContinuousSettling(presentation);
            benchmark ??= presentation;
            verified++;
        }
        Check(verified > 0 && benchmark is not null, "No live rig was available for actual drawing tests");
        Console.WriteLine($"PASS Actual live-rig drawing transforms keep planted feet fixed through sub-frame phases, scaling and native pixel placement for {verified} characters");
        Console.WriteLine($"PASS Actual live-rig settling lowers raised feet continuously without moving either contact anchor for {verified} characters");
        VerifyPixelPlacement();
        VerifyRetainedDrawingUpdates(benchmark!, capture);
        VerifyCachedUpdates(benchmark!);
        ProfileDecodeResources(benchmark!, baselineCharactersRoot);
    }

    private static GroundedRigVisual Visual(CharacterPresentation presentation, double scale = 1)
    {
        var visual = new GroundedRigVisual
        {
            Width = presentation.Definition.FrameWidth * scale,
            Height = presentation.Definition.FrameHeight * scale,
        };
        visual.SetPresentation(presentation);
        visual.Measure(new Size(visual.Width, visual.Height));
        visual.Arrange(new Rect(0, 0, visual.Width, visual.Height));
        return visual;
    }

    private static void VerifyActualContacts(CharacterPresentation presentation)
    {
        var definition = presentation.Definition;
        var distance = definition.WalkCycleDistancePx!.Value;
        var anchor = new Point(definition.FrameWidth / 2d, definition.AnchorY);
        foreach (var scale in new[] { 0.5, 1, 2.5 })
        {
            var visual = Visual(presentation, scale);
            Check(visual.HasRig && visual.TextureCount == 6 && !visual.IsHitTestVisible && !visual.Focusable,
                "Live rig did not retain six noninteractive textures");
            foreach (var view in new[] { WalkView.Side, WalkView.Turn })
            foreach (var right in new[] { true, false })
            foreach (var useFarFoot in new[] { false, true })
            foreach (var dpi in new[] { 1d, 1.25, 2 })
            {
                double? contactWorldX = null;
                var start = useFarFoot ? 1.5 : 1d;
                for (var i = 0; i <= 100; i++)
                {
                    var phase = start + i * 0.59 / 100;
                    Check(visual.UpdatePose(view, phase, 0, right), "Live pose update unexpectedly fell back to raster frames");
                    var foot = useFarFoot ? visual.Pose.FootB : visual.Pose.FootA;
                    Check(foot.IsPlanted && foot.Lift == 0, "Fixture did not sample a planted foot");
                    var local = (useFarFoot ? visual.FarFootMatrix : visual.NearFootMatrix).Transform(anchor);
                    var displayed = visual.DisplayMatrix.Transform(local);
                    var bodyX = -500.17 + (right ? 1 : -1) * phase * distance * scale;
                    var bodyY = 600 - definition.AnchorY * scale;
                    var placement = SpritePixelPlacement.Calculate(bodyX, bodyY, dpi);
                    var worldX = placement.PixelX / dpi + placement.OffsetX + displayed.X;
                    var worldY = placement.PixelY / dpi + placement.OffsetY + displayed.Y;
                    contactWorldX ??= worldX;
                    Near(worldX, contactWorldX.Value, 1e-7, "An actual retained foot transform slid in world space");
                    Near(worldY, 600, 1e-8, "Body bob, sprite scaling or pixel rounding moved the planted foot off the floor");
                }
            }
            visual.UpdatePose(WalkView.Side, 0.25, 0, true);
            var before = visual.NearFootMatrix.OffsetX;
            visual.UpdatePose(WalkView.Side, 0.25001, 0, true);
            Near(visual.NearFootMatrix.OffsetX - before, -distance * 0.00001, 1e-10,
                "Live rig still advances in discrete PNG frame-sized steps");
            Near(visual.NearFootMatrix.M11, 1, 1e-12, "Continuous foot motion squeezed its texture");
            Near(visual.NearFootMatrix.M22, 1, 1e-12, "Continuous foot motion stretched its texture");
            visual.SetPresentation(null);
        }
    }

    private static void VerifyContinuousSettling(CharacterPresentation presentation)
    {
        var visual = Visual(presentation);
        var definition = presentation.Definition;
        var anchor = new Point(definition.FrameWidth / 2d, definition.AnchorY);
        foreach (var view in new[] { WalkView.Side, WalkView.Turn })
        {
            visual.UpdatePose(view, 0.75, 0, true);
            var originalNear = visual.NearFootMatrix.Transform(anchor);
            var originalFar = visual.FarFootMatrix.Transform(anchor);
            Check(originalNear.Y < definition.AnchorY, "Fixture has no raised foot to settle");
            var previousY = originalNear.Y;
            for (var i = 1; i <= 100; i++)
            {
                visual.UpdatePose(view, 0.75, i / 100d, true);
                var near = visual.NearFootMatrix.Transform(anchor);
                var far = visual.FarFootMatrix.Transform(anchor);
                Near(near.X, originalNear.X, 1e-10, "Settling moved the near foot horizontally");
                Near(far.X, originalFar.X, 1e-10, "Settling moved the far foot horizontally");
                Near(far.Y, definition.AnchorY, 1e-10, "Settling lifted the already planted support foot");
                Check(near.Y >= previousY && near.Y <= definition.AnchorY + 1e-10 && near.Y - previousY < 0.1,
                    "Settling still drops a raised foot in two large raster steps");
                previousY = near.Y;
            }
            Near(previousY, definition.AnchorY, 1e-10, "Raised foot never reached the floor");
        }
        visual.SetPresentation(null);
    }

    private static void VerifyPixelPlacement()
    {
        foreach (var x in new[] { -3840.49, -1920.2, -0.501, -0.001, 0, 0.001, 0.499, 1080.7, 4096.125 })
        foreach (var y in new[] { -1080.7, -0.25, 0, 1080.7 })
        foreach (var dpi in new[] { 1d, 1.25, 1.5, 2, 2.5 })
        {
            var placement = SpritePixelPlacement.Calculate(x, y, dpi);
            Near(placement.PixelX / dpi + placement.OffsetX, x, 1e-9, "Native integer X and visual residual do not reconstruct the requested position");
            Near(placement.PixelY / dpi + placement.OffsetY, y, 1e-9, "Native integer Y and visual residual do not reconstruct the requested position");
            Check(Math.Abs(placement.OffsetX) <= 0.5 / dpi + 1e-10 && Math.Abs(placement.OffsetY) <= 0.5 / dpi + 1e-10,
                "Fractional visual placement exceeded one half of a native pixel");
        }
        Console.WriteLine("PASS Native integer placement plus bounded fractional visual offsets reconstructs continuous positive and negative monitor coordinates");
    }

    private static void VerifyCachedUpdates(CharacterPresentation presentation)
    {
        var visual = Visual(presentation);
        for (var i = 0; i < 2000; i++) visual.UpdatePose(i % 2 == 0 ? WalkView.Side : WalkView.Turn, i * 0.001, 0, true);
        var fields = typeof(GroundedRigVisual).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Where(field => field.FieldType == typeof(ImageDrawing) || field.FieldType == typeof(MatrixTransform)).ToArray();
        var cached = fields.Select(field => field.GetValue(visual)).ToArray();
        Check(fields.Count(field => field.FieldType == typeof(ImageDrawing)) == 3, "Live visual does not own exactly three reusable image drawings");
        var before = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        const int updates = 10000;
        for (var i = 0; i < updates; i++)
            visual.UpdatePose(i / 200 % 2 == 0 ? WalkView.Side : WalkView.Turn, i * 0.00113, 0, i / 1000 % 2 == 0);
        var milliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        for (var i = 0; i < fields.Length; i++) Check(ReferenceEquals(cached[i], fields[i].GetValue(visual)), "Pose updates replaced a cached drawing or transform object");
        Check(visual.TextureCount == 6 && visual.PoseUpdateCount == updates + 2000, "Pose updates recreated textures or lost update accounting");
        Check(allocated < updates * 2048L, "Cached pose updates allocated more than 2 KiB per update");
        Console.WriteLine($"PASS Cached live rig keeps three drawing nodes and six frozen textures: {updates} warmed pose updates allocated {allocated:N0} bytes ({allocated / (double)updates:0.##} B/update), loop {milliseconds:0.##}ms; render-thread/GPU cost not measured");
        visual.SetPresentation(null);
        Check(!visual.HasRig && visual.TextureCount == 0 && !visual.UpdatePose(WalkView.Side, 0, 0, true), "Clearing a presentation retained an active rig");
        foreach (var field in fields.Where(field => field.FieldType == typeof(ImageDrawing)))
            Check(((ImageDrawing)field.GetValue(visual)!).ImageSource is null, "Clearing a presentation retained a decoded foot/body texture");
    }

    private static void VerifyRetainedDrawingUpdates(CharacterPresentation presentation, bool capture)
    {
        var visual = Visual(presentation, 2);
        visual.UpdatePose(WalkView.Side, 0.22, 0, true);
        visual.UpdateLayout();
        var first = Render(visual);
        var firstPixels = Pixels(first);
        // Do not explicitly invalidate or rebuild the visual: changing its retained drawing
        // transforms must be enough for the next real WPF render to receive the updated pose.
        visual.UpdatePose(WalkView.Side, 0.34, 0, true);
        var second = Render(visual);
        Check(!firstPixels.AsSpan().SequenceEqual(Pixels(second)), "Retained matrix changes did not update the actual WPF drawing after its first render");
        visual.UpdatePose(WalkView.Side, 0.75, 0, true);
        var raised = Render(visual);
        visual.UpdatePose(WalkView.Side, 0.75, 1, true);
        var grounded = Render(visual);
        Check(!Pixels(raised).AsSpan().SequenceEqual(Pixels(grounded)), "Continuous settling changed diagnostics but did not change the actual WPF image");
        if (capture)
        {
            const int padding = 12;
            const int labelHeight = 30;
            var images = new[] { first, second, raised, grounded };
            var labels = new[] { "WPF pose 0.22", "WPF update 0.34", "Raised foot", "Settled foot" };
            var width = first.PixelWidth;
            var height = first.PixelHeight;
            var sheet = new DrawingVisual();
            using (var context = sheet.RenderOpen())
            {
                context.DrawRectangle(new SolidColorBrush(Color.FromRgb(245, 248, 242)), null,
                    new Rect(0, 0, (width + padding) * images.Length + padding, height + labelHeight + padding * 2));
                for (var i = 0; i < images.Length; i++)
                {
                    var text = new FormattedText(labels[i], System.Globalization.CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight, new Typeface("Segoe UI"), 14, Brushes.DarkSlateGray, 1);
                    context.DrawText(text, new Point(padding + (width + padding) * i, padding));
                    context.DrawImage(images[i], new Rect(padding + (width + padding) * i, labelHeight + padding, width, height));
                }
            }
            var bitmap = new RenderTargetBitmap((width + padding) * images.Length + padding,
                height + labelHeight + padding * 2, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(sheet);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            var path = Path.Combine(AppContext.BaseDirectory, "live-rig-v8.png");
            using var stream = File.Create(path);
            encoder.Save(stream);
            Console.WriteLine("CAPTURE Actual WPF retained drawing: " + path);
        }
        visual.SetPresentation(null);
        Console.WriteLine("PASS RenderTargetBitmap changes after a retained live-rig pose update and continuous foot settling without rebuilding or explicitly invalidating the visual");
    }

    private static RenderTargetBitmap Render(GroundedRigVisual visual)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(visual.Width), (int)Math.Ceiling(visual.Height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return bitmap;
    }

    private static byte[] Pixels(BitmapSource bitmap)
    {
        var bytes = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(bytes, bitmap.PixelWidth * 4, 0);
        return bytes;
    }

    private static void ProfileDecodeResources(CharacterPresentation presentation, string? baselineCharactersRoot)
    {
        var folder = presentation.Definition.FolderPath;
        _ = CharacterPresentation.Load(folder, includeRasterGait: true);
        _ = CharacterPresentation.Load(folder);
        var fullStart = Stopwatch.GetTimestamp();
        var full = CharacterPresentation.Load(folder, includeRasterGait: true);
        var fullMs = Stopwatch.GetElapsedTime(fullStart).TotalMilliseconds;
        var liveStart = Stopwatch.GetTimestamp();
        var live = CharacterPresentation.Load(folder);
        var liveMs = Stopwatch.GetElapsedTime(liveStart).TotalMilliseconds;
        var fullBytes = PixelBytes(full);
        var liveBytes = PixelBytes(live);
        Check(live.Frames.Count < full.Frames.Count && liveBytes < fullBytes,
            "The live presentation still decodes the complete raster gait bank");
        Console.WriteLine($"PASS Decode inventory: full audit {full.Frames.Count} textures/{fullBytes:N0} RGBA pixel bytes, live {live.Frames.Count}/{liveBytes:N0}; one warmed load full {fullMs:0.##}ms/live {liveMs:0.##}ms (raw local timing)");
        if (!string.IsNullOrWhiteSpace(baselineCharactersRoot))
        {
            var baselineFolder = Path.Combine(baselineCharactersRoot, Path.GetFileName(folder));
            Check(Directory.Exists(baselineFolder), "Requested baseline character folder does not exist");
            var baseline = CharacterPresentation.Load(baselineFolder, includeRasterGait: true);
            Console.WriteLine($"BASELINE Previous published presentation: {baseline.Frames.Count} textures/{PixelBytes(baseline):N0} RGBA pixel bytes; current live {live.Frames.Count}/{liveBytes:N0}");
        }
    }

    private static long PixelBytes(CharacterPresentation presentation) => presentation.Frames.Values.Sum(image => (long)image.PixelWidth * image.PixelHeight * 4);
    private static void Near(double actual, double expected, double tolerance, string message) => Check(double.IsFinite(actual) && Math.Abs(actual - expected) <= tolerance, $"{message}: expected {expected}, actual {actual}");
    private static void Check(bool valid, string message) { if (!valid) throw new Exception(message); }
}
