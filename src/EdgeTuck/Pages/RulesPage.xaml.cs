using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using EdgeTuck.Features;
using EdgeTuck.Shell;

namespace EdgeTuck.Pages;

/// <summary>"Always tuck this program here" rules: made from a tab's menu, the Windows page or a template (plan 04 §7, §3).</summary>
public partial class RulesPage
{
    public RulesPage()
    {
        InitializeComponent();
        TuckManager.Changed += Sync;
        Unloaded += (_, _) => TuckManager.Changed -= Sync;
        Loaded += (_, _) => { TuckManager.Changed -= Sync; TuckManager.Changed += Sync; Sync(); };
        TemplateButton.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = TemplateButton };
            foreach (var t in Rules.Templates)
            {
                var item = Tray.Item($"{t.Exe} · {Loc.T("Edge." + t.Edge)}", () => TuckManager.AddTemplate(t));
                item.IsEnabled = !Settings.Current.EdgeTuck.Rules.Any(r => r.Exe.Equals(t.Exe, StringComparison.OrdinalIgnoreCase));
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        };
        Sync();
    }

    void Sync()
    {
        var rules = Settings.Current.EdgeTuck.Rules;
        foreach (var r in rules) { r.PropertyChanged -= Save; r.PropertyChanged += Save; }
        List.ItemsSource = null;
        List.ItemsSource = rules;
        EmptyCard.Visibility = rules.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    static void Save(object? sender, PropertyChangedEventArgs e)
    {
        Settings.Save();
        if (e.PropertyName == nameof(Rule.Hotkey)) TuckManager.RegisterRuleHotkeys();
    }

    /// <summary>v1.1: this program's tab color.</summary>
    void OnColor(object sender, RoutedEventArgs e)
    {
        var r = (Rule)((FrameworkElement)sender).DataContext;
        ColorMenu.Open((FrameworkElement)sender, r.TabColor, hex => { r.TabColor = hex; TuckManager.RefreshTabs(); });
    }

    void OnDelete(object sender, RoutedEventArgs e) => TuckManager.RemoveRule(((Rule)((FrameworkElement)sender).DataContext).Exe);
}
