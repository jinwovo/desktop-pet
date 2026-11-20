namespace DesktopPet.Core;

/// <summary>
/// UI-agnostic pet state machine: owns position, facing direction and the Idle/Walk/Sit/
/// LookAtCursor/Dragged/Falling transitions. The UI layer (WPF window) just calls <see cref="Tick"/>
/// every frame and reads <see cref="X"/>/<see cref="Y"/>/<see cref="State"/>/<see cref="FacingRight"/>.
/// </summary>
public sealed class PetBrain
{
    private const double GravityPxPerSec2 = 1400;

    // How close the cursor must be, per axis, for the pet to notice it.
    private const double LookAtCursorRangeXPx = 170;
    private const double LookAtCursorRangeYPx = 190;

    /// <summary>Longest the pet will stare before losing interest and getting on with its day.</summary>
    private const double MaxLookDurationMs = 1700;

    /// <summary>After a stare ends, ignore the cursor this long so the pet doesn't re-latch instantly.</summary>
    private const double LookCooldownMs = 3500;

    /// <summary>Impact speed above which a landing counts as "that hurt" and starts a cry.</summary>
    private const double CryOnImpactSpeed = 900;

    /// <summary>How far past a platform edge the pet will look for somewhere to hop down to.</summary>
    private const double HopSearchDistancePx = 170;

    private const double HopHorizontalSpeedPxPerSec = 115;

    /// <summary>A little upward kick so the hop arcs instead of just walking off the edge.</summary>
    private const double HopUpwardSpeedPxPerSec = 200;
    private const double MaxHopHorizontalSpeedPxPerSec = 300;
    private const double WalkVelocityResponseSeconds = 0.10;
    private const double MaxSimulationStepSeconds = 1.0 / 120;
    private const int MaxSimulationStepsPerTick = 240;

    /// <summary>
    /// Launch speed for hopping *up* onto a higher ledge. Peak height is v²/2g, so this reaches
    /// roughly 65px — about two title bars, which is the useful range for stacked windows.
    /// </summary>
    private const double HopUpSpeedPxPerSec = 430;

    private const double MaxHopUpRisePx = 62;

    /// <summary>Chance of choosing to climb when both an up and a down route exist.</summary>
    private const double PreferClimbChance = 0.45;

    /// <summary>Caps how hard the pet can be flung, so a fast flick doesn't launch it off-screen.</summary>
    private const double MaxThrowSpeedPxPerSec = 1700;

    /// <summary>Fraction of horizontal speed kept when bouncing off a screen edge.</summary>
    private const double WallBounceRetention = 0.55;

    /// <summary>Per-second horizontal drag while airborne, so a throw settles instead of sliding forever.</summary>
    private const double AirDragPerSec = 0.62;

    private const double CryDurationMs = 2600;
    private const double AngryDurationMs = 2000;

    /// <summary>Pokes within this window count toward the same "stop poking me" streak.</summary>
    private const double PokeStreakWindowMs = 1200;
    private const int PokesToAnger = 3;

    /// <summary>Cursor movement beyond this counts as activity and resets the sleep timer.</summary>
    private const double CursorIdleToleragePx = 40;

    private readonly Random _rng;
    private CharacterBehaviorProfile _behaviorProfile = CharacterBehaviorProfile.Default;
    public CharacterBehaviorProfile BehaviorProfile
    {
        get => _behaviorProfile;
        set => _behaviorProfile = value ?? throw new ArgumentNullException(nameof(value));
    }
    private double _frameWidth;
    private double _groundOffset; // distance from the frame's top to the character's "feet" row (character.json anchor.y, scaled)

    private readonly PlatformLayout _layout;
    private double _fallVelocityY;
    private double _fallVelocityX;

    /// <summary>Set while airborne by choice, so a deliberate hop doesn't end in tears.</summary>
    private bool _isHopping;
    private double _dragOffsetX;
    private double _dragOffsetY;
    private double _stateElapsedMs;
    private double _nextStateDecisionAtMs;
    private double _walkTargetDirection;
    private double _walkVelocityX;
    private PetState? _walkStopDestination;
    private double _emoteEndsAtMs;
    private PetState? _queuedEmote;
    private double _totalElapsedMs;
    private double _lookCooldownUntilMs;
    private double _lastActivityMs;
    private PetPoint _lastCursorPos;
    private double _lastPokeAtMs = double.NegativeInfinity;
    private int _pokeStreak;
    private PetState _nagEmote = PetState.Angry;
    private double _gentleHoldUntilMs;

