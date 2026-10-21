using System.Drawing;
using EdgeTuck.Features;
using EdgeTuck.Shell;

namespace EdgeTuck.Tests;

[TestClass]
public class GeometryTests
{
    static string Free(Display[] layout, int monitor, Edge e, int windowAcross = 500) =>
        string.Join(", ", Geometry.FreeSegments(layout, layout[monitor], e, Geometry.ParkGap + windowAcross) is { Count: > 0 } s
            ? s.Select(x => $"{x.Start}–{x.End}") : ["—"]);

    [TestMethod]
    public void OwnerHasSevenFreeEdges()
    {
        var o = Layouts.Owner;
        Assert.AreEqual("0–1408", Free(o, 0, Edge.Left));
        Assert.AreEqual("0–3360", Free(o, 0, Edge.Top));
        Assert.AreEqual("—", Free(o, 0, Edge.Right));
        Assert.AreEqual("0–3360", Free(o, 0, Edge.Bottom));
        Assert.AreEqual("1520–2560", Free(o, 1, Edge.Left));
        Assert.AreEqual("3520–4880", Free(o, 1, Edge.Top));
        Assert.AreEqual("0–2560", Free(o, 1, Edge.Right));
        Assert.AreEqual("3440–4880", Free(o, 1, Edge.Bottom));
        foreach (var e in Geometry.Edges)
            for (var i = 0; i < 2; i++)
                Assert.AreEqual(Free(o, i, e), Free(Layouts.Owner125, i, e), "125 % on DISPLAY2 changes no segment");
    }

    [TestMethod]
    public void SegmentsOfOtherLayouts()
    {
        foreach (var e in Geometry.Edges)
            Assert.AreEqual(Geometry.IsVertical(e) ? "0–1080" : "0–1920", Free(Layouts.Single, 0, e));

        Assert.AreEqual("1160–1440", Free(Layouts.SideTopAligned, 1, Edge.Left));
        Assert.AreEqual("—", Free(Layouts.SideTopAligned, 0, Edge.Right));
        Assert.AreEqual("0–1840", Free(Layouts.SideTopAligned, 0, Edge.Bottom));

        Assert.AreEqual("0–280", Free(Layouts.SideBottomAligned, 1, Edge.Left));
        Assert.AreEqual("0–1840", Free(Layouts.SideBottomAligned, 0, Edge.Top));

        Assert.AreEqual("0–400", Free(Layouts.StackOffset, 0, Edge.Top));
        Assert.AreEqual("2000–3040", Free(Layouts.StackOffset, 1, Edge.Bottom));
        Assert.AreEqual("80–1080", Free(Layouts.StackOffset, 0, Edge.Right));

        Assert.AreEqual("-200–-80, 1160–1240", Free(Layouts.PrimaryRight, 1, Edge.Right));
        Assert.AreEqual("—", Free(Layouts.PrimaryRight, 0, Edge.Left));
        Assert.AreEqual("80–1920", Free(Layouts.PrimaryRight, 0, Edge.Top));

        Assert.AreEqual("1160–2560", Free(Layouts.LShape, 1, Edge.Left, 500));
        Assert.AreEqual("2240–2560", Free(Layouts.LShape, 1, Edge.Left, 900), "C gets into the parking strip of a wide window");

        Assert.AreEqual("1160–2088", Free(Layouts.MixedDpi, 0, Edge.Right));
        Assert.AreEqual("—", Free(Layouts.MixedDpi, 1, Edge.Left));
    }

    static readonly Size[] Windows = [new(300, 200), new(760, 480), new(900, 1000), new(450, 1100), new(1600, 900)];

