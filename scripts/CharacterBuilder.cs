// Image-processing helpers used by build-characters.ps1 (loaded via Add-Type).
// Turns a single flat artwork file into the full set of animation frames a DesktopPet
// character needs, by compositing programmatic transforms (bob, shear, squash) and
// emotion overlays (tears, anger mark) onto one extracted sprite.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class CharacterBuilder
{
    // ---------- pixel access ----------

    private static byte[] GetPixels(Bitmap bmp)
    {
        var d = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var px = new byte[bmp.Width * bmp.Height * 4];
        Marshal.Copy(d.Scan0, px, 0, px.Length);
        bmp.UnlockBits(d);
        return px;
    }

    private static void SetPixels(Bitmap bmp, byte[] px)
    {
        var d = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
            ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        Marshal.Copy(px, 0, d.Scan0, px.Length);
        bmp.UnlockBits(d);
    }

    /// <summary>An independent 32bpp ARGB copy, so a processing attempt can be thrown away.</summary>
    public static Bitmap LoadArgbFrom(Bitmap src)
    {
        var dst = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(dst))
        {
            g.Clear(Color.Transparent);
            g.DrawImage(src, new Rectangle(0, 0, src.Width, src.Height));
        }
        return dst;
    }

    /// <summary>Loads any readable image into a 32bpp ARGB bitmap we own.</summary>
    public static Bitmap LoadArgb(string path)
    {
        using (var src = new Bitmap(path))
        {
            var dst = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(dst))
            {
                g.Clear(Color.Transparent);
                g.DrawImage(src, new Rectangle(0, 0, src.Width, src.Height));
            }
            return dst;
        }
    }

    // ---------- automatic analysis ----------

    /// <summary>
    /// Guesses the flat background colour by majority vote over the image's border pixels, and
    /// reports whether the image is already transparent there. Returns packed 0xRRGGBB, or -1 when
    /// the border is already transparent (nothing to remove).
    /// </summary>
    public static int DetectBackgroundColor(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        var px = GetPixels(bmp);

        int transparent = 0, opaque = 0;
        var votes = new Dictionary<int, int>();

        Action<int, int> sample = (x, y) =>
        {
            int i = (y * w + x) * 4;
            if (px[i + 3] < 24) { transparent++; return; }
            opaque++;
            // Quantize to 8 levels per channel so near-identical shades vote together.
            int key = ((px[i + 2] >> 5) << 10) | ((px[i + 1] >> 5) << 5) | (px[i] >> 5);
            int seen;
            votes[key] = votes.TryGetValue(key, out seen) ? seen + 1 : 1;
        };

        for (int x = 0; x < w; x++) { sample(x, 0); sample(x, h - 1); }
        for (int y = 0; y < h; y++) { sample(0, y); sample(w - 1, y); }

        if (transparent > opaque) return -1;

        int bestKey = -1, bestCount = 0;
        foreach (var kv in votes)
        {
            if (kv.Value > bestCount) { bestCount = kv.Value; bestKey = kv.Key; }
        }
        if (bestKey < 0) return -1;

        // Average the actual pixels matching the winning bucket for a precise colour.
        long sumR = 0, sumG = 0, sumB = 0; int n = 0;
        Action<int, int> refine = (x, y) =>
        {
            int i = (y * w + x) * 4;
            if (px[i + 3] < 24) return;
            int key = ((px[i + 2] >> 5) << 10) | ((px[i + 1] >> 5) << 5) | (px[i] >> 5);
            if (key != bestKey) return;
            sumR += px[i + 2]; sumG += px[i + 1]; sumB += px[i]; n++;
        };
        for (int x = 0; x < w; x++) { refine(x, 0); refine(x, h - 1); }
        for (int y = 0; y < h; y++) { refine(0, y); refine(w - 1, y); }

        if (n == 0) return -1;
        return ((int)(sumR / n) << 16) | ((int)(sumG / n) << 8) | (int)(sumB / n);
    }

    /// <summary>
    /// How solidly the largest remaining blob fills its own bounding box.
    ///
    /// This is the health check for background removal. A properly extracted character is a solid
    /// shape (~0.5-0.8). If the fill leaked through a gap in the outline and ate the body, what's
    /// left is a few thin strokes, which fill their bounding box very sparsely (~0.1). The caller
    /// uses this to detect a bad extraction and retry with a tighter tolerance.
    /// </summary>
    public static double LargestBlobFillRatio(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        var px = GetPixels(bmp);
        var seen = new bool[w * h];
        var queue = new Queue<int>();
        double ratioOfLargest = 0;
        int largestArea = 0;

        for (int start = 0; start < w * h; start++)
        {
            if (seen[start] || px[start * 4 + 3] < 40) continue;

            seen[start] = true;
            queue.Enqueue(start);
            int minX = w, minY = h, maxX = -1, maxY = -1, area = 0;

            while (queue.Count > 0)
            {
                int idx = queue.Dequeue();
                int x = idx % w, y = idx / w;
                area++;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;

                for (int dy = -1; dy <= 1; dy++)
                {
                    int ny = y + dy;
                    if (ny < 0 || ny >= h) continue;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx;
                        if (nx < 0 || nx >= w) continue;
                        int n = ny * w + nx;
                        if (seen[n] || px[n * 4 + 3] < 40) continue;
                        seen[n] = true;
                        queue.Enqueue(n);
                    }
                }
            }

            // Judge the biggest blob specifically. Picking the *best-filling* blob instead would
            // let a small solid detail (a blush mark) report a healthy ratio while the character
            // body next to it is a hollow outline.
            long boxArea = (long)(maxX - minX + 1) * (maxY - minY + 1);
            if (boxArea > 0 && area > largestArea)
            {
                largestArea = area;
                ratioOfLargest = area / (double)boxArea;
            }
        }

        return ratioOfLargest;
    }

    /// <summary>
    /// Bounding boxes of the separate opaque blobs in the image, largest first, ignoring anything
    /// smaller than <paramref name="minAreaFraction"/> of the image (stray dots, watermarks).
    /// Each box is packed as {x, y, w, h} in the returned flat array.
    /// </summary>
    public static int[] FindBlobs(Bitmap bmp, double minAreaFraction)
    {
        int w = bmp.Width, h = bmp.Height;
        var px = GetPixels(bmp);
        var label = new int[w * h];
        var boxes = new List<int[]>();
        var queue = new Queue<int>();
        int minArea = Math.Max(16, (int)(w * h * minAreaFraction));

        for (int start = 0; start < w * h; start++)
        {
            if (label[start] != 0 || px[start * 4 + 3] < 40) continue;

            label[start] = 1;
            queue.Enqueue(start);
            int minX = w, minY = h, maxX = -1, maxY = -1, area = 0;

            while (queue.Count > 0)
            {
                int idx = queue.Dequeue();
                int x = idx % w, y = idx / w;
                area++;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;

                // 8-connected: diagonal touches (a paw barely grazing another) keep blobs together.
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        int nIdx = ny * w + nx;
                        if (label[nIdx] != 0 || px[nIdx * 4 + 3] < 40) continue;
                        label[nIdx] = 1;
                        queue.Enqueue(nIdx);
                    }
                }
            }

            if (area >= minArea)
            {
                boxes.Add(new[] { minX, minY, maxX - minX + 1, maxY - minY + 1, area });
            }
        }

        if (boxes.Count == 0) return new int[0];
        boxes.Sort((a, b) => b[4].CompareTo(a[4]));

        // Line art often leaves a character's limbs as strokes detached from the body outline,
        // so one character can arrive here as several blobs. Treat the substantial blobs as
        // character bodies and absorb each small fragment into the nearest body it's close to.
        int largestArea = boxes[0][4];
        var bodies = new List<int[]>();
        var fragments = new List<int[]>();
        foreach (var b in boxes)
        {
            if (b[4] >= largestArea * 0.25) bodies.Add(b); else fragments.Add(b);
        }

        double reach = Math.Max(bodies[0][3], bodies[0][2]) * 0.25;
        foreach (var f in fragments)
        {
            int bestIdx = -1;
            double bestDist = double.MaxValue;
            for (int i = 0; i < bodies.Count; i++)
            {
                double d = BoxGap(f, bodies[i]);
                if (d < bestDist) { bestDist = d; bestIdx = i; }
            }

            if (bestIdx < 0 || bestDist > reach) continue; // unrelated speck; drop it

            var body = bodies[bestIdx];
            int x0 = Math.Min(body[0], f[0]);
            int y0 = Math.Min(body[1], f[1]);
            int x1 = Math.Max(body[0] + body[2], f[0] + f[2]);
            int y1 = Math.Max(body[1] + body[3], f[1] + f[3]);
            bodies[bestIdx] = new[] { x0, y0, x1 - x0, y1 - y0, body[4] + f[4] };
        }

        var flat = new int[bodies.Count * 4];
        for (int i = 0; i < bodies.Count; i++)
        {
            flat[i * 4] = bodies[i][0];
            flat[i * 4 + 1] = bodies[i][1];
            flat[i * 4 + 2] = bodies[i][2];
            flat[i * 4 + 3] = bodies[i][3];
        }
        return flat;
    }

    /// <summary>Shortest gap between two boxes; 0 when they touch or overlap.</summary>
    private static double BoxGap(int[] a, int[] b)
    {
        double dx = Math.Max(0, Math.Max(b[0] - (a[0] + a[2]), a[0] - (b[0] + b[2])));
        double dy = Math.Max(0, Math.Max(b[1] - (a[1] + a[3]), a[1] - (b[1] + b[3])));
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// Finds x positions where a wide blob should be cut into separate characters: columns that
    /// are a clear local minimum in opaque-pixel count relative to their surroundings. Characters
    /// drawn holding hands stay connected, so blob detection alone can't separate them — but the
    /// seam between two bodies still has far less ink than either body.
    /// </summary>
    public static int[] FindSplitColumns(Bitmap bmp, int boxX, int boxY, int boxW, int boxH)
    {
        int w = bmp.Width;
        var px = GetPixels(bmp);

        var ink = new int[boxW];
        for (int i = 0; i < boxW; i++)
        {
            int count = 0;
            for (int y = boxY; y < boxY + boxH; y++)
            {
                if (px[(y * w + boxX + i) * 4 + 3] >= 40) count++;
            }
            ink[i] = count;
        }

        // Smooth to avoid reacting to single-column noise.
        var smooth = new int[boxW];
        const int R = 4;
        for (int i = 0; i < boxW; i++)
        {
            int sum = 0, n = 0;
            for (int k = -R; k <= R; k++)
            {
                int j = i + k;
                if (j < 0 || j >= boxW) continue;
                sum += ink[j]; n++;
            }
            smooth[i] = sum / Math.Max(1, n);
        }

        // Only consider splitting a blob wide enough to plausibly hold two characters side by
        // side. Without this a single round character gets bisected at its own narrowest column.
        if (boxW < boxH * 1.35) return new int[0];

        // A seam must be the lowest point for some distance around it, and clearly lower than the
        // bodies on either side. margin keeps us away from the blob's own rounded ends.
        int margin = Math.Max(20, boxH / 3);
        int window = Math.Max(24, boxH / 4);
        var cuts = new List<int>();

        for (int i = margin; i < boxW - margin; i++)
        {
            bool isLocalMin = true;
            for (int k = -window; k <= window && isLocalMin; k++)
            {
                int j = i + k;
                if (j < 0 || j >= boxW) continue;
                if (smooth[j] < smooth[i]) isLocalMin = false;
            }
            if (!isLocalMin) continue;

            int peakLeft = 0, peakRight = 0;
            for (int k = 1; k <= window * 2; k++)
            {
                if (i - k >= 0) peakLeft = Math.Max(peakLeft, smooth[i - k]);
                if (i + k < boxW) peakRight = Math.Max(peakRight, smooth[i + k]);
            }
            int neighbourPeak = Math.Min(peakLeft, peakRight);
            if (neighbourPeak == 0 || smooth[i] > neighbourPeak * 0.62) continue;

            // Collapse plateaus: keep one cut per seam.
            if (cuts.Count > 0 && i - cuts[cuts.Count - 1] < margin) continue;
            cuts.Add(i);
        }

        for (int i = 0; i < cuts.Count; i++) cuts[i] += boxX;
        return cuts.ToArray();
    }

    // ---------- background removal ----------

    /// <summary>
    /// Clears a flat background to transparent by flood-filling inward from every edge pixel,
    /// matching <paramref name="bgColor"/> (packed 0xRRGGBB) within <paramref name="tolerance"/>.
    ///
    /// Flood fill — rather than "every pixel matching the background colour" — is essential:
    /// Chiikawa's body is white and the background often is too, so only whites *connected to the
    /// outside* may be erased.
    /// </summary>
    public static void RemoveBackground(Bitmap bmp, int bgColor, int tolerance, int barrierRadius)
    {
        int w = bmp.Width, h = bmp.Height;
        int bgR = (bgColor >> 16) & 0xFF, bgG = (bgColor >> 8) & 0xFF, bgB = bgColor & 0xFF;
        var px = GetPixels(bmp);
        var barrier = BuildInkBarrier(px, w, h, barrierRadius);
        var visited = new bool[w * h];
        var stack = new Stack<int>();

        Action<int, int> push = (x, y) =>
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return;
            int idx = y * w + x;
            if (visited[idx] || barrier[idx]) return;
            int i = idx * 4;

            bool isBg = px[i + 3] < 24 ||
                        (Math.Abs(px[i + 2] - bgR) <= tolerance &&
                         Math.Abs(px[i + 1] - bgG) <= tolerance &&
                         Math.Abs(px[i] - bgB) <= tolerance);
            if (!isBg) return;
            visited[idx] = true;
            stack.Push(idx);
        };

        for (int x = 0; x < w; x++) { push(x, 0); push(x, h - 1); }
        for (int y = 0; y < h; y++) { push(0, y); push(w - 1, y); }

        while (stack.Count > 0)
        {
            int idx = stack.Pop();
            int x = idx % w, y = idx / w;
            px[idx * 4 + 3] = 0;
            push(x - 1, y); push(x + 1, y); push(x, y - 1); push(x, y + 1);
        }

        SetPixels(bmp, px);
    }

    /// <summary>
    /// Marks pixels within <paramref name="radius"/> of dark linework as off-limits to the
    /// background fill, effectively sealing gaps up to 2*radius wide.
    ///
    /// This matters when the character's interior is the same colour as the background — a white
    /// character on white. Line art frequently leaves the outline *open* (limbs drawn as separate
    /// strokes), and the fill pours straight through such a gap and eats the whole body, leaving
    /// a hollow shell. The cost is a light fringe outside the outline, which <see cref="FeatherEdges"/>
    /// fades and the downscale to sprite size hides.
    ///
    /// Dilation is done as two separable passes so cost grows with radius, not radius squared.
    /// </summary>
    private static bool[] BuildInkBarrier(byte[] px, int w, int h, int radius)
    {
        var isInk = new bool[w * h];
        for (int idx = 0; idx < w * h; idx++)
        {
            int i = idx * 4;
            if (px[i + 3] < 24) continue;
            int lum = (px[i] * 2 + px[i + 1] * 5 + px[i + 2] * 3) / 10;
            isInk[idx] = lum < 150;
        }

        if (radius < 1) return isInk;

        var horizontal = new bool[w * h];
        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                if (!isInk[row + x]) continue;
                int from = Math.Max(0, x - radius), to = Math.Min(w - 1, x + radius);
                for (int nx = from; nx <= to; nx++) horizontal[row + nx] = true;
            }
        }

        var barrier = new bool[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (!horizontal[y * w + x]) continue;
                int from = Math.Max(0, y - radius), to = Math.Min(h - 1, y + radius);
                for (int ny = from; ny <= to; ny++) barrier[ny * w + x] = true;
            }
        }
        return barrier;
    }

    /// <summary>
    /// Eats back the background-coloured fringe that a wide ink barrier necessarily leaves behind.
    ///
    /// Sealing an open outline needs a barrier several pixels thick, but that same barrier also
    /// protects the background *just outside* the outline, leaving the character wearing a thick
    /// halo. The halo inflates the sprite's bounding box, so normalising to a fixed height shrinks
    /// the actual character and it reads as squashed. This second pass re-floods from the already
    /// cleared region with a minimal barrier, so the fringe goes but the sealed interior stays.
    /// </summary>
    /// <param name="maxDepth">
    /// How many pixels inward the shave may travel. This bound is essential: with the barrier
    /// relaxed, an unbounded fill would find the same outline gap the wide barrier was sealing
    /// and hollow the character out all over again. Limiting the reach to roughly the halo's
    /// thickness removes the halo without ever getting far enough to breach the body.
    /// </param>
    public static void ShrinkFringe(Bitmap bmp, int bgColor, int tolerance, int keepRadius, int maxDepth)
    {
        int w = bmp.Width, h = bmp.Height;
        int bgR = (bgColor >> 16) & 0xFF, bgG = (bgColor >> 8) & 0xFF, bgB = bgColor & 0xFF;
        var px = GetPixels(bmp);
        var barrier = BuildInkBarrier(px, w, h, keepRadius);
        var depth = new int[w * h];
        var visited = new bool[w * h];
        var queue = new Queue<int>();

        for (int idx = 0; idx < w * h; idx++)
        {
            if (px[idx * 4 + 3] == 0)
            {
                visited[idx] = true;
                queue.Enqueue(idx);
            }
        }

        while (queue.Count > 0)
        {
            int idx = queue.Dequeue();
            int d = depth[idx];
            if (d >= maxDepth) continue;

            int x = idx % w, y = idx / w;
            for (int k = 0; k < 4; k++)
            {
                int nx = x + (k == 0 ? -1 : k == 1 ? 1 : 0);
                int ny = y + (k == 2 ? -1 : k == 3 ? 1 : 0);
                if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;

                int n = ny * w + nx;
                if (visited[n] || barrier[n]) continue;

                int i = n * 4;
                if (Math.Abs(px[i + 2] - bgR) > tolerance ||
                    Math.Abs(px[i + 1] - bgG) > tolerance ||
                    Math.Abs(px[i] - bgB) > tolerance) continue;

                visited[n] = true;
                depth[n] = d + 1;
                px[i + 3] = 0;
                queue.Enqueue(n);
            }
        }

        SetPixels(bmp, px);
    }

    /// <summary>
    /// Softens the hard edge left by flood fill: a still-opaque pixel that touches a cleared pixel
    /// and is itself close to the background colour is anti-aliasing left over from the original
    /// edge, so fade it rather than leaving a crisp halo once the sprite is scaled down.
    /// </summary>
    public static void FeatherEdges(Bitmap bmp, int bgColor, int tolerance)
    {
        int w = bmp.Width, h = bmp.Height;
        int bgR = (bgColor >> 16) & 0xFF, bgG = (bgColor >> 8) & 0xFF, bgB = bgColor & 0xFF;
        int wide = tolerance * 2;
        var px = GetPixels(bmp);
        var original = (byte[])px.Clone();

        for (int y = 1; y < h - 1; y++)
        {
            for (int x = 1; x < w - 1; x++)
            {
                int i = (y * w + x) * 4;
                if (original[i + 3] < 200) continue;
                if (Math.Abs(original[i + 2] - bgR) > wide ||
                    Math.Abs(original[i + 1] - bgG) > wide ||
                    Math.Abs(original[i] - bgB) > wide) continue;

                bool touchesCleared =
                    original[(y * w + x - 1) * 4 + 3] == 0 ||
                    original[(y * w + x + 1) * 4 + 3] == 0 ||
                    original[((y - 1) * w + x) * 4 + 3] == 0 ||
                    original[((y + 1) * w + x) * 4 + 3] == 0;

                if (touchesCleared) px[i + 3] = 90;
            }
        }

        SetPixels(bmp, px);
    }

    // ---------- geometry ----------

    public static Bitmap Crop(Bitmap src, int x, int y, int w, int h)
    {
        x = Math.Max(0, x); y = Math.Max(0, y);
        w = Math.Min(w, src.Width - x); h = Math.Min(h, src.Height - y);
        var dst = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(dst))
        {
            g.Clear(Color.Transparent);
            g.DrawImage(src, new Rectangle(0, 0, w, h), new Rectangle(x, y, w, h), GraphicsUnit.Pixel);
        }
        return dst;
    }

    /// <summary>Crops away fully transparent margins so the sprite's own bounds are known exactly.</summary>
    public static Bitmap Trim(Bitmap src, int alphaThreshold)
    {
        int w = src.Width, h = src.Height;
        var px = GetPixels(src);
        int minX = w, minY = h, maxX = -1, maxY = -1;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (px[(y * w + x) * 4 + 3] <= alphaThreshold) continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }

        if (maxX < 0) return Crop(src, 0, 0, w, h);
        return Crop(src, minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    /// <summary>
    /// Scales a trimmed sprite to <paramref name="targetHeight"/> and seats it on a fixed
    /// canvas: horizontally centered, feet <paramref name="bottomMargin"/> px above the canvas
    /// bottom. Returns the resulting feet row (the character.json anchor.y) via <paramref name="anchorY"/>.
    /// </summary>
    public static Bitmap Normalize(Bitmap trimmed, int canvasW, int canvasH, int targetHeight, int bottomMargin, out int anchorY)
    {
        double scale = (double)targetHeight / trimmed.Height;
        int newW = Math.Max(1, (int)Math.Round(trimmed.Width * scale));
        int newH = Math.Max(1, (int)Math.Round(trimmed.Height * scale));

        int left = (canvasW - newW) / 2;
        int top = canvasH - bottomMargin - newH;
        anchorY = canvasH - bottomMargin;

        var dst = new Bitmap(canvasW, canvasH, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(dst))
        {
            g.Clear(Color.Transparent);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(trimmed, new Rectangle(left, top, newW, newH));
        }
        return dst;
    }

    /// <summary>
    /// Renders a posed copy of the base sprite. Scaling pivots on the feet (so a squashed or
    /// stretched pose still stands on the ground); rotation pivots just above the feet, which
    /// makes a tilt read as the body rocking over its base — a waddle — rather than the whole
    /// character swinging sideways.
    /// </summary>
    public static Bitmap Pose(Bitmap baseSprite, int anchorY, double scaleX, double scaleY, double rotateDeg, double dx, double dy)
    {
        int w = baseSprite.Width, h = baseSprite.Height;
        var dst = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(dst))
        {
            g.Clear(Color.Transparent);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            g.TranslateTransform((float)dx, (float)dy);

            g.TranslateTransform(w / 2f, anchorY);
            g.ScaleTransform((float)scaleX, (float)scaleY);
            g.TranslateTransform(-w / 2f, -anchorY);

            if (Math.Abs(rotateDeg) > 0.001)
            {
                float pivotY = anchorY - h * 0.10f;
                g.TranslateTransform(w / 2f, pivotY);
                g.RotateTransform((float)rotateDeg);
                g.TranslateTransform(-w / 2f, -pivotY);
            }

            g.DrawImage(baseSprite, 0, 0, w, h);
        }
        return dst;
    }

    /// <summary>
    /// Renders a walk frame by moving the character's <i>feet</i> independently of its body,
    /// instead of rotating the whole sprite.
    ///
    /// Transforming the sprite as a single rigid image can only ever look like a photo being
    /// waggled, and any scaling visibly squashes the character. Real walk cycles come from parts
    /// moving relative to each other, so this splits the bottom strip of the sprite down the
    /// middle into a left and right foot and lifts/advances them in alternation, while the body
    /// just bobs very slightly. The characters here have small feet poking out below a round
    /// body, so a horizontal cut is a good enough stand-in for real rigging.
    /// </summary>
    /// <param name="footBandRatio">Height of the foot strip as a fraction of the sprite height.</param>
    public static Bitmap PoseWalk(
        Bitmap baseSprite, int anchorY, int spriteHeight, double footBandRatio,
        double bodyDy, double bodyRotateDeg,
        double leftLift, double leftDx, double rightLift, double rightDx)
    {
        int w = baseSprite.Width, h = baseSprite.Height;
        int bandTop = (int)Math.Round(anchorY - spriteHeight * footBandRatio);
        bandTop = Math.Max(1, Math.Min(h - 1, bandTop));
        int midX = w / 2;

        var dst = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(dst))
        {
            g.Clear(Color.Transparent);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (var body = Crop(baseSprite, 0, 0, w, bandTop))
            using (var leftFoot = Crop(baseSprite, 0, bandTop, midX, h - bandTop))
            using (var rightFoot = Crop(baseSprite, midX, bandTop, w - midX, h - bandTop))
            {
                // Feet first, so the body overlaps where they meet and no seam shows.
                g.DrawImage(leftFoot, (float)leftDx, (float)(bandTop - leftLift + bodyDy));
                g.DrawImage(rightFoot, (float)(midX + rightDx), (float)(bandTop - rightLift + bodyDy));

                var state = g.Save();
                if (Math.Abs(bodyRotateDeg) > 0.001)
                {
                    float pivotY = bandTop;
                    g.TranslateTransform(w / 2f, pivotY);
                    g.RotateTransform((float)bodyRotateDeg);
                    g.TranslateTransform(-w / 2f, -pivotY);
                }
                g.DrawImage(body, 0f, (float)bodyDy);
                g.Restore(state);
            }
        }
        return dst;
    }

    // ---------- emotion overlays ----------

    /// <summary>
    /// A fat teardrop under each eye plus a smaller one already falling away, in the chunky
    /// rounded style the source art uses. Positions are relative to the sprite's own bounds so
    /// this lands correctly on every character.
    /// </summary>
    public static void DrawTears(Bitmap bmp, int anchorY, int spriteHeight, double dropProgress)
    {
        int w = bmp.Width;
        float bodyTop = anchorY - spriteHeight;
        float eyeY = bodyTop + spriteHeight * 0.46f;
        float dropW = Math.Max(5f, spriteHeight * 0.11f);
        float slide = (float)(spriteHeight * 0.07 * dropProgress);

        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var fill = new SolidBrush(Color.FromArgb(235, 150, 215, 255)))
            using (var shine = new SolidBrush(Color.FromArgb(220, 240, 252, 255)))
            using (var pen = new Pen(Color.FromArgb(255, 70, 150, 225), Math.Max(1.8f, spriteHeight * 0.022f)))
            {
                foreach (float cx in new[] { w * 0.37f, w * 0.63f })
                {
                    // main drop clinging to the cheek
                    float topY = eyeY + slide;
                    var path = new GraphicsPath();
                    path.AddEllipse(cx - dropW / 2f, topY, dropW, dropW * 1.15f);
                    path.AddPolygon(new[]
                    {
                        new PointF(cx, topY - dropW * 0.75f),
                        new PointF(cx - dropW * 0.36f, topY + dropW * 0.45f),
                        new PointF(cx + dropW * 0.36f, topY + dropW * 0.45f),
                    });
                    g.FillPath(fill, path);
                    g.DrawPath(pen, path);
                    path.Dispose();

                    g.FillEllipse(shine, cx - dropW * 0.28f, topY + dropW * 0.18f, dropW * 0.26f, dropW * 0.34f);

                    // a smaller drop that has already broken off, further along as the loop advances
                    float smallW = dropW * 0.55f;
                    float smallY = topY + dropW * (1.5f + 1.4f * (float)dropProgress);
                    using (var smallPen = new Pen(pen.Color, pen.Width * 0.8f))
                    {
                        g.FillEllipse(fill, cx - smallW / 2f, smallY, smallW, smallW * 1.2f);
                        g.DrawEllipse(smallPen, cx - smallW / 2f, smallY, smallW, smallW * 1.2f);
                    }
                }
            }
        }
    }

    /// <summary>
    /// The manga anger mark (💢) over the character's head: four bent spokes around a center.
    /// Drawn twice — a thick white under-stroke first — so it stays readable on top of the
    /// character's own linework.
    /// </summary>
    public static void DrawAngerMark(Bitmap bmp, int anchorY, int spriteHeight, double pulse)
    {
        int w = bmp.Width;
        float bodyTop = anchorY - spriteHeight;
        float size = (float)(spriteHeight * (0.26 + 0.05 * pulse));
        float cx = w * 0.74f;
        float cy = bodyTop + spriteHeight * 0.07f;

        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float stroke = Math.Max(2.2f, size * 0.18f);

            using (var halo = new Pen(Color.FromArgb(220, 255, 255, 255), stroke * 2.1f))
            using (var pen = new Pen(Color.FromArgb(255, 228, 35, 58), stroke))
            {
                foreach (var p in new[] { halo, pen })
                {
                    p.StartCap = LineCap.Round;
                    p.EndCap = LineCap.Round;
                    p.LineJoin = LineJoin.Round;
                }

                float outer = size * 0.5f, inner = size * 0.19f;
                for (int pass = 0; pass < 2; pass++)
                {
                    var p = pass == 0 ? halo : pen;
                    for (int k = 0; k < 4; k++)
                    {
                        double ang = Math.PI / 4 + k * Math.PI / 2;
                        float ox = (float)Math.Cos(ang), oy = (float)Math.Sin(ang);
                        var path = new GraphicsPath();
                        path.AddLines(new[]
                        {
                            new PointF(cx + ox * outer, cy + oy * inner),
                            new PointF(cx + ox * inner, cy + oy * inner),
                            new PointF(cx + ox * inner, cy + oy * outer),
                        });
                        g.DrawPath(p, path);
                        path.Dispose();
                    }
                }
            }
        }
    }

    /// <summary>
    /// Flushes the sprite's light areas red for the angry pose. Dark pixels are skipped so the
    /// character's black outlines stay black instead of turning muddy brown.
    /// </summary>
    public static void TintRed(Bitmap bmp, double amount)
    {
        var px = GetPixels(bmp);
        for (int i = 0; i < px.Length; i += 4)
        {
            if (px[i + 3] == 0) continue;

            // BGRA order; rough perceptual luminance.
            int lum = (px[i] * 2 + px[i + 1] * 5 + px[i + 2] * 3) / 10;
            if (lum < 130) continue; // leave outlines and dark details untouched

            px[i + 2] = (byte)Math.Min(255, px[i + 2] + (255 - px[i + 2]) * amount * 0.9); // R up
            px[i + 1] = (byte)(px[i + 1] * (1 - amount * 0.45));                            // G down
            px[i] = (byte)(px[i] * (1 - amount * 0.40));                                    // B down
        }
        SetPixels(bmp, px);
    }

    public static void Save(Bitmap bmp, string path)
    {
        bmp.Save(path, ImageFormat.Png);
    }
}
