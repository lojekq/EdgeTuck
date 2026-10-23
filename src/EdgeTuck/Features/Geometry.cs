using System.Drawing;
using System.Text.Json.Serialization;
using EdgeTuck.Shell;

namespace EdgeTuck.Features;

[JsonConverter(typeof(JsonStringEnumConverter<Edge>))]
public enum Edge { Left, Top, Right, Bottom }

/// <summary>A piece of an edge on its own axis (y for Left/Right, x for Top/Bottom), [Start, End).</summary>
public readonly record struct Segment(int Start, int End)
{
    public int Length => End - Start;
}

public enum TuckFail { None, SharedEdge, TooLong, TooBig }

/// <summary>
/// Frame rectangles (DWMWA_EXTENDED_FRAME_BOUNDS) of a tucked window: on screen and behind the edge.
/// Floating (v1.0): Edge and Segment are a shared part of an edge where only the tab sits; the window waits behind ParkEdge, an outer edge of the same monitor.
/// </summary>
public sealed record TuckPlan(Display Monitor, Edge Edge, Rectangle Shown, Rectangle Parked, Segment Segment, bool Floating = false, Edge ParkEdge = Edge.Left);

/// <summary>Plan, or why not; FreePx is the longest free segment for TooLong.</summary>
public readonly record struct TuckResult(TuckPlan? Plan, TuckFail Fail, int FreePx = 0);

/// <summary>
/// Pure geometry, physical pixels, no Win32 (plan 04 §5). Monitors come as a list, so every layout is a test.
/// </summary>
public static class Geometry
{
    // ponytail: parkGap and m are physical px, not DIP; if a shadow shows through at 200 % and up, derive them from the monitor DPI
    /// <summary>Gap between a parked window and its own monitor: the DWM shadow must not show.</summary>
    public const int ParkGap = 64;
    /// <summary>Minimal distance from a parked window to any other monitor, so the nearest monitor (and the DPI) stays its own.</summary>
    public const int Margin = 80;

    public static readonly Edge[] Edges = [Edge.Left, Edge.Top, Edge.Right, Edge.Bottom];

    public static bool IsVertical(Edge e) => e is Edge.Left or Edge.Right;

    /// <summary>The extent of r along the edge's own axis.</summary>
    public static Segment Along(Rectangle r, Edge e) => IsVertical(e) ? new(r.Top, r.Bottom) : new(r.Left, r.Right);

    /// <summary>Size of a window across the edge: how deep it goes behind it.</summary>
    public static int Across(Size s, Edge e) => IsVertical(e) ? s.Width : s.Height;

    /// <summary>Behind edge e of m.Bounds, depth deep, along the whole edge.</summary>
    public static Rectangle ParkingStrip(Display m, Edge e, int depth)
    {
        var b = m.Bounds;
        return e switch
        {
            Edge.Left => Rectangle.FromLTRB(b.Left - depth, b.Top, b.Left, b.Bottom),
            Edge.Right => Rectangle.FromLTRB(b.Right, b.Top, b.Right + depth, b.Bottom),
            Edge.Top => Rectangle.FromLTRB(b.Left, b.Top - depth, b.Right, b.Top),
            _ => Rectangle.FromLTRB(b.Left, b.Bottom, b.Right, b.Bottom + depth),
        };
    }

    /// <summary>
    /// Free segments of edge e of m for a parked window depth px deep (ParkGap + its size across the edge), plan 04 §5:
    /// every other monitor near the parking strip takes its projection ± Margin; what is left, within m's work area, is free.
    /// 0, 1 or 2 segments, in axis order.
    /// </summary>
    public static List<Segment> FreeSegments(IReadOnlyList<Display> monitors, Display m, Edge e, int depth)
    {
        var strip = ParkingStrip(m, e, depth);
        var edge = Along(m.Bounds, e);
        var work = Along(m.Work, e);
        var free = new List<Segment> { new(Math.Max(edge.Start, work.Start), Math.Min(edge.End, work.End)) };
        foreach (var n in monitors)
        {
            if (n.Device == m.Device) continue;
            var near = n.Bounds;
            near.Inflate(Margin, Margin);
            if (!near.IntersectsWith(strip)) continue;
            var taken = Along(near, e);
            free = free.SelectMany(f => Subtract(f, taken)).ToList();
        }
        return free.Where(f => f.Length > 0).ToList();
    }

