"""Dump an original (AssetRipper-exported) UI prefab: hierarchy path, active flag, rect, sprite name, text.

Usage: python tools/ui/dump_prefab_sprites.py <PrefabName> [<PrefabName> ...]
Resolves m_Sprite {fileID, guid} through Texture2D/*.meta internalIDToNameTable (sactx atlas pages).
GUID->meta index is cached in .ldcap/sprite_guid_index.json (built once; ~1 min).
"""
import json
import os
import re
import sys

sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))
import project_cfg  # noqa: E402
import unity_yaml  # noqa: E402

ROOT = str(project_cfg.ROOT)
ASSETS = str(project_cfg.EXPORTED_ASSETS_DIR)
INDEX = str(project_cfg.TEMP_DIR / "sprite_guid_index.json")


def build_index():
    return unity_yaml.cached_sprite_guid_index(ASSETS)


def parse(path):
    return unity_yaml.parse_yaml_docs(path)


def field(body, name):
    return unity_yaml.yaml_field(body, name)


def main():
    idx = build_index()
    for prefab in sys.argv[1:]:
        objs = parse(os.path.join(ASSETS, "GameObject", prefab + ".prefab"))
        go_name, go_active, go_comps, rect_of, parent = {}, {}, {}, {}, {}
        for fid, (cls, body) in objs.items():
            if cls == "1":
                go_name[fid] = field(body, "m_Name")
                go_active[fid] = field(body, "m_IsActive")
                go_comps[fid] = re.findall(r"component: \{fileID: (-?\d+)\}", body)
            elif cls in ("224", "4"):
                g = re.search(r"m_GameObject: \{fileID: (-?\d+)\}", body).group(1)
                rect_of[g] = fid
                parent[fid] = re.search(r"m_Father: \{fileID: (-?\d+)\}", body).group(1)
        rect_go = {v: k for k, v in rect_of.items()}

        def path_of(g):
            parts = []
            r = rect_of.get(g)
            while r and r != "0":
                parts.append(go_name.get(rect_go.get(r), "?"))
                r = parent.get(r)
            return "/".join(reversed(parts))

        print("=====", prefab)
        rows = []
        for g, comps in go_comps.items():
            r = rect_of.get(g)
            info = []
            if r:
                rb = objs[r][1]
                info.append("pos=%s size=%s amin=%s amax=%s pivot=%s" % (field(rb, "m_AnchoredPosition"), field(rb, "m_SizeDelta"), field(rb, "m_AnchorMin"), field(rb, "m_AnchorMax"), field(rb, "m_Pivot")))
                sc = field(rb, "m_LocalScale")
                if sc and sc != "{x: 1, y: 1, z: 1}":
                    info.append("scale=" + sc)
            for c in comps:
                if c not in objs:
                    continue
                body = objs[c][1]
                sp = re.search(r"m_Sprite: \{fileID: (-?\d+), guid: (\w+)", body)
                if sp:
                    ent = idx.get(sp.group(2))
                    nm = ent["names"].get(sp.group(1), "?" + sp.group(1)) if ent else "guid:" + sp.group(2)
                    col = field(body, "m_Color")
                    info.append("SPRITE=%s color=%s type=%s fill=%s/%s/%s" % (nm, col, field(body, "m_Type"), field(body, "m_FillMethod"), field(body, "m_FillOrigin"), field(body, "m_FillAmount")))
                tx = re.search(r"m_text: (.*)", body)
                if tx:
                    info.append("TEXT=%s fs=%s col=%s align=%s" % (tx.group(1)[:40], field(body, "m_fontSize"), field(body, "m_fontColor"), field(body, "m_HorizontalAlignment") or field(body, "m_textAlignment")))
                if re.search(r"m_Spacing: |m_CellSize: ", body):
                    info.append("LAYOUT cell=%s spacing=%s pad=%s cons=%s" % (field(body, "m_CellSize"), field(body, "m_Spacing"), re.sub(r"\s+", " ", (re.search(r"m_Padding:\s*\n((?:\s+m_\w+: -?\d+\n)+)", body) or [None, ""])[1]).strip(), field(body, "m_ConstraintCount")))
            rows.append((path_of(g), go_active.get(g), " | ".join(info)))
        for p, a, i in sorted(rows):
            print(("  " if a == "1" else "x ") + p + "  " + i)


if __name__ == "__main__":
    main()
