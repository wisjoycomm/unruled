"""Generate the FTUE story dialog from the original new_player_plot config.

Every NpcDialogList the chapter 1-3 guides play becomes one script ("plot_<first id>"); the chain
follows nextIndex exactly like the game. Each talker id (new_player_talker_config) becomes a pose of
its character (5xx Sean, 6xx Buster, 7xx Bill, 8xx Alina...) with the original avatar; pos_type 2
puts the portrait on the right. Text comes from the English language table.

Output: Data/Dialog/ftue_plot_dialog.json (same schema as ftue_dialog_ch1-3.json).
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))
import dump_guides as g  # noqa: E402  (language table + plot chain helpers)
import project_cfg  # noqa: E402

ROOT = str(project_cfg.ROOT)
LUA = str(project_cfg.LUA_CONFIG_DIR)
OUT = str(project_cfg.DATA_DIR / "Dialog" / "ftue_plot_dialog.json")

# Dialog lists played by Guide_Plot_StoryLine_* / guide_anim_* in chapters 1-3, in story order.
PLOTS = [1000101, 1000102, 1000168, 1000113, 1000173, 1000179, 1000187, 1000154, 1000194, 1000195, 1000197]

FAMILY = {5: "sean", 6: "buster", 7: "bill", 8: "alina"}
NAMED = {1001: "tomasin", 1101: "natalia"}
# Original avatars that also exist as animated Spine portraits in the export.
SPINE = {
    "shawn_bust_normal_01_01": "shawn_normal",
    "shawn_bust_flustered_01_05": "shawn_flustered",
    "elena_bust_normal_04_01": "ailina_normal",
    "elena_bust_worried_04_04": "ailina_worried",
}
NAMES = {"sean": "Sean", "buster": "Buster", "bill": "Bill", "alina": "Alina", "tomasin": "Tomasin",
         "natalia": "Natalia", "bahiti": "Bahiti"}


def speaker_of(talker_id):
    if talker_id in NAMED:
        return NAMED[talker_id]
    family = talker_id if talker_id < 100 else talker_id // 100
    return FAMILY.get(family)


def main():
    talkers = json.load(open(os.path.join(LUA, "game_config_default_new_player_talker_config.json"), encoding="utf-8"))
    poses = {}
    scripts = []
    for first in PLOTS:
        lines = []
        for pid in g.chain(first):
            e = g.PLOT[str(pid)]
            content = e.get("content") or []
            if isinstance(content, dict) or not content:
                continue
            tid = e.get("speakerId")
            speaker = speaker_of(tid) if tid else "bahiti"
            avatar = talkers[str(tid)]["avatar"] if tid else "hero_fullpic_50006"
            emotion = str(tid) if tid else "normal"
            poses.setdefault(speaker, {})[emotion] = avatar
            for key in content:
                line = {"style": "speech", "speaker": speaker, "emotion": emotion,
                        "side": "right" if e.get("pos_type") == 2 else "left", "text": g.tr(key), "source": "%d %s" % (pid, key)}
                confirm = e.get("Show_confirm")
                if confirm and confirm[-1] == "common_btn_01":
                    line["confirm"] = "common_go"
                lines.append(line)
        scripts.append({"id": "plot_%d" % first, "skippable": True, "lines": lines})

    # OldManDiedPanel (opened by guide_anim_03 "OpenOldManDeadView 0.8;8.4;0.8;guide_plot_line_19;4.5;
    # guide_plot_line_91"): night scene + grave/family pictures, then the notebook line on black.
    scripts.append({"id": "plot_old_man_died", "skippable": True, "lines": [
        {"style": "narration", "illustration": "plot_oldmandied_001", "blackout": True, "text": g.tr("guide_plot_line_19"), "source": "guide_plot_line_19"},
        {"style": "narration", "illustration": "plot_oldmandied_002", "inset": "plot_oldmandied_003", "blackout": True, "text": g.tr("guide_plot_line_19"), "source": "guide_plot_line_19"},
        {"style": "narration", "blackout": True, "text": g.tr("guide_plot_line_91"), "source": "guide_plot_line_91"}]})
    # Guide_Animal_Attacks_2 "OpenTimeEvolution 0.8;3.0;0.8;guide_plot_line_84".
    scripts.append({"id": "plot_smoke_of_hope", "skippable": True, "lines": [
        {"style": "narration", "text": g.tr("guide_plot_line_84"), "source": "guide_plot_line_84"}]})

    speakers = []
    for sid, emotions in poses.items():
        speakers.append({"id": sid, "nameKey": "speaker_" + sid, "name": NAMES[sid], "poses": [
            dict({"emotion": emo, "bust": avatar}, **({"spine": SPINE[avatar]} if avatar in SPINE else {}))
            for emo, avatar in emotions.items()]})

    data = {"_source": "ResourcesExport new_player_plot + new_player_talker_config + game_languages_local_en (tools/config/extract_plot_dialog.py)",
            "speakers": speakers, "scripts": scripts}
    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=1, ensure_ascii=False)
    print("wrote", OUT, len(scripts), "scripts")


if __name__ == "__main__":
    main()
