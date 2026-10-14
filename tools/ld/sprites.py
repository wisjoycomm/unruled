"""Check that every sprite name the Hero prefab builder asks for exists in the atlases.

Usage: python tools/ld/sprites.py [name ...]
With no arguments it checks the HeroStarPopup set.
"""
import glob
import os
import re
import sys

ATLAS = os.path.abspath(os.path.join(
    os.path.dirname(__file__), "..", "..",
    "UnityAssets", "Assets", "_EndlessWinter", "Art", "UI", "Atlas"))

DEFAULT = [
    "common_bg_001", "common_bg_002", "common_bg_021", "common_bg_026_04",
    "common_bg_028", "common_btn_com_004", "common_btn_004", "common_btn_004_003",
    "common_progress_008", "common_progress_008_001", "common_icon_017",
    "common_icon_017_001", "hero_bg_024", "common_btn_blood_002", "item_icon_500007",
]


def index():
    names = {}
    for p in glob.glob(os.path.join(ATLAS, "*.png.meta")):
        text = open(p, encoding="utf-8").read()
        for n in re.findall(r"name: (\S+)", text):
            names.setdefault(n, os.path.basename(p).replace(".png.meta", ""))
    return names


def main():
    names = index()
    print(f"{len(names)} sprites across {len(set(names.values()))} atlases\n")
    wanted = sys.argv[1:] or DEFAULT
    missing = []
    for w in wanted:
        if w in names:
            print(f"  OK      {w:28} ({names[w]})")
        else:
            missing.append(w)
            near = sorted(n for n in names if n.startswith(w[:14]))[:6]
            print(f"  MISSING {w:28} near: {near}")
    print(f"\n{len(missing)} missing")


if __name__ == "__main__":
    main()
