using System.Windows;
using System.Windows.Controls;
using EdgeTuck.Features;
using EdgeTuck.Shell;
using Wpf.Ui.Controls;
using TextBlock = Wpf.Ui.Controls.TextBlock;

namespace EdgeTuck.Pages;

/// <summary>Edge Tuck's own part of the settings page (SettingsPage.AppSection): hotkeys, delays, tab, gesture, fullscreen.</summary>
public static class SettingsSection
{
    static EdgeTuckSettings S => Settings.Current.EdgeTuck;

    public static FrameworkElement Create()
    {
        var panel = new StackPanel();
        var status = TuckManager.RegisterHotkeys();

        var grid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var row = 0;
        void Add(string label, string key, Func<string?> get, Action<string?> set)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 16, 4) };
            var box = new HotkeyBox { Hotkey = get(), Status = status.GetValueOrDefault(key), Margin = new Thickness(0, 4, 0, 4), HorizontalAlignment = HorizontalAlignment.Left };
            box.Changed += (_, _) =>
            {
                set(box.Hotkey);
                Settings.Save();
                box.Status = TuckManager.RegisterHotkeys().GetValueOrDefault(key);
            };
            Grid.SetRow(text, row); Grid.SetRow(box, row); Grid.SetColumn(box, 1);
            grid.Children.Add(text); grid.Children.Add(box);
            row++;
        }
        foreach (var e in Geometry.Edges) Add(Loc.T("Settings.Hotkey." + e), e.ToString(), () => S.Hotkey(e), v => S.SetHotkey(e, v));
        Add(Loc.T("Settings.Hotkey.RestoreAll"), "RestoreAll", () => S.HotkeyRestoreAll, v => S.HotkeyRestoreAll = v);
        panel.Children.Add(new CardExpander
        {
            Header = Loc.T("Settings.Hotkeys"), Icon = new SymbolIcon(SymbolRegular.Keyboard24), IsExpanded = true,
            Content = grid, Margin = new Thickness(0, 0, 0, 4),
        });

        panel.Children.Add(Card("Settings.SlideOut", SymbolRegular.ArrowRight24, Number(S.SlideOutDelayMs, 0, 2000, v => S.SlideOutDelayMs = v, Loc.T("Unit.Ms"))));
        panel.Children.Add(Card("Settings.SlideIn", SymbolRegular.ArrowLeft24, Number(S.SlideInDelayMs, 0, 5000, v => S.SlideInDelayMs = v, Loc.T("Unit.Ms"))));
        panel.Children.Add(Card("Settings.Animation", SymbolRegular.Flash24, Number(S.AnimationMs, 0, 1000, v => S.AnimationMs = v, Loc.T("Unit.Ms")), "Settings.Animation.Note"));
        var tab = new StackPanel { Orientation = Orientation.Horizontal };
        tab.Children.Add(Number(S.TabThickness, 2, 32, v => S.TabThickness = v, "×"));
        tab.Children.Add(new FrameworkElement { Width = 8 });
        tab.Children.Add(Number(S.TabLength, 16, 400, v => S.TabLength = v, "DIP"));
        panel.Children.Add(Card("Settings.Tab", SymbolRegular.PanelLeft24, tab));

        var gesture = new StackPanel { Orientation = Orientation.Horizontal };
        gesture.Children.Add(Number(S.GesturePercent, 10, 90, v => S.GesturePercent = v, "%"));
        gesture.Children.Add(new FrameworkElement { Width = 16 });
        gesture.Children.Add(Toggle(S.Gesture, v => S.Gesture = v));
        panel.Children.Add(Card("Settings.Gesture", SymbolRegular.HandDraw24, gesture, "Settings.Gesture.Note"));
        panel.Children.Add(Card("Settings.Fullscreen", SymbolRegular.FullScreenMaximize24, Toggle(S.HideTabsInFullscreen, v => S.HideTabsInFullscreen = v), "Settings.Fullscreen.Note"));
        return panel;
    }

    static CardControl Card(string key, SymbolRegular icon, UIElement content, string? noteKey = null)
    {
        object header = Loc.T(key);
        if (noteKey is not null)
            header = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = Loc.T(key) },
                    new TextBlock { Text = Loc.T(noteKey), FontTypography = FontTypography.Caption, Appearance = TextColor.Secondary, TextWrapping = TextWrapping.Wrap },
                },
            };
        return new CardControl { Header = header, Icon = new SymbolIcon(icon), Content = content, Margin = new Thickness(0, 0, 0, 4) };
    }

    static FrameworkElement Number(int value, int min, int max, Action<int> set, string unit)
    {
        var box = new NumberBox
        {
            Value = value, Minimum = min, Maximum = max, SmallChange = 10, LargeChange = 100, Width = 140,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline, ClearButtonEnabled = false,
        };
        box.ValueChanged += (_, _) =>
        {
            if (box.Value is not { } v) return;
            set((int)Math.Clamp(v, min, max));
            Settings.Save();
        };
        var p = new StackPanel { Orientation = Orientation.Horizontal };
        p.Children.Add(box);
        p.Children.Add(new TextBlock { Text = unit, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), Appearance = TextColor.Secondary });
        return p;
    }

    static ToggleSwitch Toggle(bool value, Action<bool> set)
    {
        var t = new ToggleSwitch { IsChecked = value };
        t.Click += (_, _) => { set(t.IsChecked == true); Settings.Save(); };
        return t;
    }
}
