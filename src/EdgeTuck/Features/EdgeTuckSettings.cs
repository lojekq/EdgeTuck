using System.ComponentModel;
using System.Runtime.CompilerServices;
using EdgeTuck.Features;

namespace EdgeTuck.Shell
{
    public sealed partial class Settings
    {
        public EdgeTuckSettings EdgeTuck { get; set; } = new();
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
        public int TabThickness { get; set; } = 6;    // DIP
        public int TabLength { get; set; } = 64;      // DIP
        public bool Gesture { get; set; } = true;
        public int GesturePercent { get; set; } = 50;
        public bool HideTabsInFullscreen { get; set; } = true;
        public List<Rule> Rules { get; set; } = [];

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

        public string Exe { get => exe; set { exe = value; Changed(); } }
        public Edge Edge { get; set; }
        public string? Monitor { get; set; }
        public double Along { get; set; } = 0.5;
        public bool AutoTuck { get => autoTuck; set { autoTuck = value; Changed(); } }
        public bool Enabled { get => enabled; set { enabled = value; Changed(); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        void Changed([CallerMemberName] string? p = null) => PropertyChanged?.Invoke(this, new(p));
    }
}
