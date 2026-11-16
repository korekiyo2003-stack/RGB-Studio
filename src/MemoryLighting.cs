using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

public static partial class MemoryLighting
{
    private static readonly object LightingLock = new object();
    internal static string SetFan(string color, byte mode, int speed)
    {
        lock (LightingLock)
        {
            if (!IsHex(color)) return "缺少配色";
            if (System.Diagnostics.Process.GetProcessesByName("AsrPolychromeRGB").Length != 0)
                return "请先退出 Polychrome";

            try
            {
                using (var board = new BoardHid())
                {
                    int channels = board.Exchange(0x14, 1)[4] & 0xFD;
                    int swapped = board.Exchange(0x14, 3)[4];
                    if (channels == 0) return "未找到灯光接口";

                    uint rgb = ParseColor(color);
                    byte red = (byte)((rgb & 255) * 0.60);
                    byte green = (byte)(((rgb >> 8) & 255) * 0.60);
                    byte blue = (byte)(((rgb >> 16) & 255) * 0.60);
                    byte rate = (byte)Math.Max(0, Math.Min(255, speed));
                    for (int channel = 0; channel < 8; channel++)
                    {
                        if ((channels & (1 << channel)) == 0) continue;
                        bool swap = (swapped & (1 << channel)) != 0;
                        byte first = swap ? red : green;
                        byte second = swap ? green : red;
                        board.Exchange(0x10, channel, mode, first, second, blue, rate, 255);
                        byte[] state = board.Exchange(0x11, channel);
                        if (state[4] != mode || state[5] != first || state[6] != second ||
                            state[7] != blue || state[8] != rate)
                            return "灯光接口读回不一致";
                    }
                    return null;
                }
            }
            catch (IOException) { return "灯控通信失败"; }
            catch (DllNotFoundException) { return "灯控组件未安装"; }
            catch (BadImageFormatException) { return "灯控组件架构不匹配"; }
            catch (EntryPointNotFoundException) { return "灯控组件版本不匹配"; }
        }
    }

    private sealed class BoardHid : IDisposable
    {
        private const string HidLibrary = @"C:\Program Files\RGB Studio Memory Backend\hidapi-hotplug.dll";
        private readonly IntPtr handle;

        public BoardHid()
        {
            handle = hid_open(0x26CE, 0x01A2, IntPtr.Zero);
            if (handle == IntPtr.Zero) throw new IOException("ASRock RGB controller unavailable");
            hid_winapi_set_write_timeout(handle, 450);
        }

        public byte[] Exchange(byte command, int channel, params byte[] body)
        {
            var packet = new byte[65];
            packet[1] = command;
            packet[3] = (byte)channel;
            Buffer.BlockCopy(body, 0, packet, 4, body.Length);
            if (hid_write(handle, packet, (UIntPtr)packet.Length) != packet.Length)
                throw new IOException("ASRock RGB write failed");

            var response = new byte[65];
            for (int attempt = 0; attempt < 3; attempt++)
            {
                int count = hid_read_timeout(handle, response, (UIntPtr)response.Length, 450);
                if (count < 0) break;
                // This firmware acknowledges a write with the previous channel number.
                // The following read is what verifies the requested channel.
                // The first byte is not checked: it was 0xA6 on 2026-11-12 and 0x00 after the reboot on
                // 2026-11-13 (measured with the same library), while channel and data stayed valid.
                if (count >= 10 && (command == 0x10 || response[3] == channel))
                    return response;
            }
            throw new IOException("ASRock RGB response missing");
        }

        public void Dispose() { hid_close(handle); }

