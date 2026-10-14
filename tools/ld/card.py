"""Find the bounding box of the bright popup card in a capture (near-white panel
on a darker backdrop) and report it in 640x1136 design units.

Usage: python tools/ld/card.py <name> [minlum]
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _img import DH, DW, bright_bbox, load_cap  # noqa: E402


def main():
    name = sys.argv[1]
    minlum = int(sys.argv[2]) if len(sys.argv) > 2 else 215

    _, px, w, h = load_cap(name)
    box = bright_bbox(px, w, h, thresh=minlum)
    if box is None:
        print("no bright card found")
        return

    minx, maxx, miny, maxy = box
    sx, sy = DW / w, DH / h
    print(f"image {w}x{h}")
    print(f"card px  x{minx}..{maxx} ({maxx-minx+1})  y{miny}..{maxy} ({maxy-miny+1})")
    print(f"design   w={(maxx-minx+1)*sx:.1f} h={(maxy-miny+1)*sy:.1f}")
    print(f"design   cx={((minx+maxx)/2 - w/2)*sx:+.1f}  "
          f"top(from bottom)={(h-miny)*sy:.1f}  bottom(from bottom)={(h-maxy)*sy:.1f}")


if __name__ == "__main__":
    main()
