"""Extract the Chapter 3 rescue cutscene timeline (guide_anim_04) into JSON and Markdown cue sheets.

Reads the 110 ScriptableObject JSON files in:
  ResourcesExport/Config/AllScriptables/scene_city_plot_guide_anim_04_507f715315c40ca06ff087677265e3f0/
resolving tracks, child tracks, clips (m_Start, m_Duration, m_DisplayName), and playable assets by m_PathID.
Also cross-references AllAssetsExport (guide_anim_04.playable + guide_anim_04.prefab + Recorded*.anim) when
present to resolve PlayableDirector scene bindings and ControlTrack exposed references.

Outputs:
  .temp/agy/guide_anim_04_cues.json
  .temp/agy/guide_anim_04_cues.md
"""
import glob
import json
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))
import dump_guides  # noqa: E402
import project_cfg  # noqa: E402

ROOT = str(project_cfg.ROOT)
TIMELINE_DIR = str(
    project_cfg.RESOURCES_CONFIG_DIR
    / "AllScriptables"
    / "scene_city_plot_guide_anim_04_507f715315c40ca06ff087677265e3f0"
)
EXPORT_ASSETS = str(project_cfg.EXPORTED_ASSETS_DIR)
OUT_DIR = str(project_cfg.TEMP_DIR / "agy")
OUT_JSON = os.path.join(OUT_DIR, "guide_anim_04_cues.json")
OUT_MD = os.path.join(OUT_DIR, "guide_anim_04_cues.md")

ROOT_PATH_ID = -3662405572381473867

SCRIPT_TYPES = {
    5038369675663816281: "TimelineAsset",
    -1101229750744939834: "GroupTrack",
    5412775887553941483: "AnimationTrack",
    -5120827127598350935: "TimelineCustomAnimationTrack",
    -6422783510097303990: "ActivationTrack",
    3769382341615217048: "ControlTrack",
    -1838131115648981441: "TimelineSimpleControllerNotifyTrack",
}

NOTIFY_KINDS = {
    0: "dialog_start",
    3: "dialog_preload",
    4: "audio",
    8: "wait",
    12: "speech_bubble",
    13: "ui_flag",
}

