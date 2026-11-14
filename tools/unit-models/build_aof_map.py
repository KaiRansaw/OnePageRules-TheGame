"""
Builds FdgRaylib/Assets/Models3D/aof-unit-models.json: which 3D models from Stormvault (the Age of Sigmar Tabletop
Simulator collection, github.com/AoS-TTS/Stormvault) stand for each Age of Fantasy unit. The Grimdark Future twin of
this is build_map.py.

Inputs
  aof-aos-equivalences.tsv - the "Unit Equivalences" tables of the Age of Fantasy faction pages on onepagefan.wiki
                             (Category:Age_of_Fantasy_Miniatures) that compare against Age of Sigmar models:
                             faction, section, OPR unit, Warhammer model(s). Only the rows read so far are in it -
                             add a faction's rows and rebuild.
  the bundled army books    - FdgRaylib/Assets/Books/{Age of Fantasy, AoF Skirmish, AoF Quest}
  an unzipped Stormvault    - pass its folder. Only model NAMES and ids go into the output; the download links stay in
                             the catalogue the game builds on the player's own computer (Asset Lab > Download models).

Usage:  python3 tools/unit-models/build_aof_map.py <path to unzipped Stormvault>

A model's id is worked out from its parts' mesh links exactly as FdgApp/Models3D/StormvaultCatalog.StableId does, so
the ids here match the catalogue the game builds.
"""
import csv, hashlib, json, os, re, sys, collections

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..'))
BOOKS = os.path.join(REPO, 'FdgRaylib', 'Assets', 'Books')
OUT = os.path.join(REPO, 'FdgRaylib', 'Assets', 'Models3D', 'aof-unit-models.json')
MAX_MODELS = 12
ID_BASE = 100_000_000

# Which Age of Sigmar factions each Age of Fantasy book stands in for. OPR's factions are its own, so this is a
# judgement call (recorded in claude/house-rules.md); it only limits where a wiki row's model names are looked for and
# which faction the Match models screen opens on. Books with no AoS counterpart are left out.
AOS_FACTIONS = {
    'Beastmen': ['Beasts of Chaos'],
    'Change Disciples': ['Disciples of Tzeentch', 'Slaves To Darkness'],
    'Lust Disciples': ['Hedonites of Slaanesh', 'Slaves To Darkness'],
    'Plague Disciples': ['Maggotkin of Nurgle', 'Slaves To Darkness'],
    'War Disciples': ['Blades of Khorne', 'Slaves To Darkness'],
    'Daemons of Change': ['Disciples of Tzeentch', 'Legion of the First Prince'],
    'Daemons of Lust': ['Hedonites of Slaanesh', 'Legion of the First Prince'],
    'Daemons of Plague': ['Maggotkin of Nurgle', 'Legion of the First Prince'],
    'Daemons of War': ['Blades of Khorne', 'Legion of the First Prince'],
    'Chivalrous Kingdoms': ['Bretonnia - HOMEBREW'],
    'Dark Elves': ['Druchii - HOMEBREW', 'Daughters of Khaine'],
    'Deep-Sea Elves': ['Idoneth Deepkin'],
    'Eternal Wardens': ['Stormcast Eternals'],
    'Ghostly Undead': ['Nighthaunt'],
    'Giant Tribes': ['Sons of Behemat'],
    'Giant Tribes Change Disciples': ['Sons of Behemat'],
    'Giant Tribes Lust Disciples': ['Sons of Behemat'],
    'Giant Tribes Plague Disciples': ['Sons of Behemat'],
    'Giant Tribes War Disciples': ['Sons of Behemat'],
    'Goblins': ['Gloomspite Gitz'],
    'Havoc Dwarves': ['Legions of Azgorh'],
    'Havoc Warriors': ['Slaves To Darkness'],
    'High Elves': ['Lumineth Realmlords', 'Cities of Sigmar'],
    'Human Empire': ['Cities of Sigmar'],
    'Ogres': ['Ogor Mawtribes'],
    'Orcs': ['Orruk Warclans'],
    'Ossified Undead': ['Ossiarch Bonereapers'],
    'Ratmen': ['Skaventide'],
    'Saurians': ['Seraphon'],
    'Sky-City Dwarves': ['Kharadron Overlords'],
    'Vampiric Undead': ['Soulblight Gravelords', 'Flesh-Eater Courts'],
    'Volcanic Dwarves': ['Fyreslayers'],
    'Wood Elves': ['Sylvaneth'],
}

