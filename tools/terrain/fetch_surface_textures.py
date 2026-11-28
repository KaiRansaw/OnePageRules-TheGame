#!/usr/bin/env python3
"""Fetches the terrain surface textures (Kai, 2026-11-27: no made-up looks - water, swamp, lava, fields and hills get real
photo materials) into OprGodot/terrain/surfaces/.

All CC0 1.0 (public domain): ambientCG (https://ambientcg.com) and 3DTextures.me (Water_001,
https://3dtextures.me). The sites themselves are not reachable from the cloud sessions, so each file is taken from a
public GitHub project that ships the unchanged CC0 file (git sparse checkout, one file at a time).

  fetch_surface_textures.py <OprGodot/terrain/surfaces> [<scratch folder>]"""
import os
import shutil
import subprocess
import sys

from PIL import Image

OUT = sys.argv[1]
TMP = sys.argv[2] if len(sys.argv) > 2 else "/tmp/surface-src"

# (repo, folder in the repo, ambientCG / 3DTextures id, {map suffix: file name here})
ACG = {"Color": "", "NormalGL": "_normal"}
PLAN = [
    ("bucekpet/Dungeon-platformer", "Textures/lava", "Lava004", dict(ACG, Emission="_emission"), "lava_flow"),
    ("bearlikelion/SurfsUpSDK", "Assets/Textures/Lava", "Lava001", dict(ACG, Emission="_emission"), "lava_crust"),
    ("RetroXR/RetroXR", "RetroXR/imported-assets/bedroom/materials", "Ground048", ACG, "mud"),
    ("dcChilshock/Game-Design-II", "Textures/Ground068", "Ground068", ACG, "mossy_ground"),
    ("dcgamejams/mask-jam-2026", "assets/img", "Ground003", ACG, "grassy_dirt"),
    ("spimort/TerraBrush", "demo/Assets/cc0_textures/Grass", "Grass004", ACG, "grass"),
    ("jmkekala/metrum_rise", "godot/assets/textures/general/grass/Grass002_1K-JPG", "Grass002", ACG, "dark_grass"),
]
WATER = ("patrickelectric/bluesim", "imports/Water_001_SD", {"Water_001_NORM.jpg": "water_normal.jpg"})


def fetch(repo, patterns):
    d = os.path.join(TMP, repo.replace("/", "_"))
    if not os.path.isdir(d):
        subprocess.run(["git", "clone", "-q", "--depth", "1", "--filter=blob:none", "--no-checkout",
                        f"https://github.com/{repo}", d], check=True)
    subprocess.run(["git", "-C", d, "sparse-checkout", "set", "--no-cone", *patterns], check=True, capture_output=True)
    subprocess.run(["git", "-C", d, "checkout", "-q"], check=True)
    return d


def save(src, dst):
    # Re-encode through PIL: proves the file is a plain picture and keeps every texture 1024 px, JPEG.
    Image.open(src).convert("RGB").resize((1024, 1024), Image.LANCZOS).save(dst, quality=88, optimize=True)
    print(dst)


os.makedirs(OUT, exist_ok=True)
for repo, folder, tid, maps, name in PLAN:
    d = fetch(repo, [f"{folder}/{tid}_1K-JPG_{m}.jpg" for m in maps])
    for m, suffix in maps.items():
        save(os.path.join(d, folder, f"{tid}_1K-JPG_{m}.jpg"), os.path.join(OUT, f"{name}{suffix}.jpg"))
repo, folder, files = WATER
d = fetch(repo, [f"{folder}/{f}" for f in files])
for f, name in files.items():
    save(os.path.join(d, folder, f), os.path.join(OUT, name))

with open(os.path.join(OUT, "License.txt"), "w") as lic:
    lic.write("Terrain surface textures - all CC0 1.0 (public domain).\n\n")
    for repo, folder, tid, maps, name in PLAN:
        lic.write(f"{name}*.jpg - ambientCG {tid} (https://ambientcg.com/view?id={tid}), via github.com/{repo}\n")
    lic.write(f"water_normal.jpg - 3DTextures.me Water 001 (https://3dtextures.me), via github.com/{WATER[0]}\n")
    lic.write("\nResized to 1024 px and re-saved as JPEG by tools/terrain/fetch_surface_textures.py; otherwise unchanged.\n")
print("License.txt written")
