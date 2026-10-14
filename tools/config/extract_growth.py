"""Export the confirmed chapter-five Growth chains, without inferred rewards.

Run from any directory with Python 3.13+. Text and numbers intentionally use the
same non-bundle JSON/import pattern as extract_chapters.py.
"""
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CONFIG = ROOT / "ResourcesExport/Config"
DEST = ROOT / "UnityAssets/Assets/_EndlessWinter/Data/Missions/growth.json"
# Bounded to the confirmed opening goals and their immediate successors.
CHAINS = (
    (True, (1330001, 1330002, 1330003)),
    (False, (1335000, 1335001, 1335002)),
    (False, (1334999,)),
    (False, (1335300, 1335301, 1335302)),
    (False, (1335400, 1335401)),
)


def load(name):
    return json.loads((CONFIG / "LuaJSON" / f"game_config_default_{name}.json").read_text(encoding="utf-8"))


def main():
    tasks, system = load("growth_task"), load("systemconfig")
    texts = json.loads((CONFIG / "i18n/en.json").read_text(encoding="utf-8"))
    chains = []
    for is_main, ids in CHAINS:
        entries = []
        for index, task_id in enumerate(ids):
            row = tasks[str(task_id)]
            num = row["num"]
            condition = row.get("condition", [])
            pre = row.get("preconditions", [])
            requires = pre[1] if pre and pre[0] == 7 else 0
            if requires != (ids[index - 1] if index else 0):
                raise ValueError(f"Broken selected chain at {task_id}")
            furnace = pre[1] if pre and pre[0] == 1 else 0
            building, level = 0, 0
            if is_main:
                assert condition[0] == 200
                building, level = condition[1], num
                kind, key, n2 = "BuildingLevel", "task_desc_001", texts[f"builddes_name_{building}"]
            else:
                key = row["des"]
                kind = {102: "HeroLevel", 101: "HeroCountAtLevel", 123: "ExplorationStage"}.get(row["task_type"], "NotYetSupported")
                level = condition[1] if condition else num
                n2 = level
            text = texts[key].replace("%{n1}", str(num)).replace("%{n2}", str(n2))
            if "%{" in text:
                raise ValueError(f"Unresolved text at {task_id}: {text}")
            entries.append({"furnaceLevel": furnace, "mission": {
                "id": task_id, "kind": kind, "text": text, "building": building,
                "level": level, "target": num, "requires": requires,
                "reward": row.get("reward", []),
            }})
        chains.append({"main": is_main, "entries": entries})
    labels = {key: texts[source] for key, source in {
        "title": "task_title_03", "chapterTitle": "task_title_02",
        "chapterTab": "task_title_02_01", "growthTab": "task_title_03_01",
        "mainLabel": "task_title_05", "sideLabel": "task_title_06",
    }.items()}
    result = {"_source": "growth_task + systemconfig + i18n/en.json (extract_growth.py)",
              "unlockFurnaceLevel": system["growth_quest"]["function_open_conditon"][1],
              **labels, "chains": chains}
    DEST.write_text(json.dumps(result, indent=2, ensure_ascii=True) + "\n", encoding="utf-8")
    print(f"Exported {sum(len(c['entries']) for c in chains)} missions in {len(chains)} chains to {DEST}")


if __name__ == "__main__":
    main()
