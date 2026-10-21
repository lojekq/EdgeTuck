using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Threading;
using EdgeTuck.Shell;

namespace EdgeTuck.Features;

public enum TuckState { Parked, Out, OutPinned }

/// <summary>A tucked window: what the journal keeps plus the live state (plan 04 §5 "Состояния окна").</summary>
public sealed class Tucked : INotifyPropertyChanged
{
    TuckPlan plan = null!;
    TuckState state;
    string title = "";

    public required nint Hwnd { get; init; }
    public required int Pid { get; init; }
    public DateTime ProcStart { get; init; }
    public string Exe { get; init; } = "";
    public string Class { get; init; } = "";
    public bool WasMaximized { get; init; }
    public bool WasTopmost { get; init; }
    public ImageSource? Icon { get; init; }
    /// <summary>--demo: the program name to show instead of our own exe.</summary>
    public string? DemoExe { get; init; }

    public TuckPlan Plan
    {
        get => plan;
        set { plan = value; Changed(); Changed(nameof(Detail)); }
    }

    public TuckState State
    {
        get => state;
        set { state = value; Changed(); Changed(nameof(Detail)); }
    }

    public string Title
    {
        get => title;
        set { title = value; Changed(); }
    }

    public string Program => DemoExe ?? Path.GetFileName(Exe);
    public string Detail => $"{Program} · {MonitorName(Plan.Monitor.Device)} · {Loc.T("Edge." + Plan.Edge)} · {Loc.T("State." + State)}";
    public bool HasIcon => Icon is not null;

    public static string MonitorName(string device) => device.Replace(@"\\.\", "");

    // Live state, UI thread only
    internal TabWindow? Tab;
    internal DispatcherTimer? Debounce;
    internal Rectangle? LastSeen;       // foreign moves count only once the window stands still
    internal DateTime? LeaveSince;
    internal Anim? Anim;
    internal bool Hidden;               // its program hid the window (to the tray)
    internal int Fights;
    internal int? DragAlong;
    internal nint PeekDialog;           // its dialog that opened in the background, topmost until looked at
    internal bool Peeking;              // topmost for a peek by hover: undone when it slides in, is pinned or untucked            // v1.0: the tab is being dragged along the edge to here
    internal DateTime FightsSince;

    public JournalEntry ToEntry() => new()
    {
        Hwnd = Hwnd, Pid = Pid, ProcStart = ProcStart, Exe = Exe, Class = Class, Edge = Plan.Edge, Monitor = Plan.Monitor.Device,
        Shown = JournalEntry.Ltrb(Plan.Shown), Parked = JournalEntry.Ltrb(Plan.Parked), WasMaximized = WasMaximized, WasTopmost = WasTopmost,
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    void Changed([CallerMemberName] string? p = null) => PropertyChanged?.Invoke(this, new(p));
}

/// <summary>One slide: frames every 16 ms, ease-out.</summary>
internal sealed class Anim(Point from, Point to, int ms, bool toTop, Action? done)
{
    public readonly Point From = from, To = to;
    public readonly long Started = System.Diagnostics.Stopwatch.GetTimestamp();
    public readonly int Ms = ms;
    public readonly Action? Done = done;
    public bool ToTop = toTop;                 // HWND_TOP on the first frame only
    public Point Current = from;

    /// <summary>Position now; finished once t reaches 1.</summary>
    public (Point P, bool Finished) Step()
    {
        var t = Math.Min(1, System.Diagnostics.Stopwatch.GetElapsedTime(Started).TotalMilliseconds / Ms);
        var k = 1 - Math.Pow(1 - t, 3);
        Current = new((int)Math.Round(From.X + (To.X - From.X) * k), (int)Math.Round(From.Y + (To.Y - From.Y) * k));
        return (Current, t >= 1);
    }
}
