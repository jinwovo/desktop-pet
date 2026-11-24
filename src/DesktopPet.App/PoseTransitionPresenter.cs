using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Image = System.Windows.Controls.Image;
using Point = System.Windows.Point;

namespace DesktopPet.App;

/// <summary>
/// Dissolves changes of pose group/facing while ordinary gait frames remain immediate.
/// A separate inert image holds the previous source and flip; neither sprite is squeezed.
/// Optional controllable clocks let the motion inspector pause and step the same dissolve.
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

    private AnimationClock? _incomingClock;
    private AnimationClock? _outgoingClock;
    private bool _paused;
    private TimeSpan _pausedTransitionTime;

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

    /// <param name="poseGroup">Reuse the same frame collection instance for one pose/view.</param>
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
        _incomingClock = (AnimationClock)incoming.CreateClock(true);
        _outgoingClock = (AnimationClock)outgoing.CreateClock(true);
        if (_paused)
        {
            _incomingClock.Controller?.Pause();
            _outgoingClock.Controller?.Pause();
            _pausedTransitionTime = TimeSpan.Zero;
        }
        _outgoingClock.Completed += (_, _) =>
        {
            if (_generation != generation) return;
            Reset();
            _poseGroup = poseGroup;
        };
        _image.ApplyAnimationClock(UIElement.OpacityProperty, _incomingClock);
        _previous.ApplyAnimationClock(UIElement.OpacityProperty, _outgoingClock);
    }

    /// <summary>Freeze existing and newly created dissolves until resumed or manually advanced.</summary>
    public void SetPaused(bool paused)
    {
        if (_paused == paused) return;
        _paused = paused;
        if (paused)
        {
            _pausedTransitionTime = _incomingClock?.CurrentTime ?? TimeSpan.Zero;
            _incomingClock?.Controller?.Pause();
            _outgoingClock?.Controller?.Pause();
        }
        else
        {
            _pausedTransitionTime = TimeSpan.Zero;
            _incomingClock?.Controller?.Resume();
            _outgoingClock?.Controller?.Resume();
        }
    }

    /// <summary>Advance an inspected dissolve independently of the wall clock.</summary>
    public void AdvancePaused(TimeSpan dt)
    {
        if (!_paused || dt <= TimeSpan.Zero) return;
        if (_incomingClock is null || _outgoingClock is null) return;

        // Pause is applied by WPF's timing engine. Begin from the visible clock if it
        // reached a newer tick, while retaining manual progress between repeated clicks.
        if (_incomingClock.CurrentTime is { } observed && observed > _pausedTransitionTime)
            _pausedTransitionTime = observed;

        if (dt >= TransitionDuration - _pausedTransitionTime)
        {
            var poseGroup = _poseGroup;
            Reset();
            _poseGroup = poseGroup;
            return;
        }

        _pausedTransitionTime += dt;
        _incomingClock.Controller?.SeekAlignedToLastTick(_pausedTransitionTime, TimeSeekOrigin.BeginTime);
        _outgoingClock.Controller?.SeekAlignedToLastTick(_pausedTransitionTime, TimeSeekOrigin.BeginTime);
    }

    /// <summary>Remove animation roots and the previous pose, retaining the inspector's pause mode.</summary>
    public void Reset()
    {
        _generation++;
        _incomingClock?.Controller?.Remove();
        _outgoingClock?.Controller?.Remove();
        _incomingClock = null;
        _outgoingClock = null;

        _image.BeginAnimation(UIElement.OpacityProperty, null);
        _image.Opacity = 1;
        _previous.BeginAnimation(UIElement.OpacityProperty, null);
        _previous.Opacity = 0;
        _previous.Visibility = Visibility.Collapsed;
        _previous.Source = null;
        _poseGroup = null;
    }
}
