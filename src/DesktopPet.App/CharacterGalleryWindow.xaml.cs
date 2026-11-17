using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace DesktopPet.App;

public enum CharacterGalleryCategory { All, Original, FanArt, Custom }

public sealed record CharacterGalleryItem(string Name, string EnglishName, CharacterGalleryCategory Category,
    BitmapImage? Thumbnail, bool IsAvailable, string Detail, string SearchText)
{
    public string CategoryLabel => Category switch
    {
        CharacterGalleryCategory.Original => "오리지널", CharacterGalleryCategory.FanArt => "팬아트 팩", _ => "추가한 캐릭터",
    };
}

public partial class CharacterGalleryWindow : Window
{
    private readonly MainWindow _pet;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _loadCancellation;
    private long _loadRevision;
    private IReadOnlyList<CharacterGalleryItem> _all = Array.Empty<CharacterGalleryItem>();
    private bool _closed, _started, _filtering, _previewBusy;
    private MotionPreviewWindow? _motionPreview;
    private sealed record CategoryChoice(string Label, CharacterGalleryCategory Category);
    private sealed record Alias(string EnglishName, string[] Names);
    private static readonly Dictionary<string, Alias> BuiltinAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["치이카와"] = new("Chiikawa", ["ちいかわ"]), ["세균맨"] = new("Baikinman", ["바이킨맨", "ばいきんまん"]),
        ["헬로키티"] = new("Hello Kitty", ["hellokitty", "ハローキティ"]), ["폼폼푸린"] = new("Pompompurin", ["pompom", "ポムポムプリン"]),
        ["시나모롤"] = new("Cinnamoroll", ["시나몬롤", "cinnamonroll", "シナモロール"]), ["쿠로미"] = new("Kuromi", ["クロミ"]),
    };

    public CharacterGalleryWindow(MainWindow pet)
    {
        _pet = pet;
        _pet.RenderOptionsChanged += ApplyRenderOptions;
        InitializeComponent();
        MaxHeight = Math.Max(MinHeight, SystemParameters.WorkArea.Height - 40);
        CategoryCombo.ItemsSource = new[]
        {
            new CategoryChoice("전체 친구", CharacterGalleryCategory.All), new CategoryChoice("오리지널", CharacterGalleryCategory.Original),
            new CategoryChoice("팬아트 팩", CharacterGalleryCategory.FanArt), new CategoryChoice("추가한 캐릭터", CharacterGalleryCategory.Custom),
        };
        CategoryCombo.SelectedValue = CharacterGalleryCategory.All;
        Loaded += (_, _) => { if (!_started) { _started = true; RefreshCharacters(); } };
        Closed += (_, _) =>
        {
            _closed = true;
            _pet.RenderOptionsChanged -= ApplyRenderOptions;
            _lifetime.Cancel();
            _loadCancellation?.Dispose();
            _lifetime.Dispose();
            _motionPreview?.Close();
        };
    }

    public Task LoadingTask { get; private set; } = Task.CompletedTask;
    public Task PreviewOperation { get; private set; } = Task.CompletedTask;
    public IReadOnlyList<CharacterGalleryItem> AllCharacters => _all;
    public event Action<string>? CharacterApplied;
    private void ApplyRenderOptions(int fps, bool reducedMotion) => _motionPreview?.SetRenderOptions(fps, reducedMotion);

    /// <summary>Reload the library after import without applying a character or changing filters.</summary>
    public void RefreshCharacters()
    {
        Dispatcher.VerifyAccess();
        if (_closed || !_started) return;
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        LoadingTask = LoadCharactersAsync(++_loadRevision, _loadCancellation.Token);
    }

    private async Task LoadCharactersAsync(long revision, CancellationToken cancellation)
    {
        CountText.Text = "친구들을 불러오고 있어요…";
        try
        {
            var names = _pet.AvailableCharacterNames.ToArray();
            var aliases = ReadAliases();
            var items = await Task.Run(() =>
            {
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
            FilterCharacters();
            CharacterCards.SelectedItem = CharacterCards.Items.OfType<CharacterGalleryItem>().FirstOrDefault(item => item.Name == selectedName);
            StatusText.Text = "현재 친구: " + _pet.CurrentCharacterName;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (IsCharacterError(ex))
        {
            if (!_closed && revision == _loadRevision) { CountText.Text = "목록을 불러오지 못했어요."; StatusText.Text = "캐릭터 폴더를 확인하고 다시 열어주세요."; }
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

    private void Filter_Changed(object sender, TextChangedEventArgs e) { if (IsInitialized) FilterCharacters(); }
    private void Category_Changed(object sender, SelectionChangedEventArgs e) { if (IsInitialized) FilterCharacters(); }

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
        EmptyText.Visibility = visible.Length == 0 && _started ? Visibility.Visible : Visibility.Collapsed;
        CountText.Text = $"{_all.Count}종 중 {visible.Length}종";
        UpdateSelection();
    }

    private void CharacterCards_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (!_filtering) UpdateSelection(); }
    private void UpdateSelection()
    {
        if (PreviewImage is null) return;
        var item = CharacterCards.SelectedItem as CharacterGalleryItem;
        PreviewImage.Source = item?.Thumbnail;
        SelectedNameText.Text = item?.Name ?? "";
        SelectedDetailText.Text = item?.Detail ?? "친구를 선택해보세요.";
        ApplyButton.IsEnabled = item is { IsAvailable: true };
        PreviewButton.IsEnabled = item is { IsAvailable: true } && !_previewBusy;
    }

    private async void Preview_Click(object sender, RoutedEventArgs e)
    {
        PreviewOperation = PreviewSelectedAsync();
        await PreviewOperation;
    }

    private async Task PreviewSelectedAsync()
    {
        if (_closed || _previewBusy || CharacterCards.SelectedItem is not CharacterGalleryItem { IsAvailable: true } selected) return;
        _previewBusy = true;
        UpdateSelection();
        try
        {
            var folder = _pet.ResolveCharacterFolder(selected.Name);
            var presentation = await Task.Run(() => CharacterPresentation.Load(folder), _lifetime.Token);
            if (_closed || !ReferenceEquals(CharacterCards.SelectedItem, selected)) return;
            _motionPreview?.Close();
            _motionPreview = new MotionPreviewWindow(presentation, _pet.CurrentWalkSpeedMultiplier, _pet.MaximumFps, _pet.ReducedMotion) { Owner = this };
            _motionPreview.Show();
            StatusText.Text = "미리보기는 현재 친구를 바꾸지 않아요.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (IsCharacterError(ex)) { if (!_closed) StatusText.Text = "움직임을 읽지 못했어요. 캐릭터 파일을 확인해주세요."; }
        finally { _previewBusy = false; if (!_closed) UpdateSelection(); }
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_closed || CharacterCards.SelectedItem is not CharacterGalleryItem { IsAvailable: true } item) return;
        try
        {
            _pet.SwitchCharacter(item.Name);
            CharacterApplied?.Invoke(item.Name);
            StatusText.Text = item.Name + " 적용했어요.";
        }
        catch (Exception ex) when (IsCharacterError(ex)) { StatusText.Text = "적용하지 못했어요. 현재 친구는 그대로예요."; }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
