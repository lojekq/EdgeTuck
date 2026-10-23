using System.Windows;
using EdgeTuck.Features;

namespace EdgeTuck.Pages;

/// <summary>Tucked windows: show, return, remember as a rule (plan 04 §7).</summary>
public partial class WindowsPage
{
    public WindowsPage()
    {
        InitializeComponent();
        List.ItemsSource = TuckManager.Windows;
        ResumeButton.Click += (_, _) => TuckManager.Paused = false;
        void Sync()
        {
            EmptyCard.Visibility = TuckManager.Windows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            PausedBar.IsOpen = TuckManager.Paused;
        }
        TuckManager.Changed += Sync;
        Unloaded += (_, _) => TuckManager.Changed -= Sync;
        Loaded += (_, _) => { TuckManager.Changed -= Sync; TuckManager.Changed += Sync; Sync(); };
        Sync();
    }

    static Tucked Of(object sender) => (Tucked)((FrameworkElement)sender).DataContext;

    void OnShow(object sender, RoutedEventArgs e) => TuckManager.Show(Of(sender));
    void OnReturn(object sender, RoutedEventArgs e) => TuckManager.Untuck(Of(sender), move: true);
    void OnRemember(object sender, RoutedEventArgs e) => TuckManager.AddRule(Of(sender));

    /// <summary>v1.1.1: this window's own tab size.</summary>
    void OnSize(object sender, RoutedEventArgs e)
    {
        var t = Of(sender);
        SettingsSection.OpenTabSize((FrameworkElement)sender, t.TabThickness, t.TabLength, (a, b) => { t.TabThickness = a; t.TabLength = b; TuckManager.RefreshTabs(); });
    }

    /// <summary>v1.1: this window's own tab color.</summary>
    void OnColor(object sender, RoutedEventArgs e)
    {
        var t = Of(sender);
        ColorMenu.Open((FrameworkElement)sender, t.TabColor, hex => { t.TabColor = hex; TuckManager.RefreshTabs(); });
    }
}
