#!/usr/bin/env python3
"""Dump an exported (AssetRipper) UI prefab as an indented hierarchy with RectTransform + Image/Text data.

Usage: python tools/ui/dump_prefab.py HeroCardCell [HeroMainFramPanel ...]
Sprite references are resolved through the exported project's .meta guids (Sprite/ and Texture2D/ folders).
"""
import glob
import os
import re
import sys

sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))
import project_cfg  # noqa: E402
import unity_yaml  # noqa: E402

ROOT = str(project_cfg.ROOT)
ASSETS = str(project_cfg.EXPORTED_ASSETS_DIR)
_guid_cache = None


def guid_names():
    global _guid_cache
    cache_file = str(project_cfg.TEMP_DIR / "guid_cache.json")
    if _guid_cache is None and os.path.exists(cache_file):
        import json
        _guid_cache = json.load(open(cache_file, encoding="utf-8"))
    if _guid_cache is None:
        import json
        _guid_cache = {}
        for folder in ("Texture2D", "MonoScript"):
            for meta in glob.glob(os.path.join(ASSETS, folder, "*.meta")):
                try:
                    with open(meta, encoding="utf-8", errors="ignore") as f:
                        m = unity_yaml.GUID_RE.search(f.read(400))
                except OSError:
                    continue
                if m:
                    _guid_cache[m.group(1)] = os.path.basename(meta)[:-5]
        os.makedirs(os.path.dirname(cache_file), exist_ok=True)
        json.dump(_guid_cache, open(cache_file, "w", encoding="utf-8"))
    return _guid_cache


_atlas_tables = {}


def sprite_name(guid, file_id, gn):
    """Resolve an atlas sub-sprite (texture guid + internal fileID) to its sprite name via the texture .meta."""
    asset = gn.get(guid)
    if asset is None:
        return guid
    if guid not in _atlas_tables:
        meta = os.path.join(ASSETS, "Texture2D", asset + ".meta")
        _, table, _ = unity_yaml.parse_atlas_meta(meta) if os.path.exists(meta) else (None, {}, {})
        _atlas_tables[guid] = table
    return _atlas_tables[guid].get(file_id, asset + "#" + file_id)


def parse(path):
    return unity_yaml.parse_yaml_docs(path, int_class=True, include_header=True)


def vec(chunk, key):
    return unity_yaml.yaml_vec2(chunk, key)


def fmt(v):
    return "({:g},{:g})".format(*v) if v else "-"


def main(names):
    gn = guid_names()
    for name in names:
        path = os.path.join(ASSETS, "GameObject", name + ".prefab")
        docs = parse(path)
        gos, comps, children, parent_of = {}, {}, {}, {}
        for fid, (cls, chunk) in docs.items():
            if cls == 1:
                gos[fid] = re.search(r"m_Name: (.*)", chunk).group(1).strip()
                m = re.search(r"m_IsActive: (\d)", chunk)
                gos[fid] += "" if not m or m.group(1) == "1" else " [inactive]"
            else:
                m = re.search(r"m_GameObject: \{fileID: (-?\d+)\}", chunk)
                if m:
                    comps.setdefault(m.group(1), []).append((cls, chunk))
            if cls in (4, 224):
                kids = re.findall(r"- \{fileID: (-?\d+)\}", chunk.split("m_Children:")[1].split("m_Father")[0]) if "m_Children:" in chunk else []
                children[fid] = kids
                for k in kids:
                    parent_of[k] = fid
        tr_of_go = {}
        for go, cl in comps.items():
            for cls, chunk in cl:
                if cls in (4, 224):
                    tr_of_go[go] = re.search(r"&?(-?\d+)", chunk).group(1)
        go_of_tr = {}
        for fid, (cls, chunk) in docs.items():
            if cls in (4, 224):
                m = re.search(r"m_GameObject: \{fileID: (-?\d+)\}", chunk)
                go_of_tr[fid] = m.group(1)
        roots = [t for t in go_of_tr if t not in parent_of]
        print("=" * 20, name)

        def describe(go):
            out = []
            for cls, chunk in comps.get(go, []):
                if cls == 224:
                    out.append("a" + fmt(vec(chunk, "m_AnchorMin")) + fmt(vec(chunk, "m_AnchorMax"))
                               + " p" + fmt(vec(chunk, "m_AnchoredPosition")) + " s" + fmt(vec(chunk, "m_SizeDelta"))
                               + " pv" + fmt(vec(chunk, "m_Pivot")))
                    sc = re.search(r"m_LocalScale: \{x: ([-\d.e]+), y: ([-\d.e]+)", chunk)
                    if sc and (sc.group(1) != "1" or sc.group(2) != "1"):
                        out.append("scale(" + sc.group(1) + "," + sc.group(2) + ")")
                    rot = re.search(r"m_LocalRotation: \{x: ([-\d.e]+), y: ([-\d.e]+), z: ([-\d.e]+)", chunk)
                    if rot and rot.group(3) not in ("0", "-0"):
                        out.append("rotZq=" + rot.group(3))
                elif cls == 114:
                    script = re.search(r"m_Script: \{fileID: -?\d+, guid: (\w+)", chunk)
                    sname = gn.get(script.group(1), "?") if script else "?"
                    sname = sname.replace(".cs", "")
                    bits = [sname]
                    sp = re.search(r"m_Sprite: \{fileID: (-?\d+), guid: (\w+)", chunk)
                    if sp:
                        bits.append("sprite=" + sprite_name(sp.group(2), sp.group(1), gn))
                    col = re.search(r"m_Color: \{r: ([\d.]+), g: ([\d.]+), b: ([\d.]+), a: ([\d.]+)", chunk)
                    if col and (sp or "m_text" in chunk or "m_Text" in chunk):
                        c = [float(col.group(i)) for i in range(1, 5)]
                        if c != [1, 1, 1, 1]:
                            bits.append("col=#%02X%02X%02X/%.2f" % (int(c[0] * 255), int(c[1] * 255), int(c[2] * 255), c[3]))
                    ty = re.search(r"m_Type: (\d)", chunk)
                    if sp and ty and ty.group(1) != "0":
                        bits.append("type=" + ty.group(1))
                    tx = re.search(r"m_[tT]ext: (.*)", chunk)
                    if tx and tx.group(1).strip() not in ("", "''", '""'):
                        bits.append("text=" + tx.group(1).strip()[:40])
                    fs = re.search(r"m_fontSize: ([\d.]+)", chunk) or re.search(r"m_FontSize: ([\d.]+)", chunk)
                    if fs and tx:
                        bits.append("fs=" + fs.group(1))
                    for k in ("m_Spacing", "m_CellSize", "m_StartAxis", "m_ConstraintCount", "m_ChildAlignment"):
                        mm = re.search(k + r": (.*)", chunk)
                        if mm and ("Layout" in sname or "Grid" in sname):
                            bits.append(k[2:] + "=" + mm.group(1).strip())
                    out.append("[" + " ".join(bits) + "]")
            return " ".join(out)

        def walk(tr, depth):
            go = go_of_tr.get(tr)
            print("  " * depth + gos.get(go, "?") + "  " + describe(go))
            for k in children.get(tr, []):
                walk(k, depth + 1)

        for r in roots:
            walk(r, 0)


if __name__ == "__main__":
    main(sys.argv[1:])
