using System.Drawing;
using System.IO;
using EdgeTuck.Shell;

namespace EdgeTuck.Features;

/// <summary>Matching new windows to rules (pure); the runtime part is in TuckManager.</summary>
public static class Rules
{
    /// <summary>
    /// The first enabled rule for this exe (full path or file name), case-insensitive, whose title filter (part of the title, any case)
    /// and minimum size the window passes. No title or size given: those checks are skipped.
    /// </summary>
    public static Rule? Match(IEnumerable<Rule> rules, string? exe, string? title = null, Size? size = null) =>
        rules.FirstOrDefault(r => r.Enabled && Fits(r, exe, title, size));

    /// <summary>The rule's program, title filter and minimum size, enabled or not (a rule's own hotkey works on disabled rules too).</summary>
    public static bool Fits(Rule r, string? exe, string? title, Size? size)
    {
        if (string.IsNullOrEmpty(exe) || !r.Exe.Equals(Path.GetFileName(exe), StringComparison.OrdinalIgnoreCase)) return false;
        if (r.TitleContains is { } part && title is not null && !title.Contains(part, StringComparison.OrdinalIgnoreCase)) return false;
        return size is not { } s || (s.Width >= r.MinWidth && s.Height >= r.MinHeight);
    }

    /// <summary>v1.0 templates, off until switched on: the programs people tuck most. The size limit keeps splash screens out.</summary>
    public static Rule[] Templates =>
    [
        new() { Exe = "scrcpy.exe", Edge = Edge.Right, Along = 0.5, Enabled = false, MinWidth = 200, MinHeight = 300 },
        new() { Exe = "Discord.exe", Edge = Edge.Left, Along = 0.5, Enabled = false, MinWidth = 500, MinHeight = 400 },
        new() { Exe = "Spotify.exe", Edge = Edge.Left, Along = 0.8, Enabled = false, MinWidth = 400, MinHeight = 300 },
        new() { Exe = "WindowsTerminal.exe", Edge = Edge.Top, Along = 0.5, Enabled = false, AutoTuck = false, MinWidth = 400, MinHeight = 200 },
    ];

    /// <summary>
    /// The monitor and where along its edge the window should go: the rule's monitor, or (gone) the one the window is on, or the primary;
    /// the window's center at Along of the whole edge. Tuck then moves it into the nearest free segment.
    /// </summary>
    public static (Display Monitor, Rectangle Frame) Target(Rule r, Rectangle frame, IReadOnlyList<Display> monitors)
    {
        var m = monitors.FirstOrDefault(d => SameMonitor(d.Device, r.Monitor))
            ?? (monitors.Any(d => d.Bounds.IntersectsWith(frame)) ? Geometry.MonitorOf(frame, monitors) : Primary(monitors));
        return (m, Geometry.CenteredAlong(frame, r.Edge, Geometry.AlongPoint(m, r.Edge, r.Along)));
    }

    /// <summary>@"\\.\DISPLAY2", "DISPLAY2" and "display2" are the same monitor (hand-edited settings).</summary>
    public static bool SameMonitor(string? a, string? b) =>
        a is not null && b is not null && a.TrimStart('\\', '.').Equals(b.TrimStart('\\', '.'), StringComparison.OrdinalIgnoreCase);

    public static Display Primary(IReadOnlyList<Display> monitors) =>
        monitors.FirstOrDefault(d => d.Bounds.Contains(0, 0)) ?? monitors[0];

    /// <summary>A rule from a tucked window: its exe, edge, monitor and center.</summary>
    public static Rule From(string exe, TuckPlan p) => new()
    {
        Exe = Path.GetFileName(exe), Edge = p.Edge, Monitor = p.Monitor.Device,
        Along = Math.Round(Geometry.AlongFraction(p.Monitor, p.Edge, Center(Geometry.Along(p.Shown, p.Edge))), 3),
    };

    /// <summary>
    /// v1.1.1: a tab's size in px: the window's own, else that of the first rule of its program that sets one, else Settings;
    /// each side on its own (an empty thickness with a set length takes the thickness from further down).
    /// </summary>
    public static Geometry.TabPx TabSize(EdgeTuckSettings s, string program, int? ownThickness, int? ownLength, int dpi)
    {
        var rule = s.Rules.FirstOrDefault(r => (r.TabThickness ?? r.TabLength) is not null && r.Exe.Equals(program, StringComparison.OrdinalIgnoreCase));
        return Geometry.TabSize(ownThickness ?? rule?.TabThickness ?? s.TabThickness, ownLength ?? rule?.TabLength ?? s.TabLength, s.TabIcon, dpi);
    }

    static int Center(Segment s) => s.Start + s.Length / 2;
}
