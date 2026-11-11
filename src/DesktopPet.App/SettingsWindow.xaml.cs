using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using DesktopPet.Core;

namespace DesktopPet.App;

/// <summary>
/// Settings UI. Changes apply to the live pet immediately rather than on an OK button — with a
/// desktop mascot you want to see the effect of a size or speed change while dragging the slider.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly MainWindow _pet;
    private bool _loading;

    public SettingsWindow(MainWindow pet)
    {
        _pet = pet;
        InitializeComponent();
        LoadFromPet();
    }

    private void LoadFromPet()
    {
        _loading = true;

        CharacterCombo.ItemsSource = _pet.AvailableCharacterNames;
        CharacterCombo.SelectedItem = _pet.CurrentCharacterName;

        ScaleSlider.Value = _pet.CurrentScaleMultiplier;
        SpeedSlider.Value = _pet.CurrentWalkSpeedMultiplier;
        VisibleCheck.IsChecked = _pet.IsPetVisible;
        HopCheck.IsChecked = _pet.HopEnabled;
        SleepCheck.IsChecked = _pet.SleepEnabled;
        WindowFallCheck.IsChecked = _pet.WindowFallEnabled;

        _loading = false;

        UpdateValueLabels();
        UpdatePreview();
    }

    private void UpdateValueLabels()
    {
        ScaleValue.Text = $"{ScaleSlider.Value * 100:F0}%";
        SpeedValue.Text = $"{SpeedSlider.Value:0.##}x";
    }

    private void UpdatePreview()
    {
        if (CharacterCombo.SelectedItem is not string name)
        {
            return;
        }

        var idle = Path.Combine(MainWindow.CharactersRoot, name, "idle.png");
        if (File.Exists(idle))
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad; // don't keep the file locked
            bitmap.UriSource = new Uri(idle);
            bitmap.EndInit();
            PreviewImage.Source = bitmap;
        }
        else
        {
            PreviewImage.Source = null;
        }

        var frameCount = Directory.Exists(Path.Combine(MainWindow.CharactersRoot, name))
            ? Directory.GetFiles(Path.Combine(MainWindow.CharactersRoot, name), "*.png").Length
            : 0;
        CharacterHint.Text = $"프레임 {frameCount}장";
    }

    private void CharacterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || CharacterCombo.SelectedItem is not string name)
        {
            return;
        }

        _pet.SwitchCharacter(name);
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

    private void FolderButton_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(MainWindow.CharactersRoot) { UseShellExecute = true });

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
