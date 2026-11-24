using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using DesktopPet.Core;

namespace DesktopPet.App;

public enum CharacterGalleryCategory { All, Original, FanArt, Custom }

public sealed record CharacterGalleryItem(string Name, string EnglishName, CharacterGalleryCategory Category,
    BitmapImage? Thumbnail, bool IsAvailable, string Detail, string SearchText)
{
    public string CategoryLabel => Category switch
    {
        CharacterGalleryCategory.Original => UiText.Get("오리지널"), CharacterGalleryCategory.FanArt => UiText.Get("팬아트 팩"), _ => UiText.Get("추가한 캐릭터"),
    };
}

public partial class CharacterGalleryWindow : Window
{
    private readonly MainWindow _pet;
    private readonly Func<string, CancellationToken, CharacterPresentation> _previewLoader;
    private readonly Func<CancellationToken, IReadOnlyList<string>> _catalogDiscovery;
    private readonly Func<string, string?> _chooseExportPath;
    private readonly Func<string, string, CancellationToken, IProgress<CharacterExportProgress>?, Task<string>> _exporter;
    private CancellationTokenSource? _exportCancellation;
    private bool _exportBusy;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _loadCancellation;
    private long _loadRevision;
    private CancellationTokenSource? _previewCancellation;
    private CharacterGalleryItem? _previewSelection;
    private long _previewRevision;
    private IReadOnlyList<CharacterGalleryItem> _all = Array.Empty<CharacterGalleryItem>();
    private bool _closed, _started, _filtering, _previewBusy;
    private bool _catalogLoading, _catalogFailed;
    private bool _applyBusy;
    private string _statusKey = "";
    private object[] _statusArguments = [];
    private MotionPreviewWindow? _motionPreview;
    private sealed record CategoryChoice(string Label, CharacterGalleryCategory Category);
    private sealed record Alias(string EnglishName, string[] Names);
    private static readonly Dictionary<string, Alias> BuiltinAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["치이카와"] = new("Chiikawa", ["ちいかわ"]), ["세균맨"] = new("Baikinman", ["바이킨맨", "ばいきんまん"]),
        ["헬로키티"] = new("Hello Kitty", ["hellokitty", "ハローキティ"]), ["폼폼푸린"] = new("Pompompurin", ["pompom", "ポムポムプリン"]),
        ["시나모롤"] = new("Cinnamoroll", ["시나몬롤", "cinnamonroll", "シナモロール"]), ["쿠로미"] = new("Kuromi", ["クロミ"]),
    };

    public CharacterGalleryWindow(MainWindow pet, Func<string, CancellationToken, CharacterPresentation>? previewLoader = null,
        Func<CancellationToken, IReadOnlyList<string>>? catalogDiscovery = null,
        Func<string, string?>? chooseExportPath = null,
        Func<string, string, CancellationToken, IProgress<CharacterExportProgress>?, Task<string>>? exporter = null)
    {
        _pet = pet;
        _previewLoader = previewLoader ?? ((folder, cancellation) => CharacterPresentation.Load(folder, cancellationToken: cancellation));
        _catalogDiscovery = catalogDiscovery ?? (_ => _pet.AvailableCharacterNames);
        _chooseExportPath = chooseExportPath ?? ChooseExportPath;
        _exporter = exporter ?? _pet.ExportCharacterAsync;
        _pet.RenderOptionsChanged += ApplyRenderOptions;
        InitializeComponent();
        MaxHeight = Math.Max(MinHeight, SystemParameters.WorkArea.Height - 40);
        SetCategoryChoices(CharacterGalleryCategory.All);
        Loaded += (_, _) => { if (!_started) { _started = true; RefreshCharacters(); } };
        Closed += (_, _) =>
        {
            _closed = true;
            CancelPreview();
            _pet.RenderOptionsChanged -= ApplyRenderOptions;
            UiText.LanguageChanged -= RefreshLanguage;
            _lifetime.Cancel();
            _loadCancellation?.Dispose();
            _lifetime.Dispose();
            _motionPreview?.Close();
        };
        UiText.Attach(this);
        Loaded += (_, _) => RefreshLanguage();
        // Translate static controls first, then redraw stateful labels that must not be reset
        // to the empty strings present when UiText first attached to this window.
        UiText.LanguageChanged += RefreshLanguage;
    }

    public Task LoadingTask { get; private set; } = Task.CompletedTask;
    public Task PreviewOperation { get; private set; } = Task.CompletedTask;
    public Task ApplyOperation { get; private set; } = Task.CompletedTask;
    public Task ExportOperation { get; private set; } = Task.CompletedTask;
    public IReadOnlyList<CharacterGalleryItem> AllCharacters => _all;
    public event Action<string>? CharacterApplied;
    private void ApplyRenderOptions(int fps, bool reducedMotion) => _motionPreview?.SetRenderOptions(fps, reducedMotion);

    private void SetCategoryChoices(CharacterGalleryCategory category)
    {
        _filtering = true;
        try
        {
            CategoryCombo.ItemsSource = new[]
            {
                new CategoryChoice(UiText.Get("전체 친구"), CharacterGalleryCategory.All), new CategoryChoice(UiText.Get("오리지널"), CharacterGalleryCategory.Original),
                new CategoryChoice(UiText.Get("팬아트 팩"), CharacterGalleryCategory.FanArt), new CategoryChoice(UiText.Get("추가한 캐릭터"), CharacterGalleryCategory.Custom),
            };
            CategoryCombo.SelectedValue = category;
        }
        finally { _filtering = false; }
    }

    private void RefreshLanguage()
    {
        if (_closed) return;
        var selected = (CharacterCards.SelectedItem as CharacterGalleryItem)?.Name;
        var category = CategoryCombo.SelectedValue is CharacterGalleryCategory value ? value : CharacterGalleryCategory.All;
        SetCategoryChoices(category);
        FilterCharacters();
        CharacterCards.Items.Refresh();
        CharacterCards.SelectedItem = CharacterCards.Items.OfType<CharacterGalleryItem>().FirstOrDefault(item => item.Name == selected);
        UpdateSelection();
        RenderStatus();
    }

    private void SetStatus(string key, params object[] arguments)
    {
        _statusKey = key;
        _statusArguments = arguments;
        RenderStatus();
    }

    private void RenderStatus() => StatusText.Text = _statusArguments.Length == 0
        ? UiText.Get(_statusKey) : UiText.Format(_statusKey, _statusArguments);

    private void RenderCount(int visibleCount)
    {
        CountText.Text = _catalogLoading ? UiText.Get("친구들을 불러오고 있어요…")
            : _catalogFailed ? UiText.Get("목록을 불러오지 못했어요.")
            : _started ? UiText.Format("{0}종 중 {1}종", _all.Count, visibleCount) : "";
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (_closed || _catalogLoading || !_started) return;
        RefreshCharacters();
    }

    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (!e.Handled && e.Key == Key.F5)
        {
            e.Handled = true;
            Refresh_Click(this, new RoutedEventArgs());
        }
    }

    /// <summary>Reload the library after import without applying a character or changing filters.</summary>
    public void RefreshCharacters()
    {
        Dispatcher.VerifyAccess();
        if (_closed || !_started) return;
        CancelPreview();
        UpdateSelection();
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        LoadingTask = LoadCharactersAsync(++_loadRevision, _loadCancellation.Token);
    }

    private async Task LoadCharactersAsync(long revision, CancellationToken cancellation)
    {
        _catalogLoading = true;
        _catalogFailed = false;
        RefreshButton.IsEnabled = false;
        RenderCount(CharacterCards.Items.Count);
        try
        {
            var items = await Task.Run(() =>
            {
                cancellation.ThrowIfCancellationRequested();
                // Directory enumeration and catalog JSON reads can block on slow custom drives.
                // Keep them with thumbnail IO on the worker, before returning frozen cards to WPF.
                var names = _catalogDiscovery(cancellation);
                cancellation.ThrowIfCancellationRequested();
                var aliases = ReadAliases();
                cancellation.ThrowIfCancellationRequested();
                var result = new List<CharacterGalleryItem>();
                foreach (var name in names)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var alias = aliases.GetValueOrDefault(name) ?? new Alias(name, []);
                    var category = CharacterGalleryCategory.Custom;
                    try
                    {
                        var folder = _pet.ResolveCharacterFolder(name);
                        var customPrefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_pet.CustomCharactersRoot)) + Path.DirectorySeparatorChar;
                        var custom = Path.GetFullPath(folder).StartsWith(customPrefix, StringComparison.OrdinalIgnoreCase);
                        if (!custom && (name is "Companion" or "Nimbus" or "Ember")) category = CharacterGalleryCategory.Original;
                        else if (!custom && aliases.ContainsKey(name)) category = CharacterGalleryCategory.FanArt;
                        var thumbnail = CharacterPresentation.LoadThumbnail(folder);
                        result.Add(new(name, alias.EnglishName, category, thumbnail, true,
                            category == CharacterGalleryCategory.FanArt ? "선택형 팬아트 팩" : category == CharacterGalleryCategory.Custom ? "직접 추가한 친구" : "DesktopPet 오리지널 친구",
                            SearchText(name, alias)));
                    }
                    catch (Exception ex) when (IsCharacterError(ex))
                    {
                        result.Add(new(name, alias.EnglishName, category, null, false, "캐릭터 그림을 읽을 수 없어요.", SearchText(name, alias)));
                    }
                }
                return result;
            }, cancellation);
            if (_closed || revision != _loadRevision) return;
            var selectedName = (CharacterCards.SelectedItem as CharacterGalleryItem)?.Name ??
                (_all.Count == 0 ? _pet.CurrentCharacterName : null);
            _all = items;
            _catalogLoading = false;
            FilterCharacters();
            CharacterCards.SelectedItem = CharacterCards.Items.OfType<CharacterGalleryItem>().FirstOrDefault(item => item.Name == selectedName);
            SetStatus("현재 친구: {0}", _pet.CurrentCharacterName);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (IsCharacterError(ex))
        {
            if (!_closed && revision == _loadRevision)
            {
                _catalogLoading = false;
                _catalogFailed = true;
                RenderCount(CharacterCards.Items.Count);
                SetStatus("캐릭터 폴더를 확인하고 새로고침해주세요.");
            }
        }
        finally
        {
            if (!_closed && revision == _loadRevision) RefreshButton.IsEnabled = true;
        }
    }

    private static bool IsCharacterError(Exception ex) => ex is InvalidDataException or IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or FormatException;
    private static string SearchText(string name, Alias alias) => (name + " " + alias.EnglishName + " " + string.Join(' ', alias.Names)).Normalize(NormalizationForm.FormKC);

    private static Dictionary<string, Alias> ReadAliases()
    {
        var result = new Dictionary<string, Alias>(BuiltinAliases, StringComparer.OrdinalIgnoreCase);
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "CharacterCatalog.json");
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 128 * 1024) return result;
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("characters", out var entries) || entries.ValueKind != JsonValueKind.Array) return result;
            foreach (var entry in entries.EnumerateArray().Take(64))
            {
                if (!entry.TryGetProperty("name", out var nameElement) || nameElement.ValueKind != JsonValueKind.String || nameElement.GetString() is not { Length: > 0 and <= 100 } name) continue;
                var english = entry.TryGetProperty("englishName", out var englishElement) && englishElement.ValueKind == JsonValueKind.String ? englishElement.GetString() ?? name : name;
                var aliases = entry.TryGetProperty("aliases", out var values) && values.ValueKind == JsonValueKind.Array
                    ? values.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String).Take(16).Select(value => value.GetString() ?? "").Where(value => value.Length <= 100).ToArray() : Array.Empty<string>();
                if (english.Length <= 100)
                    result[name] = new(english, aliases.Concat(result.GetValueOrDefault(name)?.Names ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
            }
        }
        catch (Exception ex) when (IsCharacterError(ex) || ex is JsonException) { }
        return result;
    }

    private void Filter_Changed(object sender, TextChangedEventArgs e) { if (IsInitialized && !_filtering) FilterCharacters(); }
    private void Category_Changed(object sender, SelectionChangedEventArgs e) { if (IsInitialized && !_filtering) FilterCharacters(); }

    private void FilterCharacters()
    {
        if (CharacterCards is null || CategoryCombo is null || SearchBox is null) return;
        var previous = CharacterCards.SelectedItem as CharacterGalleryItem;
        var query = SearchBox.Text.Trim().Normalize(NormalizationForm.FormKC);
        var category = CategoryCombo.SelectedValue is CharacterGalleryCategory selected ? selected : CharacterGalleryCategory.All;
        var visible = _all.Where(item => (category == CharacterGalleryCategory.All || item.Category == category) && item.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        _filtering = true;
        CharacterCards.ItemsSource = visible;
        CharacterCards.SelectedItem = previous is not null && visible.Contains(previous) ? previous : null;
        _filtering = false;
        EmptyText.Visibility = visible.Length == 0 && _started && !_catalogLoading && !_catalogFailed ? Visibility.Visible : Visibility.Collapsed;
        RenderCount(visible.Length);
        UpdateSelection();
    }

    private void CharacterCards_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (!_filtering) UpdateSelection(); }
    private void UpdateSelection()
    {
        if (PreviewImage is null) return;
        var item = CharacterCards.SelectedItem as CharacterGalleryItem;
        if (_previewBusy && !ReferenceEquals(item, _previewSelection)) CancelPreview();
        PreviewImage.Source = item?.Thumbnail;
        SelectedNameText.Text = item?.Name ?? "";
        SelectedDetailText.Text = UiText.Get(item?.Detail ?? "친구를 선택해보세요.");
        ApplyButton.IsEnabled = item is { IsAvailable: true } && !_applyBusy;
        PreviewButton.IsEnabled = item is { IsAvailable: true } && !_previewBusy;
        ExportButton.IsEnabled = item is { IsAvailable: true } && !_exportBusy;
    }

    private async void Preview_Click(object sender, RoutedEventArgs e)
    {
        PreviewOperation = PreviewSelectedAsync();
        await PreviewOperation;
    }

    private void CancelPreview()
    {
        var cancellation = _previewCancellation;
        if (cancellation is null) return;
        _previewCancellation = null;
        _previewSelection = null;
        _previewBusy = false;
        ++_previewRevision;
        cancellation.Cancel();
    }

    private async Task PreviewSelectedAsync()
    {
        if (_closed || _previewBusy || CharacterCards.SelectedItem is not CharacterGalleryItem { IsAvailable: true } selected) return;

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var token = cancellation.Token;
        _previewCancellation = cancellation;
        _previewBusy = true;
        _previewSelection = selected;
        var revision = ++_previewRevision;
        UpdateSelection();

        try
        {
            var folder = _pet.ResolveCharacterFolder(selected.Name);
            var presentation = await Task.Run(() => _previewLoader(folder, token), token);

            if (_closed || token.IsCancellationRequested || revision != _previewRevision || !ReferenceEquals(CharacterCards.SelectedItem, selected)) return;

            _motionPreview?.Close();
            var preview = new MotionPreviewWindow(presentation, _pet.CurrentWalkSpeedMultiplier, _pet.MaximumFps, _pet.ReducedMotion) { Owner = this };
            preview.Closed += (s, e) => { if (ReferenceEquals(_motionPreview, preview)) _motionPreview = null; };
            _motionPreview = preview;
            _motionPreview.Show();
            SetStatus("미리보기는 현재 친구를 바꾸지 않아요.");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (IsCharacterError(ex))
        {
            if (!_closed && !token.IsCancellationRequested && revision == _previewRevision && ReferenceEquals(CharacterCards.SelectedItem, selected))
                SetStatus("움직임을 읽지 못했어요. 캐릭터 파일을 확인해주세요.");
        }
        finally
        {
            if (revision == _previewRevision)
            {
                _previewBusy = false;
                _previewCancellation = null;
                _previewSelection = null;
                if (!_closed) UpdateSelection();
            }
        }
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_closed || _applyBusy) return;
        ApplyOperation = ApplySelectedAsync();
        await ApplyOperation;
    }

    private async Task ApplySelectedAsync()
    {
        if (_closed || _applyBusy || CharacterCards.SelectedItem is not CharacterGalleryItem { IsAvailable: true } item) return;
        _applyBusy = true;
        CancelPreview();
        UpdateSelection();
        SetStatus("캐릭터를 불러오고 있어요…");
        try
        {
            var applied = await _pet.SwitchCharacterAsync(item.Name, _lifetime.Token);
            if (_closed) return;
            if (!applied) { SetStatus("현재 친구: {0}", _pet.CurrentCharacterName); return; }
            CharacterApplied?.Invoke(item.Name);
            SetStatus("{0} 적용했어요.", item.Name);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (IsCharacterError(ex))
        {
            if (!_closed) SetStatus("적용하지 못했어요. 현재 친구는 그대로예요.");
        }
        finally
        {
            _applyBusy = false;
            if (!_closed) UpdateSelection();
        }
    }

    private string? ChooseExportPath(string name)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = UiText.Get("캐릭터 ZIP 저장"), Filter = "ZIP (*.zip)|*.zip", DefaultExt = ".zip",
            FileName = name + ".zip", AddExtension = true, CheckPathExists = true, OverwritePrompt = false,
        };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_closed || _exportBusy || CharacterCards.SelectedItem is not CharacterGalleryItem { IsAvailable: true } item) return;
        var destination = _chooseExportPath(item.Name);
        if (_closed || _exportBusy || destination is null) return;
        ExportOperation = ExportSelectedAsync(item.Name, destination);
        await ExportOperation;
    }

    private async Task ExportSelectedAsync(string name, string destination)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _exportCancellation = cancellation;
        _exportBusy = true;
        UpdateSelection();
        CancelExportButton.Visibility = Visibility.Visible;
        CancelExportButton.IsEnabled = true;
        SetStatus("{0} ZIP을 내보내고 있어요…", name);
        var progress = new Progress<CharacterExportProgress>(value =>
        {
            if (_closed || cancellation.IsCancellationRequested || !ReferenceEquals(_exportCancellation, cancellation)) return;
            SetStatus("{0} ZIP 내보내는 중… {1}/{2}", name, value.CompletedFiles, value.TotalFiles);
        });
        try
        {
            await _exporter(name, destination, cancellation.Token, progress);
            if (!_closed) SetStatus("캐릭터 ZIP을 저장했어요.");
        }
        catch (OperationCanceledException)
        {
            if (!_closed) SetStatus("ZIP 내보내기를 취소했어요.");
        }
        catch (Exception ex) when (IsCharacterError(ex) || ex is ObjectDisposedException)
        {
            if (!_closed) SetStatus("ZIP을 저장하지 못했어요. 새 파일 이름과 캐릭터 그림을 확인해주세요.");
        }
        finally
        {
            _exportCancellation = null;
            _exportBusy = false;
            if (!_closed)
            {
                CancelExportButton.Visibility = Visibility.Collapsed;
                UpdateSelection();
            }
        }
    }

    private void CancelExport_Click(object sender, RoutedEventArgs e)
    {
        if (_closed || _exportCancellation is null) return;
        _exportCancellation?.Cancel();
        CancelExportButton.IsEnabled = false;
        SetStatus("ZIP 내보내기를 취소하고 있어요…");
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
