namespace DesktopPet.Interop;

public readonly record struct WinEventArgs(uint EventType, IntPtr Hwnd, int ObjectId, int ChildId);

/// <summary>
/// Thin managed wrapper over SetWinEventHook/UnhookWinEvent.
/// Must be created on a thread that pumps Windows messages (e.g. the WPF UI thread) —
/// WINEVENT_OUTOFCONTEXT delivers callbacks on that thread's message loop, no DLL injection needed.
/// </summary>
public sealed class WinEventHook : IDisposable
{
    // Keep a reference so the delegate isn't garbage-collected while native code holds a pointer to it.
    private readonly WinEventDelegate _callback;
    private IntPtr _hook;

    public event Action<WinEventArgs>? EventReceived;

    public WinEventHook(uint eventMin, uint eventMax, bool skipOwnProcess = true)
    {
        _callback = OnWinEvent;
        var flags = NativeMethods.WINEVENT_OUTOFCONTEXT | (skipOwnProcess ? NativeMethods.WINEVENT_SKIPOWNPROCESS : 0);
        _hook = NativeMethods.SetWinEventHook(eventMin, eventMax, IntPtr.Zero, _callback, 0, 0, flags);
    }

    public bool IsActive => _hook != IntPtr.Zero;

    private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint eventThread, uint eventTimeMs)
    {
        if (idObject != NativeMethods.OBJID_WINDOW || idChild != NativeMethods.CHILDID_SELF)
        {
            return;
        }

        EventReceived?.Invoke(new WinEventArgs(eventType, hwnd, idObject, idChild));
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