    [TestMethod]
    public void EveryTuckKeepsTheParkedInvariant()
    {
        var tucked = 0;
        foreach (var (name, layout) in Layouts.All)
            foreach (var m in layout)
                foreach (var e in Geometry.Edges)
                    foreach (var size in Windows)
                        foreach (var at in new[] { 0.0, 0.5, 1.0 }) // window at the start, middle and end of the monitor
                        {
                            var w = m.Work;
                            var frame = new Rectangle(w.Left + (int)((w.Width - size.Width) * at), w.Top + (int)((w.Height - size.Height) * at), size.Width, size.Height);
                            var r = Geometry.Tuck(layout, m, e, frame);
                            var what = $"{name} {m.Device} {e} {size} at {at}";
                            if (r.Plan is not { } p) { Assert.AreNotEqual(TuckFail.None, r.Fail, what); continue; }
                            tucked++;
                            Assert.AreEqual(size, p.Shown.Size, what);
                            Assert.AreEqual(p.Shown.Size, p.Parked.Size, what);
                            Assert.IsTrue(w.Contains(p.Shown), $"{what}: shown {p.Shown} outside work {w}");
                            Assert.IsFalse(layout.Any(n => n.Bounds.IntersectsWith(p.Parked)), $"{what}: parked {p.Parked} on a monitor");
                            Assert.AreEqual(Geometry.ParkGap, Geometry.Distance(p.Parked, m.Bounds), $"{what}: gap to own monitor");
                            foreach (var n in layout.Where(n => n != m))
                                Assert.IsTrue(Geometry.Distance(p.Parked, n.Bounds) >= Geometry.Margin, $"{what}: parked {p.Parked} too near {n.Device}");
                            var along = Geometry.Along(p.Shown, e);
                            Assert.IsTrue(along.Start >= p.Segment.Start && along.End <= p.Segment.End, $"{what}: shown outside its segment");
                            var tab = Geometry.Tabs(m, e, p.Segment, [along.Start + along.Length / 2], new())[0];
                            var tabAlong = Geometry.Along(tab, e);
                            Assert.IsTrue(tabAlong.Start >= p.Segment.Start && tabAlong.End <= p.Segment.End, $"{what}: tab {tab} outside {p.Segment}");
                            Assert.IsTrue(w.Contains(tab), $"{what}: tab {tab} outside work");
                        }
        Assert.IsTrue(tucked > 300, $"only {tucked} tucks succeeded: the loop proves little");
    }

    [TestMethod]
    public void TuckOnTheOwnersMonitors()
    {
        var o = Layouts.Owner;
        var r = Geometry.Tuck(o, Layouts.Display2, Edge.Left, new Rectangle(3600, 1300, 760, 480));
        Assert.AreEqual(Rectangle.FromLTRB(3440, 1520, 4200, 2000), r.Plan!.Shown);
        Assert.AreEqual(Rectangle.FromLTRB(2616, 1520, 3376, 2000), r.Plan.Parked);

        var phone = Geometry.Tuck(o, Layouts.Display2, Edge.Right, new Rectangle(4000, 1525, 360, 760)).Plan!;
        Assert.AreEqual(4944, phone.Parked.Left);
        Assert.AreEqual(Rectangle.FromLTRB(4520, 1525, 4880, 2285), phone.Shown);

        var tall = Geometry.Tuck(o, Layouts.Display2, Edge.Left, new Rectangle(3600, 100, 450, 1100));
        Assert.AreEqual(TuckFail.TooLong, tall.Fail);
        Assert.AreEqual(1040, tall.FreePx);
        Assert.IsNotNull(Geometry.Tuck(o, Layouts.Display2, Edge.Right, new Rectangle(3600, 100, 450, 1100)).Plan);

        foreach (var size in Windows)
            Assert.AreEqual(TuckFail.SharedEdge, Geometry.Tuck(o, Layouts.Display1, Edge.Right, new Rectangle(new Point(1000, 100), size)).Fail);

        Assert.AreEqual(Rectangle.FromLTRB(2160, 0, 3360, 500), Geometry.Tuck(o, Layouts.Display1, Edge.Top, new Rectangle(2600, 300, 1200, 500)).Plan!.Shown);
        Assert.AreEqual(1408, Geometry.Tuck(o, Layouts.Display1, Edge.Bottom, new Rectangle(100, 100, 800, 600)).Plan!.Shown.Bottom, "above the taskbar");
        Assert.AreEqual(TuckFail.TooBig, Geometry.Tuck(Layouts.Single, Layouts.Single[0], Edge.Top, new Rectangle(0, 0, 450, 1100)).Fail);
    }

