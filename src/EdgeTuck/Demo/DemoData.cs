using System.Drawing;
using System.Globalization;
using System.Windows.Media;
using EdgeTuck.Features;
using EdgeTuck.Shell;
using Geometry = EdgeTuck.Features.Geometry;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using FlowDirection = System.Windows.FlowDirection;

namespace EdgeTuck.Demo;

/// <summary>
/// --demo: believable data for screenshots and GIFs. Names like Alex, example.com, C:\Users\demo\…; never real data.
/// Under --demo no real services start, nothing is written, no hotkeys, no update check (00-shared §2.3).
/// Edge Tuck makes its own fake windows and tucks them on the owner's layout (plan 04 §10).
/// </summary>
public static class DemoData
{
    /// <summary>A fake window: size in DIP, where it goes on the owner's monitors, which program the list shows.</summary>
    public sealed record Spec(string Title, string Exe, string Glyph, uint Color, int Width, int Height, string Device, Edge Edge, int Along, Func<DemoWindow> Make);

    public static readonly Spec Phone = new("Phone", "scrcpy.exe", "\uE8EA", 0x4f9dde, 360, 760, @"\\.\DISPLAY2", Edge.Right, 1525, DemoWindow.Phone);
    public static readonly Spec Music = new("Music", "Spotify.exe", "\uE8D6", 0x1ed760, 760, 480, @"\\.\DISPLAY2", Edge.Left, 1600, DemoWindow.Music);
    public static readonly Spec Chat = new("Chat", "Discord.exe", "\uE8BD", 0x8a6cd8, 900, 1000, @"\\.\DISPLAY1", Edge.Left, 200, DemoWindow.Chat);
    public static readonly Spec Terminal = new("Terminal", "WindowsTerminal.exe", "\uE756", 0x9aa5b1, 1200, 500, @"\\.\DISPLAY1", Edge.Top, 1100, DemoWindow.Terminal);
    public static readonly Spec[] All = [Phone, Music, Chat, Terminal];

    public static void Fill()
    {
        Settings.Current.EdgeTuck.Rules =
        [
            new() { Exe = "scrcpy.exe", Edge = Edge.Right, Monitor = @"\\.\DISPLAY2", Along = 0.744 },
            new() { Exe = "Discord.exe", Edge = Edge.Left, Monitor = @"\\.\DISPLAY1", Along = 0.5 },
            new() { Exe = "Spotify.exe", Edge = Edge.Left, Monitor = @"\\.\DISPLAY2", Along = 0.8, Enabled = false },
            new() { Exe = "WindowsTerminal.exe", Edge = Edge.Top, Monitor = @"\\.\DISPLAY1", Along = 0.5, AutoTuck = false },
        ];
        if (Args.Current.Screenshot is not null) { ListOnly(); return; } // the page shows them; no windows on the real edges
        System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (Args.Current.Autoplay) Autoplay.Scene();
            else foreach (var s in All) Tuck(s, s.Make());
        });
    }

    public static DemoInfo Info(Spec s) => new(s.Exe, s.Title, Glyph(s.Glyph, s.Color));

    /// <summary>Where the window goes: its place on the owner's monitors, else the first free segment of the primary (Right, Left, Bottom, Top).</summary>
    public static (Display Monitor, Edge Edge, Rectangle At)? Place(Spec s, IReadOnlyList<Display> monitors)
    {
        if (monitors.Count == 0) return null;
        Rectangle Frame(Display m, Edge e, int? along)
        {
            var size = new Size(Geometry.Px(s.Width, m.Dpi), Geometry.Px(s.Height, m.Dpi));
            var w = m.Work;
            var a = along ?? (Geometry.IsVertical(e) ? w.Top + (w.Height - size.Height) / 2 : w.Left + (w.Width - size.Width) / 2);
            return Geometry.IsVertical(e) ? new(new Point(w.Left + 100, a), size) : new(new Point(a, w.Top + 100), size);
        }
        if (monitors.FirstOrDefault(d => d.Device == s.Device) is { } own && Geometry.Tuck(monitors, own, s.Edge, Frame(own, s.Edge, s.Along)).Plan is not null)
            return (own, s.Edge, Frame(own, s.Edge, s.Along));
        var primary = Rules.Primary(monitors);
        foreach (var e in new[] { Edge.Right, Edge.Left, Edge.Bottom, Edge.Top })
            if (Geometry.Tuck(monitors, primary, e, Frame(primary, e, null)).Plan is not null) return (primary, e, Frame(primary, e, null));
        return null;
    }

    /// <summary>Shows the window and tucks it where Place says.</summary>
    public static void Tuck(Spec s, DemoWindow w)
    {
        if (Place(s, Monitors.Snapshot()) is not var (m, e, at)) return;
        w.Show();
        var h = new System.Windows.Interop.WindowInteropHelper(w).Handle;
        Win.MoveFrame(h, at.Location, async: false);
        TuckManager.TryTuck(h, e, m, at, TuckSource.Demo, Info(s));
    }

    static void ListOnly()
    {
        var monitors = Monitors.Snapshot();
        var fake = -1;
        foreach (var s in All)
        {
            if (Place(s, monitors) is not var (m, e, at) || Geometry.Tuck(monitors, m, e, at).Plan is not { } plan) continue;
            TuckManager.AddDemoEntry(new Tucked { Hwnd = fake--, Pid = 0, Plan = plan, Title = s.Title, DemoExe = s.Exe, Icon = Glyph(s.Glyph, s.Color) });
        }
    }

    /// <summary>A Segoe Fluent Icons glyph as a small image, for windows without an exe icon.</summary>
    public static ImageSource Glyph(string glyph, uint rgb)
    {
        var brush = new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
        var text = new FormattedText(glyph, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(DemoWindow.Icons, System.Windows.FontStyles.Normal, System.Windows.FontWeights.Normal, System.Windows.FontStretches.Normal), 24, brush, 1.0);
        var image = new DrawingImage(new GeometryDrawing(brush, null, text.BuildGeometry(new System.Windows.Point(0, 0))));
        image.Freeze();
        return image;
    }
}
