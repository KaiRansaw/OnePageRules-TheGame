"""
Bot sweep matrix (lane 2, 2026-11-27): every game x mode x optional rule, played bot-vs-bot headless, with the
dev audit switches on (FDG_MISSION_AUDIT, FDG_HEIGHTS_AUDIT, FDG_CITY_AUDIT). Every crash, hang, error, audit
violation or odd ending is collected for fixing.

Usage: python3 tools/sweep/matrix.py [games per cell] [jobs] [out dir]
  env MATRIX_ONLY=<substring,...>   run only cells whose name contains one of these
  env MATRIX_SKIP=<substring,...>   leave out cells whose name contains one of these (e.g. strategist - slow)
  env SWEEP_SEED=<n>                base seed (default 9100)
Needs a built FdgRaylib (dotnet build FdgRaylib/FdgRaylib.csproj) and DOTNET_ROOT exported.
Writes <out>/results.json and prints a status count; logs per game in <out>/<cell>-<i>/log.txt.
"""
import glob, json, os, random, re, subprocess, sys, concurrent.futures as cf

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
EXE = os.path.join(REPO, 'FdgRaylib', 'bin', 'Debug', 'net8.0', 'FdgRaylib')
BOOKS = os.path.join(REPO, 'FdgRaylib', 'Assets', 'Books')
ARMY_GAMES = {  # flag: (books folder, points)
    'gf': ('Grimdark Future', 1000), 'gff': ('GF Firefight', 300),
    'aof': ('Age of Fantasy', 1000), 'aofs': ('AoF Skirmish', 300),
}
QUEST_GAMES = {'gfsq': 'GF Star Quest', 'aofq': 'AoF Quest'}
ALL4 = list(ARMY_GAMES)
GF_AOF = ['gf', 'aof']
SKIRMISH = ['gff', 'aofs']

BAD = re.compile(r'(Unhandled exception|Exception:|   at FDG\.|FAULT|fault:|Fatal|threw|NullReference|InvalidOperation|'
                 r'IndexOutOfRange|KeyNotFound|ArgumentOutOfRange|Stack overflow|VIOLATION|mid-climb|'
                 r'[1-9][0-9]* out of place)', re.I)

MISSIONS = ['SeizeGround', 'RelicHunt', 'Sabotage', 'Breakthrough', 'KingOfTheHill', 'PitchedBattle', 'Domination',
            'CaptureAndHold', 'Demolition', 'Headquarters', 'MoshPit', 'TheRaid', 'SmashAndGrab', 'Ambush', 'LastStand',
            'VipEscort', 'TheRescue', 'TwistsAndGambits', 'MeatGrinderRespawning', 'MeatGrinderRecruitment',
            'MeatGrinderRecruitmentUnderdog', 'Random']
SIDE_PLAYS = ['Shared', 'Hidden', 'SharedAndHidden', 'RandomPlaystyle', 'SharedDash', 'HiddenDash', 'CumulativeDash', 'RandomDash']
SIDE_PICKS = ['Free', 'Limited', 'Randomized', 'Random']
ADVANCED = [  # (name, flags, games)
    ('psychology', 'UnitPsychology', ALL4),
    ('brutal', 'BrutalDamage', GF_AOF),
    ('crit-skirmish', 'SkirmishCriticalHits', SKIRMISH),
    ('crit-weapon', 'CriticalHitsByWeaponType', SKIRMISH),
    ('terrain-effects', 'TerrainObjectiveEffects', ALL4),
    ('random-events', 'RandomEvents', ALL4),
    ('escalating', 'EscalatingBattles', ALL4),
    ('escalating-random', 'EscalatingBattles,EscalatingRandomArrival', ALL4),
    ('surprise', 'SurpriseEngagement', ALL4),
    ('ebb-flow', 'EbbAndFlow', ALL4),
    ('shifting', 'ShiftingFocus', ALL4),
    ('hesitation', 'CombatHesitation', ALL4),
    ('prolonged', 'ProlongedBattle', ALL4),
    ('command-points', 'CommandPoints', ALL4),
    ('politics', 'PoliticalDirectives,Politicians', GF_AOF),
    ('army-terrain', 'ArmyTerrain', ALL4),
    ('spellbooks', 'SpellBooks,TwoD6Casting,HeavyBacklash', ALL4),
    ('magic-pools', 'MagicPools', ALL4),
    ('living-spells', 'LivingSpells', ALL4),
    ('relics', 'RelicWargear', ALL4),
    ('tactical-phase', 'ForwardPositions,BattlePreparations', SKIRMISH),
    ('city', 'CorneredUnits,DestructibleCover,ThrownObjects,BuildingsOnFire', ALL4),
    ('everything', 'UnitPsychology,TerrainObjectiveEffects,RandomEvents,CommandPoints,ArmyTerrain,MagicPools,LivingSpells,RelicWargear', ALL4),
]
PROFILES = ['tactician', 'strategist']


