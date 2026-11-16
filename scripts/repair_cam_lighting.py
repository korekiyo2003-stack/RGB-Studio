"""Rebuild the CAM lighting profile list after lightingController.json was wiped.

Every "RGB - X" master profile needs a lighting profile entry whose id has channel data in
lightingV2Store.json. Masters that CAM already reset to DEFAULT get their old id back from
older copies of masterProfileController.json, if that id still has channel data.
Usage: repair_cam_lighting.py [--write]
"""
import json, os, sys, time
from pathlib import Path

USER = Path(os.environ["APPDATA"]) / "NZXT CAM" / "DataStorage" / "latest" / "user"
OLD = [Path(os.environ["APPDATA"]) / "NZXT CAM" / "DataStorage" / "4.76.8" / "user",
       Path(os.environ["APPDATA"]) / "NZXT CAM" / "DataStorage" / ".last-known-good" / "user"]
OLD += sorted((Path(r"D:\LocalAI\qwen-fill\lights\backups")).glob("*/cam"), reverse=True)


def load(p):
    with open(p, encoding="utf-8-sig") as f:
        return json.load(f)


lighting = load(USER / "lightingController.json")
master = load(USER / "masterProfileController.json")
store = load(USER / "lightingV2Store.json")["ledChannelMap"]
with_data = {c["profileId"] for c in store.values()}
have = {p["id"] for p in lighting["profiles"]}

old_light, old_master = {}, {}
for d in OLD:
    for name, target in (("lightingController.json", old_light), ("masterProfileController.json", old_master)):
        try:
            for p in load(d / name)["profiles"]:
                key = p["id"] if target is old_light else p["name"]
                target.setdefault(key, p)
        except Exception:
            pass

added = relinked = unresolved = 0
now = int(time.time() * 1000)
for m in master["profiles"]:
    if not m["name"].startswith("RGB - "):
        continue
    lid = m["lightingProfileId"]
    if lid == "DEFAULT":
        prev = old_master.get(m["name"])
        if prev and prev["lightingProfileId"] in with_data and prev["lightingProfileId"] != "DEFAULT":
            m["lightingProfileId"] = lid = prev["lightingProfileId"]
            relinked += 1
        else:
            unresolved += 1
            print("no old lighting id for", m["name"])
            continue
    if lid in have:
        continue
    if lid not in with_data:
        unresolved += 1
        print("no channel data for", m["name"], lid)
        continue
    entry = dict(old_light.get(lid) or {"id": lid, "originId": lid, "lastSelected": now,
                                         "globalBrightness": 1, "channelSettings": []})
    entry["name"] = m["name"]
    lighting["profiles"].append(entry)
    have.add(lid)
    added += 1

print(f"added {added} lighting profiles, relinked {relinked} masters, unresolved {unresolved}")
if "--write" in sys.argv:
    for name, data in (("lightingController.json", lighting), ("masterProfileController.json", master)):
        tmp = USER / (name + ".tmp")
        tmp.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")
        os.replace(tmp, USER / name)
    print("written")
