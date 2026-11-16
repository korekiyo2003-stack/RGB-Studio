# RGB Studio

<div align="center">

**Universal PC Lighting Synchronizer & Preset Orchestrator**  
*NZXT CAM · ASRock / ASUS / MSI / Gigabyte Motherboard ARGB · Dual-Channel DDR4/DDR5 RAM*

[![Build and Verify](https://github.com/korekiyo2003-stack/RGB-Studio/actions/workflows/build.yml/badge.svg)](https://github.com/korekiyo2003-stack/RGB-Studio/actions)
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
* **Motherboard RGB/ARGB Headers (CPU Air/AIO Cooler, LED Shrouds)**: Low-level native USB HID protocol driving onboard microcontrollers (**ASRock Polychrome USB**, **ASUS AURA**, **MSI Mystic Light**, **Gigabyte RGB Fusion**).
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

## Documentation Links

* [**中文详细说明书 (README_CN.md)**](README_CN.md): 包含功能演示、接口对比、各主板品牌接线方案与排错指南。
* [**Подробное руководство на русском (README_RU.md)**](README_RU.md): Спецификации разъемов, схемы материнских плат, настройка ОЗУ и устранение неполадок.
* [**Detailed English Manual (README_EN.md)**](README_EN.md): Full technical operation manual and troubleshooting procedures.
* [**Hardware Wiring Guide (docs/hardware_wiring_guide.md)**](docs/hardware_wiring_guide.md): 5V ARGB vs 12V RGB electrical ratings, current limits, SATA-powered hub topologies.
* [**Debugging Guide (docs/debugging_guide.md)**](docs/debugging_guide.md): HID lock diagnostics, CAM renderer log analysis, and SMBus memory clock verification.

---

## License

Licensed under the [MIT License](LICENSE).
