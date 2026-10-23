using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Controls;
using System.Windows.Threading;
using EdgeTuck.Shell;

namespace EdgeTuck.Features;

public enum TuckSource { Hotkey, Gesture, Rule, Demo }

/// <summary>--demo: what the fake windows show instead of our own process.</summary>
public sealed record DemoInfo(string Exe, string Title, System.Windows.Media.ImageSource? Icon);

/// <summary>
/// States of tucked windows, WinEvent and hotkey handlers, slides, RestoreAll (plan 04 §5). UI thread only:
/// out-of-context WinEvents arrive through its message loop, so there are no locks.
/// </summary>
public static class TuckManager
{
    const uint EVENT_SYSTEM_FOREGROUND = 0x0003, EVENT_SYSTEM_MOVESIZESTART = 0x000A, EVENT_SYSTEM_MOVESIZEEND = 0x000B,
        EVENT_OBJECT_DESTROY = 0x8001, EVENT_OBJECT_SHOW = 0x8002, EVENT_OBJECT_HIDE = 0x8003, EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    const int HotkeyBase = 100, HotkeyRestoreAll = 110, HotkeyRules = 200, MaxRuleHotkeys = 50;
    const int SpiSetWorkArea = 0x002F, PbtResumeAutomatic = 0x0012;
    const int LeaveMargin = 16;
    static readonly HashSet<string> ShellClasses = ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd"];

    public static ObservableCollection<Tucked> Windows { get; } = [];
    static readonly Dictionary<nint, Tucked> ByHwnd = [];
    static readonly Dictionary<int, (IDisposable Hook, int Refs)> LocationHooks = [];
    static List<Display> monitors = [];
    static JournalFile journal = new();
    static string? fullscreenDevice;
    static bool started, exiting, paused;

    static bool animating; // CompositionTarget.Rendering is subscribed only while something slides: no idle cost
    static readonly DispatcherTimer LeaveTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    static readonly DispatcherTimer FullscreenTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    static readonly DispatcherTimer DisplayDebounce = new() { Interval = TimeSpan.FromSeconds(1.5) };

    static Process? guard;
    static readonly List<DateTime> GuardRestarts = [];

    static EdgeTuckSettings S => Settings.Current.EdgeTuck;
    static bool Demo => Args.Current.Demo;
    public static IReadOnlyList<Display> CurrentMonitors => monitors;

    /// <summary>Raised when the set of tucked windows or one of them changes (tray, pages).</summary>
    public static event Action? Changed;

    public static bool Paused
    {
        get => paused;
        set
        {
            if (paused == value) return;
            paused = value;
            if (value) RestoreAll();
            Log.Info(value ? "paused" : "resumed");
            Changed?.Invoke();
        }
    }

