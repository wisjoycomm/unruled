#!/usr/bin/env python3
"""Render named atlas sprites from the exported project into one labelled contact sheet PNG.

Usage: python tools/ui/sprite_sheet.py out.png sprite_a sprite_b ...
Looks up each sprite in the Texture2D/*.meta spriteSheet tables (rect is bottom-left origin).
"""
import glob
import os
import re
import sys

from PIL import Image, ImageDraw

sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))
import project_cfg  # noqa: E402

ROOT = str(project_cfg.ROOT)
TEX = str(project_cfg.EXPORTED_ASSETS_DIR / "Texture2D")


def find(names):
    want = set(names)
    found = {}
    atlas = str(project_cfg.ATLAS_DIR)
    for meta in glob.glob(os.path.join(atlas, "*.png.meta")) + glob.glob(os.path.join(TEX, "sactx-*.meta")):
        text = open(meta, encoding="utf-8", errors="ignore").read()
        if not any(n in text for n in want):
            continue
        for m in re.finditer(r"- serializedVersion: 2\s*\n\s*name: (\S+)\s*\n\s*rect:\s*\n\s*serializedVersion: 2\s*\n\s*x: ([\d.]+)\s*\n\s*y: ([\d.]+)\s*\n\s*width: ([\d.]+)\s*\n\s*height: ([\d.]+)", text):
            if m.group(1) in want and m.group(1) not in found:
                found[m.group(1)] = (meta[:-5], [float(m.group(i)) for i in range(2, 6)])
    return found


def main(out, names):
    found = find(names)
    cell = 170
    cols = 6
    rows = (len(names) + cols - 1) // cols
    sheet = Image.new("RGBA", (cols * cell, rows * (cell + 16)), (40, 60, 90, 255))
    draw = ImageDraw.Draw(sheet)
    cache = {}
    for i, n in enumerate(names):
        x0, y0 = (i % cols) * cell, (i // cols) * (cell + 16)
        draw.text((x0 + 2, y0 + cell + 2), n[:28], fill=(255, 255, 255, 255))
        if n not in found:
            draw.text((x0 + 10, y0 + 70), "MISSING", fill=(255, 80, 80, 255))
            continue
        png, (x, y, w, h) = found[n]
        if png not in cache:
            cache[png] = Image.open(png).convert("RGBA")
        img = cache[png]
        crop = img.crop((int(x), int(img.height - y - h), int(x + w), int(img.height - y)))
        crop.thumbnail((cell - 6, cell - 6))
        sheet.alpha_composite(crop, (x0 + 3, y0 + 3))
        draw.text((x0 + 2, y0 + 2), "%dx%d" % (w, h), fill=(255, 255, 0, 255))
    sheet.save(out)
    print("saved", out, len(found), "/", len(names))


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2:])