    /// <summary>When false, the pet turns around at platform edges instead of jumping.</summary>
    public bool HopBetweenPlatforms { get; set; } = true;

    /// <summary>When false, the pet never dozes off no matter how long it's left alone.</summary>
    public bool SleepEnabled { get; set; } = true;
    public bool CalmBehavior { get; set; }

    /// <summary>When true, random rest selection never picks Walk and an in-progress walk decelerates naturally.</summary>
    public bool RoamingPaused
    {
        get => _roamingPaused;
        set
        {
            if (_roamingPaused == value) return;
            _roamingPaused = value;
            if (value && State == PetState.Walk) _walkStopDestination = PetState.Idle;
        }
    }
    private bool _roamingPaused;

    public PetState State { get; private set; } = PetState.Idle;
    public double X { get; private set; }
    public double Y { get; private set; }
    public bool FacingRight { get; private set; } = true;
    public double StateElapsedMs => _stateElapsedMs;
    public double WalkSpeedPxPerSec { get; set; } = 60;
    public double HorizontalVelocityPxPerSec => State switch
    {
        PetState.Walk => _walkVelocityX,
        PetState.Falling => _fallVelocityX,
        _ => 0,
    };

    public PetBrain(PlatformLayout layout, double frameWidth, double groundOffset, double startX, double startY, Random? rng = null,
        CharacterBehaviorProfile? behaviorProfile = null)
    {
        _layout = layout;
        _frameWidth = frameWidth;
        _groundOffset = groundOffset;
        X = startX;
        Y = startY;
        _rng = rng ?? new Random();
        BehaviorProfile = behaviorProfile ?? CharacterBehaviorProfile.Default;
        ScheduleNextIdleDecision();
    }

    /// <summary>Call after switching characters or changing scale — these feed the floor/edge math.</summary>
    public void UpdateFrameSize(double frameWidth, double groundOffset)
    {
        X += (_frameWidth - frameWidth) / 2;
        Y += _groundOffset - groundOffset;
        _frameWidth = frameWidth;
        _groundOffset = groundOffset;
    }

    /// <summary>Explicit user recovery: land at a monitor's floor and discard old throw/walk momentum.</summary>
    public void PlaceOnDesktop(DesktopRect workArea)
    {
        if (!workArea.IsValid) throw new ArgumentOutOfRangeException(nameof(workArea));
        X = workArea.Left + (workArea.Width - _frameWidth) / 2;
        Y = workArea.Bottom - _groundOffset;
        _fallVelocityX = _fallVelocityY = _walkVelocityX = 0;
        _dragOffsetX = _dragOffsetY = 0;
        _isHopping = false;
        IsNagging = false;
        _queuedEmote = null;
        _gentleHoldUntilMs = 0;
        _pokeStreak = 0;
        _lastActivityMs = _totalElapsedMs;
        _lookCooldownUntilMs = _totalElapsedMs + LookCooldownMs;
        ChangeState(PetState.Idle);
    }

    public void Tick(double dtSeconds, PetPoint cursorScreenPos)
    {
        if (!double.IsFinite(dtSeconds) || dtSeconds <= 0) return;
        // Keep collision/state transitions consistent across refresh rates without an unbounded
        // catch-up loop when a caller supplies a large elapsed time after a pause.
        var steps = (int)Math.Min(MaxSimulationStepsPerTick, Math.Ceiling(dtSeconds / MaxSimulationStepSeconds));
        var step = dtSeconds / steps;
        for (var i = 0; i < steps; i++) TickStep(step, cursorScreenPos);
    }

