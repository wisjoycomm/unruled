"""Report horizontal bands of a capture: for each row, the bbox of pixels that
differ from the column-median background. Used to measure UI element rows.

Usage:
  python tools/ld/rows.py <name> <top> <bottom> [thresh]
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _img import load_cap  # noqa: E402


def main():
    name = sys.argv[1]
    top = int(sys.argv[2])
    bot = int(sys.argv[3])
    thresh = int(sys.argv[4]) if len(sys.argv) > 4 else 26

    im, px, w, _ = load_cap(name)

    # Background estimate per row = colour at the far-left edge (the gradient backdrop).
    bands = []
    cur = None
    for y in range(top, min(bot, im.height)):
        bg = px[4, y]
        xs = [x for x in range(w)
              if abs(px[x, y][0] - bg[0]) + abs(px[x, y][1] - bg[1]) + abs(px[x, y][2] - bg[2]) > thresh]
        if xs:
            lo, hi = xs[0], xs[-1]
            if cur and y - cur[1] <= 2:
                cur = (cur[0], y, min(cur[2], lo), max(cur[3], hi))
            else:
                if cur:
                    bands.append(cur)
                cur = (y, y, lo, hi)
        else:
            if cur and y - cur[1] > 2:
                bands.append(cur)
                cur = None
    if cur:
        bands.append(cur)

    for y0, y1, lo, hi in bands:
        if y1 - y0 < 3:
            continue
        print(f"y {y0}..{y1} (h={y1 - y0 + 1})  x {lo}..{hi} (w={hi - lo + 1})  cx={(lo + hi) / 2:.0f}")


if __name__ == "__main__":
    main()
