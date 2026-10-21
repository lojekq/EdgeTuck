using EdgeTuck.Shell;

namespace EdgeTuck;

public partial class MainWindow
{
    public MainWindow()
    {
        InitializeComponent();
        ElevatedBar.IsOpen = AppInfo.IsElevated;
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
