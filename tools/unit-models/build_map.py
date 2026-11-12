"""
Builds FdgRaylib/Assets/Models3D/gf-unit-models.json: which 3D models from the ForceOrg Tabletop Simulator
catalogue stand for each Grimdark Future unit.

Inputs
  opr-40k-equivalences.tsv  - the "Unit Equivalences" tables of every Grimdark Future faction page on
                              onepagefan.wiki (Category:Grimdark_Future_Miniatures), read 2026-11-12:
                              faction, section, OPR unit, Warhammer model(s).
  the bundled army books     - FdgRaylib/Assets/Books/{Grimdark Future,GF Firefight,GF Star Quest}
  forceorg-models.json       - the ForceOrg catalogue (personal use; NOT in the repo). Pass its path.

Usage:  python3 tools/unit-models/build_map.py <path to forceorg-models.json>

The output names catalogue models by id and name only - no download links. The links stay in the
catalogue file on the player's own computer, which the Asset Lab's "Download models" button reads.
"""
import csv, json, os, re, sys, collections

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..'))
BOOKS = os.path.join(REPO, 'FdgRaylib', 'Assets', 'Books')
OUT = os.path.join(REPO, 'FdgRaylib', 'Assets', 'Models3D', 'gf-unit-models.json')
MAX_MODELS = 12  # per unit; more than this is a too-broad match ("Terminators" hits every chapter's)

STOP = {'the', 'and', 'with', 'squad', 'team', 'any', 'in', 'of', 'on', 'or', 'a', 'an', 'w', 'all', 'types',
        'including', 'kill', 'brood', 'unit', 'pack', 'model', 'models'}


def stem(w):
    for suf, rep in (('ies', 'y'), ('ves', 'f'), ('es', 'e'), ('s', '')):
        if len(w) > 4 and w.endswith(suf):
            return w[: -len(suf)] + rep
    return w


def toks(s):
    s = s.lower()
    s = re.sub(r'\(.*?\)', ' ', s)
    s = re.sub(r'[^a-z ]', ' ', s)
    return {stem(w) for w in s.split() if len(w) > 2 and w not in STOP}


def norm(s):
    return ' '.join(sorted(toks(s)))


def load_books(folder):
    out = {}
    for f in sorted(os.listdir(folder)):
        if not f.endswith('.fdgbook'):
            continue
        d = json.load(open(os.path.join(folder, f), encoding='utf-8-sig'))
        out[d['name']] = [u['name'] for u in d.get('units', [])]
    return out


# Which current books each wiki faction page covers. The Battle/Prime Brothers pages carry the sub-faction
# books' units as prefixed rows ("Blood Destroyers"); Havoc Brothers and the daemons were split per god in 3.6.0.
FAMILIES = {
    'Battle Brothers': ['Battle Brothers', 'Blood Brothers', 'Dark Brothers', 'Knight Brothers', 'Watch Brothers', 'Wolf Brothers'],
    'Prime Brothers': ['Prime Brothers', 'Blood Prime Brothers', 'Dark Prime Brothers', 'Knight Prime Brothers', 'Watch Prime Brothers', 'Wolf Prime Brothers'],
    'Havoc Brothers': ['Havoc Brothers', 'Disciples of Change', 'Disciples of Lust', 'Disciples of Plague', 'Disciples of War',
                       'Change Disciples', 'Lust Disciples', 'Plague Disciples', 'War Disciples'],
    'Wormhole Daemons': ['Daemons of Change', 'Daemons of Lust', 'Daemons of Plague', 'Daemons of War'],
    'Titan Lords': ['Titan Lords', 'Titan Lords Change Disciples', 'Titan Lords Lust Disciples', 'Titan Lords Plague Disciples', 'Titan Lords War Disciples'],
    'Ratmen Clans - Mortian': ['Ratmen Clans'],
    'Machine Cult': ['Machine Cults'],
    'Saurian Starhost': ['Saurian Starhosts', 'Saurian Starhost'],
}
# Sub-faction books name their units "<Prefix> <parent's unit>" (Blood Brothers' "Blood Destroyers"). They take the
# parent book's models for a unit unless the wiki gives the sub-faction its own row.
PARENT = {'Battle Brothers': ['Blood Brothers', 'Dark Brothers', 'Knight Brothers', 'Watch Brothers', 'Wolf Brothers'],
          'Prime Brothers': ['Blood Prime Brothers', 'Dark Prime Brothers', 'Knight Prime Brothers', 'Watch Prime Brothers', 'Wolf Prime Brothers'],
          'Havoc Brothers': ['Change Disciples', 'Lust Disciples', 'Plague Disciples', 'War Disciples']}


def ukey(s):
    return re.sub(r'[^a-z0-9]', '', s.lower())


