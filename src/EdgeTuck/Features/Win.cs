using System.Drawing;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;
using EdgeTuck.Shell;

namespace EdgeTuck.Features;

/// <summary>
/// The only things Edge Tuck does to other windows (plan 04 §8): position and z-order (SetWindowPos with SWP_NOSIZE)
/// and the show state (restore, maximize, show without activating). No styles, no SW_HIDE, no topmost, never closes.
/// Rectangles are frames (DWMWA_EXTENDED_FRAME_BOUNDS, no invisible borders); moves add the border offset themselves.
/// </summary>
public static unsafe class Win
{
    public static bool IsWindow(nint h) => PInvoke.IsWindow((HWND)h);
    public static bool IsVisible(nint h) => PInvoke.IsWindowVisible((HWND)h);
    public static bool IsZoomed(nint h) => PInvoke.IsZoomed((HWND)h);
    public static bool IsIconic(nint h) => PInvoke.IsIconic((HWND)h);
    public static bool IsHung(nint h) => PInvoke.IsHungAppWindow((HWND)h);
    public static nint Foreground => PInvoke.GetForegroundWindow();
    public static nint Shell => PInvoke.GetShellWindow();
    public static nint Owner(nint h) => PInvoke.GetWindow((HWND)h, GET_WINDOW_CMD.GW_OWNER);
    public static nint Root(nint h) => PInvoke.GetAncestor((HWND)h, GET_ANCESTOR_FLAGS.GA_ROOT);
    public static nint RootOwner(nint h) => PInvoke.GetAncestor((HWND)h, GET_ANCESTOR_FLAGS.GA_ROOTOWNER);

    public static Rectangle? WindowRect(nint h) =>
        PInvoke.GetWindowRect((HWND)h, out var r) ? Rectangle.FromLTRB(r.left, r.top, r.right, r.bottom) : null;

    /// <summary>The visible frame; GetWindowRect when DWM has none (some old windows).</summary>
    public static Rectangle? Frame(nint h)
    {
        RECT r;
        if (PInvoke.DwmGetWindowAttribute((HWND)h, DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS, &r, (uint)sizeof(RECT)).Succeeded && r.right > r.left)
            return Rectangle.FromLTRB(r.left, r.top, r.right, r.bottom);
        return WindowRect(h);
    }

    /// <summary>
    /// Moves the window so its frame's top-left is at p. async = SWP_ASYNCWINDOWPOS (never blocks on a hung window);
    /// toTop = HWND_TOP (above other normal windows, never topmost). False when Windows refused (UIPI) or the window is gone.
    /// </summary>
    public static bool MoveFrame(nint h, Point p, bool async, bool toTop = false)
    {
        if (WindowRect(h) is not { } w || Frame(h) is not { } f) return false;
        var flags = SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOOWNERZORDER;
        if (async) flags |= SET_WINDOW_POS_FLAGS.SWP_ASYNCWINDOWPOS;
        if (!toTop) flags |= SET_WINDOW_POS_FLAGS.SWP_NOZORDER;
        return PInvoke.SetWindowPos((HWND)h, default /* HWND_TOP; ignored with SWP_NOZORDER */, p.X + w.Left - f.Left, p.Y + w.Top - f.Top, 0, 0, flags);
    }

    public static void ShowNoActivate(nint h) => PInvoke.ShowWindowAsync((HWND)h, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);
    public static void Restore(nint h) => PInvoke.ShowWindowAsync((HWND)h, SHOW_WINDOW_CMD.SW_RESTORE);
    public static void Maximize(nint h) => PInvoke.ShowWindowAsync((HWND)h, SHOW_WINDOW_CMD.SW_MAXIMIZE);

    /// <summary>Allowed after our own input (a click on a tab, WM_HOTKEY); checks the result.</summary>
    public static bool Activate(nint h)
    {
        if (IsIconic(h)) Restore(h);
        return PInvoke.SetForegroundWindow((HWND)h) && Foreground == h;
    }

