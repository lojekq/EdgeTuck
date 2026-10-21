using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;

namespace EdgeTuck.Demo;

/// <summary>
/// --demo: our own fake windows with believable content (plan 04 §10): no real chats, no logos.
/// Borderless, so the frame is the window rectangle; not in the taskbar.
/// </summary>
public sealed class DemoWindow : Window
{
    public static readonly FontFamily Mono = new("Cascadia Mono, Consolas");
    public static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public DemoWindow(string title, double width, double height, UIElement body, Brush background)
    {
        Title = title;
        Width = width; Height = height;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -30000; Top = -30000; // TuckManager places it
        Background = background;
        BorderBrush = new SolidColorBrush(Color.FromRgb(0x3a, 0x3a, 0x3a));
        BorderThickness = new Thickness(1);

        var caption = new DockPanel { Height = 32, Background = new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)) };
        var buttons = new TextBlock { Text = "\uE921   \uE922   \uE8BB", FontFamily = Icons, FontSize = 10, Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(buttons, Dock.Right);
        caption.Children.Add(buttons);
        caption.Children.Add(new TextBlock { Text = title, Foreground = Brushes.Gainsboro, FontSize = 12, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        var root = new DockPanel();
        DockPanel.SetDock(caption, Dock.Top);
        root.Children.Add(caption);
        root.Children.Add(body);
        Content = root;
        // Software rendering: a hardware-rendered WPF window created off screen stayed black in tests (monitors asleep); the demo is simple anyway
        SourceInitialized += (_, _) => System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(this).Handle).CompositionTarget.RenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
    }

    static SolidColorBrush B(uint rgb) => new(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));

    static TextBlock T(string text, double size, Brush brush, FontWeight? weight = null, FontFamily? font = null) =>
        new() { Text = text, FontSize = size, Foreground = brush, FontWeight = weight ?? FontWeights.Normal, FontFamily = font ?? new FontFamily("Segoe UI"), TextWrapping = TextWrapping.Wrap };

    /// <summary>"Phone": a phone screen 9:19, as scrcpy shows it.</summary>
    public static DemoWindow Phone()
    {
        var screen = new Grid { Margin = new Thickness(10, 6, 10, 10), ClipToBounds = true };
        screen.Children.Add(new Rectangle
        {
            RadiusX = 26, RadiusY = 26,
            Fill = new LinearGradientBrush(Color.FromRgb(0x23, 0x3a, 0x6b), Color.FromRgb(0x6b, 0x2f, 0x5c), 65),
        });
        var face = new StackPanel { Margin = new Thickness(22, 18, 22, 0) };
        face.Children.Add(new DockPanel
        {
            Children = { T("9:41", 13, Brushes.White, FontWeights.SemiBold), new TextBlock { Text = "\uE8EA  \uE701  \uEBA7", FontFamily = Icons, FontSize = 11, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Right } },
        });
        face.Children.Add(T("10:24", 64, Brushes.White, FontWeights.Light));
        face.Children.Add(T("Wednesday, 21 October", 15, B(0xdddddd)));
        var note = new Border { Background = new SolidColorBrush(Color.FromArgb(0x40, 255, 255, 255)), CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 40, 0, 0) };
        note.Child = new StackPanel { Children = { T("Alex", 13, Brushes.White, FontWeights.SemiBold), T("The build is green, shipping at 6 PM", 13, B(0xeeeeee)) } };
        face.Children.Add(note);
        var grid = new UniformGrid { Columns = 4, Margin = new Thickness(0, 150, 0, 0) };
        uint[] colors = [0x4f9dde, 0x58b368, 0xe0a33a, 0xd9574b, 0x8a6cd8, 0x3fb8af, 0xe07bb0, 0x9aa5b1];
        foreach (var c in colors)
            grid.Children.Add(new Border { Width = 52, Height = 52, CornerRadius = new CornerRadius(14), Background = B(c), Margin = new Thickness(0, 0, 0, 18) });
        face.Children.Add(grid);
        screen.Children.Add(face);
        return new DemoWindow("Phone", 360, 760, screen, B(0x111111));
    }

    /// <summary>"Music": a player with made-up track and artist.</summary>
    public static DemoWindow Music()
    {
        var body = new Grid { Margin = new Thickness(28) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        body.ColumnDefinitions.Add(new ColumnDefinition());
        var art = new Border { CornerRadius = new CornerRadius(10), Background = new LinearGradientBrush(Color.FromRgb(0xff, 0x8a, 0x3d), Color.FromRgb(0x7a, 0x2c, 0xbf), 45) };
        art.Child = new TextBlock { Text = "\uEC4F", FontFamily = Icons, FontSize = 96, Foreground = new SolidColorBrush(Color.FromArgb(0xa0, 255, 255, 255)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        body.Children.Add(art);
        var info = new StackPanel { Margin = new Thickness(28, 20, 0, 0) };
        Grid.SetColumn(info, 1);
        info.Children.Add(T("NOW PLAYING", 11, B(0x9a9a9a), FontWeights.SemiBold));
        info.Children.Add(T("Night Drive", 34, Brushes.White, FontWeights.SemiBold));
        info.Children.Add(T("Alex Demo · Open Roads", 15, B(0xbbbbbb)));
        var bar = new Grid { Margin = new Thickness(0, 60, 0, 6), Height = 4 };
        bar.Children.Add(new Border { Background = B(0x444444), CornerRadius = new CornerRadius(2) });
        bar.Children.Add(new Border { Background = B(0x1ed760), CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, Width = 150 });
        info.Children.Add(bar);
        info.Children.Add(new DockPanel { Children = { T("1:42", 11, B(0x9a9a9a)), new TextBlock { Text = "3:58", FontSize = 11, Foreground = B(0x9a9a9a), HorizontalAlignment = HorizontalAlignment.Right } } });
        info.Children.Add(new TextBlock { Text = "\uE8B1      \uE892      \uE769      \uE893      \uE8EE", FontFamily = Icons, FontSize = 22, Foreground = Brushes.White, Margin = new Thickness(0, 34, 0, 0) });
        body.Children.Add(info);
        return new DemoWindow("Music", 760, 480, body, B(0x181818));
    }

    /// <summary>"Chat": channels and a made-up conversation.</summary>
    public static DemoWindow Chat()
    {
        var body = new DockPanel();
        var side = new StackPanel { Width = 220, Background = B(0x2b2d31) };
        DockPanel.SetDock(side, Dock.Left);
        side.Children.Add(T("  demo team", 15, Brushes.White, FontWeights.SemiBold));
        foreach (var c in new[] { "# general", "# builds", "# design", "# random" })
            side.Children.Add(new Border { Padding = new Thickness(16, 8, 8, 8), Background = c == "# builds" ? B(0x404249) : Brushes.Transparent, Child = T(c, 14, B(0xb5bac1)) });
        body.Children.Add(side);
        var msgs = new StackPanel { Margin = new Thickness(20, 16, 20, 16) };
        (string Who, uint Color, string Text)[] lines =
        [
            ("Alex", 0x5865f2, "Morning! Nightly build finished in 14 minutes."),
            ("Sam", 0x3ba55c, "Nice. Tests?"),
            ("Alex", 0x5865f2, "All 412 green. Pushing the release notes now."),
            ("Kim", 0xeb459e, "I'll check the screenshots on example.com/preview"),
            ("Sam", 0x3ba55c, "Deploy at 6 PM then?"),
            ("Alex", 0x5865f2, "Yes, 6 PM. I'll ping here when it is live."),
        ];
        foreach (var (who, color, text) in lines)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 18) };
            row.Children.Add(new Ellipse { Width = 36, Height = 36, Fill = B(color), VerticalAlignment = VerticalAlignment.Top });
            row.Children.Add(new StackPanel { Margin = new Thickness(12, 0, 0, 0), Width = 560, Children = { T(who, 14, B(color), FontWeights.SemiBold), T(text, 14, B(0xdbdee1)) } });
            msgs.Children.Add(row);
        }
        body.Children.Add(msgs);
        return new DemoWindow("Chat", 900, 1000, body, B(0x313338));
    }

    /// <summary>"Terminal": a made-up test run in C:\Users\demo.</summary>
    public static DemoWindow Terminal()
    {
        var text = T("""
            PS C:\Users\demo\src\shop> dotnet test
              Determining projects to restore...
              All projects are up-to-date for restore.
              shop -> C:\Users\demo\src\shop\bin\Debug\net10.0\shop.dll
              shop.Tests -> C:\Users\demo\src\shop\tests\bin\Debug\net10.0\shop.Tests.dll

            Passed!  - Failed: 0, Passed: 412, Skipped: 0, Total: 412, Duration: 9 s
            PS C:\Users\demo\src\shop> git push
            To https://example.com/demo/shop.git
               4f2a9c1..8d03e7b  main -> main
            PS C:\Users\demo\src\shop> _
            """, 14, B(0xcccccc), font: Mono);
        text.Margin = new Thickness(14, 10, 14, 10);
        return new DemoWindow("Terminal", 1200, 500, text, B(0x0c0c0c));
    }
}
