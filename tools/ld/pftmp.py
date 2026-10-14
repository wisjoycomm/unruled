"""List every TextMeshPro component in an exported prefab with its owning GameObject.

pfdetail.py can only print `cls114` for a TMP_Text because the type lives in a script
GUID, not the class id. This pass instead keys off the presence of `m_text:` in a
MonoBehaviour body and reports the font sizing / colour / alignment fields that decide
how the line actually renders.
"""
import re
import sys
import pathlib


def color(body, key):
    m = re.search(
        rf"{key}:\s*\n\s+r: ([\d.eE+-]+)\s*\n\s+g: ([\d.eE+-]+)\s*\n\s+b: ([\d.eE+-]+)\s*\n\s+a: ([\d.eE+-]+)",
        body,
    )
    if not m:
        m = re.search(
            rf"{key}: \{{r: ([\d.eE+-]+), g: ([\d.eE+-]+), b: ([\d.eE+-]+), a: ([\d.eE+-]+)\}}",
            body,
        )
    if not m:
        return None
    r, g, b, a = (float(x) for x in m.groups())
    return "#%02X%02X%02X/%.2f" % (
        int(r * 255 + 0.5),
        int(g * 255 + 0.5),
        int(b * 255 + 0.5),
        a,
    )


def main():
    path = pathlib.Path(sys.argv[1])
    text = path.read_text(encoding="utf-8", errors="ignore")
    bounds = [
        (m.start(), m.group(1), m.group(2))
        for m in re.finditer(r"^--- !u!(\d+) &(\d+)", text, re.M)
    ]
    docs = {}
    for i, (start, cls, fid) in enumerate(bounds):
        end = bounds[i + 1][0] if i + 1 < len(bounds) else len(text)
        docs[fid] = (cls, text[start:end])

    owner = {}
    for fid, (cls, body) in docs.items():
        if cls != "1":
            continue
        name = re.search(r"m_Name: (.+)", body)
        for comp in re.findall(r"component: \{fileID: (\d+)\}", body):
            owner[comp] = name.group(1).strip() if name else "?"

    for fid, (cls, body) in docs.items():
        if cls != "114" or "m_text:" not in body:
            continue

        def g(key, default=None):
            m = re.search(rf"^\s+{key}: (.+)$", body, re.M)
            return m.group(1).strip() if m else default

        print(
            f"[{owner.get(fid, '?')}] text={g('m_text')!r} fs={g('m_fontSize')} "
            f"base={g('m_fontSizeBase')} auto={g('m_enableAutoSizing')} "
            f"min={g('m_fontSizeMin')} max={g('m_fontSizeMax')} "
            f"style={g('m_fontStyle')} align={g('m_HorizontalAlignment')}/{g('m_VerticalAlignment')} "
            f"color={color(body, 'm_fontColor')} wrap={g('m_enableWordWrapping')} "
            f"overflow={g('m_overflowMode')} margin={g('m_margin')} "
            f"spacing={g('m_lineSpacing')}"
        )


if __name__ == "__main__":
    main()
