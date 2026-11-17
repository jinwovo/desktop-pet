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
    private bool _characterImportBusy;
    private Task _characterImportOperation = Task.CompletedTask;
    private CharacterPresentation? _previewPresentation;
    private MotionPreviewWindow? _motionPreview;
    private readonly Stopwatch _previewClock = Stopwatch.StartNew();
    private double _previewLastElapsedMs;
    private double _previewAnimationMs;
    private readonly WalkAppearanceController _walkAppearance = new(new Random(7));
    private readonly FramePacer _framePacer = new();
    private readonly PoseTransitionPresenter _poseTransition;
    private PetState _lastPreviewState = PetState.Idle;
    private double _previewVirtualPositionPx;
    private bool _liveRigActive;
    private CharacterGalleryWindow? _characterGallery;
    private TimeSpan? _lastPreviewRenderingTime;
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private sealed record PreviewChoice(string Label, PetState State);

    public SettingsWindow(MainWindow pet)
    {
        _pet = pet;
        InitializeComponent();
        _poseTransition = new PoseTransitionPresenter(PreviewImage, PreviousPreviewImage);
        MaxHeight = Math.Max(500, SystemParameters.WorkArea.Height - 60);
        LoadFromPet();
        RenderFocusTimer();
        _pet.FocusTimerChanged += RenderFocusTimer;
        _pet.RenderOptionsChanged += ApplyRenderOptions;
        ApplyRenderOptions(_pet.MaximumFps, _pet.ReducedMotion);
        _previewTimer.Tick += (_, _) => { if (!NeedsLiveRendering) RenderPreview(); };
        Loaded += (_, _) => { _previewTimer.Start(); CompositionTarget.Rendering += OnPreviewRendering; };
        Closed += (_, _) =>
        {
            _closed = true;
            _pet.FocusTimerChanged -= RenderFocusTimer;
            _pet.RenderOptionsChanged -= ApplyRenderOptions;
            _previewTimer.Stop();
            CompositionTarget.Rendering -= OnPreviewRendering;
            _poseTransition.Reset();
            PreviewRig.SetPresentation(null);
            _motionPreview?.Close();
            _characterGallery?.Close();
        };
    }

    private void LoadFromPet()
    {
        _loading = true;
        PreviewStateCombo.ItemsSource = new[]
        {
            new PreviewChoice("가만히 있기", PetState.Idle), new PreviewChoice("걷기", PetState.Walk),
            new PreviewChoice("울기", PetState.Cry), new PreviewChoice("화내기", PetState.Angry),
            new PreviewChoice("잠자기", PetState.Sleep), new PreviewChoice("떨어지기", PetState.Falling),
        };
        PreviewStateCombo.SelectedValue = PetState.Idle;

        CharacterCombo.ItemsSource = _pet.AvailableCharacterNames;
        CharacterCombo.SelectedItem = _pet.CurrentCharacterName;

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

        _loading = false;

        UpdateValueLabels();
        UpdatePreview();
    }

    private void UpdateValueLabels()
    {
        ScaleValue.Text = $"{ScaleSlider.Value * 100:F0}%";
        SpeedValue.Text = $"{SpeedSlider.Value:0.##}x";
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
            _poseTransition.Reset();
            _previewPresentation = CharacterPresentation.Load(_pet.ResolveCharacterFolder(name));
            PreviewRig.SetPresentation(_previewPresentation);
            PreviewRig.Visibility = Visibility.Collapsed;
            PreviewImage.Visibility = Visibility.Visible;
            _liveRigActive = false;
            _lastPreviewState = PetState.Idle;
            _previewVirtualPositionPx = 0;
            _previewClock.Restart();
            _previewLastElapsedMs = _previewAnimationMs = 0;
            _walkAppearance.Reset();
            RenderPreview();
            CharacterHint.Text = _previewPresentation.Definition.HasDirectionalWalk
                ? "걷는 방향을 바라보며 움직여요."
                : "동작을 골라 미리 볼 수 있어요.";
            if (_previewPresentation.Definition.BehaviorProfile.Description.Length > 0)
                CharacterHint.Text += "\n" + _previewPresentation.Definition.BehaviorProfile.Description;
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
            CharacterHint.Text = "이미지를 읽을 수 없습니다.";
        }
    }

    private void PreviewStateCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        _previewClock.Restart();
        _previewLastElapsedMs = _previewAnimationMs = 0;
        _walkAppearance.Reset();
        RenderPreview();
    }

    private bool NeedsLiveRendering => _previewPresentation?.Definition.WalkRig is not null &&
        (PreviewStateCombo.SelectedValue is PetState.Walk || _walkAppearance.IsSettling);

    private void OnPreviewRendering(object? sender, EventArgs e)
    {
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
        _previewAnimationMs = displayWalkPose ? _walkAppearance.AnimationElapsedMs : _previewAnimationMs + elapsedMs;
        var frames = displayWalkPose ? definition.GetWalkFrames(_walkAppearance.View) : definition.GetFrames(state);
        var path = displayWalkPose && definition.WalkCycleDistancePx is not null
            ? definition.GetWalkFramePath(_walkAppearance.View, _walkAppearance.GaitPhase,
                _walkAppearance.IsSettling ? _walkAppearance.SettleProgress : 0)
            : definition.GetFramePath(displayWalkPose ? PetState.Walk : state, frames, _previewAnimationMs);
        if (displayWalkPose && PreviewRig.HasRig)
        {
            if (!_liveRigActive)
            {
                _poseTransition.Reset();
                PreviewImage.Visibility = Visibility.Hidden;
                PreviewRig.Visibility = Visibility.Visible;
                _liveRigActive = true;
            }
            PreviewRig.UpdatePose(_walkAppearance.View, _walkAppearance.GaitPhase,
                _walkAppearance.IsSettling ? _walkAppearance.SettleProgress : 0, _walkAppearance.FacingRight);
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
            _poseTransition.SetFrame(_previewPresentation.Frames[path], 1, frames,
                !_pet.ReducedMotion && (definition.WalkCycleDistancePx is null || !displayWalkPose) &&
                (state != PetState.Walk || _lastPreviewState == PetState.Walk));
        }
        _lastPreviewState = state;
    }

    private void CharacterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || CharacterCombo.SelectedItem is not string name)
        {
            return;
        }

        try { _pet.SwitchCharacter(name); _motionPreview?.Close(); }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            System.Windows.MessageBox.Show(this, ex.Message, "캐릭터를 변경할 수 없습니다", MessageBoxButton.OK, MessageBoxImage.Warning);
            _loading = true;
            CharacterCombo.SelectedItem = _pet.CurrentCharacterName;
            _loading = false;
        }
        UpdatePreview();
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
            Title = "추가할 캐릭터 폴더 선택",
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) == true) _characterImportOperation = ImportCharacterFolderAsync(dialog.FolderName);
    }

    private async Task ImportCharacterFolderAsync(string folder)
        => await RunCharacterImportAsync(async () => await _pet.ImportCharacterFolderAsync(folder));

    private void AddArchive_Click(object sender, RoutedEventArgs e)
    {
        if (_characterImportBusy) return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "추가할 캐릭터 ZIP 선택",
            Filter = "캐릭터 ZIP (*.zip)|*.zip",
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) == true) _characterImportOperation = ImportCharacterArchiveAsync(dialog.FileName);
    }

    private async Task ImportCharacterArchiveAsync(string path)
        => await RunCharacterImportAsync(async () =>
        {
            using var archive = await Task.Run(() => CharacterArchive.Open(path));
            if (_closed) return null;
            var selected = archive.Characters[0];
            if (archive.Characters.Count > 1)
            {
                var picker = new CharacterArchiveWindow(archive.Characters) { Owner = this };
                if (picker.ShowDialog() != true || picker.SelectedCharacter is null) return null;
                selected = picker.SelectedCharacter;
            }
            if (_closed) return null;
            return await _pet.ImportCharacterArchiveAsync(archive, selected);
        });

    private async Task RunCharacterImportAsync(Func<Task<string?>> import)
    {
        if (_characterImportBusy || _closed) return;
        _characterImportBusy = true;
        AddArchiveButton.IsEnabled = AddCharacterButton.IsEnabled = CharacterCombo.IsEnabled = false;
        CharacterImportStatusText.Text = "캐릭터를 추가하고 있어요…";
        CharacterImportStatusText.ToolTip = null;
        try
        {
            var name = await import();
            if (_closed) return;
            if (name is null)
            {
                CharacterImportStatusText.Text = "캐릭터 추가를 취소했어요.";
                return;
            }
            _characterGallery?.RefreshCharacters();
            _motionPreview?.Close();
            _loading = true;
            try
            {
                CharacterCombo.ItemsSource = _pet.AvailableCharacterNames;
                _pet.SwitchCharacter(name);
                CharacterCombo.SelectedItem = name;
            }
            finally { _loading = false; }
            UpdatePreview();
            CharacterImportStatusText.Text = "새 캐릭터를 추가했어요.";
        }
        catch (Exception ex)
        {
            if (_closed) return;
            _loading = true;
            try { CharacterCombo.SelectedItem = _pet.CurrentCharacterName; }
            finally { _loading = false; }
            UpdatePreview();
            CharacterImportStatusText.Text = "추가하지 못했어요. 캐릭터 팩과 사용 권한을 확인해주세요.";
            CharacterImportStatusText.ToolTip = ex.Message;
        }
        finally
        {
            _characterImportBusy = false;
            if (!_closed) AddArchiveButton.IsEnabled = AddCharacterButton.IsEnabled = CharacterCombo.IsEnabled = true;
        }
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
            CharacterImportStatusText.Text = "보관 폴더를 열지 못했어요. 폴더의 사용 권한을 확인해주세요.";
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
        if (!QuietHours.TryParse(QuietStartBox.Text, out var start) || !QuietHours.TryParse(QuietEndBox.Text, out var end))
        {
            QuietHoursStatus.Text = "00:00~23:59 사이 HH:mm 형식으로 입력해주세요.";
            return;
        }
        _pet.SetQuietHours(QuietHoursCheck.IsChecked == true, start, end);
        QuietHoursStatus.Text = start == end ? "같은 시작·종료 시간은 예약을 활성화하지 않아요." : "예약을 적용했어요. 미확인 알림은 계속 보관합니다.";
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

    private void BackupPreferences_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Title = "설정 백업", Filter = "DesktopPet 설정 (*.json)|*.json",
            FileName = "DesktopPet-preferences.json", DefaultExt = ".json", AddExtension = true, OverwritePrompt = true };
        if (dialog.ShowDialog(this) == true) BackupPreferencesToFile(dialog.FileName);
    }

    private void BackupPreferencesToFile(string path)
    {
        try
        {
            _pet.SavePreferencesBackup(path);
            PreferencesTransferStatus.Text = "설정을 백업했어요. AI 연결 정보는 포함하지 않았습니다.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            PreferencesTransferStatus.Text = "백업하지 못했어요. 저장 위치와 권한을 확인해주세요.";
        }
    }

    private void RestorePreferences_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "설정 복원", Filter = "DesktopPet 설정 (*.json)|*.json", Multiselect = false };
        if (dialog.ShowDialog(this) == true) RestorePreferencesFromFile(dialog.FileName);
    }

    private void RestorePreferencesFromFile(string path)
    {
        try
        {
            var missing = _pet.RestorePreferencesJson(PetSettingsBackup.ReadFile(path));
            LoadFromPet();
            RenderFocusTimer();
            PreferencesTransferStatus.Text = missing ? "설정을 복원했어요. 설치되지 않은 캐릭터 대신 현재 친구를 유지합니다." : "설정을 복원했어요. AI 연결과 진행 중인 집중 시간은 유지합니다.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            PreferencesTransferStatus.Text = "복원하지 못했어요. 백업 형식과 캐릭터 파일을 확인해주세요.";
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Gallery_Click(object sender, RoutedEventArgs e)
    {
        if (_characterGallery is not null) { _characterGallery.Activate(); return; }
        _characterGallery = new CharacterGalleryWindow(_pet) { Owner = this };
        _characterGallery.CharacterApplied += name =>
        {
            _loading = true;
            CharacterCombo.ItemsSource = _pet.AvailableCharacterNames;
            CharacterCombo.SelectedItem = name;
            _loading = false;
            UpdatePreview();
        };
        _characterGallery.Closed += (_, _) => _characterGallery = null;
        _characterGallery.Show();
    }

    private void RenderFocusTimer()
    {
        if (_closed) return;
        FocusStatusText.Text = _pet.FocusTimerCaption;
        FocusStartButton.Content = _pet.FocusStartCaption;
        FocusStartButton.IsEnabled = _pet.FocusCanStart;
        FocusPauseButton.Content = _pet.FocusSnapshot.Status == FocusTimerStatus.Paused ? "계속" : "일시정지";
        FocusPauseButton.IsEnabled = _pet.FocusCanPause;
        FocusRestartButton.IsEnabled = FocusStopButton.IsEnabled = _pet.FocusCanStop;
    }

    private void FocusStart_Click(object sender, RoutedEventArgs e) => _pet.StartFocusTimer();
    private void FocusDurationApply_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(FocusMinutesBox.Text, out var focus) || !int.TryParse(BreakMinutesBox.Text, out var rest) ||
            focus is < 1 or > 180 || rest is < 1 or > 60)
        {
            FocusDurationStatus.Text = "집중 1~180분, 휴식 1~60분을 입력해주세요.";
            return;
        }
        _pet.SetFocusDurations(focus, rest);
        FocusDurationStatus.Text = "다음 시작·다시 시작부터 적용해요. 진행 중인 시간은 유지합니다.";
    }
    private void FocusPause_Click(object sender, RoutedEventArgs e) => _pet.PauseOrResumeFocusTimer();
    private void FocusRestart_Click(object sender, RoutedEventArgs e) => _pet.RestartFocusTimer();
    private void FocusStop_Click(object sender, RoutedEventArgs e) => _pet.StopFocusTimer();
    private void TestNotification_Click(object sender, RoutedEventArgs e) => _pet.SendTestNotification();
}
