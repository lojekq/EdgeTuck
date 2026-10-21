using System.Drawing;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using EdgeTuck.Features;
using EdgeTuck.Shell;
using Point = System.Drawing.Point;

namespace EdgeTuck.Demo;

/// <summary>
/// --demo --autoplay: the scene GIF on DISPLAY2, 1440×810 from (3440, 1500) (plan 04 §10), moving the real cursor.
/// Writes the scene area to --rect-file when ready, so tools/gif.ps1 -Scene can start recording:
///   pwsh tools/gif.ps1 -Scene -X 3440 -Y 1500 -W 1440 -H 810 -Out docs/demo.gif
/// Extra flags for still frames (tools/scene.ps1): --hero (Phone half out, cursor at the edge), --closeup (Music tab lit).
/// Only in the owner's slot: Do not disturb, nothing personal on DISPLAY2 (00-shared §9.6).
/// </summary>
public static class Autoplay
{
    public static readonly Rectangle SceneArea = new(3440, 1500, 1440, 810);
    public static readonly Rectangle CloseupArea = new(3440, 1460, 320, 400);

    public static void Scene()
    {
        var monitors = Monitors.Snapshot();
        var d2 = monitors.FirstOrDefault(d => d.Device == @"\\.\DISPLAY2");
        if (d2 is null || !d2.Bounds.Contains(SceneArea))
        {
            foreach (var s in DemoData.All) DemoData.Tuck(s, s.Make()); // not the owner's layout: no scene, just the windows
            WriteRect(SceneArea);
            return;
        }
        var hero = Args.Current.Unknown.Contains("--hero");
        var closeup = Args.Current.Unknown.Contains("--closeup");
        Backdrop(d2, closeup ? 0xf3f3f3u : 0x202020u);

        DemoData.Tuck(DemoData.Chat, DemoData.Chat.Make());
        DemoData.Tuck(DemoData.Terminal, DemoData.Terminal.Make());
        var phone = DemoData.Phone.Make();
        DemoData.Tuck(DemoData.Phone, phone);
        var music = DemoData.Music.Make();
        music.Show();
        var mh = new WindowInteropHelper(music).Handle;
        var ph = new WindowInteropHelper(phone).Handle;
        Win.MoveFrame(mh, new Point(3560, 1700), async: false);

        if (hero || closeup)
        {
            Win.MoveFrame(mh, new Point(3020, 1520), async: false); // at the seam: its tab is in the closeup area
            TuckManager.DemoDragEnd(mh, DemoData.Info(DemoData.Music));
            After(400, () =>
            {
                if (hero && TuckManager.Find(ph) is { } p)
                {
                    TuckManager.TabOf(ph)?.Hide();
                    Win.MoveFrame(ph, new Point(p.Plan.Shown.X + p.Plan.Shown.Width / 2, p.Plan.Shown.Y), async: false);
                    Win.SetCursor(new Point(4879, 1905));
                }
                if (closeup && TuckManager.TabOf(mh) is { } tab) tab.Lit = true;
                After(300, () => WriteRect(closeup ? CloseupArea : SceneArea));
            });
            return;
        }

        Win.SetCursor(new Point(4300, 2200));
        var musicTabY = 1940;
        (double From, double To, Action<double> Do)[] steps =
        [
            (1.0, 1.8, k => Win.SetCursor(Lerp(new(4300, 2200), new(4879, 1905), k))),   // to the right edge: Phone slides out
            (2.6, 3.1, k => Win.SetCursor(Lerp(new(4879, 1905), new(4050, 2150), k))),   // away: it slides back in
            (4.0, 4.5, k => Win.SetCursor(Lerp(new(4050, 2150), new(4160, 1715), k))),   // to Music's title bar
            (4.6, 6.4, k =>                                                              // drag it past the left edge below the seam
            {
                var dx = (int)Math.Round(-540 * k);
                Win.SetCursor(new(4160 + dx, 1715));
                Win.MoveFrame(mh, new(3560 + dx, 1700), async: true);
            }),
            (6.5, 6.5, _ => TuckManager.DemoDragEnd(mh, DemoData.Info(DemoData.Music))), // release: the gesture tucks it
            (7.1, 7.5, k => Win.SetCursor(Lerp(new(3620, 1715), new(3441, musicTabY), k))), // hover the new tab: Music slides out
        ];
        WriteRect(SceneArea);
        foreach (var (at, _, act) in steps.Where(s => s.From == s.To)) After((int)(at * 1000), () => act(1));
        var moves = steps.Where(s => s.To > s.From).ToArray();
        var started = DateTime.UtcNow;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            var t = (DateTime.UtcNow - started).TotalSeconds;
            foreach (var (from, to, step) in moves)
                if (t >= from && t <= to + 0.05) step(Math.Clamp((t - from) / (to - from), 0, 1));
            if (t > moves[^1].To + 1) timer.Stop();
        };
        timer.Start();
    }

    static Point Lerp(Point a, Point b, double k)
    {
        k = 1 - Math.Pow(1 - k, 2);
        return new((int)Math.Round(a.X + (b.X - a.X) * k), (int)Math.Round(a.Y + (b.Y - a.Y) * k));
    }

    /// <summary>A plain window over DISPLAY2 under the demo windows: no wallpaper or other windows in the frame.</summary>
    static void Backdrop(Display d2, uint rgb)
    {
        var w = new BackdropWindow
        {
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb)),
            Left = -30000, Top = -30000, Width = 100, Height = 100,
        };
        w.SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(w).Handle;
            Win.MakeTab(h); // not in Alt+Tab
            Win.PlaceOnTop(h, d2.Bounds);
        };
        w.Show();
    }

    /// <summary>A subclass, so WPF-UI's implicit Window style (it sets AllowsTransparency) does not reach it.</summary>
    sealed class BackdropWindow : Window;

    static void WriteRect(Rectangle r)
    {
        if (Args.Current.RectFile is { } path) AtomicFile.WriteAllText(path, $"{r.X} {r.Y} {r.Width} {r.Height}");
    }

    static void After(int ms, Action a)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(1, ms)) };
        timer.Tick += (_, _) => { timer.Stop(); a(); };
        timer.Start();
    }
}
