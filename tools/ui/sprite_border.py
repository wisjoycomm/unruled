#!/usr/bin/env python3
"""Report the 9-slice border of imported atlas sprites, and compare with the original export.

Usage: python tools/ui/sprite_border.py hero_bg_031 common_bg_021 ...
Output: "<name>  ours=(l,b,r,t)  orig=(l,b,r,t)  [DIFF]"
The original values come from ${EXPORTED_ASSETS_DIR}/Texture2D/*.meta.
"""
import glob
import os
import re
import sys

sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))
import project_cfg  # noqa: E402
import unity_yaml  # noqa: E402

ROOT = str(project_cfg.ROOT)
OURS = str(project_cfg.ATLAS_DIR)
ORIG = str(project_cfg.EXPORTED_ASSETS_DIR / "Texture2D")


def table(folder):
    return {
        name: (int(bx), int(by), int(bz), int(bw))
        for name, (_png, _x, _y, _w, _h, bx, by, bz, bw) in unity_yaml.index_atlas_sprites([folder]).items()
    }


def main(names):
    ours = table(OURS)
    orig = table(ORIG)
    bad = 0
    for n in names:
        a = ours.get(n)
        b = orig.get(n)
        flag = "" if a == b else "  <-- DIFF"
        if a != b:
            bad += 1
        print("%-34s ours=%-18s orig=%-18s%s" % (n, a, b, flag))
    print("-- %d/%d match" % (len(names) - bad, len(names)))
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
