using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopPet.Core;

namespace DesktopPet.App;

/// <summary>Exercises the real brain and sprites without scanning or capturing desktop windows.</summary>
public partial class MotionPreviewWindow : Window
{
    private readonly CharacterPresentation _presentation;
    private double _speedMultiplier;
    private readonly double _scale;
    private readonly DragVelocityTracker _velocity = new();
    private PetBrain _brain = null!;
    private PetPoint _cursor = new(-10000, -10000);
    private long _lastTimestamp;
    public bool IsAnimating { get; private set; }
    private bool _dragging;
    private double _walkElapsedMs;
    private double _idleAppearanceElapsedMs;
    private double _lastGaitPositionX = double.NaN;
    private bool _liveRigActive;
    private TimeSpan? _lastRenderingTime;
    private readonly WalkAppearanceController _walkAppearance = new(new Random(7));
    private readonly CursorGazeController _cursorGaze = new();
    private readonly PoseTransitionPresenter _poseTransition;
    private readonly FramePacer _framePacer = new();
    private bool _reducedMotion;
    private bool _closed;
    private bool _playbackPaused;
    private PetState? _requestedReaction;
    private PetState _lastPoseState = PetState.Idle;
    private readonly Func<string?> _chooseFramePath;
    private readonly Func<BitmapSource, string, CancellationToken, Task> _frameWriter;
    private CancellationTokenSource? _frameSaveCancellation;
    private string _frameSaveStatusKey = "";
    public Task FrameSaveOperation { get; private set; } = Task.CompletedTask;

    public MotionPreviewWindow(CharacterPresentation presentation, double speedMultiplier, int maximumFps = 60, bool reducedMotion = false,
        Func<string?>? chooseFramePath = null, Func<BitmapSource, string, CancellationToken, Task>? frameWriter = null)
    {
        _presentation = presentation;
        _chooseFramePath = chooseFramePath ?? ChooseFramePath;
        _frameWriter = frameWriter ?? ((source, path, token) => Task.Run(() => MotionFramePngWriter.Save(source, path, token), token));
        _speedMultiplier = speedMultiplier;
        _scale = 96.0 / Math.Max(presentation.Definition.FrameWidth, presentation.Definition.FrameHeight);
        InitializeComponent();
        _poseTransition = new PoseTransitionPresenter(Actor, PreviousActor, ActorFlip);
        SetRenderOptions(maximumFps, reducedMotion);
        MotionRig.SetPresentation(presentation);
        Title = $"DesktopPet 움직임 시험 · {presentation.Definition.Name}";
        Actor.Width = presentation.Definition.FrameWidth * _scale;
        Actor.Height = presentation.Definition.FrameHeight * _scale;
        PreviousActor.Width = Actor.Width;
        PreviousActor.Height = Actor.Height;
        MotionRig.Width = ActorInput.Width = Actor.Width;
        MotionRig.Height = ActorInput.Height = Actor.Height;
        InitializeFrameGuides();
        Loaded += (_, _) =>
        {
            ResetScene();
            UpdateAnimationState();
        };
        IsVisibleChanged += (_, _) => UpdateAnimationState();
        StateChanged += (_, _) => UpdateAnimationState();
        Closed += (_, _) =>
        {
            _closed = true;
            _frameSaveCancellation?.Cancel();
            UiText.LanguageChanged -= RefreshLanguage;
            _poseTransition.Reset();
            MotionRig.SetPresentation(null);
            CompositionTarget.Rendering -= OnRendering;
            IsAnimating = false;
            _dragging = false;
            ActorInput.ReleaseMouseCapture();
            UpdatePlaybackControls();
        };
        UiText.Attach(this);
        Loaded += (_, _) => RefreshLanguage();
        UiText.LanguageChanged += RefreshLanguage;
        RefreshLanguage();
    }

    private void UpdateAnimationState()
    {
        UpdatePlaybackControls();
        var active = CanInspect && !_playbackPaused;
        if (active == IsAnimating) return;
        if (active)
        {
            _lastTimestamp = Stopwatch.GetTimestamp(); _lastRenderingTime = null; _framePacer.Reset();
            CompositionTarget.Rendering += OnRendering;
        }
        else
        {
            CompositionTarget.Rendering -= OnRendering;
            if (!_playbackPaused) _poseTransition.Reset();
        }
        IsAnimating = active;
    }

    private bool CanInspect => !_closed && _brain is not null && IsLoaded && IsVisible && WindowState != WindowState.Minimized;

