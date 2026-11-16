#!/usr/bin/env python3
"""
RGB Studio Trigger Rebuilder
Compiles lightweight 1x1 trigger executables for all scenes defined in presets/scenes.json.
Used by NZXT CAM Game Sync profile switching.
"""
import json
import os
import subprocess
import sys

def main():
    repo_root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    scenes_path = os.path.join(repo_root, 'presets', 'scenes.json')
    trigger_src = os.path.join(repo_root, 'src', 'RgbTrigger.cs')
    triggers_dir = os.path.join(repo_root, 'release', 'Triggers')

    if not os.path.exists(scenes_path):
        print(f"[ERROR] scenes.json not found at {scenes_path}")
        return 1

    csc_path = r"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
    if not os.path.exists(csc_path):
        csc_path = r"C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
    if not os.path.exists(csc_path):
        print("[ERROR] csc.exe compiler not found!")
        return 1

    os.makedirs(triggers_dir, exist_ok=True)

    with open(scenes_path, 'r', encoding='utf-8') as f:
        scenes = json.load(f)

    print(f"Rebuilding triggers for {len(scenes)} scenes...")
    success = 0
    for scene in scenes:
        slug = scene.get('slug')
        if not slug:
            continue
        out_name = f"RGB-{slug}.exe"
        out_path = os.path.join(triggers_dir, out_name)
        cmd = [csc_path, "/target:winexe", "/optimize+", f"/out:{out_path}",
               "/reference:System.dll,System.Drawing.dll,System.Windows.Forms.dll", trigger_src]
        res = subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        if res.returncode == 0:
            success += 1
        else:
            print(f"[FAILED] {out_name}: {res.stderr.decode(errors='ignore')}")

    print(f"[DONE] Successfully built {success}/{len(scenes)} trigger executables in {triggers_dir}")
    return 0

if __name__ == '__main__':
    sys.exit(main())
