using System.Diagnostics;
using System.Drawing;
using EdgeTuck.Shell;

namespace EdgeTuck.Features;

/// <summary>
/// Brings journal entries back on screen. One code path for exit, session end, crash, the guard process and the next start;
/// plain Win32, no WPF, idempotent (a second run moves nothing).
/// </summary>
public static class Restore
{
    public static int Entries(IEnumerable<JournalEntry> entries)
    {
        var monitors = Monitors.Snapshot();
        var n = 0;
        foreach (var e in entries)
            try { if (One(e, monitors)) n++; }
            catch (Exception ex) { Log.Error("restore failed", ex); } // the other windows still come back
        return n;
    }

    /// <summary>False when the window is gone or the handle now belongs to someone else (pid, start time or class differ).</summary>
    public static bool One(JournalEntry e, IReadOnlyList<Display> monitors)
    {
        var h = (nint)e.Hwnd;
        if (h == 0 || !Win.IsWindow(h)) return false;
        var pid = TopWindows.Pid(h);
        if (!Journal.Matches(e, pid, ProcessInfo.StartTime(pid), TopWindows.ClassOf(h))) return false;
        var hung = Win.IsHung(h);

        if (Win.IsIconic(h))
        {
            // Minimized: SetWindowPos would move the icon, not where it comes back to
            if (Win.NormalRect(h, monitors) is { } normal && !monitors.Any(m => m.Bounds.IntersectsWith(Rectangle.Inflate(normal, -16, -16))))
                Win.ShiftMinimized(h, new Size(e.ShownRect.Left - e.ParkedRect.Left, e.ShownRect.Top - e.ParkedRect.Top), hung);
            return true;
        }
        if (Win.Frame(h) is not { } frame) return false;
        var target = Journal.RestoreTarget(e, frame.Size, monitors).Location;
        // Sync moves first, but our own queued async frames would then still run after it: the async copy lands last
        if (!hung) Win.MoveFrame(h, target, async: false);
        Win.MoveFrame(h, target, async: true);
        if (e.WasMaximized && !Win.IsZoomed(h)) Win.Maximize(h);
        return true;
    }
}

/// <summary>
/// EdgeTuck.exe --guard &lt;pid&gt; &lt;startTicks&gt;: a second process that waits for the main one to die and brings the windows back
/// (plan 04 §5). The main process clears the journal on a normal exit, so then the guard just leaves.
/// </summary>
public static class GuardMode
{
    public static int Run(string[] args)
    {
        if (args.Length < 2 || !int.TryParse(args[0], out var pid) || !long.TryParse(args[1], out var ticks)) return 2;
        try
        {
            using var parent = Process.GetProcessById(pid); // the open handle keeps the pid from being reused while we wait
            _ = parent.Handle;
            if (ProcessInfo.StartTime(pid)?.Ticks != ticks) return 0; // the pid belongs to another process now
            parent.WaitForExit();
        }
        catch (ArgumentException) { } // already gone: still look at the journal
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }

        var journal = Journal.Load(Journal.FilePath);
        if (journal.OwnerPid != pid || journal.OwnerStart.Ticks != ticks || journal.Windows.Count == 0) return 0;
        var n = Restore.Entries(journal.Windows);
        Log.Info($"main process {pid} is gone: {n} of {journal.Windows.Count} window(s) returned");
        journal.Windows.Clear();
        Journal.Save(Journal.FilePath, journal);
        return 0;
    }
}
