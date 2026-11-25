#!/usr/bin/env python3
"""
Adopt what scripts/audit-against-army-forge.sh found, WITHOUT replacing whole books.

    python3 scripts/adopt-army-forge-changes.py <audit-workdir> "<System>/<book>.fdgbook" [...]
    python3 scripts/adopt-army-forge-changes.py <audit-workdir> --all-differing

Why not just copy <workdir>/fresh over the bundled books (the old advice): the rebuild regenerates every
book from the rule SUPPLEMENTS, and the bundled books have moved on since - OPR's own wording in every
stored description (#468), the pick-count / once-per-game bands (#468 batch 1), the 9"/6" and most-models
rules audit (2.69), round bases (2.72, house rule B25). A whole-book copy silently undoes all of it for
that book. (Seen 2026-11-25: copying Vampiric Undead would have reverted 20 rule descriptions, Mind
Control's line of sight and 13 bases; the audit's own report could not see any of it.)

What this does, per book, taking Army Forge's data from <workdir>/fresh:
  - the version stamp;
  - units Army Forge added, at Army Forge's position in the list;
  - units Army Forge changed: every field taken from Army Forge EXCEPT `base` (curated: round bases)
    and the section ids (kept where the section survives, so the file diff stays readable);
  - rule definitions the added/changed units need and the book lacks - copied from another bundled
    book of the SAME system (so they carry the curated wording and effects), never from the rebuild.
It refuses to guess: a unit Army Forge removed, a rule needing a definition no sibling book has, or a
rule whose sibling copies disagree is REPORTED and left for a person.

Afterwards: re-run the audit (it must say 0 differ), run both test suites, and regenerate the Quest
enemy collections (--make-collections) if a Quest enemy book changed.

Books are written the way System.Text.Json writes them (2-space indent, \\u0022-style escapes), so an
untouched book round-trips byte for byte and the git diff shows only the adopted change.
"""
import glob
import json
import os
import re
import sys

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
BOOKS = os.path.join(REPO, 'FdgRaylib', 'Assets', 'Books')

_ESC = {'"': '\\u0022', "'": '\\u0027', '+': '\\u002B', '<': '\\u003C', '>': '\\u003E', '&': '\\u0026',
        '`': '\\u0060', '\\': '\\\\', '\n': '\\n', '\r': '\\r', '\t': '\\t', '\b': '\\b', '\f': '\\f'}


def _str(s):
    out = []
    for ch in s:
        if ch in _ESC:
            out.append(_ESC[ch])
        elif ord(ch) < 0x20 or ord(ch) > 0x7E:
            out.append('\\u%04X' % ord(ch))
        else:
            out.append(ch)
    return '"' + ''.join(out) + '"'


def dumps(o, ind=0):
    """System.Text.Json, WriteIndented, default encoder - verified byte-identical on all 288 books."""
    pad, pad2 = '  ' * ind, '  ' * (ind + 1)
    if isinstance(o, dict):
        if not o:
            return '{}'
        return '{\n' + ',\n'.join(f'{pad2}{_str(k)}: {dumps(v, ind + 1)}' for k, v in o.items()) + '\n' + pad + '}'
    if isinstance(o, list):
        if not o:
            return '[]'
        return '[\n' + ',\n'.join(pad2 + dumps(v, ind + 1) for v in o) + '\n' + pad + ']'
    if o is None:
        return 'null'
    if isinstance(o, bool):
        return 'true' if o else 'false'
    if isinstance(o, str):
        return _str(o)
    if isinstance(o, int):
        return str(o)
    r = repr(o)
    return r[:-2] if r.endswith('.0') else r


def load(path):
    with open(path, encoding='utf-8') as f:
        return json.load(f)


def rule_refs(o, acc):
    """Every rule name a unit or definition points at: rule entries, granted rules, required rules."""
    if isinstance(o, dict):
        if isinstance(o.get('kind'), str) and o['kind'].startswith('core') and isinstance(o.get('name'), str):
            acc.add(o['name'])
        for key in ('ruleName', 'requiredRule'):
            if isinstance(o.get(key), str):
                acc.add(o[key])
        for v in o.values():
            rule_refs(v, acc)
    elif isinstance(o, list):
        for v in o:
            rule_refs(v, acc)
    return acc