ACTOR_LABELS = {
    "timeline/buliding/TmCamera_01_3": "Camera (TmCamera_01_3)",
    "timeline/actor/citizen04": "citizen04 (Sean, blue coat, group 'huang')",
    "timeline/actor/citizen04/plot_npc_citizen_23": "citizen04 (Sean, blue coat, group 'huang')",
    "timeline/actor/citizen04/plot_npc_citizen_23/bone_root/Bn_Hip/Bn_Spine/Bn_R_UpperArm/Bn_R_ForeArm/Bn_R_Hand/v_rhand/people_tool_raw_meat": "citizen04 held meat prop (Sean)",
    "timeline/actor/citizen04/plot_npc_citizen_23/bone_root/v_top/Particle System": "citizen04 red '!' alert (Sean)",
    "timeline/actor/citizen04/plot_npc_citizen_23/fx_plot_jiaoying02/jiaoying02_1": "citizen04 footprints (Sean)",
    "timeline/actor/citizen01": "citizen01 (Buster, brown fur coat, group 'lan')",
    "timeline/actor/citizen01/plot_npc_citizen_13": "citizen01 (Buster, brown fur coat, group 'lan')",
    "timeline/actor/citizen01/plot_npc_citizen_13/bone_root/Bn_Hip/Bn_Spine/Bn_R_UpperArm/Bn_R_ForeArm/Bn_R_Hand/v_rhand/people_tool_raw_meat": "citizen01 held meat prop (Buster)",
    "timeline/actor/citizen01/plot_npc_citizen_13/bone_root/v_top/Particle System (1)": "citizen01 red '!' alert (Buster)",
    "timeline/actor/citizen01/plot_npc_citizen_13/fx_plot_jiaoying02/jiaoying02_1": "citizen01 walk footprints (Buster)",
    "timeline/actor/citizen01/plot_npc_citizen_13/fx_plot_jiaoying02/jiaoying02_2": "citizen01 run footprints (Buster)",
    "timeline/actor/monster_white_bear": "monster_white_bear (Polar Bear, group 'BJX')",
    "timeline/actor/monster_white_bear/3d_monster_white_bear": "monster_white_bear (Polar Bear, group 'BJX')",
    "timeline/actor/monster_white_bear/3d_monster_white_bear/root/Particle System (2)": "monster_white_bear '!' alert (Polar Bear)",
    "timeline/actor/'city_anim_building_explorer_cabin_mammoth '": "Mammoth Wagon (group 'mmzc')",
    "timeline/actor/'city_anim_building_explorer_cabin_mammoth '/3d_city_anim_building_explorer_cabin_mammoth": "Mammoth Wagon (group 'mmzc')",
    "timeline/actor/'city_anim_building_explorer_cabin_mammoth '/3d_city_anim_building_explorer_cabin_mammoth/root/city_hero_explorer": "Bahiti (city_hero_explorer, group 'hero')",
    "timeline/actor/'city_anim_building_explorer_cabin_mammoth '/3d_city_anim_building_explorer_cabin_mammoth/root/city_hero_explorer/3d_hero_explorer_normalcolor": "Bahiti (3d_hero_explorer_normalcolor, group 'hero')",
    "timeline/actor/'city_anim_building_explorer_cabin_mammoth '/3d_city_anim_building_explorer_cabin_mammoth/root/city_hero_explorer/3d_hero_explorer_normalcolor/bone_root/Bip001/Bip001 Pelvis/Particle System": "Bahiti red '!' alert (group 'hero')",
    "timeline/actor/people_tool_raw_meat": "Dropped meat leg #1 (people_tool_raw_meat)",
    "timeline/actor/people_tool_raw_meat_2": "Dropped meat leg #2 (people_tool_raw_meat_2)",
    "timeline/buliding/nature_anim_tree_cedar_01/3d_nature_anim_tree_cedar_1": "Cedar Tree #1 (nature_anim_tree_cedar_01)",
    "timeline/buliding/nature_anim_tree_cedar_02/3d_nature_anim_tree_cedar_2": "Cedar Tree #2 (nature_anim_tree_cedar_02)",
    "timeline/fx_plot630_guadian/fx_shumu01": "Tree #1 snow burst (fx_shumu01)",
    "timeline/fx_plot630_guadian/fx_shumu02": "Tree #2 snow burst (fx_shumu02)",
    "timeline/fx_plot630_guadian/fx_xion": "Bear attack hit FX point (fx_xion)",
    "timeline/fx_plot630_guadian/fx_rw01": "Hunter fall snow puff point (fx_rw01)",
    "timeline/fx_plot630_guadian/fx_hit": "Wagon-ram-bear hit FX point (fx_hit)",
    "timeline/fx_plot630_guadian/fx_xion02": "Bear knockdown snow puff point (fx_xion02)",
    "timeline/fx_plot630_guadian/fx_yingxion": "Bahiti landing snow puff point (fx_yingxion)",
}


def _yaml_docs(text):
    out = {}
    for block in re.split(r"^--- ", text, flags=re.M)[1:]:
        head, _, body = block.partition("\n")
        m = re.search(r"&(-?\d+)", head)
        if m:
            out[m.group(1)] = body
    return out


def _yaml_field(body, name):
    m = re.search(r"^\s*" + re.escape(name) + r": ?(.*)$", body, flags=re.M)
    return m.group(1).strip() if m else ""


def _go_path(prefab_docs, go_id):
    names = []
    while go_id:
        go = prefab_docs.get(go_id, "")
        names.append(_yaml_field(go, "m_Name"))
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


def load_json_folder(folder):
    by_id = {}
    for path in glob.glob(os.path.join(folder, "*.json")):
        fname = os.path.basename(path)
        m = re.search(r"__(-?\d+)\.json$", fname)
        if m:
            with open(path, encoding="utf-8") as f:
                by_id[int(m.group(1))] = (fname, json.load(f))
    return by_id


