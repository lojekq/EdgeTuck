using System.Collections.Concurrent;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EdgeTuck.Features;

/// <summary>
/// v1.1: the tab's color. "icon" = the dominant color of the program's icon, made readable on the theme's background;
/// colorless icons fall back to the Windows accent (null). Pure functions over pixels and colors, plus a cache by icon.
/// </summary>
public static class TabColors
{
    /// <summary>Ten easy picks for the custom color and per-window colors.</summary>
    public static readonly string[] Palette =
        ["#0A84FF", "#00B7C3", "#16C60C", "#FCE100", "#F7630C", "#E81123", "#E3008C", "#8764B8", "#7A7574", "#F2F2F2"];

    /// <summary>Backgrounds a tab is judged against: Windows' light and dark app backgrounds.</summary>
    public static readonly Color LightBackground = Color.FromRgb(0xF3, 0xF3, 0xF3), DarkBackground = Color.FromRgb(0x20, 0x20, 0x20);

    /// <summary>WCAG contrast for non-text UI: a tab must stand out at least this much from the background.</summary>
    public const double MinContrast = 3.0;

    /// <summary>
    /// The dominant colorful color of BGRA pixels (straight alpha): opaque, saturated and not too dark pixels are grouped by hue,
    /// the biggest group (with its neighbors) is averaged. Null when fewer than 8 % of the opaque pixels have a color.
    /// </summary>
    public static Color? Dominant(byte[] bgra)
    {
        const int Buckets = 24;
        var weight = new double[Buckets];
        var sum = new double[Buckets, 3];
        int opaque = 0, colorful = 0;
        for (var i = 0; i + 3 < bgra.Length; i += 4)
        {
            byte b = bgra[i], g = bgra[i + 1], r = bgra[i + 2], a = bgra[i + 3];
            if (a < 160) continue;
            opaque++;
            var (h, s, v) = Hsv(r, g, b);
            if (s < 0.25 || v < 0.2) continue;
            colorful++;
            var k = (int)(h / 360 * Buckets) % Buckets;
            weight[k] += s * v;
            sum[k, 0] += r * s * v; sum[k, 1] += g * s * v; sum[k, 2] += b * s * v;
        }
        if (opaque == 0 || colorful < Math.Max(4, opaque * 0.08)) return null;
        var best = Enumerable.Range(0, Buckets).MaxBy(k => weight[(k + Buckets - 1) % Buckets] + weight[k] + weight[(k + 1) % Buckets]);
        double w = 0, R = 0, G = 0, B = 0;
        foreach (var k in new[] { (best + Buckets - 1) % Buckets, best, (best + 1) % Buckets })
        {
            w += weight[k]; R += sum[k, 0]; G += sum[k, 1]; B += sum[k, 2];
        }
        return Color.FromRgb((byte)Math.Round(R / w), (byte)Math.Round(G / w), (byte)Math.Round(B / w));
    }

    /// <summary>The same hue, lighter on a dark background or darker on a light one, until it has MinContrast.</summary>
    public static Color Readable(Color c, bool dark)
    {
        var bg = dark ? DarkBackground : LightBackground;
        var (h, s, l) = Hsl(c);
        for (var i = 0; i < 40 && Contrast(c, bg) < MinContrast; i++)
        {
            l = Math.Clamp(l + (dark ? 0.025 : -0.025), 0, 1);
            c = FromHsl(h, s, l);
        }
        return c;
    }

    /// <summary>WCAG relative luminance.</summary>
    public static double Luminance(Color c)
    {
        static double Lin(byte v) { var x = v / 255.0; return x <= 0.04045 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4); }
        return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
    }

    public static double Contrast(Color a, Color b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>"#RRGGBB" or "RRGGBB", any case.</summary>
    public static Color? Parse(string? hex)
    {
        var s = hex?.Trim().TrimStart('#');
        return s is { Length: 6 } && uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)
            ? Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v)
            : null;
    }

    public static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    static readonly ConcurrentDictionary<ImageSource, Color?> FromIcon = new();

    /// <summary>The icon's dominant color, readable on the theme; null = use the Windows accent. Cached per icon.</summary>
    public static Color? OfIcon(ImageSource? icon, bool dark)
    {
        if (icon is null) return null;
        var c = FromIcon.GetOrAdd(icon, i => Dominant(Pixels(i)));
        return c is { } color ? Readable(color, dark) : null;
    }

    /// <summary>BGRA pixels of any image source: an exe icon is a bitmap, a demo glyph a drawing (rendered at 32 px).</summary>
    static byte[] Pixels(ImageSource src)
    {
        BitmapSource bmp;
        if (src is BitmapSource b) bmp = b;
        else
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen()) dc.DrawImage(src, new Rect(0, 0, 32, 32));
            var rt = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
            rt.Render(visual);
            bmp = rt;
        }
        var bgra = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[bgra.PixelWidth * bgra.PixelHeight * 4];
        bgra.CopyPixels(pixels, bgra.PixelWidth * 4, 0);
        return pixels;
    }

    static (double H, double S, double V) Hsv(byte r, byte g, byte b)
    {
        double R = r / 255.0, G = g / 255.0, B = b / 255.0, max = Math.Max(R, Math.Max(G, B)), min = Math.Min(R, Math.Min(G, B)), d = max - min;
        var h = d == 0 ? 0 : max == R ? 60 * (((G - B) / d + 6) % 6) : max == G ? 60 * ((B - R) / d + 2) : 60 * ((R - G) / d + 4);
        return (h, max == 0 ? 0 : d / max, max);
    }

    static (double H, double S, double L) Hsl(Color c)
    {
        double R = c.R / 255.0, G = c.G / 255.0, B = c.B / 255.0, max = Math.Max(R, Math.Max(G, B)), min = Math.Min(R, Math.Min(G, B)), d = max - min;
        var l = (max + min) / 2;
        var s = d == 0 ? 0 : d / (1 - Math.Abs(2 * l - 1));
        var h = d == 0 ? 0 : max == R ? 60 * (((G - B) / d + 6) % 6) : max == G ? 60 * ((B - R) / d + 2) : 60 * ((R - G) / d + 4);
        return (h, s, l);
    }

    static Color FromHsl(double h, double s, double l)
    {
        var c = (1 - Math.Abs(2 * l - 1)) * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = l - c / 2;
        var (r, g, b) = h switch { < 60 => (c, x, 0.0), < 120 => (x, c, 0.0), < 180 => (0.0, c, x), < 240 => (0.0, x, c), < 300 => (x, 0.0, c), _ => (c, 0.0, x) };
        static byte B8(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
        return Color.FromRgb(B8(r + m), B8(g + m), B8(b + m));
    }
}
