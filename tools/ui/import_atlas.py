#!/usr/bin/env python3
"""Import an original sprite atlas (AssetRipper `sactx-*` page + its sliced .meta) into the Unity project.

The exported project stores each runtime SpriteAtlas page as `sactx-<n>-<w>x<h>-<fmt>-<atlas>-<hash>.png`
with a TextureImporter .meta that already carries every sub-sprite rect. We copy the page to
Art/UI/Atlas/<atlas>.png and rewrite the meta with a fresh deterministic guid so the project does not
collide with the exported project's guids.

Usage: python tools/ui/import_atlas.py ui_box [ui_other ...]
"""
import hashlib
import os
import re
import shutil
import sys

sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))
import project_cfg  # noqa: E402

ROOT = str(project_cfg.ROOT)
SRC = str(project_cfg.EXPORTED_ASSETS_DIR / "Texture2D")
DST = str(project_cfg.ATLAS_DIR)


def pages(atlas):
    """All source pages for an atlas name, ordered by the sactx page index."""
    out = []
    for fn in os.listdir(SRC):
        m = re.match(r"sactx-(\d+)-\d+x\d+-[^-]+(?:-[^-]+)*?-" + re.escape(atlas) + r"-\w+\.png$", fn)
        if m:
            out.append((int(m.group(1)), fn))
    return [fn for _, fn in sorted(out)]


def guid_for(name):
    return hashlib.md5(f"{project_cfg.PROJECT_SLUG}/atlas/{name}".encode()).hexdigest()


def main(names):
    if not names:
        raise SystemExit(__doc__)
    os.makedirs(DST, exist_ok=True)
    for atlas in names:
        found = pages(atlas)
        if not found:
            print(f"[import_atlas] {atlas}: no source page found in {SRC}")
            continue
        for i, src_png in enumerate(found):
            # Single-page atlases keep the plain name; multi-page get the _0/_1 suffix the project already uses.
            dest_name = atlas if len(found) == 1 else f"{atlas}_{i}"
            shutil.copyfile(os.path.join(SRC, src_png), os.path.join(DST, dest_name + ".png"))

            meta = open(os.path.join(SRC, src_png + ".meta"), encoding="utf-8", errors="ignore").read()
            meta = re.sub(r"^guid: \w+", "guid: " + guid_for(dest_name), meta, count=1, flags=re.M)
            with open(os.path.join(DST, dest_name + ".png.meta"), "w", encoding="utf-8", newline="\n") as fh:
                fh.write(meta)

            sprites = len(re.findall(r"- serializedVersion: 2\n\s+name: \S+\n\s+rect:", meta))
            print(f"[import_atlas] {src_png} -> {dest_name}.png ({sprites} sprites)")


if __name__ == "__main__":
    main(sys.argv[1:])
