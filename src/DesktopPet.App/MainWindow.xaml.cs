using System.IO;
using System.Diagnostics;
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
    private readonly CharacterLibrary _characterLibrary;
    private readonly FocusTimer _focusTimer;
    private readonly TimeProvider _quietClock;
    private readonly Func<IReadOnlyList<DesktopRect>>? _workAreasProvider;
    private bool _scheduledQuietActive;
    private readonly DispatcherTimer _quietHoursTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    private readonly DispatcherTimer _focusTickTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _attentionTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private PetSuppressionReason _suppressionReasons;
    private bool? _appliedVisibility;
    private readonly string _inboxDirectory;
    private string? _lastFocusCaption;
    private FocusTimerStatus? _lastFocusStatus;
    private IntPtr _hwnd;
    private PlatformLayout _platformLayout = null!;
    private WindowPlatformMonitor? _windowPlatformMonitor;
    private NotificationInbox? _inbox;
    private SpeechBubbleWindow? _bubble;
    private readonly List<InboxNotification> _unreadNotifications = new();
    private readonly Queue<InboxNotification> _duplicateCleanup = new();
    private readonly DispatcherTimer _duplicateCleanupTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool _reviewingNotifications;
    private InboxNotification? _activeNotification;
    private readonly DispatcherTimer _settingsSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly DispatcherTimer _notificationPopupTimer = new();
    private readonly HashSet<string> _seenNotificationIds = new(StringComparer.Ordinal);
    private readonly Queue<string> _recentNotificationIds = new();
    private bool _settingsDirty;
    private bool _settingsSaveFailed;
    private bool _disposed;

    private CharacterDefinition _character = null!;
    private PetBrain _brain = null!;
    private PetSettings _settings = null!;
    private long _lastTickTimestamp;
    private double _dpiScale = 1.0;
    private bool _isDragging;
    private bool _isPressed;
    private System.Drawing.Point _pressOriginPx;

    // Smoothed cursor velocity during a drag, carried into the throw on release.
    private readonly DragVelocityTracker _dragVelocity = new();
    private readonly WalkAppearanceController _walkAppearance = new();
    private readonly FramePacer _framePacer = new();
    private readonly PoseTransitionPresenter _poseTransition;
    private PetState _lastPoseState = PetState.Idle;
    private double _walkAnimationElapsedMs;
    private double _idleAppearanceElapsedMs;
    private double _lastGaitPositionX = double.NaN;
    private bool _liveRigActive;
    private TimeSpan? _lastRenderingTime;
    private int _lastNativeX = int.MinValue, _lastNativeY = int.MinValue;

    // Last values actually pushed to the window/visual tree, so the render loop can skip no-ops.
    private double _lastMoveX = double.NaN;
    private double _lastMoveY = double.NaN;
    private long _lastTopmostTimestamp;
    private int _lastFlip;
    private string? _lastFramePath;
    private bool _renderLoopRunning;

    /// <summary>How far the cursor must move while held before a press becomes a drag.</summary>
    private const int DragThresholdPx = 5;

    public static string CharactersRoot => Path.Combine(AppContext.BaseDirectory, "Assets", "Characters");

    public MainWindow() : this(null) { }

    public MainWindow(CharacterLibrary? characterLibrary, FocusTimer? focusTimer = null, string? inboxDirectory = null, TimeProvider? quietClock = null,
        Func<IReadOnlyList<DesktopRect>>? workAreasProvider = null)
    {
        _characterLibrary = characterLibrary ?? new CharacterLibrary(CharactersRoot, Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopPet", "Characters"));
        _focusTimer = focusTimer ?? new FocusTimer();
        _quietClock = quietClock ?? TimeProvider.System;
        _workAreasProvider = workAreasProvider;
        _inboxDirectory = Path.GetFullPath(inboxDirectory ?? NotificationInbox.DefaultFolder);
        InitializeComponent();
        _poseTransition = new PoseTransitionPresenter(PetImage, PreviousPetImage, FlipTransform);
        PetWindowBehavior.Attach(this);
        Loaded += MainWindow_Loaded;
        _settingsSaveTimer.Tick += (_, _) => SaveSettings();
        _notificationPopupTimer.Tick += (_, _) => OnNotificationPopupElapsed();
        _duplicateCleanupTimer.Tick += (_, _) => RetryDuplicateCleanup();
        _focusTickTimer.Tick += (_, _) => UpdateFocusTimer();
        _attentionTimer.Tick += (_, _) => RefreshAttentionState();
        _quietHoursTimer.Tick += (_, _) => RefreshQuietHours();
        _focusTimer.SessionCompleted += OnFocusSessionCompleted;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindow_Loaded;
        try
        {
            InitializePet();
            IsReady = true;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            System.Windows.MessageBox.Show(ex.Message, "DesktopPet 시작 오류", MessageBoxButton.OK, MessageBoxImage.Error);
            System.Windows.Application.Current.Shutdown(1);
        }
    }

    internal bool IsReady { get; private set; }

    private void InitializePet()
    {
        _dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        _settings = PetSettings.Load();
        UiText.SetLanguage(_settings.Language);
        _framePacer.MaximumFps = _settings.MaximumFps;
        _focusTimer.ConfigureDurations(TimeSpan.FromMinutes(_settings.FocusMinutes), TimeSpan.FromMinutes(_settings.BreakMinutes));
        RefreshQuietHours();

        var names = _characterLibrary.GetNames();
        if (names.Count == 0)
        {
            throw new InvalidDataException("사용할 수 있는 캐릭터를 찾지 못했습니다. 앱의 캐릭터 폴더를 확인해주세요.");
        }

        var presentation = LoadInitialPresentation(names);
        ApplyPresentation(presentation);
        _settings.CharacterName = CurrentCharacterName;

        _platformLayout = PlatformLayout.FromWorkAreas(BuildMonitorWorkAreas());
        var (effWidth, effHeight, effGroundOffset, effSpeed) = ComputeEffectiveSize(_character);
        var floor = _platformLayout.GetMonitorFloorAt(0, 0);
        var startX = floor.Left + (floor.Right - floor.Left - effWidth) / 2;
        var startY = floor.GroundY - effGroundOffset;

        Width = effWidth;
        Height = effHeight;
        Visibility = _settings.Visible ? Visibility.Visible : Visibility.Hidden;

        _brain = new PetBrain(_platformLayout, effWidth, effGroundOffset, startX, startY, behaviorProfile: _character.BehaviorProfile)
        {
            WalkSpeedPxPerSec = effSpeed,
            HopBetweenPlatforms = _settings.HopBetweenWindowsEnabled && !_settings.ReducedMotion,
            SleepEnabled = _settings.SleepEnabled,
            CalmBehavior = _settings.CalmBehavior,
        };
        _lastGaitPositionX = _brain.X;

        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        Microsoft.Win32.SystemEvents.SessionSwitch += OnSessionSwitch;
        Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;

        var ownProcessId = (uint)Environment.ProcessId;
        _windowPlatformMonitor = new WindowPlatformMonitor(
            () => WindowPlatformScanner.Scan(ownProcessId, _dpiScale),
            _platformLayout.UpdateWindowPlatforms);

        _bubble = new SpeechBubbleWindow();
        _bubble.Acknowledged += AcknowledgeNotification;

        _inbox = new NotificationInbox(_inboxDirectory);
        // FileSystemWatcher raises on a pool thread; windows must be touched on the UI thread.
        _inbox.NotificationReceived += delivery =>
        {
            if (!Dispatcher.HasShutdownStarted)
                Dispatcher.BeginInvoke(() => { if (!_disposed) OnNotification(delivery); });
        };

        _hwnd = new WindowInteropHelper(this).Handle;
        MoveWindowTo(startX, startY); // avoid a visible flash at the WPF-default position before the first rendered frame

        _lastTickTimestamp = Stopwatch.GetTimestamp();
        RefreshAttentionState();
        _inbox.Start();
    }

    private CharacterPresentation LoadInitialPresentation(IReadOnlyList<string> names)
    {
        var errors = new List<string>();
        foreach (var name in new[] { _settings.CharacterName, PetSettings.DefaultCharacterName, "Chiikawa" }.Concat(names).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!names.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
            try { return CharacterPresentation.Load(ResolveCharacterFolder(name)); }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                errors.Add($"{name}: {ex.Message}");
                Trace.TraceWarning($"Character skipped: {name}: {ex.Message}");
            }
        }
        throw new InvalidDataException("사용할 수 있는 캐릭터가 없습니다.\n" + string.Join("\n", errors));
    }

    private void ApplyPresentation(CharacterPresentation presentation)
    {
        _poseTransition.Reset();
        _character = presentation.Definition;
        if (_brain is not null) _brain.BehaviorProfile = _character.BehaviorProfile;
        PetRig.SetPresentation(presentation);
        PetRig.Visibility = Visibility.Collapsed;
        PetImage.Visibility = Visibility.Visible;
        _liveRigActive = false;
        _lastPoseState = PetState.Idle;
        _frameCache.Clear();
        foreach (var (path, bitmap) in presentation.Frames) _frameCache.Add(path, bitmap);
        _lastFramePath = null;
        _idleAppearanceElapsedMs = 0;
        _walkAnimationElapsedMs = 0;
        _walkAppearance.Reset();
        _lastGaitPositionX = double.NaN;
    }

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
        _lastGaitPositionX = _brain.X; // Resizing shifts the frame origin, not the planted foot.
        _brain.WalkSpeedPxPerSec = effSpeed;
    }

    /// <summary>Swaps the active character at runtime (called from the tray menu).</summary>
    public void SwitchCharacter(string name)
    {
        if (!AvailableCharacterNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("캐릭터 목록에서 선택해주세요.");
        var presentation = CharacterPresentation.Load(ResolveCharacterFolder(name));
        ApplyPresentation(presentation);
        ApplyCurrentCharacterSizing();

        _settings.CharacterName = name;
        ScheduleSettingsSave();
    }

    public void SetScaleMultiplier(double multiplier)
    {
        _settings.ScaleMultiplier = double.IsFinite(multiplier) ? Math.Clamp(multiplier, 0.5, 2.5) : 1.0;
        ApplyCurrentCharacterSizing();
        ScheduleSettingsSave();
    }

    public void SetWalkSpeedMultiplier(double multiplier)
    {
        _settings.WalkSpeedMultiplier = double.IsFinite(multiplier) ? Math.Clamp(multiplier, 0.25, 3.0) : 1.0;
        ApplyCurrentCharacterSizing();
        ScheduleSettingsSave();
    }

    public void SavePreferencesBackup(string path) => PetSettingsBackup.SaveFile(path, _settings);

    public string CreateDiagnosticReport() => DiagnosticReport.Create(typeof(App).Assembly.GetName().Version!.ToString(),
        new PetDiagnosticSnapshot(_character.FolderPath.StartsWith(_characterLibrary.CustomRoot + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase) ? CharacterStorageKind.Imported : CharacterStorageKind.Bundled,
            _character.FrameWidth, _character.FrameHeight, _frameCache.Count, AvailableCharacterNames.Count,
            PendingNotificationCount, MaximumFps, ReducedMotion, IsPetVisible, IsTemporarilyPaused, IsQuietNow,
            _character.WalkRig is not null, UiText.Language));

    /// <returns>True if an unavailable backed-up character was kept as the current character.</returns>
    public bool RestorePreferencesJson(string json)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var candidate = PetSettingsBackup.ReadJson(json, _settings);
        var missing = !AvailableCharacterNames.Contains(candidate.CharacterName, StringComparer.OrdinalIgnoreCase);
        if (missing) candidate.CharacterName = CurrentCharacterName;
        var presentation = candidate.CharacterName.Equals(CurrentCharacterName, StringComparison.OrdinalIgnoreCase)
            ? null : CharacterPresentation.Load(ResolveCharacterFolder(candidate.CharacterName));
        // All untrusted JSON and replacement artwork are validated before changing live state.
        _settings = candidate;
        if (presentation is not null) ApplyPresentation(presentation);
        _settings.CharacterName = CurrentCharacterName;
        ApplyCurrentCharacterSizing();
        _brain.HopBetweenPlatforms = candidate.HopBetweenWindowsEnabled && !candidate.ReducedMotion;
        _brain.SleepEnabled = candidate.SleepEnabled;
        _brain.CalmBehavior = candidate.CalmBehavior;
        _framePacer.MaximumFps = candidate.MaximumFps;
        if (candidate.ReducedMotion) { _brain.StopNagging(); _poseTransition.Reset(); }
        _focusTimer.ConfigureDurations(TimeSpan.FromMinutes(candidate.FocusMinutes), TimeSpan.FromMinutes(candidate.BreakMinutes));
        RefreshQuietHours();
        if (IsQuietNow) DismissForQuietMode();
        UiText.SetLanguage(candidate.Language);
        RenderOptionsChanged?.Invoke(candidate.MaximumFps, candidate.ReducedMotion);
        RefreshAttentionState();
        WindowFallEnabledChanged?.Invoke(CanUseWindowFall);
        SynchronizeFocusTimer();
        ScheduleSettingsSave();
        return missing;
    }

    public void SetVisible(bool visible)
    {
        if (_settings.Visible == visible) return;
        _settings.Visible = visible;
        RefreshAttentionState();
        ScheduleSettingsSave();
    }

    public void SetPauseDuringFullscreen(bool enabled)
    {
        if (_settings.PauseDuringFullscreen == enabled) return;
        _settings.PauseDuringFullscreen = enabled;
        RefreshAttentionState();
        ScheduleSettingsSave();
    }

    private void RefreshAttentionState()
    {
        if (_disposed) return;
        var reasons = _suppressionReasons;
        var sessionPaused = (reasons & (PetSuppressionReason.SessionLocked | PetSuppressionReason.Suspended)) != 0;
        if (!_settings.PauseDuringFullscreen) reasons &= ~PetSuppressionReason.Fullscreen;
        else if (_hwnd != IntPtr.Zero)
        {
            var fullscreen = _settings.Visible && !sessionPaused && ForegroundFullscreenDetector.ShouldPause(_hwnd);
            reasons = fullscreen ? reasons | PetSuppressionReason.Fullscreen : reasons & ~PetSuppressionReason.Fullscreen;
        }
        SetTemporarySuppression(reasons);
        ApplyPetVisibility();
        UpdateAttentionPolling();
    }

    private void SetTemporarySuppression(PetSuppressionReason reasons)
    {
        if (_disposed || _suppressionReasons == reasons) return;
        _suppressionReasons = reasons;
        ApplyPetVisibility();
        UpdateAttentionPolling();
    }

    private void UpdateAttentionPolling()
    {
        var sessionPaused = (_suppressionReasons & (PetSuppressionReason.SessionLocked | PetSuppressionReason.Suspended)) != 0;
        var enabled = !_disposed && _hwnd != IntPtr.Zero && _settings.Visible && _settings.PauseDuringFullscreen && !sessionPaused;
        if (enabled && !_attentionTimer.IsEnabled) _attentionTimer.Start();
        else if (!enabled) _attentionTimer.Stop();
    }

    private void ApplyPetVisibility()
    {
        var visible = _settings.Visible && !IsTemporarilyPaused;
        if (_appliedVisibility == visible) return;
        _appliedVisibility = visible;
        Visibility = visible ? Visibility.Visible : Visibility.Hidden;

        if (visible)
        {
            _windowPlatformMonitor?.Resume();
            _lastTickTimestamp = Stopwatch.GetTimestamp();
            _lastRenderingTime = null;
            _lastTopmostTimestamp = 0;
            StartRenderLoop();
        }
        else
        {
            _windowPlatformMonitor?.Pause();
            CancelInteraction();
            _poseTransition.Reset();
            _reviewingNotifications = false;
            _notificationPopupTimer.Stop();
            _bubble?.Dismiss();
            StopRenderLoop();
        }

        PresentationVisibilityChanged?.Invoke(visible);
        if (visible && _activeNotification is not null) ShowActiveNotification();
    }

    private void StartRenderLoop()
    {
        if (_disposed || !_settings.Visible || IsTemporarilyPaused) return;
        if (_renderLoopRunning)
        {
            return;
        }

        CompositionTarget.Rendering += CompositionTarget_Rendering;
        _framePacer.Reset();
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

    public IReadOnlyList<string> AvailableCharacterNames => _characterLibrary.GetNames();
    public string CustomCharactersRoot => _characterLibrary.CustomRoot;
    public string ResolveCharacterFolder(string name) => _characterLibrary.Resolve(name);

    /// <summary>Validate/decode the complete prepared pack off the UI thread before publishing it.</summary>
    public Task<string> ImportCharacterFolderAsync(string folder, CancellationToken cancellationToken = default,
        IProgress<CharacterImportProgress>? progress = null) => Task.Run(() =>
        _characterLibrary.ImportFolder(folder, stagedFolder => { _ = CharacterPresentation.Load(stagedFolder, includeRasterGait: true, cancellationToken); },
            cancellationToken, progress), cancellationToken);

    public Task<string> ImportCharacterArchiveAsync(CharacterArchive archive, ArchiveCharacter character, CancellationToken cancellationToken = default,
        IProgress<CharacterImportProgress>? progress = null) => Task.Run(() =>
        archive.Import(character, _characterLibrary, stagedFolder => { _ = CharacterPresentation.Load(stagedFolder, includeRasterGait: true, cancellationToken); },
            cancellationToken, progress), cancellationToken);

    public string CurrentCharacterName => Path.GetFileName(_character.FolderPath);
    public double CurrentScaleMultiplier => _settings.ScaleMultiplier;
    public double CurrentWalkSpeedMultiplier => _settings.WalkSpeedMultiplier;
    public bool IsPetVisible => _settings.Visible;
    public bool IsTemporarilyPaused => _suppressionReasons != PetSuppressionReason.None;
    public bool PauseDuringFullscreen => _settings.PauseDuringFullscreen;
    public bool IsPresentationActive => !_disposed && _settings.Visible && !IsTemporarilyPaused;
    public bool HopEnabled => _settings.HopBetweenWindowsEnabled;
    public bool SleepEnabled => _settings.SleepEnabled;
    public bool WindowFallEnabled => _settings.WindowFallEnabled;
    public string UiLanguagePreference => _settings.Language;
    public void SetLanguage(string language)
    {
        if (_disposed) return;
        if (language is not ("auto" or "ko" or "en" or "zh-CN")) throw new ArgumentOutOfRangeException(nameof(language));
        _settings.Language = language;
        UiText.SetLanguage(language);
        SynchronizeFocusTimer();
        ScheduleSettingsSave();
    }
    public bool CanUseWindowFall => _settings.WindowFallEnabled && !_settings.ReducedMotion;
    public bool ReducedMotion => _settings.ReducedMotion;
    public int MaximumFps => _settings.MaximumFps;
    public event Action<int, bool>? RenderOptionsChanged;

    public void SetRenderOptions(int maximumFps, bool reducedMotion)
    {
        if (_disposed) return;
        _framePacer.MaximumFps = maximumFps; // Validate before changing preferences.
        _settings.MaximumFps = maximumFps;
        _settings.ReducedMotion = reducedMotion;
        _brain.HopBetweenPlatforms = _settings.HopBetweenWindowsEnabled && !reducedMotion;
        if (reducedMotion) { _brain.StopNagging(); _poseTransition.Reset(); }
        WindowFallEnabledChanged?.Invoke(CanUseWindowFall);
        RenderOptionsChanged?.Invoke(maximumFps, reducedMotion);
        ScheduleSettingsSave();
    }
    public bool CalmBehaviorEnabled => _settings.CalmBehavior;
    public bool AutoDismissNotifications => _settings.AutoDismissNotifications;
    public bool QuietMode => _settings.QuietMode;
    public bool QuietHoursEnabled => _settings.QuietHoursEnabled;
    public int QuietHoursStartMinute => _settings.QuietHoursStartMinute;
    public int QuietHoursEndMinute => _settings.QuietHoursEndMinute;
    public bool IsQuietNow => _settings.QuietMode || QuietHours.IsActive(_settings.QuietHoursEnabled,
        _settings.QuietHoursStartMinute, _settings.QuietHoursEndMinute, _quietClock.GetLocalNow());

    public void SetQuietHours(bool enabled, int startMinute, int endMinute)
    {
        if (_disposed) return;
        _ = QuietHours.IsActive(enabled, startMinute, endMinute, _quietClock.GetLocalNow());
        _settings.QuietHoursEnabled = enabled;
        _settings.QuietHoursStartMinute = startMinute;
        _settings.QuietHoursEndMinute = endMinute;
        RefreshQuietHours();
        ScheduleSettingsSave();
    }

    private void RefreshQuietHours()
    {
        if (_disposed) return;
        var active = QuietHours.IsActive(_settings.QuietHoursEnabled, _settings.QuietHoursStartMinute,
            _settings.QuietHoursEndMinute, _quietClock.GetLocalNow());
        if (_settings.QuietHoursEnabled) _quietHoursTimer.Start();
        else _quietHoursTimer.Stop();
        if (active && !_scheduledQuietActive) DismissForQuietMode();
        _scheduledQuietActive = active;
    }
    public bool AutoConnectAssistants => _settings.AutoConnectAssistants;
    public bool AssistantSetupSeen => _settings.AssistantSetupSeen;
    public int PendingNotificationCount => _unreadNotifications.Count;
    public FocusTimerSnapshot FocusSnapshot => _focusTimer.Snapshot;
    public int FocusMinutes => _settings.FocusMinutes;
    public int BreakMinutes => _settings.BreakMinutes;

    public void SetFocusDurations(int focusMinutes, int breakMinutes)
    {
        if (_disposed) return;
        if (focusMinutes is < 1 or > 180 || breakMinutes is < 1 or > 60)
            throw new ArgumentOutOfRangeException(nameof(focusMinutes), "집중 1~180분, 휴식 1~60분으로 설정해주세요.");
        _focusTimer.ConfigureDurations(TimeSpan.FromMinutes(focusMinutes), TimeSpan.FromMinutes(breakMinutes));
        _settings.FocusMinutes = focusMinutes;
        _settings.BreakMinutes = breakMinutes;
        SynchronizeFocusTimer();
        ScheduleSettingsSave();
    }
    public string FocusTimerCaption
    {
        get
        {
            var snapshot = FocusSnapshot;
            var kind = snapshot.Session == FocusSessionKind.Focus ? UiText.Get("집중") : UiText.Get("휴식");
            var state = snapshot.Status switch
            {
                FocusTimerStatus.Stopped => UiText.Get("집중 대기"),
                FocusTimerStatus.Paused => kind + UiText.Get(" 일시정지"),
                FocusTimerStatus.Completed => kind + UiText.Get(" 완료"),
                _ => kind,
            };
            var seconds = (int)Math.Ceiling(snapshot.Remaining.TotalSeconds);
            return $"{state} · {seconds / 60:00}:{seconds % 60:00}";
        }
    }
    public string FocusStartCaption => FocusSnapshot.Status == FocusTimerStatus.Completed && FocusSnapshot.Session == FocusSessionKind.Focus
        ? UiText.Get("휴식 시작") : UiText.Get("집중 시작");
    public bool FocusCanStart => FocusSnapshot.Status is FocusTimerStatus.Stopped or FocusTimerStatus.Completed;
    public bool FocusCanPause => FocusSnapshot.Status is FocusTimerStatus.Running or FocusTimerStatus.Paused;
    public bool FocusCanStop => FocusSnapshot.Status != FocusTimerStatus.Stopped;

    public event Action? FocusTimerChanged;

    public void StartFocusTimer()
    {
        if (_disposed || !FocusCanStart) return;
        var next = FocusSnapshot.Status == FocusTimerStatus.Completed && FocusSnapshot.Session == FocusSessionKind.Focus
            ? FocusSessionKind.Break : FocusSessionKind.Focus;
        _focusTimer.Start(next);
        SynchronizeFocusTimer();
    }

    public void PauseOrResumeFocusTimer()
    {
        if (_disposed) return;
        if (FocusSnapshot.Status == FocusTimerStatus.Running) _focusTimer.Pause();
        else if (FocusSnapshot.Status == FocusTimerStatus.Paused) _focusTimer.Resume();
        SynchronizeFocusTimer();
    }

    public void RestartFocusTimer()
    {
        if (_disposed || !FocusCanStop) return;
        _focusTimer.Restart();
        SynchronizeFocusTimer();
    }

    public void StopFocusTimer()
    {
        if (_disposed) return;
        _focusTimer.Stop();
        SynchronizeFocusTimer();
    }

    private void UpdateFocusTimer()
    {
        if (_disposed) return;
        _focusTimer.Tick();
        SynchronizeFocusTimer();
    }

    private void SynchronizeFocusTimer()
    {
        if (FocusSnapshot.Status == FocusTimerStatus.Running) _focusTickTimer.Start();
        else _focusTickTimer.Stop();
        var caption = FocusTimerCaption;
        if (caption == _lastFocusCaption && FocusSnapshot.Status == _lastFocusStatus) return;
        _lastFocusCaption = caption;
        _lastFocusStatus = FocusSnapshot.Status;
        FocusTimerChanged?.Invoke();
    }

    private void OnFocusSessionCompleted(FocusSessionCompletion completion) => PublishLocalNotification(new PetNotification
    {
        Title = completion.Session == FocusSessionKind.Focus ? UiText.Get("집중 시간 완료") : UiText.Get("휴식 시간 완료"),
        Message = completion.Session == FocusSessionKind.Focus
            ? UiText.Get("잠깐 쉬어가세요. 준비되면 휴식을 시작해요.") : UiText.Get("쉬는 시간이 끝났어요. 준비되면 다시 집중해요."),
        Mood = "gentle", Source = "focus-timer", EventId = Guid.NewGuid().ToString("N"), CreatedAtUtc = DateTimeOffset.UtcNow,
    });

    public void SendTestNotification() => PublishLocalNotification(new PetNotification
    {
        Title = UiText.Get("테스트 알림"), Message = UiText.Get("알림이 잘 도착했어요. 확인해보세요."), Mood = "gentle",
        Source = "test", EventId = Guid.NewGuid().ToString("N"), CreatedAtUtc = DateTimeOffset.UtcNow,
    });

    private void PublishLocalNotification(PetNotification notification)
    {
        if (_disposed) return;
        try { NotificationWriter.Publish(_inboxDirectory, notification); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            OperationFailed?.Invoke("알림을 보관하지 못했습니다. " + ex.Message);
        }
    }

    /// <summary>Raised when the window-fall feature is toggled, so the service can start/stop.</summary>
    public event Action<bool>? WindowFallEnabledChanged;
    public event Action<bool>? PresentationVisibilityChanged;
    public event Action<string>? OperationFailed;
    public event Action<int>? PendingNotificationCountChanged;
    public event Action<string>? AssistantNotificationReceived;
    public event Action<bool>? AutoConnectAssistantsChanged;

    public void SetCalmBehavior(bool enabled)
    {
        if (_settings.CalmBehavior == enabled) return;
        _settings.CalmBehavior = _brain.CalmBehavior = enabled;
        ScheduleSettingsSave();
    }

    public void SetAutoDismissNotifications(bool enabled)
    {
        if (_settings.AutoDismissNotifications == enabled) return;
        _settings.AutoDismissNotifications = enabled;
        if (_bubble is { IsShowing: true }) ShowActiveNotification();
        ScheduleSettingsSave();
    }

    public void SetQuietMode(bool enabled)
    {
        if (_settings.QuietMode == enabled) return;
        _settings.QuietMode = enabled;
        if (enabled) DismissForQuietMode();
        ScheduleSettingsSave();
    }

    private void DismissForQuietMode()
    {
        _reviewingNotifications = false;
        _notificationPopupTimer.Stop();
        _bubble?.Dismiss();
        _brain?.StopNagging();
    }

    public void SetAutoConnectAssistants(bool enabled)
    {
        if (_settings.AutoConnectAssistants == enabled) return;
        _settings.AutoConnectAssistants = enabled;
        ScheduleSettingsSave();
        AutoConnectAssistantsChanged?.Invoke(enabled);
    }

    public void MarkAssistantSetupSeen()
    {
        _settings.AssistantSetupSeen = true;
        ScheduleSettingsSave();
    }

    public void ShowUnreadNotifications()
    {
        _activeNotification = _unreadNotifications.FirstOrDefault();
        _reviewingNotifications = true;
        ShowActiveNotification(userRequested: true);
    }

    public void SetHopEnabled(bool enabled)
    {
        if (_settings.HopBetweenWindowsEnabled == enabled) return;
        _settings.HopBetweenWindowsEnabled = enabled;
        _brain.HopBetweenPlatforms = enabled && !_settings.ReducedMotion;
        ScheduleSettingsSave();
    }

    public void SetSleepEnabled(bool enabled)
    {
        if (_settings.SleepEnabled == enabled) return;
        _settings.SleepEnabled = enabled;
        _brain.SleepEnabled = enabled;
        ScheduleSettingsSave();
    }

    public void SetWindowFallEnabled(bool enabled)
    {
        if (_settings.WindowFallEnabled == enabled)
        {
            return;
        }

        _settings.WindowFallEnabled = enabled;
        ScheduleSettingsSave();
        WindowFallEnabledChanged?.Invoke(CanUseWindowFall);
    }

    public IReadOnlyList<DesktopRect> DesktopWorkAreas => BuildMonitorWorkAreas();

    public bool TryRecoverPet(DesktopRect? preferredMonitor = null)
    {
        if (_disposed) return false;
        var areas = BuildMonitorWorkAreas();
        if (areas.Count == 0 || areas.Any(area => !area.IsValid)) return false;
        DesktopRect target;
        if (preferredMonitor is { } preferred)
        {
            if (!areas.Contains(preferred)) return false;
            target = preferred;
        }
        else
        {
            var cursor = WinForms.Cursor.Position;
            var x = cursor.X / _dpiScale; var y = cursor.Y / _dpiScale;
            target = areas.OrderBy(area => area.DistanceSquared(x, y)).First();
        }
        CancelInteraction();
        _windowPlatformMonitor?.Pause();
        _platformLayout.UpdateWorkAreas(areas);
        _platformLayout.UpdateWindowPlatforms(Array.Empty<Platform>());
        _brain.PlaceOnDesktop(target);
        _walkAppearance.Reset(); _poseTransition.Reset();
        _lastGaitPositionX = _brain.X;
        _lastNativeX = _lastNativeY = int.MinValue;
        _lastTickTimestamp = Stopwatch.GetTimestamp();
        _framePacer.Reset();
        MoveWindowTo(_brain.X, _brain.Y, forceTopmost: true);
        var wasHidden = !_settings.Visible;
        _settings.Visible = true;
        RefreshAttentionState();
        if (IsPresentationActive) _windowPlatformMonitor?.Resume();
        if (wasHidden) ScheduleSettingsSave();
        return true;
    }

    private List<DesktopRect> BuildMonitorWorkAreas() => _workAreasProvider?.Invoke().ToList() ??
        MonitorHelper.GetWorkAreasPx()
            .Select(rect => new DesktopRect(rect.Left / _dpiScale, rect.Top / _dpiScale, rect.Right / _dpiScale, rect.Bottom / _dpiScale))
            .ToList();

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.HasShutdownStarted)
            Dispatcher.BeginInvoke(() =>
            {
                if (_disposed) return;
                _windowPlatformMonitor?.Pause();
                _platformLayout.UpdateWorkAreas(BuildMonitorWorkAreas());
                _platformLayout.UpdateWindowPlatforms(Array.Empty<Platform>());
                _lastNativeX = _lastNativeY = int.MinValue;
                _lastGaitPositionX = double.NaN;
                RefreshAttentionState();
                if (IsPresentationActive) _windowPlatformMonitor?.Resume();
            });
    }

    private void OnSessionSwitch(object sender, Microsoft.Win32.SessionSwitchEventArgs e)
    {
        if (e.Reason is Microsoft.Win32.SessionSwitchReason.SessionLock or Microsoft.Win32.SessionSwitchReason.SessionUnlock)
            DispatchSessionState(PetSuppressionReason.SessionLocked, e.Reason == Microsoft.Win32.SessionSwitchReason.SessionLock);
    }

    private void OnPowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (e.Mode is Microsoft.Win32.PowerModes.Suspend or Microsoft.Win32.PowerModes.Resume)
            DispatchSessionState(PetSuppressionReason.Suspended, e.Mode == Microsoft.Win32.PowerModes.Suspend);
    }

    private void DispatchSessionState(PetSuppressionReason reason, bool enabled)
    {
        if (Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (_disposed) return;
            // Refresh foreground state before applying the new session state, avoiding a
            // one-frame flash over a fullscreen app when the session wakes/unlocks.
            _suppressionReasons = enabled ? _suppressionReasons | reason : _suppressionReasons & ~reason;
            RefreshAttentionState();
        });
    }

    private void ScheduleSettingsSave()
    {
        _settingsDirty = true;
        _settingsSaveTimer.Stop();
        _settingsSaveTimer.Start();
    }

    private void SaveSettings()
    {
        _settingsSaveTimer.Stop();
        if (!_settingsDirty) return;
        try
        {
            _settings.Save();
            _settingsDirty = false;
            _settingsSaveFailed = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceError($"Settings save: {ex}");
            if (!_settingsSaveFailed)
                OperationFailed?.Invoke("설정을 저장하지 못했습니다. 저장 폴더의 권한과 디스크 공간을 확인해주세요.");
            _settingsSaveFailed = true;
        }
    }

    private void CompositionTarget_Rendering(object? sender, EventArgs e)
    {
        if (_disposed || !_settings.Visible || IsTemporarilyPaused) return;
        if (e is RenderingEventArgs rendering)
        {
            if (_lastRenderingTime == rendering.RenderingTime) return;
            _lastRenderingTime = rendering.RenderingTime;
            if (!_framePacer.ShouldRender(rendering.RenderingTime.TotalMilliseconds)) return;
        }
        var now = Stopwatch.GetTimestamp();
        var dt = Math.Clamp(Stopwatch.GetElapsedTime(_lastTickTimestamp, now).TotalSeconds, 0, 0.1);
        _lastTickTimestamp = now;

        var cursor = WinForms.Cursor.Position;
        var cursorWpf = new PetPoint(cursor.X / _dpiScale, cursor.Y / _dpiScale);

        _brain.Tick(dt, cursorWpf);

        var drawY = _brain.Y + _brain.NagHopOffset;

        // This runs at display refresh rate, so anything unconditional here is paid 60x a second.
        // A resting pet changes neither position nor frame, so skip the Win32 call and the WPF
        // property writes unless something actually moved.
        if (!_isDragging)
        {
            // Topmost still has to be re-asserted periodically even while standing still,
            // otherwise another always-on-top window can end up covering the pet.
            var topmostDue = _lastTopmostTimestamp == 0 || Stopwatch.GetElapsedTime(_lastTopmostTimestamp, now).TotalMilliseconds >= 900;

            MoveWindowTo(_brain.X, drawY, topmostDue);
            _lastMoveX = _brain.X;
            _lastMoveY = drawY;
            if (topmostDue) _lastTopmostTimestamp = now;
        }

        if (_bubble is { IsShowing: true })
        {
            _bubble.FollowPet(_brain.X, drawY, Width);
        }

        var cursorIsOnRight = cursorWpf.X >= _brain.X + Width / 2;
        var spriteScale = Width / _character.FrameWidth;
        var distanceDeltaPx = spriteScale > 0 && double.IsFinite(_lastGaitPositionX)
            ? (_brain.X - _lastGaitPositionX) / spriteScale : 0;
        _lastGaitPositionX = _brain.X;
        _walkAppearance.Update(_brain.State, _brain.FacingRight, _brain.HorizontalVelocityPxPerSec,
            _character.WalkSpeedPxPerSec, dt, _brain.WalkSpeedPxPerSec,
            distanceDeltaPx, _character.WalkCycleDistancePx);
        _walkAnimationElapsedMs = _walkAppearance.AnimationElapsedMs;
        var displayWalkPose = _walkAppearance.DisplayWalkPose;
        var hasDirectionalLook = !displayWalkPose && _brain.State == PetState.LookAtCursor &&
            (cursorIsOnRight ? _character.LookRightFrames : _character.LookLeftFrames).Count > 0;
        var walkingDirection = displayWalkPose && (_character.HasDirectionalWalk || _character.WalkCycleDistancePx is not null);
        var frontWalk = walkingDirection && _character.UsesFrontalWalkFrames(_walkAppearance.View);
        var facingRight = walkingDirection ? _walkAppearance.FacingRight : _brain.FacingRight;
        var flip = hasDirectionalLook || frontWalk || !displayWalkPose && _character.UsesFrontalRestFrames(_brain.State) || facingRight ? 1 : -1;
        if (flip != _lastFlip)
        {
            FlipTransform.ScaleX = flip;
            _lastFlip = flip;
        }

        var frames = displayWalkPose ? _character.GetWalkFrames(_walkAppearance.View)
            : _brain.State == PetState.LookAtCursor
            ? _character.GetLookFrames(cursorIsOnRight)
            : _character.GetFrames(_brain.State);

        var animationElapsedMs = displayWalkPose ? _walkAnimationElapsedMs : _brain.StateElapsedMs;
        if (!displayWalkPose && _brain.State == PetState.Idle && _character.FrameDurationsByState.ContainsKey(PetState.Idle))
        {
            _idleAppearanceElapsedMs += dt * 1000;
            animationElapsedMs = _idleAppearanceElapsedMs;
        }
        var framePath = displayWalkPose && _character.WalkCycleDistancePx is not null
            ? _character.GetWalkFramePath(_walkAppearance.View, _walkAppearance.GaitPhase,
                _walkAppearance.IsSettling ? _walkAppearance.SettleProgress : 0)
            : _character.GetFramePath(displayWalkPose ? PetState.Walk : _brain.State, frames, animationElapsedMs);
        if (!string.Equals(framePath, _lastFramePath, StringComparison.Ordinal))
        {
            _lastFramePath = framePath;
        }
        // Depart in the quarter/profile pose immediately, so the frontal rest sprite
        // cannot linger as a moving outgoing layer of a dissolve.
        if (displayWalkPose && PetRig.HasRig)
        {
            if (!_liveRigActive)
            {
                _poseTransition.Reset();
                PetImage.Visibility = Visibility.Hidden;
                PetRig.Visibility = Visibility.Visible;
                _liveRigActive = true;
            }
            if (_walkAppearance.IsSettling)
            {
                var rest = _character.GetFrames(_brain.State);
                PetRig.SetRestFrame(GetFrame(_character.GetFramePath(_brain.State, rest, _brain.StateElapsedMs)), _character.UsesFrontalRestFrames(_brain.State));
            }
            PetRig.UpdatePose(_walkAppearance.View, _walkAppearance.GaitPhase,
                _walkAppearance.IsSettling ? _walkAppearance.SettleProgress : 0, _walkAppearance.FacingRight, dt, _settings.ReducedMotion);
        }
        else
        {
            if (_liveRigActive)
            {
                _poseTransition.Reset();
                PetRig.Visibility = Visibility.Collapsed;
                PetImage.Visibility = Visibility.Visible;
                _liveRigActive = false;
            }
            _poseTransition.SetFrame(GetFrame(framePath), flip, frames,
                !_settings.ReducedMotion && (_character.WalkCycleDistancePx is null || !displayWalkPose) &&
                (_brain.State != PetState.Walk || _lastPoseState == PetState.Walk));
        }
        _lastPoseState = _brain.State;
    }

    /// <summary>Moves the native window directly and re-asserts HWND_TOPMOST in the same call.</summary>
    private void MoveWindowTo(double wpfX, double wpfY, bool forceTopmost = false)
    {
        var placement = SpritePixelPlacement.Calculate(wpfX, wpfY, _dpiScale);
        if (ResidualOffset.X != placement.OffsetX) ResidualOffset.X = placement.OffsetX;
        if (ResidualOffset.Y != placement.OffsetY) ResidualOffset.Y = placement.OffsetY;
        if (_lastNativeX == placement.PixelX && _lastNativeY == placement.PixelY && !forceTopmost) return;
        NativeMethods.SetWindowPos(
            _hwnd, NativeMethods.HWND_TOPMOST, placement.PixelX, placement.PixelY, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        _lastNativeX = placement.PixelX;
        _lastNativeY = placement.PixelY;
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
        ActorVisualRoot.CaptureMouse();
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
            _brain.BeginDrag(new PetPoint(_pressOriginPx.X / _dpiScale, _pressOriginPx.Y / _dpiScale));
            StartRenderLoop(); // don't wait for the next idle tick to go smooth

            _dragVelocity.Reset(new PetPoint(cursor.X / _dpiScale, cursor.Y / _dpiScale), MonotonicSeconds());
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
        _dragVelocity.Sample(new PetPoint(cursor.X / _dpiScale, cursor.Y / _dpiScale), MonotonicSeconds());
    }

    private static double MonotonicSeconds() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    private void PetImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isPressed)
        {
            return;
        }

        _isPressed = false;
        ActorVisualRoot.ReleaseMouseCapture();

        if (_isDragging)
        {
            _isDragging = false;
            TrackThrowVelocity(WinForms.Cursor.Position);

            // A slow release should just drop the pet, so ignore residual drift below a threshold.
            var velocity = _dragVelocity.ReleaseVelocity;
            _brain.EndDrag(velocity.X, velocity.Y);
        }
        else if (_activeNotification is not null)
        {
            AcknowledgeNotification();
        }
        else
        {
            _brain.Poke();
        }
    }

    public void DisposeResources()
    {
        if (_disposed) return;
        _disposed = true;
        _attentionTimer.Stop();
        _windowPlatformMonitor?.Dispose();
        _focusTickTimer.Stop();
        _quietHoursTimer.Stop();
        _focusTimer.SessionCompleted -= OnFocusSessionCompleted;
        _focusTimer.Dispose();
        _poseTransition.Reset();
        PetRig.SetPresentation(null);
        StopRenderLoop();
        _notificationPopupTimer.Stop();
        _duplicateCleanupTimer.Stop();
        CancelInteraction();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        Microsoft.Win32.SystemEvents.SessionSwitch -= OnSessionSwitch;
        Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SaveSettings();
        _inbox?.Dispose();
        _bubble?.Shutdown();
    }

    /// <summary>Plays an emote on demand (tray menu).</summary>
    public void TriggerEmote(PetState emote) => _brain.StartEmote(emote);

    private void CancelInteraction()
    {
        if (!_isPressed) return;
        _isPressed = false;
        if (_isDragging)
        {
            _isDragging = false;
            _brain.EndDrag();
        }
        ActorVisualRoot.ReleaseMouseCapture();
    }

    private void PetImage_LostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e) => CancelInteraction();

    private void OnNotification(InboxNotification delivery)
    {
        var notification = delivery.Notification;
        if (!string.IsNullOrWhiteSpace(notification.EventId) && notification.EventId.Length <= 512)
        {
            var key = notification.Source + ":" + notification.EventId;
            if (!_seenNotificationIds.Add(key))
            {
                if (!delivery.Acknowledge() && _duplicateCleanup.Count < 128)
                {
                    _duplicateCleanup.Enqueue(delivery);
                    _duplicateCleanupTimer.Start();
                }
                return;
            }
            else
            {
                _recentNotificationIds.Enqueue(key);
                if (_recentNotificationIds.Count > 512) _seenNotificationIds.Remove(_recentNotificationIds.Dequeue());
            }
        }
        if (notification.Source is "claude" or "codex") AssistantNotificationReceived?.Invoke(notification.Source);
        _unreadNotifications.Add(delivery);
        var alreadyShowing = _bubble is { IsShowing: true };
        if (_activeNotification is null || _settings.AutoDismissNotifications && !_reviewingNotifications)
        {
            _activeNotification = delivery;
            ShowActiveNotification(resetTimer: !alreadyShowing);
        }
        else
        {
            _bubble?.SetPendingCount(Math.Max(0, _unreadNotifications.Count - 1));
        }
        PendingNotificationCountChanged?.Invoke(PendingNotificationCount);
    }

    private void ShowActiveNotification(bool userRequested = false, bool resetTimer = true)
    {
        RefreshQuietHours();
        if (_activeNotification is null || IsTemporarilyPaused) return;
        if (!_settings.Visible)
        {
            if (!userRequested) return;
            SetVisible(true);
            if (_bubble is { IsShowing: true }) return;
        }
        if (IsQuietNow && !userRequested) return;

        var n = _activeNotification.Notification;
        var emote = string.Equals(n.Mood, "cry", StringComparison.OrdinalIgnoreCase)
            ? PetState.Cry
            : PetState.Angry;

        var gentle = _settings.ReducedMotion || _settings.CalmBehavior || string.Equals(n.Mood, "gentle", StringComparison.OrdinalIgnoreCase);
        if (gentle) _brain.StartGentleAttention();
        else _brain.StartNagging(emote);
        StartRenderLoop();
        _bubble?.ShowMessage(n.Title, n.Message, _dpiScale, gentle);
        _bubble?.SetPendingCount(Math.Max(0, _unreadNotifications.Count - 1));
        _bubble?.FollowPet(_brain.X, _brain.Y + _brain.NagHopOffset, Width);
        if (resetTimer || !_notificationPopupTimer.IsEnabled)
        {
            _notificationPopupTimer.Stop();
            if (_settings.AutoDismissNotifications)
            {
                _notificationPopupTimer.Interval = TimeSpan.FromSeconds(_settings.NotificationDisplaySeconds);
                _notificationPopupTimer.Start();
            }
        }
    }

    private void OnNotificationPopupElapsed()
    {
        _notificationPopupTimer.Stop();
        _bubble?.DismissSoftly();
        _brain.StopNagging();
        _reviewingNotifications = false;
        // Auto-hide is not acknowledgement: the receipt and tray badge stay available.
    }

    private void AcknowledgeNotification()
    {
        if (_activeNotification is null) return;
        _notificationPopupTimer.Stop();
        if (!_activeNotification.Acknowledge())
        {
            OperationFailed?.Invoke("알림 확인을 저장하지 못했습니다. 잠시 후 다시 클릭해주세요.");
            ShowActiveNotification(userRequested: true);
            return;
        }
        _brain.StopNagging();
        _bubble?.Dismiss();
        _unreadNotifications.Remove(_activeNotification);
        _activeNotification = _unreadNotifications.FirstOrDefault();
        _reviewingNotifications = _activeNotification is not null;
        PendingNotificationCountChanged?.Invoke(PendingNotificationCount);
        ShowActiveNotification(userRequested: true);
    }

    private void RetryDuplicateCleanup()
    {
        var count = Math.Min(32, _duplicateCleanup.Count);
        for (var i = 0; i < count; i++)
        {
            var duplicate = _duplicateCleanup.Dequeue();
            if (!duplicate.Acknowledge()) _duplicateCleanup.Enqueue(duplicate);
        }
        if (_duplicateCleanup.Count == 0) _duplicateCleanupTimer.Stop();
    }
}
