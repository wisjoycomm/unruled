"""Crop named sprites out of the Unity atlas copies (Art/UI/Atlas/*.png + meta spriteSheet rects) into a
labelled contact sheet for visual checks.

Usage: python tools/ui/crop_sprites.py out.png name1 name2 ...
"""
import os
import re
import sys

import cv2
import numpy as np

sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))
import project_cfg  # noqa: E402

ROOT = str(project_cfg.ROOT)
ATLAS = str(project_cfg.ATLAS_DIR)


def index():
    idx = {}
    for fn in os.listdir(ATLAS):
        if not fn.endswith(".png.meta"):
            continue
        txt = open(os.path.join(ATLAS, fn), encoding="utf-8").read()
        for m in re.finditer(r"- serializedVersion: 2\n\s+name: (\S+)\n\s+rect:\n\s+serializedVersion: 2\n\s+x: ([\d.]+)\n\s+y: ([\d.]+)\n\s+width: ([\d.]+)\n\s+height: ([\d.]+)", txt):
            idx.setdefault(m.group(1), (fn[:-5], *[float(v) for v in m.groups()[1:]]))
    return idx


def main():
    out, names = sys.argv[1], sys.argv[2:]
    idx = index()
    cache = {}
    tiles = []
    for n in names:
        if n not in idx:
            print("missing", n)
            continue
        png, x, y, w, h = idx[n]
        if png not in cache:
            cache[png] = cv2.imread(os.path.join(ATLAS, png), cv2.IMREAD_UNCHANGED)
        img = cache[png]
        H = img.shape[0]
        crop = img[int(H - y - h):int(H - y), int(x):int(x + w)]
        if crop.shape[2] == 4:
            a = crop[:, :, 3:4] / 255.0
            bg = np.full(crop[:, :, :3].shape, 255, np.uint8)
            bg[::16, :] = 200
            crop = (crop[:, :, :3] * a + bg * (1 - a)).astype(np.uint8)
        scale = min(1.0, 260.0 / max(crop.shape[:2]))
        crop = cv2.resize(crop, (max(1, int(crop.shape[1] * scale)), max(1, int(crop.shape[0] * scale))))
        tile = np.full((300, 280, 3), 255, np.uint8)
        tile[:crop.shape[0], :crop.shape[1]] = crop
        cv2.putText(tile, n, (2, 292), cv2.FONT_HERSHEY_SIMPLEX, 0.4, (0, 0, 0), 1)
        tiles.append(tile)
    cols = 6
    while len(tiles) % cols:
        tiles.append(np.full((300, 280, 3), 255, np.uint8))
    rows = [np.hstack(tiles[i:i + cols]) for i in range(0, len(tiles), cols)]
    cv2.imwrite(out, np.vstack(rows))
    print("wrote", out)


if __name__ == "__main__":
    main()