    /// <summary>App start (plan 04 §5 "Запуск"): leftovers of a crash back, journal, guard, hooks, hotkeys, rules for open windows.</summary>
    public static void Start()
    {
        if (started) return;
        started = true;
        monitors = Monitors.Snapshot();
        LeaveTimer.Tick += (_, _) => LeaveTick();
        FullscreenTimer.Tick += (_, _) => CheckFullscreen();
        DisplayDebounce.Tick += (_, _) => { DisplayDebounce.Stop(); Relayout(); };
        App.Exiting += OnExiting;
        if (Demo) { RegisterHotkeys(toastBusy: true); return; } // own fake windows only: no journal, guard, hooks or rules (00-shared §2.3)

        // The journal first: whatever fails later, a crash must not write an empty journal over windows a previous run left
        RecoverLeftovers();
        var me = Process.GetCurrentProcess();
        journal = new JournalFile
        {
            OwnerPid = me.Id, OwnerStart = ProcessInfo.StartTime(me.Id) ?? DateTime.UtcNow,
            RescueExes = journal.RescueExes,
        };
        journalTaken = true;
        SaveJournal();
        StartGuard();
        RegisterHotkeys(toastBusy: true);

        WinEvents.Hook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, OnEvent);
        WinEvents.Hook(EVENT_SYSTEM_MOVESIZESTART, EVENT_SYSTEM_MOVESIZEEND, OnEvent);
        WinEvents.Hook(EVENT_OBJECT_DESTROY, EVENT_OBJECT_HIDE, OnEvent);
        MessageWindow.WndProc += OnMessage;
        StartShellHook();
        foreach (var w in TopWindows.List()) ApplyRule(w.Hwnd);
    }

    const int HshellFlash = 0x8006; // HSHELL_REDRAW | HSHELL_HIGHBIT
    static int shellHookMessage;

    /// <summary>
    /// RegisterShellHookWindow is "not intended for general use" and may go away (plan 04 §6): if it fails, tabs just never flash.
    /// </summary>
    static void StartShellHook()
    {
        try
        {
            shellHookMessage = Win.RegisterShellHook(MessageWindow.Handle);
            if (shellHookMessage == 0) Log.Info("shell hook not available: tabs will not flash");
        }
        catch (Exception e) { Log.Warn("shell hook", e); }
    }

    static void OnExiting()
    {
        exiting = true;
        RestoreAll();
        foreach (var h in LocationHooks.Values) h.Hook.Dispose();
        LocationHooks.Clear();
        // The guard sees the empty journal and leaves by itself
    }

    // ---------- journal and guard ----------

    static void RecoverLeftovers()
    {
        try
        {
            var old = Journal.Load(Journal.FilePath);
            journal.RescueExes = old.RescueExes;
            if (old.Windows.Count == 0) return;
            var n = Restore.Entries(old.Windows);
            Log.Info($"start: {n} of {old.Windows.Count} window(s) left by the last run returned");
            if (n > 0) Toast("Toast.Recovered", n);
        }
        catch (Exception e) { Log.Error("journal not recovered", e); }
    }

    /// <summary>Set once this process has read the previous journal and written its own: only then may it save over the file.</summary>
    static bool journalTaken;

    static void SaveJournal()
    {
        if (Demo || !journalTaken) return;
        try { Journal.Save(Journal.FilePath, journal); }
        catch (Exception e) { Log.Error("journal not saved", e); }
    }

    static void StartGuard()
    {
        try
        {
            var me = Environment.ProcessId;
            var psi = new ProcessStartInfo(AppInfo.ExePath) { UseShellExecute = false, CreateNoWindow = true };
            psi.ArgumentList.Add("--guard");
            psi.ArgumentList.Add(me.ToString());
            psi.ArgumentList.Add(journal.OwnerStart.Ticks.ToString());
            guard = Process.Start(psi);
            if (guard is null) return;
            guard.EnableRaisingEvents = true;
            guard.Exited += (_, _) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(OnGuardExited);
            Log.Info($"guard {guard.Id} started");
        }
        catch (Exception e) { Log.Error("guard not started", e); }
    }

    static void OnGuardExited()
    {
        if (exiting) return;
        GuardRestarts.RemoveAll(t => DateTime.UtcNow - t > TimeSpan.FromMinutes(1));
        if (GuardRestarts.Count >= 3) { Log.Error("guard keeps dying, not restarted"); return; }
        GuardRestarts.Add(DateTime.UtcNow);
        Log.Warn($"guard exited with {SafeExitCode()}, restarting");
        StartGuard();
    }

    static string SafeExitCode() { try { return guard?.ExitCode.ToString() ?? "?"; } catch { return "?"; } }

    /// <summary>For tests of the guard: its process id, or 0.</summary>
    public static int GuardPid { get { try { return guard is { HasExited: false } g ? g.Id : 0; } catch { return 0; } } }

    // ---------- hotkeys ----------

    /// <summary>Registers all five; the settings page shows each result next to its box.</summary>
    public static Dictionary<string, HotkeyStatus> RegisterHotkeys(bool toastBusy = false)
    {
        var result = new Dictionary<string, HotkeyStatus>();
        foreach (var e in Geometry.Edges)
            result[e.ToString()] = Register(HotkeyBase + (int)e, S.Hotkey(e), () => OnHotkey(e), toastBusy);
        result["RestoreAll"] = Register(HotkeyRestoreAll, S.HotkeyRestoreAll, () =>
        {
            var n = RestoreAll();
            Toast("Toast.RestoredAll", n);
        }, toastBusy);
        RegisterRuleHotkeys(toastBusy);
        return result;
    }

    /// <summary>v1.0: each rule's own "show or hide this window" key (quake mode for any program).</summary>
    public static void RegisterRuleHotkeys(bool toastBusy = true)
    {
        for (var i = 0; i < MaxRuleHotkeys; i++) Hotkeys.Unregister(HotkeyRules + i);
        var rules = S.Rules;
        for (var i = 0; i < Math.Min(rules.Count, MaxRuleHotkeys); i++)
        {
            var r = rules[i];
            if (r.Hotkey is not null) Register(HotkeyRules + i, r.Hotkey, () => OnRuleHotkey(r), toastBusy);
        }
    }

    static void OnRuleHotkey(Rule r)
    {
        if (Windows.FirstOrDefault(x => Rules.Fits(r, x.DemoExe ?? x.Exe, x.Title, null)) is { } t)
        {
            if (t.State == TuckState.Parked) { SlideOut(t, activate: true); return; }
            var wasForeground = Win.Foreground == t.Hwnd;
            SlideIn(t);
            if (wasForeground) ActivateNext(t); // the keys should not go on typing into a window nobody sees
            return;
        }
        monitors = Monitors.Snapshot();
        foreach (var w in TopWindows.List())
        {
            if (w.Cloaked || ByHwnd.ContainsKey(w.Hwnd) || Ineligible(w.Hwnd) is not null || Win.Frame(w.Hwnd) is not { } f) continue;
            if (!Rules.Fits(r, ProcessInfo.ExePath(w.Pid), w.Title, f.Size)) continue;
            var (m, at) = Rules.Target(r, f, monitors);
            TryTuck(w.Hwnd, r.Edge, m, at, TuckSource.Hotkey);
            return;
        }
    }

    static HotkeyStatus Register(int id, string? text, Action pressed, bool toastBusy)
    {
        if (Hotkey.Parse(text) is not { } h) { Hotkeys.Unregister(id); return HotkeyStatus.Ok; }
        var status = Hotkeys.Register(id, h, pressed);
        if (status == HotkeyStatus.Busy && toastBusy) Toast("Toast.HotkeyBusy", h.ToString());
        return status;
    }

    static void OnHotkey(Edge e)
    {
        var fg = Win.Foreground;
        if (FindTucked(fg) is { } t)
        {
            if (fg != t.Hwnd) return; // a dialog of a tucked window
            Untuck(t, move: true);
            if (t.Plan.Edge == e) return; // the same combination again: back to normal
        }
        TryTuck(fg, e, null, null, TuckSource.Hotkey);
    }

    // ---------- tuck / untuck ----------

    /// <summary>
    /// Tucks hwnd to edge e (plan 04 §5 "Прятание"): checks, un-maximizes, plans, writes the journal, then the first move
    /// synchronously and the rest as a slide. at = where along the edge (rules, demo); monitor = which one (else the window's).
    /// </summary>
    public static bool TryTuck(nint hwnd, Edge e, Display? monitor, Rectangle? at, TuckSource source, DemoInfo? demo = null)
    {
        if (hwnd == 0 || ByHwnd.ContainsKey(hwnd) || !Win.IsWindow(hwnd)) return false;
        var own = source == TuckSource.Demo;
        if (!own && Ineligible(hwnd) is { } why)
        {
            if (source == TuckSource.Hotkey && why.Length > 0) Toast(why);
            return false;
        }
        monitors = Monitors.Snapshot();
        if (monitors.Count == 0 || Win.Frame(hwnd) is not { } before) return false;
        var m = monitor is not null ? monitors.FirstOrDefault(d => d.Device == monitor.Device) ?? monitor : Geometry.MonitorOf(before, monitors);
        var wasMax = Win.IsZoomed(hwnd);
        if (wasMax) Win.RestoreNow(hwnd);
        if (Win.Frame(hwnd) is not { } frame) return false;
        if (at is { } a) frame = new Rectangle(a.Location, frame.Size);

        var r = Geometry.Plan(monitors, m, e, frame, S.SharedEdges);
        if (r.Plan is not { } plan)
        {
            if (wasMax) Win.Maximize(hwnd);
            Log.Info($"tuck refused: {r.Fail} ({e}, {m.Device})");
            if (source is TuckSource.Hotkey or TuckSource.Gesture) Toast(r.Fail switch
            {
                TuckFail.SharedEdge => "Toast.SharedEdge",
                TuckFail.TooLong => "Toast.TooLong",
                _ => "Toast.TooBig",
            }, r.FreePx);
            return false;
        }
        if (!Win.OnNoMonitor(plan.Parked))
        {
            if (wasMax) Win.Maximize(hwnd);
            Log.Error($"parked rectangle is on a monitor, not tucked ({e}, {m.Device})");
            return false;
        }

        var pid = TopWindows.Pid(hwnd);
        if (ProcessInfo.StartTime(pid) is null)
        {
            if (wasMax) Win.Maximize(hwnd);
            Log.Warn("tuck refused: process start time unknown, the window could not be recognized after a crash");
            return false;
        }
        var exe = own ? "" : ProcessInfo.ExePath(pid) ?? "";
        var t = new Tucked
        {
            Hwnd = hwnd, Pid = pid, ProcStart = ProcessInfo.StartTime(pid)!.Value, Exe = exe, Class = TopWindows.ClassOf(hwnd),
            WasMaximized = wasMax, WasTopmost = Win.IsTopmost(hwnd), Icon = demo?.Icon ?? (exe.Length > 0 ? ExeIcon.Get(exe) : null), DemoExe = demo?.Exe, Plan = plan,
            Title = demo?.Title ?? TopWindows.TitleOf(hwnd),
        };
        var wasForeground = Win.Foreground == hwnd;

        journal.Windows.Add(t.ToEntry()); // write-ahead: on disk before the first move
        if (exe.Length > 0) Journal.AddRescue(journal, exe);
        SaveJournal();

        Add(t);
        if (!Slide(t, plan.Parked.Location, toTop: false, syncFirst: true, done: () => { ShowTab(t); Verify(t); }, Geometry.EaseInOut))
        {
            Remove(t);
            if (wasMax) Win.Maximize(hwnd);
            Log.Warn($"tuck: window of {t.Program} did not move");
            Toast(ProcessInfo.IsElevated(pid) != false ? "Toast.Admin" : "Toast.Refused");
            return false;
        }
        Log.Info($"tucked {t.Program} to {e} of {m.Device} ({source})");
        if (source == TuckSource.Hotkey && wasForeground) ActivateNext(t);
        return true;
    }

    /// <summary>Why a window cannot be tucked: null = it can; "" = silently not (the shell, a tool window).</summary>
    static string? Ineligible(nint h)
    {
        if (!TopWindows.Passes(h))
        {
            // An owned window the base filter dropped is fine when its owner is invisible (WinForms, WPF and others hide one)
            var owner = Win.Owner(h);
            if (owner == 0 || Win.IsVisible(owner) || !Win.IsVisible(h) || Win.IsToolWindow(h)
                || TopWindows.Pid(h) == Environment.ProcessId || ShellClasses.Contains(TopWindows.ClassOf(h))) return "";
        }
        if (TopWindows.IsCloaked(h) || Win.IsIconic(h)) return "";
        if (Win.IsHung(h)) return "Toast.Hung";
        if (Win.WindowRect(h) is { } r && monitors.Count > 0 && Geometry.IsFullscreen(r, Geometry.MonitorOf(r, monitors).Bounds, Win.IsZoomed(h))) return "";
        return null;
    }

    /// <summary>150 ms after the first slide: the window must still be where we put it (UIPI, or a program that puts itself back).</summary>
    static void Verify(Tucked t)
    {
        After(150, () =>
        {
            if (!ByHwnd.ContainsKey(t.Hwnd) || t.State != TuckState.Parked || t.Anim is not null || Demo) return;
            if (Win.Frame(t.Hwnd) is not { } f) return;
            if (Near(f.Location, t.Plan.Parked.Location))
            {
                if (Win.NearestMonitor(t.Hwnd) is { } near && near != t.Plan.Monitor.Device)
                    Log.Warn($"parked {t.Program}: nearest monitor is {near}, not {t.Plan.Monitor.Device}");
                return;
            }
            if (Geometry.Distance(f, t.Plan.Parked) < 400 && !monitors.Any(m => m.Bounds.IntersectsWith(f))) return; // still on its way (a slow window)
            Log.Warn($"{t.Program} did not stay tucked");
            Forget(t);
            Toast(ProcessInfo.IsElevated(t.Pid) != false ? "Toast.Admin" : "Toast.Refused");
        });
    }

    static void Add(Tucked t)
    {
        ByHwnd[t.Hwnd] = t;
        Windows.Add(t);
        if (!Demo)
        {
            if (LocationHooks.TryGetValue(t.Pid, out var h)) LocationHooks[t.Pid] = (h.Hook, h.Refs + 1);
            else
                try { LocationHooks[t.Pid] = (WinEvents.Hook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE, OnEvent, t.Pid), 1); }
                catch (Exception e) { Log.Error("location hook", e); }
        }
        var tab = new TabWindow();
        tab.Hovered += () => { if (t.State == TuckState.Parked && HoverOpens(t)) SlideOut(t, activate: false, peek: true); };
        tab.Clicked += () => SlideOut(t, activate: true);
        tab.DpiMoved += ScheduleRelayout;
        tab.DragMoved += p => DragTab(t, p);
        tab.DragEnded += () => DropTab(t);
        tab.DragOver += () => { if (t.State == TuckState.Parked) SlideOut(t, activate: false, peek: true); }; // a file for the window: it comes out to take it
        tab.Menu = () => TabMenu(t);
        t.Tab = tab;
        RelayoutTabs();
        if (!FullscreenTimer.IsEnabled) FullscreenTimer.Start();
        Changed?.Invoke();
    }

    /// <summary>Drops a window from everything without moving it (it was closed, dragged or moved by its program).</summary>
    static void Remove(Tucked t)
    {
        if (!ByHwnd.Remove(t.Hwnd)) return;
        Windows.Remove(t);
        t.Anim = null;
        Unpeek(t);
        t.Debounce?.Stop();
        t.Tab?.Close();
        t.Tab = null;
        if (LocationHooks.TryGetValue(t.Pid, out var h))
        {
            if (h.Refs <= 1) { h.Hook.Dispose(); LocationHooks.Remove(t.Pid); }
            else LocationHooks[t.Pid] = (h.Hook, h.Refs - 1);
        }
        journal.Windows.RemoveAll(x => x.Hwnd == t.Hwnd);
        SaveJournal();
        if (Windows.Count == 0) { FullscreenTimer.Stop(); fullscreenDevice = null; }
        RelayoutTabs();
        Changed?.Invoke();
    }

    /// <summary>Untuck without a move, and a second look a second later: if it still ended up off every monitor, bring it back.</summary>
    static void Forget(Tucked t)
    {
        var entry = t.ToEntry();
        Remove(t);
        Log.Info($"forgot {t.Program} (moved by itself or by the user)");
        After(1000, () =>
        {
            if (!Win.IsWindow(t.Hwnd) || Win.IsIconic(t.Hwnd) || ByHwnd.ContainsKey(t.Hwnd) || Win.Frame(t.Hwnd) is not { } f) return;
            monitors = Monitors.Snapshot();
            if (monitors.Any(m => m.Bounds.IntersectsWith(f))) return;
            Restore.One(entry, monitors);
            Log.Warn($"{t.Program} was off screen after it left, brought back");
        });
    }

    /// <summary>Back to normal; move = put it at its shown place (clamped to a monitor that exists), maximize if it was.</summary>
    public static void Untuck(Tucked t, bool move)
    {
        t.Anim = null; // no frame of a slide may come after the return
        if (move && !Demo) Restore.One(t.ToEntry(), monitors.Count > 0 ? monitors : Monitors.Snapshot());
        else if (move && Demo) Win.MoveFrame(t.Hwnd, t.Plan.Shown.Location, async: false);
        Remove(t); // the journal entry goes only after the return (write-ahead)
        Log.Info($"untucked {t.Program}");
    }

    /// <summary>
    /// Uninstall and "Remove data": the live tucked windows back (when this process tucks), then whatever a crashed copy left in the
    /// journal (the uninstaller is its own process and never started tucking).
    /// </summary>
    public static void ReturnEverything()
    {
        if (started) RestoreAll();
        if (Demo) return;
        try
        {
            var j = Journal.Load(Journal.FilePath);
            if (j.Windows.Count == 0) return;
            var n = Restore.Entries(j.Windows);
            j.Windows.Clear();
            Journal.Save(Journal.FilePath, j);
            Log.Info($"before removal: {n} window(s) left in the journal returned");
        }
        catch (Exception e) { Log.Error("journal not restored before removal", e); }
    }

    /// <summary>Every tucked window back on screen, synchronously, no slides. Idempotent. Returns how many.</summary>
    public static int RestoreAll()
    {
        monitors = Monitors.Snapshot();
        var all = Windows.ToList();
        foreach (var t in all)
            try { Untuck(t, move: true); }
            catch (Exception e) { Log.Error("untuck failed", e); }
        // Entries without a live window object (should not happen) are still tried
        if (journal.Windows.Count > 0 && !Demo) Restore.Entries(journal.Windows.ToList());
        journal.Windows.Clear();
        SaveJournal();
        if (all.Count > 0) Log.Info($"restored all: {all.Count}");
        return all.Count;
    }

    // ---------- slides ----------

    /// <summary>
    /// Out to the shown place. activate: also give it the focus (a click, the Windows page, the tray, a rule's key) and pin it.
    /// peek: out by hover or a file over the tab, without the focus: topmost while it is out, so a maximized active window
    /// does not cover it; taken back when it slides in, is pinned or leaves.
    /// </summary>
    public static void SlideOut(Tucked t, bool activate, bool peek = false)
    {
        if (!ByHwnd.ContainsKey(t.Hwnd)) return;
        var h = t.Hwnd;
        if (Win.IsIconic(h)) { if (activate) Win.Restore(h); else Win.ShowNoActivate(h); }
        if (peek && !activate && !t.WasTopmost && !t.Peeking && !Demo) { Win.SetTopmost(h, true, Win.IsHung(h)); t.Peeking = true; }
        if (t.State == TuckState.Parked || t.Anim is not null) Slide(t, t.Plan.Shown.Location, toTop: true, syncFirst: false, done: null, Geometry.EaseOut);
        t.State = activate || t.State == TuckState.OutPinned ? TuckState.OutPinned : TuckState.Out;
        t.LeaveSince = null;
        if (t.Tab is { } tab) { tab.Hide(); tab.Flash = false; }
        if (activate && !Win.Activate(h)) Log.Info("activation refused by Windows");
        if (t.State == TuckState.OutPinned) Unpeek(t); // active now: above the others the usual way
        if (!LeaveTimer.IsEnabled) LeaveTimer.Start();
        Changed?.Invoke();
    }

    /// <summary>Out and in use: stays until the focus goes to another program.</summary>
    static void Pin(Tucked t)
    {
        t.State = TuckState.OutPinned;
        Unpeek(t);
        Changed?.Invoke();
    }

    static void Unpeek(Tucked t)
    {
        if (t.PeekDialog != 0)
        {
            if (Win.IsWindow(t.PeekDialog)) Win.SetTopmost(t.PeekDialog, false, Win.IsHung(t.PeekDialog));
            t.PeekDialog = 0;
        }
        if (!t.Peeking) return;
        t.Peeking = false;
        if (Win.IsWindow(t.Hwnd) && !t.WasTopmost) Win.SetTopmost(t.Hwnd, false, Win.IsHung(t.Hwnd));
    }

    public static void SlideIn(Tucked t)
    {
        if (!ByHwnd.ContainsKey(t.Hwnd)) return;
        t.State = TuckState.Parked;
        t.LeaveSince = null;
        if (Win.IsIconic(t.Hwnd))
        {
            // Minimized while out: it would come back at the shown place; make that the parked place
            Win.ShiftMinimized(t.Hwnd, new Size(t.Plan.Parked.X - t.Plan.Shown.X, t.Plan.Parked.Y - t.Plan.Shown.Y), Win.IsHung(t.Hwnd));
            Unpeek(t);
            ShowTab(t);
        }
        else Slide(t, t.Plan.Parked.Location, toTop: false, syncFirst: false, done: () => { Unpeek(t); ShowTab(t); }, Geometry.EaseInOut);
        Changed?.Invoke();
    }

    /// <summary>Starts a slide to `to`. syncFirst: the first frame is a synchronous SetWindowPos whose result is returned.</summary>
    static bool Slide(Tucked t, Point to, bool toTop, bool syncFirst, Action? done, Func<double, double> ease)
    {
        var h = t.Hwnd;
        var from = t.Anim?.Current ?? Win.Frame(h)?.Location ?? to;
        // A floating tab's window jumps from another edge; "Show animations in Windows" off means no slides at all
        var ms = t.Plan.Floating || !System.Windows.SystemParameters.ClientAreaAnimation ? 0 : S.AnimationMs;
        var hung = Win.IsHung(h);
        if (ms <= 0 || hung || from == to)
        {
            t.Anim = null;
            var ok = Win.MoveFrame(h, to, async: !syncFirst || hung, toTop);
            if (ok) done?.Invoke();
            return ok;
        }
        var a = new Anim(from, to, ms, toTop, done, ease);
        t.Anim = a;
        var (p, _) = a.Step();
        var first = Win.MoveFrame(h, p, async: !syncFirst, a.ToTop);
        (a.Sent, a.SentAt, a.ToTop) = (p, System.Diagnostics.Stopwatch.GetTimestamp(), false);
        if (!first) { t.Anim = null; return false; }
        if (!animating)
        {
            animating = true;
            System.Windows.Media.CompositionTarget.Rendering += AnimTick;
        }
        return true;
    }

    /// <summary>
    /// One frame per display refresh. A window that has not caught up with the last frame gets no new one for up to 50 ms
    /// (async moves would pile up in its queue and arrive in a burst); the last frame always goes.
    /// </summary>
    static void AnimTick(object? sender, EventArgs e)
    {
        var any = false;
        foreach (var t in Windows.ToList())
        {
            if (t.Anim is not { } a) continue;
            var (p, finished) = a.Step();
            var lagging = !finished && Win.Frame(t.Hwnd) is { } f && f.Location != a.Sent
                && System.Diagnostics.Stopwatch.GetElapsedTime(a.SentAt).TotalMilliseconds < 50;
            if (!lagging && p != a.Sent)
            {
                Win.MoveFrame(t.Hwnd, p, async: true, a.ToTop);
                (a.Sent, a.SentAt, a.ToTop) = (p, System.Diagnostics.Stopwatch.GetTimestamp(), false);
            }
            if (finished)
            {
                if (t.Anim == a) t.Anim = null;
                a.Done?.Invoke();
            }
            else any = true;
        }
        if (!any && animating)
        {
            animating = false;
            System.Windows.Media.CompositionTarget.Rendering -= AnimTick;
        }
    }

    /// <summary>Every 33 ms while a window is out: a click inside pins it; gone mouse for the slide-in delay slides it in.</summary>
    static void LeaveTick()
    {
        var outs = Windows.Where(t => t.State == TuckState.Out).ToList();
        if (outs.Count == 0) { LeaveTimer.Stop(); return; }
        var cursor = Win.Cursor;
        var down = Win.AnyButtonDown;
        var under = Win.TopAt(cursor);
        var underPid = TopWindows.Pid(under);
        foreach (var t in outs)
        {
            if (Win.IsIconic(t.Hwnd)) continue;
            var over = Demo ? under == t.Hwnd : underPid == t.Pid;
            if (down && over && Win.Frame(t.Hwnd) is { } f && f.Contains(cursor))
            {
                Pin(t); // a click inside the window
                continue;
            }
            if (Geometry.ShouldSlideIn(false, down, over, cursor, t.Plan.Shown, LeaveMargin))
            {
                t.LeaveSince ??= DateTime.UtcNow;
                if ((DateTime.UtcNow - t.LeaveSince.Value).TotalMilliseconds >= S.SlideInDelayMs) SlideIn(t);
            }
            else t.LeaveSince = null;
        }
    }

    static void ShowTab(Tucked t)
    {
        if (t.Tab is null || t.Hidden || t.State != TuckState.Parked || t.Plan.Monitor.Device == fullscreenDevice) return;
        t.Tab.Show();
        RelayoutTabs();
    }

    /// <summary>Tabs of each segment of each edge, laid out together (Geometry.Tabs).</summary>
    static void RelayoutTabs()
    {
        var size = TabSize();
        foreach (var t in Windows) t.ColorMayChange();
        foreach (var g in Windows.Where(t => t.Tab is not null).GroupBy(t => (t.Plan.Monitor.Device, t.Plan.Edge, t.Plan.Segment)))
        {
            var list = g.ToList();
            var first = list[0].Plan;
            var rects = Geometry.Tabs(first.Monitor, first.Edge, first.Segment,
                list.Select(t => { var a = Geometry.Along(t.Plan.Shown, t.Plan.Edge); return a.Start + a.Length / 2; }).ToList(), size);
            for (var i = 0; i < list.Count; i++)
            {
                list[i].Tab!.IconSource = S.TabIcon ? list[i].Icon : null;
                list[i].Tab!.Color = ColorFor(list[i]);
                list[i].Tab!.Place(rects[i]);
            }
        }
    }

    /// <summary>
    /// v1.1: the window's own color, else its rule's, else Settings: the icon's dominant color (readable on the theme; a colorless
    /// icon gets the accent), the Windows accent, or the custom color. Null = the accent.
    /// </summary>
    internal static System.Windows.Media.Color? ColorFor(Tucked t)
    {
        if (TabColors.Parse(t.TabColor) is { } own) return own;
        if (TabColors.Parse(S.Rules.FirstOrDefault(r => r.TabColor is not null && r.Exe.Equals(t.Program, StringComparison.OrdinalIgnoreCase))?.TabColor) is { } ruled)
            return ruled;
        return S.TabColorMode switch
        {
            "accent" => null,
            "custom" => TabColors.Parse(S.TabColor),
            _ => TabColors.OfIcon(t.Icon, Theme.IsDark),
        };
    }

    /// <summary>Thin tab from the settings, or (v1.0) a wider one with the program's icon.</summary>
    static Geometry.TabSize TabSize() => S.TabIcon
        ? new Geometry.TabSize(24, 48)
        : new Geometry.TabSize(Math.Clamp(S.TabThickness, 2, 32), Math.Clamp(S.TabLength, 16, 400));

    /// <summary>Settings changed: tabs take the new size and style.</summary>
    public static void RefreshTabs() => RelayoutTabs();

    /// <summary>v1.0: the tab follows the mouse along its segment while dragged.</summary>
    static void DragTab(Tucked t, Point cursor)
    {
        if (t.Tab is null || t.State != TuckState.Parked) return;
        t.DragAlong = Geometry.IsVertical(t.Plan.Edge) ? cursor.Y : cursor.X;
        t.Tab.Place(Geometry.Tabs(t.Plan.Monitor, t.Plan.Edge, t.Plan.Segment, [t.DragAlong.Value], TabSize())[0]);
    }

    /// <summary>v1.0: let go: the window moves along the edge to where the tab is (it may move to another free part).</summary>
    static void DropTab(Tucked t)
    {
        if (t.DragAlong is not { } along) return;
        t.DragAlong = null;
        var size = Win.IsIconic(t.Hwnd) ? t.Plan.Shown.Size : Win.Frame(t.Hwnd)?.Size ?? t.Plan.Shown.Size;
        var at = Geometry.CenteredAlong(new Rectangle(t.Plan.Shown.Location, size), t.Plan.Edge, along).Location;
        monitors = Monitors.Snapshot();
        if (Replan(t, size, at)) Log.Info($"tab of {t.Program} dragged along the edge");
        RelayoutTabs();
    }

    // ---------- events ----------

    static void OnEvent(uint ev, nint h, int idObject, int idChild)
    {
        if (idObject != 0 || idChild != 0 || h == 0) return; // OBJID_WINDOW, CHILDID_SELF only
        switch (ev)
        {
            case EVENT_SYSTEM_FOREGROUND: OnForeground(h); break;
            case EVENT_SYSTEM_MOVESIZESTART:
                if (ByHwnd.TryGetValue(h, out var dragged)) Forget(dragged); // the user takes it
                break;
            case EVENT_SYSTEM_MOVESIZEEND: OnMoveSizeEnd(h); break;
            case EVENT_OBJECT_SHOW: OnShow(h); break;
            case EVENT_OBJECT_HIDE:
                if (ByHwnd.TryGetValue(h, out var hidden)) { hidden.Hidden = true; hidden.Tab?.Hide(); }
                else if (Windows.FirstOrDefault(x => x.PeekDialog == h) is { } asked) asked.PeekDialog = 0; // the dialog closed
                break;
            case EVENT_OBJECT_DESTROY:
                if (ByHwnd.TryGetValue(h, out var gone)) { Remove(gone); Log.Info($"{gone.Program} closed while tucked"); }
                break;
            case EVENT_OBJECT_LOCATIONCHANGE:
                if (ByHwnd.TryGetValue(h, out var moved)) Debounce(moved);
                break;
        }
    }

    /// <summary>The window itself or the tucked window that owns it (a dialog).</summary>
    static Tucked? FindTucked(nint h)
    {
        for (var i = 0; i < 8 && h != 0; i++, h = Win.Owner(h))
            if (ByHwnd.TryGetValue(h, out var t)) return t;
        return null;
    }

    static void OnForeground(nint h)
    {
        var t = FindTucked(h);
        if (t is not null)
        {
            if (t.State != TuckState.OutPinned) SlideOut(t, activate: false);
            Pin(t);
            if (h != t.Hwnd) BringAlong(h, t);
        }
        var pid = TopWindows.Pid(h);
        foreach (var o in Windows.Where(x => x.State == TuckState.OutPinned && x != t && x.Pid != pid).ToList())
        {
            Log.Info($"{o.Program} slides back: the focus went to another program");
            SlideIn(o);
        }
        CheckFullscreen();
    }

    static void OnShow(nint h)
    {
        if (ByHwnd.TryGetValue(h, out var t))
        {
            t.Hidden = false;
            ShowTab(t);
            return;
        }
        var owner = Win.Owner(h);
        if (owner != 0 && FindTucked(owner) is { } o && Win.IsVisible(h) && Win.IsDialogLike(h))
        {
            // A dialog of a program in the background opens without the focus, under the active window: then the owner peeks
            // (topmost, the dialog above it) and stays pinned; the peek ends when the dialog or anything else gets the focus
            var focused = FindTucked(Win.Foreground) == o;
            Log.Info($"a dialog of {o.Program} opened while tucked ({(focused ? "focused" : "in the background")})");
            if (o.State != TuckState.OutPinned) SlideOut(o, activate: false);
            Pin(o);
            BringAlong(h, o);
            if (!focused && !Win.IsTopmost(h))
            {
                // Its program is in the background: the dialog would open under the active window. Topmost (not the owner: a window
                // with a modal dialog does not keep it) until it gets the focus, closes, or the focus goes to another program
                Win.SetTopmost(h, true, Win.IsHung(h));
                o.PeekDialog = h;
            }
            return;
        }
        if (!paused && (S.Rules.Count > 0 || journal.RescueExes.Count > 0) && TopWindows.Passes(h))
            After(1000, () => ApplyRule(h)); // a splash or an updater is usually gone by then
    }

    /// <summary>A dialog of a tucked window that opened off screen comes out together with it.</summary>
    static void BringAlong(nint dialog, Tucked t)
    {
        if (Win.Frame(dialog) is not { } f || monitors.Any(m => m.Bounds.IntersectsWith(f))) return;
        Win.MoveFrame(dialog, new Point(f.X + t.Plan.Shown.X - t.Plan.Parked.X, f.Y + t.Plan.Shown.Y - t.Plan.Parked.Y), async: true);
    }

    static void OnMoveSizeEnd(nint h)
    {
        if (paused || !S.Gesture || ByHwnd.ContainsKey(h) || Win.Frame(h) is not { } f) return;
        monitors = Monitors.Snapshot();
        if (Geometry.DragGestureEdge(monitors, f, Win.IsZoomed(h), Math.Clamp(S.GesturePercent, 10, 90) / 100.0) is { } g)
            TryTuck(h, g.Edge, g.Monitor, null, TuckSource.Gesture);
    }

    /// <summary>--autoplay: the scripted drag ends here, as a real one would.</summary>
    public static bool DemoDragEnd(nint h, DemoInfo demo)
    {
        monitors = Monitors.Snapshot();
        return Win.Frame(h) is { } f && Geometry.DragGestureEdge(monitors, f, false, S.GesturePercent / 100.0) is { } g
            && TryTuck(h, g.Edge, g.Monitor, null, TuckSource.Demo, demo);
    }

    /// <summary>--screenshot: list entries without windows behind them.</summary>
    public static void AddDemoEntry(Tucked t) => Windows.Add(t);

    /// <summary>For --autoplay poses: the tab of a tucked demo window.</summary>
    public static TabWindow? TabOf(nint h) => ByHwnd.GetValueOrDefault(h)?.Tab;
    public static Tucked? Find(nint h) => ByHwnd.GetValueOrDefault(h);

    static void Debounce(Tucked t)
    {
        if (t.Debounce is null)
        {
            t.Debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            t.Debounce.Tick += (_, _) => { t.Debounce.Stop(); ForeignCheck(t); };
        }
        t.Debounce.Stop();
        t.Debounce.Start();
    }

    /// <summary>
    /// LOCATIONCHANGE settled: our own move (expected rectangle ± 2 px) is nothing. Anything else is the program or the user:
    /// on a monitor or maximized → untuck without a move; only resized and still off screen → plan again and park. Never fight.
    /// </summary>
    static void ForeignCheck(Tucked t)
    {
        if (!ByHwnd.ContainsKey(t.Hwnd)) return;
        var h = t.Hwnd;
        if (!Win.IsWindow(h)) { Remove(t); return; }
        if (t.Anim is not null || Win.IsIconic(h)) { t.LastSeen = null; return; }
        if (Win.IsZoomed(h)) { Log.Info($"{t.Program} maximized while tucked"); Forget(t); return; }
        if (Win.Frame(h) is not { } f) return;
        var expected = t.State == TuckState.Parked ? t.Plan.Parked : t.Plan.Shown;
        if (f.Size == expected.Size && Near(f.Location, expected.Location)) { t.LastSeen = null; return; }
        if (t.LastSeen != f) { t.LastSeen = f; Debounce(t); return; } // decide only once it stands still
        t.LastSeen = null;

        var onScreen = monitors.Any(m => m.Bounds.IntersectsWith(f));
        if (f.Size == expected.Size && (onScreen || t.State != TuckState.Parked))
        {
            Log.Info($"{t.Program} was moved by its program while tucked");
            Forget(t);
            return;
        }
        if (t.State == TuckState.Parked && onScreen) { Log.Info($"{t.Program} moved itself onto a monitor"); Forget(t); return; }

        if (DateTime.UtcNow - t.FightsSince > TimeSpan.FromMinutes(1)) { t.FightsSince = DateTime.UtcNow; t.Fights = 0; }
        if (++t.Fights > 3)
        {
            Log.Warn($"{t.Program} keeps moving itself, untucked");
            Untuck(t, move: true);
            Toast("Toast.Fights");
            return;
        }
        if (!Replan(t, f.Size)) Toast("Toast.NoLongerFits");
    }

    /// <summary>Same edge and place along it, new size or new monitors: plan again; no longer fits → back on screen.</summary>
    static bool Replan(Tucked t, Size size, Point? at = null)
    {
        var m = monitors.FirstOrDefault(d => d.Device == t.Plan.Monitor.Device);
        var r = m is null ? default : Geometry.Plan(monitors, m, t.Plan.Edge, new Rectangle(at ?? t.Plan.Shown.Location, size), S.SharedEdges);
        if (r.Plan is not { } plan)
        {
            Log.Info($"{t.Program} no longer fits on {t.Plan.Edge} of {t.Plan.Monitor.Device}: {(m is null ? "monitor gone" : r.Fail)}");
            Untuck(t, move: true);
            return false;
        }
        if (plan != t.Plan)
        {
            t.Plan = plan;
            var i = journal.Windows.FindIndex(x => x.Hwnd == t.Hwnd);
            if (i >= 0) journal.Windows[i] = t.ToEntry();
            SaveJournal();
        }
        if (!Win.IsIconic(t.Hwnd)) Win.MoveFrame(t.Hwnd, (t.State == TuckState.Parked ? plan.Parked : plan.Shown).Location, async: true);
        RelayoutTabs();
        return true;
    }

    // ---------- fullscreen, monitors ----------

    static void CheckFullscreen()
    {
        if (Windows.Count == 0) { FullscreenTimer.Stop(); return; }
        // v1.0: the tab stays on its window's desktop. A snapshot: a COM call pumps messages, and a window may close meanwhile
        foreach (var t in Windows.ToList()) if (ByHwnd.ContainsKey(t.Hwnd) && t.Tab is { } tab) VirtualDesktops.Follow(tab.Handle, t.Hwnd);
        string? device = null;
        var fg = Win.Foreground;
        if (S.HideTabsInFullscreen && fg != 0 && !ByHwnd.ContainsKey(fg) && TopWindows.Pid(fg) != Environment.ProcessId
            && !ShellClasses.Contains(TopWindows.ClassOf(fg)) && Win.WindowRect(fg) is { } r && monitors.Count > 0)
        {
            var m = Geometry.MonitorOf(r, monitors);
            if (Geometry.IsFullscreen(r, m.Bounds, Win.IsZoomed(fg)) || UserBusy.Current == UserBusy.State.D3DFullScreen) device = m.Device;
        }
        if (device == fullscreenDevice) return;
        fullscreenDevice = device;
        Log.Info(device is null ? "fullscreen ended" : $"fullscreen on {device}");
        foreach (var t in Windows.ToList())
            if (t.Plan.Monitor.Device == device)
            {
                t.Tab?.Hide();
                if (t.State != TuckState.Parked) SlideIn(t);
            }
            else ShowTab(t);
    }

    static nint OnMessage(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        // v1.0: a tucked window flashes (a new message): light its tab up
        if (shellHookMessage != 0 && msg == shellHookMessage && (int)wParam == HshellFlash && ByHwnd.TryGetValue(lParam, out var flashing)
            && flashing.State == TuckState.Parked && flashing.Tab is { } tab)
            tab.Flash = true;
        if (msg == MessageWindow.WM_SETTINGCHANGE && Windows.Count > 0)
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(RelayoutTabs); // light or dark: icon colors are made readable again
        if (msg == MessageWindow.WM_DISPLAYCHANGE
            || (msg == MessageWindow.WM_SETTINGCHANGE && wParam == SpiSetWorkArea)
            || (msg == MessageWindow.WM_POWERBROADCAST && wParam == PbtResumeAutomatic))
            ScheduleRelayout();
        return 0;
    }

    static void ScheduleRelayout()
    {
        DisplayDebounce.Stop();
        DisplayDebounce.Start();
    }

    /// <summary>Monitors, resolution, orientation, DPI or work area changed, or the PC woke up: plan every window again.</summary>
    static void Relayout()
    {
        monitors = Monitors.Snapshot();
        Log.Info($"relayout: {monitors.Count} monitor(s)");
        var returned = 0;
        foreach (var t in Windows.ToList())
        {
            if (!Win.IsWindow(t.Hwnd)) { Remove(t); continue; }
            var size = Win.IsIconic(t.Hwnd) ? t.Plan.Shown.Size : Win.Frame(t.Hwnd)?.Size ?? t.Plan.Shown.Size;
            if (!Replan(t, size)) returned++;
        }
        RelayoutTabs();
        if (returned > 0) Toast("Toast.MonitorsChanged", returned);
    }

    // ---------- rules ----------

    /// <summary>A new top-level window: tuck it by its rule, or bring it back if a known program opened it off every monitor.</summary>
    static void ApplyRule(nint h)
    {
        if (paused || Demo || ByHwnd.ContainsKey(h) || !Win.IsWindow(h) || !Win.IsVisible(h)) return;
        if (S.Rules.Count == 0 && journal.RescueExes.Count == 0) return;
        if (Ineligible(h) is not null) return;
        var exe = ProcessInfo.ExePath(TopWindows.Pid(h));
        if (exe is null || Win.Frame(h) is not { } f) return;
        var rule = Rules.Match(S.Rules, exe, TopWindows.TitleOf(h), f.Size);
        if (rule is null && !journal.RescueExes.Contains(Path.GetFileName(exe), StringComparer.OrdinalIgnoreCase)) return;
        monitors = Monitors.Snapshot();
        if (rule is { AutoTuck: true })
        {
            var (m, at) = Rules.Target(rule, f, monitors);
            if (TryTuck(h, rule.Edge, m, at, TuckSource.Rule)) return;
        }
        if (monitors.Any(m => m.Bounds.IntersectsWith(f))) return;
        var target = Monitors.ClampToVisible(f, monitors);
        Win.MoveFrame(h, target.Location, async: Win.IsHung(h));
        Log.Info($"rescued a window of {Path.GetFileName(exe)} that opened off screen");
    }

    public static void AddRule(Tucked t)
    {
        if (t.Exe.Length == 0 && t.DemoExe is null) return;
        var rules = S.Rules;
        rules.RemoveAll(r => r.Exe.Equals(t.Program, StringComparison.OrdinalIgnoreCase));
        var rule = Rules.From(t.DemoExe ?? t.Exe, t.Plan);
        var first = rules.Count == 0;
        rules.Add(rule);
        Settings.Save();
        RegisterRuleHotkeys();
        Log.Info($"rule added for {rule.Exe}");
        if (first && !Autostart.IsEnabled) ToastPage("Toast.FirstRule", "settings");
        Changed?.Invoke();
    }

    public static void RemoveRule(string exe)
    {
        S.Rules.RemoveAll(r => r.Exe.Equals(exe, StringComparison.OrdinalIgnoreCase));
        Settings.Save();
        RegisterRuleHotkeys();
        Changed?.Invoke();
    }

    /// <summary>v1.0: a template from the Rules page, off until the user turns it on; an existing rule for that program stays.</summary>
    public static void AddTemplate(Rule template)
    {
        if (S.Rules.Any(r => r.Exe.Equals(template.Exe, StringComparison.OrdinalIgnoreCase))) return;
        S.Rules.Add(template);
        Settings.Save();
        Changed?.Invoke();
    }

    static ContextMenu TabMenu(Tucked t)
    {
        var menu = new ContextMenu();
        menu.Items.Add(Tray.Item(Loc.T("Tab.Return"), () => Untuck(t, move: true)));
        if (t.Program.Length > 0)
        {
            var has = S.Rules.Any(r => r.Exe.Equals(t.Program, StringComparison.OrdinalIgnoreCase));
            menu.Items.Add(has
                ? Tray.Item(string.Format(Loc.T("Tab.ForgetRule"), t.Program), () => RemoveRule(t.Program))
                : Tray.Item(string.Format(Loc.T("Tab.AlwaysHere"), t.Program), () => AddRule(t)));
        }
        return menu;
    }

    // ---------- helpers ----------

    /// <summary>After a hotkey tuck the keyboard should not stay in a window nobody sees: activate the next one in Z order.</summary>
    static void ActivateNext(Tucked t)
    {
        foreach (var w in TopWindows.List())
        {
            if (w.Hwnd == t.Hwnd || ByHwnd.ContainsKey(w.Hwnd) || w.Cloaked || Win.IsIconic(w.Hwnd)) continue;
            if (Win.Frame(w.Hwnd) is { } f && monitors.Any(m => m.Bounds.IntersectsWith(f))) { Win.Activate(w.Hwnd); return; }
        }
        Win.Activate(Win.Shell);
    }

    /// <summary>Hover opens a tab unless click-only is on; a floating tab on a shared edge only if allowed (the mouse crosses the seam all the time).</summary>
    static bool HoverOpens(Tucked t) => S.SlideOutOnHover && (!t.Plan.Floating || S.HoverOnSharedEdges);

    static bool Near(Point a, Point b) => Math.Abs(a.X - b.X) <= 2 && Math.Abs(a.Y - b.Y) <= 2;

    static void After(int ms, Action a)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_, _) => { timer.Stop(); a(); };
        timer.Start();
    }

    static void Toast(string key, params object[] args) => ToastPage(key, null, args);

    static void ToastPage(string key, string? page, params object[] args)
    {
        if (exiting) return;
        Tray.Notify(AppInfo.DisplayName, args.Length > 0 ? string.Format(Loc.T(key), args) : Loc.T(key), page);
    }

    /// <summary>Peek from the tray or the Windows page: out and active.</summary>
    public static void Show(Tucked t) => SlideOut(t, activate: true);
}
