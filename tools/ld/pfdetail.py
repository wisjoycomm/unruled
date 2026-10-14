"""Resolve the concrete sprite name, colour and TMP font settings of an exported prefab.

pflayout.py only reports the *texture* a sprite GUID points at, which is useless for an
atlas page holding 70 sprites. This walks the same prefab but resolves each
`m_Sprite: {fileID: N, guid: G}` pair through the owning .meta's nameFileIdTable, so the
output names the actual sprite (e.g. common_bg_tip_001) rather than the atlas page.
"""
import re
import sys
import pathlib

DOC = re.compile(r"^--- !u!(\d+) &(\d+)", re.M)
SPRITE_REF = re.compile(r"m_Sprite: \{fileID: (-?\d+), guid: (\S+?),")
FONT_REF = re.compile(r"m_FontAsset: \{fileID: (-?\d+), guid: (\S+?),")


def parse(path):
    text = pathlib.Path(path).read_text(encoding="utf-8", errors="ignore")
    bounds = [(m.start(), m.group(1), m.group(2)) for m in DOC.finditer(text)]
    docs = {}
    for i, (start, cls, fid) in enumerate(bounds):
        end = bounds[i + 1][0] if i + 1 < len(bounds) else len(text)
        docs[fid] = (cls, text[start:end])
    return docs, text


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
            body = meta.read_text(encoding="utf-8", errors="ignore")
            m = re.search(r"guid: (\S+)", body)
            if not m or m.group(1) not in remaining:
                continue
            guid = m.group(1)
            names = {}
            for first, second in re.findall(
                r"- first:\s*\n\s+\d+: (\d+)\s*\n\s+second: (\S+)", body
            ):
                names[first] = second
            table[guid] = (meta.stem, names)
            remaining.discard(guid)
            if not remaining:
                break
    for guid in remaining:
        table[guid] = ("?" + guid[:8], {})
    return table


def field(body, name, default=None):
    m = re.search(rf"^\s+{re.escape(name)}: (.+)$", body, re.M)
    return m.group(1).strip() if m else default


def vec(body, name):
    raw = field(body, name)
    if not raw:
        return None
    nums = re.findall(r"-?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?", raw)
    return tuple(round(float(n), 2) for n in nums[:2]) if len(nums) >= 2 else None


def color_at(body, key):
    m = re.search(
        rf"{key}:\s*\n\s+r: ([\d.eE+-]+)\s*\n\s+g: ([\d.eE+-]+)\s*\n\s+b: ([\d.eE+-]+)\s*\n\s+a: ([\d.eE+-]+)",
        body,
    )
    if not m:
        return None
    r, g, b, a = (float(x) for x in m.groups())
    return f"#{int(r*255+0.5):02X}{int(g*255+0.5):02X}{int(b*255+0.5):02X}/{a:.2f}"


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
