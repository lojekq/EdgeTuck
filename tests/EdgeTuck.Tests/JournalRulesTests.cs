using System.Drawing;
using EdgeTuck.Features;
using EdgeTuck.Shell;

namespace EdgeTuck.Tests;

[TestClass]
public class JournalRulesTests
{
    static readonly DateTime Start = new(2026, 10, 21, 9, 1, 12, 123, DateTimeKind.Utc);

    static JournalEntry Phone() => new()
    {
        Hwnd = 1234567, Pid = 4321, ProcStart = Start.AddTicks(4567), Exe = @"C:\Users\demo\Tools\scrcpy\scrcpy.exe", Class = "SDL_app",
        Edge = Edge.Right, Monitor = @"\\.\DISPLAY2",
        Shown = JournalEntry.Ltrb(Rectangle.FromLTRB(4520, 1525, 4880, 2285)), Parked = JournalEntry.Ltrb(Rectangle.FromLTRB(4944, 1525, 5304, 2285)),
    };

    [TestMethod]
    public void RoundTrip()
    {
        var path = Path.Combine(Path.GetTempPath(), $"edgetuck-test-{Guid.NewGuid():N}.json");
        try
        {
            var f = new JournalFile { OwnerPid = 12345, OwnerStart = Start, Windows = [Phone()] };
            Journal.AddRescue(f, Phone().Exe);
            Journal.AddRescue(f, "SCRCPY.EXE");
            Journal.Save(path, f);
            var back = Journal.Load(path);
            Assert.AreEqual(12345, back.OwnerPid);
            Assert.AreEqual(Start, back.OwnerStart);
            var e = back.Windows.Single();
            Assert.AreEqual(Phone().ProcStart, e.ProcStart, "100 ns precision survives JSON");
            Assert.AreEqual(Phone().ShownRect, e.ShownRect);
            Assert.AreEqual(Phone().ParkedRect, e.ParkedRect);
            Assert.AreEqual(Edge.Right, e.Edge);
            CollectionAssert.AreEqual(new[] { "scrcpy.exe" }, back.RescueExes);
            StringAssert.Contains(File.ReadAllText(path), "\"edge\": \"Right\"");
        }
        finally { foreach (var p in new[] { path, path + ".bak" }) File.Delete(p); }
    }

    [TestMethod]
    public void ReusedHandlesAreNotRestored()
    {
        var e = Phone();
        Assert.IsTrue(Journal.Matches(e, 4321, e.ProcStart, "SDL_app"));
        Assert.IsFalse(Journal.Matches(e, 4322, e.ProcStart, "SDL_app"), "another pid");
        Assert.IsFalse(Journal.Matches(e, 4321, e.ProcStart.AddSeconds(3), "SDL_app"), "pid reused by a newer process");
        Assert.IsFalse(Journal.Matches(e, 4321, null, "SDL_app"), "process gone");
        Assert.IsFalse(Journal.Matches(e, 4321, e.ProcStart, "Chrome_WidgetWin_1"), "hwnd reused by another window");
    }

    [TestMethod]
    public void RestoreTargetFollowsTheMonitorsThatExist()
    {
        var e = Phone();
        Assert.AreEqual(e.ShownRect, Journal.RestoreTarget(e, e.ShownRect.Size, Layouts.Owner));
        Assert.AreEqual(Rectangle.FromLTRB(3080, 648, 3440, 1408), Journal.RestoreTarget(e, e.ShownRect.Size, [Layouts.Display1]), "DISPLAY2 unplugged");
        Assert.AreEqual(new Size(400, 700), Journal.RestoreTarget(e, new Size(400, 700), Layouts.Owner).Size, "resized while parked");
        var tall = new JournalEntry { Shown = JournalEntry.Ltrb(new Rectangle(3440, 1600, 486, 480)) };
        Assert.AreEqual(new Rectangle(3440, 1467, 486, 1093), Journal.RestoreTarget(tall, new Size(486, 1093), Layouts.Owner), "grew taller while tucked: pulled up into DISPLAY2");
        var huge = new JournalEntry { Shown = JournalEntry.Ltrb(new Rectangle(100, 100, 300, 300)) };
        Assert.AreEqual(new Point(0, 0), Journal.RestoreTarget(huge, new Size(4000, 2000), Layouts.Owner).Location, "bigger than the monitor: top-left on screen");
    }

    [TestMethod]
    public void RulesMatchByExe()
    {
        Rule[] rules = [new() { Exe = "Discord.exe", Enabled = false }, new() { Exe = "scrcpy.exe", Edge = Edge.Right }];
        Assert.AreEqual(rules[1], Rules.Match(rules, @"C:\Tools\SCRCPY.EXE"));
        Assert.AreEqual(rules[1], Rules.Match(rules, "scrcpy.exe"));
        Assert.IsNull(Rules.Match(rules, @"C:\Users\demo\AppData\Local\Discord\Discord.exe"), "disabled");
        Assert.IsNull(Rules.Match(rules, null));
    }

    [TestMethod]
    public void AlongOnASharedPartMovesIntoTheFreeOne()
    {
        var o = Layouts.Owner;
        var spotify = new Rule { Exe = "Spotify.exe", Edge = Edge.Left, Monitor = @"\\.\DISPLAY2", Along = 0.2 }; // y 512: shared with DISPLAY1
        var (m, frame) = Rules.Target(spotify, new Rectangle(100, 100, 760, 480), o);
        Assert.AreEqual(Layouts.Display2, m);
        Assert.AreEqual(512 - 240, frame.Top);
        Assert.AreEqual(Rectangle.FromLTRB(3440, 1520, 4200, 2000), Geometry.Tuck(o, m, spotify.Edge, frame).Plan!.Shown);

        var gone = new Rule { Exe = "scrcpy.exe", Edge = Edge.Right, Monitor = @"\\.\DISPLAY3" };
        Assert.AreEqual(Layouts.Display2, Rules.Target(gone, new Rectangle(3600, 100, 360, 760), o).Monitor, "the monitor the window is on");
        Assert.AreEqual(Layouts.Display1, Rules.Target(gone, new Rectangle(-5000, 100, 360, 760), o).Monitor, "off screen: the primary");

        var plan = Geometry.Tuck(o, Layouts.Display2, Edge.Right, new Rectangle(4000, 1525, 360, 760)).Plan!;
        var made = Rules.From(@"C:\Tools\scrcpy\scrcpy.exe", plan);
        Assert.AreEqual(("scrcpy.exe", Edge.Right, @"\\.\DISPLAY2"), (made.Exe, made.Edge, made.Monitor));
        Assert.AreEqual(plan.Shown.Top, Geometry.Tuck(o, Layouts.Display2, Edge.Right, Rules.Target(made, plan.Shown, o).Frame).Plan!.Shown.Top);
    }
}
