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

## 2. Motherboard Headers

RGB Studio drives motherboard headers on **ASRock boards with the Polychrome USB controller (VID 26CE / PID 01A2)** only.
ASUS, MSI and Gigabyte boards are recognised and named in the status line but not driven; use OpenRGB or the vendor
software for them.

### 2.1. ASRock (Polychrome USB)
* **Headers**: `ADDR_LED1..3` (5 V ARGB), `RGB_LED1` (12 V RGB). Plug the cooler's ARGB lead into an `ADDR_LED` header.
* **Channels**: the controller reports which channels are installed (`0x14, 1`) and which need red/green swapped
  (`0x14, 3`); RGB Studio reads both and writes every installed channel, then reads each one back.
* **Polychrome** must be closed: it polls the controller and overwrites it.
* Checked on an ASRock B850 Challenger WiFi. Other ASRock boards with the same controller ID should behave the same, untested.

### 2.2. Other vendors (for reference)
| Brand | Typical 5 V ARGB header | Driven by RGB Studio |
| :--- | :--- | :--- |
| ASUS | `ADD_GEN 2` | no |
| MSI | `JARGB_V2` | no |
| Gigabyte / AORUS | `D_LED1`, `D_LED2` (older boards: 5 V / 12 V jumper, set to 5 V for ARGB) | no |

---

## 3. Dedicated External Controllers & Hubs

### 3.1. NZXT RGB & Fan Controller / Smart Device
* **Physical Connections**:
  * 9-pin Internal USB 2.0 header connection to motherboard.
  * Dedicated SATA power cable from PSU (provides up to 4.5A on 5V rail).
  * Fans connect to NZXT proprietary 4-pin RGB channels (or via NZXT-to-Standard ARGB adapter cables).
* **Control Mechanism in RGB Studio**:
  * NZXT CAM runs locally.
  * RGB Studio starts a small trigger exe per scene (`RGB-<slug>.exe`, an invisible 1×1 window). Profiles are created once with `scripts/setup_cam.py`.
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

### 4.3. RGB Studio's approach
* Every stick OpenRGB lists as DRAM is put into **Direct mode**.
* One thread (`MemoryShow.cs`) computes each frame from a single clock and sends it to all sticks, about 14 frames
  per second while an effect moves.
* The sticks therefore cannot drift apart. `scripts/sync_check.py` samples them to confirm it.