        [DllImport(HidLibrary, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr hid_open(ushort vendor, ushort product, IntPtr serial);
        [DllImport(HidLibrary, CallingConvention = CallingConvention.Cdecl)]
        private static extern int hid_write(IntPtr device, byte[] data, UIntPtr length);
        [DllImport(HidLibrary, CallingConvention = CallingConvention.Cdecl)]
        private static extern int hid_read_timeout(IntPtr device, [Out] byte[] data, UIntPtr length, int timeout);
        [DllImport(HidLibrary, CallingConvention = CallingConvention.Cdecl)]
        private static extern void hid_close(IntPtr device);
        [DllImport(HidLibrary, CallingConvention = CallingConvention.Cdecl)]
        private static extern int hid_winapi_set_write_timeout(IntPtr device, uint timeout);
    }

    // mode is an OpenRGB ENE mode name: Direct, Off, Static, Breathing, Flashing, Chase Fade, Chase.
    // The effect runs inside the ENE chip on each stick (RGBController_ENESMBus::DeviceUpdateMode writes
    // the mode, speed and direction registers), so nothing is sent again until the next scene.
    // speed: 4 slowest .. 0 fastest (ENE_SPEED_*). Returns null on success, otherwise what went wrong.
    public static string Apply(string[] sceneColors, string mode, int speed, bool reverse)
    {
        lock (LightingLock)
        {
            if (System.Diagnostics.Process.GetProcessesByName("AsrPolychromeRGB").Length != 0)
                return "请先退出 Polychrome";

            try
            {
                if (sceneColors == null || sceneColors.Length != 4)
                    return "缺少四点配色";

                uint[] points = sceneColors.Select(hex => ParseColor(Vivid(hex))).ToArray();
                using (var sdk = new Sdk())
                {
                    var devices = sdk.MemoryDevices();
                    if (devices.Count != 2)
                        return "只找到 " + devices.Count + "/2 条";

                    var jobs = new List<KeyValuePair<Device, uint[]>>();
                    foreach (Device device in devices)
                    {
                        if (device.Modes.All(m => m.Name != mode)) return "不支持「" + mode + "」";
                        jobs.Add(new KeyValuePair<Device, uint[]>(device, Enumerable.Range(0, device.Colors.Length)
                            .Select(i => Blend(points, i, device.Colors.Length)).ToArray()));
                    }
                    // Colours first, then the mode switch on both sticks back to back, so their
                    // effects start within a few milliseconds of each other.
                    if (mode == "Direct")
                    {
                        foreach (var job in jobs)
                        {
                            if (job.Key.ActiveMode != "Direct") sdk.Send(job.Key.Id, 1100, new byte[0]);
                            sdk.WriteColors(job.Key.Id, job.Value);
                        }
                    }
                    else
                    {
                        foreach (var job in jobs) sdk.SetMode(job.Key, mode, speed, reverse);
                        if (mode != "Off")
                        {
                            foreach (var job in jobs) sdk.WriteColors(job.Key.Id, job.Value);
                            foreach (var job in jobs) sdk.SetMode(job.Key, mode, speed, reverse);
                        }
                    }
                    foreach (var job in jobs)
                    {
                        Device updated = sdk.ReadDevice(job.Key.Id);
                        if (updated.ActiveMode != mode || (mode != "Off" && !updated.Colors.SequenceEqual(job.Value)))
                            return "灯控状态未确认";
                    }
                }
                return null;
            }
            catch (SocketException) { return "灯控服务未连接"; }
            catch (IOException) { return "灯控通信失败"; }
            catch (TimeoutException) { return "灯控服务响应超时"; }
            catch (FormatException) { return "配色格式错误"; }
        }
    }

    // LEDs are linear while the scene colours are sRGB values picked on a monitor, so a pastel such as
    // A06BFF comes out nearly white on the stick. Gamma 2.2 relative to the strongest channel restores
    // the hue and keeps the brightness (the usual LED gamma correction, see Adafruit's "LED Tricks:
    // Gamma Correction"; grey and white are unchanged).
    internal static string Vivid(string hex)
    {
        if (!IsHex(hex)) return hex;
        int value = int.Parse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        int[] c = { (value >> 16) & 255, (value >> 8) & 255, value & 255 };
        double max = c.Max();
        if (max == 0) return hex;
        return String.Concat(c.Select(x => ((int)Math.Round(Math.Pow(x / max, 2.2) * max)).ToString("X2")));
    }

    private static uint ParseColor(string hex)
    {
        if (hex == null || hex.Length != 6)
            throw new FormatException();
        uint rgb = uint.Parse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        return ((rgb >> 16) & 255) | (rgb & 0xFF00) | ((rgb & 255) << 16);
    }

    private static uint Blend(uint[] points, int index, int count)
    {
        double position = count <= 1 ? 0 : 3.0 * index / (count - 1);
        int start = Math.Min(2, (int)position);
        double fraction = position - start;
        uint color = 0;
        for (int shift = 0; shift < 24; shift += 8)
        {
            uint a = (points[start] >> shift) & 255;
            uint b = (points[start + 1] >> shift) & 255;
            color |= (uint)Math.Round(a * (1 - fraction) + b * fraction) << shift;
        }
        return color;
    }

    internal static bool IsHex(string value)
    {
        return value != null && value.Length == 6 && value.All(Uri.IsHexDigit);
    }

    private sealed class Device
    {
        public uint Id;
        public int Type;
        public string Name;
        public string ActiveMode;
        public uint[] Colors;
        public List<Mode> Modes = new List<Mode>();
    }

    // One entry of the SDK's Mode Data block (protocol 3): name, twelve 4-byte fields, colours.
    // Field order: value, flags, speed_min, speed_max, brightness_min, brightness_max, colors_min,
    // colors_max, speed, brightness, direction, color_mode.
    private sealed class Mode
    {
        public string Name;
        public int[] Fields;
        public uint[] Colors;
    }

    private sealed class Sdk : IDisposable
    {
        private const uint Protocol = 3;
        private const uint Magic = 0x4247524F;
        private readonly TcpClient client = new TcpClient();
        private readonly BinaryReader reader;
        private readonly BinaryWriter writer;

        public Sdk()
        {
            try
            {
                var connection = client.BeginConnect(IPAddress.Loopback, 6743, null, null);
                using (var pending = connection.AsyncWaitHandle)
                    if (!pending.WaitOne(700)) throw new TimeoutException();
                client.EndConnect(connection);
                client.ReceiveTimeout = 1600;
                client.SendTimeout = 1600;
                reader = new BinaryReader(client.GetStream());
                writer = new BinaryWriter(client.GetStream());
                Send(0, 40, BitConverter.GetBytes(Protocol));
                if (BitConverter.ToUInt32(Receive(0, 40), 0) < Protocol)
                    throw new IOException("Unsupported OpenRGB protocol");
            }
            catch
            {
                client.Close();
                throw;
            }
        }

        public List<Device> MemoryDevices()
        {
            Send(0, 0, new byte[0]);
            uint count = BitConverter.ToUInt32(Receive(0, 0), 0);
            if (count > 16) throw new IOException("Unexpected lighting controllers");
            var result = new List<Device>();
            for (uint id = 0; id < count; id++)
            {
                Device device = ReadDevice(id);
                if (device.Type == 1 && device.Name == "ENE DRAM" && device.Colors.Length > 0)
                    result.Add(device);
            }
            return result;
        }

        public Device ReadDevice(uint id)
        {
            Send(id, 1, BitConverter.GetBytes(Protocol));
            using (var data = new BinaryReader(new MemoryStream(Receive(id, 1))))
            {
                data.ReadUInt32();
                var device = new Device { Id = id, Type = data.ReadInt32(), Name = ReadString(data) };
                for (int i = 0; i < 5; i++) ReadString(data);
                int modes = data.ReadUInt16();
                int active = data.ReadInt32();
                for (int i = 0; i < modes; i++)
                {
                    var mode = new Mode { Name = ReadString(data), Fields = new int[12] };
                    if (i == active) device.ActiveMode = mode.Name;
                    for (int f = 0; f < 12; f++) mode.Fields[f] = data.ReadInt32();
                    mode.Colors = new uint[data.ReadUInt16()];
                    for (int c = 0; c < mode.Colors.Length; c++) mode.Colors[c] = data.ReadUInt32();
                    device.Modes.Add(mode);
                }
                int zones = data.ReadUInt16();
                for (int i = 0; i < zones; i++)
                {
                    ReadString(data);
                    Skip(data, 16);
                    Skip(data, data.ReadUInt16());
                }
                int leds = data.ReadUInt16();
                for (int i = 0; i < leds; i++)
                {
                    ReadString(data);
                    data.ReadUInt32();
                }
                int colors = data.ReadUInt16();
                if (colors != leds || colors > 512) throw new IOException("Unexpected LED count");
                device.Colors = new uint[colors];
                for (int i = 0; i < colors; i++) device.Colors[i] = data.ReadUInt32();
                return device;
            }
        }

        public void WriteColors(uint id, uint[] colors)
        {
            using (var payload = new MemoryStream())
            using (var data = new BinaryWriter(payload))
            {
                data.Write(6 + 4 * colors.Length);
                data.Write((ushort)colors.Length);
                foreach (uint color in colors) data.Write(color);
                Send(id, 1050, payload.ToArray());
            }
        }

        // NET_PACKET_ID_RGBCONTROLLER_UPDATEMODE (1101): data_size, mode_idx, Mode Data.
        public void SetMode(Device device, string name, int speed, bool reverse)
        {
            int index = device.Modes.FindIndex(m => m.Name == name);
            Mode mode = device.Modes[index];
            int[] fields = (int[])mode.Fields.Clone();
            if ((fields[1] & 1) != 0) fields[8] = Math.Max(0, Math.Min(4, speed));
            if ((fields[1] & 2) != 0) fields[10] = reverse ? 1 : 0;
            if ((fields[1] & 0x20) != 0) fields[11] = 1;
            using (var payload = new MemoryStream())
            using (var data = new BinaryWriter(payload))
            {
                byte[] text = Encoding.UTF8.GetBytes(mode.Name + "\0");
                data.Write(0);
                data.Write(index);
                data.Write((ushort)text.Length);
                data.Write(text);
                foreach (int field in fields) data.Write(field);
                data.Write((ushort)mode.Colors.Length);
                foreach (uint color in mode.Colors) data.Write(color);
                data.Flush();
                byte[] bytes = payload.ToArray();
                BitConverter.GetBytes(bytes.Length).CopyTo(bytes, 0);
                Send(device.Id, 1101, bytes);
            }
        }

        public void Send(uint id, uint command, byte[] data)
        {
            writer.Write(Magic);
            writer.Write(id);
            writer.Write(command);
            writer.Write(data.Length);
            writer.Write(data);
            writer.Flush();
        }

        private byte[] Receive(uint id, uint command)
        {
            for (int attempt = 0; attempt < 24; attempt++)
            {
                if (reader.ReadUInt32() != Magic) throw new IOException("Invalid OpenRGB header");
                uint responseId = reader.ReadUInt32();
                uint responseCommand = reader.ReadUInt32();
                int size = reader.ReadInt32();
                if (size < 0 || size > 1024 * 1024) throw new IOException("Invalid OpenRGB packet size");
                byte[] payload = reader.ReadBytes(size);
                if (payload.Length != size || responseCommand == 100)
                    throw new IOException("Lighting device list changed");
                if (responseId == id && responseCommand == command) return payload;
            }
            throw new IOException("Missing OpenRGB response");
        }

        private static string ReadString(BinaryReader data)
        {
            int length = data.ReadUInt16();
            byte[] value = data.ReadBytes(length);
            if (value.Length != length) throw new EndOfStreamException();
            return Encoding.UTF8.GetString(value).TrimEnd('\0');
        }

        private static void Skip(BinaryReader data, int length)
        {
            if (data.BaseStream.Seek(length, SeekOrigin.Current) > data.BaseStream.Length)
                throw new EndOfStreamException();
        }

        public void Dispose() { client.Close(); }
    }
}
