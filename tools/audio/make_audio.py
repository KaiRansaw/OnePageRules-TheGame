#!/usr/bin/env python3
"""Builds the game's non-shot audio (lane 4, 2026-11-27): menu music, battle ambience, dice, round /
objective / morale / unit-destroyed cues. Everything is CC0: picked from Kenney's audio packs (www.kenney.nl, CC0 1.0)
or made here (the wind / rain / rumble beds are generated noise - no source recording).

Usage: make_audio.py <kenney Audio folder> <OprGodot/sounds>
The Kenney folder is the "Audio" folder of the Kenney all-in-one (subfolders "Interface Sounds", "Casino Audio"...).
Each file is loudness-matched so nothing is ear-splitting: music -20 LUFS, ambience -30 LUFS, cues peak -6 dBFS,
the menu tick / click stay the synthesized ones in UiJuice (measured afterwards with ffmpeg ebur128 - see check_audio.py).
"""
import os, subprocess, sys, tempfile
import numpy as np

KENNEY, OUT = sys.argv[1], sys.argv[2]

PICKS = {
    # folder/file in the Kenney Audio folder -> game file, kind
    "dice/dice_1.ogg": ("Casino Audio", "dice-throw-1", "cue"),
    "dice/dice_2.ogg": ("Casino Audio", "dice-throw-2", "cue"),
    "dice/dice_3.ogg": ("Casino Audio", "dice-throw-3", "cue"),
    "dice/die_1.ogg": ("Casino Audio", "die-throw-1", "cue"),
    "dice/die_2.ogg": ("Casino Audio", "die-throw-2", "cue"),
    "cues/round_start.ogg": ("Music Jingles", "jingles-hit_14", "cue"),
    "cues/round_over.ogg": ("Music Jingles", "jingles-hit_10", "cue"),
    "cues/objective_seized.ogg": ("Interface Sounds", "confirmation_002", "cue"),
    "cues/objective_lost.ogg": ("Interface Sounds", "error_004", "cue"),
    "cues/unit_destroyed.ogg": ("Impact Sounds", "impactPlate_heavy_002", "cue"),
    "cues/morale_shaken.ogg": ("Impact Sounds", "impactBell_heavy_001", "cue"),
    "cues/morale_routed.ogg": ("Impact Sounds", "impactBell_heavy_003", "cue"),
    "cues/victory.ogg": ("Music Loops", "Serious ident", "cue"),
    "cues/defeat.ogg": ("Music Jingles", "jingles-steel_16", "cue"),
    "music/menu_grimdark.ogg": ("Music Loops", "Space Cadet", "music"),
    "music/menu_fantasy.ogg": ("Music Loops", "Flowing Rocks", "music"),
}

TARGET = {"music": ("lufs", -20.0), "ambience": ("lufs", -30.0), "cue": ("peak", -6.0), "ui": ("peak", -12.0)}
RATE = 44100


def find(folder, name):
    for root, _, files in os.walk(os.path.join(KENNEY, folder)):
        if name + ".ogg" in files:
            return os.path.join(root, name + ".ogg")
    raise SystemExit(f"missing: {folder}/{name}.ogg")


def decode(path):
    raw = subprocess.run(["ffmpeg", "-v", "quiet", "-i", path, "-ac", "1", "-ar", str(RATE), "-f", "f32le", "-"],
                         capture_output=True, check=True).stdout
    return np.frombuffer(raw, dtype=np.float32).astype(np.float64)


def lufs(x):
    with tempfile.NamedTemporaryFile(suffix=".wav") as t:
        write_wav(t.name, x)
        r = subprocess.run(["ffmpeg", "-nostats", "-i", t.name, "-af", "ebur128", "-f", "null", "-"], capture_output=True, text=True)
    line = [l for l in r.stderr.splitlines() if l.strip().startswith("I:")][-1]
    return float(line.split()[1])


