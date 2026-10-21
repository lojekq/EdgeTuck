using System.Windows;
using System.Windows.Controls;
using EdgeTuck.Shell;
using Wpf.Ui.Controls;
using TextBlock = Wpf.Ui.Controls.TextBlock;

namespace EdgeTuck.Pages;

/// <summary>"If a window got lost" on the About page (plan 04 §8). The shell's About page has no slot, so the card goes under its last one.</summary>
public static class LostWindowHelp
{
    public static void AddTo(AboutPage page)
    {
        if (page.Content is not ScrollViewer { Content: StackPanel panel } || panel.Children.OfType<FrameworkElement>().Any(c => c.Tag is "lost")) return;
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = Loc.T("Lost.Title"), FontTypography = FontTypography.BodyStrong, Margin = new Thickness(0, 0, 0, 6) });
        foreach (var key in new[] { "Lost.Restart", "Lost.Keys", "Lost.Move" })
            text.Children.Add(new TextBlock { Text = "•  " + Loc.T(key), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) });
        panel.Children.Add(new Card { Content = text, Margin = new Thickness(0, 12, 0, 4), Tag = "lost" });
    }
}
