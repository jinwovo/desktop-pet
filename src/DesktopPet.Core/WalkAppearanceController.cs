namespace DesktopPet.Core;

public enum WalkView { Front, Turn, Side }

/// <summary>
/// Coordinates pose changes and gait without changing the physical brain. Distance-aware skins
/// advance their planted-foot cycle from measured sprite-space travel; legacy skins retain time.
/// </summary>
public sealed class WalkAppearanceController
{
    private enum Stage { Enter, Side, GlanceTurn, ReverseTurn }
    private const double SettleDurationMs = 180;
    private const double EntryDurationMs = 90;
    private readonly Random _random;
    private bool _walking;
    private bool _requestedFacingRight = true;
    private Stage _stage;
    private double _remainingMs;
    private bool _firstGlance = true;
    private WalkView _stageView;
    private bool _hasMoved;
    private bool _slowing;
    private double _previousSpeed;
    private bool _distanceDriven;
    private bool _hasDistancePose;
    private double _settleElapsedMs;
    private double _entryStartWeight, _entryElapsedMs;

    public WalkAppearanceController(Random? random = null) => _random = random ?? new Random();

    public WalkView View { get; private set; } = WalkView.Front;
    public bool FacingRight { get; private set; } = true;
    public double AnimationElapsedMs { get; private set; }
    public double GaitPhase { get; private set; }
    public bool IsSettling { get; private set; }
    public double SettleProgress { get; private set; } = 1;
    /// <summary>Only vertical lift and secondary motion ease in; horizontal contact travel stays exact.</summary>
    public double EntryWeight { get; private set; } = 1;
    public bool DisplayWalkPose => _walking || IsSettling;
    public bool UsesDistanceGait => _distanceDriven;

    public void Reset(bool facingRight = true)
    {
        _walking = false;
        _requestedFacingRight = FacingRight = facingRight;
        _remainingMs = 0;
        _firstGlance = true;
        _hasMoved = _slowing = false;
        _previousSpeed = 0;
        _distanceDriven = false;
        _hasDistancePose = false;
        _settleElapsedMs = 0;
        IsSettling = false;
        SettleProgress = 1;
        EntryWeight = 1;
        _entryStartWeight = _entryElapsedMs = 0;
        AnimationElapsedMs = GaitPhase = 0;
        View = WalkView.Front;
    }