    private void TickStep(double dtSeconds, PetPoint cursorScreenPos)
    {
        _stateElapsedMs += dtSeconds * 1000;
        _totalElapsedMs += dtSeconds * 1000;

        // Cursor movement means the user is around, which both resets the doze timer and wakes
        // a sleeping pet. Compared against a tolerance so tiny mouse jitter doesn't count.
        var cursorMoved =
            Math.Abs(cursorScreenPos.X - _lastCursorPos.X) + Math.Abs(cursorScreenPos.Y - _lastCursorPos.Y)
            > CursorIdleToleragePx;

        if (cursorMoved)
        {
            _lastCursorPos = cursorScreenPos;
            _lastActivityMs = _totalElapsedMs;

            if (State == PetState.Sleep)
            {
                ChangeState(PetState.Idle);
            }
        }

        if (State == PetState.Sleep)
        {
            if (!SleepEnabled)
            {
                ChangeState(PetState.Idle);
                _lastActivityMs = _totalElapsedMs;
                return;
            }
            TickSleep();
            return;
        }

        switch (State)
        {
            case PetState.Dragged:
                // Position is driven by UpdateDrag(); nothing to simulate here.
                break;

            case PetState.Falling:
                TickFalling(dtSeconds);
                break;

            case PetState.Walk:
                TickWalk(dtSeconds, cursorScreenPos);
                break;

            case PetState.Cry:
            case PetState.Angry:
                TickEmote();
                break;

            case PetState.Sit:
            case PetState.Idle:
            case PetState.LookAtCursor:
                TickIdleLike(dtSeconds, cursorScreenPos);
                break;
        }
    }

    /// <summary>Asleep: hold position, and keep falling if the ground disappears underneath.</summary>
    private void TickSleep()
    {
        var support = _layout.GetSupportPlatform(X + _frameWidth / 2, Y + _groundOffset);
        if (support is null)
        {
            BeginPlainFall();
            return;
        }

        Y = support.Value.GroundY - _groundOffset;
    }

    /// <summary>Emotes stay put on whatever they're standing on until their timer runs out.</summary>
    private void TickEmote()
    {
        var support = _layout.GetSupportPlatform(X + _frameWidth / 2, Y + _groundOffset);
        if (support is null)
        {
            BeginPlainFall();
            return;
        }

        Y = support.Value.GroundY - _groundOffset;

        // While nagging, the emote has no timer: it holds until the user acknowledges.
        if (!IsNagging && _stateElapsedMs >= _emoteEndsAtMs)
        {
            ChangeState(PetState.Idle);
        }
    }

    /// <summary>True while the pet is demanding attention for an unacknowledged notification.</summary>
    public bool IsNagging { get; private set; }

    /// <summary>
    /// Extra vertical offset (negative = up) that makes a nagging pet hop in place. Applied by the
    /// UI on top of <see cref="Y"/>; the brain's own Y stays on the ground so platform logic and
    /// hit-testing are unaffected.
    /// </summary>
    public double NagHopOffset =>
        IsNagging && State.IsEmote() ? -Math.Abs(Math.Sin(_stateElapsedMs / 150.0)) * 7.0 : 0.0;

    /// <summary>Plays an emote indefinitely until <see cref="StopNagging"/> is called.</summary>
    public void StartNagging(PetState emote)
    {
        if (!emote.IsEmote()) throw new ArgumentOutOfRangeException(nameof(emote));
        _nagEmote = emote;
        IsNagging = true;
        _lastActivityMs = _totalElapsedMs;
        if (State is not (PetState.Dragged or PetState.Falling)) StartEmote(emote);
    }

    public void StopNagging()
    {
        if (!IsNagging)
        {
            return;
        }

        IsNagging = false;
        if (State.IsEmote()) ChangeState(PetState.Idle);
    }

    /// <summary>A brief quiet pause, with no repeated jumping or interruption of a drag or flight.</summary>
    public void StartGentleAttention()
    {
        StopNagging();
        _lastActivityMs = _totalElapsedMs;
        _gentleHoldUntilMs = _totalElapsedMs + 1400;
        if (State is not (PetState.Dragged or PetState.Falling)) ChangeState(PetState.Idle);
    }

