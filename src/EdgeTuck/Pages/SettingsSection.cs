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
        var tab = TabSizeBoxes(S.TabThickness, S.TabLength, inherit: false, (t, l) =>
        {
            S.TabThickness = t!.Value; S.TabLength = l!.Value;
            Settings.Save();
            TuckManager.RefreshTabs();
        });
        panel.Children.Add(Card("Settings.Tab", SymbolRegular.PanelLeft24, tab, "Settings.Tab.Note"));

        var gesture = new StackPanel { Orientation = Orientation.Horizontal };
        gesture.Children.Add(Number(S.GesturePercent, 10, 90, v => S.GesturePercent = v, "%"));
        gesture.Children.Add(new FrameworkElement { Width = 16 });
        gesture.Children.Add(Toggle(S.Gesture, v => S.Gesture = v));
        panel.Children.Add(Card("Settings.Gesture", SymbolRegular.HandDraw24, gesture, "Settings.Gesture.Note"));
        panel.Children.Add(Card("Settings.Fullscreen", SymbolRegular.FullScreenMaximize24, Toggle(S.HideTabsInFullscreen, v => S.HideTabsInFullscreen = v), "Settings.Fullscreen.Note"));
        panel.Children.Add(Card("Settings.SlideOutOnHover", SymbolRegular.CursorHover24, Toggle(S.SlideOutOnHover, v => S.SlideOutOnHover = v), "Settings.SlideOutOnHover.Note"));
        var shared = new StackPanel { Orientation = Orientation.Horizontal };
        shared.Children.Add(new TextBlock { Text = Loc.T("Settings.HoverOnSharedEdges"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), Appearance = TextColor.Secondary });
        shared.Children.Add(Toggle(S.HoverOnSharedEdges, v => S.HoverOnSharedEdges = v));
        shared.Children.Add(new FrameworkElement { Width = 24 });
        shared.Children.Add(Toggle(S.SharedEdges, v => S.SharedEdges = v));
        panel.Children.Add(Card("Settings.SharedEdges", SymbolRegular.SplitVertical24, shared, "Settings.SharedEdges.Note"));
        panel.Children.Add(Card("Settings.TabColor", SymbolRegular.Color24, ColorChooser(), "Settings.TabColor.Note"));
        panel.Children.Add(Card("Settings.TabIcon", SymbolRegular.Apps24, Toggle(S.TabIcon, v => { S.TabIcon = v; TuckManager.RefreshTabs(); })));
        return panel;
    }

    /// <summary>v1.1: By the icon / Windows accent / My color, with a palette and HEX for my color.</summary>
    static FrameworkElement ColorChooser()
    {
        var mode = new ComboBox { MinWidth = 200 };
        foreach (var (tag, key) in new[] { ("icon", "Settings.TabColor.Icon"), ("accent", "Settings.TabColor.Accent"), ("custom", "Settings.TabColor.Custom") })
            mode.Items.Add(new ComboBoxItem { Tag = tag, Content = Loc.T(key) });
        mode.SelectedItem = mode.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == S.TabColorMode) ?? mode.Items[0];
        var hex = new Wpf.Ui.Controls.TextBox { Text = S.TabColor, Width = 110, MaxLength = 7 };
        var swatches = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var custom = new StackPanel { Children = { hex, swatches } };
        void Apply()
        {
            custom.Visibility = S.TabColorMode == "custom" ? Visibility.Visible : Visibility.Collapsed;
            Settings.Save();
            TuckManager.RefreshTabs();
        }
        foreach (var c in TabColors.Palette)
        {
            var b = new Wpf.Ui.Controls.Button { Padding = new Thickness(4), Margin = new Thickness(0, 0, 4, 0), Content = ColorMenu.Swatch(new System.Windows.Media.SolidColorBrush(TabColors.Parse(c)!.Value)), ToolTip = c };
            b.Click += (_, _) => { S.TabColor = c; hex.Text = c; Apply(); };
            swatches.Children.Add(b);
        }
        hex.LostFocus += (_, _) =>
        {
            if (TabColors.Parse(hex.Text) is { } parsed) { S.TabColor = TabColors.Hex(parsed); Apply(); }
            hex.Text = S.TabColor;
        };
        mode.SelectionChanged += (_, _) => { S.TabColorMode = (string)((ComboBoxItem)mode.SelectedItem).Tag; Apply(); };
        custom.Visibility = S.TabColorMode == "custom" ? Visibility.Visible : Visibility.Collapsed;
        return new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, Children = { mode, custom } };
    }

    /// <summary>
    /// v1.1.1: thickness × length in px, applied while typing and with the arrows. inherit: for a window or a rule, where an empty box
    /// means "as in Settings" (the Settings value is its placeholder); Settings' own boxes ignore an empty box.
    /// </summary>
    public static FrameworkElement TabSizeBoxes(int? thickness, int? length, bool inherit, Action<int?, int?> set)
    {
        int? t = thickness, l = length;
        var a = PxBox(t, Geometry.MinTabThickness, Geometry.MaxTabThickness, 1, inherit ? S.TabThickness : null, v => { t = v; set(t, l); });
        var b = PxBox(l, Geometry.MinTabLength, Geometry.MaxTabLength, 8, inherit ? S.TabLength : null, v => { l = v; set(t, l); });
        TextBlock Unit(string s) => new() { Text = s, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0), Appearance = TextColor.Secondary };
        return new StackPanel { Orientation = Orientation.Horizontal, Children = { a, Unit("×"), b, Unit("px") } };
    }

    static NumberBox PxBox(int? value, int min, int max, int step, int? placeholder, Action<int?> set)
    {
        var box = new NumberBox
        {
            Value = value, Minimum = min, Maximum = max, SmallChange = step, LargeChange = step * 8, Width = 130,
            PlaceholderText = placeholder?.ToString() ?? "", SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline, ClearButtonEnabled = false,
        };
        box.TextChanged += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(box.Text)) { if (placeholder is not null && value is not null) set(value = null); }
            else if (int.TryParse(box.Text, out var v) && v >= min && v <= max && v != value) set(value = v);
        };
        return box;
    }

    /// <summary>v1.1.1: the size boxes in a popup under a button (Windows page).</summary>
    public static void OpenTabSize(FrameworkElement target, int? thickness, int? length, Action<int?, int?> set)
    {
        var border = new Border
        {
            Padding = new Thickness(12), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
            Child = new StackPanel
            {
                Children =
                {
                    TabSizeBoxes(thickness, length, inherit: true, set),
                    new TextBlock { Text = Loc.T("Tab.Size.Hint"), FontTypography = FontTypography.Caption, Appearance = TextColor.Secondary, Margin = new Thickness(0, 8, 0, 0) },
                },
            },
        };
        border.SetResourceReference(Border.BackgroundProperty, "ApplicationBackgroundBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
        _ = new System.Windows.Controls.Primitives.Popup
        {
            PlacementTarget = target, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom, StaysOpen = false,
            AllowsTransparency = true, Child = border, IsOpen = true,
        };
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