def resolve_scene_bindings(by_id):
    """Map track m_PathID -> bound GameObject path and exposedName -> GameObject path if AllAssetsExport exists."""
    tl_path = os.path.join(EXPORT_ASSETS, "MonoBehaviour", "guide_anim_04.playable")
    prefab_path = os.path.join(EXPORT_ASSETS, "GameObject", "guide_anim_04.prefab")
    if not (os.path.exists(tl_path) and os.path.exists(prefab_path)):
        return {}, {}

    with open(tl_path + ".meta", encoding="utf-8") as f:
        tl_guid = re.search(r"guid: (\w+)", f.read()).group(1)
    with open(tl_path, encoding="utf-8") as f:
        tl_docs = _yaml_docs(f.read())
    with open(prefab_path, encoding="utf-8") as f:
        prefab = _yaml_docs(f.read())

    director = next(b for b in prefab.values() if "m_PlayableAsset:" in b and "m_SceneBindings:" in b)
    bindings = {}
    for key_id, key_guid, value in re.findall(
        r"key: \{fileID: (-?\d+), guid: (\w+), type: 2\}\n\s+value: \{fileID: (-?\d+)\}", director
    ):
        if value == "0":
            continue
        comp = prefab.get(value, "")
        go = re.search(r"m_GameObject: \{fileID: (-?\d+)", comp)
        gid = go.group(1) if go else (value if value in prefab else None)
        if gid:
            bindings[(key_id, key_guid)] = _go_path(prefab, gid)

    exposed = {}
    for exp, val in re.findall(r"- (-?\d+): \{fileID: (-?\d+)\}", director):
        if val != "0" and val in prefab:
            exposed[exp] = _go_path(prefab, val)

    root_yaml = tl_docs["11400000"]
    yaml_top_tids = re.findall(
        r"- \{fileID: (-?\d+)\}", root_yaml.split("m_Tracks:")[1].split("m_FixedDuration")[0]
    )
    json_top_tids = [t["m_PathID"] for t in by_id[ROOT_PATH_ID][1]["m_Tracks"]]

    track_target = {}
    for y_tid, j_tid in zip(yaml_top_tids, json_top_tids):
        y_body = tl_docs[y_tid]
        track_target[j_tid] = bindings.get((y_tid, tl_guid), "")
        j_children = [c["m_PathID"] for c in by_id[j_tid][1].get("m_Children", [])]
        if "m_Children:" in y_body and j_children:
            ch_part = y_body.split("m_Children:")[1].split("m_Clips:")[0]
            y_children = re.findall(r"\{fileID: (-?\d+), guid: (\w+), type: 2\}", ch_part)
            for (yc_id, yc_guid), jc_id in zip(y_children, j_children):
                track_target[jc_id] = bindings.get((yc_id, yc_guid), "")

    return track_target, exposed


def format_notify(disp, tpl):
    kind = int(tpl.get("param1", 0))
    p2 = tpl.get("param2", 0)
    p3 = str(tpl.get("param3", "")).strip()
    p4 = str(tpl.get("param4", "")).strip()
    kind_name = NOTIFY_KINDS.get(kind, f"kind_{kind}")

    if kind == 12:
        action = "show" if int(p2) == 1 else "hide"
        text = dump_guides.tr(p4) if p4.startswith("guide_plot_line_") else p4
        return f"bubble {action}: {p4} (\"{text}\")", p3, {"kind": kind, "kind_name": kind_name, "action": action, "speaker": p3, "line_key": p4, "text": text}
    if kind == 4:
        return f"{disp} (audio: {p3})", p3, {"kind": kind, "kind_name": kind_name, "audio": p3}
    if kind in (0, 3):
        lines = dump_guides.dialog(int(p4)) if p4.isdigit() else []
        first_line = lines[0].strip() if lines else ""
        return f"{kind_name}: {p3} {p4}" + (f" ({first_line})" if first_line else ""), p3, {"kind": kind, "kind_name": kind_name, "table": p3, "plot_id": p4}
    if kind == 8:
        return f"{disp} (wait: {p3})", p3, {"kind": kind, "kind_name": kind_name, "target": p3}
    if kind == 13:
        return f"ui_flag {p2}={p3}", f"ui_{p2}", {"kind": kind, "kind_name": kind_name, "flag": p2, "value": p3}
    return f"{disp} (kind={kind} p2={p2} p3={p3} p4={p4})", p3, {"kind": kind, "param2": p2, "param3": p3, "param4": p4}