    [TestMethod]
    public void DragGesture()
    {
        var s = Layouts.Single;
        Rectangle W(int x, int y) => new(x, y, 1000, 500);
        Assert.IsNull(Geometry.DragGestureEdge(s, W(-490, 100), false));
        Assert.AreEqual(Edge.Left, Geometry.DragGestureEdge(s, W(-510, 100), false)?.Edge);
        Assert.IsNull(Geometry.DragGestureEdge(s, W(1410, 100), false));
        Assert.AreEqual(Edge.Right, Geometry.DragGestureEdge(s, W(1430, 100), false)?.Edge);
        Assert.IsNull(Geometry.DragGestureEdge(s, W(100, 825), false));
        Assert.AreEqual(Edge.Bottom, Geometry.DragGestureEdge(s, W(100, 835), false)?.Edge);
        Assert.IsNull(Geometry.DragGestureEdge(s, W(100, -400), false), "never the top");
        Assert.IsNull(Geometry.DragGestureEdge(s, W(-510, 100), true), "never maximized");

        var o = Layouts.Owner;
        Assert.AreEqual((Layouts.Display2, Edge.Left), Geometry.DragGestureEdge(o, Rectangle.FromLTRB(3000, 1600, 3800, 2100), false));
        Assert.IsNull(Geometry.DragGestureEdge(o, Rectangle.FromLTRB(3000, 1000, 3800, 1500), false), "the part behind the edge is on DISPLAY1");
        Assert.IsNull(Geometry.DragGestureEdge(o, Rectangle.FromLTRB(3000, 100, 3800, 600), false), "across the seam is just a move");
    }

    [TestMethod]
    public void TabsDoNotOverlapAndKeepOffTheEnds()
    {
        var m = Layouts.Single[0];
        var seg = new Segment(0, 1080);
        var tabs = Geometry.Tabs(m, Edge.Left, seg, [500, 510, 520], new());
        for (var i = 0; i < tabs.Length; i++)
        {
            Assert.AreEqual(new Size(6, 64), tabs[i].Size);
            Assert.IsTrue(tabs[i].Top >= 64 && tabs[i].Bottom <= 1080 - 64);
            for (var j = i + 1; j < tabs.Length; j++) Assert.IsFalse(tabs[i].IntersectsWith(tabs[j]), $"{tabs[i]} and {tabs[j]}");
        }
        Assert.AreEqual(64, Geometry.Tabs(m, Edge.Left, seg, [0], new())[0].Top);
        Assert.AreEqual(1080 - 64 - 64, Geometry.Tabs(m, Edge.Left, seg, [5000], new())[0].Top);

        // owner, DISPLAY2 at 125 %: 8×80 px, 80 px from the segment ends (left edge below the seam: y ≥ 1600)
        var d2 = Layouts.Owner125[1];
        var t = Geometry.Tabs(d2, Edge.Left, new Segment(1520, 2560), [1500], new())[0];
        Assert.AreEqual(Rectangle.FromLTRB(3440, 1600, 3448, 1680), t);
        Assert.AreEqual(new Size(6, 64), Geometry.Tabs(Layouts.Display1, Edge.Left, new Segment(0, 1408), [700], new())[0].Size);
        Assert.AreEqual(1584, Geometry.Tabs(Layouts.Display2, Edge.Left, new Segment(1520, 2560), [1500], new())[0].Top, "plan 04 §7: y ≥ 1584");

        var k4 = Layouts.MixedDpi[0]; // 150 %
        var big = Geometry.Tabs(k4, Edge.Right, new Segment(1160, 2088), [1500], new())[0];
        Assert.AreEqual(new Size(9, 96), big.Size);
        Assert.AreEqual(3840 - 9, big.Left);
        Assert.AreEqual(1408 - 6, Geometry.Tabs(Layouts.Display1, Edge.Bottom, new Segment(0, 3360), [700], new())[0].Top, "above the taskbar");
    }

    [TestMethod]
    public void Fullscreen()
    {
        var mon = Layouts.Display1.Bounds;
        Assert.IsTrue(Geometry.IsFullscreen(mon, mon, false));
        Assert.IsFalse(Geometry.IsFullscreen(Rectangle.Inflate(mon, 7, 7), mon, true), "maximized");
        Assert.IsFalse(Geometry.IsFullscreen(Rectangle.FromLTRB(0, 0, 3440, 1408), mon, false), "smaller than the monitor");
    }

    [TestMethod]
    public void SlideInOnlyWhenTheMouseIsReallyGone()
    {
        var shown = new Rectangle(4520, 1525, 360, 760);
        Point far = new(3800, 300), near = new(4510, 1600);
        Assert.IsTrue(Geometry.ShouldSlideIn(false, false, false, far, shown, 16));
        Assert.IsFalse(Geometry.ShouldSlideIn(true, false, false, far, shown, 16), "pinned");
        Assert.IsFalse(Geometry.ShouldSlideIn(false, true, false, far, shown, 16), "a button is down");
        Assert.IsFalse(Geometry.ShouldSlideIn(false, false, true, far, shown, 16), "over its menu or popup");
        Assert.IsFalse(Geometry.ShouldSlideIn(false, false, false, near, shown, 16), "within the margin");
    }
}
