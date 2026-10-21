using System.Windows.Media;
using EdgeTuck.Features;
using Geometry = EdgeTuck.Features.Geometry;

namespace EdgeTuck.Tests;

[TestClass]
public class TabColorsTests
{
    /// <summary>A 16×16 BGRA icon: `colored` pixels of one color, the rest gray, some transparent.</summary>
    static byte[] Icon(Color main, int colored, Color? rest = null, int transparent = 0)
    {
        var px = new byte[16 * 16 * 4];
        for (var i = 0; i < 256; i++)
        {
            var c = i < colored ? main : rest ?? Color.FromRgb(0x80, 0x80, 0x80);
            px[i * 4] = c.B; px[i * 4 + 1] = c.G; px[i * 4 + 2] = c.R; px[i * 4 + 3] = (byte)(i >= 256 - transparent ? 0 : 255);
        }
        return px;
    }

    [TestMethod]
    public void DominantColorOfAnIcon()
    {
        var spotify = Color.FromRgb(0x1E, 0xD7, 0x60);
        Assert.AreEqual(spotify, TabColors.Dominant(Icon(spotify, 200)));
        Assert.AreEqual(spotify, TabColors.Dominant(Icon(spotify, 60, transparent: 150)), "transparent pixels do not count");
        Assert.IsNull(TabColors.Dominant(Icon(Color.FromRgb(0x30, 0x30, 0x30), 256)), "gray and black icons have no color");
        Assert.IsNull(TabColors.Dominant(Icon(Colors.Red, 5)), "a few colored pixels are not enough");
        var mixed = Icon(Colors.Blue, 150, rest: Colors.Red);
        Assert.AreEqual(Colors.Blue, TabColors.Dominant(mixed), "the bigger hue wins");
    }

    [TestMethod]
    public void IconColorsAreMadeReadable()
    {
        var yellow = Color.FromRgb(0xFC, 0xE1, 0x00);
        var onLight = TabColors.Readable(yellow, dark: false);
        Assert.IsTrue(TabColors.Contrast(onLight, TabColors.LightBackground) >= TabColors.MinContrast, $"{onLight} on light");
        Assert.IsTrue(TabColors.Luminance(onLight) < TabColors.Luminance(yellow), "darker on a light theme");

        var navy = Color.FromRgb(0x10, 0x20, 0x50);
        var onDark = TabColors.Readable(navy, dark: true);
        Assert.IsTrue(TabColors.Contrast(onDark, TabColors.DarkBackground) >= TabColors.MinContrast, $"{onDark} on dark");
        Assert.IsTrue(onDark.B > onDark.R, "still blue");

        var accent = Color.FromRgb(0x0A, 0x84, 0xFF);
        Assert.AreEqual(accent, TabColors.Readable(accent, dark: true), "already readable: unchanged");
    }

    [TestMethod]
    public void HexAndEasing()
    {
        Assert.AreEqual(Color.FromRgb(0x0A, 0x84, 0xFF), TabColors.Parse("#0a84ff"));
        Assert.AreEqual(Color.FromRgb(0x0A, 0x84, 0xFF), TabColors.Parse(" 0A84FF "));
        Assert.IsNull(TabColors.Parse("#12345"));
        Assert.IsNull(TabColors.Parse("blue"));
        Assert.AreEqual("#E81123", TabColors.Hex(TabColors.Parse("e81123")!.Value));
        Assert.IsTrue(TabColors.Palette.All(p => TabColors.Parse(p) is not null));

        foreach (var ease in new Func<double, double>[] { Geometry.EaseOut, Geometry.EaseInOut })
        {
            Assert.AreEqual(0, ease(0), 1e-9);
            Assert.AreEqual(1, ease(1), 1e-9);
            for (var t = 0.0; t < 1; t += 0.05) Assert.IsTrue(ease(t + 0.05) >= ease(t), "never goes back");
        }
        Assert.IsTrue(Geometry.EaseOut(0.3) > 0.6, "slide out: most of the way early, a soft landing");
        Assert.AreEqual(0.5, Geometry.EaseInOut(0.5), 1e-9);
        Assert.IsTrue(Geometry.EaseInOut(0.1) < 0.01, "slide in: a gentle start");
    }
}
