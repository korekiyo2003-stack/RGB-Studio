# Hardware Wiring & Interface Matching Guide / 硬件接口搭配与接线方案

This guide provides exhaustive technical specifications, pinout schematics, electrical limitations, and vendor-specific connection architectures for PC lighting equipment controlled by **RGB Studio**.

---

## 1. Electrical Header Standards & Pinouts

Modern motherboards and PC lighting devices predominantly feature two incompatible electrical lighting standards. Connecting devices to the wrong header will result in permanent hardware damage.

```
       5V 3-Pin ARGB (Addressable)              12V 4-Pin RGB (Non-Addressable)
      ┌───┬───┬───┬───┐                              ┌───┬───┬───┬───┐
      │ 1 │ 2 │ X │ 3 │                              │ 1 │ 2 │ 3 │ 4 │
      └───┴───┴───┴───┘                              └───┴───┴───┴───┘
     +5V  DATA KEY GND                              +12V  G   R   B
    (Pin 3 physically blocked)                    (All LEDs same color)
```

### 1.1. 5V 3-Pin Addressable RGB (ARGB / DRGB / WS2812B / SK6812)
* **Physical Connector**: 4-pin housing with Pin 3 blocked (`[+5V] [DATA] [KEY] [GND]`).
* **Operating Voltage**: **+5V DC ± 5%** (Regulated).
* **Communication Protocol**: High-speed NRZ single-wire serial data protocol (800 kHz, 1.25 µs bit period, 24-bit GRB/RGB per LED).
* **Maximum Current & Power**:
  * Standard motherboard header limit: **3.0 Amperes (15 Watts)** max.
  * Typical ARGB LED power draw: ~0.06A (60mA) per LED at peak white (100% R+G+B).
  * Safe capacity per motherboard header: **Up to 50–60 LEDs** without external power injection.
* **Control Capability**: Each individual LED in a fan ring or strip can display independent colors, gradients, pulses, waves, and chasings.
* ⚠️ **CRITICAL WARNING**: **NEVER** force a 3-pin 5V ARGB plug into a 4-pin 12V RGB header. The 12V rail will instantly destroy the WS2812B logic controllers and damage the motherboard trace!

### 1.2. 12V 4-Pin Analog RGB (Standard 5050 SMD)
* **Physical Connector**: Standard 4-pin male/female header (`[+12V] [Green] [Red] [Blue]`).
* **Operating Voltage**: **+12V DC**.
* **Communication Protocol**: Low-side PWM ground switching. The controller motherboard drives discrete MOSFETs to modulate the ground path of each color channel.
* **Maximum Current & Power**:
  * Standard motherboard header limit: **2.0A to 3.0A (24W to 36W)** max.
* **Control Capability**: The entire connected strip or fan lights up in **one single uniform color** simultaneously. No individual LED addressability.

---

## 2. Motherboard Vendor-Specific Architectures & Solutions

Different motherboard manufacturers implement onboard lighting controllers through varying hardware interfaces (USB HID, Super I/O, or SMBus).

| Motherboard Brand | Header Markings | Onboard Chipset | Control Channel in RGB Studio |
| :--- | :--- | :--- | :--- |
| **ASRock** | `ADDR_LED1`, `ADDR_LED2`, `ADDR_LED3`, `RGB_LED1` | Nuvoton NUC121 USB MCU (`0x26CE:0x01A2`) | **Direct Native USB HID** (`BoardRgb.cs`) |
| **ASUS** | `ADD_GEN 2_1`, `ADD_GEN 2_2`, `AURA_RGB` | AURA LED Controller (USB/ITE8297) | **OpenRGB SDK Backend** (Port 6743) |
| **MSI** | `JARGB_V2_1`, `JARGB_V2_2`, `JRGB1` | MSI Nuvoton / APM Controller | **OpenRGB SDK Backend** (Port 6743) |
| **Gigabyte / AORUS** | `D_LED1`, `D_LED2` (5V), `LED_C1`, `LED_C2` (12V) | ITE IT8297 USB Controller | **OpenRGB SDK Backend** (Port 6743) |

### 2.1. ASRock Motherboards (Native Direct USB HID)
* **Target Platforms**: Intel Z790/Z690/B760/B660, AMD X670E/B650/X570/B550 (e.g. Steel Legend, Taichi, PG Riptide, Pro RS).
* **Hardware Controller**: Internal Nuvoton NUC121 ARM Cortex-M0 microcontroller attached to internal USB (`VID: 0x26CE`, `PID: 0x01A2`).
* **Pin Mapping & Channel Configuration**:
  * `0x00`: 12V RGB Header 1 (`RGB1`)
  * `0x02`: 5V Addressable Header 1 (`ARGB1`)
  * `0x03`: 5V Addressable Header 2 (`ARGB2`)
  * `0x07`: 5V Addressable Header 3 (`ARGB3`)
  * `0x04-0x06`: Onboard heatsink / I/O shroud RGB zones
