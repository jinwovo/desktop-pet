using System.Windows;
using System.Windows.Controls;
using DesktopPet.Core;
using Button = System.Windows.Controls.Button;
using ListBox = System.Windows.Controls.ListBox;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace DesktopPet.App;

/// <summary>Explicit selection when a downloaded ZIP contains several character packs.</summary>
public sealed class CharacterArchiveWindow : Window
{
    private readonly ListBox _characters;
    public ArchiveCharacter? SelectedCharacter { get; private set; }

    public CharacterArchiveWindow(IReadOnlyList<ArchiveCharacter> characters)
    {
        Title = UiText.Get("ZIP 캐릭터 선택");
        Width = 410;
        Height = 360;
        MinWidth = 330;
        MinHeight = 280;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new DockPanel { Margin = new Thickness(18) };
        var hint = new TextBlock { Text = UiText.Get("추가할 캐릭터를 선택하세요."), Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(hint, Dock.Top);
        panel.Children.Add(hint);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0) };
        var cancel = new Button { Content = UiText.Get("취소"), IsCancel = true, Padding = new Thickness(14, 6, 14, 6) };
        var install = new Button { Name = "InstallArchiveCharacter", Content = UiText.Get("선택한 캐릭터 추가"), IsDefault = true,
            Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(8, 0, 0, 0) };
        buttons.Children.Add(cancel);
        buttons.Children.Add(install);
        DockPanel.SetDock(buttons, Dock.Bottom);
        panel.Children.Add(buttons);
        _characters = new ListBox { Name = "ArchiveCharacters", ItemsSource = characters, DisplayMemberPath = nameof(ArchiveCharacter.Label),
            SelectedIndex = 0, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        install.Click += (_, _) =>
        {
            if (_characters.SelectedItem is not ArchiveCharacter selected) return;
            SelectedCharacter = selected;
            DialogResult = true;
        };
        panel.Children.Add(_characters);
        Content = panel;
        UiText.Attach(this);
    }
}