    /// <summary>Called when the user clicks the pet. Enough pokes in quick succession makes it angry.</summary>
    public void Poke()
    {
        _lastActivityMs = _totalElapsedMs;

        // A poke wakes it up; being jabbed awake doesn't immediately count as provocation.
        if (State == PetState.Sleep)
        {
            ChangeState(PetState.Idle);
            _pokeStreak = 0;
            _lastPokeAtMs = _totalElapsedMs;
            return;
        }

        _pokeStreak = _totalElapsedMs - _lastPokeAtMs <= PokeStreakWindowMs ? _pokeStreak + 1 : 1;
        _lastPokeAtMs = _totalElapsedMs;

        if (_pokeStreak >= PokesToAnger)
        {
            _pokeStreak = 0;
            StartEmote(PetState.Angry);
        }
    }

    public void StartEmote(PetState emote)
    {
        if (!emote.IsEmote())
        {
            throw new ArgumentOutOfRangeException(nameof(emote), emote, "감정 상태(Cry/Angry)만 지정할 수 있습니다.");
        }

        _lastActivityMs = _totalElapsedMs; // an emote means something just happened
        if (State is PetState.Dragged or PetState.Falling)
        {
            _queuedEmote = emote; // latest wins
        }
        else
        {
            _emoteEndsAtMs = emote == PetState.Cry ? CryDurationMs : AngryDurationMs;
            ChangeState(emote);
        }
    }

    private void TickFalling(double dtSeconds)
    {
        // Horizontal carry-over is what turns a fall into a hop or a throw; zero for a plain drop.
        var horizontalDecay = Math.Pow(AirDragPerSec, dtSeconds);
        X += _fallVelocityX * HorizontalTravelFactor(dtSeconds);
        _fallVelocityX *= horizontalDecay;

        // Bound horizontal movement before testing a landing; otherwise a fast throw can
        // miss the floor outside its X range and remain below every platform forever.
        var monitorFloor = _layout.GetMonitorFloorAt(X + _frameWidth / 2, Y + _groundOffset - 0.5);
        var minX = monitorFloor.Left;
        var maxX = monitorFloor.Right - _frameWidth;
        if (maxX < minX) minX = maxX = (monitorFloor.Left + monitorFloor.Right - _frameWidth) / 2;
        if (X < minX)
        {
            X = minX;
            if (_fallVelocityX < 0)
            {
                _fallVelocityX = -_fallVelocityX * WallBounceRetention;
                FacingRight = true;
            }
        }
        else if (X > maxX)
        {
            X = maxX;
            if (_fallVelocityX > 0)
            {
                _fallVelocityX = -_fallVelocityX * WallBounceRetention;
                FacingRight = false;
            }
        }
        var centerX = X + _frameWidth / 2;
        var feetYOld = Y + _groundOffset;

        var feetYNew = feetYOld + _fallVelocityY * dtSeconds + 0.5 * GravityPxPerSec2 * dtSeconds * dtSeconds;
        _fallVelocityY += GravityPxPerSec2 * dtSeconds;

        // Only test for landing while descending, so the upward part of a hop passes over the
        // edge it just left instead of immediately re-landing on it.
        var landing = _fallVelocityY > 0
            ? _layout.FindLandingPlatform(centerX, feetYOld, feetYNew)
            : null;

        // Work areas may move above the pet after display changes. The desktop floor is
        // the final boundary even when the pet was already below it at the start of a tick.
        if (_fallVelocityY > 0 && feetYNew >= monitorFloor.GroundY &&
            (landing is null || landing.Value.GroundY > monitorFloor.GroundY))
            landing = monitorFloor;

        if (landing is { } platform)
        {
            Y = platform.GroundY - _groundOffset;
            var impactSpeed = _fallVelocityY;
            var wasHopping = _isHopping;

            _fallVelocityY = 0;
            _fallVelocityX = 0;
            _isHopping = false;

            // A gentle drop is shrugged off; a long fall hurts — unless the pet jumped on purpose.
            // Finish the physical landing before starting a reaction; StartEmote queues while airborne.
            var queuedEmote = _queuedEmote;
            _queuedEmote = null;
            ChangeState(PetState.Idle);
            if (IsNagging)
            {
                StartEmote(_nagEmote);
            }
            else if (queuedEmote is { } requested)
            {
                StartEmote(requested);
            }
            else if (impactSpeed >= CryOnImpactSpeed && !wasHopping)
            {
                StartEmote(PetState.Cry);
            }
        }
        else
        {
            Y = feetYNew - _groundOffset;
        }

    }