* **Hardware Color Order (RGB vs. GRB)**:
  * Register `0x14, 0x03` on the NUC121 reports whether a given channel requires red/green byte swapping. RGB Studio automatically queries this register and performs bitwise swapping (`swap ? red : green`).
* **Direct Control Advantage**: RGB Studio writes directly to the HID endpoints. You **do not** need ASRock Polychrome RGB running. (In fact, Polychrome must be closed so it does not poll and overwrite the USB queue).

### 2.2. ASUS Motherboards (ROG, TUF Gaming, Prime)
* **Connection**: Plug CPU cooler fan ARGB and case strip ARGB into `ADD_GEN 2_1` or `ADD_GEN 2_2`.
* **RGB Studio Integration**:
  * Communication runs through the OpenRGB backend engine (`127.0.0.1:6743`).
  * Ensure ASUS Armoury Crate / LightingService is stopped to prevent bus collision.

### 2.3. MSI Motherboards (MPG, MAG, MEG, PRO)
* **Connection**: Connect to `JARGB_V2_1` (Pinout: 5V-D-Empty-GND).
* **RGB Studio Integration**: Controlled via OpenRGB backend. Ensure MSI Center / Mystic Light is closed.

### 2.4. Gigabyte / AORUS Motherboards
* **Connection**: Connect to `D_LED1` or `D_LED2`.
* ⚠️ **Vintage Board Warning**: Older Gigabyte motherboards (Z370/B360 era) feature a 3-pin header with a movable jumper for 5V vs 12V selection. Always ensure the jumper is set to **5V** before connecting ARGB fans.
* **RGB Studio Integration**: Controlled via OpenRGB backend.

---

## 3. Dedicated External Controllers & Hubs

### 3.1. NZXT RGB & Fan Controller / Smart Device
* **Physical Connections**:
  * 9-pin Internal USB 2.0 header connection to motherboard.
  * Dedicated SATA power cable from PSU (provides up to 4.5A on 5V rail).
  * Fans connect to NZXT proprietary 4-pin RGB channels (or via NZXT-to-Standard ARGB adapter cables).
* **Control Mechanism in RGB Studio**:
  * NZXT CAM runs locally.
  * RGB Studio triggers CAM profiles with zero performance overhead using lightweight 1x1 hidden window processes (`RGB-<slug>.exe`).
  * When a scene switches, RGB Studio starts the trigger binary; NZXT CAM detects the game profile sync event and immediately switches its internal fan and lighting channels across Bottom, Side, and Rear arrays.

### 3.2. Powered ARGB Hubs / Splitters (SATA Powered)
* **When to Use**: When powering more than 4 ARGB fans (e.g. 6 to 9 chassis fans).
* **Wiring Topology**:
  * Hub Signal Input -> Motherboard 3-Pin ARGB Header (`ADDR_LED1`).
  * Hub Power Input -> Direct SATA Power cable from Power Supply.
  * Hub Outputs (1 to 10) -> Individual ARGB fans.
* **Why**: The SATA cable provides isolated 5V power (up to 4.5A / 22.5W), while the motherboard header only needs to emit the low-current (0.005A) digital PWM/NRZ data signal. This protects motherboard copper traces from overheating.

---

## 4. Dual-Channel DDR4 / DDR5 RAM Lighting

### 4.1. Hardware Architecture
* Desktop DDR4 and DDR5 memory modules (e.g., ADATA XPG Lancer, Corsair Vengeance, G.Skill Trident Z, Kingston Fury) use onboard RGB microcontrollers (such as ENE 6K7750, ITE, or Holtek) located directly on each RAM stick PCB.
* Communication occurs over the motherboard **SMBus / I2C bus** (System Management Bus) through the DDR DIMM edge connector pins.

### 4.2. Dual-Stick Hardware Clock Drift Problem
* In typical RGB software, the two RAM sticks are sent an effect command (e.g., "Flash" or "Pulse") and then left to execute independently on their internal hardware timers.
* Because individual quartz crystal oscillators on RAM stick PCBs have tiny manufacturing tolerances (±20 to 50 ppm), one stick runs slightly faster than the other. Within 5–10 minutes, the two RAM sticks noticeably desynchronize.

### 4.3. RGB Studio's Synchronized Software Clock Solution
* RGB Studio configures both memory sticks into **Direct Mode**.
* A centralized high-precision software timer thread (`MemoryShow.cs`) generates unified color frames at fixed intervals (30–60 FPS).
* Synchronous frame packets are sent simultaneously to both DIMM slots over the OpenRGB SDK client (`MemoryLighting.cs`).
* **Result**: Dual memory sticks maintain 100% mathematical phase alignment indefinitely, with zero drift.
