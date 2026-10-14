"""Masked multi-scale template search: which atlas sprite best explains a region of a reference shot.

Usage: python tools/ui/locate_sprites.py <ref.png> x,y,w,h <name-regex> [minScale maxScale]
Prints the top matches as: score sprite scale @(x,y) size. Score = TM_CCORR_NORMED-like on alpha mask
(we use TM_SQDIFF with mask, reported as mean abs error per pixel; lower is better).
"""
import os
import re
import sys
import cv2
import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
UNITY = os.path.join(ROOT, "UnityAssets")
_cache = {}
MIN = int(os.environ.get("MINPX", "20"))


def atlas(path):
    if path not in _cache:
        img = cv2.imread(os.path.join(UNITY, path), cv2.IMREAD_UNCHANGED)
        if img is not None and img.ndim == 3 and img.shape[2] == 3:
            img = cv2.cvtColor(img, cv2.COLOR_BGR2BGRA)
        _cache[path] = img
    return _cache[path]


def main():
    ref = cv2.imread(sys.argv[1], cv2.IMREAD_COLOR)
    x, y, w, h = map(int, sys.argv[2].split(","))
    region = ref[y:y + h, x:x + w].astype(np.float32)
    pat = re.compile(sys.argv[3])
    lo = float(sys.argv[4]) if len(sys.argv) > 4 else 0.2
    hi = float(sys.argv[5]) if len(sys.argv) > 5 else 1.2
    results = []
    with open(os.path.join(ROOT, ".ldcap", "sprites_all.txt"), encoding="utf-8") as f:
        rows = [l.rstrip("\n").split("|") for l in f if l.strip()]
    for p, name, sx, sy, sw, sh, th in rows:
        if not pat.search(name):
            continue
        sx, sy, sw, sh, th = map(int, (sx, sy, sw, sh, th))
        img = atlas(p)
        if img is None:
            continue
        spr = img[th - sy - sh:th - sy, sx:sx + sw]
        best = None
        for s in np.linspace(lo, hi, 21):
            tw, tht = int(round(sw * s)), int(round(sh * s))
            if tw < MIN or tht < MIN or tw > w or tht > h:
                continue
            t = cv2.resize(spr, (tw, tht), interpolation=cv2.INTER_AREA)
            a = t[:, :, 3].astype(np.float32) / 255.0
            if a.sum() < 10:
                continue
            m = np.dstack([a, a, a])
            res = cv2.matchTemplate(region, t[:, :, :3].astype(np.float32), cv2.TM_SQDIFF, mask=m)
            mn, _, loc, _ = cv2.minMaxLoc(res)
            err = np.sqrt(mn / (a.sum() * 3))
            if best is None or err < best[0]:
                best = (err, s, loc, (tw, tht))
        if best:
            results.append((best[0], name, best[1], (best[2][0] + x, best[2][1] + y), best[3]))
    results.sort()
    for r in results[:8]:
        print("%.1f %s scale=%.2f @%s size=%s" % r)


if __name__ == "__main__":
    main()
