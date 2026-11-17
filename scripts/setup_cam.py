"""Create one NZXT CAM lighting profile per RGB Studio scene, and write release/scenes.json with their ids.

RGB Studio changes the case fans by starting a tiny trigger exe (release/Triggers/RGB-<scene>.exe).
CAM's Profile Sync treats each exe as a "game" and switches to the matching profile. This script
creates those profiles from the scene deck (presets/deck.json), copying the structure of ONE template
profile you make by hand, because CAM's files differ between hubs and versions.

One-time preparation in NZXT CAM (2 minutes):
  1. Lighting: create a lighting profile named "RGB - Template" (any effect) for the hub your fans are on.
  2. Games / Profile Sync: add release/Triggers/RGB-Template.exe as a custom game named "RGB-Template",
     and create a profile named "RGB - Template" that uses the lighting profile above when that game runs.
  3. Close NZXT CAM completely (tray icon -> Exit).

Then:
  python scripts/setup_cam.py                 # dry run: shows what would be added
  python scripts/setup_cam.py --write         # backs up CAM's files, adds the scenes, writes release/scenes.json

Zones: every scene has three zones (bottom row, side row, rear fan). --channels maps them to CAM
channel ids of your hub, in that order (default: the first three channel ids of the template).
Run with --list-channels to see them.
"""
from __future__ import annotations

import argparse
import copy
import json
import os
import shutil
import subprocess
import sys
import time
import uuid
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DECK = ROOT / "presets" / "deck.json"
OUT = ROOT / "release" / "scenes.json"
DEFAULT_CAM = Path(os.environ.get("APPDATA", "")) / "NZXT CAM" / "DataStorage" / "latest" / "user"
CAM_FILES = ("lightingController.json", "lightingV2Store.json", "masterProfileController.json", "customGames.json")
EFFECTS = {  # effect -> (takes speed, takes direction), from CAM's defaults table
    "BreathingCycling": (True, False), "BreathingFixed": (True, False), "Fading": (True, False),
    "Pulse": (True, False), "Marquee": (True, True), "CoveringMarquee": (True, True),
    "Alternating": (True, True), "StarryNight": (True, False), "Candle": (False, False),
    "Wave": (True, True), "TaiChi": (True, True), "Swish": (True, False), "Fixed": (False, False),
}
COOLER_SPEED = {"Slowest": 245, "Slow": 232, "Normal": 215, "Fast": 195}


def read_json(path: Path):
    return json.loads(path.read_text("utf-8-sig"))


def write_json(path: Path, data) -> None:
    tmp = path.with_suffix(path.suffix + ".tmp")
    tmp.write_text(json.dumps(data, ensure_ascii=False), encoding="utf-8")
    tmp.replace(path)


def cam_running() -> bool:
    if os.name != "nt":
        return False
    out = subprocess.run(["tasklist", "/FI", "IMAGENAME eq NZXT CAM.exe", "/FO", "CSV", "/NH"],
                         capture_output=True, text=True).stdout
    return "nzxt cam.exe" in out.lower()


def cam_mode(zone: dict) -> dict:
    rgb = lambda c: {k: int(c[i:i + 2], 16) for i, k in zip((0, 2, 4), "rgb")}  # noqa: E731
    speed, direction = EFFECTS[zone["effect"]]
    mode = {"colors": [rgb(c) for c in zone["colors"]], "effect": zone["effect"]}
    if speed:
        mode["speed"] = zone.get("speed") or "Normal"
    if direction:
        mode["direction"] = zone.get("direction") or "Forwards"
    if zone["effect"] in ("Marquee", "Alternating"):
        mode["effectLength"] = int(zone.get("length", 4))
    if zone["effect"] == "Alternating":
        mode["moving"] = bool(zone.get("moving", True))
    return mode


def game_list_of(games):
    if isinstance(games, list):
        return games
    for key in ("customGames", "games"):
        if key in games:
            return games[key]
    raise SystemExit("customGames.json: unknown layout, nothing changed")


def find(items, name, what):
    hit = next((x for x in items if x.get("name") == name), None)
    if hit is None:
        raise SystemExit(f'Template {what} "{name}" not found in CAM. See the preparation steps at the top of this file.')
    return hit


def merge(cam: dict, deck: list[dict], template: str, game_template: str, channels: list[int] | None):
    lights = cam["lightingController.json"]["profiles"]
    store = cam["lightingV2Store.json"]["ledChannelMap"]
    masters = cam["masterProfileController.json"]["profiles"]
    games = game_list_of(cam["customGames.json"])
    src_light = find(lights, template, "lighting profile")
    src_master = find(masters, template, "profile")
    src_game = find(games, game_template, "custom game")
    baseline = [copy.deepcopy(c) for c in store.values() if c["profileId"] == src_light["id"]]
    if not baseline:
        raise SystemExit(f'"{template}" has no channel data yet: open it in CAM, change any colour once, close CAM, retry.')
    ids_available = sorted({c["channelId"] for c in baseline})
    channels = channels or ids_available[:3]
    missing = [c for c in channels if c not in ids_available]
    if missing:
        raise SystemExit(f"Channels {missing} are not in the template (it has {ids_available}).")

    ids, added = {}, 0
    for scene in deck:
        name, game_name = "RGB - " + scene["slug"], "RGB-" + scene["slug"]
        light = next((p for p in lights if p["name"] == name), None)
        if light is None:
            light = {**copy.deepcopy(src_light), "id": str(uuid.uuid4()), "name": name, "lastSelected": int(time.time() * 1000)}
            lights.append(light)
            added += 1
        game = next((g for g in games if g["name"] == game_name), None)
        if game is None:
            game = {**copy.deepcopy(src_game), "id": max(int(g["id"]) for g in games) + 1, "name": game_name,
                    "slug": "rgb-" + scene["slug"].lower(), "game_exes": [game_name + ".exe"]}
            games.append(game)
        master = next((p for p in masters if p["name"] == name), None)
        if master is None:
            master = {**copy.deepcopy(src_master), "id": str(uuid.uuid4()), "name": name}
            masters.append(master)
        master.update(condition={"type": "game", "gameIds": [str(game["id"])]}, lightingProfileId=light["id"])
        by_channel = dict(zip(channels, scene["zones"]))
        for base in baseline:
            ch = copy.deepcopy(base)
            ch["profileId"] = light["id"]
            zone = by_channel.get(ch["channelId"])
            if zone:
                ch.update(brightness=float(zone["brightness"]), selectedMode=cam_mode(zone))
            store[f'{light["id"]}#{ch["deviceUniqueId"]}#{ch["channelId"]}'] = ch
        ids[scene["slug"]] = master["id"]
    return ids, added, channels


