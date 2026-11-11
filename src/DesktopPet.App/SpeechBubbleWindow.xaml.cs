using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using DesktopPet.Interop;

namespace DesktopPet.App;

/// <summary>
/// The pet's speech bubble. Stays up until the user clicks it — the whole point is that a
/// finished task shouldn't be missed, so it doesn't quietly time out.
/// </summary>
public partial class SpeechBubbleWindow : Window
{
    private IntPtr _hwnd;
    private double _dpiScale = 1.0;

    /// <summary>Raised when the user clicks the bubble to acknowledge it.</summary>
    public event Action? Acknowledged;

    public bool IsShowing { get; private set; }

    public SpeechBubbleWindow()
    {
        InitializeComponent();

        Left = -10000;
        Top = -10000;
        Show();
        Hide();

        _hwnd = new WindowInteropHelper(this).Handle;
        MouseLeftButtonDown += (_, _) => Acknowledge();
    }

    public void ShowMessage(string title, string message, double dpiScale)
    {
        _dpiScale = dpiScale;
        TitleText.Text = string.IsNullOrWhiteSpace(title) ? "알림" : title;
        MessageText.Text = message ?? string.Empty;
        MessageText.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;

        IsShowing = true;
        Show();
        Activate();
    }

    /// <summary>Keeps the bubble parked above the pet as it moves around.</summary>
    public void FollowPet(double petLeft, double petTop, double petWidth)
    {
        if (!IsShowing)
        {
            return;
        }

        var left = petLeft + petWidth / 2 - ActualWidth / 2;
        var top = petTop - ActualHeight + 6;

        // Keep it on screen — a bubble half off the top edge is unreadable.
        var work = SystemParameters.WorkArea;
        left = Math.Clamp(left, work.Left + 4, Math.Max(work.Left + 4, work.Right - ActualWidth - 4));
        top = Math.Max(work.Top + 4, top);

        NativeMethods.SetWindowPos(
            _hwnd, NativeMethods.HWND_TOPMOST,
            (int)Math.Round(left * _dpiScale), (int)Math.Round(top * _dpiScale), 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    private void Acknowledge()
    {
        if (!IsShowing)
        {
            return;
        }

        IsShowing = false;
        Hide();
        Acknowledged?.Invoke();
    }

    /// <summary>Closes the bubble without re-raising <see cref="Acknowledged"/> (the caller already acted).</summary>
    public void Dismiss()
    {
        IsShowing = false;
        Hide();
    }

    public void Shutdown()
    {
        IsShowing = false;
        Close();
    }
}
