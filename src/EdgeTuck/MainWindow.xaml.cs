using EdgeTuck.Shell;

namespace EdgeTuck;

public partial class MainWindow
{
    public MainWindow()
    {
        InitializeComponent();
        ElevatedBar.IsOpen = AppInfo.IsElevated;
        Loaded += (_, _) => { if (Nav.SelectedItem is null) Navigate("windows"); };
        Nav.Navigated += (_, e) => { if (e.Page is AboutPage about) Pages.LostWindowHelp.AddTo(about); };
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
