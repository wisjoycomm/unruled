"""Scan a single row / column of a capture and print where large colour steps
happen, so panel edges can be located exactly.

Usage:
  python tools/ld/edge.py <name> row <y> [xlo] [xhi]
  python tools/ld/edge.py <name> col <x> [ylo] [yhi]
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _img import DH, DW, load_cap  # noqa: E402


def main():
    name, mode, idx = sys.argv[1], sys.argv[2], int(sys.argv[3])
    _, px, w, h = load_cap(name)

    if mode == "row":
        lo = int(sys.argv[4]) if len(sys.argv) > 4 else 0
        hi = int(sys.argv[5]) if len(sys.argv) > 5 else w
        seq = [(x, px[x, idx]) for x in range(lo, min(hi, w))]
        scale, half = DW / w, w / 2
    else:
        lo = int(sys.argv[4]) if len(sys.argv) > 4 else 0
        hi = int(sys.argv[5]) if len(sys.argv) > 5 else h
        seq = [(y, px[idx, y]) for y in range(lo, min(hi, h))]
        scale, half = DH / h, None

    prev = None
    for i, c in seq:
        if prev is not None:
            d = abs(c[0] - prev[0]) + abs(c[1] - prev[1]) + abs(c[2] - prev[2])
            if d > 60:
                if mode == "row":
                    print(f"x={i:5d} design {(i - half) * scale:+8.1f}  {prev} -> {c}  d={d}")
                else:
                    print(f"y={i:5d} designFromBottom {(h - i) * scale:8.1f}  {prev} -> {c}  d={d}")
        prev = c


if __name__ == "__main__":
    main()
