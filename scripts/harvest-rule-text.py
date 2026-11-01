#!/usr/bin/env python3
"""
#420 - harvest the OFFICIAL wording of every special rule from OnePageRules' own data.

Two sources, both authoritative, both already used by the game's importer:

  1. /api/rules/common/{gameSystemId}  - the ~39 core rules and ~36 Hero traits, WORDED PER SYSTEM.
     This is where Star Quest's Scout/Fear/Blast rewordings live, so the text has to be kept per
     system rather than merged into one shared string.

  2. /api/army-books/{uid}?gameSystem={id} - each book's `specialRules`, which carries the official
     text for every book-level rule AND for core-tagged ones the common endpoint does not list
     (Ravage, for example, is `coreType`-tagged in book data and absent from the common 39).

Output: OfficialRuleText.json - {slug: {ruleName: text}}, ASCII-folded, with a `_meta` block
recording where it came from and when, so a future session can tell a stale bake from a fresh one.
"""
import json, sys, time, urllib.request, unicodedata

BASE = "https://army-forge.onepagerules.com"
SYSTEMS = {
    "grimdark-future": 2,
    "grimdark-future-firefight": 3,
    "age-of-fantasy": 4,
    "age-of-fantasy-skirmish": 5,
    "age-of-fantasy-quest": 7,
    "age-of-fantasy-quest-ai": 8,
    "grimdark-future-star-quest": 9,
    "grimdark-future-star-quest-ai": 10,
}
# The index is keyed by the slug WITHOUT the -ai suffix for both Quest systems (CLAUDE.md), so the
# enemy/AI ids share their parent's index while fetching each book at their own id.
INDEX_SLUG = {"age-of-fantasy-quest-ai": "age-of-fantasy-quest",
              "grimdark-future-star-quest-ai": "grimdark-future-star-quest"}


def fold(s):
    """The ImGui atlas bakes Basic Latin + Latin-1 only, so anything above U+00FF renders as '?'.
    OPR's text is full of typographic quotes and dashes; fold them the same way OprBookImporter does."""
    if not s:
        return ""
    for a, b in (("“", '"'), ("”", '"'), ("‘", "'"), ("’", "'"),
                 ("–", "-"), ("—", "-"), ("…", "..."), (" ", " "),
                 ("−", "-"), ("×", "x"), ("≤", "<="), ("≥", ">=")):
        s = s.replace(a, b)
    s = unicodedata.normalize("NFKD", s)
    s = "".join(c for c in s if ord(c) < 128)
    return " ".join(s.split())


def get(url, tries=3):
    for attempt in range(tries):
        try:
            with urllib.request.urlopen(url, timeout=60) as r:
                return json.loads(r.read().decode("utf-8"))
        except Exception as e:
            if attempt == tries - 1:
                print(f"    FAILED {url}: {e}", file=sys.stderr)
                return None
            time.sleep(2 * (attempt + 1))


out = {}
stats = {}
for slug, sid in SYSTEMS.items():
    text = {}

    common = get(f"{BASE}/api/rules/common/{sid}")
    n_common = 0
    if common:
        for entry in common.get("rules", []) + common.get("traits", []):
            if entry.get("name") and entry.get("description"):
                text[entry["name"]] = fold(entry["description"])
                n_common += 1

    index_slug = INDEX_SLUG.get(slug, slug)
    index = get(f"{BASE}/api/army-books?filters=official&gameSystemSlug={index_slug}")
    books = index if isinstance(index, list) else (index or {}).get("data", [])
    uids = [b.get("uid") or b.get("id") for b in books if (b.get("uid") or b.get("id"))]

    n_books = 0
    conflicts = set()
    for i, uid in enumerate(uids):
        book = get(f"{BASE}/api/army-books/{uid}?gameSystem={sid}")
        if not book:
            continue
        n_books += 1
        for r in book.get("specialRules", []) or []:
            name, desc = r.get("name"), fold(r.get("description"))
            if not name or not desc:
                continue
            # The common endpoint is the more authoritative of the two for the rules it lists, so a
            # book never overwrites it. Between books, first write wins and a disagreement is
            # recorded rather than silently resolved - OPR does reword a rule per book occasionally.
            if name in text:
                if text[name] != desc:
                    conflicts.add(name)
                continue
            text[name] = desc
        if (i + 1) % 10 == 0:
            print(f"  {slug}: {i + 1}/{len(uids)} books", flush=True)

    out[slug] = dict(sorted(text.items()))
    stats[slug] = {"rules": len(text), "fromCommon": n_common, "books": n_books,
                   "booksListed": len(uids), "conflicts": sorted(conflicts)}
    print(f"{slug}: {len(text)} rules ({n_common} common + books), "
          f"{n_books}/{len(uids)} books, {len(conflicts)} per-book disagreements", flush=True)

# Each Quest system is TWO Army Forge ids (CLAUDE.md): the player's hero roster and the enemy/AI army
# books, with different stats - and, it turns out, overlapping but non-identical rule text. The GAME has
# one slug per Quest system, so the two have to be merged onto it or half the rules in a Quest game would
# show no official text at all. The hero id wins a disagreement: it carries that system's CORE rules (the
# per-system Scout/Fear/Blast/Tough rewordings), which is the player-facing ruleset.
MERGE_ONTO = {"grimdark-future-star-quest": "grimdark-future-star-quest-ai",
              "age-of-fantasy-quest": "age-of-fantasy-quest-ai"}
for keep, absorb in MERGE_ONTO.items():
    merged = dict(out.get(absorb, {}))
    merged.update(out.get(keep, {}))          # hero id last, so it wins
    added = len(merged) - len(out.get(keep, {}))
    out[keep] = dict(sorted(merged.items()))
    out.pop(absorb, None)
    stats[keep]["mergedFrom"] = absorb
    stats[keep]["rulesAfterMerge"] = len(merged)
    stats.pop(absorb, None)
    print(f"{keep}: +{added} rules merged in from {absorb} -> {len(merged)}", flush=True)

out = {"_meta": {
    "source": "OnePageRules Army Forge: /api/rules/common/{id} and /api/army-books/{uid}",
    "harvested": time.strftime("%Y-%m-%d"),
    "note": "Official rule wording, verbatim except ASCII folding. Regenerate with "
            "scripts/harvest-rule-text.py when OPR publishes a new ruleset.",
    "stats": stats},
    **out}

with open(sys.argv[1], "w", encoding="ascii") as f:
    json.dump(out, f, indent=1, ensure_ascii=True)
print("wrote", sys.argv[1])
