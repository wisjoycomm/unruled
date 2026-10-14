"""Export exploration stages 1-50 (chapter 7 asks for stage 50). Run: python tools/config/extract_stages.py."""
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SRC = ROOT / "ResourcesExport" / "Config" / "LuaJSON"
DATA = ROOT / "UnityAssets" / "Assets" / "_EndlessWinter" / "Data"
RESOURCE_TYPES = ROOT / "UnityAssets/Assets/_EndlessWinter/Scripts/Contracts/Economy/ResourceType.cs"
STAGE_COUNT = 50
# drop_conf chests have no item owner yet. Do not send their ids to ItemService or the wallet.
SKIPPED_REWARDS = {800005: "Unregistered drop_conf chest; omitted until chest support exists.",
                   800006: "Unregistered drop_conf chest; omitted until chest support exists."}
# The Lua export drops each column's default value: every row missing attack/hp gives exactly 138/2160 from its
# unit's fixed attack:def and hp:def ratio, and no row states those values.
MONSTER_DEFAULTS = {"attack": 138, "hp": 2160}
# Stages 35 and 36 are the only rows without a power, so they share the dropped default, which lies between stage 34
# (145442) and stage 37 (154970). ESTIMATE (midpoint) until an LD capture of stage 35 shows the real value.
STAGE_POWER_ESTIMATES = {35: 150206, 36: 150206}


def load(name):
    return json.loads((SRC / f"game_config_default_{name}.json").read_text(encoding="utf-8"))


def main():
    levels = load("explore_level_config")
    monsters = load("explore_monster_config")
    heroes = load("hero_config")
    item_source = load("item_config")
    resources = load("resource_conf")
    known_items = {row["id"] for row in json.loads((DATA / "Items/items.json").read_text())["items"]}
    known_resources = {int(value) for value in re.findall(r"\b\w+\s*=\s*(\d+)", RESOURCE_TYPES.read_text())}
    known_resources.discard(0)
    stages = []
    reward_ids = set()
    idle = load("systemconfig")["adventure_pve"]
    for key in ("max_hang_up_time", "hang_up_reward_interval"):
        if not isinstance(idle[key], int) or idle[key] <= 0:
            raise ValueError(f"Invalid adventure_pve.{key}")
    for index, source in enumerate(levels[:STAGE_COUNT]):
        number = index + 1
        raw_rewards = source["clear_reward"]
        if len(raw_rewards) % 2:
            raise ValueError(f"Stage {number}: incomplete reward pair")
        rewards = []
        for item_id, amount in zip(raw_rewards[::2], raw_rewards[1::2]):
            if item_id in SKIPPED_REWARDS:
                continue
            if item_id not in known_items and item_id not in known_resources:
                raise ValueError(f"Stage {number}: reward {item_id} has no registered owner")
            if amount <= 0:
                raise ValueError(f"Stage {number}: invalid reward amount for {item_id}")
            rewards.extend([item_id, amount])
            reward_ids.add(item_id)
        monster = {**MONSTER_DEFAULTS, **monsters[str(source["monsterinfo"][1])]}
        unit_id = str(monster["battle_unit_id"])
        avatar = source.get("head_icon") or heroes[unit_id]["avatar"]
        hangs_reward = source["hangs_reward"]
        if len(hangs_reward) != 4 or hangs_reward[::2] != [106, 107] or any(value <= 0 for value in hangs_reward[1::2]):
            raise ValueError(f"Stage {number}: expected positive Steel and EXP idle rates")
        reward_ids.update(hangs_reward[::2])
        stages.append({
            "stage": number,
            "power": source["power"] if "power" in source else STAGE_POWER_ESTIMATES[number],
            "monsterLevel": source["monster_level"],
            "levelType": source.get("level_type", 0),
            "monsterInfo": source["monsterinfo"],
            "precondition": source.get("precondition_02") or [],
            "enemyAvatar": avatar,
            "attack": round(monster["attack"] * source["attack_coe"], 6),
            "defense": round(monster["def"] * source["def_coe"], 6),
            "health": round(monster["hp"] * source["hp_coe"], 6),
            "clearReward": rewards,
            "hangs_reward": hangs_reward,
        })
    visuals = []
    for item_id in sorted(reward_ids):
        row = item_source.get(str(item_id)) or resources.get(str(item_id))
        if row is None:
            raise ValueError(f"Reward {item_id}: missing visual config")
        visuals.append({"id": item_id, "icon": row.get("icon", f"item_icon_{item_id}"),
                        "quality": row.get("quality", 1)})
    output = {
        "_source": "explore_level_config + explore_monster_config + systemconfig.adventure_pve",
        "_skippedRewards": [{"id": key, "reason": value} for key, value in SKIPPED_REWARDS.items()],
        "unlock_auto_level": idle["unlock_auto_level"],
        "max_hang_up_time": idle["max_hang_up_time"],
        "hang_up_reward_interval": idle["hang_up_reward_interval"],
        "stages": stages,
        "rewardVisuals": visuals,
    }
    destination = DATA / "Exploration/stages.json"
    destination.write_text(json.dumps(output, indent=2) + "\n", encoding="utf-8")
    print(f"Wrote {len(stages)} stages to {destination}; skipped chests {', '.join(map(str, SKIPPED_REWARDS))}.")


if __name__ == "__main__":
    main()
