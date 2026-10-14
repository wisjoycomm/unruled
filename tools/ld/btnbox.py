"""Measure a bright UI button's bounding box in an image, in design units.

Usage:
  python tools/ld/btnbox.py <image-path> [top-fraction]

Scans the lower part of the image for the saturated blue button plate and
reports its box in source pixels and in 640x1136 canvas design units.
"""
import sys

from PIL import Image

DESIGN_W = 640.0


def main():
    path = sys.argv[1]
    frac = float(sys.argv[2]) if len(sys.argv) > 2 else 0.85

    im = Image.open(path).convert("RGB")
    w, h = im.size
    px = im.load()

    y0 = int(h * frac)
    xs, ys = [], []
    for y in range(y0, h):
        for x in range(w):
            r, g, b = px[x, y]
            # The button plate is a bright, strongly blue-dominant fill.
            if b > 150 and b - r > 45 and g > 90:
                xs.append(x)
                ys.append(y)

    if not xs:
        print("no button-like pixels found")
        return

    l, r_, t, b_ = min(xs), max(xs), min(ys), max(ys)
    scale = DESIGN_W / w
    print(f"image {w}x{h}   scan from y={y0}")
    print(f"box px      = ({l},{t})-({r_},{b_})  size {r_-l+1}x{b_-t+1}")
    print(f"center px   = ({(l+r_)/2:.1f}, {(t+b_)/2:.1f})")
    print(f"design size = {(r_-l+1)*scale:.1f} x {(b_-t+1)*scale:.1f}")
    print(f"design cx   = {((l+r_)/2 - w/2)*scale:+.1f}  (0 = screen centre)")
    print(f"design y    = {(h - (t+b_)/2)*scale:.1f} above bottom")


if __name__ == "__main__":
    main()
