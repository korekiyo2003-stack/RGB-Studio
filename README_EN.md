# RGB Studio Manual: Architecture, Wiring Schemes, and Debugging

**RGB Studio** is a unified, lightweight, high-performance PC lighting orchestrator engineered to bridge fragmented RGB/ARGB hardware ecosystems. Without running bloated vendor suites, RGB Studio seamlessly synchronizes chassis fans (NZXT CAM), motherboard ARGB/RGB headers (ASRock, ASUS, MSI, Gigabyte), and dual-channel DDR4/DDR5 memory modules in a unified desktop interface with real-time per-LED animation previews and 100 handcrafted design presets.

---

## Table of Contents
1. [Key Features](#1-key-features)
2. [Quickstart & Portable Execution](#2-quickstart--portable-execution)
3. [User Interface & Operation](#3-user-interface--operation)
4. [Hardware Header Standards & Motherboard Solutions](#4-hardware-header-standards--motherboard-solutions)
5. [Debugging & Troubleshooting Guide](#5-debugging--troubleshooting-guide)
6. [Building from Source](#6-building-from-source)
7. [Deep Dive: Low-Level Hardware Protocol & Engineering Notes](#7-deep-dive-low-level-hardware-protocol--engineering-notes)

---

## 1. Key Features

* **Universal Multi-Vendor Synchronization**:
  * **Chassis Fan Arrays (Bottom, Side, Rear)**: Controlled via lightweight, zero-overhead process triggers hooked directly into **NZXT CAM**.
  * **Motherboard RGB/ARGB Headers (CPU Air/AIO Cooler, Motherboard Shrouds)**: Low-level native USB HID protocol driving onboard microcontrollers (**ASRock, ASUS, MSI, Gigabyte**).
  * **Dual-Channel DDR4 / DDR5 Memory**: Powered by OpenRGB SDK with an internal high-precision software clock engine (`MemoryShow.cs`) that completely eliminates dual-DIMM hardware timer drift.
* **100 Handcrafted Lighting Scenes**: Organized into curated aesthetic palettes (Rhythm, Cool, Pink-Purple, Cyan-Green, Warm, Neutral). Supports gradients, breathing, pulse, strobe, alternating colors, and fixed lighting.
* **Interactive Dynamic Rig Canvas**: Double-buffered GDI+ vector rendering of your PC chassis, rendering individual fan LEDs, cooler fans, and dual RAM sticks in real-time at 30–60 FPS.
* **Inline Customization Panel**: Modify CPU cooler fan effects, select color harmony groups (Scene Original, Unified, Accent, Soft, Custom), and pick custom Hex swatches on the fly.
* **Automated Rotation Engine**: Built-in 5, 10, 15, 30, and 60-minute non-repeating shuffle rotation, customizable rotation pool, and system tray integration.

---

## 2. Quickstart & Portable Execution

### System Requirements
* Operating System: Windows 10 / Windows 11 (64-bit).
* Runtime: .NET Framework 4.5 or newer (pre-installed on Windows 10 and 11).
* External Services (Optional based on hardware):
  * **NZXT CAM**: Required if controlling fans plugged into an NZXT Smart Device / Controller.
  * **OpenRGB Server**: Required if controlling DDR4/DDR5 RAM RGB.

### Running RGB Studio
1. Clone or download the `release/` directory from this repository.
2. Ensure the following files are present:
   * `RGB Studio.exe`
   * `MemoryLighting.dll`
   * `scenes.json`
   * `Triggers/` folder (containing all `RGB-*.exe` binaries).
3. Double-click `RGB Studio.exe` to launch.

---

## 3. User Interface & Operation

```
┌────────────────────────────────────────────────────────────────────────┐
│  RGB Studio               100 Themes · 6 Modes          [Pause] [Shuffle]│
├──────────────────────────────────────┬─────────────────────────────────┤
│ [All Themes  ▼]                      │  【Dynamic Rig Preview】         │
│ ┌──────────────────────────────────┐ │  ┌───────────────────────────┐  │
│ │ Neon Rails  /  Rhythm  [✓] [Apply│ │  │   [Animated Chassis Canvas]   │  │
│ │ [Bottom: Fade] [Side: Pulse]     │ │  └───────────────────────────┘  │
│ ├──────────────────────────────────┤ │  Cooler · Breathing Verified    │
│ │ Glacier     /  Cool    [✓] [Apply│ │  CPU Fan Effect   Fan & RAM Color│
│ │ [Bottom: Fade] [Side: Breath]    │ │  [Soft Breath ▼]  [Accent Pop  ▼]│
│ └──────────────────────────────────┘ │  [Color1][Color2] [#00F0FF][OK] │
│                                      │  [       Save & Apply        ]  │
└──────────────────────────────────────┴─────────────────────────────────┘
```

### 3.1. Selecting and Applying Scenes
* **Manual Application**: Browse presets on the left panel (filter by theme group). Click **"Apply"** on any scene card.
  * NZXT CAM applies the corresponding master fan profile.
  * Motherboard CPU cooler updates color and breathing/static mode.
  * RAM sticks immediately begin synchronized multi-point gradient playback.
* **Timed Shuffle**: Toggle **"Start/Pause Rotation"** at the top bar. Choose your interval (5 to 60 minutes). Check or uncheck **"Include in Rotation"** checkboxes to customize your rotation pool.

### 3.2. Customizing Hardware Colors & Effects
* Selecting a scene populates the right-hand editor:
  * **CPU Fan Effect**: Recommended, Static, Soft Breath, Slow Flash, Beat Flash, Alternate, Off.
  * **Color Harmonies**: Scene Original, Unified, Accent Pop, Soft Pastel, Custom.
  * **Hex Color Input**: Click on swatches to input specific Hex codes (e.g. `#FF0055`).
  * Click **"Save and Apply"** to write changes to physical hardware and save preferences.

---

## 4. Hardware Header Standards & Motherboard Solutions

```
       5V 3-Pin ARGB (Addressable)              12V 4-Pin RGB (Non-Addressable)
      ┌───┬───┬───┬───┐                              ┌───┬───┬───┬───┐
      │ 1 │ 2 │ X │ 3 │                              │ 1 │ 2 │ 3 │ 4 │
      └───┴───┴───┴───┘                              └───┴───┴───┴───┘
     +5V  DATA KEY GND                              +12V  G   R   B
    (Pin 3 physically blocked)                    (All LEDs same color)
```

### 4.1. Electrical Header Comparison
| Standard | Operating Voltage | Protocol | Max Rating | Characteristics |
| :--- | :--- | :--- | :--- | :--- |
| **5V 3-Pin ARGB** | **+5V DC** | WS2812B / SK6812 NRZ Serial (800 kHz) | **3.0A / 15W** (~50–60 LEDs) | Individual per-LED color, chasing, and waves |
| **12V 4-Pin RGB** | **+12V DC** | Analog PWM Ground-switching MOSFETs | **2.0A–3.0A / 24W–36W** | Entire strip shares identical color simultaneously |

> [!CAUTION]
> **FATAL HARDWARE RISK**: Never force a 3-pin 5V ARGB connector into a 4-pin 12V RGB header. The 12V supply line will destroy the 5V logic chips and damage motherboard traces!

### 4.2. Motherboard Connection Architectures

#### 1. ASRock Motherboards (Native USB HID)
* **Headers**: `ADDR_LED1`, `ADDR_LED2`, `ADDR_LED3` (5V 3-Pin); `RGB_LED1` (12V 4-Pin).
* **Chipset**: Nuvoton NUC121 USB microcontroller (`VID 0x26CE, PID 0x01A2`).
* **Connection**: Plug CPU cooler ARGB into `ADDR_LED1`.
* **Advantage**: RGB Studio communicates directly via native HID packets. **ASRock Polychrome software must be closed** to prevent bus contention.

#### 2. ASUS Motherboards (ROG / TUF / Prime)
* **Headers**: `ADD_GEN 2_1`, `ADD_GEN 2_2` (5V 3-Pin); `AURA_RGB` (12V 4-Pin).
* **Setup**: Connect cooler to `ADD_GEN 2`. Managed through OpenRGB backend server on port 6743. Ensure ASUS Armoury Crate / LightingService is closed.

#### 3. MSI Motherboards (MPG / MAG / MEG / PRO)
* **Headers**: `JARGB_V2_1`, `JARGB_V2_2` (5V 3-Pin); `JRGB1` (12V 4-Pin).
* **Setup**: Connect to `JARGB_V2`. Managed via OpenRGB backend. Ensure MSI Center / Mystic Light is closed.

#### 4. Gigabyte / AORUS Motherboards
* **Headers**: `D_LED1`, `D_LED2` (5V 3-Pin); `LED_C1`, `LED_C2` (12V 4-Pin).
* **Warning**: On older motherboards, verify that the 5V/12V selector jumper is positioned at **5V**. Managed via OpenRGB backend.

### 4.3. High Fan Count & Powered ARGB Hubs (SATA Powered)
* Single motherboard ARGB headers are rated for 3A (15W). Daisy-chaining more than 4–5 fans risks pulling excessive current through motherboard traces.
* **Recommended Setup**: Use a **SATA-powered ARGB Splitter Hub**:
  * Hub Signal Input -> Motherboard `ADDR_LED` header (carries only Data & Ground).
  * Hub Power Input -> Direct SATA power from PSU (supplies clean 5V up to 4.5A).

### 4.4. Mitigating Dual-DIMM Memory Drift
* DDR4 and DDR5 memory modules (XPG, Corsair, G.Skill, Kingston) operate on internal hardware clocks that drift apart within minutes.
* RGB Studio locks both DIMMs into **Direct Mode** and broadcasts synchronized color frames from a central software clock thread (`MemoryShow.cs`), guaranteeing permanent phase synchronization.

---

## 5. Debugging & Troubleshooting Guide

| Issue | Root Cause | Solution |
| :--- | :--- | :--- |
| **Cooler · Controller not responding** | Motherboard software (Polychrome, Armoury Crate, MSI Center) locking USB HID device | Close all vendor RGB software in system tray and task manager. In RGB Studio, click **"Save and Apply"**. |
| **Memory · Service not connected** | OpenRGB server not running on port 6743 | Start OpenRGB server: `OpenRGB.exe --server -p 6743` or test with `python scripts/sync_check.py`. |
| **CAM did not confirm scene** | NZXT CAM closed or `Triggers` folder missing | Launch NZXT CAM. Verify `release/Triggers/` exists with `RGB-*.exe` files. If needed, run `python scripts/repair_cam_lighting.py`. |
| **Inverted Colors (Red appears Green)** | LED strip uses GRB byte order instead of RGB | ASRock motherboards are automatically corrected by register readback. For other boards, set color order in OpenRGB device settings. |

---

## 6. Building from Source

RGB Studio requires zero heavy SDKs and compiles in seconds using the Windows native .NET C# compiler:

```cmd
:: Quick build
build.bat

:: Direct compiler invocation
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /optimize+ /out:"release\RGB Studio.exe" /reference:System.dll,System.Core.dll,System.Drawing.dll,System.Windows.Forms.dll,System.Web.Extensions.dll src\BoardRgb.cs src\LightingPanel.cs src\MemoryLighting.cs src\MemoryShow.cs src\RgbStudio.cs src\RigPreview.cs src\SceneCard.cs
```
The compiled output is saved to `release/RGB Studio.exe`.

---

## 7. Deep Dive: Low-Level Hardware Protocol & Engineering Notes

### 7.1. ASRock Polychrome USB HID Protocol (VID 0x26CE / PID 0x01A2)
RGB Studio interfaces directly with the onboard Nuvoton/ASRock USB microcontroller (`vid_26ce&pid_01a2`) via Win32 SetupDi device enumeration and unbuffered `CreateFile` file streams. All communications adhere to a custom 65-byte USB report structure:

```text
+----------+-------------------------------------------------------------+
| Offset   | Field Description & Protocol Semantics                     |
+----------+-------------------------------------------------------------+
| Byte 0   | 0x00: USB HID Report ID                                    |
| Byte 1   | Command: 0x10 = Set Zone, 0x11 = Read Zone, 0x14 = Header   |
| Byte 2   | Sub-command / Reserved (0x00)                               |
| Byte 3   | Target Zone Index (0x00 - 0x07: ARGB Header, RGB, PCH, IO) |
| Byte 4   | Mode: 0x01 = Static, 0x02 = Breathing                      |
| Byte 5   | Primary Color Component (G or R depending on GRB/RGB swap)  |
| Byte 6   | Secondary Color Component (R or G)                          |
| Byte 7   | Blue Color Component (B)                                    |
| Byte 8   | Speed / Animation Rate (0-255, default 128)                 |
| Byte 9   | Brightness Level (0-255, fixed to 255)                      |
| Byte 10+ | Zero padding up to 65 bytes                                 |
+----------+-------------------------------------------------------------+
```

### 7.2. Defensive Programming & Bus Integrity
1. **FIFO Drain Loop (Residual Response Skipping)**:
   USB HID read queues frequently buffer stale responses from preceding transactions or external bus queries. `BoardRgb.cs` includes a 6-attempt drain loop with 450ms timeouts:
   ```csharp
   for (int attempt = 0; attempt < 6; attempt++) {
       var input = new byte[65];
       var read = port.ReadAsync(input, 0, input.Length);
       if (!read.Wait(450)) throw new IOException("ASRock read timeout");
       // Align frame if Report ID is stripped by host controller driver
       if (read.Result > 10 && input[0] == 0 && input[1] == 0xA6)
           Buffer.BlockCopy(input, 1, input, 0, read.Result - 1);
       if (read.Result >= 10 && input[0] == 0xA6 && input[3] == (byte)address)
           return input;
   }
   ```
2. **Preemptive Process Conflict Quarantine**:
   Vendor software like `AsrPolychromeRGB.exe` runs high-frequency background polling loops. Concurrent reads corrupt the shared USB endpoint's FIFO buffer. RGB Studio inspects the active process table (`Process.GetProcessesByName`) and alerts the user rather than corrupting device state.
3. **Roundtrip Write Verification**:
   Every `SetZone` (command `0x10`) is followed by an immediate `ReadZone` (`0x11`) command. The returned payload is compared byte-by-byte with expected RGB/mode values. Any divergence triggers an explicit warning (`"散热器 · 已发送但读回不一致"`), guaranteeing that configuration states in UI strictly match physical microcontroller registers.

### 7.3. Why Not Just Rely on OpenRGB?
While OpenRGB is effective for device detection, RGB Studio employs a hybrid design:
* **Deterministic Motherboard I/O**: Direct Win32 SetupDi implementation avoids OpenRGB SDK IPC overhead, adds active process conflict mitigation, and enforces roundtrip readback verification.
* **Dual-Channel DRAM Phase Locking**: OpenRGB applies colors asynchronously to RAM sticks. Because dual DIMMs have separate hardware crystal oscillators, their visual phases drift out of sync within minutes. `MemoryShow.cs` runs a master software clock with continuous phase resynchronization to keep both sticks aligned.
