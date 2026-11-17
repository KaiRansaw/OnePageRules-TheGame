#!/usr/bin/env python3
"""Writes FdgRaylib/Assets/Books/race-groups.json: every official army book per game, in Army Forge's own order,
with its Army Forge raceGroup (None -> "Unknown", Army Forge's word) and its description ("background", shown in the
Choose an Army info panel - Kai, 2026-11-17). The army picker grid groups books by the group (Kai, 2026-11-16:
"grouped like Army Forge"). Game text is ASCII-only, so the description is folded here. Re-run after an Army Forge
refresh:
    python3 tools/army-groups/fetch_race_groups.py
"""
import json, os, sys, urllib.request

SLUGS = ["grimdark-future", "grimdark-future-firefight", "grimdark-future-star-quest",
         "age-of-fantasy", "age-of-fantasy-skirmish", "age-of-fantasy-quest"]
FOLD = {"\u2018": "'", "\u2019": "'", "\u201c": '"', "\u201d": '"', "\u2013": "-", "\u2014": " - ", "\u2026": "...",
        "\u00a0": " ", "\u2022": "-", "\u2122": "", "\u00ae": ""}


def ascii_fold(text):
    import unicodedata
    text = "".join(FOLD.get(c, c) for c in (text or ""))
    text = unicodedata.normalize("NFKD", text)
    return "".join(c for c in text if ord(c) < 128).replace("\r\n", "\n").strip()


root = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
out = {}
for slug in SLUGS:
    url = f"https://army-forge.onepagerules.com/api/army-books?filters=official&gameSystemSlug={slug}"
    with urllib.request.urlopen(url, timeout=60) as r:
        books = json.load(r)
    out[slug] = [{"name": b["name"], "group": b.get("raceGroup") or "Unknown", "about": ascii_fold(b.get("background"))}
                 for b in books]
    print(slug, len(books), file=sys.stderr)
path = os.path.join(root, "FdgRaylib", "Assets", "Books", "race-groups.json")
with open(path, "w", newline="\n") as f:
    json.dump({"_source": "Army Forge /api/army-books?filters=official (raceGroup, background)", "systems": out}, f, indent=1)
print(path)
