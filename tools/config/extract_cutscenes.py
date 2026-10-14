"""Extract the original FTUE camp cutscenes (TimelineAssets guide_anim_*) into plain JSON.

For every timeline: the bound GameObject path of each track (from the prefab PlayableDirector),
the recorded transform clip (AssetRipper keeps its GUID), animator-state clips (start, duration,
state) and the notify events (bubble / dialog / pause / wait). Output:
Data/Cutscenes/cutscenes_ftue.json
"""
import json
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))
import dump_guides  # noqa: E402  (English language table for bubble lines)
import project_cfg  # noqa: E402

ROOT = str(project_cfg.ROOT)
EXPORT = str(project_cfg.EXPORTED_ASSETS_DIR)
OUT = str(project_cfg.DATA_DIR / "Cutscenes" / "cutscenes_ftue.json")
TIMELINES = ["guide_anim_01_1", "guide_anim_01_2", "guide_anim_01_3", "guide_anim_05", "guide_anim_03", "guide_anim_07", "guide_anim_04"]

_meta = {}


def meta_path(guid):
    if not _meta:
        for folder in ("MonoBehaviour", "AnimationClip"):
            for dirpath, _, files in os.walk(os.path.join(EXPORT, folder)):
                for f in files:
                    if not f.endswith(".meta"):
                        continue
                    p = os.path.join(dirpath, f)
                    m = re.search(r"^guid: (\w+)", open(p, encoding="utf-8").read(), flags=re.M)
                    if m:
                        _meta.setdefault(m.group(1), p[:-5])
    return _meta.get(guid)


def docs(text):
    out = {}
    for block in re.split(r"^--- ", text, flags=re.M)[1:]:
        head, _, body = block.partition("\n")
        m = re.search(r"&(-?\d+)", head)
        if m:
            out[m.group(1)] = body
    return out


def field(body, name):
    m = re.search(r"^\s*" + re.escape(name) + r": ?(.*)$", body, flags=re.M)
    value = m.group(1).strip() if m else ""
    # YAML quotes names with edge spaces ('city_anim_building_explorer_cabin_mammoth '); the name is the inner text.
    if len(value) > 1 and value[0] == value[-1] == "'":
        value = value[1:-1].replace("''", "'").strip()
    return value


def go_path(prefab_docs, go_id):
    """Hierarchy path of a GameObject inside the prefab (prefab root excluded)."""
    names = []
    while go_id:
        go = prefab_docs.get(go_id, "")
        names.append(field(go, "m_Name"))
        tr = None
        for comp in re.findall(r"component: \{fileID: (-?\d+)\}", go):
            if "m_Father:" in prefab_docs.get(comp, ""):
                tr = prefab_docs[comp]
                break
        father = re.search(r"m_Father: \{fileID: (-?\d+)\}", tr or "")
        if not father or father.group(1) == "0":
            break
        go_id = re.search(r"m_GameObject: \{fileID: (-?\d+)\}", prefab_docs[father.group(1)]).group(1)
    return "/".join(reversed(names[:-1]))


def clips_of(track_body, tdocs, fallback=None):
    """Clips of a track; clip assets live in the track file or, for nested tracks, in the timeline."""
    part = track_body.split("m_Clips:")[1].split("m_Markers:")[0] if "m_Clips:" in track_body else ""
    for item in re.split(r"\n  - ", part)[1:]:
        asset = re.search(r"m_Asset: \{fileID: (-?\d+)(?:, guid: (\w+))?", item)
        body = ""
        if asset and asset.group(2):
            # Clip asset stored in its own file (guide_anim_03 notify clips).
            p = meta_path(asset.group(2))
            body = docs(open(p, encoding="utf-8").read()).get(asset.group(1), "") if p else ""
        elif asset:
            body = tdocs.get(asset.group(1)) or (fallback or {}).get(asset.group(1), "")
        yield (float(field(item, "m_Start") or 0), float(field(item, "m_Duration") or 0),
               field(item, "m_DisplayName"), body)


def anim_name(guid):
    p = meta_path(guid)
    return os.path.splitext(os.path.basename(p))[0] if p else guid


