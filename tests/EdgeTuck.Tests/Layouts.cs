using System.Drawing;
using EdgeTuck.Shell;

namespace EdgeTuck.Tests;

/// <summary>Monitor layouts for geometry tests, physical px. Owner = the owner's PC (plan 04-edge-tuck §1); apps add their own rows here.</summary>
static class Layouts
{
    public static readonly Display Display1 = new(@"\\.\DISPLAY1", Rectangle.FromLTRB(0, 0, 3440, 1440), Rectangle.FromLTRB(0, 0, 3440, 1408), 96);
    public static readonly Display Display2 = new(@"\\.\DISPLAY2", Rectangle.FromLTRB(3440, 0, 4880, 2560), Rectangle.FromLTRB(3440, 0, 4880, 2560), 96); // portrait, no taskbar

    public static readonly Display[] Owner = [Display1, Display2];

    // Edge Tuck, plan 04 §9: one table for all Geometry tests
    public static readonly Display[] Owner125 = [Display1, Display2 with { Dpi = 120 }];
    public static readonly Display[] Single = [D(1, 0, 0, 1920, 1080)];
    public static readonly Display[] SideTopAligned = [D(1, 0, 0, 1920, 1080), D(2, 1920, 0, 4480, 1440)];
    public static readonly Display[] SideBottomAligned = [D(1, 0, 360, 1920, 1440), D(2, 1920, 0, 4480, 1440)];
    public static readonly Display[] StackOffset = [D(1, 0, 0, 1920, 1080), D(2, 480, -1440, 3040, 0)];
    public static readonly Display[] PrimaryRight = [D(1, 0, 0, 1920, 1080), D(2, -2560, -200, 0, 1240)];
    public static readonly Display[] LShape = [D(1, 0, 0, 1920, 1080), D(2, 1920, 0, 3360, 2560), D(3, 0, 1080, 1000, 2160)];
    public static readonly Display[] MixedDpi =
        [new(@"\\.\DISPLAY1", Rectangle.FromLTRB(0, 0, 3840, 2160), Rectangle.FromLTRB(0, 0, 3840, 2088), 144), D(2, 3840, 0, 5760, 1080)];

    public static readonly (string Name, Display[] Monitors)[] All =
    [
        ("Owner", Owner), ("Owner125", Owner125), ("Single", Single), ("SideTopAligned", SideTopAligned),
        ("SideBottomAligned", SideBottomAligned), ("StackOffset", StackOffset), ("PrimaryRight", PrimaryRight),
        ("LShape", LShape), ("MixedDpi", MixedDpi),
    ];

    static Display D(int n, int l, int t, int r, int b) => new($@"\\.\DISPLAY{n}", Rectangle.FromLTRB(l, t, r, b), Rectangle.FromLTRB(l, t, r, b), 96);
}
