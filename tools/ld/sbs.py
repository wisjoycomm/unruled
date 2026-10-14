"""Side-by-side compare an LD capture and a Unity capture over the same
design-space band (640x1136 reference), scaled to a common width.

Usage:
  python tools/ld/sbs.py <ld> <unity> <dy0> <dy1> <out> [dx0] [dx1]

dy0/dy1 are design-space Y from the BOTTOM of the screen (0 = bottom edge).
dx0/dx1 are design-space X relative to centre (default -320..320).
"""
import os
import sys

from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _img import DH, DW, OUT  # noqa: E402


def band(path, dy0, dy1, dx0, dx1, width):
    im = Image.open(path).convert("RGB")
    w, h = im.size
    sx, sy = w / DW, h / DH
    x0 = int(round((dx0 + DW / 2) * sx))
    x1 = int(round((dx1 + DW / 2) * sx))
    y0 = int(round(h - dy1 * sy))
    y1 = int(round(h - dy0 * sy))
    crop = im.crop((max(0, x0), max(0, y0), min(w, x1), min(h, y1)))
    return crop.resize((width, max(1, int(crop.height * width / crop.width))), Image.LANCZOS)


def main():
    ld, unity, dy0, dy1, out = sys.argv[1], sys.argv[2], float(sys.argv[3]), float(sys.argv[4]), sys.argv[5]
    dx0 = float(sys.argv[6]) if len(sys.argv) > 6 else -320.0
    dx1 = float(sys.argv[7]) if len(sys.argv) > 7 else 320.0

    width = 880
    a = band(os.path.join(OUT, f"{ld}.png"), dy0, dy1, dx0, dx1, width)
    b = band(os.path.join(OUT, f"{unity}.png"), dy0, dy1, dx0, dx1, width)

    pad = 14
    canvas = Image.new("RGB", (width, a.height + b.height + pad * 3), (28, 28, 34))
    canvas.paste(a, (0, pad))
    canvas.paste(b, (0, pad * 2 + a.height))
    canvas.save(os.path.join(OUT, f"{out}.png"))
    print(f"{out}.png  top=LD({a.size})  bottom=Unity({b.size})  designY {dy0}..{dy1}")


if __name__ == "__main__":
    main()
