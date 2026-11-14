"""
Playtest sweep (2026-11-14): bots play whole games of every system and mode headless, with random factions and
dice, and every crash, hang, error or odd ending is collected for fixing.

Usage: python3 tools/sweep/sweep.py <games per system/mode> [jobs] [out dir]
Needs a built FdgRaylib (dotnet build FdgRaylib/FdgRaylib.csproj).
"""
import glob, json, os, random, re, subprocess, sys, concurrent.futures as cf

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
EXE = os.path.join(REPO, 'FdgRaylib', 'bin', 'Debug', 'net8.0', 'FdgRaylib')
BOOKS = os.path.join(REPO, 'FdgRaylib', 'Assets', 'Books')
PLANS = [  # system flag, mode flag, books folder, points, quest enemy folder
    ('gff', 'standard', 'GF Firefight', 300, None), ('gff', 'horde', 'GF Firefight', 300, None),
    ('aofs', 'standard', 'AoF Skirmish', 300, None), ('aofs', 'horde', 'AoF Skirmish', 300, None),
    ('gf', 'standard', 'Grimdark Future', 1000, None), ('gf', 'horde', 'Grimdark Future', 1000, None),
    ('aof', 'standard', 'Age of Fantasy', 1000, None), ('aof', 'horde', 'Age of Fantasy', 1000, None),
    ('gfsq', 'sqmission', None, 0, 'GF Star Quest'), ('gfsq', 'sqhorde', None, 0, 'GF Star Quest'),
    ('aofq', 'sqmission', None, 0, 'AoF Quest'), ('aofq', 'sqhorde', None, 0, 'AoF Quest'),
]
BAD = re.compile(r'(Unhandled exception|Exception:|   at FDG\.|FAULT|fault:|Fatal|threw|NullReference|InvalidOperation|IndexOutOfRange|KeyNotFound|ArgumentOutOfRange|Stack overflow)', re.I)


def run(job):
    i, (sysf, mode, books, pts, quest), seed, out = job
    rng = random.Random(seed)
    tag = f'{sysf}-{mode}-{i:03d}'
    work = os.path.join(out, tag)
    os.makedirs(work, exist_ok=True)
    args = [EXE, '--headless', '--all-ai', '--seed', str(seed), '--game-system', sysf, '--game-mode', mode]
    picked = []
    if quest:
        enemies = glob.glob(os.path.join(REPO, 'armies', quest, '*.fdgarmy'))
        e = rng.choice(enemies); picked.append(os.path.basename(e))
        args += ['--party', 'auto', '--army2', e]
    else:
        files = glob.glob(os.path.join(BOOKS, books, '*.fdgbook'))
        for side in (1, 2):
            b = rng.choice(files); picked.append(os.path.basename(b))
            dst = os.path.join(work, f'army{side}.fdgarmy')
            r = subprocess.run([EXE, '--headless', '--random-army', b, str(pts), str(seed + side), dst], capture_output=True, text=True, timeout=120)
            if r.returncode != 0 or not os.path.exists(dst):
                return {'tag': tag, 'status': 'army-build-failed', 'books': picked, 'tail': (r.stdout + r.stderr)[-1500:]}
            args += [f'--army{side}', dst]
        args += ['--army-points', str(pts)]
    try:
        r = subprocess.run(args, capture_output=True, text=True, timeout=420, stdin=subprocess.DEVNULL, cwd=REPO)
        text = r.stdout + r.stderr
        status = 'ok' if r.returncode == 0 else f'exit-{r.returncode}'
    except subprocess.TimeoutExpired as t:
        text = (t.stdout or b'').decode(errors='replace') if isinstance(t.stdout, bytes) else (t.stdout or '')
        status = 'timeout'
    open(os.path.join(work, 'log.txt'), 'w').write(text)
    bad = sorted(set(m.group(0) for m in BAD.finditer(text)))
    result = re.findall(r'Game result: .*', text)
    if status == 'ok' and bad: status = 'errors-in-log'
    if status == 'ok' and not result: status = 'no-result'
    return {'tag': tag, 'status': status, 'books': picked, 'seed': seed, 'result': result[-1] if result else '',
            'bad': bad, 'args': ' '.join(args[1:]), 'tail': text[-2500:] if status != 'ok' else ''}


def main():
    only = os.environ.get('SWEEP_ONLY')  # e.g. 'standard' to run only plans whose mode matches
    n = int(sys.argv[1]); jobs = int(sys.argv[2]) if len(sys.argv) > 2 else 4
    out = sys.argv[3] if len(sys.argv) > 3 else '/tmp/sweep'
    os.makedirs(out, exist_ok=True)
    work = [(i, plan, 5000 + i * 13 + k * 101, out) for k, plan in enumerate(PLANS) for i in range(n) if not only or plan[1] in only.split(',')]
    results = []
    with cf.ThreadPoolExecutor(jobs) as ex:
        for res in ex.map(run, work):
            results.append(res)
            print(res['tag'], res['status'], res.get('result', ''), flush=True)
    json.dump(results, open(os.path.join(out, 'results.json'), 'w'), indent=1)
    by = {}
    for r in results: by.setdefault(r['status'], []).append(r['tag'])
    print({k: len(v) for k, v in by.items()})


if __name__ == '__main__':
    main()