    private void UpdatePlaybackControls()
    {
        PausePlaybackButton.Content = UiText.Get(_playbackPaused ? "계속" : "일시정지");
        PausePlaybackButton.IsEnabled = CanInspect;
        NextFrameButton.IsEnabled = CanInspect && _playbackPaused;
        CryReactionButton.IsEnabled = AngryReactionButton.IsEnabled = CanInspect;
        SaveSceneButton.IsEnabled = CanInspect && _frameSaveCancellation is null;
        FrameGuidesToggle.IsEnabled = CanInspect;
        if (CanInspect) UpdateFrameGuides();
    }

    private void InitializeFrameGuides()
    {
        FrameGuides.Width = Actor.Width; FrameGuides.Height = Actor.Height;
        FrameBoundary.Width = Math.Max(0, Actor.Width - 1); FrameBoundary.Height = Math.Max(0, Actor.Height - 1);
        Canvas.SetLeft(FrameBoundary, 0.5); Canvas.SetTop(FrameBoundary, 0.5);
        var x = _presentation.Definition.AnchorX * _scale;
        var y = _presentation.Definition.AnchorY * _scale;
        FrameBaseline.X1 = 0; FrameBaseline.X2 = Actor.Width; FrameBaseline.Y1 = FrameBaseline.Y2 = y;
        AnchorHorizontal.X1 = x - 4; AnchorHorizontal.X2 = x + 4; AnchorHorizontal.Y1 = AnchorHorizontal.Y2 = y;
        AnchorVertical.X1 = AnchorVertical.X2 = x; AnchorVertical.Y1 = y - 4; AnchorVertical.Y2 = y + 4;
    }

    private void FrameGuides_Changed(object sender, RoutedEventArgs e)
    {
        if (CanInspect) UpdateFrameGuides();
    }

    private void UpdateFrameGuides()
    {
        FrameGuides.Visibility = FrameGuidesToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (FrameGuidesToggle.IsChecked != true) return;
        Canvas.SetLeft(FrameGuides, _brain.X); Canvas.SetTop(FrameGuides, _brain.Y);
    }

    private void PausePlayback_Click(object sender, RoutedEventArgs e)
    {
        if (!CanInspect) return;
        _playbackPaused = !_playbackPaused;
        _poseTransition.SetPaused(_playbackPaused);
        UpdateAnimationState();
        UpdateStateText();
    }

    private void NextFrame_Click(object sender, RoutedEventArgs e)
    {
        if (!CanInspect || !_playbackPaused) return;
        var dt = 1d / _framePacer.MaximumFps;
        _brain.Tick(dt, _cursor);
        _poseTransition.AdvancePaused(TimeSpan.FromSeconds(dt));
        RenderActor(dt);
    }

    public void SetRenderOptions(int maximumFps, bool reducedMotion)
    {
        _framePacer.MaximumFps = maximumFps;
        _reducedMotion = reducedMotion;
        if (!_playbackPaused || reducedMotion) _poseTransition.Reset();
        if (_brain is not null) _brain.HopBetweenPlatforms = !reducedMotion;
    }

    private void ResetScene()
    {
        _requestedReaction = null;
        _poseTransition.Reset();
        _lastPoseState = PetState.Idle;
        _dragging = false;
        ActorInput.ReleaseMouseCapture();
        var ground = Scene.ActualHeight - 12;
        var lower = new Platform(230, 405, ground - 58);
        var upper = new Platform(465, Scene.ActualWidth - 16, ground - 110);
        var layout = new PlatformLayout(new[] { new Platform(0, Scene.ActualWidth, ground) });
        layout.UpdateWindowPlatforms(new[] { lower, upper });
        PlaceSurface(FloorVisual, new Platform(0, Scene.ActualWidth, ground));
        PlaceSurface(LowerLedgeVisual, lower);
        PlaceSurface(UpperLedgeVisual, upper);
        var definition = _presentation.Definition;
        _brain = new PetBrain(layout, Actor.Width, definition.AnchorY * _scale, 40, ground - definition.AnchorY * _scale, new Random(42))
        {
            SleepEnabled = false,
            WalkSpeedPxPerSec = definition.WalkSpeedPxPerSec * _speedMultiplier,
        };
        if (_reducedMotion) _brain.HopBetweenPlatforms = false;
        _walkElapsedMs = 0;
        _idleAppearanceElapsedMs = 0;
        _walkAppearance.Reset();
        _cursorGaze.Reset();
        _lastGaitPositionX = _brain.X;
        RenderActor(0);
    }

