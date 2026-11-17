using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Sockets;

// Software-driven memory lighting.
// The ENE chip on each stick can run effects by itself, but the two chips keep their own time and drift
// apart within minutes (reported by the user on 2026-11-13: the sticks no longer flashed together). So both
// sticks stay in Direct mode and one thread here sends every frame to both, from one clock. The preview
// draws the same frames, so what the window shows is what was sent.
public static partial class MemoryLighting
{
    internal sealed class Show
    {
        public string Effect;      // Static Breath Flow Chase Wave Twinkle Bounce Duet Heartbeat Fill Flash DoubleBeat Shimmer RippleFlash Off
        public int Speed;          // 0 fastest .. 4 slowest
        public bool Reverse;
        public string[] Points;    // four scene colours, RRGGBB
    }

    internal struct Px { public int R, G, B; public double Level; }

    internal const int Leds = 8;           // preview size; the hardware gets as many as each stick has
    private static readonly double[] Periods = { 1.3, 2.0, 3.0, 4.4, 6.2 };
    internal static double Gamma = 2.2;   // 1 = send scene colours as they are; larger = deeper colours

    private static double Hash(int a, int b)
    {
        unchecked
        {
            uint h = (uint)(a * 374761393 + b * 668265263);
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0xFFFF) / 65535.0;
        }
    }

    // Colour at x along the four points: 0..1 end to end, or wrapping round when cyclic.
    private static Px Along(int[][] c, double x, bool cyclic)
    {
        double pos;
        int a, b;
        if (cyclic)
        {
            pos = (x - Math.Floor(x)) * c.Length;
            a = Math.Min((int)pos, c.Length - 1);
            b = (a + 1) % c.Length;
        }
        else
        {
            pos = Math.Max(0, Math.Min(1, x)) * (c.Length - 1);
            a = Math.Min((int)pos, c.Length - 2);
            b = a + 1;
        }
        double f = pos - a;
        return new Px { R = (int)Math.Round(c[a][0] + (c[b][0] - c[a][0]) * f), G = (int)Math.Round(c[a][1] + (c[b][1] - c[a][1]) * f),
            B = (int)Math.Round(c[a][2] + (c[b][2] - c[a][2]) * f), Level = 1 };
    }

    // What both sticks show t seconds into the show: [stick][led]. Colour and brightness are kept apart so
    // the preview can dim towards its background while the hardware gets a linear-light value.
    internal static Px[][] Frame(Show show, double t)
    {
        return Frame(show, t, new[] { Leds, Leds });
    }

    // Same, for any number of sticks with any number of LEDs each (OpenRGB reports both).
    internal static Px[][] Frame(Show show, double t, int[] ledsPerStick)
    {
        var c = show.Points.Select(hex => {
            int v = int.Parse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
            return new[] { (v >> 16) & 255, (v >> 8) & 255, v & 255 };
        }).ToArray();
        double period = Periods[Math.Max(0, Math.Min(4, show.Speed))];
        double p = t / period - Math.Floor(t / period);
        int turn = (int)Math.Floor(t / period);
        var sticks = new Px[ledsPerStick.Length][];
        for (int s = 0; s < ledsPerStick.Length; s++)
        {
            int Leds = Math.Max(1, ledsPerStick[s]);
            sticks[s] = new Px[Leds];
            for (int i = 0; i < Leds; i++)
            {
                int at = show.Reverse ? Leds - 1 - i : i;
                Px px = Along(c, i / (double)Math.Max(1, Leds - 1), false);
                double level = 1;
                switch (show.Effect)
                {
                    case "Off": level = 0; break;
                    case "Breath": level = 0.06 + 0.94 * (0.5 - 0.5 * Math.Cos(2 * Math.PI * p)); break;
                    case "Flash":
                    {
                        // 柔光脉冲闪：快速起峰后平滑指数衰减，取代生硬方波
                        double attack = 0.12;
                        level = p < attack
                            ? 0.04 + 0.96 * Math.Pow(p / attack, 2)
                            : 0.04 + 0.96 * Math.Exp(-5.2 * (p - attack));
                        break;
                    }
                    case "DoubleBeat":
                    {
                        // 双重律动闪：像双击鼓点，两段利落的闪光峰值，随之柔和淡出
                        double b1 = p < 0.18 ? Math.Exp(-40 * Math.Pow(p - 0.08, 2)) : 0;
                        double b2 = (p >= 0.18 && p < 0.45) ? 0.85 * Math.Exp(-45 * Math.Pow(p - 0.28, 2)) : 0;
                        level = 0.04 + 0.96 * Math.Max(b1, b2);
                        break;
                    }
                    case "Shimmer":
                    {
                        // 极光微烁：有机微脉冲波在8颗灯珠间流动闪烁
                        double phase = (p * 3.5 + at * 0.28) % 1.0;
                        double wave = 0.5 + 0.5 * Math.Sin(2 * Math.PI * phase);
                        double micro = 0.5 + 0.5 * Math.Sin(6 * Math.PI * phase);
                        level = 0.06 + 0.94 * (0.65 * wave + 0.35 * micro * wave);
                        break;
                    }
                    case "RippleFlash":
                    {
                        // 逐光掠影：集中光斑极速掠过内存条
                        double head = p * (Leds + 3);
                        double dist = Math.Abs(at - head);
                        level = 0.04 + 0.96 * Math.Exp(-dist * dist / 1.0);
                        break;
                    }
                    case "Chase": level = (p * Leds - at + Leds) % Leds < 3 ? 1 : 0.04; break;
                    case "Flow":
                    {
                        double behind = p * (Leds + 6) - at;
                        level = behind < 0 ? 0.03 : Math.Max(0.03, Math.Exp(-behind / 2.0));
                        break;
                    }
                    case "Wave":
                        px = Along(c, at / (double)Leds - p, true);
                        break;
                    case "Bounce":
                    {
                        double head = (Leds - 1) * (1 - Math.Abs(2 * p - 1.0));
                        level = Math.Max(0.04, Math.Exp(-(at - head) * (at - head) / 1.6));
                        break;
                    }
                    case "Duet":
                    {
                        // The sticks answer each other: one breathes in the first half, the other in the second.
                        double half = 2 * p - Math.Floor(2 * p);
                        bool mine = (p < 0.5) == (s % 2 == 0);
                        level = mine ? 0.06 + 0.94 * Math.Pow(Math.Sin(Math.PI * half), 2) : 0.06;
                        px = Along(c, (s % 2 == 0 ? i : Leds - 1 - i) / (double)Math.Max(1, Leds - 1), false);
                        break;
                    }
                    case "Heartbeat":
                        level = Math.Max(0.08, Math.Max(Math.Exp(-7 * p), 0.65 * Math.Exp(-70 * (p - 0.26) * (p - 0.26))));
                        break;
                    case "Fill":
                    {
                        int k = ((turn % c.Length) + c.Length) % c.Length;
                        int[] now = at < p * (Leds + 1) ? c[k] : c[(k + c.Length - 1) % c.Length];
                        px = new Px { R = now[0], G = now[1], B = now[2] };
                        break;
                    }
                    case "Twinkle":
                    {
                        double clock = t / (period / 2.5) + Hash(i, 7 + s);
                        double spark = Hash(i * 31 + s, (int)Math.Floor(clock)) < 0.34 ? Math.Sin(Math.PI * (clock - Math.Floor(clock))) : 0;
                        level = 0.12 + 0.88 * spark;
                        break;
                    }
                }
                px.Level = level;
                sticks[s][i] = px;
            }
        }
        return sticks;
    }

    // LEDs are linear while scene colours are sRGB values picked on a monitor: hue is corrected relative to
    // the strongest channel (see Vivid), brightness goes from a perceptual level to linear light.
    private static uint Wire(Px px)
    {
        double max = Math.Max(px.R, Math.Max(px.G, px.B));
        if (max == 0) return 0;
        double gain = Math.Pow(Math.Max(0, Math.Min(1, px.Level)), 2.2);
        Func<int, uint> channel = v => (uint)Math.Round(Math.Pow(v / max, Gamma) * max * gain);
        return channel(px.R) | (channel(px.G) << 8) | (channel(px.B) << 16);
    }

    private static readonly object ShowLock = new object();
    private static readonly System.Threading.ManualResetEventSlim FirstFrame = new System.Threading.ManualResetEventSlim(false);
    private static System.Threading.Thread showThread;
    private static volatile Show showNow;
    private static volatile int showGeneration;
    private static volatile string showError;
    internal static long FramesSent;

    // Starts (or replaces) the show. Returns null once the first frame has been accepted, otherwise why not.
    internal static string Start(Show show)
    {
        if (System.Diagnostics.Process.GetProcessesByName("AsrPolychromeRGB").Length != 0) return "请先退出 Polychrome";
        if (show.Points == null || show.Points.Length != 4 || !show.Points.All(IsHex)) return "缺少四点配色";
        lock (ShowLock)
        {
            FirstFrame.Reset();
            showError = null;
            showNow = show;
            showGeneration++;
            if (showThread == null)
            {
                showThread = new System.Threading.Thread(RunShow) { IsBackground = true, Name = "memory show" };
                showThread.Start();
            }
        }
        return FirstFrame.Wait(4000) ? showError : "灯控服务响应超时";
    }

    private static void RunShow()
    {
        Sdk sdk = null;
        List<Device> devices = null;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var sinceDirect = System.Diagnostics.Stopwatch.StartNew();
        int shown = -1, reported = -1;
        while (true)
        {
            Show show = showNow;
            int generation = showGeneration;
            try
            {
                bool fresh = shown != generation;
                if (sdk == null) { sdk = new Sdk(); devices = null; }
                // Direct mode is re-asserted every 20 s: the flag on the chip is lost after sleep.
                if (devices == null || fresh || sinceDirect.Elapsed.TotalSeconds > 20)
                {
                    devices = sdk.MemoryDevices();
                    if (devices.Count == 0) throw new IOException("未找到内存灯（OpenRGB 没有列出 DRAM 设备）");
                    foreach (Device device in devices) sdk.Send(device.Id, 1100, new byte[0]);
                    sinceDirect.Restart();
                    fresh = true;
                }
                if (shown != generation) clock.Restart();
                bool moving = show.Effect != "Static" && show.Effect != "Off";
                if (moving || fresh)
                {
                    Px[][] frame = Frame(show, clock.Elapsed.TotalSeconds, devices.Select(d => d.Colors.Length).ToArray());
                    for (int k = 0; k < devices.Count; k++)
                        sdk.WriteColors(devices[k].Id, frame[k].Take(devices[k].Colors.Length).Select(Wire).ToArray());
                    FramesSent++;
                }
                shown = generation;
                if (reported != generation) { reported = generation; showError = null; FirstFrame.Set(); }
                System.Threading.Thread.Sleep(moving ? 70 : 400);
            }
            catch (Exception error)
            {
                if (!(error is IOException || error is SocketException || error is TimeoutException || error is ObjectDisposedException)) throw;
                if (sdk != null) sdk.Dispose();
                sdk = null;
                if (reported != generation)
                {
                    reported = generation;
                    showError = error is SocketException ? "灯控服务未连接" : error is TimeoutException ? "灯控服务响应超时"
                        : error.Message.StartsWith("未找到") ? error.Message : "灯控通信失败";
                    FirstFrame.Set();
                }
                System.Threading.Thread.Sleep(3000);
            }
        }
    }
}
