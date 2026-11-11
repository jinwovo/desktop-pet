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
    private const double HopUpwardSpeedPxPerSec = 170;

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

    /// <summary>How long the pet must go untouched, with the cursor idle, before dozing off.</summary>
    private const double SleepAfterIdleMs = 90_000;

    /// <summary>Cursor movement beyond this counts as activity and resets the sleep timer.</summary>
    private const double CursorIdleToleragePx = 40;

    private readonly Random _rng;
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
    private double _emoteEndsAtMs;
    private double _totalElapsedMs;
    private double _lookCooldownUntilMs;
    private double _lastActivityMs;
    private PetPoint _lastCursorPos;
    private double _lastPokeAtMs = double.NegativeInfinity;
    private int _pokeStreak;

    /// <summary>When false, the pet turns around at platform edges instead of jumping.</summary>
    public bool HopBetweenPlatforms { get; set; } = true;

    /// <summary>When false, the pet never dozes off no matter how long it's left alone.</summary>
    public bool SleepEnabled { get; set; } = true;

    public PetState State { get; private set; } = PetState.Idle;
    public double X { get; private set; }
    public double Y { get; private set; }
    public bool FacingRight { get; private set; } = true;
    public double StateElapsedMs => _stateElapsedMs;
    public double WalkSpeedPxPerSec { get; set; } = 60;

    public PetBrain(PlatformLayout layout, double frameWidth, double groundOffset, double startX, double startY, Random? rng = null)
    {
        _layout = layout;
        _frameWidth = frameWidth;
        _groundOffset = groundOffset;
        X = startX;
        Y = startY;
        _rng = rng ?? new Random();
        ScheduleNextIdleDecision();
    }

    /// <summary>Call after switching characters or changing scale — these feed the floor/edge math.</summary>
    public void UpdateFrameSize(double frameWidth, double groundOffset)
    {
        _frameWidth = frameWidth;
        _groundOffset = groundOffset;
    }

    public void Tick(double dtSeconds, PetPoint cursorScreenPos)
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
        IsNagging ? -Math.Abs(Math.Sin(_stateElapsedMs / 150.0)) * 7.0 : 0.0;

    /// <summary>Plays an emote indefinitely until <see cref="StopNagging"/> is called.</summary>
    public void StartNagging(PetState emote)
    {
        StartEmote(emote);
        IsNagging = true;
    }

    public void StopNagging()
    {
        if (!IsNagging)
        {
            return;
        }

        IsNagging = false;
        ChangeState(PetState.Idle);
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
        _emoteEndsAtMs = emote == PetState.Cry ? CryDurationMs : AngryDurationMs;
        ChangeState(emote);
    }

    private void TickFalling(double dtSeconds)
    {
        // Horizontal carry-over is what turns a fall into a hop or a throw; zero for a plain drop.
        X += _fallVelocityX * dtSeconds;
        _fallVelocityX *= Math.Pow(AirDragPerSec, dtSeconds);

        var centerX = X + _frameWidth / 2;
        var feetYOld = Y + _groundOffset;

        _fallVelocityY += GravityPxPerSec2 * dtSeconds;
        var feetYNew = feetYOld + _fallVelocityY * dtSeconds;

        // Only test for landing while descending, so the upward part of a hop passes over the
        // edge it just left instead of immediately re-landing on it.
        var landing = _fallVelocityY > 0
            ? _layout.FindLandingPlatform(centerX, feetYOld, feetYNew)
            : null;

        if (landing is { } platform)
        {
            Y = platform.GroundY - _groundOffset;
            var impactSpeed = _fallVelocityY;
            var wasHopping = _isHopping;

            _fallVelocityY = 0;
            _fallVelocityX = 0;
            _isHopping = false;

            // A gentle drop is shrugged off; a long fall hurts — unless the pet jumped on purpose.
            if (impactSpeed >= CryOnImpactSpeed && !wasHopping)
            {
                StartEmote(PetState.Cry);
            }
            else
            {
                ChangeState(PetState.Idle);
            }
        }
        else
        {
            Y = feetYNew - _groundOffset;
        }

        // Screen edges are walls while airborne. Bouncing (rather than clamping) is what sells a
        // throw: flinging the pet at the side of the screen should rebound, not stick.
        var monitorFloor = _layout.GetMonitorFloorAt(centerX);
        var minX = monitorFloor.Left;
        var maxX = monitorFloor.Right - _frameWidth;

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
        var target = _layout.FindHopDownTarget(
            X + _frameWidth / 2, direction, current.GroundY, HopSearchDistancePx, _frameWidth);

        if (target is null)
        {
            return false;
        }

        Launch(direction, HopHorizontalSpeedPxPerSec, HopUpwardSpeedPxPerSec);
        return true;
    }

    /// <summary>Jumps up onto a higher ledge in the direction of travel, if one is in reach.</summary>
    private bool TryHopUp(double direction, Platform current)
    {
        var target = _layout.FindHopUpTarget(
            X + _frameWidth / 2, direction, current.GroundY, MaxHopUpRisePx, HopSearchDistancePx, _frameWidth);

        if (target is null)
        {
            return false;
        }

        Launch(direction, HopHorizontalSpeedPxPerSec * 0.85, HopUpSpeedPxPerSec);
        return true;
    }

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
        X += _walkTargetDirection * WalkSpeedPxPerSec * dtSeconds;
        FacingRight = _walkTargetDirection >= 0;

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
            if (TryLeaveEdge(-1, platform))
            {
                return;
            }

            X = leftEdge;
            _walkTargetDirection = 1;
            FacingRight = true;
        }
        else if (X >= rightEdge)
        {
            if (TryLeaveEdge(1, platform))
            {
                return;
            }

            X = rightEdge;
            _walkTargetDirection = -1;
            FacingRight = false;
        }

        Y = platform.GroundY - _groundOffset;

        if (_stateElapsedMs >= _nextStateDecisionAtMs)
        {
            ChangeState(PickNextRestState());
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
        if (SleepEnabled && _totalElapsedMs - _lastActivityMs >= SleepAfterIdleMs)
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
        // Weighted toward walking — standing still is what makes a desktop pet feel dead.
        var roll = _rng.NextDouble();
        if (roll < 0.7)
        {
            _walkTargetDirection = _rng.NextDouble() < 0.5 ? -1 : 1;
            return PetState.Walk;
        }

        return roll < 0.88 ? PetState.Idle : PetState.Sit;
    }

    private void ChangeState(PetState next)
    {
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
                ScheduleNextActionWindow(1800, 4000);
                break;
        }
    }

    private void ScheduleNextIdleDecision() => ScheduleNextActionWindow(700, 2000);

    private void ScheduleNextActionWindow(double minMs, double maxMs) =>
        _nextStateDecisionAtMs = minMs + _rng.NextDouble() * (maxMs - minMs);

    public void BeginDrag(PetPoint cursorScreenPos)
    {
        IsNagging = false; // grabbing the pet counts as noticing it
        _lastActivityMs = _totalElapsedMs;
        _dragOffsetX = cursorScreenPos.X - X;
        _dragOffsetY = cursorScreenPos.Y - Y;
        ChangeState(PetState.Dragged);
    }

    public void UpdateDrag(PetPoint cursorScreenPos)
    {
        X = cursorScreenPos.X - _dragOffsetX;
        Y = cursorScreenPos.Y - _dragOffsetY;
        FacingRight = true;
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
