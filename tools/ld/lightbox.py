"""Find the bounding box of the light (near-white / pale blue) popup panel in a
capture, and report it in 640x1136 design units.

The Hero Overall Stats popup is a light sheet over a dark scene, so thresholding
on brightness isolates it cleanly.

Usage:
  python tools/ld/lightbox.py <name> [minBrightness] [xlo ylo xhi yhi]
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _img import DH, DW, bright_bbox, load_cap  # noqa: E402


def main():
    name = sys.argv[1]
    thresh = int(sys.argv[2]) if len(sys.argv) > 2 else 200

    _, px, w, h = load_cap(name)

    # Optional search window, so stray bright pixels elsewhere on screen (HUD text,
    # the hero portrait) do not inflate the bounding box.
    window = tuple(int(v) for v in sys.argv[3:7]) if len(sys.argv) > 6 else None
    box = bright_bbox(px, w, h, thresh=thresh, window=window)
    if box is None:
        print("no light pixels found")
        return

    x0, x1, y0, y1 = box
    sx, sy = DW / w, DH / h
    print(f"px   x {x0}..{x1} (w={x1 - x0 + 1})  y {y0}..{y1} (h={y1 - y0 + 1})")
    print(f"design w={(x1 - x0 + 1) * sx:.1f} h={(y1 - y0 + 1) * sy:.1f}")
    print(f"design centre x={((x0 + x1) / 2 - w / 2) * sx:+.1f} "
          f"yFromBottom={(h - (y0 + y1) / 2) * sy:.1f}")


if __name__ == "__main__":
    main()
