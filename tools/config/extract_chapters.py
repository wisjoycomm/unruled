"""
Chapter Missions 1-3 from the original config (chapter_quest_conf + chapter_task) →
Data/Missions/chapters_ch1-3.json.

Condition decoding (checked against the LD captures):
  task_type 109 or jump 40002  → build: [200, building]               "Build: Sawmill"
  no task_type, num n          → building reaches Lv n                 "Upgrade Furnace to Lv. 2"
  task_type 111, [.., 250, t]  → one furniture of type t reaches Lv num  "Upgrade one Bunk Bed in Shelter 1 to Lv. 2"
  task_type 112, [.., 250, t, 151, L] → num pieces of type t reach Lv L  "Upgrade 2 Bunk Beds in Shelter 1 to Lv. 4"
  task_type 144                → appoint num survivors to the building
  task_type 103, [200, hero]   → hero reaches star sub-tier num        "Upgrade Bahiti to 1-Star"
  task_type 116, [250, t, 151, L] → train num troops of type t, Lv L   "Train 10 Infantry"
  task_type 139, [151, L]      → defeat a Lv L beast                   "Defeat 1 Lv. 1 Beast"
  task_type 134                → finish num Intel missions             "Finish 3 Intel Mission(s)"
  1340001 (no condition)       → light the fire
Mission text is generated from the decoded goal (localization export has no mission strings).
Per-mission reward when the row has none: 20 gems + 100 wood (capture 071).

Run:  python tools/config/extract_chapters.py
"""
import json, os, sys

sys.stdout.reconfigure(encoding="utf-8")
sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))
import project_cfg  # noqa: E402

ROOT = str(project_cfg.ROOT)
SRC = str(project_cfg.LUA_CONFIG_DIR)
DST = str(project_cfg.DATA_DIR / "Missions")

CHAPTERS = {
    "1001": ("Chapter 1", "A Place to Call Home"),
    "1002": ("Chapter 2", "Tough Decisions"),
    "1003": ("Chapter 3", "Taking Shape"),
    "1004": ("Chapter 4", "Explorers, Godspeed!"),
    "1005": ("Chapter 5", "Time to Mine"),
    "1006": ("Chapter 6", "Legal Constraints"),
    "1007": ("Chapter 7", "Brave the Icefield"),
}
BUILDING_NAMES = {301: "Furnace", 302: "Sawmill", 303: "Coal Mine", 305: "Hunters' Hut", 306: "Cookhouse",
                  307: "Clinic", 308: "Shelter 1", 309: "Shelter 2", 310: "Shelter 3",
                  311: "Shelter 4", 318: "Hero Hall", 304: "Iron Mine", 312: "Shelter 5", 313: "Shelter 6",
                  319: "Chief's House", 321: "Infantry Camp", 328: "Lighthouse", 332: "Barricade"}
HERO_NAMES = {50006: "Bahiti"}
# Troop type in condition [250, type, 151, level] (soldiers.soldier_type): 1 Infantry, 2 Lancer, 3 Marksman.
TROOP_NAMES = {1: "Infantry", 2: "Lancers", 3: "Marksmen"}
FURNITURE_TYPE_NAMES = {1: ("Bunk Bed", "Bunk Beds"), 2: ("Desk", "Desks"), 18: ("Hunters' Station", "Hunters' Stations"),
                        24: ("Workbench", "Workbenches"), 30: ("Sorting Station", "Sorting Stations")}
DEFAULT_REWARD = [101, 20, 103, 100]


def load(name):
    with open(os.path.join(SRC, name), encoding="utf-8") as f:
        return json.load(f)