    private static void PlaceSurface(System.Windows.Shapes.Rectangle rectangle, Platform platform)
    {
        rectangle.Width = platform.Right - platform.Left;
        Canvas.SetLeft(rectangle, platform.Left);
        Canvas.SetTop(rectangle, platform.GroundY);
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (!IsAnimating) return;
        if (e is RenderingEventArgs rendering)
        {
            if (_lastRenderingTime == rendering.RenderingTime) return;
            _lastRenderingTime = rendering.RenderingTime;
            if (!_framePacer.ShouldRender(rendering.RenderingTime.TotalMilliseconds)) return;
        }
        var now = Stopwatch.GetTimestamp();
        var dt = Math.Clamp(Stopwatch.GetElapsedTime(_lastTimestamp, now).TotalSeconds, 0, 0.1);
        _lastTimestamp = now;
        _brain.Tick(dt, _cursor);
        RenderActor(dt);
    }

    private void RenderActor(double dt)
    {
        var definition = _presentation.Definition;
        var distanceDeltaPx = double.IsFinite(_lastGaitPositionX) ? (_brain.X - _lastGaitPositionX) / _scale : 0;
        _lastGaitPositionX = _brain.X;
        _walkAppearance.Update(_brain.State, _brain.FacingRight, _brain.HorizontalVelocityPxPerSec,
            definition.WalkSpeedPxPerSec, dt, _brain.WalkSpeedPxPerSec,
            distanceDeltaPx, definition.WalkCycleDistancePx);
        _walkElapsedMs = _walkAppearance.AnimationElapsedMs;
        Canvas.SetLeft(Actor, _brain.X);
        Canvas.SetTop(Actor, _brain.Y);
        Canvas.SetLeft(PreviousActor, _brain.X);
        Canvas.SetTop(PreviousActor, _brain.Y);
        Canvas.SetLeft(MotionRig, _brain.X);
        Canvas.SetTop(MotionRig, _brain.Y);
        Canvas.SetLeft(ActorInput, _brain.X);
        Canvas.SetTop(ActorInput, _brain.Y);
        if (FrameGuidesToggle.IsChecked == true) UpdateFrameGuides();
        var right = _cursorGaze.Update(_cursor.X, _brain.X + Actor.Width / 2, 8 * _scale);
        var displayWalkPose = _walkAppearance.DisplayWalkPose;
        var directional = !displayWalkPose && _brain.State == PetState.LookAtCursor &&
            (right ? definition.LookRightFrames : definition.LookLeftFrames).Count > 0;
        var walkingDirection = displayWalkPose && (definition.HasDirectionalWalk || definition.WalkCycleDistancePx is not null);
        var frontWalk = walkingDirection && definition.UsesFrontalWalkFrames(_walkAppearance.View);
        var facingRight = walkingDirection ? _walkAppearance.FacingRight : _brain.FacingRight;
        ActorFlip.ScaleX = directional || frontWalk || !displayWalkPose && definition.UsesFrontalRestFrames(_brain.State) || facingRight ? 1 : -1;
        var frames = displayWalkPose ? definition.GetWalkFrames(_walkAppearance.View)
            : _brain.State == PetState.LookAtCursor ? definition.GetLookFrames(right) : definition.GetFrames(_brain.State);
        var elapsed = displayWalkPose ? _walkElapsedMs : _brain.StateElapsedMs;
        if (_brain.State == PetState.Idle && definition.FrameDurationsByState.ContainsKey(PetState.Idle))
        {
            _idleAppearanceElapsedMs += dt * 1000;
            elapsed = _idleAppearanceElapsedMs;
        }
        var framePath = displayWalkPose && definition.WalkCycleDistancePx is not null
            ? definition.GetWalkFramePath(_walkAppearance.View, _walkAppearance.GaitPhase,
                _walkAppearance.IsSettling ? _walkAppearance.SettleProgress : 0)
            : definition.GetFramePath(displayWalkPose ? PetState.Walk : _brain.State, frames, elapsed);
        if (displayWalkPose && MotionRig.HasRig)
        {
            if (!_liveRigActive)
            {
                _poseTransition.Reset();
                Actor.Visibility = Visibility.Hidden;
                MotionRig.Visibility = Visibility.Visible;
                _liveRigActive = true;
            }
            if (_walkAppearance.IsSettling)
            {
                var rest = definition.GetFrames(_brain.State);
                var restElapsed = _brain.State == PetState.Idle && definition.FrameDurationsByState.ContainsKey(PetState.Idle)
                    ? _idleAppearanceElapsedMs : _brain.StateElapsedMs;
                MotionRig.SetRestFrame(_presentation.Frames[definition.GetFramePath(_brain.State, rest, restElapsed)], definition.UsesFrontalRestFrames(_brain.State));
            }
            MotionRig.UpdatePose(_walkAppearance.View, _walkAppearance.GaitPhase,
                _walkAppearance.IsSettling ? _walkAppearance.SettleProgress : 0, _walkAppearance.FacingRight, dt, _reducedMotion);
        }
        else
        {
            if (_liveRigActive)
            {
                _poseTransition.Reset();
                Actor.Visibility = Visibility.Visible;
                MotionRig.Visibility = Visibility.Collapsed;
                _liveRigActive = false;
            }
            _poseTransition.SetFrame(_presentation.Frames[framePath], ActorFlip.ScaleX, frames,
                !_reducedMotion && (definition.WalkCycleDistancePx is null || !displayWalkPose) &&
                (_brain.State != PetState.Walk || _lastPoseState == PetState.Walk));
        }
        _lastPoseState = _brain.State;
        UpdateStateText();
    }

