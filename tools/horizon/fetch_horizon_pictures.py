#!/usr/bin/env python3
"""Fetches the Generic / Wasteland / Death horizon pictures (lane 4, 2026-11-27) into OprGodot/horizon/<Theme>/.
All CC0: skies from Poly Haven, grounds from ambientCG, taken from public GitHub repos that vendor the original files
(kenney.nl, polyhaven.com and ambientcg.com themselves are not reachable from the cloud sandbox). Usage:
  fetch_horizon_pictures.py <OprGodot/horizon> [<scratch folder>]"""
import os, shutil, subprocess, sys
from PIL import Image

OUT = sys.argv[1]
TMP = sys.argv[2] if len(sys.argv) > 2 else "/tmp/horizon-src"

SOURCES = {  # repo -> files to check out
    "Griiimon/Cosmic-Constructors": ["game/textures/Ground067_1K-JPG_Color.jpg"],
    "christianmateus/silence-on-the-9th-floor": ["Materials/Ground031_1K-JPG/Ground031_1K-JPG_Color.jpg",
                                                  "Materials/Ground031_1K-JPG/Ground031_1K-JPG_NormalGL.jpg"],
    "wangyvqian/tafang-td": ["assets/textures/sand/Ground054_1K-JPG_Color.jpg", "assets/textures/sand/Ground054_1K-JPG_NormalGL.jpg"],
    "ShockBock/TheOpenWastes": ["images/Textures/Rock033/Rock033_1K-JPG_Color.jpg", "images/Textures/Rock033/Rock033_1K-JPG_NormalGL.jpg",
                                "images/Textures/Ground051/Ground051_1K-JPG_Color.jpg", "images/Textures/Ground051/Ground051_1K-JPG_NormalGL.jpg"],
    "auraoneai/aura3d": ["public/hdri/autumn_field_puresky_1k.hdr", "public/hdri/kloppenheim_06_puresky_1k.hdr"],
}

PLAN = [  # (repo, file, theme, name, jpeg quality or None = copy)
    ("Griiimon/Cosmic-Constructors", "game/textures/Ground067_1K-JPG_Color.jpg", "Generic", "ground.jpg", 85),
    ("auraoneai/aura3d", "public/hdri/autumn_field_puresky_1k.hdr", "Generic", "sky.hdr", None),
    ("christianmateus/silence-on-the-9th-floor", "Materials/Ground031_1K-JPG/Ground031_1K-JPG_Color.jpg", "Wasteland", "ground.jpg", 85),
    ("christianmateus/silence-on-the-9th-floor", "Materials/Ground031_1K-JPG/Ground031_1K-JPG_NormalGL.jpg", "Wasteland", "ground_normal.jpg", 90),
    ("wangyvqian/tafang-td", "assets/textures/sand/Ground054_1K-JPG_Color.jpg", "Wasteland", "ground2.jpg", 85),
    ("wangyvqian/tafang-td", "assets/textures/sand/Ground054_1K-JPG_NormalGL.jpg", "Wasteland", "ground2_normal.jpg", 90),
    ("auraoneai/aura3d", "public/hdri/kloppenheim_06_puresky_1k.hdr", "Wasteland", "sky.hdr", None),
    ("ShockBock/TheOpenWastes", "images/Textures/Rock033/Rock033_1K-JPG_Color.jpg", "Death", "ground.jpg", 85),
    ("ShockBock/TheOpenWastes", "images/Textures/Rock033/Rock033_1K-JPG_NormalGL.jpg", "Death", "ground_normal.jpg", 90),
    ("ShockBock/TheOpenWastes", "images/Textures/Ground051/Ground051_1K-JPG_Color.jpg", "Death", "ground2.jpg", 85),
    ("ShockBock/TheOpenWastes", "images/Textures/Ground051/Ground051_1K-JPG_NormalGL.jpg", "Death", "ground2_normal.jpg", 90),
]


def fetch(repo, files):
    d = os.path.join(TMP, repo.replace("/", "_"))
    if not os.path.isdir(d):
        subprocess.run(["git", "clone", "-q", "--depth", "1", "--filter=blob:none", "--no-checkout",
                        f"https://github.com/{repo}", d], check=True)
    subprocess.run(["git", "-C", d, "sparse-checkout", "set", "--no-cone", *files], check=True, capture_output=True)
    subprocess.run(["git", "-C", d, "checkout", "-q"], check=True)
    return d


for repo, files in SOURCES.items():
    fetch(repo, files)
for repo, f, theme, name, q in PLAN:
    src = os.path.join(TMP, repo.replace("/", "_"), f)
    dst = os.path.join(OUT, theme, name)
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    if q is None:
        shutil.copy(src, dst)
    else:
        Image.open(src).convert("RGB").resize((1024, 1024), Image.LANCZOS).save(dst, quality=q, optimize=True)
    print(dst)
