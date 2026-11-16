# RGB Studio Debugging & Diagnostics Guide / 调试与故障诊断指南

This document provides a systematic debugging workflow for identifying and resolving hardware communication faults, software locks, and protocol errors in **RGB Studio**.

---

## 1. Quick Diagnostic Checklist

| Subsystem | Diagnostic Indicator | Healthy Status | If Failed, See Section |
| :--- | :--- | :--- | :--- |
| **GUI & State** | Main Window Top Banner | Shows active scene name & countdown | Section 2.1 |
| **Motherboard ARGB** | Right Panel: CPU Fan Status | `散热器 · 常亮已验证` or `散热器 · 呼吸已验证` | Section 2.2 |
| **DDR4/DDR5 RAM** | Right Panel: Memory Status | `内存 · 两条已同步` | Section 2.3 |
| **NZXT CAM** | Main Window Status Text | `CAM 已应用 <场景名称>` | Section 2.4 |

---

## 2. Subsystem Troubleshooting & Solutions

### 2.1. GUI / Application Startup
* **Symptom**: "RGB Studio 已在运行。请从任务栏或系统托盘打开。"
  * **Cause**: Another instance of RGB Studio is already running in the background or minimized to the system tray.
  * **Solution**: Check the Windows notification area (System Tray) near the clock. Double-click the RGB Studio icon. If frozen, kill the process via PowerShell:
    ```powershell
    Stop-Process -Name "RGB Studio" -Force -ErrorAction SilentlyContinue
    ```
* **Symptom**: `preferences.json` or `scenes.json` read error on startup.
  * **Solution**: Verify that `scenes.json` exists in the same folder as `RGB Studio.exe`. If missing, copy it from `presets/scenes.json`.

---

### 2.2. Motherboard ARGB / CPU Cooler ("散热器 · 控制器没有响应" or "灯控通信失败")

#### Cause 1: Vendor Software Locking the USB HID Device
* On ASRock boards, the onboard Nuvoton controller (`VID: 0x26CE, PID: 0x01A2`) can only accept one exclusive HID connection stream at a time.
* If **ASRock Polychrome RGB** (`AsrPolychromeRGB.exe`), **ASUS Armoury Crate**, **MSI Center**, or **Gigabyte RGB Fusion** is running, it constantly queries and re-writes the hardware registers.
* **Fix**:
  1. Exit Polychrome / Armoury Crate from the system tray.
  2. Kill background processes:
     ```powershell
     Stop-Process -Name "AsrPolychromeRGB", "LightingService", "MSICenter" -Force -ErrorAction SilentlyContinue
     ```
  3. In RGB Studio, click any scene and press **"保存并应用"** (Save and Apply).

#### Cause 2: USB Device Access Permissions
* Windows requires standard user access for HID class devices, but some custom driver configurations restrict HID access.
* **Fix**: Right-click `RGB Studio.exe` and select **Run as Administrator**.

#### Cause 3: Color Channel Inversion (Red appears Green or Blue)
* Some ARGB light strips wire LEDs as `GRB` while others wire them as `RGB`.
* **Fix**:
  * On ASRock motherboards, RGB Studio reads register `0x14, 0x03` to detect swapped channels.
  * You can test motherboard header communication using the diagnostic script in `scripts/`:
    ```powershell
    python scripts/orgb.py
    ```

---

### 2.3. Memory Lighting ("内存 · 灯控服务未连接" or "检测到 0/2 条")

RGB Studio uses an OpenRGB SDK client (`127.0.0.1:6743`) to communicate with motherboard SMBus DRAM controllers.

#### Cause 1: OpenRGB Backend Service Not Running
* The OpenRGB server must be running in the background to handle low-level kernel driver SMBus transactions (`SmbusPIIX4`, `SmbusI801`, `PawnIOLib`).
* **Verification**:
  ```powershell
  python scripts/sync_check.py
  ```
* **Fix**:
  * Launch OpenRGB with the server parameter:
    ```powershell
    & "C:\Program Files\RGB Studio Memory Backend\OpenRGB.exe" --server -p 6743 --profile None
    ```
  * Or install it as a Windows scheduled task on logon.

#### Cause 2: Anti-Cheat or Kernel Driver Conflict
* Certain anti-cheat engines (e.g. Riot Vanguard, Easy Anti-Cheat) may temporarily block kernel drivers from accessing the memory SPD SMBus addresses (`0x50`–`0x57`).
* **Fix**: Start OpenRGB before launching anti-cheat protected games.

#### Cause 3: Polychrome or XPG Prime Locking SMBus
* If XPG Prime or Polychrome is running, it will fight for the SMBus lock, leading to dropped frames. Close all third-party RAM lighting apps.

---

### 2.4. NZXT CAM Synchronization ("CAM 未确认「场景名」" or Timeout)

#### How the Trigger Protocol Works
1. RGB Studio starts a hidden 1x1 trigger window (`release/Triggers/RGB-<slug>.exe`).
2. NZXT CAM has Game Hook detection configured in `customGames.json`. When CAM sees `RGB-<slug>.exe`, it matches the executable path and applies the linked lighting master profile (`masterProfileController.json`).
3. RGB Studio tails CAM's log file:
   `%APPDATA%\NZXT CAM\logs\renderer.log`
   and searches for the line:
   `Applying master profile (id: <profileId>, ...)`
4. When confirmed, RGB Studio closes the previous trigger process and switches the motherboard/RAM lighting to match.

#### Common Issues & Fixes
* **CAM Not Running**:
  * Make sure NZXT CAM is launched and visible in your system tray.
* **Missing Trigger Binary**:
  * If the error says `无法启动切换。请确认 CAM 已打开，并保留 Triggers 文件夹。`, verify that `Triggers/` folder exists alongside `RGB Studio.exe` with `RGB-<slug>.exe` files inside.
  * You can regenerate all triggers at any time by running:
    ```powershell
    python scripts/build_triggers.py
    ```
* **CAM Database Out of Sync**:
  * If CAM was recently updated or reinstalled, its profile database may not contain the RGB Studio scene hooks. Run the repair script:
    ```powershell
    python scripts/repair_cam_lighting.py
    ```
  * Restart NZXT CAM after running the repair script.

---

## 3. Command-Line Verification Tools

### 3.1. Motherboard Header Diagnostic Probe (`BoardProbe.exe`)
```powershell
# Compile probe utility
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:exe /out:src/BoardProbe.exe src/BoardRgb.cs src/BoardProbe.cs

# Send a pure red color (FF0000) to motherboard headers:
.\src\BoardProbe.exe FF0000
```
Expected output:
```text
散热器 · 常亮已验证
```

### 3.2. RAM Dual-Channel Clock Sync Check (`scripts/sync_check.py`)
```powershell
python scripts/sync_check.py
```
Expected output:
```text
[OK] Connected to OpenRGB SDK at 127.0.0.1:6743
[OK] Found 2 memory devices in Direct Mode
[OK] Latency: 1.2ms | Phase Drift: 0.00ms
```
