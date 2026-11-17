# RGB Studio architecture

```
                    RGB Studio.exe  (WinForms, tray, scene rotation)
                                   │  Apply(scene)
           ┌───────────────────────┼─────────────────────────┐
           ▼                       ▼                         ▼
   NZXT CAM (optional)     ASRock controller          OpenRGB SDK server
   start Triggers/         USB HID 26CE:01A2           127.0.0.1:6743
   RGB-<slug>.exe          via hidapi-hotplug.dll      (TCP, protocol 3)
           │                       │                         │
   CAM Profile Sync        write 0x10, read back 0x11   Direct mode, one clock
   switches fan profile    per installed channel        thread streams frames
           │                       │                         │
   case fans (3 zones)     cooler + board ARGB          every DRAM stick listed
```

## Order of a scene change

1. `RgbStudio.Apply` picks the path:
   * CAM installed, the scene has a `profileId` and its trigger exe exists: start the trigger, wait for
     `Applying master profile (id: …)` in `%APPDATA%\NZXT CAM\logs\renderer.log`, then step 2.
     If CAM does not confirm within 25 s, step 2 runs anyway with a warning; fans stay on the last profile.
   * Otherwise (no CAM, or `setup_cam.py` not run): go straight to step 2.
2. `LightingPanel.ApplyScene` sends the cooler colour (`MemoryLighting.SetFan`) and starts the RAM show
   (`MemoryLighting.Start`) in the background. The results become the second status line.

## Files

| File | Role |
| :--- | :--- |
| `src/RgbStudio.cs` | Main window, rotation timer, CAM trigger and log confirmation, preferences, power-resume reapply |
| `src/LightingPanel.cs` | Per-scene editor (cooler effect, RAM effect, speed); `ApplyScene` |
| `src/MemoryLighting.cs` | OpenRGB SDK client (`Sdk`), cooler writes over hidapi (`BoardHid`, `SetFan`), one-shot RAM modes (`Apply`) |
| `src/MemoryShow.cs` | 14 RAM effects as a function of time (`Frame`), and the thread that streams them (`RunShow`) |
| `src/BoardRgb.cs` | Same cooler protocol without hidapi (SetupDi + overlapped `FileStream`); used by the `BoardProbe` diagnostic |
| `src/MotherboardDetector.cs` | Reads board vendor/model (registry, then WMI) for the status line only |
| `src/RigPreview.cs`, `src/SceneCard.cs` | GDI+ case preview; RAM previews draw the same frames that are sent |
| `src/RgbTrigger.cs` | The trigger exe: an invisible 1×1 window that exits when RGB Studio signals or exits |
| `scripts/setup_cam.py` | Creates CAM profiles from `presets/deck.json`, writes `release/scenes.json` |
| `scripts/register_openrgb_server.ps1` | Starts OpenRGB as SDK server at logon |
| `scripts/orgb.py`, `scripts/sync_check.py` | Python SDK client; samples the sticks to check they move together |

## Cooler protocol (ASRock Polychrome USB)

* `0x14, 1` returns the installed-channel mask (bit 1 is masked off), `0x14, 3` the per-channel red/green swap mask.
* `0x10, channel, mode, c1, c2, blue, speed, 0xFF` sets a channel; `c1`/`c2` are red/green or green/red per the swap mask.
* `0x11, channel` reads the state back; RGB Studio compares mode, colour and speed.
* The first byte of replies changed between reboots on the test machine (`0xA6`, later `0x00`), so `BoardHid`
  validates replies by channel number and content only.

## RAM show

* Every DRAM device OpenRGB lists (device type 1) is used, each with its own LED count.
* Frames are sent about 14 times a second while an effect moves, every 0.4 s when static.
* Direct mode is re-sent every 20 s because it is lost after sleep; after resume the whole scene is re-applied.
