"""Find the bounding box of the light (near-white / pale blue) popup panel in a
capture, and report it in 640x1136 design units.

The Hero Overall Stats popup is a light sheet over a dark scene, so thresholding
on brightness isolates it cleanly.

Usage:
  python tools/ld/lightbox.py <name> [minBrightness] [xlo ylo xhi yhi]
"""
import os
import sys

from PIL import Image

OUT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".ldcap"))
DW, DH = 640.0, 1136.0


def main():
    name = sys.argv[1]
    thresh = int(sys.argv[2]) if len(sys.argv) > 2 else 200

    im = Image.open(os.path.join(OUT, f"{name}.png")).convert("RGB")
    px = im.load()
    w, h = im.size

    # Optional search window, so stray bright pixels elsewhere on screen (HUD text,
    # the hero portrait) do not inflate the bounding box.
    if len(sys.argv) > 6:
        xlo, ylo, xhi, yhi = (int(v) for v in sys.argv[3:7])
    else:
        xlo, ylo, xhi, yhi = 0, 0, w, h

    xs, ys = [], []
    for y in range(max(0, ylo), min(yhi, h)):
        for x in range(max(0, xlo), min(xhi, w)):
            r, g, b = px[x, y]
            if r >= thresh and g >= thresh and b >= thresh:
                xs.append(x)
                ys.append(y)

    if not xs:
        print("no light pixels found")
        return

    x0, x1, y0, y1 = min(xs), max(xs), min(ys), max(ys)
    sx, sy = DW / w, DH / h
    print(f"px   x {x0}..{x1} (w={x1 - x0 + 1})  y {y0}..{y1} (h={y1 - y0 + 1})")
    print(f"design w={(x1 - x0 + 1) * sx:.1f} h={(y1 - y0 + 1) * sy:.1f}")
    print(f"design centre x={((x0 + x1) / 2 - w / 2) * sx:+.1f} "
          f"yFromBottom={(h - (y0 + y1) / 2) * sy:.1f}")


if __name__ == "__main__":
    main()
