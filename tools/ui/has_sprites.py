#!/usr/bin/env python3
"""Check that sprite names exist in the imported atlas .meta sprite sheets.

Usage: python tools/ui/has_sprites.py name1 name2 ...
       python tools/ui/has_sprites.py --file list.txt
Prints "OK name" / "MISSING name" and exits 1 if any are missing.
"""
import glob
import os
import re
import sys

sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))
import project_cfg  # noqa: E402

ROOT = str(project_cfg.ROOT)
ATLAS = str(project_cfg.ATLAS_DIR)


def sprite_names():
    names = set()
    for meta in glob.glob(os.path.join(ATLAS, "*.png.meta")):
        text = open(meta, encoding="utf-8", errors="ignore").read()
        names.update(re.findall(r"\n\s*name: (\S+)", text))
    return names


def main(argv):
    wanted = []
    if argv and argv[0] == "--file":
        wanted = [l.strip() for l in open(argv[1], encoding="utf-8") if l.strip()]
    else:
        wanted = argv
    have = sprite_names()
    missing = []
    for n in wanted:
        if n in have:
            print("OK      " + n)
        else:
            print("MISSING " + n)
            missing.append(n)
    print("-- %d/%d present (%d sprites indexed)" % (len(wanted) - len(missing), len(wanted), len(have)))
    return 1 if missing else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
