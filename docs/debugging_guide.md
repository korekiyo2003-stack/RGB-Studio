# Troubleshooting

RGB Studio shows two status lines: the first for the scene / NZXT CAM, the second for cooler and RAM.
The messages are in Chinese; this table lists them.

## Cooler / motherboard ARGB (second line)

| Message | Meaning | Fix |
| :--- | :--- | :--- |
| 请先退出 Polychrome | ASRock Polychrome is running and owns the controller | Exit it from the tray (and its startup entry) |
| 灯控组件未安装 | `hidapi-hotplug.dll` not found | Install OpenRGB, or set `OPENRGB_DIR` to its folder |
| 灯控通信失败 | No ASRock controller 26CE:01A2, or it did not answer | Only ASRock Polychrome USB boards are supported |
| 未找到灯光接口 | Controller reports no installed channels | Check the header the cooler is plugged into |
| 灯光接口读回不一致 | Write was not confirmed by read-back | Close other RGB software; try again |

Diagnostic without the app (uses the hidapi-free path in `BoardRgb.cs`):
```powershell
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /out:BoardProbe.exe src\BoardRgb.cs src\BoardProbe.cs src\MotherboardDetector.cs /r:System.Management.dll
.\BoardProbe.exe FF0000
```
Prints e.g. `散热器 · 常亮已验证` (static colour verified).

## RAM (second line)

| Message | Meaning | Fix |
| :--- | :--- | :--- |
| 灯控服务未连接 | Nothing listens on 127.0.0.1:6743 | Run `scripts\register_openrgb_server.ps1`, or start `OpenRGB.exe --server --server-port 6743` |
| 灯控服务响应超时 | OpenRGB is busy (often still scanning at logon) | Wait a few seconds; it retries every 3 s |
| 未找到内存灯… | OpenRGB lists no DRAM device | Open OpenRGB itself: if it shows no RAM there, it needs admin rights / its SMBus driver |

Check that the sticks move together:
```powershell
python scripts\sync_check.py 5
```
It samples the sticks about nine times a second and prints how many samples were identical
(the "Duet" effect is meant to differ: the sticks take turns).

## NZXT CAM (first line)

| Message | Meaning | Fix |
| :--- | :--- | :--- |
| 等待 NZXT CAM 启动 | CAM is installed but not running | Start CAM; RGB Studio retries every 20 s |
| CAM 未确认「…」的风扇灯效… | CAM did not switch within 25 s; cooler and RAM were applied anyway | Run `scripts\setup_cam.py`, or check the scene's game binding in CAM → Profile Sync |
| 无法启动切换… | The trigger exe could not be started | Keep the `Triggers` folder next to `RGB Studio.exe`; `python scripts\build_triggers.py` rebuilds it |

If CAM's lighting list was wiped (seen after a Windows shutdown on the test machine, while channel data
survived), `scripts\repair_cam_lighting.py` rebuilds the list from the channel data and old backups
(`RGB_STUDIO_BACKUPS` can point to a folder of `<stamp>\cam` copies). Run it with CAM closed.