    /// <summary>
    /// Starts falling straight down — the surface underneath went away (window closed or moved),
    /// rather than the pet choosing to jump. Clears any leftover hop momentum.
    /// </summary>
    private void BeginPlainFall()
    {
        _fallVelocityX = 0;
        _fallVelocityY = 0;
        _isHopping = false;
        ChangeState(PetState.Falling);
    }

    /// <summary>
    /// At a platform edge, looks for somewhere lower to hop down to in the direction of travel.
    /// Returns false when there's nothing worth jumping to, so the caller can turn around instead.
    /// </summary>
    private bool TryHopDown(double direction, Platform current)
    {
        return TryHop(direction, current, upward: false);
    }

    /// <summary>Jumps up onto a higher ledge in the direction of travel, if one is in reach.</summary>
    private bool TryHopUp(double direction, Platform current)
    {
        return TryHop(direction, current, upward: true);
    }

    private bool TryHop(double direction, Platform current, bool upward)
    {
        var centerX = X + _frameWidth / 2;
        var upwardSpeed = upward ? HopUpSpeedPxPerSec : HopUpwardSpeedPxPerSec;
        var candidates = _layout.GetHopCandidates(centerX, direction, current.GroundY,
            upward ? MaxHopUpRisePx : 0, upward ? 0 : 520, HopSearchDistancePx, _frameWidth);
        foreach (var target in candidates)
        {
            var discriminant = upwardSpeed * upwardSpeed + 2 * GravityPxPerSec2 * (target.GroundY - current.GroundY);
            if (discriminant <= 0) continue;
            var flightSeconds = (upwardSpeed + Math.Sqrt(discriminant)) / GravityPxPerSec2;
            var travelFactor = HorizontalTravelFactor(flightSeconds);

            var landingLeft = target.Left + _frameWidth / 2;
            var landingRight = target.Right - _frameWidth / 2;
            if (landingRight < landingLeft)
                landingLeft = landingRight = (target.Left + target.Right) / 2;
            var nearDistance = direction > 0 ? landingLeft - centerX : centerX - landingRight;
            var farDistance = direction > 0 ? landingRight - centerX : centerX - landingLeft;
            var minimumSpeed = Math.Max(8, nearDistance) / travelFactor;
            var maximumSpeed = Math.Min(MaxHopHorizontalSpeedPxPerSec, farDistance / travelFactor);

            if (!upward)
            {
                // A down-hop must leave the current ledge before returning to its height.
                // Otherwise it simply lands on the same window and repeats the hop forever.
                var clearanceDistance = direction > 0 ? current.Right - centerX + 2 : centerX - current.Left + 2;
                var returnSeconds = 2 * upwardSpeed / GravityPxPerSec2;
                minimumSpeed = Math.Max(minimumSpeed, clearanceDistance / HorizontalTravelFactor(returnSeconds));
            }
            if (minimumSpeed > maximumSpeed || maximumSpeed <= 0) continue;
            var speed = Math.Clamp(HopHorizontalSpeedPxPerSec, minimumSpeed, maximumSpeed);
            if (!HopPathIsClear(centerX, current.GroundY, target, direction * speed, upwardSpeed, flightSeconds)) continue;
            Launch(direction, speed, upwardSpeed);
            return true;
        }
        return false;
    }

    private bool HopPathIsClear(double centerX, double groundY, Platform target, double velocityX, double upwardSpeed, double flightSeconds)
    {
        var previousY = groundY;
        var steps = (int)Math.Ceiling(flightSeconds / MaxSimulationStepSeconds);
        for (var i = 1; i <= steps; i++)
        {
            var time = flightSeconds * i / steps;
            var x = centerX + velocityX * HorizontalTravelFactor(time);
            var y = groundY - upwardSpeed * time + 0.5 * GravityPxPerSec2 * time * time;
            var floor = _layout.GetMonitorFloorAt(x, y - 0.5);
            if (x - _frameWidth / 2 < floor.Left - 0.5 || x + _frameWidth / 2 > floor.Right + 0.5) return false;
            if (time * GravityPxPerSec2 >= upwardSpeed)
            {
                var landing = _layout.FindLandingPlatform(x, previousY, y);
                if (landing is { } platform && platform != target) return false;
            }
            previousY = y;
        }
        return true;
    }

