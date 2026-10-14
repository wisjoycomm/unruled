"""Find the bounding box of the bright popup card in a capture (near-white panel
on a darker backdrop) and report it in 640x1136 design units.

Usage: python tools/ld/card.py <name> [minlum]
"""
import os
import sys

from PIL import Image

OUT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".ldcap"))
DW, DH = 640.0, 1136.0


def main():
    name = sys.argv[1]
    minlum = int(sys.argv[2]) if len(sys.argv) > 2 else 215

    im = Image.open(os.path.join(OUT, f"{name}.png")).convert("RGB")
    px = im.load()
    w, h = im.size

    minx, miny, maxx, maxy = w, h, -1, -1
    for y in range(h):
        for x in range(w):
            r, g, b = px[x, y]
            if r >= minlum and g >= minlum and b >= minlum:
                if x < minx:
                    minx = x
                if x > maxx:
                    maxx = x
                if y < miny:
                    miny = y
                if y > maxy:
                    maxy = y

    if maxx < 0:
        print("no bright card found")
        return

    sx, sy = DW / w, DH / h
    print(f"image {w}x{h}")
    print(f"card px  x{minx}..{maxx} ({maxx-minx+1})  y{miny}..{maxy} ({maxy-miny+1})")
    print(f"design   w={(maxx-minx+1)*sx:.1f} h={(maxy-miny+1)*sy:.1f}")
    print(f"design   cx={((minx+maxx)/2 - w/2)*sx:+.1f}  "
          f"top(from bottom)={(h-miny)*sy:.1f}  bottom(from bottom)={(h-maxy)*sy:.1f}")


if __name__ == "__main__":
    main()
