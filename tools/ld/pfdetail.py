"""Resolve the concrete sprite name, colour and TMP font settings of an exported prefab.

pflayout.py only reports the *texture* a sprite GUID points at, which is useless for an
atlas page holding 70 sprites. This walks the same prefab but resolves each
`m_Sprite: {fileID: N, guid: G}` pair through the owning .meta's nameFileIdTable, so the
output names the actual sprite (e.g. common_bg_tip_001) rather than the atlas page.
"""
import os
import re
import sys
import pathlib

sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))
import unity_yaml  # noqa: E402

SPRITE_REF = re.compile(r"m_Sprite: \{fileID: (-?\d+), guid: (\S+?),")
FONT_REF = re.compile(r"m_FontAsset: \{fileID: (-?\d+), guid: (\S+?),")


def parse(path):
    text = pathlib.Path(path).read_text(encoding="utf-8", errors="ignore")
    return unity_yaml.parse_yaml_docs(text, include_header=True), text


def build_sprite_table(root, guids):
    """guid -> {fileID: spriteName} taken from each referenced asset's .meta."""
    table = {}
    remaining = set(guids)
    assets = pathlib.Path(root) / "Assets"
    for folder in ("Texture2D", "SpriteAtlas", "Font", "Material"):
        directory = assets / folder
        if not directory.is_dir() or not remaining:
            continue
        for meta in directory.rglob("*.meta"):
            guid, names, _ = unity_yaml.parse_atlas_meta(meta)
            if not guid or guid not in remaining:
                continue
            table[guid] = (meta.stem, names)
            remaining.discard(guid)
            if not remaining:
                break
    for guid in remaining:
        table[guid] = ("?" + guid[:8], {})
    return table


def field(body, name, default=None):
    return unity_yaml.yaml_field(body, name, default)


def vec(body, name):
    v = unity_yaml.yaml_vec2(body, name)
    return (round(v[0], 2), round(v[1], 2)) if v else None


def color_at(body, key):
    return unity_yaml.yaml_color_hex(body, key)


def describe(cls, body, table):
    parts = []
    sprite = SPRITE_REF.search(body)
    if sprite:
        fid, guid = sprite.group(1), sprite.group(2)
        page, names = table.get(guid, ("?", {}))
        parts.append("sprite=" + names.get(fid, page + ":" + fid))
    col = color_at(body, "m_Color")
    if col:
        parts.append("col=" + col)
    face = color_at(body, "m_faceColor")
    if face:
        parts.append("face=" + face)
    for key, label in (
        ("m_Type", "imgType"),
        ("m_PreserveAspect", "aspect"),
        ("m_text", "text"),
        ("m_fontSize", "fs"),
        ("m_fontStyle", "style"),
        ("m_TextAlignment", "align"),
        ("m_HorizontalAlignment", "hAlign"),
        ("m_VerticalAlignment", "vAlign"),
        ("m_enableWordWrapping", "wrap"),
        ("m_margin", "margin"),
        ("m_Spacing", "spacing"),
        ("m_Padding", "pad"),
        ("m_ChildAlignment", "childAlign"),
        ("m_ChildControlWidth", "ctrlW"),
        ("m_ChildControlHeight", "ctrlH"),
        ("m_ChildForceExpandWidth", "expW"),
        ("m_ChildForceExpandHeight", "expH"),
        ("m_MinHeight", "minH"),
        ("m_PreferredHeight", "prefH"),
        ("m_MinWidth", "minW"),
        ("m_PreferredWidth", "prefW"),
        ("m_HorizontalFit", "hFit"),
        ("m_VerticalFit", "vFit"),
        ("m_Horizontal", "horiz"),
        ("m_Vertical", "vert"),
        ("m_MovementType", "move"),
        ("m_Alpha", "alpha"),
    ):
        value = field(body, key)
        if value not in (None, ""):
            parts.append(f"{label}={value}")
    return " ".join(parts) or f"cls{cls}"


def children(body):
    m = re.search(r"m_Children:\n((?:\s+- \{fileID: \d+\}\n)*)", body)
    return re.findall(r"- \{fileID: (\d+)\}", m.group(1)) if m else []


def walk(docs, rect_id, table, depth, out):
    cls, body = docs.get(rect_id, (None, None))
    if body is None:
        return out
    go_id = re.search(r"m_GameObject: \{fileID: (\d+)\}", body)
    name, active, comps = "?", "1", []
    if go_id:
        _, go_body = docs.get(go_id.group(1), (None, ""))
        name = field(go_body, "m_Name", "?")
        active = field(go_body, "m_IsActive", "1")
        comps = re.findall(r"component: \{fileID: (\d+)\}", go_body)

    pos, size = vec(body, "m_AnchoredPosition"), vec(body, "m_SizeDelta")
    line = f"{'  ' * depth}{name}{'' if active == '1' else ' [OFF]'}"
    line += f" a{vec(body,'m_AnchorMin')}{vec(body,'m_AnchorMax')} p{pos} s{size} pv{vec(body,'m_Pivot')}"
    out.append(line)
    for comp in comps:
        c_cls, c_body = docs.get(comp, (None, None))
        if c_body is None or c_cls in ("4", "224", "1", "222"):
            continue
        out.append(f"{'  ' * depth}    . {describe(c_cls, c_body, table)}")
    for child in children(body):
        walk(docs, child, table, depth + 1, out)
    return out


def main():
    path = pathlib.Path(sys.argv[1])
    root = path.parents[2]
    docs, text = parse(path)
    guids = {g for _, g in SPRITE_REF.findall(text)} | {
        g for _, g in FONT_REF.findall(text)
    }
    table = build_sprite_table(root, guids)
    roots = [
        fid
        for fid, (cls, body) in docs.items()
        if cls == "224" and field(body, "m_Father") == "{fileID: 0}"
    ]
    for fid in roots:
        print("\n".join(walk(docs, fid, table, 0, [])))


if __name__ == "__main__":
    main()
