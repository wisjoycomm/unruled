"""Dump the RectTransform tree of an original exported Unity prefab.

The exported prefabs are plain YAML, so we can read the authentic anchors,
offsets, sizes and sprite bindings without opening Unity.

Usage:
  python tools/ld/pflayout.py <prefab path> [--depth N] [--filter substr]
"""
import os
import re
import sys

NUM = r"-?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?"


def parse(path):
    """Split the YAML stream into {fileID: (class, body)}."""
    docs = {}
    fid = None
    cls = None
    buf = []
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        for line in fh:
            m = re.match(r"^--- !u!(\d+) &(\d+)", line)
            if m:
                if fid is not None:
                    docs[fid] = (cls, "".join(buf))
                cls, fid, buf = m.group(1), m.group(2), []
                continue
            if fid is not None:
                buf.append(line)
    if fid is not None:
        docs[fid] = (cls, "".join(buf))
    return docs


def val(body, key, default=None):
    m = re.search(rf"^\s*{key}:\s*(.+)$", body, re.M)
    return m.group(1).strip() if m else default


def vec2(body, key):
    m = re.search(rf"^\s*{key}:\s*\{{x:\s*({NUM}),\s*y:\s*({NUM})\}}", body, re.M)
    return (float(m.group(1)), float(m.group(2))) if m else (0.0, 0.0)


def fid_of(body, key):
    m = re.search(rf"^\s*{key}:\s*\{{fileID:\s*(\d+)", body, re.M)
    return m.group(1) if m else None


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    path = args[0]
    depth_cap = 99
    filt = None
    for i, a in enumerate(sys.argv):
        if a == "--depth":
            depth_cap = int(sys.argv[i + 1])
        if a == "--filter":
            filt = sys.argv[i + 1]

    docs = parse(path)

    # GameObject (class 1) -> name + component list; RectTransform (224) -> geometry.
    names, comps = {}, {}
    for fid, (cls, body) in docs.items():
        if cls == "1":
            names[fid] = val(body, "m_Name", "?").strip()
            comps[fid] = re.findall(r"component:\s*\{fileID:\s*(\d+)\}", body)

    rects, children, parent_of = {}, {}, {}
    for fid, (cls, body) in docs.items():
        if cls not in ("224", "4"):
            continue
        go = fid_of(body, "m_GameObject")
        rects[fid] = (go, body)
        children[fid] = re.findall(r"- \{fileID:\s*(\d+)\}", val(body, "m_Children", "") or "")
        kids = re.search(r"m_Children:\s*\n((?:\s*- \{fileID: \d+\}\n)*)", body)
        children[fid] = re.findall(r"fileID:\s*(\d+)", kids.group(1)) if kids else []
        for k in children[fid]:
            parent_of[k] = fid

    roots = [f for f in rects if f not in parent_of]

    def describe(fid):
        go, body = rects[fid]
        name = names.get(go, "?")
        amin, amax = vec2(body, "m_AnchorMin"), vec2(body, "m_AnchorMax")
        pos, size = vec2(body, "m_AnchoredPosition"), vec2(body, "m_SizeDelta")
        piv = vec2(body, "m_Pivot")
        active = val(docs[go][1], "m_IsActive", "1") if go in docs else "1"
        tags = []
        for c in comps.get(go, []):
            if c not in docs:
                continue
            ccls, cbody = docs[c]
            if ccls == "114":
                tags.append("mono")
            elif ccls == "222":
                tags.append("canvasRenderer")
            elif ccls == "212":  # SpriteRenderer-ish / Image
                tags.append("img")
        line = (f"{name}{'' if active == '1' else ' [inactive]'}  "
                f"a{amin}{amax} p{pos} s{size} pv{piv}")
        return line

    def walk(fid, depth=0):
        if depth > depth_cap:
            return
        line = "  " * depth + describe(fid)
        if filt is None or filt.lower() in line.lower():
            print(line)
        for k in children.get(fid, []):
            if k in rects:
                walk(k, depth + 1)

    for r in roots:
        walk(r)


if __name__ == "__main__":
    main()
