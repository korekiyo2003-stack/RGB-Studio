using System;
using System.Collections.Generic;
using System.Management;
using Microsoft.Win32;

public enum MotherboardVendor
{
    Unknown,
    ASRock,
    Asus,
    Msi,
    Gigabyte
}

public sealed class MotherboardInfo
{
    public string Manufacturer { get; set; }
    public string Product { get; set; }
    public MotherboardVendor Vendor { get; set; }
    public string ProtocolName { get; set; }
    public string PrimaryDeviceId { get; set; }
    public string[] SupportedDeviceIds { get; set; }

    public string Summary
    {
        get
        {
            if (Vendor == MotherboardVendor.Unknown)
            {
                return string.Format("主板识别 · {0} {1} (通用 HID)",
                    string.IsNullOrEmpty(Manufacturer) ? "未知主板" : Manufacturer,
                    string.IsNullOrEmpty(Product) ? "" : Product).Trim();
            }
            return string.Format("主板识别 · {0} {1} ({2})",
                Manufacturer, Product, ProtocolName);
        }
    }
}

public static class MotherboardDetector
{
    private static MotherboardInfo current;
    private static readonly object syncRoot = new object();

    public static MotherboardInfo Current
    {
        get
        {
            if (current == null)
            {
                lock (syncRoot)
                {
                    if (current == null) current = Detect();
                }
            }
            return current;
        }
    }

    public static MotherboardInfo Detect()
    {
        string manufacturer = "";
        string product = "";

        // Strategy 1: High-speed Registry read (0ms overhead)
        try
        {
            using (var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS"))
            {
                if (key != null)
                {
                    var mfgObj = key.GetValue("BaseBoardManufacturer");
                    var prodObj = key.GetValue("BaseBoardProduct");
                    if (mfgObj != null) manufacturer = mfgObj.ToString().Trim();
                    if (prodObj != null) product = prodObj.ToString().Trim();
                }
            }
        }
        catch { }

        // Strategy 2: WMI query fallback if registry strings were empty
        if (string.IsNullOrEmpty(manufacturer) || string.IsNullOrEmpty(product))
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        if (string.IsNullOrEmpty(manufacturer) && obj["Manufacturer"] != null)
                            manufacturer = obj["Manufacturer"].ToString().Trim();
                        if (string.IsNullOrEmpty(product) && obj["Product"] != null)
                            product = obj["Product"].ToString().Trim();
                    }
                }
            }
            catch { }
        }

        return Classify(manufacturer, product);
    }

    private static MotherboardInfo Classify(string manufacturer, string product)
    {
        var info = new MotherboardInfo
        {
            Manufacturer = string.IsNullOrEmpty(manufacturer) ? "Unknown" : manufacturer,
            Product = string.IsNullOrEmpty(product) ? "Unknown" : product
        };

        string mfgUpper = info.Manufacturer.ToUpperInvariant();

        if (mfgUpper.Contains("ASROCK"))
        {
            info.Vendor = MotherboardVendor.ASRock;
            info.ProtocolName = "Polychrome USB";
            info.PrimaryDeviceId = "vid_26ce&pid_01a2";
            info.SupportedDeviceIds = new string[] {
                "vid_26ce&pid_01a2",
                "vid_26ce&pid_01a4",
                "vid_26ce&pid_01a6",
                "vid_26ce"
            };
        }
        else if (mfgUpper.Contains("ASUS") || mfgUpper.Contains("ASUSTEK"))
        {
            info.Vendor = MotherboardVendor.Asus;
            info.ProtocolName = "AURA HID";
            info.PrimaryDeviceId = "vid_0b05&pid_1939";
            info.SupportedDeviceIds = new string[] {
                "vid_0b05&pid_1939",
                "vid_0b05&pid_18f3",
                "vid_0b05&pid_1872",
                "vid_0b05"
            };
        }
        else if (mfgUpper.Contains("MSI") || mfgUpper.Contains("MICRO-STAR"))
        {
            info.Vendor = MotherboardVendor.Msi;
            info.ProtocolName = "Mystic Light";
            info.PrimaryDeviceId = "vid_1462&pid_7d25";
            info.SupportedDeviceIds = new string[] {
                "vid_1462&pid_7d25",
                "vid_1462&pid_7c37",
                "vid_1462&pid_7b98",
                "vid_1462"
            };
        }
        else if (mfgUpper.Contains("GIGABYTE") || mfgUpper.Contains("AORUS"))
        {
            info.Vendor = MotherboardVendor.Gigabyte;
            info.ProtocolName = "RGB Fusion";
            info.PrimaryDeviceId = "vid_048d&pid_5702";
            info.SupportedDeviceIds = new string[] {
                "vid_048d&pid_5702",
                "vid_048d&pid_8297",
                "vid_048d"
            };
        }
        else
        {
            info.Vendor = MotherboardVendor.Unknown;
            info.ProtocolName = "Universal HID";
            info.PrimaryDeviceId = "vid_26ce&pid_01a2";
            info.SupportedDeviceIds = new string[] {
                "vid_26ce&pid_01a2",
                "vid_0b05&pid_1939",
                "vid_1462&pid_7d25",
                "vid_048d&pid_5702"
            };
        }

        return info;
    }

    public static bool MatchesDevice(string devicePath)
    {
        if (string.IsNullOrEmpty(devicePath)) return false;

        var currentInfo = Current;
        if (currentInfo.SupportedDeviceIds != null)
        {
            for (int i = 0; i < currentInfo.SupportedDeviceIds.Length; i++)
            {
                if (devicePath.IndexOf(currentInfo.SupportedDeviceIds[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
        }

        // Fallback: check all known vendor IDs
        string[] allKnown = new string[] { "vid_26ce", "vid_0b05", "vid_1462", "vid_048d" };
        for (int i = 0; i < allKnown.Length; i++)
        {
            if (devicePath.IndexOf(allKnown[i], StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }
}
