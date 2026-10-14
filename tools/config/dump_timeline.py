"""Summarize an exported Unity TimelineAsset (.playable): tracks, clips, timing, referenced assets."""
import os, re, sys

sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))
import project_cfg  # noqa: E402
from unity_yaml import field, load_guid_map, parse_documents  # noqa: E402

EXPORT = str(project_cfg.EXPORTED_ASSETS_DIR)
_guid_map = None

def guid_name(guid):
    global _guid_map
    if _guid_map is None:
        _guid_map = load_guid_map(EXPORT)
    rel = _guid_map.get(guid)
    return rel[:-5] if rel and rel.endswith(".meta") else (rel or guid)

def docs(text):
    return {fid: body for fid, (_, body) in parse_documents(text).items()}

def main(path):
    text = open(path, encoding="utf-8").read()
    d = docs(text)
    root = d["11400000"]
    tracks = re.findall(r"- \{fileID: (-?\d+)\}", root.split("m_Tracks:")[1].split("m_FixedDuration")[0])
    script_names = {}
    def show(tid, depth):
        b = d.get(tid)
        if b is None:
            return
        script = re.search(r"m_Script: \{fileID: \d+, guid: (\w+)", b)
        sname = guid_name(script.group(1)) if script else "?"
        print("  " * depth + f"[{tid}] {field(b, 'm_Name')}  ({os.path.basename(sname)})  muted={field(b,'m_Muted')}")
        inf = re.search(r"m_InfiniteClip: \{fileID: \d+, guid: (\w+)", b)
        if inf:
            print("  " * depth + "   infinite clip: " + os.path.basename(guid_name(inf.group(1))))
        clips = b.split("m_Clips:")[1].split("m_Markers:")[0] if "m_Clips:" in b else ""
        for c in re.split(r"\n  - m_Version", clips)[1:]:
            start = field(c, "m_Start"); dur = field(c, "m_Duration"); name = field(c, "m_DisplayName")
            asset = re.search(r"m_Asset: \{fileID: (-?\d+)", c)
            extra = ""
            if asset and asset.group(1) in d:
                ab = d[asset.group(1)]
                cl = re.search(r"m_Clip: \{fileID: \d+, guid: (\w+)", ab)
                if cl:
                    extra = " clip=" + os.path.basename(guid_name(cl.group(1)))
                pf = re.search(r"prefabGameObject|m_Prefab", ab)
                # dump scalar fields of custom playable assets
                if not cl:
                    vals = [l.strip() for l in ab.splitlines() if re.match(r"\s{2}\w+: [^{]", l) and not l.strip().startswith(("m_", "serializedVersion"))]
                    extra = " " + "; ".join(vals[:8])
            print("  " * depth + f"   clip '{name}' start={float(start):.2f} dur={float(dur):.2f}{extra}")
        markers = b.split("m_Markers:")[1] if "m_Markers:" in b else ""
        for mid in re.findall(r"\{fileID: (-?\d+)\}", markers.split("m_Infinite")[0]):
            mb = d.get(mid, "")
            vals = [l.strip() for l in mb.splitlines() if re.match(r"\s{2}\w+: ", l) and not l.strip().startswith(("m_Object", "m_Corr", "m_Prefab", "m_GameObject", "m_Enabled", "m_Editor", "m_Script"))]
            print("  " * depth + "   marker: " + "; ".join(vals[:8]))
        for child in re.findall(r"\{fileID: (-?\d+)\}", (b.split("m_Children:")[1].split("m_Clips:")[0] if "m_Children:" in b else "")):
            show(child, depth + 1)
    for t in tracks:
        show(t, 0)

if __name__ == "__main__":
    main(sys.argv[1])
