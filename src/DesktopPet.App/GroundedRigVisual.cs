using System.Windows;
using System.Windows.Media;
using DesktopPet.Core;

namespace DesktopPet.App;

/// <summary>Six cached textures, three persistent drawing nodes and continuous distance-driven transforms.</summary>
public sealed class GroundedRigVisual : FrameworkElement
{
    private readonly DrawingGroup _drawing = new();
    private readonly ImageDrawing _farDrawing = new(), _nearDrawing = new(), _bodyDrawing = new();
    private readonly MatrixTransform _farTransform = new(), _nearTransform = new(), _bodyTransform = new();
    private readonly MatrixTransform _displayTransform = new();
    private CharacterPresentation? _presentation;
    private WalkRigDefinition? _rig;
    private WalkView? _view;
    private bool _facingRight = true;
    private readonly record struct Textures(ImageSource Body, ImageSource Near, ImageSource Far);
    private Textures _side, _turn;

    public GroundedRigVisual()
    {
        IsHitTestVisible = false;
        Focusable = false;
        AddLayer(_farDrawing, _farTransform);
        AddLayer(_nearDrawing, _nearTransform);
        AddLayer(_bodyDrawing, _bodyTransform);
        _drawing.Transform = _displayTransform;
        SizeChanged += (_, _) => UpdateDisplayMatrix();
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
    }

    public bool HasRig => _rig is not null;
    public int TextureCount => HasRig ? 6 : 0;
    public long PoseUpdateCount { get; private set; }
    public GroundedGaitPose Pose { get; private set; }
    public Matrix NearFootMatrix => _nearTransform.Matrix;
    public Matrix FarFootMatrix => _farTransform.Matrix;
    public Matrix BodyMatrix => _bodyTransform.Matrix;
    public Matrix DisplayMatrix => _displayTransform.Matrix;

    private void AddLayer(ImageDrawing image, MatrixTransform transform)
    {
        var layer = new DrawingGroup { Transform = transform };
        layer.Children.Add(image);
        _drawing.Children.Add(layer);
    }

    public void SetPresentation(CharacterPresentation? presentation)
    {
        if (ReferenceEquals(_presentation, presentation)) return;
        _presentation = presentation;
        _rig = presentation?.Definition.WalkRig;
        _view = null;
        PoseUpdateCount = 0;
        Pose = default;
        _side = _turn = default;
        _bodyDrawing.ImageSource = _nearDrawing.ImageSource = _farDrawing.ImageSource = null;
        if (_rig is not null && presentation is not null)
        {
            Textures Load(WalkRigViewDefinition view) => new(
                presentation.Frames[view.BodyPath], presentation.Frames[view.NearFootPath], presentation.Frames[view.FarFootPath]);
            _side = Load(_rig.Side);
            _turn = Load(_rig.Turn);
            var rect = new Rect(0, 0, presentation.Definition.FrameWidth, presentation.Definition.FrameHeight);
            _bodyDrawing.Rect = _nearDrawing.Rect = _farDrawing.Rect = rect;
        }
        _nearTransform.Matrix = _farTransform.Matrix = _bodyTransform.Matrix = Matrix.Identity;
        UpdateDisplayMatrix();
        InvalidateVisual();
    }

    public bool UpdatePose(WalkView view, double phase, double settleProgress, bool facingRight)
    {
        if (_rig is null || _presentation?.Definition.WalkCycleDistancePx is not { } distance) return false;
        var selected = view == WalkView.Turn ? WalkView.Turn : WalkView.Side;
        if (_view != selected)
        {
            var textures = selected == WalkView.Turn ? _turn : _side;
            _bodyDrawing.ImageSource = textures.Body;
            _nearDrawing.ImageSource = textures.Near;
            _farDrawing.ImageSource = textures.Far;
            _view = selected;
        }
        var pose = GroundedGait.Sample(phase, distance, _rig.FootLiftPx);
        if (settleProgress > 0) pose = GroundedGait.Settle(pose, settleProgress);
        Pose = pose;
        _nearTransform.Matrix = new Matrix(1, 0, 0, 1, pose.FootA.X, -pose.FootA.Lift);
        _farTransform.Matrix = new Matrix(1, 0, 0, 1, pose.FootB.X, -pose.FootB.Lift);
        _bodyTransform.Matrix = new Matrix(1, 0, 0, 1, 0, pose.BodyBob);
        if (_facingRight != facingRight)
        {
            _facingRight = facingRight;
            UpdateDisplayMatrix();
        }
        PoseUpdateCount++;
        return true;
    }

    private void UpdateDisplayMatrix()
    {
        if (_presentation is null) { _displayTransform.Matrix = Matrix.Identity; return; }
        var frame = _presentation.Definition;
        var width = ActualWidth > 0 ? ActualWidth : double.IsFinite(Width) && Width > 0 ? Width : frame.FrameWidth;
        var height = ActualHeight > 0 ? ActualHeight : double.IsFinite(Height) && Height > 0 ? Height : frame.FrameHeight;
        var scale = Math.Min(width / frame.FrameWidth, height / frame.FrameHeight);
        var x = (width - frame.FrameWidth * scale) / 2;
        var y = (height - frame.FrameHeight * scale) / 2;
        _displayTransform.Matrix = _facingRight
            ? new Matrix(scale, 0, 0, scale, x, y)
            : new Matrix(-scale, 0, 0, scale, x + frame.FrameWidth * scale, y);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        drawingContext.DrawDrawing(_drawing);
    }
}