def write_wav(path, x):
    subprocess.run(["ffmpeg", "-v", "quiet", "-y", "-f", "f32le", "-ar", str(RATE), "-ac", "1", "-i", "-", path],
                   input=x.astype(np.float32).tobytes(), check=True)


def level(x, kind):
    how, target = TARGET[kind]
    if how == "peak":
        return x * (10 ** (target / 20) / max(1e-9, np.abs(x).max()))
    g = 10 ** ((target - lufs(x)) / 20)
    y = x * g
    peak = np.abs(y).max()
    return y * (0.89 / peak) if peak > 0.89 else y   # never past -1 dBFS


def save(rel, x):
    path = os.path.join(OUT, rel)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    subprocess.run(["ffmpeg", "-v", "quiet", "-y", "-f", "f32le", "-ar", str(RATE), "-ac", "1", "-i", "-",
                    "-c:a", "libvorbis", "-q:a", "5", path], input=x.astype(np.float32).tobytes(), check=True)
    print(rel, f"{len(x) / RATE:.1f}s")


def lowpass(x, cutoff):
    a = np.exp(-2 * np.pi * cutoff / RATE)
    y = np.empty_like(x)
    s = 0.0
    for i in range(len(x)):       # one-pole, run twice for 12 dB/oct
        s = (1 - a) * x[i] + a * s
        y[i] = s
    s = 0.0
    for i in range(len(y)):
        s = (1 - a) * y[i] + a * s
        y[i] = s
    return y


def seamless(x, fade):
    """The last `fade` seconds cross-faded into the start, so the loop has no seam."""
    n = int(fade * RATE)
    head, body, tail = x[:n], x[n:-n], x[-n:]
    t = np.linspace(0, 1, n)
    return np.concatenate([tail * (1 - t) + head * t, body])


def wind(seconds, seed, gusts):
    rng = np.random.default_rng(seed)
    n = int(seconds * RATE)
    noise = np.cumsum(rng.standard_normal(n))            # brown noise
    noise -= np.convolve(noise, np.ones(4410) / 4410, mode="same")
    bed = lowpass(noise, 500)
    lfo = np.zeros(n)
    for f, a in gusts:                                    # slow swells (whole cycles over the loop length)
        lfo += a * np.sin(2 * np.pi * f * np.arange(n) / RATE + rng.uniform(0, 6.28))
    return bed * (1 + lfo)


def rain(seconds, seed):
    rng = np.random.default_rng(seed)
    n = int(seconds * RATE)
    hiss = rng.standard_normal(n)
    hiss = hiss - lowpass(hiss, 900)                      # high-passed white noise
    drops = np.zeros(n)
    for i in rng.integers(0, n - 400, size=int(seconds * 60)):
        drops[i:i + 400] += rng.uniform(0.2, 1) * np.exp(-np.arange(400) / 60) * rng.standard_normal(400)
    return hiss * 0.6 + drops * 0.5


def main():
    for rel, (folder, name, kind) in PICKS.items():
        x = decode(find(folder, name))
        save(rel, level(x, kind))
    secs = 60
    base = wind(secs + 3, 11, [(1 / 60, 0.35), (3 / 60, 0.2)])
    save("ambience/wind.ogg", level(seamless(base, 3), "ambience"))
    # Grimdark: the same wind with distant rumbles (Kenney's low-frequency explosion, far off and muffled).
    boom = lowpass(decode(find("Sci-Fi Sounds", "lowFrequency_explosion_001")), 180)
    w = wind(secs + 3, 12, [(1 / 60, 0.35), (2 / 60, 0.25)])
    for at in (7, 26, 41, 53):
        i = int(at * RATE)
        k = min(len(boom), len(w) - i)
        w[i:i + k] += boom[:k] * (np.abs(w).max() * 1.4 / max(1e-9, np.abs(boom).max()))
    save("ambience/war.ogg", level(seamless(w, 3), "ambience"))
    save("ambience/rain.ogg", level(seamless(rain(secs + 3, 13), 3), "ambience"))


if __name__ == "__main__":
    main()
