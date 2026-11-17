namespace DesktopPet.Core;

/// <summary>A rectangle in one caller-selected desktop coordinate space.</summary>
public readonly record struct DesktopRect(double Left, double Top, double Right, double Bottom)
{
    public double Width => Right - Left;
    public double Height => Bottom - Top;
    public bool IsValid => double.IsFinite(Left) && double.IsFinite(Top) && double.IsFinite(Right) &&
        double.IsFinite(Bottom) && double.IsFinite(Width) && double.IsFinite(Height) && Width > 0 && Height > 0;
    public bool Contains(double x, double y) => x >= Left && x < Right && y >= Top && y < Bottom;
    public double DistanceSquared(double x, double y)
    {
        var dx = Math.Max(Math.Max(Left - x, 0), x - Right);
        var dy = Math.Max(Math.Max(Top - y, 0), y - Bottom);
        return dx * dx + dy * dy;
    }
}
