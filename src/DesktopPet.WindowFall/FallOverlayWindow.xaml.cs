using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using DesktopPet.Interop;

namespace DesktopPet.WindowFall;

/// <summary>
/// A transparent overlay that shows a captured window's last appearance falling (ease-in, like
/// gravity) and shrinking into the taskbar.
///
/// Two deliberate performance choices, both learned from visible stutters:
/// * The window is created once and <b>reused</b> (Hide, never Close). Constructing and showing a
///   fresh AllowsTransparency window costs tens of milliseconds, which was a visible hitch on
///   every single window close.
/// * The native window is only ever <b>moved</b>, never resized per frame; the shrink is a render
///   transform on the image instead.
/// </summary>
public partial class FallOverlayWindow : Window
{
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(500);
    private const double EndScale = 0.15;

    private double _dpiScale = 1.0;
    private double _startWidth;
    private double _startHeight;
    private double _startCenterX;
    private double _startCenterY;
    private double _targetCenterX;
    private double _targetCenterY;

    private IntPtr _hwnd;
    private DateTime _startTimeUtc;
    private bool _isPlaying;

    public bool IsAvailable => !_isPlaying;

    public FallOverlayWindow()
    {
        InitializeComponent();

        // Realize the native window up front (off-screen) so the first real animation doesn't
        // pay window-creation cost at the exact moment the user closes something.
        Left = -10000;
        Top = -10000;
        Width = 1;
        Height = 1;
        Show();
        Hide();

        _hwnd = new WindowInteropHelper(this).Handle;
        WindowStyleHelper.MakeClickThrough(_hwnd);
    }

    public void Play(WindowSnapshot snapshot, double dpiScale, RECT targetRectPx)
    {
        if (_isPlaying)
        {
            return;
        }

        _isPlaying = true;
        _dpiScale = dpiScale;

        _startWidth = snapshot.RectPx.Width / dpiScale;
        _startHeight = snapshot.RectPx.Height / dpiScale;
        _startCenterX = snapshot.RectPx.Left / dpiScale + _startWidth / 2;
        _startCenterY = snapshot.RectPx.Top / dpiScale + _startHeight / 2;

        _targetCenterX = (targetRectPx.Left + targetRectPx.Right) / 2.0 / dpiScale;
        _targetCenterY = (targetRectPx.Top + targetRectPx.Bottom) / 2.0 / dpiScale;

        Width = _startWidth;
        Height = _startHeight;
        PreviewImage.Source = snapshot.Image;
        ShrinkTransform.ScaleX = 1;
        ShrinkTransform.ScaleY = 1;
        Opacity = 1;

        MoveTo(_startCenterX, _startCenterY);
        Show();

        _startTimeUtc = DateTime.UtcNow;
        CompositionTarget.Rendering += CompositionTarget_Rendering;
    }

    private void CompositionTarget_Rendering(object? sender, EventArgs e)
    {
        var elapsedMs = (DateTime.UtcNow - _startTimeUtc).TotalMilliseconds;
        var t = Math.Clamp(elapsedMs / Duration.TotalMilliseconds, 0, 1);
        var easedFall = t * t; // ease-in: starts slow, accelerates like gravity

        var scale = 1.0 - (1.0 - EndScale) * t;
        ShrinkTransform.ScaleX = scale;
        ShrinkTransform.ScaleY = scale;
        Opacity = 1.0 - t;

        MoveTo(
            _startCenterX + (_targetCenterX - _startCenterX) * t,
            _startCenterY + (_targetCenterY - _startCenterY) * easedFall);

        if (t >= 1.0)
        {
            CompositionTarget.Rendering -= CompositionTarget_Rendering;
            Hide();
            PreviewImage.Source = null; // let the captured bitmap go
            _isPlaying = false;
        }
    }

    private void MoveTo(double centerX, double centerY)
    {
        var px = (int)Math.Round((centerX - _startWidth / 2) * _dpiScale);
        var py = (int)Math.Round((centerY - _startHeight / 2) * _dpiScale);
        NativeMethods.SetWindowPos(
            _hwnd, IntPtr.Zero, px, py, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER);
    }

    /// <summary>Really closes the window (app shutdown) rather than just hiding it.</summary>
    public void Shutdown()
    {
        CompositionTarget.Rendering -= CompositionTarget_Rendering;
        _isPlaying = false;
        Close();
    }
}