    static IEnumerable<Segment> Subtract(Segment f, Segment taken)
    {
        if (taken.End <= f.Start || taken.Start >= f.End) { yield return f; yield break; }
        if (taken.Start > f.Start) yield return new(f.Start, taken.Start);
        if (taken.End < f.End) yield return new(taken.End, f.End);
    }

    /// <summary>
    /// Where frame goes when tucked to edge e of m: the nearest free segment it fits, shifted along the edge as little as possible,
    /// pressed to the work area; parked is the same rectangle ParkGap behind m's bounds.
    /// </summary>
    public static TuckResult Tuck(IReadOnlyList<Display> monitors, Display m, Edge e, Rectangle frame)
    {
        var segments = FreeSegments(monitors, m, e, ParkGap + Across(frame.Size, e));
        if (segments.Count == 0) return new(null, TuckFail.SharedEdge);
        var workAcross = IsVertical(e) ? m.Work.Width : m.Work.Height;
        if (Across(frame.Size, e) > workAcross) return new(null, TuckFail.TooBig);

        var along = Along(frame, e);
        var fits = segments.Where(s => s.Length >= along.Length)
            .Select(s => (Segment: s, Start: Math.Clamp(along.Start, s.Start, s.End - along.Length)))
            .OrderBy(x => Math.Abs(x.Start - along.Start))
            .ToList();
        if (fits.Count == 0) return new(null, TuckFail.TooLong, segments.Max(s => s.Length));
        var (segment, start) = fits[0];

        var b = m.Bounds;
        var (width, height) = (frame.Width, frame.Height);
        var shown = Pressed(m, e, start, frame.Size);
        var parked = e switch
        {
            Edge.Left => shown with { X = b.Left - ParkGap - width },
            Edge.Right => shown with { X = b.Right + ParkGap },
            Edge.Top => shown with { Y = b.Top - ParkGap - height },
            _ => shown with { Y = b.Bottom + ParkGap },
        };
        return new(new(m, e, shown, parked, segment), TuckFail.None);
    }

    /// <summary>The parts of an edge (within the work area) that are not free: next to another monitor. Tabs there float (v1.0).</summary>
    public static List<Segment> SharedSegments(IReadOnlyList<Display> monitors, Display m, Edge e, int depth)
    {
        var edge = Along(m.Bounds, e);
        var work = Along(m.Work, e);
        var shared = new List<Segment> { new(Math.Max(edge.Start, work.Start), Math.Min(edge.End, work.End)) };
        foreach (var f in FreeSegments(monitors, m, e, depth)) shared = shared.SelectMany(s => Subtract(s, f)).ToList();
        return shared.Where(s => s.Length > 0).ToList();
    }

    /// <summary>
    /// A tab on a shared part of edge e (v1.0): the window shows pressed to e, but waits behind an outer edge of the same monitor
    /// (the opposite one first), so the parked invariant is the usual one. Slides are instant, the tab opens by click.
    /// </summary>
    public static TuckResult TuckFloating(IReadOnlyList<Display> monitors, Display m, Edge e, Rectangle frame)
    {
        var shared = SharedSegments(monitors, m, e, ParkGap + Across(frame.Size, e));
        if (shared.Count == 0) return new(null, TuckFail.SharedEdge);
        var workAcross = IsVertical(e) ? m.Work.Width : m.Work.Height;
        if (Across(frame.Size, e) > workAcross) return new(null, TuckFail.TooBig);
        var along = Along(frame, e);
        var fits = shared.Where(s => s.Length >= along.Length)
            .Select(s => (Segment: s, Start: Math.Clamp(along.Start, s.Start, s.End - along.Length)))
            .OrderBy(x => Math.Abs(x.Start - along.Start))
            .ToList();
        if (fits.Count == 0) return new(null, TuckFail.TooLong, shared.Max(s => s.Length));
        var (segment, start) = fits[0];
        var shown = Pressed(m, e, start, frame.Size);
        foreach (var park in ParkOrder(e))
            if (Tuck(monitors, m, park, shown).Plan is { } p)
                return new(new(m, e, shown, p.Parked, segment, Floating: true, ParkEdge: park), TuckFail.None);
        return new(null, TuckFail.SharedEdge);
    }

    /// <summary>Free part of the edge first; else (v1.0, when allowed) a floating tab on its shared part. The first refusal is the one reported.</summary>
    public static TuckResult Plan(IReadOnlyList<Display> monitors, Display m, Edge e, Rectangle frame, bool floating)
    {
        var r = Tuck(monitors, m, e, frame);
        if (r.Plan is not null || !floating || r.Fail == TuckFail.TooBig) return r;
        var f = TuckFloating(monitors, m, e, frame);
        return f.Plan is not null ? f : r;
    }