def studio_scenes(deck: list[dict], ids: dict[str, str]) -> list[dict]:
    out = []
    for s in deck:
        z0 = s["zones"][0]
        out.append({"slug": s["slug"], "name": s["name"], "profileId": ids.get(s["slug"], ""), "group": s.get("group") or "联动",
                    "memory": s["memory"], "idea": s.get("idea", ""), "coolerSpeed": COOLER_SPEED.get(z0.get("speed"), 232),
                    "pattern": s["name"].split("·")[0] if s.get("by") == "mix" else "手工",
                    "cooler": s["cooler"]["color"], "board": z0["colors"][0], "coolerBreathing": bool(s["cooler"].get("breathing")),
                    "zones": [{"effect": z["effect"], "brightness": z["brightness"], "colors": z["colors"], "speed": z.get("speed") or "Normal",
                               "direction": z.get("direction") or "Forwards", "length": int(z.get("length", 4))} for z in s["zones"]]})
    return out


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--write", action="store_true", help="change CAM's files (default is a dry run)")
    ap.add_argument("--template", default="RGB - Template", help="lighting profile AND profile to copy")
    ap.add_argument("--game-template", default="RGB-Template", help="custom game to copy")
    ap.add_argument("--channels", help="CAM channel ids for bottom,side,rear, e.g. 0,1,4")
    ap.add_argument("--list-channels", action="store_true", help="print the template's channels and exit")
    ap.add_argument("--cam-dir", type=Path, default=DEFAULT_CAM, help="CAM DataStorage/latest/user folder")
    ap.add_argument("--deck", type=Path, default=DECK)
    ap.add_argument("--out", type=Path, default=OUT, help="scenes.json for RGB Studio")
    ap.add_argument("--no-cam", action="store_true", help="only write scenes.json without CAM ids (cooler + memory only)")
    a = ap.parse_args(argv)

    deck = json.loads(a.deck.read_text("utf-8"))
    if a.no_cam:
        write_json(a.out, studio_scenes(deck, {}))
        print(f"wrote {a.out} ({len(deck)} scenes, no CAM ids: fans are left alone)")
        return 0

    missing = [n for n in CAM_FILES if not (a.cam_dir / n).exists()]
    if missing:
        raise SystemExit(f"NZXT CAM data not found in {a.cam_dir} ({', '.join(missing)}). Is CAM installed? Use --no-cam otherwise.")
    cam = {n: read_json(a.cam_dir / n) for n in CAM_FILES}

    if a.list_channels:
        light = find(cam["lightingController.json"]["profiles"], a.template, "lighting profile")
        for c in sorted((c for c in cam["lightingV2Store.json"]["ledChannelMap"].values() if c["profileId"] == light["id"]),
                        key=lambda c: (c["deviceUniqueId"], c["channelId"])):
            print(f'device {c["deviceUniqueId"]}  channel {c["channelId"]}  mode {c.get("selectedMode", {}).get("effect")}')
        return 0

    channels = [int(x) for x in a.channels.split(",")] if a.channels else None
    ids, added, used = merge(cam, deck, a.template, a.game_template, channels)
    print(f"{len(deck)} scenes, {added} new CAM profiles, zones -> channels {used}")
    if not a.write:
        print("dry run: nothing written. Close NZXT CAM and add --write to apply.")
        return 0
    if a.cam_dir == DEFAULT_CAM and cam_running():
        raise SystemExit("NZXT CAM is running; it would overwrite the changes. Exit it from the tray and retry.")
    backup = ROOT / "backups" / ("cam-" + time.strftime("%Y%m%d-%H%M%S"))
    backup.mkdir(parents=True)
    for n in CAM_FILES:
        shutil.copy2(a.cam_dir / n, backup / n)
    for n in CAM_FILES:
        write_json(a.cam_dir / n, cam[n])
    write_json(a.out, studio_scenes(deck, ids))
    triggers = a.out.parent / "Triggers"
    source = triggers / (a.game_template + ".exe")
    if source.exists():
        for s in deck:
            target = triggers / f'RGB-{s["slug"]}.exe'
            if not target.exists():
                shutil.copy2(source, target)
    print(f"CAM files written (backup: {backup}), {a.out} updated. Start NZXT CAM, then RGB Studio.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
