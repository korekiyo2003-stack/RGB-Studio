using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Diagnostics;

internal sealed class RigPreview : Control
{
    public Scene Scene { get; set; }
    private readonly Stopwatch time = Stopwatch.StartNew();
    private readonly Timer frames = new Timer { Interval = 42 };

    public RigPreview()
    {
        DoubleBuffered = true;
        AccessibleName = "机箱灯光示意图";
        frames.Tick += delegate { if (Visible) Invalidate(); };
        frames.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) frames.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.FromArgb(15, 21, 29));
        float scale = Math.Min(Width / 360f, Height / 270f);
        if (scale <= 0) return;
        g.TranslateTransform((Width - 360 * scale) / 2, (Height - 270 * scale) / 2);
        g.ScaleTransform(scale, scale);
        double seconds = time.Elapsed.TotalSeconds;
        Color cooler = Parse(Scene == null ? null : Scene.cooler);
        Color memory = Parse(Scene == null || Scene.memory == null ? null : Scene.memory[0]);
        if (Scene != null && Scene.coolerBreathing)
            cooler = Mix(Color.FromArgb(32, 42, 57), cooler, Breath(seconds + 1.2, 7));
        cooler = LightingPanel.PreviewFan(this, cooler, seconds);
        Color[][] sticks = LightingPanel.PreviewMemory(this, memory, seconds);
        using (var shell = new LinearGradientBrush(new Rectangle(26, 22, 307, 220),
            Color.FromArgb(40, 53, 65), Color.FromArgb(20, 29, 40), 90f))
        using (var line = new Pen(Color.FromArgb(104, 127, 143), 1.2f))
        {
            g.FillRectangle(shell, 26, 22, 307, 220);
            g.DrawRectangle(line, 26, 22, 307, 220);
            g.DrawLine(line, 26, 233, 333, 233);
        }
        Fill(g, Color.FromArgb(24, 33, 44), 82, 45, 177, 123);
        using (var grid = new Pen(Color.FromArgb(49, 66, 79), 1))
        {
            for (int x = 89; x < 257; x += 13) g.DrawLine(grid, x, 47, x, 166);
            for (int y = 49; y < 166; y += 13) g.DrawLine(grid, 84, y, 256, y);
        }
        Fan(g, 60, 86, 22, Ring(2, seconds, 0, 1));
        Fan(g, 300, 60, 24, Ring(1, seconds, 0, 3));
        Fan(g, 300, 115, 24, Ring(1, seconds, 1, 3));
        Fan(g, 300, 170, 24, Ring(1, seconds, 2, 3));
        Fan(g, 105, 203, 23, Ring(0, seconds, 0, 3));
        Fan(g, 163, 203, 23, Ring(0, seconds, 1, 3));
        Fan(g, 221, 203, 23, Ring(0, seconds, 2, 3));
        Fill(g, Color.FromArgb(77, 92, 106), 97, 137, 160, 30);
        Fill(g, Color.FromArgb(49, 61, 76), 99, 143, 155, 20);
        Fill(g, Color.FromArgb(66, 82, 96), 112, 57, 82, 74);
        for (int x = 116; x < 193; x += 4) Fill(g, Color.FromArgb(89, 105, 117), x, 61, 1, 66);
        Fan(g, 149, 94, 24, Solid(cooler));
        for (int stick = 0; stick < 2; stick++)
        {
            int x = 209 + stick * 16;
            Fill(g, Color.FromArgb(58, 70, 84), x, 54, 11, 75);
            for (int k = 0; k < 8; k++)
            {
                // LED 0 is drawn at the top; which end it is on the real stick has not been checked.
                Fill(g, Color.FromArgb(120, sticks[stick][k]), x + 1, 56 + k * 9, 9, 9);
                Fill(g, sticks[stick][k], x + 3, 57 + k * 9, 5, 7);
            }
        }
    }

    private static double Breath(double seconds, double period)
    {
        return 0.24 + 0.76 * (0.5 - 0.5 * Math.Cos(2 * Math.PI * seconds / period));
    }

    private const int Leds = 12;

    private Color[] Ring(int zone, double seconds, int fan, int fans)
    {
        var ring = new Color[Leds];
        for (int s = 0; s < Leds; s++)
            ring[s] = Scene == null ? Parse(null) : Led(Scene.zones[zone], seconds, fan * Leds + s, fans * Leds);
        return ring;
    }

    private static Color[] Solid(Color color)
    {
        var ring = new Color[Leds];
        for (int s = 0; s < Leds; s++) ring[s] = color;
        return ring;
    }

    private static double Frac(double x) { return x - Math.Floor(x); }

    private static double Noise(int a, int b)
    {
        unchecked
        {
            uint h = (uint)(a * 374761393 + b * 668265263);
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0xFFFF) / 65535.0;
        }
    }

    // An approximation of the controller's firmware effects, one colour per LED. Position i runs along the
    // whole channel, so a marquee or wave travels from one fan to the next as it does on the daisy chain.
    private static Color Led(Zone zone, double t, int i, int n)
    {
        var low = Color.FromArgb(30, 39, 52);
        string[] c = zone.colors;
        Color first = Parse(c[0]);
        double sf = zone.speed == "Slowest" ? 0.7 : zone.speed == "Normal" ? 1.4 : zone.speed == "Fast" ? 2.0 : 1.0;
        int dir = zone.direction == "Backwards" ? -1 : 1;
        int length = zone.length > 0 ? zone.length : 4;
        switch (zone.effect)
        {
            case "BreathingCycling":
            case "BreathingFixed":
            {
                double period = 6.8 / sf;
                int k = zone.effect == "BreathingFixed" ? 0 : (int)(t / period) % c.Length;
                return Mix(low, Parse(c[k]), Breath(t, period));
            }
            case "Pulse":
            {
                double beat = 2.6 / sf;
                double progress = t % beat;
                double glow = Math.Exp(-5.5 * progress) + 0.52 * Math.Exp(-64 * Math.Pow(progress - .28, 2));
                return Mix(low, Parse(c[(int)(t / beat) % c.Length]), 0.19 + 0.81 * Math.Min(1, glow));
            }
            case "Fading":
            {
                if (c.Length < 2) return first;
                double step = (t * sf / 2.8) % c.Length;
                int index = (int)step;
                double amount = (1 - Math.Cos(Math.PI * (step - index))) * .5;
                return Mix(Parse(c[index]), Parse(c[(index + 1) % c.Length]), amount);
            }
            case "Marquee":
            {
                double head = t * sf * 6;
                double d = Frac((i * dir - head) / n) * n;
                return d < length ? first : low;
            }
            case "CoveringMarquee":
            {
                double period = 3.2 / sf;
                int k = (int)(t / period);
                double front = Frac(t / period) * n;
                int at = dir > 0 ? i : n - 1 - i;
                return Parse(at < front ? c[k % c.Length] : c[(k + c.Length - 1) % c.Length]);
            }
            case "Alternating":
            {
                double shift = t * sf * 3 * dir;
                int block = (int)Math.Floor((i - shift) / length);
                return Parse(c[(((block % 2) + 2) % 2) % c.Length]);
            }
            case "StarryNight":
            {
                double clock = t * sf / 1.1 + Noise(i, 7);
                Color dim = Mix(low, first, 0.12);
                if (Noise(i, (int)Math.Floor(clock)) > 0.24) return dim;
                return Mix(dim, first, Math.Sin(Math.PI * Frac(clock)));
            }
            case "Candle":
            {
                double level = .62 + .2 * Math.Sin(t * 5.3) + .12 * Math.Sin(t * 11.7 + 1.3) + .06 * Math.Sin(t * 23.1);
                return Mix(low, first, Math.Max(0.25, Math.Min(1, level)));
            }
            case "Wave":
            {
                double x = i / (double)n * 2 - t * sf / 3.5 * dir;
                return Mix(low, first, .12 + .88 * Math.Pow(.5 + .5 * Math.Cos(2 * Math.PI * x), 2));
            }
            case "TaiChi":
                return Parse(c[(Frac(i / (double)n - t * sf / 5 * dir) < .5 ? 0 : 1) % c.Length]);
            case "Swish":
            {
                double d = Frac((t * sf * 7 - i) / n) * n;
                return Mix(low, first, Math.Exp(-d / (n * 0.16)));
            }
        }
        return first;
    }

    private static Color Mix(Color start, Color end, double ratio)
    {
        return Color.FromArgb((int)(start.R + (end.R - start.R) * ratio),
            (int)(start.G + (end.G - start.G) * ratio),
            (int)(start.B + (end.B - start.B) * ratio));
    }

    private static Color Parse(string value)
    {
        return value == null ? Color.FromArgb(111, 143, 157) : ColorTranslator.FromHtml("#" + value);
    }

    private static void Fill(Graphics g, Color color, int x, int y, int w, int h)
    {
        using (var brush = new SolidBrush(color)) g.FillRectangle(brush, x, y, w, h);
    }

    private static void Fan(Graphics g, int x, int y, int r, Color[] leds)
    {
        int red = 0, green = 0, blue = 0;
        foreach (Color led in leds) { red += led.R; green += led.G; blue += led.B; }
        Color color = Color.FromArgb(red / leds.Length, green / leds.Length, blue / leds.Length);
        using (var path = new GraphicsPath())
        {
            int bloom = r + 11;
            path.AddEllipse(x - bloom, y - bloom, bloom * 2, bloom * 2);
            using (var glow = new PathGradientBrush(path) {
                CenterColor = Color.FromArgb(76, color), SurroundColors = new[] { Color.FromArgb(0, color) }
            })
                g.FillPath(glow, path);
        }
        float sweep = 360f / leds.Length;
        for (int s = 0; s < leds.Length; s++)
        {
            using (var halo = new Pen(Color.FromArgb(42, leds[s]), 10))
            using (var ring = new Pen(leds[s], 2.8f))
            {
                g.DrawArc(halo, x-r, y-r, 2*r, 2*r, s * sweep - 90, sweep + 1);
                g.DrawArc(ring, x-r, y-r, 2*r, 2*r, s * sweep - 90, sweep + 1);
            }
        }
        using (var blade = new SolidBrush(Color.FromArgb(70, color)))
        using (var hub = new SolidBrush(Color.FromArgb(92, 106, 122)))
        {
            for (int a = 0; a < 360; a += 60)
                g.FillPie(blade, x-r+4, y-r+4, 2*r-8, 2*r-8, a, 23);
            g.FillEllipse(hub, x-5, y-5, 10, 10);
        }
    }
}
