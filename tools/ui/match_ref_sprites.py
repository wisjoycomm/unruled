"""Rank atlas sprites against crops of LDPlayer reference screenshots.

Usage: python tools/ui/match_ref_sprites.py <ref.png> name:x,y,w,h [name:x,y,w,h ...]
Sprite rects come from .ldcap/sprites_all.txt (dumped from Unity: path|name|x|y|w|h|texH).
Each sprite is alpha-composited over the crop's own border colour, resized to the crop box
(aspect-preserved, centred) and scored with a grayscale+colour normalised error. Prints top 6.
"""
import sys
import os
import cv2
import numpy as np

sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))
import project_cfg  # noqa: E402

ROOT = str(project_cfg.ROOT)
UNITY = str(project_cfg.UNITY_ROOT)


def load_sprites():
    rows = []
    with open(os.path.join(ROOT, ".ldcap", "sprites_all.txt"), encoding="utf-8") as f:
        for line in f:
            p = line.rstrip("\n").split("|")
            if len(p) == 7:
                rows.append((p[0], p[1], *map(int, p[2:])))
    return rows


_cache = {}


def atlas(path):
    if path not in _cache:
        img = cv2.imread(os.path.join(UNITY, path), cv2.IMREAD_UNCHANGED)
        if img is not None and img.ndim == 3 and img.shape[2] == 3:
            img = cv2.cvtColor(img, cv2.COLOR_BGR2BGRA)
        _cache[path] = img
    return _cache[path]


def fit(sprite, w, h, bg):
    sh, sw = sprite.shape[:2]
    s = min(w / sw, h / sh)
    nw, nh = max(1, int(round(sw * s))), max(1, int(round(sh * s)))
    r = cv2.resize(sprite, (nw, nh), interpolation=cv2.INTER_AREA)
    canvas = np.zeros((h, w, 3), np.float32)
    canvas[:] = bg
    a = r[:, :, 3:4].astype(np.float32) / 255.0
    ox, oy = (w - nw) // 2, (h - nh) // 2
    canvas[oy:oy + nh, ox:ox + nw] = r[:, :, :3] * a + canvas[oy:oy + nh, ox:ox + nw] * (1 - a)
    return canvas


def main():
    ref = cv2.imread(sys.argv[1], cv2.IMREAD_COLOR).astype(np.float32)
    sprites = load_sprites()
    for spec in sys.argv[2:]:
        name, box = spec.split(":")
        x, y, w, h = map(int, box.split(","))
        crop = ref[y:y + h, x:x + w]
        border = np.concatenate([crop[0], crop[-1], crop[:, 0], crop[:, -1]])
        bg = np.median(border, axis=0)
        scores = []
        for path, sname, sx, sy, sw, sh, th in sprites:
            if sw < 8 or sh < 8 or sw > 700:
                continue
            ar = sw / sh
            if not (0.5 < ar / (w / h) < 2.0):
                continue
            img = atlas(path)
            if img is None:
                continue
            spr = img[th - sy - sh:th - sy, sx:sx + sw]
            if spr.size == 0:
                continue
            cand = fit(spr, w, h, bg)
            err = float(np.mean(np.abs(cand - crop)))
            scores.append((err, sname))
        scores.sort()
        print(name, " ".join("%s(%.1f)" % (n, e) for e, n in scores[:6]))


if __name__ == "__main__":
    main()