def decode(task_id, row):
    cond = row.get("condition") or []
    if isinstance(cond, dict):
        cond = []
    jump = row.get("jump") or []
    if isinstance(jump, dict):
        jump = []
    ttype = row.get("task_type")
    num = row.get("num", 1)
    building = cond[1] if len(cond) >= 2 and cond[0] == 200 else (jump[1] if len(jump) >= 2 else 0)
    bname = BUILDING_NAMES.get(building, str(building))
    if task_id == 1340001:
        return {"kind": "LightFire", "building": 301, "target": 1, "text": "Light a fire"}
    if ttype == 123:
        stage = cond[1]
        return {"kind": "ExplorationStage", "target": stage, "text": "Clear Exploration Stage %d" % stage}
    if ttype == 102:
        return {"kind": "HeroLevel", "target": num, "text": "Upgrade a Hero to Lv. %d" % num}
    if ttype == 118:
        return {"kind": "AdvancedRecruitment", "target": num, "text": "Use Advanced Recruitment %d time(s)" % num}
    # [200, hero], num = star sub-tier (6 per star): "Upgrade Bahiti to 1-Star (1/1)".
    if ttype == 103:
        hero = cond[1]
        return {"kind": "HeroStar", "hero": hero, "level": num, "target": 1,
                "text": "Upgrade %s to %d-Star" % (HERO_NAMES.get(hero, str(hero)), num // 6)}
    # [250, troop type, 151, troop level]: "Train 10 Infantry".
    if ttype == 116:
        return {"kind": "TrainTroops", "troopType": cond[1], "level": cond[3], "target": num,
                "text": "Train %d %s" % (num, TROOP_NAMES.get(cond[1], "troops"))}
    # [151, beast level]: "Defeat 1 Lv. 1 Beast".
    if ttype == 139:
        return {"kind": "DefeatBeast", "level": cond[1], "target": 1, "text": "Defeat 1 Lv. %d Beast" % cond[1]}
    if ttype == 134:
        return {"kind": "IntelMissions", "target": num, "text": "Finish %d Intel Mission(s)" % num}
    # jump 40002 = open the build panel → "Build: X" (1340006 Hunters' Hut, 1340201 Coal Mine have no task_type).
    if ttype == 109 or (jump and jump[0] == 40002):
        return {"kind": "BuildingLevel", "building": building, "level": 1, "target": 1, "text": "Build: " + bname}
    if ttype == 111:
        ftype = cond[3]
        single = FURNITURE_TYPE_NAMES.get(ftype, ("furniture", "furniture"))[0]
        return {"kind": "FurnitureLevel", "building": building, "furnitureType": ftype, "level": num, "count": 1,
                "target": num, "text": "Upgrade one %s in %s to Lv. %d" % (single, bname, num)}
    if ttype == 112:
        ftype = cond[3]
        level = cond[5] if len(cond) >= 6 else 1
        plural = FURNITURE_TYPE_NAMES.get(ftype, ("furniture", "furniture"))[1]
        return {"kind": "FurnitureLevel", "building": building, "furnitureType": ftype, "level": level, "count": num,
                "target": num, "text": "Upgrade %d %s in %s to Lv. %d" % (num, plural, bname, level)}
    if ttype == 144:
        return {"kind": "Workers", "building": building, "target": num,
                "text": "Appoint %d Survivor(s) to %s work role" % (num, bname)}
    return {"kind": "BuildingLevel", "building": building, "level": num, "target": num,
            "text": "Upgrade %s to Lv. %d" % (bname, num)}


def main():
    conf = load("game_config_default_chapter_quest_conf.json")
    tasks = load("game_config_default_chapter_task.json")
    chapters = []
    for cid, (title, name) in CHAPTERS.items():
        c = conf[cid]
        missions = []
        for tid in c["quest_group"]:
            row = tasks[str(tid)]
            m = decode(tid, row)
            pre = row.get("preconditions") or []
            m["id"] = tid
            m["requires"] = pre[1] if isinstance(pre, list) and len(pre) >= 2 and pre[0] == 7 else 0
            m["reward"] = row.get("reward") or DEFAULT_REWARD
            missions.append(m)
        chapters.append({"id": int(cid), "title": title, "name": name, "reward": c["rewards"], "missions": missions})
    os.makedirs(DST, exist_ok=True)
    path = os.path.join(DST, "chapters_ch1-3.json")
    with open(path, "w", encoding="utf-8") as f:
        json.dump({"_source": "chapter_quest_conf + chapter_task (extract_chapters.py)", "chapters": chapters}, f, indent=1, ensure_ascii=False)
    print("wrote", path)
    for ch in chapters:
        print(ch["title"], ch["name"], ch["reward"])
        for m in ch["missions"]:
            print("  ", m["id"], m["kind"], "|", m["text"], "| req", m["requires"], "| rew", m["reward"])


if __name__ == "__main__":
    main()