    private static double HorizontalTravelFactor(double seconds) =>
        (Math.Pow(AirDragPerSec, seconds) - 1) / Math.Log(AirDragPerSec);

    private void Launch(double direction, double horizontalSpeed, double upwardSpeed)
    {
        _fallVelocityX = direction * horizontalSpeed;
        _fallVelocityY = -upwardSpeed;
        _isHopping = true;
        FacingRight = direction > 0;
        ChangeState(PetState.Falling);
    }

    /// <summary>
    /// At an edge, decides between climbing, dropping, or turning back. When both routes exist
    /// the choice is random so the pet doesn't deterministically drain to the bottom of the screen
    /// and stay there.
    /// </summary>
    private bool TryLeaveEdge(double direction, Platform current)
    {
        if (!HopBetweenPlatforms)
        {
            return false;
        }

        if (_rng.NextDouble() < PreferClimbChance)
        {
            return TryHopUp(direction, current) || TryHopDown(direction, current);
        }

        return TryHopDown(direction, current) || TryHopUp(direction, current);
    }

    private void TickWalk(double dtSeconds, PetPoint cursorScreenPos)
    {
        var targetVelocity = _walkStopDestination is null ? _walkTargetDirection * WalkSpeedPxPerSec : 0;
        var decay = Math.Exp(-dtSeconds / WalkVelocityResponseSeconds);
        X += targetVelocity * dtSeconds + (_walkVelocityX - targetVelocity) * WalkVelocityResponseSeconds * (1 - decay);
        _walkVelocityX = targetVelocity + (_walkVelocityX - targetVelocity) * decay;
        FacingRight = Math.Abs(_walkVelocityX) > 1 ? _walkVelocityX > 0 : _walkTargetDirection >= 0;

        var support = _layout.GetSupportPlatform(X + _frameWidth / 2, Y + _groundOffset);
        if (support is null)
        {
            BeginPlainFall();
            return;
        }

        var platform = support.Value;
        var leftEdge = platform.Left;
        var rightEdge = platform.Right - _frameWidth;
        if (rightEdge < leftEdge)
        {
            // Platform narrower than the character (e.g. a small dialog) — just center on it.
            leftEdge = rightEdge = (platform.Left + platform.Right - _frameWidth) / 2;
        }

        if (X <= leftEdge)
        {
            // Prefer moving to an adjacent surface over turning around at every edge.
            if (!_roamingPaused && TryLeaveEdge(-1, platform))
            {
                return;
            }

            X = leftEdge;
            _walkTargetDirection = 1;
            _walkVelocityX = 0;
            if (!_roamingPaused) FacingRight = true;
        }
        else if (X >= rightEdge)
        {
            if (!_roamingPaused && TryLeaveEdge(1, platform))
            {
                return;
            }

            X = rightEdge;
            _walkTargetDirection = -1;
            _walkVelocityX = 0;
            if (!_roamingPaused) FacingRight = false;
        }

        Y = platform.GroundY - _groundOffset;

        if (_walkStopDestination is { } destination)
        {
            if (Math.Abs(_walkVelocityX) <= 1) ChangeState(destination);
        }
        else if (_stateElapsedMs >= _nextStateDecisionAtMs)
        {
            var next = PickNextRestState();
            if (next == PetState.Walk) ChangeState(next);
            else _walkStopDestination = next;
        }
    }

