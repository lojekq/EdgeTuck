using System.Windows;
using System.Windows.Controls;
using EdgeTuck.Shell;

namespace EdgeTuck;

public partial class MainWindow
{
    public MainWindow()
    {
        InitializeComponent();
        ElevatedBar.IsOpen = AppInfo.IsElevated;
        // WPF-UI wraps every Page in its own DynamicScrollViewer unless the page says ScrollViewer.CanContentScroll="False".
        // Our pages scroll themselves (a ScrollViewer, or a list in a * row): inside that wrapper they get infinite height,
        // never scroll, and their ScrollViewer still marks the mouse wheel handled, so nothing scrolls at all.
        Nav.Navigating += (_, e) => { if (e.Page is DependencyObject page) ScrollViewer.SetCanContentScroll(page, false); };
        Loaded += (_, _) => { if (Nav.SelectedItem is null) Navigate("windows"); };
        Nav.Navigated += (_, e) =>
        {
            if (e.Page is AboutPage about) Pages.LostWindowHelp.AddTo(about);
            // settings.png shows Edge Tuck's own section, which sits below the shell's settings
            if (e.Page is SettingsPage page && Args.Current.Screenshot is not null)
                page.Dispatcher.BeginInvoke(() =>
                {
                    // NavigationView may scroll the page itself: scroll whichever viewer holds it, after layout
                    for (System.Windows.DependencyObject? d = page; d is not null; d = System.Windows.Media.VisualTreeHelper.GetParent(d))
                        (d as System.Windows.Controls.ScrollViewer)?.ScrollToEnd();
                    (page.Content as System.Windows.Controls.ScrollViewer)?.ScrollToEnd();
                }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        };
    }

    /// <summary>By TargetPageTag: "windows", "rules", "settings", "about".</summary>
    public void Navigate(string page) => Nav.Navigate(page);

    public void ShowUpdate(string version, string url)
    {
        UpdateBar.Message = string.Format(Loc.T("Update.Message"), version);
        UpdateLink.NavigateUri = url;
        UpdateBar.IsOpen = true;
    }
}
