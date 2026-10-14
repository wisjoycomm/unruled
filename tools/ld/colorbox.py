"""Find the bounding box of pixels matching a target color in a capture.

Usage:
  python tools/ld/colorbox.py <capture> <#RRGGBB> [tol] [l t r b]
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _img import load_cap  # noqa: E402


def main():
    name = sys.argv[1]
    hexv = sys.argv[2].lstrip("#")
    target = tuple(int(hexv[i:i + 2], 16) for i in (0, 2, 4))
    tol = int(sys.argv[3]) if len(sys.argv) > 3 else 12

    _, px, w, h = load_cap(name)
    if len(sys.argv) > 7:
        l, t, r, b = (int(v) for v in sys.argv[4:8])
    else:
        l, t, r, b = 0, 0, w, h

    xs, ys = [], []
    rowcount = {}
    for y in range(t, b):
        n = 0
        for x in range(l, r):
            c = px[x, y]
            if max(abs(a - q) for a, q in zip(c, target)) <= tol:
                xs.append(x)
                ys.append(y)
                n += 1
        if n:
            rowcount[y] = n

    if not xs:
        print("no match")
        return
    print(f"bbox l={min(xs)} t={min(ys)} r={max(xs)} b={max(ys)} "
          f"w={max(xs)-min(xs)+1} h={max(ys)-min(ys)+1} px={len(xs)}")

    # widest rows help identify the true panel body vs. stray matches
    top = sorted(rowcount.items(), key=lambda kv: -kv[1])[:5]
    print("widest rows:", ", ".join(f"y={y}:{n}px" for y, n in top))


if __name__ == "__main__":
    main()