    private void RefreshLanguage()
    {
        if (_closed) return;
        Title = UiText.Format("DesktopPet 움직임 시험 · {0}", _presentation.Definition.Name);
        UpdatePlaybackControls();
        UpdateStateText();
        RefreshFrameSaveStatus();
        FrameGuidesToggle.Content = UiText.Get("기준선");
        FrameGuidesToggle.ToolTip = UiText.Get("점선은 프레임, 십자는 선언된 기준점입니다.");
    }

    private string? ChooseFramePath()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = UiText.Get("장면 PNG 저장"), Filter = "PNG (*.png)|*.png",
            FileName = "scene.png", DefaultExt = ".png", AddExtension = true, OverwritePrompt = false,
        };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private async void SaveScene_Click(object sender, RoutedEventArgs e)
    {
        if (!CanInspect || _frameSaveCancellation is not null) return;
        try
        {
            // This canvas contains only the controlled pet and platforms, never the desktop.
            // Capture before the modal picker so it saves exactly the clicked frame.
            var width = (int)Math.Ceiling(Scene.ActualWidth);
            var height = (int)Math.Ceiling(Scene.ActualHeight);
            if (width <= 0 || height <= 0) return;
            var snapshot = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            snapshot.Render(Scene);
            snapshot.Freeze();
            var path = _chooseFramePath();
            if (path is null || !CanInspect || _frameSaveCancellation is not null) return;
            FrameSaveOperation = SaveSceneAsync(snapshot, path);
            await FrameSaveOperation;
        }
        catch (Exception ex) when (IsFrameSaveError(ex))
        {
            if (!_closed) SetFrameSaveStatus("장면을 저장하지 못했어요. 새 PNG 파일 이름을 선택해주세요.");
        }
    }

    private async Task SaveSceneAsync(BitmapSource snapshot, string path)
    {
        using var cancellation = new CancellationTokenSource();
        _frameSaveCancellation = cancellation;
        SetFrameSaveStatus("장면을 저장하는 중이에요.");
        UpdatePlaybackControls();
        try
        {
            await _frameWriter(snapshot, path, cancellation.Token);
            if (!_closed && !cancellation.IsCancellationRequested) SetFrameSaveStatus("장면 PNG를 저장했어요.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex) when (IsFrameSaveError(ex))
        {
            if (!_closed) SetFrameSaveStatus("장면을 저장하지 못했어요. 새 PNG 파일 이름을 선택해주세요.");
        }
        finally
        {
            _frameSaveCancellation = null;
            if (!_closed) UpdatePlaybackControls();
        }
    }

    private static bool IsFrameSaveError(Exception ex) => ex is IOException or UnauthorizedAccessException or ArgumentException
        or NotSupportedException or InvalidOperationException or System.Security.SecurityException;

    private void SetFrameSaveStatus(string key)
    {
        _frameSaveStatusKey = key;
        RefreshFrameSaveStatus();
    }

    private void RefreshFrameSaveStatus()
    {
        var label = _frameSaveStatusKey switch
        {
            "장면을 저장하는 중이에요." => "저장 중…",
            "장면 PNG를 저장했어요." => "PNG 저장됨",
            "장면을 저장하지 못했어요. 새 PNG 파일 이름을 선택해주세요." => "PNG 다시 저장",
            _ => "장면 PNG 저장",
        };
        SaveSceneButton.Content = UiText.Get(label);
        SaveSceneButton.ToolTip = UiText.Get(_frameSaveStatusKey.Length == 0 ? "현재 미리보기 장면만 저장해요." : _frameSaveStatusKey);
        System.Windows.Automation.AutomationProperties.SetHelpText(SaveSceneButton, (string)SaveSceneButton.ToolTip);
    }

    private void UpdateStateText()
    {
        if (_closed) return;
        var state = _brain?.State ?? PetState.Idle;
        StateText.Text = UiText.Get(_walkAppearance.IsSettling ? "발을 모으는 중" : state switch
        {
            PetState.Walk => _walkAppearance.View switch
            {
                WalkView.Front when _presentation.Definition.WalkFrontFrames.Count > 0 => "걷는 중 · 앞을 살펴요",
                WalkView.Turn when _presentation.Definition.WalkTurnFrames.Count > 0 => "걷는 중 · 고개를 돌려요",
                _ => "걷는 중 · 진행 방향을 봐요"
            },
            PetState.Falling => "공중",
            PetState.Dragged => "잡고 있는 중",
            PetState.Cry => "울기",
            PetState.Angry => "화내기",
            PetState.LookAtCursor => "바라보는 중",
            _ => "쉬는 중"
        });
        if (_requestedReaction is { } pending && state is PetState.Dragged or PetState.Falling)
            StateText.Text = UiText.Format("{0} · 착지 후 {1}", StateText.Text, UiText.Get(pending == PetState.Cry ? "울기" : "화내기"));
        else _requestedReaction = null;
        if (_playbackPaused) StateText.Text = UiText.Format("{0} · 일시정지", StateText.Text);
    }

    private void Actor_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var point = e.GetPosition(Scene);
        _cursor = new PetPoint(point.X, point.Y);
        _brain.BeginDrag(_cursor);
        _velocity.Reset(_cursor, Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
        _dragging = true;
        ActorInput.CaptureMouse();
        e.Handled = true;
    }

    private void Scene_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var point = e.GetPosition(Scene);
        _cursor = new PetPoint(point.X, point.Y);
        if (!_dragging) return;
        _velocity.Sample(_cursor, Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
        _brain.UpdateDrag(_cursor);
        RenderActor(0);
    }

    private void Scene_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_dragging) _cursor = new PetPoint(-10000, -10000);
    }

    private void Actor_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        var point = e.GetPosition(Scene);
        _velocity.Sample(new PetPoint(point.X, point.Y), Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
        _dragging = false;
        ActorInput.ReleaseMouseCapture();
        var velocity = _velocity.ReleaseVelocity;
        _brain.EndDrag(velocity.X, velocity.Y);
        if (_playbackPaused) RenderActor(0);
    }

    private void Actor_LostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        _brain.EndDrag();
        if (_playbackPaused && CanInspect) RenderActor(0);
    }

    public void SetSpeedMultiplier(double multiplier)
    {
        _speedMultiplier = double.IsFinite(multiplier) ? Math.Clamp(multiplier, 0.25, 3) : 1;
        if (_brain is not null) _brain.WalkSpeedPxPerSec = _presentation.Definition.WalkSpeedPxPerSec * _speedMultiplier;
    }

    private void CryReaction_Click(object sender, RoutedEventArgs e) => RequestReaction(PetState.Cry);
    private void AngryReaction_Click(object sender, RoutedEventArgs e) => RequestReaction(PetState.Angry);

    private void RequestReaction(PetState state)
    {
        if (!CanInspect) return;
        var deferred = _brain.State is PetState.Dragged or PetState.Falling;
        _brain.StartEmote(state);
        _requestedReaction = deferred ? state : null;
        RenderActor(0);
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e) => ResetScene();
    private void JumpButton_Click(object sender, RoutedEventArgs e)
    {
        _dragging = false;
        ActorInput.ReleaseMouseCapture();
        _brain.BeginDrag(new PetPoint(_brain.X, _brain.Y));
        _brain.EndDrag(220, -430);
        if (_playbackPaused) RenderActor(0);
    }

    private void DropButton_Click(object sender, RoutedEventArgs e)
    {
        _dragging = false;
        ActorInput.ReleaseMouseCapture();
        _brain.BeginDrag(new PetPoint(_brain.X, _brain.Y));
        _brain.UpdateDrag(new PetPoint(_brain.X, 8));
        _brain.EndDrag();
        if (_playbackPaused) RenderActor(0);
    }
}