    private void TickIdleLike(double dtSeconds, PetPoint cursorScreenPos)
    {
        var support = _layout.GetSupportPlatform(X + _frameWidth / 2, Y + _groundOffset);
        if (support is null)
        {
            BeginPlainFall();
            return;
        }

        Y = support.Value.GroundY - _groundOffset;
        if (_totalElapsedMs < _gentleHoldUntilMs) return;

        var dx = cursorScreenPos.X - (X + _frameWidth / 2);
        var dy = cursorScreenPos.Y - (Y + _groundOffset / 2);
        var cursorIsNear = Math.Abs(dx) <= LookAtCursorRangeXPx && Math.Abs(dy) <= LookAtCursorRangeYPx;

        if (State == PetState.LookAtCursor)
        {
            // Staring is time-boxed. Without this the pet latches onto a parked cursor and never
            // moves again, which just reads as "it's frozen".
            if (!cursorIsNear || _stateElapsedMs >= MaxLookDurationMs)
            {
                _lookCooldownUntilMs = _totalElapsedMs + LookCooldownMs;
                ChangeState(PickNextRestState());
                return;
            }

            FacingRight = dx >= 0;
            return;
        }

        if (cursorIsNear && _totalElapsedMs >= _lookCooldownUntilMs)
        {
            FacingRight = dx >= 0;
            ChangeState(PetState.LookAtCursor);
            return;
        }

        // Nobody's been around for a long while — settle down for a nap.
        if (SleepEnabled && _totalElapsedMs - _lastActivityMs >= BehaviorProfile.SleepAfterIdleMs)
        {
            ChangeState(PetState.Sleep);
            return;
        }

        if (_stateElapsedMs >= _nextStateDecisionAtMs)
        {
            ChangeState(PickNextRestState());
        }
    }

    private PetState PickNextRestState()
    {
        var walk = _roamingPaused ? 0 : (CalmBehavior ? 42 : 70) * BehaviorProfile.WalkWeight;
        var idle = (CalmBehavior ? 40 : 18) * BehaviorProfile.IdleWeight;
        var sit = (CalmBehavior ? 18 : 12) * BehaviorProfile.SitWeight;
        var roll = _rng.NextDouble() * (walk + idle + sit);
        if (roll < walk)
        {
            _walkTargetDirection = _rng.NextDouble() < 0.5 ? -1 : 1;
            return PetState.Walk;
        }

        return roll < walk + idle ? PetState.Idle : PetState.Sit;
    }

    private void ChangeState(PetState next)
    {
        if (State != PetState.Walk || next != PetState.Walk) _walkVelocityX = 0;
        _walkStopDestination = null;
        State = next;
        _stateElapsedMs = 0;

        switch (next)
        {
            case PetState.Walk:
                ScheduleNextActionWindow(2000, 5500);
                break;
            case PetState.Idle:
                ScheduleNextIdleDecision();
                break;
            case PetState.Sit:
                ScheduleNextActionWindow(CalmBehavior ? 3200 : 1800, CalmBehavior ? 6200 : 4000);
                break;
        }
    }

    private void ScheduleNextIdleDecision() => ScheduleNextActionWindow(CalmBehavior ? 2400 : 700, CalmBehavior ? 5600 : 2000);

    private void ScheduleNextActionWindow(double minMs, double maxMs)
    {
        var multiplier = State == PetState.Walk ? BehaviorProfile.WalkDurationMultiplier : BehaviorProfile.RestDurationMultiplier;
        _nextStateDecisionAtMs = (minMs + _rng.NextDouble() * (maxMs - minMs)) * multiplier;
    }

    public void BeginDrag(PetPoint cursorScreenPos)
    {
        _lastActivityMs = _totalElapsedMs;
        _dragOffsetX = cursorScreenPos.X - X;
        _dragOffsetY = cursorScreenPos.Y - Y;
        ChangeState(PetState.Dragged);
    }

    public void UpdateDrag(PetPoint cursorScreenPos)
    {
        X = cursorScreenPos.X - _dragOffsetX;
        Y = cursorScreenPos.Y - _dragOffsetY;
    }

    /// <summary>
    /// Releases the pet, carrying over the cursor's velocity at the moment of release so a flick
    /// throws it rather than just dropping it straight down.
    /// </summary>
    public void EndDrag(double releaseVelocityX = 0, double releaseVelocityY = 0)
    {
        _fallVelocityX = Math.Clamp(releaseVelocityX, -MaxThrowSpeedPxPerSec, MaxThrowSpeedPxPerSec);
        _fallVelocityY = Math.Clamp(releaseVelocityY, -MaxThrowSpeedPxPerSec, MaxThrowSpeedPxPerSec);
        _isHopping = false; // dropped by the user, not a hop — a hard landing should still hurt

        if (Math.Abs(_fallVelocityX) > 1)
        {
            FacingRight = _fallVelocityX > 0;
        }

        ChangeState(PetState.Falling);
    }
}
