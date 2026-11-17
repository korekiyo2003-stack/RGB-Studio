# RGB Studio

<div align="center">

**Universal PC Lighting Synchronizer & Preset Orchestrator**  
*NZXT CAM · ASRock Polychrome USB · Dual-Channel DDR4/DDR5 RAM (OpenRGB SDK)*

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Platform: Windows](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011%20(64--bit)-lightgrey.svg)](release/)
[![Scenes: 100 Handcrafted Presets](https://img.shields.io/badge/Scenes-100%20Presets-gradient.svg)](presets/scenes.json)

[**简体中文 说明文档**](README_CN.md) • [**Русская инструкция**](README_RU.md) • [**English Manual**](README_EN.md)

[**Hardware Wiring Guide**](docs/hardware_wiring_guide.md) • [**Debugging Guide**](docs/debugging_guide.md) • [**Architecture**](docs/architecture.md)

</div>

---

## Overview / 概览 / Обзор

**RGB Studio** is a unified, lightweight, and bloat-free desktop control hub for PC enthusiasts. It bridges fragmented vendor lighting ecosystems into a single elegant interface:
* **Chassis Fans (Bottom, Side, Rear)**: Controlled via zero-overhead process triggers hooked into **NZXT CAM**.
* **Motherboard RGB/ARGB Headers (CPU Air/AIO Cooler, LED Shrouds)**: Low-level native USB HID protocol driving onboard microcontrollers (**ASRock Polychrome USB**, VID 26CE / PID 01A2). Other vendors (ASUS AURA, MSI Mystic Light, Gigabyte RGB Fusion) are detected and shown in the status line but are **not driven** yet; use OpenRGB for them.
* **Dual-Channel Memory (DDR4 / DDR5)**: Synchronous high-precision software clock engine over OpenRGB SDK, eliminating dual-DIMM crystal oscillator timer drift.
* **100 Curated Design Scenes**: Handcrafted color harmonies across 6 theme categories (Rhythm, Cool, Pink-Purple, Cyan-Green, Warm, Neutral).
* **Real-time Animated Canvas**: Dynamic GDI+ vector chassis simulator showing per-LED wave, pulse, breathing, and chasing patterns.

```
       5V 3-Pin ARGB (Addressable)              12V 4-Pin RGB (Non-Addressable)
      ┌───┬───┬───┬───┐                              ┌───┬───┬───┬───┐
      │ 1 │ 2 │ X │ 3 │                              │ 1 │ 2 │ 3 │ 4 │
      └───┴───┴───┴───┘                              └───┴───┴───┴───┘
     +5V  DATA KEY GND                              +12V  G   R   B
    (Pin 3 physically blocked)                    (All LEDs same color)
```

---

## Quickstart / 快速开始 / Быстрый старт

### Ready-to-Run (No Installation Required)
Precompiled binaries and scene triggers are available in the [`release/`](release/) folder:
1. Ensure .NET Framework 4.5+ is enabled (built-in on Windows 10/11).
2. Download or clone this repository.
3. Open `release/` and launch `RGB Studio.exe`.

### Build from Source
RGB Studio uses standard .NET C# compiler with no external toolchains:
```cmd
build.bat
```
The compiled standalone binary is placed in `release/RGB Studio.exe`.

---

## Deep Dive: Low-Level Hardware Protocol & Engineering Notes

### 1. ASRock Polychrome USB HID Protocol (VID 0x26CE / PID 0x01A2)
RGB Studio communicates directly with the onboard Nuvoton/ASRock USB microcontroller over a 65-byte raw USB HID report descriptor rather than relying on high-overhead SDK wrappers:

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
| Byte 10+ | Zero padding to 65 bytes                                    |
+----------+-------------------------------------------------------------+
```

### 2. Defensive Engineering & Bus Integrity
* **FIFO Drain & Residual Frame Skipping**: USB HID read buffers frequently contain stale responses from previous transactions. A loop (up to 6 iterations with 450ms timeouts) drains mismatched packets until the header `0xA6` with matching target address is acknowledged.
* **Process Conflict Quarantine**: ASRock's official `AsrPolychromeRGB.exe` daemon constantly polls and overrides the controller state, polluting the shared HID read queue. RGB Studio proactively checks active process tables and halts writes to prevent bus contention.
* **Post-Write Readback Verification**: After writing zone configurations via `0x10`, RGB Studio executes a roundtrip `0x11` query to verify target bytes in hardware registers (`state[5..7] == target_rgb`), ensuring zero packet drops under high I/O load.

### 3. Why Not Just Rely on OpenRGB?
While OpenRGB offers broad device support, its standard driver model lacks:
1. **Host process concurrency guards**: OpenRGB collides with running vendor daemons like `AsrPolychromeRGB.exe`, causing queue corruption.
2. **Deterministic write verification**: It assumes USB HID writes always succeed, leaving dropped frames unhandled.
3. **Dual-Channel DRAM Clock Drift**: Dual DIMMs have independent crystal oscillators; asynchronous updates cause lighting phase drift within minutes. RGB Studio's `MemoryShow.cs` runs a master software clock with continuous phase resynchronization to keep both sticks aligned.

---

## Documentation Links

* [**中文详细说明书 (README_CN.md)**](README_CN.md): 包含功能演示、接口对比、各主板品牌接线方案与排错指南。
* [**Подробное руководство на русском (README_RU.md)**](README_RU.md): Спецификации разъемов, схемы материнских плат, настройка ОЗУ и устранение неполадок.
* [**Detailed English Manual (README_EN.md)**](README_EN.md): Full technical operation manual and troubleshooting procedures.
* [**Hardware Wiring Guide (docs/hardware_wiring_guide.md)**](docs/hardware_wiring_guide.md): 5V ARGB vs 12V RGB electrical ratings, current limits, SATA-powered hub topologies.
* [**Debugging Guide (docs/debugging_guide.md)**](docs/debugging_guide.md): HID lock diagnostics, CAM renderer log analysis, and SMBus memory clock verification.

---

## License

Licensed under the [MIT License](LICENSE).
