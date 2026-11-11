using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DesktopPet.Core;
using DesktopPet.Interop;
using WinForms = System.Windows.Forms;

namespace DesktopPet.App;

public partial class MainWindow : Window
{
    private readonly Dictionary<string, BitmapImage> _frameCache = new();
    private IntPtr _hwnd;
    private PlatformLayout _platformLayout = null!;
    private System.Threading.Timer? _windowPlatformTimer;
    private NotificationInbox? _inbox;
    private SpeechBubbleWindow? _bubble;

    private CharacterDefinition _character = null!;
    private PetBrain _brain = null!;
    private PetSettings _settings = null!;
    private DateTime _lastTickUtc;
    private double _dpiScale = 1.0;
    private bool _isDragging;
    private bool _isPressed;
    private System.Drawing.Point _pressOriginPx;

    // Smoothed cursor velocity during a drag, carried into the throw on release.
    private System.Drawing.Point _dragSampleCursor;
    private DateTime _dragSampleTimeUtc;
    private double _throwVelocityX;
    private double _throwVelocityY;

    // Last values actually pushed to the window/visual tree, so the render loop can skip no-ops.
    private double _lastMoveX = double.NaN;
    private double _lastMoveY = double.NaN;
    private DateTime _lastTopmostAssertUtc = DateTime.MinValue;
    private int _lastFlip;
    private string? _lastFramePath;
    private bool _renderLoopRunning;

    /// <summary>How far the cursor must move while held before a press becomes a drag.</summary>
    private const int DragThresholdPx = 5;

    public static string CharactersRoot => Path.Combine(AppContext.BaseDirectory, "Assets", "Characters");

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        _settings = PetSettings.Load();

        var names = CharacterLoader.ListAvailableCharacterNames(CharactersRoot);
        if (names.Count == 0)
        {
            throw new InvalidOperationException($"'{CharactersRoot}' 아래에서 캐릭터를 찾을 수 없습니다.");
        }

        var initialName = names.Contains(_settings.CharacterName)
            ? _settings.CharacterName
            : names.Contains("Chiikawa") ? "Chiikawa" : names[0];

        _character = LoadCharacter(initialName);
        PreloadFrames();

        _platformLayout = new PlatformLayout(BuildMonitorFloors());
        var (effWidth, effHeight, effGroundOffset, effSpeed) = ComputeEffectiveSize(_character);
        var floor = _platformLayout.GetMonitorFloorAt(0);
        var startX = floor.Left + (floor.Right - floor.Left - effWidth) / 2;
        var startY = floor.GroundY - effGroundOffset;

        Width = effWidth;
        Height = effHeight;
        Visibility = _settings.Visible ? Visibility.Visible : Visibility.Hidden;

        _brain = new PetBrain(_platformLayout, effWidth, effGroundOffset, startX, startY)
        {
            WalkSpeedPxPerSec = effSpeed,
            HopBetweenPlatforms = _settings.HopBetweenWindowsEnabled,
            SleepEnabled = _settings.SleepEnabled,
        };

        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (_, _) => _platformLayout.UpdateMonitors(BuildMonitorFloors());

        var ownProcessId = (uint)Environment.ProcessId;
        // ~0.13ms per sweep, so this is cheap; 300ms is still well inside the time it takes a
        // user to drag a window somewhere the pet might stand.
        _windowPlatformTimer = new System.Threading.Timer(
            _ => _platformLayout.UpdateWindowPlatforms(WindowPlatformScanner.Scan(ownProcessId, _dpiScale)),
            null, TimeSpan.Zero, TimeSpan.FromMilliseconds(300));

        _bubble = new SpeechBubbleWindow();
        _bubble.Acknowledged += () => _brain.StopNagging();

        _inbox = new NotificationInbox();
        // FileSystemWatcher raises on a pool thread; windows must be touched on the UI thread.
        _inbox.NotificationReceived += n => Dispatcher.BeginInvoke(() => OnNotification(n));

        _hwnd = new WindowInteropHelper(this).Handle;
        MoveWindowTo(startX, startY); // avoid a visible flash at the WPF-default position before the first rendered frame

