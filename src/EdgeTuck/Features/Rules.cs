using System.Drawing;
using System.IO;
using EdgeTuck.Shell;

namespace EdgeTuck.Features;

/// <summary>Matching new windows to rules (pure); the runtime part is in TuckManager.</summary>
public static class Rules
{
    /// <summary>The first enabled rule for this exe (full path or file name), case-insensitive.</summary>
    public static Rule? Match(IEnumerable<Rule> rules, string? exe)
    {
        if (string.IsNullOrEmpty(exe)) return null;
        var name = Path.GetFileName(exe);
        return rules.FirstOrDefault(r => r.Enabled && r.Exe.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The monitor and where along its edge the window should go: the rule's monitor, or (gone) the one the window is on, or the primary;
    /// the window's center at Along of the whole edge. Tuck then moves it into the nearest free segment.
    /// </summary>
    public static (Display Monitor, Rectangle Frame) Target(Rule r, Rectangle frame, IReadOnlyList<Display> monitors)
    {
        var m = monitors.FirstOrDefault(d => d.Device == r.Monitor)
            ?? (monitors.Any(d => d.Bounds.IntersectsWith(frame)) ? Geometry.MonitorOf(frame, monitors) : Primary(monitors));
        return (m, Geometry.CenteredAlong(frame, r.Edge, Geometry.AlongPoint(m, r.Edge, r.Along)));
    }

    public static Display Primary(IReadOnlyList<Display> monitors) =>
        monitors.FirstOrDefault(d => d.Bounds.Contains(0, 0)) ?? monitors[0];

    /// <summary>A rule from a tucked window: its exe, edge, monitor and center.</summary>
    public static Rule From(string exe, TuckPlan p) => new()
    {
        Exe = Path.GetFileName(exe), Edge = p.Edge, Monitor = p.Monitor.Device,
        Along = Math.Round(Geometry.AlongFraction(p.Monitor, p.Edge, Center(Geometry.Along(p.Shown, p.Edge))), 3),
    };

    static int Center(Segment s) => s.Start + s.Length / 2;
}
