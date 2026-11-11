namespace DesktopPet.Core;

/// <summary>
/// A horizontal surface the pet can stand on: either a monitor's floor (desktop work area) or the
/// top edge of some other visible window. All values are WPF device-independent units.
/// </summary>
public readonly record struct Platform(double Left, double Right, double GroundY);

/// <summary>A screen point in WPF device-independent units (e.g. the mouse cursor position).</summary>
public readonly record struct PetPoint(double X, double Y);
