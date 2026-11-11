namespace DesktopPet.Interop;

public static class WindowStyleHelper
{
    /// <summary>Makes a window invisible to mouse input (clicks pass through to whatever's behind it).</summary>
    public static void MakeClickThrough(IntPtr hwnd)
    {
        var exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        exStyle |= NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, exStyle);
    }
}
