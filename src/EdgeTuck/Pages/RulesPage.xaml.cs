using System.ComponentModel;
using System.Windows;
using EdgeTuck.Features;
using EdgeTuck.Shell;

namespace EdgeTuck.Pages;

/// <summary>"Always tuck this program here" rules: made from a tab's menu or the Windows page (plan 04 §7).</summary>
public partial class RulesPage
{
    public RulesPage()
    {
        InitializeComponent();
        TuckManager.Changed += Sync;
        Unloaded += (_, _) => TuckManager.Changed -= Sync;
        Loaded += (_, _) => { TuckManager.Changed -= Sync; TuckManager.Changed += Sync; Sync(); };
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

    static void Save(object? sender, PropertyChangedEventArgs e) => Settings.Save();

    void OnDelete(object sender, RoutedEventArgs e) => TuckManager.RemoveRule(((Rule)((FrameworkElement)sender).DataContext).Exe);
}