STOP = {'the', 'and', 'with', 'squad', 'team', 'any', 'in', 'of', 'on', 'or', 'a', 'an', 'w', 'all', 'types',
        'including', 'unit', 'pack', 'model', 'models'}


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


def ukey(s):
    return re.sub(r'[^a-z0-9]', '', s.lower())


def stable_id(meshes):
    h = hashlib.sha1('\n'.join(m.strip() for m in meshes).encode('utf-8')).digest()
    v = int.from_bytes(h[:4], 'big')
    return ID_BASE + v % 900_000_000


def load_stormvault(folder):
    """faction -> [(id, name)], walked the way StormvaultCatalog.Build walks it."""
    files = []
    for dp, dirs, fs in os.walk(folder):
        dirs[:] = [d for d in dirs if not d.startswith('.')]
        files += [os.path.join(dp, f) for f in fs if f.endswith('.json')]
    files.sort(key=lambda f: os.path.splitext(os.path.basename(f))[0])
    out, seen = collections.OrderedDict(), set()
    for f in files:
        fac = os.path.splitext(os.path.basename(f))[0]
        save = json.load(open(f, encoding='utf-8'))
        models = out.setdefault(fac, [])

        def collect(o, parts):
            mesh = ((o.get('CustomMesh') or {}).get('MeshURL') or '').strip()
            if mesh:
                parts.append((mesh, ((o.get('CustomMesh') or {}).get('DiffuseURL') or '').strip()))
            for c in o.get('ChildObjects') or []:
                collect(c, parts)

        def name_of(o):
            n = (o.get('Nickname') or '').strip()
            if not n:
                n = next(((c.get('Nickname') or '').strip() for c in o.get('ChildObjects') or [] if (c.get('Nickname') or '').strip()), '')
            n = re.sub(r'\s+', ' ', n).strip()
            return n or 'Unnamed'

        def walk(o):
            if o.get('Name') == 'Custom_Model':
                parts = []
                collect(o, parts)
                if parts and any(t for _, t in parts):
                    i = stable_id(m for m, _ in parts)
                    if i not in seen:
                        seen.add(i)
                        models.append((i, name_of(o)))
            for c in o.get('ContainedObjects') or []:
                walk(c)
            for s in (o.get('States') or {}).values():
                walk(s)

        for o in save.get('ObjectStates') or []:
            walk(o)
    return out


def load_books(folder):
    out = {}
    if not os.path.isdir(folder):
        return out
    for f in sorted(os.listdir(folder)):
        if f.endswith('.fdgbook'):
            d = json.load(open(os.path.join(folder, f), encoding='utf-8-sig'))
            out[d['name']] = [u['name'] for u in d.get('units', [])]
    return out


