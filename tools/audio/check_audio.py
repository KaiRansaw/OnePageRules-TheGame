#!/usr/bin/env python3
"""Loudness check of every sound the game ships (lane 4): integrated loudness (LUFS) and true peak per file, from
ffmpeg's ebur128 filter. Flags anything ear-splitting (true peak over -1 dBFS, or louder than -14 LUFS for a sound
longer than a second) and anything silent. Usage: check_audio.py <folder> [<folder>...]"""
import os, re, subprocess, sys

bad = 0
for top in sys.argv[1:]:
    for root, _, files in sorted(os.walk(top)):
        for f in sorted(files):
            if not f.endswith((".ogg", ".wav", ".mp3")):
                continue
            p = os.path.join(root, f)
            r = subprocess.run(["ffmpeg", "-nostats", "-i", p, "-af", "ebur128=peak=true", "-f", "null", "-"],
                               capture_output=True, text=True).stderr
            i = re.findall(r"I:\s+(-?[\d.inf]+) LUFS", r)
            pk = re.findall(r"Peak:\s+(-?[\d.inf]+) dBFS", r)
            dur = re.search(r"Duration: (\d+):(\d+):([\d.]+)", r)
            secs = int(dur.group(1)) * 3600 + int(dur.group(2)) * 60 + float(dur.group(3)) if dur else 0
            lufs = float(i[-1]) if i else float("-inf")
            peak = float(pk[-1]) if pk else float("-inf")
            flag = ""
            if peak > -1.0: flag += " PEAK"
            if secs > 1 and lufs > -14: flag += " LOUD"
            if peak < -40: flag += " SILENT"
            bad += bool(flag)
            print(f"{os.path.relpath(p, top):45s} {secs:6.2f}s {lufs:7.1f} LUFS  peak {peak:6.1f} dBFS{flag}")
print(f"{bad} flagged")
