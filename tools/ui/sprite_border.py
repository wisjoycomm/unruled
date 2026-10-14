#!/usr/bin/env python3
"""Report the 9-slice border of imported atlas sprites, and compare with the original export.

Usage: python tools/ui/sprite_border.py hero_bg_031 common_bg_021 ...
Output: "<name>  ours=(l,b,r,t)  orig=(l,b,r,t)  [DIFF]"
The original values come from AllAssetsExport/ExportedProject/Assets/Texture2D/*.meta.
"""
import glob
import os
import re
import sys

sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))
import project_cfg  # noqa: E402

ROOT = str(project_cfg.ROOT)
OURS = str(project_cfg.ATLAS_DIR)
ORIG = str(project_cfg.EXPORTED_ASSETS_DIR / "Texture2D")

NAME = re.compile(r"^\s*name: (\S+)\s*$")
BORDER = re.compile(r"^\s*border: \{x: ([-\d.]+), y: ([-\d.]+), z: ([-\d.]+), w: ([-\d.]+)\}")


def table(folder):
    """Walk the sprite sheet line by line: each entry is "name:" followed later by "border:"."""
    out = {}
    for meta in glob.glob(os.path.join(folder, "*.meta")):
        pending = None
        with open(meta, encoding="utf-8", errors="ignore") as fh:
            for line in fh:
                m = NAME.match(line)
                if m:
                    pending = m.group(1)
                    continue
                m = BORDER.match(line)
                if m and pending:
                    out.setdefault(pending, tuple(int(float(m.group(i))) for i in (1, 2, 3, 4)))
                    pending = None
    return out


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
