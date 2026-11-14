using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Image = System.Windows.Controls.Image;
using Point = System.Windows.Point;

namespace DesktopPet.App;

/// <summary>
/// Dissolves only changes of pose group or facing, while ordinary gait frames remain immediate.
/// A separate inert image holds the previous source and flip; neither sprite is squeezed to turn.
/// </summary>
public sealed class PoseTransitionPresenter
{
    public static readonly TimeSpan TransitionDuration = TimeSpan.FromMilliseconds(90);
    private readonly Image _image;
    private readonly Image _previous;
    private readonly ScaleTransform _flip;
    private readonly ScaleTransform _previousFlip = new(1, 1);
    private object? _poseGroup;
    private double _lastFlip = 1;
    private long _generation;

    public PoseTransitionPresenter(Image image, Image previous, ScaleTransform? flip = null)
    {
        _image = image;
        _previous = previous;
        _flip = flip ?? new ScaleTransform(1, 1);
        _image.RenderTransform = _flip;
        _image.RenderTransformOrigin = new Point(0.5, 0.5);
        _previous.RenderTransform = _previousFlip;
        _previous.RenderTransformOrigin = new Point(0.5, 0.5);
        _previous.IsHitTestVisible = false;
        _previous.Focusable = false;
        Reset();
    }

    public bool IsTransitioning => _previous.Source is not null;

    /// <param name="poseGroup">The resolved frame collection; reuse the same instance for one pose/view.</param>
    public void SetFrame(ImageSource source, double flipX, object poseGroup, bool allowTransition = true)
    {
        var poseChanged = !ReferenceEquals(_poseGroup, poseGroup) || _lastFlip != flipX;
        var previousSource = _image.Source;
        var previousFlip = _lastFlip;
        var animate = allowTransition && poseChanged && _poseGroup is not null && previousSource is not null && _image.IsVisible &&
            (!ReferenceEquals(previousSource, source) || previousFlip != flipX);

        if (poseChanged || !allowTransition && IsTransitioning) Reset();
        _poseGroup = poseGroup;
        _lastFlip = flipX;
        if (_flip.ScaleX != flipX) _flip.ScaleX = flipX;
        if (!ReferenceEquals(_image.Source, source)) _image.Source = source;
        if (!animate) return;

        _previous.Source = previousSource;
        _previousFlip.ScaleX = previousFlip;
        _previous.Visibility = Visibility.Visible;
        var generation = _generation;
        var incoming = new DoubleAnimation(0, 1, TransitionDuration) { FillBehavior = FillBehavior.Stop };
        var outgoing = new DoubleAnimation(1, 0, TransitionDuration) { FillBehavior = FillBehavior.Stop };
        outgoing.Completed += (_, _) =>
        {
            if (_generation != generation) return;
            // Release the previous bitmap and animation clocks as soon as the small dissolve ends.
            Reset();
            _poseGroup = poseGroup;
        };
        _image.BeginAnimation(UIElement.OpacityProperty, incoming);
        _previous.BeginAnimation(UIElement.OpacityProperty, outgoing);
    }

    /// <summary>Stop clocks and release the previous pose on close, hide, reset or character change.</summary>
    public void Reset()
    {
        _generation++;
        _image.BeginAnimation(UIElement.OpacityProperty, null);
        _image.Opacity = 1;
        _previous.BeginAnimation(UIElement.OpacityProperty, null);
        _previous.Opacity = 0;
        _previous.Visibility = Visibility.Collapsed;
        _previous.Source = null;
        _poseGroup = null;
    }
}