    /// <summary>
    /// distanceDeltaPx is measured signed travel in unscaled sprite pixels, including the last
    /// walking frame before a rest state. A valid cycle distance opts in to distance-driven gait.
    /// Gaze/settling use bounded real time and never manufacture foot travel when distance is zero.
    /// </summary>
    public void Update(PetState state, bool facingRight, double horizontalVelocityPxPerSec,
        double nominalWalkSpeedPxPerSec, double elapsedSeconds, double? targetWalkSpeedPxPerSec = null,
        double? distanceDeltaPx = null, double? walkCycleDistancePx = null)
    {
        var elapsedMs = double.IsFinite(elapsedSeconds) && elapsedSeconds > 0 ? Math.Min(elapsedSeconds, 0.25) * 1000 : 0;
        var validCycle = walkCycleDistancePx is double cycle && double.IsFinite(cycle) && cycle > 0;
        if (validCycle && distanceDeltaPx.HasValue) _distanceDriven = true;
        var speed = double.IsFinite(horizontalVelocityPxPerSec) ? Math.Abs(horizontalVelocityPxPerSec) : 0;
        var movementFacingRight = _distanceDriven && distanceDeltaPx is double dx && double.IsFinite(dx) && Math.Abs(dx) > 0.000001
            ? dx > 0 : speed > 0.5 ? horizontalVelocityPxPerSec > 0 : facingRight;

        if (state != PetState.Walk)
        {
            if (!_distanceDriven)
            {
                Reset(facingRight);
                return;
            }
            var resting = state is PetState.Idle or PetState.Sit or PetState.LookAtCursor or PetState.Sleep;
            if (_walking && validCycle && resting)
                AdvanceDistance(distanceDeltaPx, walkCycleDistancePx!.Value, nominalWalkSpeedPxPerSec);

            if (_walking && resting)
            {
                _walking = false;
                IsSettling = true;
                _settleElapsedMs = 0;
                SettleProgress = 0;
                // Keep the last angle and foot positions. Settle artwork lowers the swing foot
                // without moving the foot that is already planted on the floor.
                return;
            }
            _walking = false;
            if (IsSettling && resting && SettleProgress < 1)
            {
                _settleElapsedMs += elapsedMs;
                SettleProgress = Math.Clamp(_settleElapsedMs / SettleDurationMs, 0, 1);
                // Present the exact final rest blend once before handing back to raster animation.
                // Otherwise a refresh interval can skip progress=1 and leave a small visible cut.
                return;
            }
            IsSettling = false;
            SettleProgress = 1;
            View = WalkView.Front;
            _requestedFacingRight = FacingRight = facingRight;
            return;
        }

        if (!_walking)
        {
            _entryStartWeight = _distanceDriven
                ? IsSettling ? EntryWeight * (1 - SettleProgress * SettleProgress * (3 - 2 * SettleProgress)) : 0
                : 1;
            _entryElapsedMs = 0;
            EntryWeight = _entryStartWeight;
            if (_distanceDriven && _hasDistancePose && FacingRight != movementFacingRight)
                GaitPhase = GroundedGait.ReversePhase(GaitPhase);
            _walking = true;
            IsSettling = false;
            SettleProgress = 0;
            _settleElapsedMs = 0;
            _requestedFacingRight = FacingRight = movementFacingRight;
            _firstGlance = true;
            _hasMoved = _slowing = false;
            _previousSpeed = 0;
            SetStage(Stage.Enter, WalkView.Turn, 180);
            if (_distanceDriven && validCycle)
            {
                AdvanceDistance(distanceDeltaPx, walkCycleDistancePx!.Value, nominalWalkSpeedPxPerSec);
                _hasDistancePose = true;
            }
        }
        else
        {
            // Advance in the previous display direction before reflecting a reversal. This
            // preserves world foot positions even when an edge frame travels right but ends
            // with the brain already facing left, or when the first actual leftward step arrives.
            if (_distanceDriven && validCycle)
            {
                AdvanceDistance(distanceDeltaPx, walkCycleDistancePx!.Value, nominalWalkSpeedPxPerSec);
                _hasDistancePose = true;
            }
            if (_requestedFacingRight != movementFacingRight)
            {
                if (_distanceDriven) GaitPhase = GroundedGait.ReversePhase(GaitPhase);
                _requestedFacingRight = FacingRight = movementFacingRight;
                SetStage(Stage.ReverseTurn, WalkView.Turn, 250);
            }
        }

        if (elapsedMs <= 0) return;
        if (_distanceDriven)
        {
            _entryElapsedMs = Math.Min(EntryDurationMs, _entryElapsedMs + elapsedMs);
            var t = _entryElapsedMs / EntryDurationMs;
            EntryWeight = _entryStartWeight + (1 - _entryStartWeight) * t * t * (3 - 2 * t);
        }
        View = _stageView;
        if (!_distanceDriven)
        {
            var rate = double.IsFinite(horizontalVelocityPxPerSec) && double.IsFinite(nominalWalkSpeedPxPerSec) && nominalWalkSpeedPxPerSec > 0
                ? Math.Clamp(speed / nominalWalkSpeedPxPerSec, 0, 16) : 0;
            AnimationElapsedMs += elapsedMs * rate;
        }

        while (elapsedMs >= _remainingMs)
        {
            elapsedMs -= _remainingMs;
            AdvanceStage();
        }
        _remainingMs -= elapsedMs;

        var target = targetWalkSpeedPxPerSec ?? nominalWalkSpeedPxPerSec;
        var movementRatio = double.IsFinite(target) && target > 0 ? speed / target : 0;
        if (movementRatio >= 0.7) _hasMoved = true;
        if (_hasMoved && speed < _previousSpeed - 0.001 && movementRatio < 0.65) _slowing = true;
        if (speed > _previousSpeed + 0.001 || movementRatio >= 0.75) _slowing = false;
        _previousSpeed = speed;
        if (_slowing && movementRatio < 0.65) View = WalkView.Turn;
    }

    private void AdvanceDistance(double? distanceDeltaPx, double cycleDistancePx, double nominalSpeed)
    {
        if (distanceDeltaPx is not double delta || !double.IsFinite(delta)) return;
        var phaseDelta = delta * (FacingRight ? 1 : -1) / cycleDistancePx;
        if (double.IsFinite(phaseDelta)) GaitPhase = GroundedGait.NormalizePhase(GaitPhase + phaseDelta);
        var elapsed = double.IsFinite(nominalSpeed) && nominalSpeed > 0 ? Math.Abs(delta) / nominalSpeed * 1000 : 0;
        if (double.IsFinite(elapsed) && double.IsFinite(AnimationElapsedMs + elapsed)) AnimationElapsedMs += elapsed;
    }

    private void AdvanceStage()
    {
        switch (_stage)
        {
            case Stage.Enter:
            case Stage.GlanceTurn:
            case Stage.ReverseTurn:
                SetStage(Stage.Side, WalkView.Side, _distanceDriven ? double.PositiveInfinity :
                    _firstGlance ? 1600 + _random.NextDouble() * 800 : 3200 + _random.NextDouble() * 2000);
                break;
            case Stage.Side:
                _firstGlance = false;
                SetStage(Stage.GlanceTurn, WalkView.Turn, 600 + _random.NextDouble() * 250);
                break;
        }
    }

    private void SetStage(Stage stage, WalkView view, double durationMs)
    {
        _stage = stage;
        _stageView = View = view;
        _remainingMs = durationMs;
    }
}