def main(stormvault):
    aof = load_books(os.path.join(BOOKS, 'Age of Fantasy'))
    others = [('AoF Skirmish', load_books(os.path.join(BOOKS, 'AoF Skirmish'))),
              ('AoF Quest', load_books(os.path.join(BOOKS, 'AoF Quest')))]
    sv = load_stormvault(stormvault)

    def find_models(gw, factions):
        pool = [(i, nm, toks(nm), norm(nm)) for f in factions for i, nm in sv.get(f, [])]
        bare = re.sub(r'\([^)]*\)?', ' ', gw)
        opts = [o for o in re.split(r'\s*/\s*|,\s*', bare) if toks(o)]
        found = {}
        for o in opts:
            t, n = toks(o), norm(o)
            exact = [(i, nm) for i, nm, mt, mn in pool if mn == n]
            if exact:
                for i, nm in exact:
                    found[i] = nm
                continue
            if len(t) < 2:
                continue
            sub = [(len(mt - t), i, nm) for i, nm, mt, mn in pool if t <= mt]
            if not sub:
                continue
            best = min(s[0] for s in sub)
            for extra, i, nm in sorted(sub):
                if extra <= best + 1:
                    found[i] = nm
        items = sorted(found.items(), key=lambda kv: (len(kv[1]), kv[1], kv[0]))
        return [{'id': i, 'name': nm} for i, nm in items[:MAX_MODELS]], len(items) > MAX_MODELS

    rows = list(csv.reader(open(os.path.join(HERE, 'aof-aos-equivalences.tsv'), encoding='utf-8'), delimiter='\t'))[1:]
    result = collections.OrderedDict()
    no_book, no_model, no_gw = [], [], []
    for r in rows:
        r += [''] * (4 - len(r))
        fac, sec, opr, gw = (x.strip() for x in r[:4])
        if not opr:
            continue
        book = next((b for b in aof if ukey(b) == ukey(fac)), None)
        unit = next((u for u in aof.get(book, []) if ukey(u) == ukey(opr)), None) if book else None
        if not gw:
            no_gw.append({'wiki': fac, 'unit': opr})
            continue
        if not unit:
            no_book.append({'wiki': fac, 'unit': opr, 'warhammer': gw})
            continue
        ms, capped = find_models(gw, AOS_FACTIONS.get(book, []))
        if not ms:
            no_model.append({'wiki': fac, 'unit': opr, 'warhammer': gw})
            continue
        result[(book, unit)] = {'warhammer': gw, 'models': ms, 'tooBroad': capped}

    def stem_all(s):
        return ' '.join(stem(w) for w in s.lower().split())

    def aliases(book, unit):
        out = []
        for system, books in others:
            for u in books.get(book, []):
                if ukey(u) == ukey(unit) or ukey(stem_all(u)) == ukey(stem_all(unit)):
                    out.append({'system': system, 'unit': u})
                    break
        return out

    units = [{'book': b, 'unit': u, 'warhammer': e['warhammer'], 'alsoIn': aliases(b, u), 'tooBroad': e['tooBroad'],
              'models': e['models']} for (b, u), e in result.items()]
    units.sort(key=lambda x: (x['book'], x['unit']))
    out = {
        '_comment': 'Built by tools/unit-models/build_aof_map.py from onepagefan.wiki\'s Age of Fantasy Unit Equivalences '
                    '(the rows read so far) and Stormvault, the Age of Sigmar TTS collection. Models are named by id + name '
                    'only; the download links stay in the catalogue the game builds on your own computer.',
        'catalogue': 'Stormvault (github.com/AoS-TTS/Stormvault)',
        'aosFactions': {b: f for b, f in AOS_FACTIONS.items() if b in aof},
        'units': units,
        'notMatched': {'noBookUnit': no_book, 'noCatalogueModel': no_model, 'noWarhammerModelOnWiki': no_gw},
    }
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    json.dump(out, open(OUT, 'w', encoding='utf-8'), indent=1, ensure_ascii=True)
    print(f'{len(units)} book units mapped, {sum(len(x["models"]) for x in units)} model files; '
          f'{sum(len(v) for v in sv.values())} Stormvault models in {len(sv)} factions')
    print(f'not matched: {len(no_book)} not in a book, {len(no_model)} no Stormvault model, {len(no_gw)} no model on the wiki')
    for x in no_model:
        print('  no model:', x['unit'], '<-', x['warhammer'][:80])


if __name__ == '__main__':
    main(sys.argv[1])