def extract(name):
    tl_path = os.path.join(EXPORT, "MonoBehaviour", name + ".playable")
    tl_guid = re.search(r"guid: (\w+)", open(tl_path + ".meta", encoding="utf-8").read()).group(1)
    tl_docs = docs(open(tl_path, encoding="utf-8").read())
    prefab = docs(open(os.path.join(EXPORT, "GameObject", name + ".prefab"), encoding="utf-8").read())
    director = next(b for b in prefab.values() if "m_PlayableAsset:" in b and "m_SceneBindings:" in b)
    bindings = {}
    for key_id, key_guid, value in re.findall(
            r"key: \{fileID: (-?\d+), guid: (\w+), type: 2\}\n\s+value: \{fileID: (-?\d+)\}", director):
        comp = prefab.get(value, "")
        go = re.search(r"m_GameObject: \{fileID: (-?\d+)", comp)
        # Animation tracks bind a component (Animator); activation tracks bind the GameObject itself.
        go_id = go.group(1) if go else (value if "m_Component:" in comp else None)
        bindings[(key_id, key_guid)] = go_path(prefab, go_id) if go_id else ""

    tracks, events = [], []

    def visit(track_id, body, owner_guid, owner_docs):
        target = bindings.get((track_id, owner_guid), bindings.get(("11400000", owner_guid)))
        infinite = re.search(r"m_InfiniteClip: \{fileID: -?\d+, guid: (\w+)", body)
        entry = {"target": target, "name": field(body, "m_Name")}
        if infinite:
            entry["transformClip"] = anim_name(infinite.group(1))
            entry["transformClipGuid"] = infinite.group(1)
            vec = r"\{x: ([-\d.eE]+), y: ([-\d.eE]+), z: ([-\d.eE]+)\}"
            pos = re.search(r"m_InfiniteClipOffsetPosition: " + vec, body)
            eul = re.search(r"m_InfiniteClipOffsetEulerAngles: " + vec, body)
            entry["offsetPosition"] = [round(float(v), 4) for v in pos.groups()] if pos else [0, 0, 0]
            entry["offsetEuler"] = [round(float(v), 4) for v in eul.groups()] if eul else [0, 0, 0]
        states = []
        for start, dur, label, asset in clips_of(body, owner_docs, tl_docs):
            tpl = dict(re.findall(r"\n\s+(type|param\d|otherFlag): ?(.*)", asset))
            clip_guid = re.search(r"m_Clip: \{fileID: -?\d+, guid: (\w+)", asset)
            if "param1" in tpl:
                ev = {"time": round(start, 3), "duration": round(dur, 3), "label": label,
                      "kind": int(tpl.get("param1") or 0), "p2": tpl.get("param2", "").strip(),
                      "p3": tpl.get("param3", "").strip(), "p4": tpl.get("param4", "").strip()}
                if ev["p4"].startswith("guide_plot_line_"):
                    ev["text"] = dump_guides.tr(ev["p4"])
                events.append(ev)
            elif clip_guid:
                entry.setdefault("clips", []).append({"start": round(start, 3), "duration": round(dur, 3),
                                                      "clip": anim_name(clip_guid.group(1)),
                                                      "clipGuid": clip_guid.group(1)})
            elif entry["name"].startswith("Activation"):
                # The bound object is shown only inside these spans (guide_anim_04: the bear from 4.27 s).
                entry.setdefault("active", []).append({"start": round(start, 3), "duration": round(dur, 3)})
            elif "Control" in entry["name"] or "VFX" in label or "fx_" in label:
                entry.setdefault("controls", []).append({"start": round(start, 3), "duration": round(dur, 3), "label": label})
            elif label:
                states.append({"start": round(start, 3), "duration": round(dur, 3), "state": label})
        if states:
            entry["states"] = sorted(states, key=lambda s: s["start"])
        if any(k in entry for k in ("transformClip", "states", "clips", "controls", "active")):
            tracks.append(entry)
        children = body.split("m_Children:")[1].split("m_Clips:")[0] if "m_Children:" in body else ""
        for _, child_guid in re.findall(r"\{fileID: (-?\d+), guid: (\w+), type: 2\}", children):
            p = meta_path(child_guid)
            if p is None:
                continue
            cdocs = docs(open(p, encoding="utf-8").read())
            cid = next(k for k, v in cdocs.items() if "m_Parent:" in v)
            visit(cid, cdocs[cid], child_guid, cdocs)
        for child_id in re.findall(r"- \{fileID: (-?\d+)\}\n", children):
            if child_id in owner_docs:
                visit(child_id, owner_docs[child_id], owner_guid, owner_docs)

    root = tl_docs["11400000"]
    for tid in re.findall(r"- \{fileID: (-?\d+)\}", root.split("m_Tracks:")[1].split("m_FixedDuration")[0]):
        visit(tid, tl_docs[tid], tl_guid, tl_docs)
    events.sort(key=lambda e: e["time"])
    ends = [e["time"] + e["duration"] for e in events]
    for t in tracks:
        ends += [s["start"] + s["duration"] for s in t.get("states", []) + t.get("active", [])]
    return {"length": round(max(ends + [0.0]), 3), "tracks": tracks, "events": events}


def main():
    result = []
    for name in TIMELINES:
        if os.path.exists(os.path.join(EXPORT, "MonoBehaviour", name + ".playable")):
            cut = extract(name)
            cut["name"] = name
            result.append(cut)
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8") as f:
        json.dump({"_source": f"{project_cfg.CFG.get('EXPORTED_ASSETS_DIR', 'ExportedAssets')} TimelineAssets guide_anim_* + prefab PlayableDirector bindings",
                   "cutscenes": result}, f, indent=1, ensure_ascii=False)
    print("wrote", OUT)


if __name__ == "__main__":
    main()
