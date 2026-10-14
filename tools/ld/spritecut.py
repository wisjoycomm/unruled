"""Cut a named sprite out of its atlas so it can be compared with the original UI.

Usage: python tools/ld/spritecut.py <sprite> [...]   -> .ldcap/sp_<sprite>.png
Also prints the sprite's rect, border (9-slice) and mean colour.
"""
import glob
import os
import re
import sys

from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
ATLAS = os.path.join(ROOT, "UnityAssets", "Assets", "_EndlessWinter", "Art", "UI", "Atlas")
OUT = os.path.join(ROOT, ".ldcap")

ENTRY = re.compile(
    r"- serializedVersion: \d+\s+name: (\S+).*?"
    r"rect:\s+serializedVersion: \d+\s+x: ([\d.]+)\s+y: ([\d.]+)\s+width: ([\d.]+)\s+height: ([\d.]+)"
    r".*?border: \{x: ([-\d.]+), y: ([-\d.]+), z: ([-\d.]+), w: ([-\d.]+)\}",
    re.S)


def table():
    out = {}
    for meta in glob.glob(os.path.join(ATLAS, "*.png.meta")):
        text = open(meta, encoding="utf-8").read()
        png = meta[:-5]
        for m in ENTRY.finditer(text):
            out[m.group(1)] = (png, *[float(v) for v in m.groups()[1:]])
    return out


def main():
    t = table()
    os.makedirs(OUT, exist_ok=True)
    for name in sys.argv[1:]:
        if name not in t:
            print(f"{name}: not found")
            continue
        png, x, y, w, h, bx, by, bz, bw = t[name]
        im = Image.open(png).convert("RGBA")
        # Unity sprite rects are bottom-left origin.
        box = (int(x), int(im.height - y - h), int(x + w), int(im.height - y))
        sp = im.crop(box)
        dst = os.path.join(OUT, f"sp_{name}.png")
        sp.save(dst)
        px = sp.convert("RGB").resize((1, 1)).getpixel((0, 0))
        print(f"{name}: {os.path.basename(png)} rect {int(w)}x{int(h)} at {box} "
              f"border({bx:.0f},{by:.0f},{bz:.0f},{bw:.0f}) mean rgb{px} -> {dst}")


if __name__ == "__main__":
    main()
