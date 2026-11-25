using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopPet.Core;

namespace DesktopPet.App;

/// <summary>Retained head/torso/foot layers share six frozen textures; view and rest changes blend continuously.</summary>
public sealed class GroundedRigVisual : FrameworkElement
{
    private readonly DrawingGroup _drawing = new(), _walking = new(), _rest = new();
    private readonly MatrixTransform _nearTransform = new(), _farTransform = new(), _headTransform = new(), _bodyTransform = new();
    private readonly MatrixTransform _displayTransform = new(), _restDisplayTransform = new();
    private readonly ImageDrawing _restDrawing = new();
    private readonly ViewLayers _layers;
    private RigTextureBlend? _bodyBlend, _nearBlend, _farBlend;
    private RigTextureBlend? _restBlend;
    private BitmapSource? _restFrame, _restSnapshot;
    private readonly DrawingVisual _restCapture = new();
    private readonly RigViewBlend _viewBlend = new();
    private readonly RigFacingBlend _facingBlend = new();
    private CharacterPresentation? _presentation;
    private WalkRigDefinition? _rig;
    private bool _facingRight = true, _unmirroredRest = true;
    private bool? _restFacingRight;
    private bool RestFacesRight => _unmirroredRest || (_restFacingRight ?? _facingRight);

    private sealed class ViewLayers
    {
        public readonly DrawingGroup Group = new();
        public readonly ImageDrawing Far = new(), Near = new(), Torso = new(), Head = new();
        public readonly DrawingGroup TorsoLayer, HeadLayer;
        public ViewLayers(MatrixTransform far, MatrixTransform near, MatrixTransform torso, MatrixTransform head)
        {
            DrawingGroup Add(ImageDrawing image, MatrixTransform transform)
            {
                var node = new DrawingGroup { Transform = transform }; node.Children.Add(image); Group.Children.Add(node); return node;
            }
            Add(Far, far); Add(Near, near); TorsoLayer = Add(Torso, torso); HeadLayer = Add(Head, head);
        }
        public void Load(Rect canvas, double split)
        {
            Far.Rect = Near.Rect = Head.Rect = Torso.Rect = canvas;
            var upper = new RectangleGeometry(new Rect(0, 0, canvas.Width, split + 2)); upper.Freeze();
            var lower = new RectangleGeometry(new Rect(0, split - 2, canvas.Width, canvas.Height - split + 2)); lower.Freeze();
            HeadLayer.ClipGeometry = upper; TorsoLayer.ClipGeometry = lower;
        }
        public void Clear() { Far.ImageSource = Near.ImageSource = Head.ImageSource = Torso.ImageSource = null; }
    }

