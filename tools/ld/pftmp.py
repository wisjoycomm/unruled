"""List every TextMeshPro component in an exported prefab with its owning GameObject.

pfdetail.py can only print `cls114` for a TMP_Text because the type lives in a script
GUID, not the class id. This pass instead keys off the presence of `m_text:` in a
MonoBehaviour body and reports the font sizing / colour / alignment fields that decide
how the line actually renders.
"""
import os
import re
import sys
import pathlib

sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))
import unity_yaml  # noqa: E402


def color(body, key):
    return unity_yaml.yaml_color_hex(body, key)


def main():
    path = pathlib.Path(sys.argv[1])
    docs = unity_yaml.parse_yaml_docs(path, include_header=True)

    owner = {}
    for fid, (cls, body) in docs.items():
        if cls != "1":
            continue
        name = unity_yaml.yaml_field(body, "m_Name", "?")
        for comp in re.findall(r"component: \{fileID: (\d+)\}", body):
            owner[comp] = name

    for fid, (cls, body) in docs.items():
        if cls != "114" or "m_text:" not in body:
            continue

        def g(key, default=None):
            return unity_yaml.yaml_field(body, key, default)

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
