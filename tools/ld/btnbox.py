"""Measure a bright UI button's bounding box in an image, in design units.

Usage:
  python tools/ld/btnbox.py <image-path> [top-fraction]

Scans the lower part of the image for the saturated blue button plate and
reports its box in source pixels and in 640x1136 canvas design units.
"""
import os
import sys
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _img import DW as DESIGN_W, load_cap_np, to_real_span  # noqa: E402


def main():
    path = sys.argv[1]
    frac = float(sys.argv[2]) if len(sys.argv) > 2 else 0.85

    arr, w, h, scale_factor = load_cap_np(path)
    sh, sw = arr.shape[:2]
    sy0 = int(sh * frac)
    sub = arr[sy0:, :, :].astype(np.int16)
    r, g, b = sub[:, :, 0], sub[:, :, 1], sub[:, :, 2]
    mask = (b > 150) & ((b - r) > 45) & (g > 90)
    if not mask.any():
        print("no button-like pixels found")
        return

    ys, xs = np.nonzero(mask)
    l, r_ = to_real_span(int(xs.min()), int(xs.max()), scale_factor)
    t, b_ = to_real_span(int(ys.min() + sy0), int(ys.max() + sy0), scale_factor)

    scale = DESIGN_W / w
    print(f"image {w}x{h} (analyzed at {sw}x{sh})   scan from y={int(round(sy0 * scale_factor))}")
    print(f"box px      = ({l},{t})-({r_},{b_})  size {r_-l+1}x{b_-t+1}")
    print(f"center px   = ({(l+r_)/2:.1f}, {(t+b_)/2:.1f})")
    print(f"design size = {(r_-l+1)*scale:.1f} x {(b_-t+1)*scale:.1f}")
    print(f"design cx   = {((l+r_)/2 - w/2)*scale:+.1f}  (0 = screen centre)")
    print(f"design y    = {(h - (t+b_)/2)*scale:.1f} above bottom")


if __name__ == "__main__":
    main()
