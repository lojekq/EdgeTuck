using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace EdgeTuck.Features;

/// <summary>
/// The thin accent tab on the edge, one per tucked window: topmost, never activated, not in Alt+Tab or the taskbar.
/// Placed in physical px with SetWindowPos (Geometry.Tabs), not with WPF Left/Top.
/// v1.0: an icon style, dragging along the edge, a file dragged over it, a flash when its window wants attention.
/// </summary>
public sealed class TabWindow : Window
{
    const int WM_MOUSEACTIVATE = 0x0021, MA_NOACTIVATE = 3, WM_DPICHANGED = 0x02E0;
    const double Idle = 0.7;

    readonly Border bar = new() { Opacity = Idle };
    readonly Image icon = new() { Width = 16, Height = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    readonly DispatcherTimer hover = new();
    readonly DispatcherTimer flash = new() { Interval = TimeSpan.FromMilliseconds(500) };
    System.Drawing.Rectangle placed;
    Color? color;
    Point? pressedAt;
    bool dragging;

    public nint Handle { get; private set; }

    /// <summary>The mouse rested on the tab for the slide-out delay with no button down.</summary>
    public event Action? Hovered;
    public event Action? Clicked;
    /// <summary>The tab moved to a monitor with another DPI or the DPI changed: relayout.</summary>
    public event Action? DpiMoved;
    /// <summary>Dragged along the edge (the cursor in physical px), then let go.</summary>
    public event Action<System.Drawing.Point>? DragMoved;
    public event Action? DragEnded;
    /// <summary>Something (a file) is dragged over the tab.</summary>
    public event Action? DragOver;
    /// <summary>Right click: the menu is built at that moment (rules change).</summary>
    public Func<ContextMenu>? Menu;

    public TabWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        AllowDrop = true;
        Width = 6; Height = 64; Left = -32000; Top = -32000; // real place comes from Place()
        bar.SetResourceReference(Border.BackgroundProperty, "SystemAccentBrush");
        Content = new Grid { Children = { bar, icon } };

        SourceInitialized += (_, _) =>
        {
            Handle = new WindowInteropHelper(this).Handle;
            Win.MakeTab(Handle);
            HwndSource.FromHwnd(Handle).AddHook(WndProc);
            if (!placed.IsEmpty) Win.PlaceTab(Handle, placed);
        };
        hover.Tick += (_, _) =>
        {
            hover.Stop();
            if (IsMouseOver && !Win.AnyButtonDown) Hovered?.Invoke();
        };
        flash.Tick += (_, _) => bar.Opacity = bar.Opacity > 0.8 ? 0.35 : 1;
        MouseEnter += (_, _) =>
        {
            if (!flash.IsEnabled) bar.Opacity = 1;
            hover.Interval = TimeSpan.FromMilliseconds(Math.Max(1, Shell.Settings.Current.EdgeTuck.SlideOutDelayMs));
            hover.Start();
        };
        MouseLeave += (_, _) => { if (!flash.IsEnabled) bar.Opacity = Idle; hover.Stop(); };
        MouseLeftButtonDown += (_, e) => { pressedAt = e.GetPosition(this); dragging = false; CaptureMouse(); hover.Stop(); };
        MouseMove += (_, e) =>
        {
            if (pressedAt is not { } p || e.LeftButton != MouseButtonState.Pressed) return;
            var now = e.GetPosition(this);
            if (!dragging && Math.Abs(now.X - p.X) + Math.Abs(now.Y - p.Y) < 6) return;
            dragging = true;
            DragMoved?.Invoke(Win.Cursor);
        };
        MouseLeftButtonUp += (_, _) =>
        {
            ReleaseMouseCapture();
            var dragged = dragging;
            pressedAt = null;
            dragging = false;
            if (dragged) DragEnded?.Invoke();
            else Clicked?.Invoke();
        };
        MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            hover.Stop();
            if (Menu?.Invoke() is not { } menu) return;
            menu.PlacementTarget = this;
            menu.IsOpen = true;
        };
        DragEnter += (_, e) => { e.Effects = DragDropEffects.None; DragOver?.Invoke(); };
    }

    /// <summary>Hover look without the mouse, for --demo screenshots.</summary>
    public bool Lit { set => bar.Opacity = value ? 1 : Idle; }

    /// <summary>v1.0: its window wants attention (HSHELL_FLASH) until it is looked at.</summary>
    public bool Flash
    {
        set
        {
            if (value == flash.IsEnabled) return;
            if (value) { bar.SetResourceReference(Border.BackgroundProperty, "SystemFillColorCautionBrush"); flash.Start(); }
            else { flash.Stop(); Paint(); bar.Opacity = IsMouseOver ? 1 : Idle; }
        }
    }

    /// <summary>v1.1: the tab's color; null = the Windows accent (follows it).</summary>
    public Color? Color
    {
        set
        {
            if (color == value) return;
            color = value;
            if (!flash.IsEnabled) Paint();
        }
    }

    void Paint()
    {
        if (color is { } c) bar.Background = new SolidColorBrush(c);
        else bar.SetResourceReference(Border.BackgroundProperty, "SystemAccentBrush");
    }

    /// <summary>v1.0: the program's icon on a wide tab; null for the thin one.</summary>
    public ImageSource? IconSource { set => icon.Source = value; }

    public void Place(System.Drawing.Rectangle r)
    {
        placed = r;
        if (Handle != 0) Win.PlaceTab(Handle, r);
    }

    nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_MOUSEACTIVATE:
                handled = true;
                return MA_NOACTIVATE;
            case WM_DPICHANGED:
                handled = true; // our own size comes from Geometry.Tabs, not from WPF's suggested rectangle
                Dispatcher.BeginInvoke(() => DpiMoved?.Invoke());
                return 0;
        }
        return 0;
    }
}
