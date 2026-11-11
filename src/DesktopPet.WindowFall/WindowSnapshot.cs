using System.Windows.Media.Imaging;
using DesktopPet.Interop;

namespace DesktopPet.WindowFall;

/// <summary>The last known appearance/position of a tracked top-level window, in screen pixels.</summary>
public sealed record WindowSnapshot(IntPtr Hwnd, string Title, RECT RectPx, BitmapSource? Image);