    /// <summary>
    /// A minimized window comes back to its "normal" rectangle, which SetWindowPos does not touch:
    /// shift that rectangle by delta (WINDOWPLACEMENT, workspace coordinates; a delta is the same in both), staying minimized.
    /// </summary>
    public static bool ShiftMinimized(nint h, Size delta, bool async)
    {
        var wp = new WINDOWPLACEMENT { length = (uint)sizeof(WINDOWPLACEMENT) };
        if (!PInvoke.GetWindowPlacement((HWND)h, ref wp)) return false;
        wp.rcNormalPosition.left += delta.Width; wp.rcNormalPosition.right += delta.Width;
        wp.rcNormalPosition.top += delta.Height; wp.rcNormalPosition.bottom += delta.Height;
        wp.showCmd = SHOW_WINDOW_CMD.SW_SHOWMINNOACTIVE;
        if (async) wp.flags |= WINDOWPLACEMENT_FLAGS.WPF_ASYNCWINDOWPLACEMENT;
        return PInvoke.SetWindowPlacement((HWND)h, in wp);
    }

    /// <summary>The normal (restored) rectangle of a minimized window in screen coordinates, workspace offset of its monitor removed.</summary>
    public static Rectangle? NormalRect(nint h, IReadOnlyList<Display> monitors)
    {
        var wp = new WINDOWPLACEMENT { length = (uint)sizeof(WINDOWPLACEMENT) };
        if (!PInvoke.GetWindowPlacement((HWND)h, ref wp)) return null;
        var n = wp.rcNormalPosition;
        var r = Rectangle.FromLTRB(n.left, n.top, n.right, n.bottom);
        if (monitors.Count == 0) return r;
        var m = Geometry.MonitorOf(r, monitors); // ponytail: workspace offset from the monitor the workspace rect is on; differs only with a taskbar on top or left
        r.Offset(m.Work.Left - m.Bounds.Left, m.Work.Top - m.Bounds.Top);
        return r;
    }

    /// <summary>Safety net for the parked invariant: Windows itself says the rectangle is on no monitor.</summary>
    public static bool OnNoMonitor(Rectangle r)
    {
        var rc = new RECT { left = r.Left, top = r.Top, right = r.Right, bottom = r.Bottom };
        return PInvoke.MonitorFromRect(&rc, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONULL) == HMONITOR.Null;
    }

    /// <summary>Device name of the monitor Windows considers nearest to the window (its DPI follows it).</summary>
    public static string? NearestMonitor(nint h)
    {
        var m = PInvoke.MonitorFromWindow((HWND)h, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFOEXW { monitorInfo = { cbSize = (uint)sizeof(MONITORINFOEXW) } };
        return PInvoke.GetMonitorInfo(m, (MONITORINFO*)&info) ? info.szDevice.ToString() : null;
    }

    public static Point Cursor => PInvoke.GetCursorPos(out var p) ? new(p.X, p.Y) : default;

    /// <summary>Only --autoplay moves the cursor (00-shared §2.3).</summary>
    public static void SetCursor(Point p) => PInvoke.SetCursorPos(p.X, p.Y);

    /// <summary>Physical buttons, polled; no mouse hook.</summary>
    public static bool AnyButtonDown =>
        Down(0x01) || Down(0x02) || Down(0x04); // VK_LBUTTON, VK_RBUTTON, VK_MBUTTON

    static bool Down(int vk) => (PInvoke.GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>The top-level window under a point (root of whatever child is there).</summary>
    public static nint TopAt(Point p) => Root(PInvoke.WindowFromPoint(new System.Drawing.Point(p.X, p.Y)));

    /// <summary>WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW for our tabs: never activated, not in Alt+Tab.</summary>
    public static void MakeTab(nint h)
    {
        var ex = (WINDOW_EX_STYLE)PInvoke.GetWindowLong((HWND)h, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        ex |= WINDOW_EX_STYLE.WS_EX_NOACTIVATE | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW;
        PInvoke.SetWindowLong((HWND)h, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, (int)ex);
    }

    /// <summary>Our own tab window: topmost, physical px, never activated.</summary>
    public static void PlaceTab(nint h, Rectangle r) =>
        PInvoke.SetWindowPos((HWND)h, new HWND(-1) /* HWND_TOPMOST */, r.X, r.Y, r.Width, r.Height,
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOOWNERZORDER);
}