def cells():
    out = []  # (name, game, mode, extra args)
    for g in ALL4:
        out.append((f'{g}-base', g, 'standard', []))
        out.append((f'{g}-horde', g, 'horde', []))
        out.append((f'{g}-solid', g, 'standard', ['--solid-buildings']))
        out.append((f'{g}-simplified', g, 'standard', ['--simplified-terrain']))
        for p in PROFILES:
            out.append((f'{g}-simplified-{p}', g, 'standard', ['--simplified-terrain', '--ai-profile', p]))
        out.append((f'{g}-heights', g, 'standard', ['--terrain-layout', os.path.join(REPO, 'Maps', 'Climbing Test.fdgterrain' if g in GF_AOF else 'Climbing Test 4x4.fdgterrain')]))
        for p in PROFILES:
            out.append((f'{g}-bot-{p}', g, 'standard', ['--ai-profile', p]))
        for m in MISSIONS:
            out.append((f'{g}-mission-{m}', g, 'standard', ['--extra-mission', m]))
        for s in SIDE_PLAYS:
            out.append((f'{g}-side-{s}', g, 'standard', ['--side-missions', s]))
        for p in SIDE_PICKS[1:]:
            out.append((f'{g}-sidepick-{p}', g, 'standard', ['--side-missions', 'Shared', '--side-mission-pick', p]))
        for c in range(1, 13):
            out.append((f'{g}-condition-{c}', g, 'standard', ['--special-condition', str(c)]))
        for name, flags, games in ADVANCED:
            if g in games:
                out.append((f'{g}-adv-{name}', g, 'standard', ['--advanced', flags]))
        for p in PROFILES:
            out.append((f'{g}-cp-{p}', g, 'standard', ['--advanced', 'CommandPoints', '--ai-profile', p]))
            out.append((f'{g}-pools-{p}', g, 'standard', ['--advanced', 'MagicPools', '--ai-profile', p]))
            out.append((f'{g}-2d6-{p}', g, 'standard', ['--advanced', 'SpellBooks,TwoD6Casting', '--ai-profile', p]))
    for q in QUEST_GAMES:
        for mode in ('sqmission', 'sqhorde'):
            out.append((f'{q}-{mode}', q, mode, []))
            out.append((f'{q}-{mode}-challenges', q, mode, ['--challenges', 'all']))
    return out


def run(job):
    name, game, mode, extra, i, seed, outdir = job
    rng = random.Random(seed)
    tag = f'{name}-{i:02d}'
    work = os.path.join(outdir, tag)
    os.makedirs(work, exist_ok=True)
    args = [EXE, '--headless', '--all-ai', '--seed', str(seed), '--game-system', game, '--game-mode', mode]
    picked = []
    if game in QUEST_GAMES:
        enemies = glob.glob(os.path.join(REPO, 'armies', QUEST_GAMES[game], '*.fdgarmy'))
        e = rng.choice(enemies); picked.append(os.path.basename(e))
        args += ['--party', 'auto', '--army2', e]
    else:
        books, pts = ARMY_GAMES[game]
        if '--army-points' in extra:   # a bigger game: build the lists to it (the flag below then repeats it)
            pts = int(extra[extra.index('--army-points') + 1])
        files = sorted(glob.glob(os.path.join(BOOKS, books, '*.fdgbook')))
        for side in (1, 2):
            b = rng.choice(files); picked.append(os.path.basename(b))
            dst = os.path.join(work, f'army{side}.fdgarmy')
            r = subprocess.run([EXE, '--headless', '--random-army', b, str(pts), str(seed + side), dst],
                               capture_output=True, text=True, timeout=120)
            if r.returncode != 0 or not os.path.exists(dst):
                return {'tag': tag, 'status': 'army-build-failed', 'books': picked, 'tail': (r.stdout + r.stderr)[-1500:]}
            args += [f'--army{side}', dst]
        args += ['--army-points', str(pts)]
    args += extra
    env = dict(os.environ, FDG_MISSION_AUDIT='1', FDG_HEIGHTS_AUDIT='1', FDG_CITY_AUDIT='1')
    try:
        r = subprocess.run(args, capture_output=True, text=True, timeout=600, stdin=subprocess.DEVNULL, cwd=REPO, env=env)
        text = r.stdout + r.stderr
        status = 'ok' if r.returncode == 0 else f'exit-{r.returncode}'
    except subprocess.TimeoutExpired as t:
        out = t.stdout or b''
        text = out.decode(errors='replace') if isinstance(out, bytes) else out
        status = 'timeout'
    open(os.path.join(work, 'log.txt'), 'w').write(text)
    bad = sorted(set(m.group(0) for m in BAD.finditer(text)))
    result = re.findall(r'Game result: .*', text)
    if status == 'ok' and bad: status = 'errors-in-log'
    if status == 'ok' and not result: status = 'no-result'
    return {'tag': tag, 'status': status, 'books': picked, 'seed': seed, 'result': result[-1] if result else '',
            'bad': bad, 'args': ' '.join(args[1:]), 'tail': text[-2500:] if status != 'ok' else ''}


def main():
    n = int(sys.argv[1]) if len(sys.argv) > 1 else 1
    jobs = int(sys.argv[2]) if len(sys.argv) > 2 else 2
    outdir = sys.argv[3] if len(sys.argv) > 3 else '/tmp/matrix'
    only = [s for s in os.environ.get('MATRIX_ONLY', '').split(',') if s]
    skip = [s for s in os.environ.get('MATRIX_SKIP', '').split(',') if s]
    base = int(os.environ.get('SWEEP_SEED', '9100'))
    os.makedirs(outdir, exist_ok=True)
    work = []
    for k, (name, game, mode, extra) in enumerate(cells()):
        if only and not any(o in name for o in only): continue
        if skip and any(o in name for o in skip): continue
        for i in range(n):
            work.append((name, game, mode, extra, i, base + k * 37 + i * 1009, outdir))
    print(f'{len(work)} games', flush=True)
    results = []
    with cf.ThreadPoolExecutor(jobs) as ex:
        for res in ex.map(run, work):
            results.append(res)
            print(res['tag'], res['status'], res.get('result', ''), flush=True)
            json.dump(results, open(os.path.join(outdir, 'results.json'), 'w'), indent=1)
    by = {}
    for r in results: by.setdefault(r['status'], []).append(r['tag'])
    print({k: len(v) for k, v in by.items()})
    for k, v in by.items():
        if k != 'ok': print(k, ' '.join(v))


if __name__ == '__main__':
    main()
