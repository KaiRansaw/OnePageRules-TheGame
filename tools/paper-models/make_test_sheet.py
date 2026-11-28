"""Makes a stand-in paper-miniature sheet PDF (white page, coloured figures with a fold line) to prove the Paper
models cutter in the cloud, where OPR's real sheets aren't available. Kai's real sheets are the real test.

    python tools/paper-models/make_test_sheet.py out.pdf
"""
import random, sys
from PIL import Image, ImageDraw

DPI = 150
W, H = int(8.27 * DPI), int(11.69 * DPI)  # A4


def figure(draw, x, y, w, h, colour, flip, seed):
    """A little soldier in a w x h box at (x, y); flip = drawn upside down (the fold-back copy)."""
    rnd = random.Random(seed)
    def Y(v):  # v in 0..1 from the head down
        return y + (h - v * h if flip else v * h)
    def box(x0, v0, x1, v1, c):
        a, b = Y(v0), Y(v1)
        draw.rectangle([x + x0 * w, min(a, b), x + x1 * w, max(a, b)], fill=c, outline=(20, 20, 20), width=2)
    def ell(x0, v0, x1, v1, c):
        a, b = Y(v0), Y(v1)
        draw.ellipse([x + x0 * w, min(a, b), x + x1 * w, max(a, b)], fill=c, outline=(20, 20, 20), width=2)
    dark = tuple(max(0, c - 70) for c in colour)
    skin = (226, 180, 140)
    ell(0.33, 0.02, 0.67, 0.2, skin)                 # head
    box(0.22, 0.19, 0.78, 0.55, colour)              # body
    box(0.05, 0.22, 0.22, 0.5, dark)                 # arms
    box(0.78, 0.22, 0.95, 0.5, dark)
    box(0.8, 0.12 + rnd.random() * 0.1, 0.9, 0.5, (90, 90, 90))  # a gun
    box(0.27, 0.55, 0.48, 0.92, dark)                # legs
    box(0.52, 0.55, 0.73, 0.92, dark)
    box(0.0, 0.92, 1.0, 1.0, (70, 110, 60))          # base strip
    # a white highlight inside the body (must stay opaque)
    box(0.42, 0.3, 0.5, 0.38, (255, 255, 255))


def page(seed, rows, cols):
    img = Image.new("RGB", (W, H), (255, 255, 255))
    d = ImageDraw.Draw(img)
    d.text((60, 40), "TEST PAPER MINIATURES - NOT OPR ART", fill=(0, 0, 0))
    rnd = random.Random(seed)
    fw, fh = int(0.9 * DPI), int(1.5 * DPI)   # one figure ~ 23 x 38 mm
    gap = int(0.35 * DPI)
    x0, y0 = int(0.6 * DPI), int(0.8 * DPI)
    for r in range(rows):
        for c in range(cols):
            colour = tuple(rnd.randint(40, 200) for _ in range(3))
            x = x0 + c * (fw + gap)
            y = y0 + r * (2 * fh + gap + 6)
            figure(d, x, y, fw, fh, colour, True, seed * 100 + r * 10 + c)       # the back, upside down, on top
            d.line([x - 4, y + fh + 3, x + fw + 4, y + fh + 3], fill=(120, 120, 120), width=2)  # fold line
            figure(d, x, y + fh + 6, fw, fh, colour, False, seed * 100 + r * 10 + c)  # the front, upright
    return img


if __name__ == "__main__":
    out = sys.argv[1] if len(sys.argv) > 1 else "test-sheet.pdf"
    p1, p2 = page(1, 3, 5), page(2, 2, 4)
    p1.save(out, "PDF", resolution=DPI, save_all=True, append_images=[p2])
    print(out)
