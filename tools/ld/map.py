"""Coarse ASCII luminance map of a capture, to work out what is on screen.

Usage: python tools/ld/map.py <name> [cols] [rows]
"""
import os
import sys

import numpy as np
from PIL import Image

OUT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".ldcap"))
RAMP = " .:-=+*#%@"

name = sys.argv[1] if len(sys.argv) > 1 else "gt3"
cols = int(sys.argv[2]) if len(sys.argv) > 2 else 54
rows = int(sys.argv[3]) if len(sys.argv) > 3 else 60

a = np.asarray(Image.open(os.path.join(OUT, name + ".png")).convert("RGB")).astype(float)
H, W, _ = a.shape
lum = a.mean(axis=2)
ch, cw = H / rows, W / cols
print(f"{name} {W}x{H}   each cell = {cw:.0f}x{ch:.0f}px   ramp '{RAMP}' dark->bright")
print("     " + "".join(str((x * 2 // 10) % 10) for x in range(cols)))
for r in range(rows):
    y0, y1 = int(r * ch), int((r + 1) * ch)
    line = ""
    for c in range(cols):
        x0, x1 = int(c * cw), int((c + 1) * cw)
        v = lum[y0:y1, x0:x1].mean()
        line += RAMP[min(9, int(v / 25.6))]
    print(f"{int(r*ch):4d} {line}")
