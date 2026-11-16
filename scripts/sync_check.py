"""Sample both memory sticks through the OpenRGB backend and report whether they move together.

    python lights\\sync_check.py [seconds]

Reads what OpenRGB last wrote to each stick about nine times a second. "identical" counts the samples in
which both sticks held the same eight colours (a 对答 show is meant to differ: there the sticks take turns).
"""
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import orgb  # noqa: E402

seconds = float(sys.argv[1]) if len(sys.argv) > 1 else 5
sdk = orgb.Sdk()
same, frames, total, started = 0, set(), 0, time.time()
while time.time() - started < seconds:
    sticks = orgb.memory(sdk)
    a, b = (tuple(d["colors"]) for d in sticks)
    same += a == b
    frames.add(a)
    total += 1
    time.sleep(0.11)
mode = sticks[0]["modes"][sticks[0]["active"]]["name"]
print(f"mode {mode} | {total} samples | both sticks identical in {same} | distinct frames {len(frames)}")
print("last:", " ".join(orgb.hexof(c) for c in sticks[0]["colors"]), "|", " ".join(orgb.hexof(c) for c in sticks[1]["colors"]))
sdk.close()
