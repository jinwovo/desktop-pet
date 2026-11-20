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
            VerifyLayeredTransitions(presentation);
            VerifyReversal(presentation);
            VerifyRestOpacity(presentation);
            benchmark ??= presentation;
            verified++;
        }
        Check(verified > 0 && benchmark is not null, "No live rig was available for actual drawing tests");
        Console.WriteLine($"PASS Actual live-rig drawing transforms keep planted feet fixed through sub-frame phases, scaling and native pixel placement for {verified} characters");
        Console.WriteLine($"PASS Actual live-rig settling lowers raised feet continuously without moving either contact anchor for {verified} characters");
        Console.WriteLine($"PASS Actual upper-body reversals blend without a one-frame mirror jump while contact feet stay fixed for {verified} characters");
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
        var root = (DrawingGroup)typeof(GroundedRigVisual).GetField("_drawing", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(visual)!;
        var cached = Drawings(root).ToArray();
        Check(cached.Count(drawing => drawing is ImageDrawing) == 5, "Live visual omitted head/torso/feet parts or the resting layer");
        var before = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        const int updates = 10000;
        for (var i = 0; i < updates; i++)
            visual.UpdatePose(i / 200 % 2 == 0 ? WalkView.Side : WalkView.Turn, i * 0.00113, 0, i / 1000 % 2 == 0);
        var milliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        var after = Drawings(root).ToArray();
        Check(cached.Length == after.Length && cached.Zip(after).All(pair => ReferenceEquals(pair.First, pair.Second)), "Pose updates replaced retained drawing nodes");
        Check(visual.TextureCount == 6 && visual.PoseUpdateCount == updates + 2000, "Pose updates recreated textures or lost update accounting");
        Check(allocated < updates * 2048L, "Cached pose updates allocated more than 2 KiB per update");
        Console.WriteLine($"PASS Layered live rig retains five drawing nodes, six source rig textures, four derived body images and three reusable blend buffers: {updates} warmed pose updates allocated {allocated:N0} bytes ({allocated / (double)updates:0.##} B/update), loop {milliseconds:0.##}ms; render-thread/GPU cost not measured");
        visual.SetPresentation(null);
        Check(!visual.HasRig && visual.TextureCount == 0 && !visual.UpdatePose(WalkView.Side, 0, 0, true), "Clearing a presentation retained an active rig");
        foreach (var drawing in cached.OfType<ImageDrawing>())
            Check(drawing.ImageSource is null, "Clearing a presentation retained a decoded foot/body/rest texture");
    }

    private static IEnumerable<Drawing> Drawings(Drawing drawing)
    {
        yield return drawing;
        if (drawing is DrawingGroup group)
            foreach (var child in group.Children)
            foreach (var item in Drawings(child)) yield return item;
    }

    private static void VerifyLayeredTransitions(CharacterPresentation presentation)
    {
        var visual = Visual(presentation, 2);
        visual.UpdatePose(WalkView.Side, 0.8, 0, true, 0);
        var side = Pixels(Render(visual));
        var target = Visual(presentation, 2); target.UpdatePose(WalkView.Turn, 0.8, 0, true, 0);
        var turn = Pixels(Render(target));
        static long Difference(byte[] a, byte[] b) { long total = 0; for (var i = 0; i < a.Length; i++) total += Math.Abs(a[i] - b[i]); return total; }
        var fullJump = Difference(side, turn);
        Check(fullJump > 0, "Fixture has identical side and quarter artwork");
        visual.UpdatePose(WalkView.Turn, 0.8, 0, true, 1d / 60);
        var first = Pixels(Render(visual));
        Check(visual.TurnWeight is > 0 and < 1 && Difference(side, first) < fullJump * 0.7 && visual.RestWeight == 0,
            $"View changed in one frame or frontal rest leaked into walking: {presentation.Definition.Name}, weight {visual.TurnWeight}, delta {Difference(side, first)}/{fullJump}");
        Near(visual.HeadMatrix.Determinant, 1, 1e-12, "Head articulation stretched artwork");
        Near(visual.BodyMatrix.Determinant, 1, 1e-12, "Torso articulation stretched artwork");
        Check(visual.HeadMatrix != visual.BodyMatrix, "Head and torso are still one rigid transform");
        var weight = 0d;
        for (var i = 0; i <= 100; i++)
        {
            visual.UpdatePose(WalkView.Turn, 0.8, i / 100d, true);
            Check(visual.RestWeight >= weight && double.IsFinite(visual.RestWeight), "Rest transition moved backwards or became invalid");
            weight = visual.RestWeight;
        }
        Near(weight, 1, 1e-12, "The live pose never converged to its exact resting artwork");
        var actualRest = Pixels(Render(visual));
        var restImage = presentation.Frames[presentation.Definition.GetFrames(PetState.Idle)[0]];
        var expected = new DrawingVisual();
        using (var context = expected.RenderOpen()) context.DrawImage(restImage, new Rect(0, 0, visual.Width, visual.Height));
        var expectedBitmap = new RenderTargetBitmap((int)visual.Width, (int)visual.Height, 96, 96, PixelFormats.Pbgra32);
        expectedBitmap.Render(expected);
        Check(actualRest.AsSpan().SequenceEqual(Pixels(expectedBitmap)), "Settling did not end on the exact next rest image");
        var alternate = presentation.Frames[presentation.Definition.GetFrames(PetState.Idle)[1]];
        visual.UpdatePose(WalkView.Side, 0.8, 0.75, false);
        var snapshot = typeof(GroundedRigVisual).GetField("_restSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(visual);
        visual.SetRestFrame(alternate, unmirrored: true);
        visual.UpdatePose(WalkView.Side, 0.8, 0.85, false);
        Check(snapshot is not null && ReferenceEquals(snapshot, typeof(GroundedRigVisual).GetField("_restSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(visual)),
            "A mid-settle blink rebuilt the walking snapshot instead of changing only the resting target");
        visual.UpdatePose(WalkView.Side, 0.8, 1, false);
        var targetDrawing = new DrawingVisual();
        using (var context = targetDrawing.RenderOpen()) context.DrawImage(alternate, new Rect(0, 0, visual.Width, visual.Height));
        var targetBitmap = new RenderTargetBitmap((int)visual.Width, (int)visual.Height, 96, 96, PixelFormats.Pbgra32);
        targetBitmap.Render(targetDrawing);
        Check(Pixels(Render(visual)).AsSpan().SequenceEqual(Pixels(targetBitmap)), "A changed blink target was not the exact final unmirrored resting image");
        visual.SetRestFrame(alternate, unmirrored: false);
        visual.UpdatePose(WalkView.Side, 0.8, 1, false);
        using (var context = targetDrawing.RenderOpen())
        {
            context.PushTransform(new MatrixTransform(-1, 0, 0, 1, visual.Width, 0));
            context.DrawImage(alternate, new Rect(0, 0, visual.Width, visual.Height)); context.Pop();
        }
        targetBitmap = new RenderTargetBitmap((int)visual.Width, (int)visual.Height, 96, 96, PixelFormats.Pbgra32); targetBitmap.Render(targetDrawing);
        Check(Pixels(Render(visual)).AsSpan().SequenceEqual(Pixels(targetBitmap)), "An authored directional rest was not mirrored exactly after a leftward walk");
        visual.UpdatePose(WalkView.Side, 0.8, 0, true, 1d / 60, reducedMotion: true);
        Near(visual.HeadMatrix.M12, 0, 1e-12, "Reduced motion retained secondary head rotation");
        Near(visual.NearFootMatrix.M12, 0, 1e-12, "Reduced motion retained toe roll");
        Check(visual.TurnWeight == 0 && visual.RestWeight == 0 && !actualRest.AsSpan().SequenceEqual(Pixels(Render(visual))), "Reduced motion did not suppress blends");
        visual.SetPresentation(null); target.SetPresentation(null);
    }

    private static void VerifyRestOpacity(CharacterPresentation presentation)
    {
        foreach (var scale in new[] { 0.5, 1, 2.5 })
        foreach (var facingRight in new[] { true, false })
        {
            var visual = Visual(presentation, scale);
            visual.UpdatePose(WalkView.Side, 0.175, 0.65, facingRight);
            var walking = Render(visual);
            var index = ((int)(walking.PixelHeight * 0.625) * walking.PixelWidth + walking.PixelWidth / 2) * 4 + 3;
            var alphaWalking = Pixels(walking)[index];
            visual.UpdatePose(WalkView.Side, 0.175, 0.825, facingRight);
            var alphaMidpoint = Pixels(Render(visual))[index];
            visual.UpdatePose(WalkView.Side, 0.175, 1, facingRight);
            var alphaRest = Pixels(Render(visual))[index];
            Check(alphaWalking >= 240 && alphaRest >= 240, "Rest-opacity fixture missed the opaque body interior");
            Check(alphaMidpoint >= Math.Min(alphaWalking, alphaRest) - 4,
                $"Rest blending made an opaque body translucent: {presentation.Definition.Name}, scale {scale}, right {facingRight}, alpha {alphaWalking}/{alphaMidpoint}/{alphaRest}");
            visual.SetPresentation(null);
        }
    }

    private static void VerifyReversal(CharacterPresentation presentation)
    {
        var visual = Visual(presentation, 2);
        const double phase = 0.2;
        var reversed = GroundedGait.ReversePhase(phase);
        visual.UpdatePose(WalkView.Side, phase, 0, true, 0);
        var before = Pixels(Render(visual));
        var anchor = new Point(presentation.Definition.FrameWidth / 2d, presentation.Definition.AnchorY);
        var contact = visual.DisplayMatrix.Transform(visual.NearFootMatrix.Transform(anchor));
        var target = Visual(presentation, 2);
        target.UpdatePose(WalkView.Side, reversed, 0, false, 0);
        var targetPixels = Pixels(Render(target));
        var upperBytes = (int)(visual.Height * 0.65) * (int)visual.Width * 4;
        long Difference(byte[] a, byte[] b) { long sum = 0; for (var i = 0; i < upperBytes; i++) sum += Math.Abs(a[i] - b[i]); return sum; }
        var instant = Difference(before, targetPixels);
        Check(instant > 0, "Reversal fixture is fully symmetrical");
        visual.UpdatePose(WalkView.Side, reversed, 0, false, 0);
        Check(Difference(before, Pixels(Render(visual))) < instant * 0.3,
            "Changing direction still mirrored the upper body in a single frame: " + presentation.Definition.Name);
        var after = visual.DisplayMatrix.Transform(visual.NearFootMatrix.Transform(anchor));
        Near(after.X, contact.X, 1e-8, "Upper-body blending dragged a planted foot during reversal");
        Near(after.Y, contact.Y, 1e-8, "Upper-body blending lifted a planted foot during reversal");
        visual.UpdatePose(WalkView.Side, reversed, 0, false, 1d / 60);
        Check(visual.FacingRightWeight is > 0 and < 1 && Difference(before, Pixels(Render(visual))) < instant * 0.7,
            "The first timed reversal frame still snapped to its target");
        visual.UpdatePose(WalkView.Side, reversed, 0, false, 0.06 - 1d / 60);
        Near(visual.FacingRightWeight, 0.5, 1e-12, "Facing midpoint missed its pivot");
        var root = (DrawingGroup)typeof(GroundedRigVisual).GetField("_drawing", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(visual)!;
        var pivotSource = (BitmapSource)Drawings(root).OfType<ImageDrawing>().ElementAt(2).ImageSource;
        var pivot = Pixels(pivotSource);
        var idle = presentation.Frames[presentation.Definition.GetFrames(PetState.Idle)[0]];
        var front = Pixels(new FormatConvertedBitmap(idle, PixelFormats.Pbgra32, null, 0));
        var w = pivotSource.PixelWidth;
        for (var y = 0; y < pivotSource.PixelHeight * 0.55; y++)
        for (var x = 0; x < w; x++)
        for (var c = 0; c < 4; c++)
            Check(pivot[(y * w + x) * 4 + c] == front[(y * w + w - x - 1) * 4 + c],
                "Mid-turn superimposed two faces instead of using a single frontal pivot");
        var lastBodyRow = -1;
        foreach (var path in new[] { presentation.Definition.WalkRig!.Side.BodyPath, presentation.Definition.WalkRig.Turn.BodyPath })
        {
            var bytes = Pixels(new FormatConvertedBitmap(presentation.Frames[path], PixelFormats.Pbgra32, null, 0));
            for (var i = 3; i < bytes.Length; i += 4) if (bytes[i] > 0) lastBodyRow = Math.Max(lastBodyRow, i / 4 / w);
        }
        for (var i = (lastBodyRow + 1) * w * 4 + 3; i < pivot.Length; i += 4)
            Check(pivot[i] == 0, "The upper-body pivot introduced a duplicate pair of feet");
        visual.UpdatePose(WalkView.Side, reversed, 0, false, 0.12);
        Check(visual.FacingRightWeight == 0 && Pixels(Render(visual)).AsSpan().SequenceEqual(targetPixels),
            "Facing interpolation left a slow-motion tail or stale mirrored texture");
        visual.UpdatePose(WalkView.Side, phase, 0, true, 0, reducedMotion: true);
        Check(visual.FacingRightWeight == 1, "Reduced motion did not opt out of reversal blending");
        visual.SetPresentation(null); target.SetPresentation(null);
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
            CaptureMotion(presentation);
        }
        visual.SetPresentation(null);
        Console.WriteLine("PASS RenderTargetBitmap changes after a retained live-rig pose update and continuous foot settling without rebuilding or explicitly invalidating the visual");
    }

    private static void CaptureMotion(CharacterPresentation presentation)
    {
        // Actual WPF frames at 50 Hz; translation and gait use the same travelled distance.
        var visual = Visual(presentation, 2);
        var encoder = new GifBitmapEncoder();
        var appearance = new WalkAppearanceController();
        var x = 12d;
        const int walkingFrames = 110;
        for (var i = 0; i < 145; i++)
        {
            var walking = i < walkingFrames;
            var right = i < 60;
            var speed = presentation.Definition.WalkSpeedPxPerSec;
            var distance = walking ? (right ? 1 : -1) * speed * 0.02 : 0;
            x += distance * 2;
            appearance.Update(walking ? PetState.Walk : PetState.Idle, right, walking ? (right ? speed : -speed) : 0,
                speed, 0.02, distanceDeltaPx: distance, walkCycleDistancePx: presentation.Definition.WalkCycleDistancePx);
            visual.UpdatePose(appearance.View, appearance.GaitPhase, walking ? 0 : appearance.SettleProgress, appearance.FacingRight, 0.02);
            if (i is 59 or 62 or 66)
            {
                var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(Render(visual)));
                using var preview = File.Create(Path.Combine(AppContext.BaseDirectory, $"reversal-frame-{i}.png"));
                png.Save(preview);
            }
            var frame = new DrawingVisual();
            using (var context = frame.RenderOpen())
            {
                context.DrawRectangle(Brushes.WhiteSmoke, null, new Rect(0, 0, 440, 292));
                context.DrawText(new FormattedText("Actual WPF: walk / reverse / stop", System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface("Segoe UI"), 14, Brushes.DarkSlateGray, 1), new Point(12, 8));
                context.DrawImage(Render(visual), new Rect(x, 30, visual.Width, visual.Height));
            }
            var bitmap = new RenderTargetBitmap(440, 292, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(frame);
            var metadata = new BitmapMetadata("gif");
            metadata.SetQuery("/grctlext/Delay", (ushort)2);
            encoder.Frames.Add(BitmapFrame.Create(bitmap, null, metadata, null));
        }
        var path = Path.Combine(AppContext.BaseDirectory, "layered-rig-motion.gif");
        using var stream = File.Create(path); encoder.Save(stream);
        visual.SetPresentation(null);
        Console.WriteLine("CAPTURE Actual WPF motion at 50 Hz: " + path);
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