    static IEnumerable<Edge> ParkOrder(Edge e) =>
        new[] { Opposite(e), Edge.Bottom, Edge.Top, Edge.Left, Edge.Right }.Where(x => x != e).Distinct();

    public static Edge Opposite(Edge e) => e switch { Edge.Left => Edge.Right, Edge.Right => Edge.Left, Edge.Top => Edge.Bottom, _ => Edge.Top };

    /// <summary>A window of this size at `start` along edge e of m, pressed to the edge of the work area.</summary>
    static Rectangle Pressed(Display m, Edge e, int start, Size size)
    {
        var w = m.Work;
        return e switch
        {
            Edge.Left => new Rectangle(w.Left, start, size.Width, size.Height),
            Edge.Right => new Rectangle(w.Right - size.Width, start, size.Width, size.Height),
            Edge.Top => new Rectangle(start, w.Top, size.Width, size.Height),
            _ => new Rectangle(start, w.Bottom - size.Height, size.Width, size.Height),
        };
    }

    /// <summary>The monitor with the largest part of r; if r is on none, the nearest one.</summary>
    public static Display MonitorOf(Rectangle r, IReadOnlyList<Display> monitors) =>
        monitors.MaxBy(m => Area(Rectangle.Intersect(r, m.Bounds))) is { } best && Area(Rectangle.Intersect(r, best.Bounds)) > 0
            ? best
            : monitors.MinBy(m => Distance(r, m.Bounds))!;

    /// <summary>
    /// Drag-past-the-edge: at least threshold of the window is behind the left, right or bottom edge of the monitor
    /// that has most of it, and that part is on no monitor. Never the top (Windows pulls the caption back), never maximized.
    /// </summary>
    public static (Display Monitor, Edge Edge)? DragGestureEdge(IReadOnlyList<Display> monitors, Rectangle frame, bool zoomed, double threshold = 0.5)
    {
        if (zoomed || frame.Width <= 0 || frame.Height <= 0 || monitors.Count == 0) return null;
        var m = MonitorOf(frame, monitors);
        var b = m.Bounds;
        foreach (var e in new[] { Edge.Left, Edge.Right, Edge.Bottom })
        {
            var behind = e switch
            {
                Edge.Left => Rectangle.FromLTRB(frame.Left, frame.Top, Math.Min(frame.Right, b.Left), frame.Bottom),
                Edge.Right => Rectangle.FromLTRB(Math.Max(frame.Left, b.Right), frame.Top, frame.Right, frame.Bottom),
                _ => Rectangle.FromLTRB(frame.Left, Math.Max(frame.Top, b.Bottom), frame.Right, frame.Bottom),
            };
            if (behind.Width <= 0 || behind.Height <= 0) continue;
            if (Area(behind) < threshold * Area(frame)) continue;
            if (monitors.Any(n => n.Bounds.IntersectsWith(behind))) continue;
            return (m, e);
        }
        return null;
    }

    /// <summary>Fullscreen = covers the whole monitor and is not maximized (a maximized window covers it too, with its borders).</summary>
    public static bool IsFullscreen(Rectangle window, Rectangle monitor, bool zoomed) => !zoomed && window.Contains(monitor);

    /// <summary>
    /// Slide an Out window back in? Not while pinned, a mouse button is down or the cursor is over a window of the same process,
    /// nor while it is within margin of the window.
    /// </summary>
    public static bool ShouldSlideIn(bool pinned, bool buttonDown, bool overSameProcess, Point cursor, Rectangle shown, int margin)
    {
        if (pinned || buttonDown || overSameProcess) return false;
        shown.Inflate(margin, margin);
        return !shown.Contains(cursor);
    }

    /// <summary>Slide out: quick start, soft landing at the edge (cubic ease-out).</summary>
    public static double EaseOut(double t) => 1 - Math.Pow(1 - Math.Clamp(t, 0, 1), 3);

    /// <summary>Slide in and tuck: gentle start and stop (cubic ease-in-out).</summary>
    public static double EaseInOut(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
    }

    /// <summary>DIP → physical px on a monitor (6 DIP at 125 % = 8 px).</summary>
    public static int Px(int dip, int dpi) => (int)Math.Round(dip * dpi / 96.0, MidpointRounding.AwayFromZero);

