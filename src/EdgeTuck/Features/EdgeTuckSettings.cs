using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using EdgeTuck.Features;
using EdgeTuck.Shell;

namespace EdgeTuck.Shell
{
    public sealed partial class Settings
    {
        EdgeTuckSettings edgeTuck = new();

        /// <summary>A hand-edited "edgeTuck": null means the defaults, not a crash.</summary>
        public EdgeTuckSettings EdgeTuck { get => edgeTuck; set => edgeTuck = value ?? new(); }
    }
}

namespace EdgeTuck.Features
{
    /// <summary>The "edgeTuck" section of settings.json (plan 04 §5, §7).</summary>
    public sealed class EdgeTuckSettings
    {
        // Only combinations from 00-shared §9.5
        public string? HotkeyLeft { get; set; } = "Ctrl+Win+Alt+Left";
        public string? HotkeyRight { get; set; } = "Ctrl+Win+Alt+Right";
        public string? HotkeyTop { get; set; } = "Ctrl+Win+Alt+Up";
        public string? HotkeyBottom { get; set; } = "Ctrl+Win+Alt+Down";
        public string? HotkeyRestoreAll { get; set; } = "Ctrl+Win+Alt+Home";

        public int SlideOutDelayMs { get; set; } = 200;
        public int SlideInDelayMs { get; set; } = 400;
        public int AnimationMs { get; set; } = 150;   // 0 = instant
        public int TabThickness { get; set; } = 6;    // px (DIP before 1.1.1)
        public int TabLength { get; set; } = 64;      // px
        public bool Gesture { get; set; } = true;
        public int GesturePercent { get; set; } = 50;
        public bool HideTabsInFullscreen { get; set; } = true;
        // v1.0
        public bool SlideOutOnHover { get; set; } = true;      // false = click only
        public bool SharedEdges { get; set; } = true;          // a floating tab on an edge shared with another monitor
        public bool HoverOnSharedEdges { get; set; }           // the mouse crosses the seam all the time: click only by default
        public bool TabIcon { get; set; }                      // a wider tab with the program's icon
        // v1.1
        public string TabColorMode { get; set; } = "icon";     // icon | accent | custom
        public string TabColor { get; set; } = "#0A84FF";      // the custom color
        List<Rule> rules = [];

        /// <summary>"rules": null in a hand-edited file means no rules (Edge Tuck 1.0.0 crashed on it at start).</summary>
        public List<Rule> Rules { get => rules; set => rules = value?.Where(r => r is not null).ToList() ?? []; }

        public string? Hotkey(Edge e) => e switch
        {
            Edge.Left => HotkeyLeft, Edge.Right => HotkeyRight, Edge.Top => HotkeyTop, _ => HotkeyBottom,
        };

        public void SetHotkey(Edge e, string? value)
        {
            switch (e)
            {
                case Edge.Left: HotkeyLeft = value; break;
                case Edge.Right: HotkeyRight = value; break;
                case Edge.Top: HotkeyTop = value; break;
                default: HotkeyBottom = value; break;
            }
        }
    }

    /// <summary>"Always tuck this program here": by exe file name; Along is the fraction of the whole edge at the window's center.</summary>
    public sealed class Rule : INotifyPropertyChanged
    {
        string exe = "";
        bool autoTuck = true, enabled = true;
        string? titleContains, hotkey, tabColor;
        int minWidth, minHeight;
        int? tabThickness, tabLength;

        public string Exe { get => exe; set { exe = value; Changed(); } }
        public Edge Edge { get; set; }
        public string? Monitor { get; set; }
        public double Along { get; set; } = 0.5;
        public bool AutoTuck { get => autoTuck; set { autoTuck = value; Changed(); } }
        public bool Enabled { get => enabled; set { enabled = value; Changed(); } }
        // v1.0: catch only the right window (not a splash or an updater) and show or hide it with a key
        public string? TitleContains { get => titleContains; set { titleContains = string.IsNullOrWhiteSpace(value) ? null : value; Changed(); } }
        public int MinWidth { get => minWidth; set { minWidth = Math.Max(0, value); Changed(); } }
        public int MinHeight { get => minHeight; set { minHeight = Math.Max(0, value); Changed(); } }
        public string? Hotkey { get => hotkey; set { hotkey = value; Changed(); } }
        /// <summary>v1.1: this program's tabs in this color ("#RRGGBB"); null = as in Settings.</summary>
        public string? TabColor { get => tabColor; set { tabColor = value; Changed(); Changed(nameof(TabBrush)); } }
        [JsonIgnore] public System.Windows.Media.Brush? TabBrush => TabColors.Parse(TabColor) is { } c ? new System.Windows.Media.SolidColorBrush(c) : null;
        /// <summary>v1.1.1: this program's tab size in px; null = as in Settings.</summary>
        public int? TabThickness { get => tabThickness; set { tabThickness = value; Changed(); } }
        public int? TabLength { get => tabLength; set { tabLength = value; Changed(); } }

        [JsonIgnore] public string Detail => $"{Loc.T("Edge." + Edge)} · {(Monitor is null ? "" : Tucked.MonitorName(Monitor))}";

        public event PropertyChangedEventHandler? PropertyChanged;
        void Changed([CallerMemberName] string? p = null) => PropertyChanged?.Invoke(this, new(p));
    }
}
