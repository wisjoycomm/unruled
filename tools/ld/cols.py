"""Report vertical runs (columns) of non-background pixels inside a row band.
Used to split a horizontal UI band into its individual elements.

Usage:
  python tools/ld/cols.py <name> <top> <bottom> [thresh] [gap]
"""
import os
import sys

from PIL import Image

OUT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".ldcap"))


def main():
    name = sys.argv[1]
    top = int(sys.argv[2])
    bot = int(sys.argv[3])
    thresh = int(sys.argv[4]) if len(sys.argv) > 4 else 26
    gap = int(sys.argv[5]) if len(sys.argv) > 5 else 6

    im = Image.open(os.path.join(OUT, f"{name}.png")).convert("RGB")
    px = im.load()

    hits = []
    for x in range(im.width):
        on = False
        for y in range(top, min(bot, im.height)):
            bg = px[4, y]
            p = px[x, y]
            if abs(p[0] - bg[0]) + abs(p[1] - bg[1]) + abs(p[2] - bg[2]) > thresh:
                on = True
                break
        hits.append(on)

    runs = []
    cur = None
    for x, on in enumerate(hits):
        if on:
            if cur is None:
                cur = [x, x]
            else:
                cur[1] = x
        elif cur is not None and x - cur[1] > gap:
            runs.append(tuple(cur))
            cur = None
    if cur:
        runs.append(tuple(cur))

    w, h = im.size
    for lo, hi in runs:
        if hi - lo < 3:
            continue
        # Also report in 640x1136 design units for a 1080x1920 capture.
        s = 640.0 / w
        print(f"x {lo}..{hi} (w={hi - lo + 1})  cx={(lo + hi) / 2:.0f}   "
              f"design: cx={((lo + hi) / 2 - w / 2) * s:+.1f} w={(hi - lo + 1) * s:.1f}")


if __name__ == "__main__":
    main()