        _lastTickUtc = DateTime.UtcNow;
        if (_settings.Visible)
        {
            StartRenderLoop();
        }
    }

    private static CharacterDefinition LoadCharacter(string name) =>
        CharacterLoader.Load(Path.Combine(CharactersRoot, name));

    private (double width, double height, double groundOffset, double speed) ComputeEffectiveSize(CharacterDefinition c)
    {
        var multiplier = c.Scale * _settings.ScaleMultiplier;
        return (
            c.FrameWidth * multiplier,
            c.FrameHeight * multiplier,
            c.AnchorY * multiplier,
            c.WalkSpeedPxPerSec * _settings.WalkSpeedMultiplier);
    }

    private void ApplyCurrentCharacterSizing()
    {
        var (effWidth, effHeight, effGroundOffset, effSpeed) = ComputeEffectiveSize(_character);
        Width = effWidth;
        Height = effHeight;
        _brain.UpdateFrameSize(effWidth, effGroundOffset);
        _brain.WalkSpeedPxPerSec = effSpeed;
    }

    /// <summary>Swaps the active character at runtime (called from the tray menu).</summary>
    public void SwitchCharacter(string name)
    {
        _character = LoadCharacter(name);
        _frameCache.Clear();
        PreloadFrames();
        ApplyCurrentCharacterSizing();

        _settings.CharacterName = name;
        _settings.Save();
    }

    public void SetScaleMultiplier(double multiplier)
    {
        _settings.ScaleMultiplier = multiplier;
        ApplyCurrentCharacterSizing();
        _settings.Save();
    }

    public void SetWalkSpeedMultiplier(double multiplier)
    {
        _settings.WalkSpeedMultiplier = multiplier;
        ApplyCurrentCharacterSizing();
        _settings.Save();
    }

    public void SetVisible(bool visible)
    {
        Visibility = visible ? Visibility.Visible : Visibility.Hidden;

        // No point simulating or rendering a pet nobody can see.
        if (visible)
        {
            _lastTickUtc = DateTime.UtcNow; // avoid one huge dt after a long hide
            StartRenderLoop();
        }
        else
        {
            StopRenderLoop();
        }

        _settings.Visible = visible;
        _settings.Save();
    }

    private void StartRenderLoop()
    {
        if (_renderLoopRunning)
        {
            return;
        }

        CompositionTarget.Rendering += CompositionTarget_Rendering;
        _renderLoopRunning = true;
    }

    private void StopRenderLoop()
    {
        if (!_renderLoopRunning)
        {
            return;
        }

        CompositionTarget.Rendering -= CompositionTarget_Rendering;
        _renderLoopRunning = false;
    }

    public IReadOnlyList<string> AvailableCharacterNames => CharacterLoader.ListAvailableCharacterNames(CharactersRoot);

    public string CurrentCharacterName => _character.Name;
    public double CurrentScaleMultiplier => _settings.ScaleMultiplier;
    public double CurrentWalkSpeedMultiplier => _settings.WalkSpeedMultiplier;
    public bool IsPetVisible => _settings.Visible;
    public bool HopEnabled => _settings.HopBetweenWindowsEnabled;
    public bool SleepEnabled => _settings.SleepEnabled;
    public bool WindowFallEnabled => _settings.WindowFallEnabled;

    /// <summary>Raised when the window-fall feature is toggled, so the service can start/stop.</summary>
    public event Action<bool>? WindowFallEnabledChanged;

    public void SetHopEnabled(bool enabled)
    {
        _settings.HopBetweenWindowsEnabled = enabled;
        _brain.HopBetweenPlatforms = enabled;
        _settings.Save();
    }

    public void SetSleepEnabled(bool enabled)
    {
        _settings.SleepEnabled = enabled;
        _brain.SleepEnabled = enabled;
        _settings.Save();
    }

    public void SetWindowFallEnabled(bool enabled)
    {
        if (_settings.WindowFallEnabled == enabled)
        {
            return;
        }

        _settings.WindowFallEnabled = enabled;
        _settings.Save();
        WindowFallEnabledChanged?.Invoke(enabled);
    }

    private List<Platform> BuildMonitorFloors() =>
        MonitorHelper.GetWorkAreasPx()
            .Select(rect => new Platform(rect.Left / _dpiScale, rect.Right / _dpiScale, rect.Bottom / _dpiScale))
            .ToList();

    private void CompositionTarget_Rendering(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        var dt = Math.Min((now - _lastTickUtc).TotalSeconds, 0.1); // clamp so a stall/breakpoint can't cause a huge jump
        _lastTickUtc = now;

        var cursor = WinForms.Cursor.Position;
        var cursorWpf = new PetPoint(cursor.X / _dpiScale, cursor.Y / _dpiScale);

        _brain.Tick(dt, cursorWpf);

        var drawY = _brain.Y + _brain.NagHopOffset;

        // This runs at display refresh rate, so anything unconditional here is paid 60x a second.
        // A resting pet changes neither position nor frame, so skip the Win32 call and the WPF
        // property writes unless something actually moved.
        if (!_isDragging)
        {
            var moved = Math.Abs(_brain.X - _lastMoveX) > 0.4 || Math.Abs(drawY - _lastMoveY) > 0.4;

            // Topmost still has to be re-asserted periodically even while standing still,
            // otherwise another always-on-top window can end up covering the pet.
            var topmostDue = (now - _lastTopmostAssertUtc).TotalMilliseconds >= 900;

            if (moved || topmostDue)
            {
                MoveWindowTo(_brain.X, drawY);
                _lastMoveX = _brain.X;
                _lastMoveY = drawY;
                _lastTopmostAssertUtc = now;
            }
        }

        if (_bubble is { IsShowing: true })
        {
            _bubble.FollowPet(_brain.X, drawY, Width);
        }

        var flip = _brain.FacingRight ? 1 : -1;
        if (flip != _lastFlip)
        {
            FlipTransform.ScaleX = flip;
            _lastFlip = flip;
        }

        var frames = _brain.State == PetState.LookAtCursor
            ? _character.GetLookFrames(cursorWpf.X >= _brain.X + _character.FrameWidth / 2)
            : _character.GetFrames(_brain.State);

        var framePath = _character.GetFramePath(_brain.State, frames, _brain.StateElapsedMs);
        if (!string.Equals(framePath, _lastFramePath, StringComparison.Ordinal))
        {
            PetImage.Source = GetFrame(framePath);
            _lastFramePath = framePath;
        }
    }

    /// <summary>Moves the native window directly and re-asserts HWND_TOPMOST in the same call.</summary>
    private void MoveWindowTo(double wpfX, double wpfY)
    {
        var px = (int)Math.Round(wpfX * _dpiScale);
        var py = (int)Math.Round(wpfY * _dpiScale);
        NativeMethods.SetWindowPos(
            _hwnd, NativeMethods.HWND_TOPMOST, px, py, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    private void PreloadFrames()
    {
        foreach (var path in _character.AllFramePaths())
        {
            GetFrame(path);
        }
    }

    private BitmapImage GetFrame(string path)
    {
        if (_frameCache.TryGetValue(path, out var cached))
        {
            return cached;
        }

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        _frameCache[path] = bitmap;
        return bitmap;
    }

    private void PetImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Don't commit to a drag yet — a press that never moves is a poke, not a drag.
        _isPressed = true;
        _isDragging = false;
        _pressOriginPx = WinForms.Cursor.Position;
        PetImage.CaptureMouse();
    }

    private void PetImage_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isPressed)
        {
            return;
        }

        var cursor = WinForms.Cursor.Position;

        if (!_isDragging)
        {
            var movedPx = Math.Abs(cursor.X - _pressOriginPx.X) + Math.Abs(cursor.Y - _pressOriginPx.Y);
            if (movedPx < DragThresholdPx)
            {
                return;
            }

            _isDragging = true;
            _brain.BeginDrag(new PetPoint(cursor.X / _dpiScale, cursor.Y / _dpiScale));
            StartRenderLoop(); // don't wait for the next idle tick to go smooth

            _dragSampleCursor = cursor;
            _dragSampleTimeUtc = DateTime.UtcNow;
            _throwVelocityX = 0;
            _throwVelocityY = 0;
        }
        else
        {
            TrackThrowVelocity(cursor);
        }

        _brain.UpdateDrag(new PetPoint(cursor.X / _dpiScale, cursor.Y / _dpiScale));
        MoveWindowTo(_brain.X, _brain.Y);
    }

    /// <summary>
    /// Keeps a smoothed estimate of how fast the cursor is moving, to use as throw velocity on
    /// release. Raw frame-to-frame deltas are far too jittery — a single stuttery sample right at
    /// release would otherwise fling the pet across the screen.
    /// </summary>
    private void TrackThrowVelocity(System.Drawing.Point cursor)
    {
        var now = DateTime.UtcNow;
        var dt = (now - _dragSampleTimeUtc).TotalSeconds;
        if (dt < 0.004)
        {
            return;
        }

        var vx = (cursor.X - _dragSampleCursor.X) / _dpiScale / dt;
        var vy = (cursor.Y - _dragSampleCursor.Y) / _dpiScale / dt;

        const double smoothing = 0.45;
        _throwVelocityX = _throwVelocityX * (1 - smoothing) + vx * smoothing;
        _throwVelocityY = _throwVelocityY * (1 - smoothing) + vy * smoothing;

        _dragSampleCursor = cursor;
        _dragSampleTimeUtc = now;
    }

    private void PetImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isPressed)
        {
            return;
        }

        _isPressed = false;
        PetImage.ReleaseMouseCapture();

        if (_isDragging)
        {
            _isDragging = false;

            // A slow release should just drop the pet, so ignore residual drift below a threshold.
            var speed = Math.Sqrt(_throwVelocityX * _throwVelocityX + _throwVelocityY * _throwVelocityY);
            if (speed < 90)
            {
                _brain.EndDrag();
            }
            else
            {
                _brain.EndDrag(_throwVelocityX, _throwVelocityY);
            }
        }
        else if (_brain.IsNagging)
        {
            // Clicking the pet itself acknowledges the notification too, not just the bubble.
            _brain.StopNagging();
            _bubble?.Dismiss();
        }
        else
        {
            _brain.Poke();
        }
    }

    public void DisposeResources()
    {
        _inbox?.Dispose();
        _windowPlatformTimer?.Dispose();
        _bubble?.Shutdown();
    }

    /// <summary>Plays an emote on demand (tray menu).</summary>
    public void TriggerEmote(PetState emote) => _brain.StartEmote(emote);

    private void OnNotification(PetNotification n)
    {
        if (!_settings.Visible)
        {
            SetVisible(true); // a hidden pet can't deliver a message
        }

        var emote = string.Equals(n.Mood, "cry", StringComparison.OrdinalIgnoreCase)
            ? PetState.Cry
            : PetState.Angry;

        _brain.StartNagging(emote);
        StartRenderLoop(); // the attention hop needs every frame
        _bubble?.ShowMessage(n.Title, n.Message, _dpiScale);
    }
}
