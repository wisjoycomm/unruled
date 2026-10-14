"""Print a coarse grid of pixel colours from a capture, to ground-truth what is
actually on screen instead of guessing from a downscaled view.

Usage:
  python tools/ld/probe.py <name> [xlo ylo xhi yhi] [step]
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _img import load_cap  # noqa: E402


def main():
    name = sys.argv[1]
    _, px, w, h = load_cap(name)

    if len(sys.argv) > 5:
        xlo, ylo, xhi, yhi = (int(v) for v in sys.argv[2:6])
    else:
        xlo, ylo, xhi, yhi = 0, 0, w, h
    step = int(sys.argv[6]) if len(sys.argv) > 6 else 100

    print(f"image {w}x{h}  window x {xlo}..{xhi} y {ylo}..{yhi} step {step}")
    header = "      " + "".join(f"{x:>14d}" for x in range(xlo, xhi, step))
    print(header)
    for y in range(ylo, yhi, step):
        cells = []
        for x in range(xlo, xhi, step):
            if 0 <= x < w and 0 <= y < h:
                cells.append(f"{str(px[x, y]):>14s}")
            else:
                cells.append(f"{'-':>14s}")
        print(f"{y:>5d} " + "".join(cells))


if __name__ == "__main__":
    main()