def extract_timeline():
    by_id = load_json_folder(TIMELINE_DIR)
    track_target, exposed = resolve_scene_bindings(by_id)
    _, root_data = by_id[ROOT_PATH_ID]
    framerate = float(root_data.get("m_EditorSettings", {}).get("m_Framerate", 30.0))

    cues = []
    transform_tracks = []

    def visit_track(tid, group_name=""):
        fname, data = by_id[tid]
        tname = data.get("m_Name", "")
        script_id = data.get("m_Script", {}).get("m_PathID")
        ttype = SCRIPT_TYPES.get(script_id, f"Script_{script_id}")
        full_track_name = f"{group_name}/{tname}" if group_name else tname
        bound_path = track_target.get(tid, "")
        friendly_actor = ACTOR_LABELS.get(bound_path, bound_path or group_name)

        inf_clip = data.get("m_InfiniteClip", {}).get("m_PathID", 0)
        if inf_clip and inf_clip != 0:
            pos = data.get("m_InfiniteClipOffsetPosition", {"x": 0.0, "y": 0.0, "z": 0.0})
            eul = data.get("m_InfiniteClipOffsetEulerAngles", {"x": 0.0, "y": 0.0, "z": 0.0})
            transform_tracks.append({
                "track": full_track_name,
                "track_type": ttype,
                "track_path_id": tid,
                "infinite_clip_path_id": inf_clip,
                "offset_position": [round(float(pos["x"]), 4), round(float(pos["y"]), 4), round(float(pos["z"]), 4)],
                "offset_euler": [round(float(eul["x"]), 4), round(float(eul["y"]), 4), round(float(eul["z"]), 4)],
                "bound_path": bound_path,
                "actor": friendly_actor,
            })

        for clip in data.get("m_Clips", []):
            start = round(float(clip.get("m_Start", 0.0)), 3)
            duration = round(float(clip.get("m_Duration", 0.0)), 3)
            end = round(start + duration, 3)
            disp = clip.get("m_DisplayName", "")
            asset_id = clip.get("m_Asset", {}).get("m_PathID", 0)
            asset_fname, asset_data = by_id.get(asset_id, ("", {}))

            cue_text = disp
            actor = friendly_actor
            target_path = bound_path
            details = {}

            if ttype == "TimelineSimpleControllerNotifyTrack":
                tpl = asset_data.get("template", {})
                cue_text, notify_target, details = format_notify(disp, tpl)
                if notify_target:
                    if notify_target == "citizen04":
                        actor = "citizen04 (Sean, blue coat, group 'huang')"
                    elif notify_target == "citizen01":
                        actor = "citizen01 (Buster, brown fur coat, group 'lan')"
                    else:
                        actor = notify_target
            elif ttype == "ControlTrack":
                exp_name = asset_data.get("sourceGameObject", {}).get("exposedName", "")
                if exp_name in exposed:
                    target_path = exposed[exp_name]
                    actor = ACTOR_LABELS.get(target_path, target_path)
                prefab_pid = asset_data.get("prefabGameObject", {}).get("m_PathID", 0)
                details = {"exposed_name": exp_name, "prefab_path_id": prefab_pid}
            elif ttype == "TimelineCustomAnimationTrack":
                tpl = asset_data.get("template", {})
                details = {
                    "clip_path_id": tpl.get("clip", {}).get("m_PathID", 0),
                    "transition_time": round(float(tpl.get("transitionTime", 0.0)), 3),
                    "speed": round(float(tpl.get("speed", 1.0)), 3),
                }

            entry = {
                "start": start,
                "duration": duration,
                "end": end,
                "track": full_track_name,
                "track_type": ttype,
                "cue": cue_text,
                "actor": actor,
            }
            if target_path:
                entry["bound_path"] = target_path
            if details:
                entry["details"] = details
            cues.append(entry)

        for ch in data.get("m_Children", []):
            visit_track(ch["m_PathID"], full_track_name)

    for t in root_data.get("m_Tracks", []):
        visit_track(t["m_PathID"])

    cues.sort(key=lambda c: (c["start"], c["end"], c["track"]))
    total_length = round(max((c["end"] for c in cues), default=0.0), 3)

    return {
        "name": root_data.get("m_Name", "guide_anim_04"),
        "source_folder": "ResourcesExport/Config/AllScriptables/scene_city_plot_guide_anim_04_507f715315c40ca06ff087677265e3f0",
        "framerate": framerate,
        "total_length": total_length,
        "clip_count": len(cues),
        "transform_track_count": len(transform_tracks),
        "transform_tracks": transform_tracks,
        "cues": cues,
    }


