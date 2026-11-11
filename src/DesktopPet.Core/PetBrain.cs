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

    private const double CryDurationMs = 2600;
    private const double AngryDurationMs = 2000;

    /// <summary>Pokes within this window count toward the same "stop poking me" streak.</summary>
    private const double PokeStreakWindowMs = 1200;
    private const int PokesToAnger = 3;

    private readonly Random _rng;
    private double _frameWidth;
    private double _groundOffset; // distance from the frame's top to the character's "feet" row (character.json anchor.y, scaled)

    private readonly PlatformLayout _layout;
    private double _fallVelocityY;
    private double _dragOffsetX;
    private double _dragOffsetY;
    private double _stateElapsedMs;
    private double _nextStateDecisionAtMs;
    private double _walkTargetDirection;
    private double _emoteEndsAtMs;
    private double _totalElapsedMs;
    private double _lookCooldownUntilMs;
    private double _lastPokeAtMs = double.NegativeInfinity;
    private int _pokeStreak;

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

    /// <summary>Emotes stay put on whatever they're standing on until their timer runs out.</summary>
    private void TickEmote()
    {
        var support = _layout.GetSupportPlatform(X + _frameWidth / 2, Y + _groundOffset);
        if (support is null)
        {
            ChangeState(PetState.Falling);
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

        _emoteEndsAtMs = emote == PetState.Cry ? CryDurationMs : AngryDurationMs;
        ChangeState(emote);
    }

    private void TickFalling(double dtSeconds)
    {
        var centerX = X + _frameWidth / 2;
        var feetYOld = Y + _groundOffset;

        _fallVelocityY += GravityPxPerSec2 * dtSeconds;
        var feetYNew = feetYOld + _fallVelocityY * dtSeconds;

        var landing = _layout.FindLandingPlatform(centerX, feetYOld, feetYNew);
        if (landing is { } platform)
        {
            Y = platform.GroundY - _groundOffset;
            var impactSpeed = _fallVelocityY;
            _fallVelocityY = 0;

            // A gentle drop is shrugged off; a long fall hurts.
            if (impactSpeed >= CryOnImpactSpeed)
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

        // Even mid-air, don't let it drift off the current monitor entirely.
        var monitorFloor = _layout.GetMonitorFloorAt(centerX);
        X = Math.Clamp(X, monitorFloor.Left, monitorFloor.Right - _frameWidth);
    }

    private void TickWalk(double dtSeconds, PetPoint cursorScreenPos)
    {
        X += _walkTargetDirection * WalkSpeedPxPerSec * dtSeconds;
        FacingRight = _walkTargetDirection >= 0;

        var support = _layout.GetSupportPlatform(X + _frameWidth / 2, Y + _groundOffset);
        if (support is null)
        {
            ChangeState(PetState.Falling);
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
            X = leftEdge;
            _walkTargetDirection = 1;
            FacingRight = true;
        }
        else if (X >= rightEdge)
        {
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
            ChangeState(PetState.Falling);
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

    public void EndDrag()
    {
        _fallVelocityY = 0;
        ChangeState(PetState.Falling);
    }
}
