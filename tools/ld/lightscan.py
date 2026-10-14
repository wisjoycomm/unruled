"""Print, per scanline, the contiguous run of 'light' pixels, so the edges of a
light popup sheet can be read off directly.

Usage:
  python tools/ld/lightscan.py <name> row <y> [thresh]
  python tools/ld/lightscan.py <name> col <x> [thresh]
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _img import DH, DW, load_cap  # noqa: E402


def runs(vals, thresh):
    """Longest contiguous run of indices whose pixel is lighter than thresh."""
    best = cur = None
    for i, c in vals:
        light = c[0] >= thresh and c[1] >= thresh and c[2] >= thresh
        if light:
            cur = (i, i) if cur is None else (cur[0], i)
            if best is None or (cur[1] - cur[0]) > (best[1] - best[0]):
                best = cur
        else:
            cur = None
    return best


def main():
    name, mode, idx = sys.argv[1], sys.argv[2], int(sys.argv[3])
    thresh = int(sys.argv[4]) if len(sys.argv) > 4 else 170

    _, px, w, h = load_cap(name)

    if mode == "row":
        vals = [(x, px[x, idx]) for x in range(w)]
        r = runs(vals, thresh)
        if r:
            sx = DW / w
            print(f"row y={idx}: x {r[0]}..{r[1]} (w={r[1] - r[0] + 1})  "
                  f"design w={(r[1] - r[0] + 1) * sx:.1f} "
                  f"cx={((r[0] + r[1]) / 2 - w / 2) * sx:+.1f}")
        else:
            print(f"row y={idx}: none")
    else:
        vals = [(y, px[idx, y]) for y in range(h)]
        r = runs(vals, thresh)
        if r:
            sy = DH / h
            print(f"col x={idx}: y {r[0]}..{r[1]} (h={r[1] - r[0] + 1})  "
                  f"design h={(r[1] - r[0] + 1) * sy:.1f} "
                  f"topFromBottom={(h - r[0]) * sy:.1f} botFromBottom={(h - r[1]) * sy:.1f}")
        else:
            print(f"col x={idx}: none")


if __name__ == "__main__":
    main()
