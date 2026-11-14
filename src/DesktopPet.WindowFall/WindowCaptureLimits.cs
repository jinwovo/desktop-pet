using DesktopPet.Interop;

namespace DesktopPet.WindowFall;

/// <summary>Bounds checked before native capture allocation; these calculations need no live window.</summary>
public static class WindowCaptureLimits
{
    // A 32-bpp capture uses at most 64 MiB before the small preview is made. 4K windows fit;
    // oversized/invalid windows are skipped rather than downscaling after an unbounded allocation.
    public const int MaxCaptureDimension = 8192;
    public const long MaxCapturePixels = 16L * 1024 * 1024;
    public const int MaxPreviewEdge = 512;
    public const int MaxCachedWindows = 32;

    public static bool TryGetCaptureSize(RECT rect, out int width, out int height)
    {
        // RECT.Width/Height use int subtraction, which can overflow for corrupt native bounds.
        var wideWidth = (long)rect.Right - rect.Left;
        var wideHeight = (long)rect.Bottom - rect.Top;
        width = height = 0;
        if (wideWidth <= 0 || wideHeight <= 0 || wideWidth > MaxCaptureDimension ||
            wideHeight > MaxCaptureDimension || wideWidth * wideHeight > MaxCapturePixels)
            return false;
        width = (int)wideWidth;
        height = (int)wideHeight;
        return true;
    }

    public static (int Width, int Height) GetPreviewSize(int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        var scale = Math.Min(1, (double)MaxPreviewEdge / Math.Max(width, height));
        return (Math.Max(1, (int)(width * scale)), Math.Max(1, (int)(height * scale)));
    }
}
