using System.Windows;
using System.Windows.Interop;
using DesktopPet.Interop;

namespace DesktopPet.App;

internal static class PetWindowBehavior
{
    public static void Attach(Window window)
    {
        window.ShowActivated = false;
        window.SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(window).Handle;
            WindowStyleHelper.PreventActivation(handle);
            HwndSource.FromHwnd(handle)?.AddHook(KeepKeyboardFocus);
        };
    }

    private static IntPtr KeepKeyboardFocus(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != NativeMethods.WM_MOUSEACTIVATE) return IntPtr.Zero;
        handled = true;
        return new IntPtr(NativeMethods.MA_NOACTIVATE);
    }
}
