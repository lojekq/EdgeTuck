using System.Windows;
using System.Windows.Controls;
using EdgeTuck.Shell;

namespace EdgeTuck.Features;

/// <summary>
/// Edge Tuck's part of the tray menu (plan 04 §7): the tucked windows (rebuilt each time the menu opens),
/// "Return all windows", "Pause", "Settings". The shell adds "Open" above and "Exit" below.
/// </summary>
public static class TrayMenu
{
    static readonly Separator Anchor = new() { Visibility = Visibility.Collapsed };
    static readonly List<Control> Dynamic = [];
    static MenuItem? pause, restoreAll;

    public static IEnumerable<Control> Items()
    {
        restoreAll = Tray.Item(Loc.T("Tray.RestoreAll"), () => TuckManager.RestoreAll());
        pause = new MenuItem { Header = Loc.T("Tray.Pause"), IsCheckable = true };
        pause.Click += (_, _) => TuckManager.Paused = pause.IsChecked;
        return [Anchor, restoreAll, pause, Tray.Item(Loc.T("Page.Settings"), () => App.ShowMain("settings"))];
    }

    /// <summary>After Tray.Create: the anchor now sits in the menu, so the menu is reachable.</summary>
    public static void Attach()
    {
        if (Anchor.Parent is ContextMenu menu) menu.Opened += (_, _) => Rebuild(menu);
    }

    /// <summary>--demo screenshot of the menu: the same items, rebuilt now.</summary>
    public static void Rebuild(ContextMenu menu)
    {
        foreach (var c in Dynamic) menu.Items.Remove(c);
        Dynamic.Clear();
        var at = menu.Items.IndexOf(Anchor);
        foreach (var t in TuckManager.Windows)
        {
            var item = Tray.Item(string.IsNullOrWhiteSpace(t.Title) ? t.Program : Trim(t.Title), () => TuckManager.Show(t));
            if (t.Icon is not null) item.Icon = new System.Windows.Controls.Image { Source = t.Icon, Width = 16, Height = 16 };
            menu.Items.Insert(at++, item);
            Dynamic.Add(item);
        }
        if (Dynamic.Count > 0)
        {
            var sep = new Separator();
            menu.Items.Insert(at, sep);
            Dynamic.Add(sep);
        }
        if (pause is not null) pause.IsChecked = TuckManager.Paused;
        if (restoreAll is not null) restoreAll.InputGestureText = Settings.Current.EdgeTuck.HotkeyRestoreAll ?? "";
    }

    static string Trim(string s) => s.Length > 48 ? s[..47] + "…" : s;
}
