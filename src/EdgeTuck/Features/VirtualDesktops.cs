using System.Runtime.InteropServices;
using EdgeTuck.Shell;

namespace EdgeTuck.Features;

/// <summary>
/// IVirtualDesktopManager (documented since Windows 10): which desktop a window is on, and moving our own tab there.
/// Only our windows are moved; other programs' windows can't be (plan 04 §6). Gone or failing: tabs simply stay where they are.
/// </summary>
public static class VirtualDesktops
{
    [ComImport, Guid("a5cd92ff-29be-454c-8d04-d82879fb3f1b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IVirtualDesktopManager
    {
        [PreserveSig] int IsWindowOnCurrentVirtualDesktop(nint topLevelWindow, out int onCurrentDesktop);
        [PreserveSig] int GetWindowDesktopId(nint topLevelWindow, out Guid desktopId);
        [PreserveSig] int MoveWindowToDesktop(nint topLevelWindow, ref Guid desktopId);
    }

    static readonly IVirtualDesktopManager? Manager = Create();

    static IVirtualDesktopManager? Create()
    {
        try
        {
            return Type.GetTypeFromCLSID(new Guid("aa509086-5ca9-4c25-8f95-589d3c07b48a")) is { } t
                ? Activator.CreateInstance(t) as IVirtualDesktopManager
                : null;
        }
        catch (Exception e) { Log.Warn("virtual desktops unavailable", e); return null; }
    }

    public static Guid? Of(nint hwnd)
    {
        try { return Manager is not null && Manager.GetWindowDesktopId(hwnd, out var id) == 0 && id != Guid.Empty ? id : null; }
        catch { return null; }
    }

    /// <summary>Our tab onto the desktop of the window it belongs to.</summary>
    public static void Follow(nint tab, nint window)
    {
        if (Manager is null || tab == 0 || Of(window) is not { } id || Of(tab) == id) return;
        try { Manager.MoveWindowToDesktop(tab, ref id); }
        catch { } // a hidden tab is not on any desktop yet: next time
    }
}
