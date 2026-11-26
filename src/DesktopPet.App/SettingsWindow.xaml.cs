using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DesktopPet.Core;

namespace DesktopPet.App;

/// <summary>
/// Settings UI. Changes apply to the live pet immediately rather than on an OK button — with a
/// desktop mascot you want to see the effect of a size or speed change while dragging the slider.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly MainWindow _pet;
    private bool _loading = true;
    private bool _closed;
    private const string QuietHoursHelpKey = "로컬 시간 HH:mm. 자정을 넘길 수 있고 같은 시간은 예약하지 않아요.";
    private string _focusDurationStatusKey = "";
    private string? _preferencesTransferStatusKey;
    private CancellationTokenSource? _preferencesTransferCancellation;
    private string _quietHoursStatusKey = QuietHoursHelpKey;
    private bool _characterImportBusy;
    private Task _characterImportOperation = Task.CompletedTask;
    private CancellationTokenSource? _characterImportCancellation;
    private IProgress<CharacterImportProgress>? _characterImportProgress;
    private CancellationTokenSource? _characterSelectionCancellation;
    private long _characterSelectionRevision;
    public Task CharacterSelectionOperation { get; private set; } = Task.CompletedTask;
    private CharacterPresentation? _previewPresentation;
    private MotionPreviewWindow? _motionPreview;
    private readonly Stopwatch _previewClock = Stopwatch.StartNew();
    private double _previewLastElapsedMs;
    private double _previewAnimationMs;
    private double _previewStateElapsedMs;
    private bool _previewRenderingSubscribed;
    private readonly WalkAppearanceController _walkAppearance = new(new Random(7));
    private readonly FramePacer _framePacer = new();
    private readonly PoseTransitionPresenter _poseTransition;
    private PetState _lastPreviewState = PetState.Idle;
    private double _previewVirtualPositionPx;
    private bool _liveRigActive;
    private CharacterGalleryWindow? _characterGallery;
    private DiagnosticWindow? _diagnosticWindow;
    private TimeSpan? _lastPreviewRenderingTime;
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private sealed record PreviewChoice(string Label, PetState State);
    private sealed record MonitorChoice(string Label, DesktopRect Area);

    public SettingsWindow(MainWindow pet)
    {
        _pet = pet;
        InitializeComponent();
        InitializeTimeInputShortcuts();
        InitializeSettingsNavigation();
        _poseTransition = new PoseTransitionPresenter(PreviewImage, PreviousPreviewImage);
        VersionText.Text = "DesktopPet " + typeof(App).Assembly.GetName().Version!.ToString(3);
        MaxHeight = Math.Max(500, SystemParameters.WorkArea.Height - 60);
        LoadFromPet();
        RenderFocusTimer();
        _pet.FocusTimerChanged += RenderFocusTimer;
        _pet.SettingsSaveFailureChanged += RenderSettingsSaveFailure;
        RenderSettingsSaveFailure();
        RefreshDurationPresetTooltips();
        _pet.RenderOptionsChanged += ApplyRenderOptions;
        ApplyRenderOptions(_pet.MaximumFps, _pet.ReducedMotion);
        _previewTimer.Tick += (_, _) => { if (IsPreviewActive && !NeedsLiveRendering) RenderPreview(); };
        Loaded += (_, _) => UpdatePreviewAnimationState();
        IsVisibleChanged += (_, _) => UpdatePreviewAnimationState();
        StateChanged += (_, _) => UpdatePreviewAnimationState();
        Closed += (_, _) =>
        {
            _closed = true;
            ++_characterSelectionRevision;
            _characterSelectionCancellation?.Cancel();
            _characterImportCancellation?.Cancel();
            _preferencesTransferCancellation?.Cancel();
            _pet.FocusTimerChanged -= RenderFocusTimer;
            _pet.SettingsSaveFailureChanged -= RenderSettingsSaveFailure;
            _pet.RenderOptionsChanged -= ApplyRenderOptions;
            UiText.LanguageChanged -= RefreshLanguage;
            _previewTimer.Stop();
            CompositionTarget.Rendering -= OnPreviewRendering;
            _previewRenderingSubscribed = false;
            _poseTransition.Reset();
            PreviewRig.SetPresentation(null);
            _motionPreview?.Close();
            _characterGallery?.Close();
            _diagnosticWindow?.Close();
        };
        UiText.Attach(this);
        UiText.LanguageChanged += RefreshLanguage;
        RefreshAccessibleNames();
        RefreshTimeInputShortcutHelp();
        RefreshSettingsNavigationHelp();
    }

    private bool IsPreviewActive => !_closed && IsLoaded && IsVisible && WindowState != WindowState.Minimized;
    private void UpdatePreviewAnimationState()
    {
        var active = IsPreviewActive && _previewPresentation is not null;
        var wasActive = _previewRenderingSubscribed || _previewTimer.IsEnabled;
        var renderContinuously = active && NeedsLiveRendering;
        if (active && !wasActive)
            _previewLastElapsedMs = _previewClock.Elapsed.TotalMilliseconds;

        if (renderContinuously != _previewRenderingSubscribed)
        {
            if (renderContinuously)
            {
                _lastPreviewRenderingTime = null;
                _framePacer.Reset();
                CompositionTarget.Rendering += OnPreviewRendering;
            }
            else CompositionTarget.Rendering -= OnPreviewRendering;
            _previewRenderingSubscribed = renderContinuously;
        }

        var useTimer = active && !renderContinuously;
        if (useTimer != _previewTimer.IsEnabled)
        {
            if (useTimer) _previewTimer.Start();
            else _previewTimer.Stop();
        }
        if (!active && wasActive) _poseTransition.Reset();
    }

    private void LoadFromPet(bool preservePreview = false)
    {
        if (!preservePreview)
        {
            _focusDurationStatusKey = "";
            _quietHoursStatusKey = QuietHoursHelpKey;
        }
        var previewState = preservePreview && PreviewStateCombo.SelectedValue is PetState selected ? selected : PetState.Idle;
        _loading = true;
        LanguageCombo.SelectedValue = _pet.UiLanguagePreference;
        PreviewStateCombo.ItemsSource = new[]
        {
            new PreviewChoice(UiText.Get("가만히 있기"), PetState.Idle), new PreviewChoice(UiText.Get("걷기"), PetState.Walk),
            new PreviewChoice(UiText.Get("울기"), PetState.Cry), new PreviewChoice(UiText.Get("화내기"), PetState.Angry),
            new PreviewChoice(UiText.Get("잠자기"), PetState.Sleep), new PreviewChoice(UiText.Get("떨어지기"), PetState.Falling),
        };
        PreviewStateCombo.SelectedValue = previewState;

        var pendingCharacter = preservePreview && _characterSelectionCancellation is { IsCancellationRequested: false }
            ? CharacterCombo.SelectedItem as string : null;
        if (!preservePreview) CharacterCombo.ItemsSource = _pet.AvailableCharacterNames;
        CharacterCombo.SelectedItem = pendingCharacter ?? _pet.CurrentCharacterName;

        ScaleSlider.Value = _pet.CurrentScaleMultiplier;
        SpeedSlider.Value = _pet.CurrentWalkSpeedMultiplier;
        VisibleCheck.IsChecked = _pet.IsPetVisible;
        FullscreenCheck.IsChecked = _pet.PauseDuringFullscreen;
        HopCheck.IsChecked = _pet.HopEnabled;
        SleepCheck.IsChecked = _pet.SleepEnabled;
        WindowFallCheck.IsChecked = _pet.WindowFallEnabled;
        CalmCheck.IsChecked = _pet.CalmBehaviorEnabled;
        GentleCheck.IsChecked = _pet.AutoDismissNotifications;
        QuietCheck.IsChecked = _pet.QuietMode;
        FrameRateCombo.SelectedValue = _pet.MaximumFps.ToString(System.Globalization.CultureInfo.InvariantCulture);
        ReducedMotionCheck.IsChecked = _pet.ReducedMotion;
        QuietHoursCheck.IsChecked = _pet.QuietHoursEnabled;
        QuietStartBox.Text = QuietHours.Format(_pet.QuietHoursStartMinute);
        QuietEndBox.Text = QuietHours.Format(_pet.QuietHoursEndMinute);
        FocusMinutesBox.Text = _pet.FocusMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture);
        BreakMinutesBox.Text = _pet.BreakMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture);
        LoadMonitorChoices();

        _loading = false;

        UpdateValueLabels();
        if (preservePreview && _previewPresentation is not null) UpdateCharacterHint();
        else UpdatePreview();
        RenderDurationFeedback();
    }

    private void LoadMonitorChoices()
    {
        var selected = (MonitorTargetCombo.SelectedItem as MonitorChoice)?.Area;
        var choices = _pet.DesktopWorkAreas.Select((area, i) => new MonitorChoice(
            $"{i + 1} · {area.Width:0}×{area.Height:0} · ({area.Left:0}, {area.Top:0})", area)).ToArray();
        MonitorTargetCombo.ItemsSource = choices;
        MonitorTargetCombo.SelectedItem = choices.FirstOrDefault(choice => choice.Area == selected) ?? choices.FirstOrDefault();
    }

    private void RecoverPet_Click(object sender, RoutedEventArgs e)
    {
        if (MonitorTargetCombo.SelectedItem is not MonitorChoice choice || !_pet.TryRecoverPet(choice.Area))
        {
            LoadMonitorChoices();
            RecoveryStatusText.Text = UiText.Get("화면 구성이 바뀌었어요. 목록에서 다시 선택해주세요.");
            return;
        }
        VisibleCheck.IsChecked = _pet.IsPetVisible;
        RecoveryStatusText.Text = UiText.Get("선택한 화면으로 데려왔어요. 알림과 집중 시간은 유지합니다.");
    }

    private void Language_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && LanguageCombo.SelectedValue is string language) _pet.SetLanguage(language);
    }

    private void RefreshLanguage()
    {
        if (_closed) return;
        var focusDraft = FocusMinutesBox.Text;
        var breakDraft = BreakMinutesBox.Text;
        var quietStartDraft = QuietStartBox.Text;
        var quietEndDraft = QuietEndBox.Text;
        var quietEnabledDraft = QuietHoursCheck.IsChecked;
        LoadFromPet(preservePreview: true);
        FocusMinutesBox.Text = focusDraft;
        BreakMinutesBox.Text = breakDraft;
        QuietStartBox.Text = quietStartDraft;
        QuietEndBox.Text = quietEndDraft;
        QuietHoursCheck.IsChecked = quietEnabledDraft;
        RenderDurationFeedback();
        RenderPreferencesTransferStatus();
        if (_characterSelectionCancellation is { IsCancellationRequested: false })
            CharacterHint.Text = UiText.Get("캐릭터를 불러오고 있어요…");
        RenderFocusTimer();
        RefreshDurationPresetTooltips();
        RefreshAccessibleNames();
        RefreshTimeInputShortcutHelp();
        RefreshSettingsNavigationHelp();
    }

    private void RefreshAccessibleNames()
    {
        if (_closed) return;

        System.Windows.Automation.AutomationProperties.SetName(LanguageCombo, UiText.Get("언어"));
        System.Windows.Automation.AutomationProperties.SetName(CharacterCombo, UiText.Get("캐릭터"));
        System.Windows.Automation.AutomationProperties.SetName(PreviewStateCombo, UiText.Get("동작 미리보기"));
        System.Windows.Automation.AutomationProperties.SetName(MonitorTargetCombo, UiText.Get("펫을 데려올 화면"));
        System.Windows.Automation.AutomationProperties.SetName(FrameRateCombo, UiText.Get("프레임 제한"));
        System.Windows.Automation.AutomationProperties.SetName(ScaleSlider, UiText.Get("크기"));
        System.Windows.Automation.AutomationProperties.SetName(SpeedSlider, UiText.Get("걷기 속도"));
        System.Windows.Automation.AutomationProperties.SetName(FocusMinutesBox, UiText.Get("집중 시간(분)"));
        System.Windows.Automation.AutomationProperties.SetName(BreakMinutesBox, UiText.Get("휴식 시간(분)"));
        System.Windows.Automation.AutomationProperties.SetName(QuietStartBox, UiText.Get("방해 금지 시작 시간"));
        System.Windows.Automation.AutomationProperties.SetName(QuietEndBox, UiText.Get("방해 금지 종료 시간"));

        System.Windows.Automation.AutomationProperties.SetHelpText(FocusMinutesBox, UiText.Get("집중 1~180분, 휴식 1~60분. 시간 적용을 눌러 반영하세요."));
        System.Windows.Automation.AutomationProperties.SetHelpText(BreakMinutesBox, UiText.Get("집중 1~180분, 휴식 1~60분. 시간 적용을 눌러 반영하세요."));
        System.Windows.Automation.AutomationProperties.SetHelpText(QuietStartBox, UiText.Get(QuietHoursHelpKey));
        System.Windows.Automation.AutomationProperties.SetHelpText(QuietEndBox, UiText.Get(QuietHoursHelpKey));
    }

    private void UpdateValueLabels()
    {
        ScaleValue.Text = $"{ScaleSlider.Value * 100:F0}%";
        SpeedValue.Text = $"{SpeedSlider.Value:0.##}x";
    }

    private void RenderDurationFeedback()
    {
        if (_closed) return;
        FocusDurationStatus.Text = UiText.Get(_focusDurationStatusKey);
        QuietHoursStatus.Text = UiText.Get(_quietHoursStatusKey);
    }

    private void ApplyRenderOptions(int maximumFps, bool reducedMotion)
    {
        _framePacer.MaximumFps = maximumFps;
        if (reducedMotion) _poseTransition.Reset();
        _motionPreview?.SetRenderOptions(maximumFps, reducedMotion);
    }

    private void RenderOptions_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loading && FrameRateCombo.SelectedValue is string value && int.TryParse(value, out var fps))
            _pet.SetRenderOptions(fps, ReducedMotionCheck.IsChecked == true);
    }

    private void Fullscreen_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loading) _pet.SetPauseDuringFullscreen(FullscreenCheck.IsChecked == true);
    }

    private void UpdatePreview()
    {
        if (CharacterCombo.SelectedItem is not string name)
        {
            return;
        }

        try
        {
            // The active character is already fully decoded and frozen. Reuse it instead of
            // opening every PNG again on the UI thread after an asynchronous switch.
            var current = _pet.CurrentPresentation;
            if (ReferenceEquals(_previewPresentation, current)) { UpdateCharacterHint(); return; }
            _poseTransition.Reset();
            _previewPresentation = current;
            PreviewRig.SetPresentation(_previewPresentation);
            PreviewRig.Visibility = Visibility.Collapsed;
            PreviewImage.Visibility = Visibility.Visible;
            _liveRigActive = false;
            _lastPreviewState = PetState.Idle;
            _previewVirtualPositionPx = 0;
            _previewClock.Restart();
            _previewLastElapsedMs = _previewAnimationMs = _previewStateElapsedMs = 0;
            _walkAppearance.Reset();
            RenderPreview();
            UpdateCharacterHint();
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            _poseTransition.Reset();
            PreviewImage.Source = null;
            _previewPresentation = null;
            PreviewRig.SetPresentation(null);
            PreviewRig.Visibility = Visibility.Collapsed;
            PreviewImage.Visibility = Visibility.Visible;
            _liveRigActive = false;
            CharacterHint.Text = UiText.Get("이미지를 읽을 수 없습니다.");
        }
    }

    private void UpdateCharacterHint()
    {
        if (_previewPresentation is null) return;
        CharacterHint.Text = _previewPresentation.Definition.HasDirectionalWalk
            ? UiText.Get("걷는 방향을 바라보며 움직여요.") : UiText.Get("동작을 골라 미리 볼 수 있어요.");
        if (_previewPresentation.Definition.BehaviorProfile.Description.Length > 0)
            CharacterHint.Text += "\n" + _previewPresentation.Definition.BehaviorProfile.Description;
    }

    private void PreviewStateCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        _previewClock.Restart();
        _previewLastElapsedMs = _previewAnimationMs = _previewStateElapsedMs = 0;
        RenderPreview();
    }

    private bool NeedsLiveRendering => _previewPresentation?.Definition.WalkRig is not null &&
        (PreviewStateCombo.SelectedValue is PetState.Walk || _walkAppearance.IsSettling);

    private void OnPreviewRendering(object? sender, EventArgs e)
    {
        if (!IsPreviewActive) return;
        if (!NeedsLiveRendering) return;
        if (e is RenderingEventArgs rendering)
        {
            if (_lastPreviewRenderingTime == rendering.RenderingTime) return;
            _lastPreviewRenderingTime = rendering.RenderingTime;
            if (!_framePacer.ShouldRender(rendering.RenderingTime.TotalMilliseconds)) return;
        }
        RenderPreview();
    }

    private void RenderPreview()
    {
        if (_previewPresentation is null) return;
        var state = PreviewStateCombo.SelectedValue is PetState selected ? selected : PetState.Idle;
        var now = _previewClock.Elapsed.TotalMilliseconds;
        var elapsedMs = Math.Clamp(now - _previewLastElapsedMs, 0, 250);
        _previewLastElapsedMs = now;
        var definition = _previewPresentation.Definition;
        // This swatch has constant virtual speed instead of a moving window. Its exact
        // source-pixel travel still feeds the same distance gait as the desktop/physics preview.
        var previousPosition = _previewVirtualPositionPx;
        if (state == PetState.Walk)
            _previewVirtualPositionPx += definition.WalkSpeedPxPerSec * _pet.CurrentWalkSpeedMultiplier * elapsedMs / 1000;
        _walkAppearance.Update(state, true, definition.WalkSpeedPxPerSec * _pet.CurrentWalkSpeedMultiplier,
            definition.WalkSpeedPxPerSec, elapsedMs / 1000, definition.WalkSpeedPxPerSec * _pet.CurrentWalkSpeedMultiplier,
            _previewVirtualPositionPx - previousPosition, definition.WalkCycleDistancePx);
        var displayWalkPose = _walkAppearance.DisplayWalkPose;
        _previewStateElapsedMs += elapsedMs;
        _previewAnimationMs = displayWalkPose ? _walkAppearance.AnimationElapsedMs : _previewStateElapsedMs;
        if (displayWalkPose && PreviewRig.HasRig)
        {
            if (!_liveRigActive)
            {
                _poseTransition.Reset();
                PreviewImage.Visibility = Visibility.Hidden;
                PreviewRig.Visibility = Visibility.Visible;
                _liveRigActive = true;
            }
            if (_walkAppearance.IsSettling)
            {
                var rest = definition.GetFrames(state);
                PreviewRig.SetRestFrame(_previewPresentation.Frames[definition.GetFramePath(state, rest, _previewStateElapsedMs)], definition.UsesFrontalRestFrames(state));
            }
            PreviewRig.UpdatePose(_walkAppearance.View, _walkAppearance.GaitPhase,
                _walkAppearance.IsSettling ? _walkAppearance.SettleProgress : 0, _walkAppearance.FacingRight, elapsedMs / 1000, _pet.ReducedMotion, _walkAppearance.EntryWeight);
        }
        else
        {
            if (_liveRigActive)
            {
                _poseTransition.Reset();
                PreviewImage.Visibility = Visibility.Visible;
                PreviewRig.Visibility = Visibility.Collapsed;
                _liveRigActive = false;
            }
            var frames = displayWalkPose ? definition.GetWalkFrames(_walkAppearance.View) : definition.GetFrames(state);
            var path = displayWalkPose && definition.WalkCycleDistancePx is not null
                ? definition.GetWalkFramePath(_walkAppearance.View, _walkAppearance.GaitPhase,
                    _walkAppearance.IsSettling ? _walkAppearance.SettleProgress : 0)
                : definition.GetFramePath(displayWalkPose ? PetState.Walk : state, frames, _previewAnimationMs);
            _poseTransition.SetFrame(_previewPresentation.Frames[path], 1, frames,
                !_pet.ReducedMotion && (definition.WalkCycleDistancePx is null || !displayWalkPose) &&
                (state != PetState.Walk || _lastPreviewState == PetState.Walk));
        }
        _lastPreviewState = state;
        UpdatePreviewAnimationState();
    }

    private async void CharacterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_closed || _loading || _characterImportBusy || CharacterCombo.SelectedItem is not string name)
        {
            return;
        }

        CharacterSelectionOperation = SelectCharacterAsync(name);
        await CharacterSelectionOperation;
    }

    private async Task SelectCharacterAsync(string name)
    {
        _characterSelectionCancellation?.Cancel();
        using var cancellation = new CancellationTokenSource();
        _characterSelectionCancellation = cancellation;
        var revision = ++_characterSelectionRevision;
        CharacterHint.Text = UiText.Get("캐릭터를 불러오고 있어요…");
        try
        {
            var applied = await _pet.SwitchCharacterAsync(name, cancellation.Token);
            if (_closed || cancellation.IsCancellationRequested || revision != _characterSelectionRevision) return;
            if (applied) _motionPreview?.Close();
            _loading = true;
            try { CharacterCombo.SelectedItem = _pet.CurrentCharacterName; }
            finally { _loading = false; }
            UpdatePreview();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            if (_closed || cancellation.IsCancellationRequested || revision != _characterSelectionRevision) return;
            System.Windows.MessageBox.Show(this, ex.Message, "캐릭터를 변경할 수 없습니다", MessageBoxButton.OK, MessageBoxImage.Warning);
            _loading = true;
            try { CharacterCombo.SelectedItem = _pet.CurrentCharacterName; }
            finally { _loading = false; }
            UpdatePreview();
        }
        finally
        {
            if (ReferenceEquals(_characterSelectionCancellation, cancellation)) _characterSelectionCancellation = null;
        }
    }

    private void ScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading)
        {
            return;
        }

        UpdateValueLabels();
        _pet.SetScaleMultiplier(ScaleSlider.Value);
    }

    private void SpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading)
        {
            return;
        }

        UpdateValueLabels();
        _pet.SetWalkSpeedMultiplier(SpeedSlider.Value);
        _motionPreview?.SetSpeedMultiplier(_pet.CurrentWalkSpeedMultiplier);
    }

    private void Toggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _pet.SetVisible(VisibleCheck.IsChecked == true);
        _pet.SetHopEnabled(HopCheck.IsChecked == true);
        _pet.SetSleepEnabled(SleepCheck.IsChecked == true);
        _pet.SetWindowFallEnabled(WindowFallCheck.IsChecked == true);
    }

    private void AddCharacter_Click(object sender, RoutedEventArgs e)
    {
        if (_characterImportBusy) return;
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = UiText.Get("추가할 캐릭터 폴더 선택"),
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) == true) _characterImportOperation = ImportCharacterFolderAsync(dialog.FolderName);
    }

    private async Task ImportCharacterFolderAsync(string folder)
        => await RunCharacterImportAsync(async () => await _pet.ImportCharacterFolderAsync(folder,
            _characterImportCancellation!.Token, _characterImportProgress));

    private void AddArchive_Click(object sender, RoutedEventArgs e)
    {
        if (_characterImportBusy) return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = UiText.Get("추가할 캐릭터 ZIP 선택"),
            Filter = "캐릭터 ZIP (*.zip)|*.zip",
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) == true) _characterImportOperation = ImportCharacterArchiveAsync(dialog.FileName);
    }

    private async Task ImportCharacterArchiveAsync(string path)
        => await RunCharacterImportAsync(async () =>
        {
            var token = _characterImportCancellation!.Token;
            using var archive = await Task.Run(() => CharacterArchive.Open(path, token), token);
            if (_closed) return null;
            var selected = archive.Characters[0];
            if (archive.Characters.Count > 1)
            {
                var picker = new CharacterArchiveWindow(archive.Characters) { Owner = this };
                if (picker.ShowDialog() != true || picker.SelectedCharacter is null) return null;
                selected = picker.SelectedCharacter;
            }
            if (_closed) return null;
            return await _pet.ImportCharacterArchiveAsync(archive, selected, token, _characterImportProgress);
        });

    private async Task RunCharacterImportAsync(Func<Task<string?>> import)
    {
        if (_characterImportBusy || _closed) return;
        _preferencesTransferCancellation?.Cancel();
        ++_characterSelectionRevision;
        _characterSelectionCancellation?.Cancel();
        _loading = true;
        try { CharacterCombo.SelectedItem = _pet.CurrentCharacterName; }
        finally { _loading = false; }
        UpdatePreview();
        _characterImportBusy = true;
        var selectionRevision = _pet.CharacterSelectionRevision;
        using var cancellation = new CancellationTokenSource();
        _characterImportCancellation = cancellation;
        _characterImportProgress = new Progress<CharacterImportProgress>(value =>
        {
            if (_closed || !ReferenceEquals(_characterImportCancellation, cancellation) || cancellation.IsCancellationRequested) return;
            CharacterImportProgressBar.IsIndeterminate = value.Phase is CharacterImportPhase.Preparing or CharacterImportPhase.Validating;
            CharacterImportProgressBar.Value = value.TotalFiles > 0 ? Math.Clamp(value.CompletedFiles * 100d / value.TotalFiles, 0, 100) : 0;
            var phase = value.Phase switch
            {
                CharacterImportPhase.Extracting => UiText.Get("압축 해제"), CharacterImportPhase.Copying => UiText.Get("파일 복사"),
                CharacterImportPhase.Validating => UiText.Get("PNG 검사"), _ => UiText.Get("준비 중")
            };
            CharacterImportStatusText.Text = $"{phase} · {value.CompletedFiles}/{value.TotalFiles}";
        });
        CharacterImportProgressBar.Visibility = CancelCharacterImportButton.Visibility = Visibility.Visible;
        CharacterImportProgressBar.IsIndeterminate = true;
        CancelCharacterImportButton.IsEnabled = true;
        AddArchiveButton.IsEnabled = AddCharacterButton.IsEnabled = CharacterCombo.IsEnabled = false;
        CharacterImportStatusText.Text = UiText.Get("캐릭터를 추가하고 있어요…");
        CharacterImportStatusText.ToolTip = null;
        try
        {
            var name = await import();
            if (_closed) return;
            if (name is null)
            {
                CharacterImportStatusText.Text = UiText.Get("캐릭터 추가를 취소했어요.");
                return;
            }
            _characterGallery?.RefreshCharacters();
            _motionPreview?.Close();
            var names = await Task.Run(() => _pet.AvailableCharacterNames);
            if (_closed) return;
            var applied = !cancellation.IsCancellationRequested && selectionRevision == _pet.CharacterSelectionRevision &&
                await _pet.SwitchCharacterAsync(name, cancellation.Token);
            if (_closed) return;
            _loading = true;
            try
            {
                CharacterCombo.ItemsSource = names;
                CharacterCombo.SelectedItem = _pet.CurrentCharacterName;
            }
            finally { _loading = false; }
            UpdatePreview();
            CharacterImportStatusText.Text = !applied
                ? UiText.Get("설치는 이미 완료됐어요. 현재 친구는 바꾸지 않았습니다.") : UiText.Get("새 캐릭터를 추가했어요.");
        }
        catch (OperationCanceledException)
        {
            if (!_closed) CharacterImportStatusText.Text = UiText.Get("캐릭터 추가를 취소했어요.");
        }
        catch (Exception ex)
        {
            if (_closed) return;
            _loading = true;
            try { CharacterCombo.SelectedItem = _pet.CurrentCharacterName; }
            finally { _loading = false; }
            UpdatePreview();
            CharacterImportStatusText.Text = UiText.Get("추가하지 못했어요. 캐릭터 팩과 사용 권한을 확인해주세요.");
            CharacterImportStatusText.ToolTip = ex.Message;
        }
        finally
        {
            _characterImportBusy = false;
            _characterImportCancellation = null;
            _characterImportProgress = null;
            if (!_closed)
            {
                AddArchiveButton.IsEnabled = AddCharacterButton.IsEnabled = CharacterCombo.IsEnabled = true;
                CharacterImportProgressBar.Visibility = CancelCharacterImportButton.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void CancelCharacterImport_Click(object sender, RoutedEventArgs e)
    {
        _characterImportCancellation?.Cancel();
        CancelCharacterImportButton.IsEnabled = false;
        CharacterImportStatusText.Text = UiText.Get("취소 요청 중… 임시 파일을 정리합니다.");
    }

    private void FolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_pet.CustomCharactersRoot);
            Process.Start(new ProcessStartInfo(_pet.CustomCharactersRoot) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            CharacterImportStatusText.Text = UiText.Get("보관 폴더를 열지 못했어요. 폴더의 사용 권한을 확인해주세요.");
            CharacterImportStatusText.ToolTip = ex.Message;
        }
    }

    private void Comfort_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _pet.SetCalmBehavior(CalmCheck.IsChecked == true);
        _pet.SetAutoDismissNotifications(GentleCheck.IsChecked == true);
        _pet.SetQuietMode(QuietCheck.IsChecked == true);
    }

    private void QuietHoursApply_Click(object sender, RoutedEventArgs e)
    {
        if (_closed) return;
        if (!QuietHours.TryParse(QuietStartBox.Text, out var start) || !QuietHours.TryParse(QuietEndBox.Text, out var end))
        {
            _quietHoursStatusKey = "00:00~23:59 사이 HH:mm 형식으로 입력해주세요.";
            RenderDurationFeedback();
            return;
        }
        _pet.SetQuietHours(QuietHoursCheck.IsChecked == true, start, end);
        _quietHoursStatusKey = start == end ? "같은 시작·종료 시간은 예약을 활성화하지 않아요." : "예약을 적용했어요. 미확인 알림은 계속 보관합니다.";
        RenderDurationFeedback();
    }

    private void AssistantConnections_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.Application.Current is App app) app.ShowAssistantConnections();
    }

    private void MotionPreviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (_previewPresentation is null) return;
        _motionPreview?.Close();
        _motionPreview = new MotionPreviewWindow(_previewPresentation, _pet.CurrentWalkSpeedMultiplier, _pet.MaximumFps, _pet.ReducedMotion) { Owner = this };
        _motionPreview.Closed += (_, _) => _motionPreview = null;
        _motionPreview.Show();
    }

    private void Diagnostics_Click(object sender, RoutedEventArgs e)
    {
        if (_diagnosticWindow is not null) { _diagnosticWindow.Activate(); return; }
        try
        {
            _diagnosticWindow = new DiagnosticWindow(_pet.CreateDiagnosticReport()) { Owner = this };
            _diagnosticWindow.Closed += (_, _) => _diagnosticWindow = null;
            _diagnosticWindow.Show();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            AboutStatusText.Text = UiText.Get("진단 정보를 읽지 못했어요.");
        }
    }

    private void ReleasePage_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("https://github.com/jinwovo/desktop-pet/releases") { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        { AboutStatusText.Text = UiText.Get("브라우저를 열지 못했어요."); }
    }

    private void RenderSettingsSaveFailure()
    {
        if (!_closed)
            RetrySettingsSaveButton.Visibility = _pet.HasSettingsSaveFailure ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RetrySettingsSave_Click(object sender, RoutedEventArgs e)
    {
        if (!_closed) _pet.RetryFailedSettingsSave();
    }

    private void SetPreferencesTransferStatus(string key)
    {
        _preferencesTransferStatusKey = key;
        RenderPreferencesTransferStatus();
    }

    private void RenderPreferencesTransferStatus()
    {
        if (!_closed && _preferencesTransferStatusKey is { } key)
            PreferencesTransferStatus.Text = UiText.Get(key);
    }

    private async void BackupPreferences_Click(object sender, RoutedEventArgs e)
    {
        if (_closed || _preferencesTransferCancellation is not null) return;
        var dialog = new Microsoft.Win32.SaveFileDialog { Title = UiText.Get("설정 백업"), Filter = "DesktopPet 설정 (*.json)|*.json",
            FileName = "DesktopPet-preferences.json", DefaultExt = ".json", AddExtension = true, OverwritePrompt = true };
        if (dialog.ShowDialog(this) == true) await BackupPreferencesToFile(dialog.FileName);
    }

    private async Task BackupPreferencesToFile(string path)
    {
        if (_closed || _preferencesTransferCancellation is not null) return;
        using var cancellation = new CancellationTokenSource();
        _preferencesTransferCancellation = cancellation;
        BackupPreferencesButton.IsEnabled = RestorePreferencesButton.IsEnabled = false;
        SetPreferencesTransferStatus("설정을 백업하고 있어요…");
        try
        {
            await _pet.SavePreferencesBackupAsync(path, cancellation.Token);
            if (!_closed) SetPreferencesTransferStatus("설정을 백업했어요. AI 연결 정보는 포함하지 않았습니다.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (!_closed) SetPreferencesTransferStatus("설정 백업이 취소됐어요.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            if (!_closed) SetPreferencesTransferStatus("백업하지 못했어요. 저장 위치와 권한을 확인해주세요.");
        }
        finally
        {
            _preferencesTransferCancellation = null;
            if (!_closed) BackupPreferencesButton.IsEnabled = RestorePreferencesButton.IsEnabled = true;
        }
    }

    private async void RestorePreferences_Click(object sender, RoutedEventArgs e)
    {
        if (_closed || _characterImportBusy || _preferencesTransferCancellation is not null) return;
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = UiText.Get("설정 복원"), Filter = "DesktopPet 설정 (*.json)|*.json", Multiselect = false };
        if (dialog.ShowDialog(this) == true) await RestorePreferencesFromFile(dialog.FileName);
    }

    private async Task RestorePreferencesFromFile(string path)
    {
        if (_closed || _characterImportBusy || _preferencesTransferCancellation is not null) return;
        using var cancellation = new CancellationTokenSource();
        _preferencesTransferCancellation = cancellation;
        BackupPreferencesButton.IsEnabled = RestorePreferencesButton.IsEnabled = false;
        SetPreferencesTransferStatus("설정을 복원하고 있어요…");
        try
        {
            var (applied, missing) = await _pet.RestorePreferencesFileAsync(path, cancellation.Token);
            if (_closed) return;
            if (!applied || cancellation.IsCancellationRequested)
            {
                SetPreferencesTransferStatus("설정 복원이 취소됐어요.");
                return;
            }
            ++_characterSelectionRevision;
            _characterSelectionCancellation?.Cancel();
            LoadFromPet();
            RenderFocusTimer();
            SetPreferencesTransferStatus(missing ? "설정을 복원했어요. 설치되지 않은 캐릭터 대신 현재 친구를 유지합니다." : "설정을 복원했어요. AI 연결과 진행 중인 집중 시간은 유지합니다.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (!_closed) SetPreferencesTransferStatus("설정 복원이 취소됐어요.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            if (!_closed) SetPreferencesTransferStatus("복원하지 못했어요. 백업 형식과 캐릭터 파일을 확인해주세요.");
        }
        finally
        {
            _preferencesTransferCancellation = null;
            if (!_closed) BackupPreferencesButton.IsEnabled = RestorePreferencesButton.IsEnabled = true;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Gallery_Click(object sender, RoutedEventArgs e)
    {
        if (_characterGallery is not null) { _characterGallery.Activate(); return; }
        _characterGallery = new CharacterGalleryWindow(_pet) { Owner = this };
        _characterGallery.CharacterApplied += name =>
        {
            if (_closed || _characterGallery is null) return;
            _loading = true;
            try
            {
                CharacterCombo.ItemsSource = _characterGallery.AllCharacters.Select(item => item.Name).ToArray();
                CharacterCombo.SelectedItem = name;
            }
            finally { _loading = false; }
            UpdatePreview();
        };
        _characterGallery.Closed += (_, _) => _characterGallery = null;
        _characterGallery.Show();
    }

    private void RenderFocusTimer()
    {
        if (_closed) return;
        FocusStatusText.Text = _pet.FocusTimerCaption;
        FocusCountText.Text = _pet.FocusCountCaption;
        FocusResetCountButton.IsEnabled = _pet.FocusCanResetCount;
        FocusResetCountButton.Content = UiText.Get("횟수 초기화");
        FocusResetCountButton.ToolTip = UiText.Get("앱을 닫으면 사라지는 횟수예요. 초기화해도 타이머와 알림은 유지해요.");
        FocusStartButton.Content = _pet.FocusStartCaption;
        FocusStartButton.IsEnabled = _pet.FocusCanStart;
        FocusPauseButton.Content = _pet.FocusSnapshot.Status == FocusTimerStatus.Paused ? UiText.Get("계속") : UiText.Get("일시정지");
        FocusPauseButton.IsEnabled = _pet.FocusCanPause;
        FocusExtendButton.IsEnabled = _pet.FocusCanExtend;
        FocusExtendButton.Content = UiText.Get("5분 추가");
        FocusExtendButton.ToolTip = UiText.Get("이번 세션에만 5분을 추가해요. 기본 시간은 바뀌지 않아요.");
        FocusRestartButton.IsEnabled = FocusStopButton.IsEnabled = _pet.FocusCanStop;
    }

    private void FocusStart_Click(object sender, RoutedEventArgs e) => _pet.StartFocusTimer();
    private void Preset25_Click(object sender, RoutedEventArgs e) => FillDurationDraft("25", "5");
    private void Preset50_Click(object sender, RoutedEventArgs e) => FillDurationDraft("50", "10");

    private void FillDurationDraft(string focus, string rest)
    {
        if (_closed) return;
        FocusMinutesBox.Text = focus;
        BreakMinutesBox.Text = rest;
        _focusDurationStatusKey = "입력칸만 채웠어요. 시간 적용을 눌러 반영하세요.";
        RenderDurationFeedback();
    }

    private void RefreshDurationPresetTooltips()
    {
        FocusPreset25Button.ToolTip = FocusPreset50Button.ToolTip = UiText.Get("입력칸만 채웁니다. 현재 타이머와 기본 시간은 바뀌지 않아요.");
    }

    private void FocusDurationApply_Click(object sender, RoutedEventArgs e)
    {
        if (_closed) return;
        if (!int.TryParse(FocusMinutesBox.Text, out var focus) || !int.TryParse(BreakMinutesBox.Text, out var rest) ||
            focus is < 1 or > 180 || rest is < 1 or > 60)
        {
            _focusDurationStatusKey = "집중 1~180분, 휴식 1~60분을 입력해주세요.";
            RenderDurationFeedback();
            return;
        }
        _pet.SetFocusDurations(focus, rest);
        _focusDurationStatusKey = "다음 시작·다시 시작부터 적용해요. 진행 중인 시간은 유지합니다.";
        RenderDurationFeedback();
    }
    private void FocusPause_Click(object sender, RoutedEventArgs e) => _pet.PauseOrResumeFocusTimer();
    private void FocusExtend_Click(object sender, RoutedEventArgs e)
    {
        if (!_closed) _pet.ExtendFocusTimer();
    }
    private void FocusResetCount_Click(object sender, RoutedEventArgs e)
    {
        if (!_closed) _pet.ResetFocusCount();
    }
    private void FocusRestart_Click(object sender, RoutedEventArgs e) => _pet.RestartFocusTimer();
    private void FocusStop_Click(object sender, RoutedEventArgs e) => _pet.StopFocusTimer();
    private void TestNotification_Click(object sender, RoutedEventArgs e) => _pet.SendTestNotification();
}