def main(catalogue_path):
    gf = load_books(os.path.join(BOOKS, 'Grimdark Future'))
    ff = load_books(os.path.join(BOOKS, 'GF Firefight'))
    sq = load_books(os.path.join(BOOKS, 'GF Star Quest'))
    cat = json.load(open(catalogue_path, encoding='utf-8'))
    models = [(m['id'], m['name'], toks(m['name']), norm(m['name'])) for m in cat['models']]

    def find_models(gw):
        # Parentheses are the wiki's notes on what the model stands in for ("(Walker Combat Claws / ...)"), not names.
        bare = re.sub(r'\([^)]*\)?', ' ', gw)
        opts = [o for o in re.split(r'\s*/\s*|,\s*', bare) if toks(o)]
        found = {}
        for o in opts:
            t, n = toks(o), norm(o)
            exact = [(i, nm) for i, nm, mt, mn in models if mn == n]
            if exact:
                for i, nm in exact:
                    found[i] = nm
                continue
            if len(t) < 2:
                continue  # one word ("Hunter", "Devastators") only matches exactly - looser hits the wrong models
            sub = [(len(mt - t), i, nm) for i, nm, mt, mn in models if t <= mt]
            if not sub:
                continue
            best = min(s[0] for s in sub)
            for extra, i, nm in sorted(sub):
                if extra <= best + 1:
                    found[i] = nm
        items = sorted(found.items(), key=lambda kv: (len(kv[1]), kv[1]))
        return [{'id': i, 'name': nm} for i, nm in items[:MAX_MODELS]], len(items) > MAX_MODELS

    rows = list(csv.reader(open(os.path.join(HERE, 'opr-40k-equivalences.tsv'), encoding='utf-8'), delimiter='\t'))[1:]
    result = collections.OrderedDict()  # (book, unit) -> entry
    no_book, no_model, no_gw = [], [], []
    for r in rows:
        r += [''] * (4 - len(r))
        fac, sec, opr, gw = (x.strip() for x in r[:4])
        if not opr or opr.lower().startswith('star quest'):
            continue
        family = FAMILIES.get(fac, [fac])
        family = [b for b in family if b in gf] or [b for b in gf if ukey(b) == ukey(fac)]
        targets = []
        for b in family:
            if any(ukey(u) == ukey(opr) for u in gf[b]):
                targets.append((b, next(u for u in gf[b] if ukey(u) == ukey(opr))))
        if not targets:
            # Older or shorter wording ("Havoc Tank" for "Havoc Battle Tank", "Change Brothers" for "Change Havoc
            # Brothers"): every word of the wiki's name is in exactly one unit of the family's books.
            t = set(re.sub(r'[^a-z ]', ' ', opr.lower()).split())
            cands = [(b, u) for b in family for u in gf[b] if t and t <= set(re.sub(r'[^a-z ]', ' ', u.lower()).split())]
            if len(cands) == 1 or (cands and len({b for b, _ in cands}) == len(cands) and all(len(u.split()) == len(cands[0][1].split()) for _, u in cands)):
                targets = cands
        if not gw:
            no_gw.append({'wiki': fac, 'unit': opr})
            continue
        if not targets:
            no_book.append({'wiki': fac, 'unit': opr, 'warhammer': gw})
            continue
        ms, capped = find_models(gw)
        if not ms:
            no_model.append({'wiki': fac, 'unit': opr, 'warhammer': gw})
            continue
        for b, u in targets:
            key = (b, u)
            prefixed = opr != u  # a sub-faction's own row beats the generic row it inherits
            if key in result and not prefixed and result[key]['specific']:
                continue
            result[key] = {'book': b, 'unit': u, 'warhammer': gw, 'models': ms, 'tooBroad': capped, 'specific': prefixed}

    # Inherit: a sub-faction unit with no row of its own takes its parent unit's models.
    for parent, subs in PARENT.items():
        if parent not in gf:
            continue
        for sub in subs:
            if sub not in gf:
                continue
            prefix = sub.split()[0] + ' '
            for u in gf[sub]:
                if (sub, u) in result or not u.startswith(prefix):
                    continue
                base = u[len(prefix):]
                if (parent, base) in result:
                    e = dict(result[(parent, base)]); e['specific'] = False
                    result[(sub, u)] = e

    # The same unit in Firefight and Star Quest: same book name, same unit name or its singular.
    def aliases(book, unit):
        out = []
        for system, books in (('GF Firefight', ff), ('GF Star Quest', sq)):
            if book not in books:
                continue
            for u in books[book]:
                if ukey(u) == ukey(unit) or ukey(stem_all(u)) == ukey(stem_all(unit)):
                    out.append({'system': system, 'unit': u})
                    break
        return out

    def stem_all(s):
        return ' '.join(stem(w) for w in s.lower().split())

    units = []
    for (b, u), e in result.items():
        units.append({'book': b, 'unit': u, 'warhammer': e['warhammer'], 'alsoIn': aliases(b, u),
                      'tooBroad': e['tooBroad'], 'models': e['models']})
    units.sort(key=lambda x: (x['book'], x['unit']))
    out = {
        '_comment': 'Built by tools/unit-models/build_map.py from onepagefan.wiki\'s Unit Equivalences (read 2026-11-12) and the '
                    'ForceOrg TTS catalogue. Models are named by catalogue id + name only; the download links stay in the '
                    'catalogue file on your own computer. Rebuild after a book refresh.',
        'catalogue': cat.get('source', ''),
        'units': units,
        'notMatched': {'noBookUnit': no_book, 'noCatalogueModel': no_model, 'noWarhammerModelOnWiki': no_gw},
    }
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    json.dump(out, open(OUT, 'w', encoding='utf-8'), indent=1, ensure_ascii=True)
    nb = collections.Counter(x['book'] for x in units)
    print(f'{len(units)} book units mapped, {sum(len(x["models"]) for x in units)} model files')
    print(f'not matched: {len(no_book)} wiki units not found in a current book, {len(no_model)} with no catalogue model, '
          f'{len(no_gw)} with no Warhammer model on the wiki')
    for b, n in sorted(nb.items()):
        print(f'  {b}: {n} units / {len(gf[b])} in book')


if __name__ == '__main__':
    main(sys.argv[1])
