"""Find the bounding box of a bright UI panel in a capture and sample its colors.

Usage:
  python tools/ld/panelbox.py <capture> <l> <t> <r> <b> [--thresh N]
     scans the given search window for the panel rect (pixels brighter than thresh)
  python tools/ld/panelbox.py <capture> px <x> <y>            sample one pixel
  python tools/ld/panelbox.py <capture> row <y> <l> <r>       print color runs along a row
  python tools/ld/panelbox.py <capture> col <x> <t> <b>       print color runs down a column
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _img import load_cap  # noqa: E402


def runs(pixels, start):
    """Collapse a list of colors into (color, from, to) runs."""
    out = []
    prev = None
    begin = start
    for i, c in enumerate(pixels, start):
        if prev is None:
            prev, begin = c, i
            continue
        if max(abs(a - b) for a, b in zip(c, prev)) > 10:
            out.append((prev, begin, i - 1))
            prev, begin = c, i
    if prev is not None:
        out.append((prev, begin, start + len(pixels) - 1))
    return out


def main():
    name = sys.argv[1]
    _, px, _, _ = load_cap(name)
    mode = sys.argv[2]

    if mode == "px":
        x, y = int(sys.argv[3]), int(sys.argv[4])
        c = px[x, y]
        print(f"({x},{y}) rgb{c} #{c[0]:02X}{c[1]:02X}{c[2]:02X}")
        return

    if mode == "row":
        y, l, r = (int(v) for v in sys.argv[3:6])
        for c, a, b in runs([px[x, y] for x in range(l, r)], l):
            if b - a >= 2:
                print(f"x {a:4d}-{b:4d} ({b-a+1:4d}px) #{c[0]:02X}{c[1]:02X}{c[2]:02X}")
        return

    if mode == "col":
        x, t, b_ = (int(v) for v in sys.argv[3:6])
        for c, a, b in runs([px[x, y] for y in range(t, b_)], t):
            if b - a >= 2:
                print(f"y {a:4d}-{b:4d} ({b-a+1:4d}px) #{c[0]:02X}{c[1]:02X}{c[2]:02X}")
        return

    # bounding-box scan
    l, t, r, b = (int(v) for v in sys.argv[2:6])
    thresh = 150
    if "--thresh" in sys.argv:
        thresh = int(sys.argv[sys.argv.index("--thresh") + 1])
    xs, ys = [], []
    for y in range(t, b):
        for x in range(l, r):
            if sum(px[x, y]) / 3 >= thresh:
                xs.append(x)
                ys.append(y)
    if not xs:
        print("no bright pixels found")
        return
    print(f"bbox l={min(xs)} t={min(ys)} r={max(xs)} b={max(ys)} "
          f"w={max(xs)-min(xs)+1} h={max(ys)-min(ys)+1}")


if __name__ == "__main__":
    main()
