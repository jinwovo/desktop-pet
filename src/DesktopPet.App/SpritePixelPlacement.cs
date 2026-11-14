namespace DesktopPet.App;

/// <summary>Keep native window coordinates integral while rendering the remaining fraction in WPF.</summary>
public readonly record struct SpritePixelPlacement(int PixelX, int PixelY, double OffsetX, double OffsetY)
{
    public static SpritePixelPlacement Calculate(double x, double y, double dpiScale)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(dpiScale) || dpiScale <= 0)
            throw new ArgumentOutOfRangeException(nameof(dpiScale));
        var pixelX = Math.Round(x * dpiScale);
        var pixelY = Math.Round(y * dpiScale);
        if (!double.IsFinite(pixelX) || !double.IsFinite(pixelY) || pixelX < int.MinValue || pixelX > int.MaxValue || pixelY < int.MinValue || pixelY > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(x));
        return new((int)pixelX, (int)pixelY, x - pixelX / dpiScale, y - pixelY / dpiScale);
    }
}
