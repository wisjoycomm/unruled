"""Shared image & design-space helpers for LDPlayer capture inspection tools."""
import os
from PIL import Image

OUT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".ldcap"))
DW, DH = 640.0, 1136.0


def cap_path(name: str, ext: str = ".png") -> str:
    """Resolve a capture name ('001' -> '.ldcap/001.png') or return an existing path."""
    if os.path.exists(name):
        return name
    return os.path.join(OUT, f"{name}{ext}")


def load_cap(name: str, mode: str = "RGB"):
    """Open a capture image in the given mode and return (im, px, w, h)."""
    im = Image.open(cap_path(name)).convert(mode)
    return im, im.load(), im.width, im.height


def bright_bbox(px, w: int, h: int, thresh: int = 200, window=None):
    """Find (x0, x1, y0, y1) bounding box of pixels with R,G,B >= thresh."""
    xlo, ylo, xhi, yhi = window if window is not None else (0, 0, w, h)
    xlo, ylo = max(0, xlo), max(0, ylo)
    xhi, yhi = min(w, xhi), min(h, yhi)
    minx, miny, maxx, maxy = w, h, -1, -1
    for y in range(ylo, yhi):
        for x in range(xlo, xhi):
            r, g, b = px[x, y]
            if r >= thresh and g >= thresh and b >= thresh:
                if x < minx:
                    minx = x
                if x > maxx:
                    maxx = x
                if y < miny:
                    miny = y
                if y > maxy:
                    maxy = y
    if maxx < 0:
        return None
    return minx, maxx, miny, maxy
