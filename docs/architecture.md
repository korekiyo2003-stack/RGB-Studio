# RGB Studio Technical Architecture / 系统架构设计

```
                         ┌─────────────────────────┐
                         │   RGB Studio WinForms   │
                         │      (Main UI & Tray)   │
                         └────────────┬────────────┘
                                      │
        ┌─────────────────────────────┼─────────────────────────────┐
        ▼                             ▼                             ▼
┌───────────────┐             ┌───────────────┐             ┌───────────────┐
│  NZXT CAM     │             │ Motherboard   │             │ DDR4/DDR5 RAM │
│  Game Sync    │             │ Direct USB    │             │ OpenRGB SDK   │
└───────┬───────┘             └───────┬───────┘             └───────┬───────┘
        │                             │                             │
  Starts 1x1 Process           Direct HID Packets           Software Clock
  RGB-<slug>.exe               VID:0x26CE PID:0x01A2        Thread (30-60 FPS)
        │                             │                             │
        ▼                             ▼                             ▼
NZXT Smart Device             Onboard Headers               SMBus / ENE Chip
(Chassis Fans:                (CPU Cooler Fan,              (Dual RAM Sticks,
Bottom, Side, Rear)           Heatsink LEDs)                Zero Phase Drift)
```

## Subsystem Details

1. **Host Application (`src/RgbStudio.cs`)**:
   - Manages global state, persistent preferences (`preferences.json`), rotation interval timer (5m, 10m, 15m, 30m, 60m), and system tray integration.
   - Hosts real-time animated chassis simulator canvas (`src/RigPreview.cs`).
   - Hosts the interactive scene customization sidebar (`src/LightingPanel.cs`).

2. **Motherboard Driver (`src/BoardRgb.cs`)**:
   - Zero-overhead native Win32/C# implementation using `SetupDiGetDeviceInterfaceDetail` and `CreateFile` / `FileStream`.
   - Sends 65-byte USB HID report packets directly to the Nuvoton NUC121 microcontroller.
   - Queries hardware register `0x14, 0x01` (installed channels) and `0x14, 0x03` (channel byte swap mask).

3. **RAM Lighting Engine (`src/MemoryLighting.cs` & `src/MemoryShow.cs`)**:
   - High-throughput binary TCP client communicating with OpenRGB SDK protocol (Protocol version 3, Magic `0x4247524F` "ORGB").
   - Implements 14 real-time lighting algorithms: Static, Breath, Flow, Chase, Wave, Twinkle, Bounce, Duet, Heartbeat, Fill, Flash, DoubleBeat, Shimmer, RippleFlash.
   - Employs software gamma correction ($$Level^\gamma$$, default $$\gamma = 2.2$$) for deep, non-washed-out pastel and neon tones.

4. **NZXT CAM Automation Engine (`src/RgbTrigger.cs`)**:
   - Bypasses proprietary or unavailable cloud APIs by exploiting NZXT CAM's native Game Hook engine.
   - Automatically confirms profile application by monitoring CAM's local renderer log asynchronously.