    /// <summary>One tab in physical px: thickness across the edge, length along it.</summary>
    public readonly record struct TabPx(int Thickness, int Length);

    public const int MinTabThickness = 2, MaxTabThickness = 48, MinTabLength = 16, MaxTabLength = 600;
    const int TabEndMarginDip = 64, TabGapDip = 8;

    /// <summary>
    /// v1.1.1: the size the user set, in px as it is (not scaled by the monitor's DPI), within the limits;
    /// a tab with the program's icon is at least 24×48 DIP so the icon fits.
    /// </summary>
    public static TabPx TabSize(int thickness, int length, bool icon, int dpi)
    {
        int t = Math.Clamp(thickness, MinTabThickness, MaxTabThickness), l = Math.Clamp(length, MinTabLength, MaxTabLength);
        return icon ? new(Math.Max(t, Px(24, dpi)), Math.Max(l, Px(48, dpi))) : new(t, l);
    }

    /// <summary>
    /// Tabs on one segment of one edge, in px of m: at the work-area edge (above a taskbar), as near the wanted centers as they can be,
    /// not overlapping, no closer than 64 DIP to the segment ends. Each tab has its own size. Same order as given.
    /// </summary>
    public static Rectangle[] Tabs(Display m, Edge e, Segment segment, IReadOnlyList<(int Center, TabPx Size)> tabs)
    {
        int gap = Px(TabGapDip, m.Dpi), end = Px(TabEndMarginDip, m.Dpi);
        bool TooShort(int len) => segment.End - end - len < segment.Start + end; // for the margins: center it
        int Lo(int len) => TooShort(len) ? segment.Start + (segment.Length - len) / 2 : segment.Start + end;
        int Hi(int len) => TooShort(len) ? Lo(len) : segment.End - end - len;

        var order = Enumerable.Range(0, tabs.Count).OrderBy(i => tabs[i].Center).ToArray();
        var starts = new int[tabs.Count];
        int? prevEnd = null;
        foreach (var i in order) // forward: no overlap, not before lo
        {
            var len = tabs[i].Size.Length;
            starts[i] = Math.Max(Math.Max(tabs[i].Center - len / 2, Lo(len)), prevEnd + gap ?? int.MinValue);
            prevEnd = starts[i] + len;
        }
        int? nextStart = null;
        foreach (var i in order.Reverse()) // backward: not after hi
        {
            var len = tabs[i].Size.Length;
            nextStart = starts[i] = Math.Min(starts[i], Math.Min(Hi(len), nextStart - gap - len ?? int.MaxValue));
        }

        var w = m.Work;
        return Enumerable.Range(0, tabs.Count).Select(i =>
        {
            var (s, (t, len)) = (starts[i], tabs[i].Size);
            return e switch
            {
                Edge.Left => new Rectangle(w.Left, s, t, len),
                Edge.Right => new Rectangle(w.Right - t, s, t, len),
                Edge.Top => new Rectangle(s, w.Top, len, t),
                _ => new Rectangle(s, w.Bottom - t, len, t),
            };
        }).ToArray();
    }

    /// <summary>Chebyshev gap between two rectangles: 0 when they touch or overlap.</summary>
    public static int Distance(Rectangle a, Rectangle b) =>
        Math.Max(Math.Max(0, Math.Max(a.Left - b.Right, b.Left - a.Right)), Math.Max(0, Math.Max(a.Top - b.Bottom, b.Top - a.Bottom)));

    public static long Area(Rectangle r) => r.Width <= 0 || r.Height <= 0 ? 0 : (long)r.Width * r.Height;

    /// <summary>Fraction (0..1) of the whole edge length at a point on it, and back: rules keep "along" this way.</summary>
    public static double AlongFraction(Display m, Edge e, int center)
    {
        var edge = Along(m.Bounds, e);
        return edge.Length == 0 ? 0.5 : Math.Clamp((center - edge.Start) / (double)edge.Length, 0, 1);
    }

    public static int AlongPoint(Display m, Edge e, double fraction)
    {
        var edge = Along(m.Bounds, e);
        return edge.Start + (int)Math.Round(edge.Length * Math.Clamp(fraction, 0, 1));
    }

    /// <summary>frame moved along edge e so that its center is at center (rules and the demo place windows this way).</summary>
    public static Rectangle CenteredAlong(Rectangle frame, Edge e, int center) =>
        IsVertical(e) ? frame with { Y = center - frame.Height / 2 } : frame with { X = center - frame.Width / 2 };
}
