using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using EdgeTuck.Shell;

namespace EdgeTuck.Features;

/// <summary>One tucked window. Shown and Parked are frames [left, top, right, bottom].</summary>
public sealed class JournalEntry
{
    public long Hwnd { get; set; }
    public int Pid { get; set; }
    public DateTime ProcStart { get; set; }
    public string Exe { get; set; } = "";
    public string Class { get; set; } = "";
    public Edge Edge { get; set; }
    public string Monitor { get; set; } = "";
    public int[] Shown { get; set; } = [0, 0, 0, 0];
    public int[] Parked { get; set; } = [0, 0, 0, 0];
    public bool WasMaximized { get; set; }

    [JsonIgnore] public Rectangle ShownRect => Rectangle.FromLTRB(Shown[0], Shown[1], Shown[2], Shown[3]);
    [JsonIgnore] public Rectangle ParkedRect => Rectangle.FromLTRB(Parked[0], Parked[1], Parked[2], Parked[3]);

    public static int[] Ltrb(Rectangle r) => [r.Left, r.Top, r.Right, r.Bottom];
}

public sealed class JournalFile
{
    public int Version { get; set; } = 1;
    public int OwnerPid { get; set; }
    public DateTime OwnerStart { get; set; }
    public List<JournalEntry> Windows { get; set; } = [];
    /// <summary>File names of programs that were ever tucked: if one opens a window off all monitors, it is brought back (plan 04 §8).</summary>
    public List<string> RescueExes { get; set; } = [];
}

/// <summary>
/// %APPDATA%\EdgeTuck\tucked.json, written before every first move (write-ahead) and after every return.
/// Read by the next start and by the guard process; under --demo nothing is written.
/// </summary>
public static class Journal
{
    public static string FilePath => Path.Combine(AppInfo.DataDir, "tucked.json");

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static JournalFile Load(string path) => AtomicFile.Read(path, s => JsonSerializer.Deserialize<JournalFile>(s, Json)) ?? new();

    public static void Save(string path, JournalFile file) => AtomicFile.WriteAllText(path, JsonSerializer.Serialize(file, Json));

    /// <summary>HWNDs and PIDs are reused: an entry belongs to a live window only if pid, process start and class all match.</summary>
    public static bool Matches(JournalEntry e, int pid, DateTime? procStart, string cls) =>
        e.Pid == pid && procStart is { } s && Math.Abs((s - e.ProcStart).TotalMilliseconds) < 1 && e.Class == cls;

    /// <summary>Where a window goes back: its shown position with its current size, inside the nearest monitor that still exists.</summary>
    public static Rectangle RestoreTarget(JournalEntry e, Size size, IReadOnlyList<Display> monitors) =>
        Monitors.ClampToVisible(new Rectangle(e.ShownRect.Location, size), monitors);

    public const int MaxRescueExes = 32;

    public static void AddRescue(JournalFile f, string exe)
    {
        var name = Path.GetFileName(exe);
        if (name.Length == 0 || f.RescueExes.Contains(name, StringComparer.OrdinalIgnoreCase)) return;
        f.RescueExes.Add(name);
        if (f.RescueExes.Count > MaxRescueExes) f.RescueExes.RemoveAt(0);
    }
}
