using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EdgeTuck.Features;
using EdgeTuck.Shell;

namespace EdgeTuck.Pages;

/// <summary>v1.1: a palette menu for a window's or a rule's tab color; "As in Settings" clears it.</summary>
public static class ColorMenu
{
    public static void Open(FrameworkElement target, string? current, Action<string?> pick)
    {
        var menu = new ContextMenu { PlacementTarget = target };
        menu.Items.Add(Item(Loc.T("Color.Auto"), null, current is null, pick));
        menu.Items.Add(new Separator());
        foreach (var hex in TabColors.Palette)
            menu.Items.Add(Item(hex, hex, string.Equals(current, hex, StringComparison.OrdinalIgnoreCase), pick));
        menu.IsOpen = true;
    }

    static MenuItem Item(string header, string? hex, bool chosen, Action<string?> pick)
    {
        var item = new MenuItem { Header = header, IsCheckable = true, IsChecked = chosen };
        if (TabColors.Parse(hex) is { } c)
            item.Icon = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(c) };
        item.Click += (_, _) => pick(hex);
        return item;
    }

    /// <summary>A swatch that shows a color (or the accent for null), for buttons and cards.</summary>
    public static Border Swatch(Brush? brush)
    {
        var b = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(4) };
        if (brush is not null) b.Background = brush;
        else b.SetResourceReference(Border.BackgroundProperty, "SystemAccentBrush");
        return b;
    }
}
