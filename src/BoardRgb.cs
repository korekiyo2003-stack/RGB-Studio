using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

internal static class BoardRgb
{
    private const int ConnectorMask = 0x8D;
    private const int OnboardMask = 0x70;
    private const int ReadHeader = 0x14;
    private const int ReadZone = 0x11;
    private const int SetZone = 0x10;

    // The RGB controller protocol exposes runtime color and configuration readback.
    // Reference: OpenRGB, Controllers/ASRockPolychromeUSBController.
    public static string Apply(string hex, bool breathing = false, int speed = 128)
    {
        if (String.IsNullOrEmpty(hex)) return "主板灯光 · 无配色";
        // Polychrome polls this controller all the time and re-applies its own state. Its replies also land in our
        // read queue, so nothing we read back can be trusted while it runs.
        if (System.Diagnostics.Process.GetProcessesByName("AsrPolychromeRGB").Length > 0)
            return "散热器未切换 · Polychrome 开着时主板灯归它管，关掉它再点应用";
        using (var handle = Open())
        {
            if (handle == null) return string.Format("{0} · 控制器不可用", MotherboardDetector.Current.ProtocolName);
            using (var port = new FileStream(handle, FileAccess.ReadWrite, 65, true))
            {
                var present = Exchange(port, ReadHeader, 1)[4];
                var swapped = Exchange(port, ReadHeader, 3)[4];
                int enabled = present & (ConnectorMask | OnboardMask);
                byte mode = breathing ? (byte)2 : (byte)1;
                var color = ColorTranslator.FromHtml("#" + hex);
                var checks = 0;
                for (int zone = 0; zone < 8; zone++)
                {
                    if ((enabled & (1 << zone)) == 0) continue;
                    bool swap = (swapped & (1 << zone)) != 0;
                    byte r = (byte)(color.R * 0.60);
                    byte g = (byte)(color.G * 0.60);
                    byte b = (byte)(color.B * 0.60);
                    byte first = swap ? r : g;
                    byte second = swap ? g : r;
                    Exchange(port, SetZone, zone, mode, first, second, b, (byte)Math.Max(0, Math.Min(255, speed)), 255);
                    byte[] state = Exchange(port, ReadZone, zone);
                    if (state[3] == zone && state[4] == mode && state[5] == first &&
                        state[6] == second && state[7] == b) checks |= 1 << zone;
                }
                if (enabled == 0 || checks != enabled) return string.Format("{0} · 已发送但读回不一致", MotherboardDetector.Current.ProtocolName);
                return breathing ? string.Format("{0} · 呼吸已验证", MotherboardDetector.Current.ProtocolName) : string.Format("{0} · 常亮已验证", MotherboardDetector.Current.ProtocolName);
            }
        }
    }

    private static byte[] Exchange(FileStream port, int command, int address, params byte[] body)
    {
        var output = new byte[65];
        output[1] = (byte)command;
        output[3] = (byte)address;
        Buffer.BlockCopy(body, 0, output, 4, body.Length);
        var write = port.WriteAsync(output, 0, output.Length);
        if (!write.Wait(450)) throw new IOException("ASRock write timeout");
        port.Flush();
        // Skip replies left over from earlier requests: the answer to this one carries the same address.
        for (int attempt = 0; attempt < 6; attempt++)
        {
            var input = new byte[65];
            var read = port.ReadAsync(input, 0, input.Length);
            if (!read.Wait(450)) throw new IOException("ASRock read timeout");
            if (read.Result > 10 && input[0] == 0 && input[1] == 0xA6)
                Buffer.BlockCopy(input, 1, input, 0, read.Result - 1);
            if (read.Result >= 10 && input[0] == 0xA6 && input[3] == (byte)address) return input;
        }
        throw new IOException("ASRock invalid response");
    }

    private static SafeFileHandle Open()
    {
        Guid hid;
        HidD_GetHidGuid(out hid);
        IntPtr devices = SetupDiGetClassDevs(ref hid, IntPtr.Zero, IntPtr.Zero, 0x12);
        if (devices == new IntPtr(-1)) return null;
        try
        {
            for (uint i = 0; ; i++)
            {
                var entry = new InterfaceData { Size = Marshal.SizeOf(typeof(InterfaceData)) };
                if (!SetupDiEnumDeviceInterfaces(devices, IntPtr.Zero, ref hid, i, ref entry)) break;
                int required;
                SetupDiGetDeviceInterfaceDetail(devices, ref entry, IntPtr.Zero, 0, out required, IntPtr.Zero);
                if (required < 8) continue;
                IntPtr detail = Marshal.AllocHGlobal(required);
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(devices, ref entry, detail, required, out required, IntPtr.Zero)) continue;
                    string path = Marshal.PtrToStringUni(IntPtr.Add(detail, 4));
                    if (!MotherboardDetector.MatchesDevice(path)) continue;
                    var handle = CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
                    if (!handle.IsInvalid) return handle;
                    handle.Dispose();
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(devices); }
        return null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InterfaceData { public int Size; public Guid Class; public int Flags; public IntPtr Reserved; }

    [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid guid, IntPtr enumerator, IntPtr owner, uint flags);
    [DllImport("setupapi.dll")]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid guid, uint index, ref InterfaceData data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref InterfaceData data, IntPtr detail,
        int detailSize, out int required, IntPtr info);
    [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint sharing, IntPtr security,
        uint creation, uint flags, IntPtr template);
}