    public GroundedRigVisual()
    {
        IsHitTestVisible = false; Focusable = false;
        _layers = new(_farTransform, _nearTransform, _bodyTransform, _headTransform);
        _walking.Children.Add(_layers.Group); _walking.Transform = _displayTransform;
        _rest.Children.Add(_restDrawing); _rest.Transform = _restDisplayTransform;
        _drawing.Children.Add(_walking); _drawing.Children.Add(_rest);
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
    public Matrix HeadMatrix => _headTransform.Matrix;
    public Matrix DisplayMatrix => _displayTransform.Matrix;
    public double TurnWeight => _viewBlend.TurnWeight;
    public double FacingRightWeight => _facingBlend.RightWeight;
    public double RestWeight { get; private set; }

    public void SetPresentation(CharacterPresentation? presentation)
    {
        if (ReferenceEquals(_presentation, presentation)) return;
        _presentation = presentation; _rig = presentation?.Definition.WalkRig;
        PoseUpdateCount = 0; Pose = default; _viewBlend.Reset(); _facingBlend.Reset(); RestWeight = 0;
        _layers.Clear(); _restDrawing.ImageSource = null;
        _restFrame = _restSnapshot = null; _restBlend = null;
        _bodyBlend = _nearBlend = _farBlend = null;
        if (_rig is not null && presentation is not null)
        {
            var definition = presentation.Definition;
            var rect = new Rect(0, 0, definition.FrameWidth, definition.FrameHeight);
            var split = SplitY(definition);
            _layers.Load(rect, split);
            _bodyBlend = new(presentation.Frames[_rig.Side.BodyPath], presentation.Frames[_rig.Turn.BodyPath],
                presentation.Frames[definition.GetFrames(PetState.Idle)[0]]);
            _nearBlend = new(presentation.Frames[_rig.Side.NearFootPath], presentation.Frames[_rig.Turn.NearFootPath]);
            _farBlend = new(presentation.Frames[_rig.Side.FarFootPath], presentation.Frames[_rig.Turn.FarFootPath]);
            _restDrawing.Rect = rect;
            SetRestFrame(presentation.Frames[definition.GetFrames(PetState.Idle)[0]], definition.FrontFacingRest);
        }
        _nearTransform.Matrix = _farTransform.Matrix = _headTransform.Matrix = _bodyTransform.Matrix = Matrix.Identity;
        _walking.Opacity = 1; _rest.Opacity = 0;
        UpdateDisplayMatrix(); InvalidateVisual();
    }
    public void SetRestFrame(ImageSource source, bool unmirrored, bool? restFacingRight = null)
    {
        if (source is not BitmapSource bitmap) throw new ArgumentException("A rig resting frame must be a bitmap.", nameof(source));
        if (!ReferenceEquals(_restFrame, source))
        {
            _restFrame = bitmap; _restBlend = null;
            if (RestWeight == 0) _restDrawing.ImageSource = source;
        }
        if (_unmirroredRest == unmirrored && _restFacingRight == restFacingRight) return;
        _restFacingRight = restFacingRight;
        _unmirroredRest = unmirrored; _restSnapshot = null; _restBlend = null; UpdateDisplayMatrix();
    }
    private static double SplitY(CharacterDefinition definition) => Math.Clamp(definition.AnchorY - definition.FrameHeight * 0.2,
        definition.FrameHeight * 0.55, definition.FrameHeight * 0.85);
    public bool UpdatePose(WalkView view, double phase, double settleProgress, bool facingRight, double elapsedSeconds = 1d / 60,
        bool reducedMotion = false, double entryWeight = 1)
    {
        if (_rig is null || _presentation?.Definition.WalkCycleDistancePx is not { } distance) return false;
        var turn = _viewBlend.Update(view, elapsedSeconds, reducedMotion);
        var right = _facingBlend.Update(facingRight, elapsedSeconds, reducedMotion);
        var bodySource = _bodyBlend!.At(turn, facingRight ? 1 - right : right, pivotMirrored: !facingRight);
        var nearSource = _nearBlend!.At(turn); var farSource = _farBlend!.At(turn);
        if (!ReferenceEquals(_layers.Head.ImageSource, bodySource)) _layers.Head.ImageSource = _layers.Torso.ImageSource = bodySource;
        if (!ReferenceEquals(_layers.Near.ImageSource, nearSource)) _layers.Near.ImageSource = nearSource;
        if (!ReferenceEquals(_layers.Far.ImageSource, farSource)) _layers.Far.ImageSource = farSource;
        var pose = GroundedGait.Sample(phase, distance, _rig.FootLiftPx);
        if (settleProgress > 0) pose = GroundedGait.Settle(pose, settleProgress);
        var weight = reducedMotion || !double.IsFinite(entryWeight) ? 1 : Math.Clamp(entryWeight, 0, 1);
        pose = pose with
        {
            FootA = pose.FootA with { Lift = pose.FootA.Lift * weight },
            FootB = pose.FootB with { Lift = pose.FootB.Lift * weight },
            BodyBob = pose.BodyBob * weight
        };
        Pose = pose;
        var joints = RigSecondaryMotion.Sample(phase, settleProgress, reducedMotion);
        joints = joints with
        {
            HeadAngle = joints.HeadAngle * weight, TorsoAngle = joints.TorsoAngle * weight,
            HeadLagY = joints.HeadLagY * weight, NearFootRoll = joints.NearFootRoll * weight,
            FarFootRoll = joints.FarFootRoll * weight
        };
        var definition = _presentation.Definition;
        Matrix Foot(GaitFootPose foot, double roll)
        {
            var matrix = Matrix.Identity;
            matrix.RotateAt(roll, definition.FrameWidth / 2d, definition.AnchorY);
            matrix.Translate(foot.X, -foot.Lift);
            return matrix;
        }
        _nearTransform.Matrix = Foot(pose.FootA, joints.NearFootRoll); _farTransform.Matrix = Foot(pose.FootB, joints.FarFootRoll);
        var split = SplitY(definition);
        var body = Matrix.Identity; body.RotateAt(joints.TorsoAngle, definition.FrameWidth / 2d, split);
        body.Translate(0, pose.BodyBob); _bodyTransform.Matrix = body;
        var head = Matrix.Identity; head.RotateAt(joints.HeadAngle, definition.FrameWidth / 2d, split);
        head.Translate(0, pose.BodyBob + joints.HeadLagY); _headTransform.Matrix = head;
        var rest = double.IsFinite(settleProgress) ? Math.Clamp((settleProgress - 0.65) / 0.35, 0, 1) : 0;
        RestWeight = reducedMotion ? 0 : rest * rest * (3 - 2 * rest);
        if (_facingRight != facingRight)
        {
            _facingRight = facingRight; _restSnapshot = null; _restBlend = null; UpdateDisplayMatrix();
        }
        UpdateRestBlend();
        PoseUpdateCount++;
        return true;
    }

    private void UpdateRestBlend()
    {
        if (RestWeight == 0 || _restFrame is null)
        {
            _walking.Opacity = 1; _rest.Opacity = 0;
            _restSnapshot = null; _restBlend = null; _restDrawing.ImageSource = _restFrame;
            return;
        }
        _walking.Opacity = 0; _rest.Opacity = 1;
        if (RestWeight == 1)
        {
            _restDrawing.ImageSource = _restFrame;
            _restSnapshot = null; _restBlend = null;
            return;
        }
        if (_restSnapshot is null)
        {
            var frame = _presentation!.Definition;
            using (var context = _restCapture.RenderOpen())
            {
                // Capture unscaled parts in the resting layer's coordinate system. Its final
                // display transform still owns scaling and any authored resting-frame flip.
                var flipSnapshot = _facingRight != RestFacesRight;
                if (flipSnapshot)
                    context.PushTransform(new MatrixTransform(-1, 0, 0, 1, frame.FrameWidth, 0));
                context.DrawDrawing(_layers.Group);
                if (flipSnapshot) context.Pop();
            }
            var snapshot = new RenderTargetBitmap(frame.FrameWidth, frame.FrameHeight, 96, 96, PixelFormats.Pbgra32);
            snapshot.Render(_restCapture); snapshot.Freeze(); _restSnapshot = snapshot;
        }
        _restBlend ??= new RigTextureBlend(_restSnapshot, _restFrame);
        _restDrawing.ImageSource = _restBlend.At(RestWeight);
    }
    private void UpdateDisplayMatrix()
    {
        if (_presentation is null) { _displayTransform.Matrix = _restDisplayTransform.Matrix = Matrix.Identity; return; }
        var frame = _presentation.Definition;
        var width = ActualWidth > 0 ? ActualWidth : double.IsFinite(Width) && Width > 0 ? Width : frame.FrameWidth;
        var height = ActualHeight > 0 ? ActualHeight : double.IsFinite(Height) && Height > 0 ? Height : frame.FrameHeight;
        var scale = Math.Min(width / frame.FrameWidth, height / frame.FrameHeight);
        var x = (width - frame.FrameWidth * scale) / 2; var y = (height - frame.FrameHeight * scale) / 2;
        var normal = new Matrix(scale, 0, 0, scale, x, y);
        var mirrored = new Matrix(-scale, 0, 0, scale, x + frame.FrameWidth * scale, y);
        _displayTransform.Matrix = _facingRight ? normal : mirrored;
        _restDisplayTransform.Matrix = RestFacesRight ? normal : mirrored;
    }
    protected override void OnRender(DrawingContext drawingContext) { base.OnRender(drawingContext); drawingContext.DrawDrawing(_drawing); }
}
