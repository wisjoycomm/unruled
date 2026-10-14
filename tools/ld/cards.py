"""Locate hero-card rectangles in a Heroes-tab capture by finding columns/rows
whose colour differs from the panel's flat background.

The Heroes list sits on a flat blue sheet, so projecting "non-background" pixel
counts onto each axis gives clean card bands.

Usage:
  python tools/ld/cards.py <name> <xlo> <ylo> <xhi> <yhi> [bgTolerance]
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _img import DH, DW, load_cap  # noqa: E402


def bands(counts, lo, floor):
    """Contiguous index ranges where count exceeds floor."""
    out, start = [], None
    for i, c in enumerate(counts):
        if c > floor:
            if start is None:
                start = i
        elif start is not None:
            out.append((start + lo, i - 1 + lo))
            start = None
    if start is not None:
        out.append((start + lo, len(counts) - 1 + lo))
    return [b for b in out if b[1] - b[0] >= 8]


def main():
    name = sys.argv[1]
    xlo, ylo, xhi, yhi = (int(v) for v in sys.argv[2:6])
    tol = int(sys.argv[6]) if len(sys.argv) > 6 else 40

    _, px, w, h = load_cap(name)
    xhi, yhi = min(xhi, w), min(yhi, h)

    # Background = the most common colour inside the window (the flat sheet).
    hist = {}
    for y in range(ylo, yhi, 3):
        for x in range(xlo, xhi, 3):
            hist[px[x, y]] = hist.get(px[x, y], 0) + 1
    bg = max(hist, key=hist.get)

    def differs(c):
        return abs(c[0] - bg[0]) + abs(c[1] - bg[1]) + abs(c[2] - bg[2]) > tol

    colc = [sum(1 for y in range(ylo, yhi) if differs(px[x, y])) for x in range(xlo, xhi)]
    rowc = [sum(1 for x in range(xlo, xhi) if differs(px[x, y])) for y in range(ylo, yhi)]

    sx, sy = DW / w, DH / h
    print(f"background {bg}")
    print("columns:")
    for a, b in bands(colc, xlo, (yhi - ylo) * 0.06):
        print(f"  x {a}..{b} (w={b - a + 1})  design w={(b - a + 1) * sx:.1f} "
              f"cx={((a + b) / 2 - w / 2) * sx:+.1f}")
    print("rows:")
    for a, b in bands(rowc, ylo, (xhi - xlo) * 0.06):
        print(f"  y {a}..{b} (h={b - a + 1})  design h={(b - a + 1) * sy:.1f} "
              f"cyFromBottom={(h - (a + b) / 2) * sy:.1f}")


if __name__ == "__main__":
    main()