def differing_books(work):
    """Books the audit's per-system reports list (their summary tables), as '<System>/<file>' paths."""
    found = []
    for system in sorted(os.listdir(os.path.join(work, 'fresh'))):
        report = os.path.join(work, f"report-{system.replace(' ', '-')}.md")
        if not os.path.isdir(os.path.join(BOOKS, system)) or not os.path.exists(report):
            continue
        names = set(re.findall(r'^\| ([^|]+?) \| \d+ \| \d+ \| \d+ \|$', open(report, encoding='utf-8').read(), re.M))
        for path in sorted(glob.glob(os.path.join(BOOKS, system, '*.fdgbook'))):
            if load(path).get('name') in names:
                found.append(f'{system}/{os.path.basename(path)}')
    return found


def adopt(work, rel):
    bundled_path, fresh_path = os.path.join(BOOKS, rel), os.path.join(work, 'fresh', rel)
    raw = open(bundled_path, encoding='utf-8').read()
    book, fresh = load(bundled_path), load(fresh_path)
    log, problems = [], []

    if book.get('version') != fresh.get('version'):
        log.append(f"version {book.get('version')} -> {fresh.get('version')}")
        book['version'] = fresh.get('version')

    fresh_ids = {u['id'] for u in fresh['units']}
    for u in book['units']:
        if u['id'] not in fresh_ids:
            problems.append(f"unit '{u['name']}' is gone from Army Forge - NOT removed, decide by hand")

    touched = []
    for i, fu in enumerate(fresh['units']):
        mine = next((u for u in book['units'] if u['id'] == fu['id']), None)
        if mine is None:
            pos = 0
            for j in range(i - 1, -1, -1):
                k = next((n for n, x in enumerate(book['units']) if x['id'] == fresh['units'][j]['id']), None)
                if k is not None:
                    pos = k + 1
                    break
            book['units'].insert(pos, fu)
            touched.append(fu)
            log.append(f"added unit {fu['name']} ({fu.get('basePointCost')} pts)")
            continue
        merged = json.loads(json.dumps(fu))
        merged['base'] = mine.get('base', merged.get('base'))
        for k, sec in enumerate(merged.get('sections', [])):
            old = mine.get('sections', [])
            if k < len(old) and old[k].get('variant') == sec.get('variant'):
                sec['id'] = old[k]['id']
        if json.dumps(merged, sort_keys=True) != json.dumps(mine, sort_keys=True):
            fields = sorted(k for k in set(merged) | set(mine) if merged.get(k) != mine.get(k))
            book['units'][book['units'].index(mine)] = merged
            touched.append(merged)
            log.append(f"updated unit {fu['name']}: {', '.join(fields)}")

    have = {r['name'] for r in book.get('ruleDefinitions', [])}
    siblings = {}
    for path in sorted(glob.glob(os.path.join(os.path.dirname(bundled_path), '*.fdgbook'))):
        if os.path.samefile(path, bundled_path):
            continue
        for r in load(path).get('ruleDefinitions', []):
            siblings.setdefault(r['name'], {}).setdefault(json.dumps(r, sort_keys=True), (os.path.basename(path), r))
    fresh_defs = {r['name'] for r in fresh.get('ruleDefinitions', [])}
    queue = sorted(set().union(*(rule_refs(u, set()) for u in touched)) if touched else set())
    while queue:
        name = queue.pop(0)
        if name in have:
            continue
        variants = siblings.get(name, {})
        if len(variants) == 1:
            (source, definition), = variants.values()
            book.setdefault('ruleDefinitions', []).append(definition)
            have.add(name)
            log.append(f"rule '{name}' copied from {source}")
            queue.extend(sorted(rule_refs(definition, set()) - have))
        elif len(variants) > 1:
            problems.append(f"rule '{name}': {len(variants)} different curated copies in this system - NOT added")
        elif name in fresh_defs:
            problems.append(f"rule '{name}': no curated copy in this system, only the rebuild's - NOT added")
        # else: an engine-handled core rule with no book definition anywhere, like every other book

    with open(bundled_path, 'w', encoding='utf-8', newline='\n') as f:
        f.write(dumps(book) + ('\n' if raw.endswith('\n') else ''))
    return log, problems


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        sys.exit(2)
    work = sys.argv[1]
    books = differing_books(work) if sys.argv[2] == '--all-differing' else sys.argv[2:]
    if not books:
        print('Nothing to adopt.')
        return
    trouble = False
    for rel in books:
        log, problems = adopt(work, rel)
        print(f'== {rel}')
        for line in log:
            print(f'   {line}')
        for line in problems:
            print(f'   !! {line}')
        trouble |= bool(problems)
    print('\nNow re-run scripts/audit-against-army-forge.sh (expect 0 differ) and both test suites.')
    sys.exit(1 if trouble else 0)


if __name__ == '__main__':
    main()
