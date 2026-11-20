namespace DesktopPet.Core;

/// <summary>
/// Keeps a cursor-facing pose stable while the pointer crosses the character's center.
/// The caller supplies coordinates and dead zone in the same coordinate space.
/// </summary>
public sealed class CursorGazeController
{
    public bool FacingRight { get; private set; } = true;

    public void Reset(bool facingRight = true) => FacingRight = facingRight;

    /// <summary>
    /// Returns the retained facing direction. Crossing strictly beyond either edge changes
    /// direction immediately; a sample inside or exactly on an edge keeps the previous pose.
    /// Invalid input leaves the pose unchanged.
    /// </summary>
    public bool Update(double cursorX, double centerX, double deadZonePx = 8)
    {
        if (!double.IsFinite(cursorX) || !double.IsFinite(centerX) ||
            !double.IsFinite(deadZonePx) || deadZonePx < 0)
            return FacingRight;

        // A difference can overflow for two finite, extremely distant coordinates. Its sign
        // still correctly identifies a deliberate far-side turn; adding a band to centerX
        // instead would lose that decision when the center plus band overflows.
        var offset = cursorX - centerX;
        if (offset > deadZonePx) FacingRight = true;
        else if (offset < -deadZonePx) FacingRight = false;
        return FacingRight;
    }
}