def build_markdown(data):
    lines = [
        "# `guide_anim_04` Timeline Cue Sheet (Chapter 3 Bahiti Rescue)",
        "",
        f"- **Source folder**: `ResourcesExport/Config/AllScriptables/scene_city_plot_guide_anim_04_507f715315c40ca06ff087677265e3f0/` (110 JSON files)",
        f"- **Scene bindings cross-checked with**: `AllAssetsExport/ExportedProject/Assets/MonoBehaviour/guide_anim_04.playable` & `GameObject/guide_anim_04.prefab`",
        f"- **Framerate**: {data['framerate']:.0f} fps",
        f"- **Total timeline length**: **{data['total_length']:.3f} s** (active cutscene ends and triggers dialog `1000125` at **21.067–21.133 s**; `CCidle` runs to **24.633 s**; `等待` wait clip holds to **26.016 s**)",
        f"- **Total timed clips**: {data['clip_count']} clips (+ {data['transform_track_count']} recorded InfiniteClip transform tracks)",
        "",
        "## Bound Actors & Track Groups",
        "",
        "| Group / Track | Bound Prefab Path | Role in Scene |",
        "|---|---|---|",
        "| `huang` | `timeline/actor/citizen04/plot_npc_citizen_23` | **Sean** (blue coat hunter; speaks `guide_plot_line_74`, knocked down at 7.667 s into `behit1_hit` -> stays down in `behit1_loop` until end) |",
        "| `lan` | `timeline/actor/citizen01/plot_npc_citizen_13` | **Buster** (brown fur parka hunter; shouts `guide_plot_line_75` `\"Sean, run!\"`, knocked down at 8.000 s into `behit2_hit` -> `behit2_loop` -> `behit3` -> gets up at 19.600 s `behit2_up` -> `idle1` and speaks `guide_plot_line_83` `\"...\"`) |",
        "| `BJX` | `timeline/actor/monster_white_bear/3d_monster_white_bear` | **Polar Bear** (`run`, `roar`, `attack`, `turnleft`, `die`) |",
        "| `mmzc` | `timeline/actor/'city_anim_building_explorer_cabin_mammoth '/3d_city_anim_building_explorer_cabin_mammoth` | **Mammoth Wagon** (`run` 10.033–13.433 s, `attack` 13.433–15.300 s, `idle` 15.300–22.667 s) |",
        "| `hero` | `.../root/city_hero_explorer/3d_hero_explorer_normalcolor` | **Bahiti** (`CCloop` riding wagon 5.633–15.300 s, `CC` jumping off wagon 15.300–19.633 s, `CCidle` standing 19.633–24.633 s) |",
        "| `tree` | `timeline/buliding/nature_anim_tree_cedar_01` & `02` | **Two Cedar Trees** shaken when bear emerges (`anim_1` at 4.833 s and 4.933 s) |",
        "| Root Meat #1/#2 | `timeline/actor/people_tool_raw_meat` & `people_tool_raw_meat_2` | **Two Dropped Meat Legs** activated on the ground at 5.633 s when the hunters' hand-held meat props deactivate at 5.667 s |",
        "",
        "## Recorded Transform Tracks (`m_InfiniteClip`)",
        "",
        "| Track | Type | Offset Position | Offset Euler | Bound Actor |",
        "|---|---|---|---|---|",
    ]
    for tr in data["transform_tracks"]:
        pos = f"({tr['offset_position'][0]}, {tr['offset_position'][1]}, {tr['offset_position'][2]})"
        eul = f"({tr['offset_euler'][0]}, {tr['offset_euler'][1]}, {tr['offset_euler'][2]})"
        lines.append(f"| `{tr['track']}` | `{tr['track_type']}` | `{pos}` | `{eul}` | {tr['actor']} |")

    lines.extend([
        "",
        "## All Timeline Clips (Sorted by Start Time)",
        "",
        "| Start (s) | Duration (s) | End (s) | Track | Track Type | Animation / FX / Notify Cue | Bound Actor / Target |",
        "|---:|---:|---:|---|---|---|---|",
    ])
    for c in data["cues"]:
        cue_escaped = c["cue"].replace("|", "\\|")
        actor_escaped = c["actor"].replace("|", "\\|")
        lines.append(
            f"| {c['start']:0.3f} | {c['duration']:0.3f} | {c['end']:0.3f} | `{c['track']}` | `{c['track_type']}` | `{cue_escaped}` | {actor_escaped} |"
        )

    lines.extend([
        "",
        "## Story Mapping (Timeline -> LD Beats & Frames)",
        "",
        "The LD capture timestamps (`00120_954150.png` .. `00124_969780.png`) align with the `guide_anim_04` timeline via `t_timeline ≈ (frame_ms - 949250) / 1000`:",
        "",
        "| Beat | Time Range (s) | LD Frame | What Happens & Which Clips Drive It |",
        "|---|---|---|---|",
        "| **1. Hunters walk & talk** | `0.000 – 4.167` | *(before 00120)* | Both hunters walk (`huang`/`citizen04` Sean `walk2` `0.000–4.000`, `lan`/`citizen01` Buster `walk2` `0.000–4.167`) carrying meat legs (`ActivationTrack` `0.000–5.667`) and leaving footprint trails (`jiaoyin` `0.000–5.633`, `jiaoyin (1)` `0.000–7.533`). Footstep SFX `脚步5` (`Guide_se_footstep_005`) plays at `0.033`. Sean shows speech bubble `guide_plot_line_74` (*\"We'll have venison for dinner! I cannot wait!\"*) + VO from `0.433` to `4.133`. |",
        "| **2. Bear alert, tree shake & bear appears** | `3.133 – 6.167` | **00120** (`t ≈ 4.90 s`) | At `3.133`, `熊出现` (`Guide_se_bearattack`) and `战斗音乐` (`battle_bgm_001`) start; Skip/cutscene UI flag `631=true` turns on at `3.333`. Both hunters pop red `!` alert marks (`3.767–5.500` / `5.517`) and play `walk2_reaction` (`4.000–5.667` and `4.167–5.767`). Polar bear (`BJX`) activates at `4.267`, runs out from behind the trees (`run` `4.567–4.900`), shakes both cedar trees (`tree` `anim_1` at `4.833` & `4.933`) with falling snow bursts (`fx_plot630_Fallingsnow01` at `4.767` & `4.900`), and roars (`roar` `4.900–6.167`). |",
        "| **3. Meat dropped, hunters panic & bear attacks / hunters fall** | `5.633 – 10.333` | **00121** (`t ≈ 8.72 s`) | At `5.633–5.667`, the hunters' hand-held meat props turn off and the two dropped meat leg props (`people_tool_raw_meat`, `people_tool_raw_meat_2`) activate and tumble onto the snow. Sean (`citizen04` / `huang`) freezes in `scared` (`5.667–7.667`); Buster (`citizen01` / `lan`) plays `run` (`5.767–8.000`) with second footprint trail (`jiaoyin` `5.633–11.533`) and yells `guide_plot_line_75` (*\"Sean, run!\"*, `6.467–8.500`). Bear charges (`run` `6.167–7.033`) and swipes (`attack` `7.033–8.367`). Hit FX `fx_plot630_Hit01` fires at `7.667`; Sean is knocked down (`behit1_hit` `7.667–9.900` -> stays down in `behit1_loop` `9.900–22.667`). Buster falls right after (`behit2_hit` `8.000–10.333` with snow puff `fx_plot630_snow01` at `8.000` -> `behit2_loop` `10.333–17.300`). Bear roars over them (`roar` `8.367–9.700`, seen in LD **00121**). |",
        "| **4. Wagon charge** | `9.500 – 13.433` | **00122** (`t ≈ 12.43 s`) | Bear gets an alert particle (`BJX/Activation Track (2)` `9.500–10.700`) and turns toward the incoming wagon (`turnleft` `9.700–10.700`). Mammoth wagon (`mmzc`) charges in (`run` `10.033–13.433`) wrapped in wind/snow swirl VFX (`fx_plot630_snow02` `10.067–19.134`) while Bahiti (`hero`) rides on top (`CCloop` `5.633–15.300`, seen in LD **00122**). |",
        "| **5. Wagon rams bear / bear knocked away (flees)** | `12.633 – 18.033` | **00123** (`t ≈ 16.46 s`) | Bear takes a short `run` step (`12.633–13.500`) as the mammoth wagon executes `attack` (`13.433–15.300`). Impact FX `fx_plot630_Hit01` plays at `13.567` and snow puff `fx_plot630_snow01` (`fx_xion02`) at `13.833`. Bear plays `die` (`13.500–18.033`) while its recorded transform slides it off-screen to the bottom-left (visible at the lower-left edge of LD **00123**). Wagon parks in `idle` (`15.300–22.667`), while Bahiti dismounts (`CC` `15.300–19.633`) with landing snow puff `fx_plot630_snow01` (`fx_yingxion`) at `17.133`. BGM switches back to `主城音乐` (`city_bgm_001`) at `17.467`. |",
        "| **6. Buster stands up, red `!` & `\"...\"` bubble** | `17.300 – 20.934` | **00124** (`t ≈ 20.53 s`) | Buster (`citizen01` / `lan`) stirs on the ground (`behit3` `17.300–19.600`), gets back up (`behit2_up` `19.600–20.100`), and stands in `idle1` (`20.100–22.667`) beside the still-fallen Sean (`behit1_loop`). Bahiti finishes dismounting into `CCidle` (`19.633–24.633`) with his red `!` mark active (`hero/Activation Track` `19.461–21.000`). Buster shows the `\"...\"` speech bubble (`guide_plot_line_83`, shown at `19.767`, hidden at `20.867` — seen in LD **00124**). |",
        "| **7. End / Sean's dialog** | `21.033 – 26.016` | **00125** (`t ≥ 21.13 s`) | Cutscene UI flag `631=false` fires at `21.033`. Notify tracks preload (`21.067`, `kind=3`) and start (`21.133`, `kind=0`) dialog `new_player_plot` `1000125` (*Sean: \"We're saved. Thank goodness... Phew.\"*), while the timeline holds in `等待` (`21.233–26.016`) over the parked wagon, standing Bahiti and Buster, and fallen Sean. |",
        "",
        "### Notes & Guesses",
        "- **Confirmed (from `guide_anim_04.prefab` bindings)**: Although the timeline group tracks are named `huang` (yellow) and `lan` (blue), `PlayableDirector` binds `huang` to `citizen04` (Sean, blue coat, stays down injured in `behit1_loop`) and `lan` to `citizen01` (Buster, brown fur coat, shouts *\"Sean, run!\"*, gets back up via `behit2_up` -> `idle1`, and displays the `\"...\"` bubble).",
        "- **Guess (UI flag `631`)**: Notify `param1=13, param2=631, param3=true/false` at `3.333 s` and `21.033 s` likely toggles a cutscene camera/HUD lock or Skip overlay state.",
    ])
    return "\n".join(lines) + "\n"


def main():
    data = extract_timeline()
    os.makedirs(OUT_DIR, exist_ok=True)
    with open(OUT_JSON, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2, ensure_ascii=False)
    md = build_markdown(data)
    with open(OUT_MD, "w", encoding="utf-8") as f:
        f.write(md)
    print(f"wrote {OUT_JSON} and {OUT_MD} ({data['clip_count']} clips, length {data['total_length']}s)")


if __name__ == "__main__":
    main()
