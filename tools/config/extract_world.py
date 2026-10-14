"""
World systems that chapter 7 asks for, from the original config → Data/World/world.json.

  soldiers      Rookie troops (soldiers, lv 1) whose camp the remake has: training time per troop and cost per troop.
  camps         Training capacity per camp level (build321 buff_effect 10503, barrack_training_capacity_increase).
  beasts        World map beasts (world_map_monster type 1, "Cave Hyena"): level, recommended power, drop.
  intel         Lighthouse Intel the Searchlight levels name (intel_config upgrade_specify ids): rescue or beast, reward.
  searchlight   intel_radar_config rows: Intel offered at each level, finished Intel needed for the next, level reward.
  stamina       Chief Stamina: attack cost (systemconfig.world_march.atk_monster_cost); regen 1 per 5 minutes
                (resource_desc_201 as shown in game). The cap is not on screen yet: ESTIMATE, see STAMINA_CAP.

Run:  python tools/config/extract_world.py
"""
import json, os, sys

sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))
import project_cfg  # noqa: E402

ROOT = str(project_cfg.ROOT)
SRC = str(project_cfg.LUA_CONFIG_DIR)
DST = str(project_cfg.DATA_DIR / "World" / "world.json")

ROOKIES = [10100]                 # Infantry Camp (321) is the only camp the remake builds.
CAMPS = [321]
BEAST_LEVELS = 10
BEAST_BASE = 7100000
LIGHTHOUSE = 328
STAMINA_REGEN_SECONDS = 300       # "1 Stamina is restored every 5 minutes." (resource_desc_201)
STAMINA_CAP = 100                 # ESTIMATE until an LD capture shows "Chief Stamina is maxed at N".
# intel_config.type → kind: 2 Rescue Survivors (finished on arrival), 1 beast (a fight against its recommended power),
# 3 A Hero's Journey (a squad fight, intel_pve_config; checked by power like a beast).
INTEL_KINDS = {2: 0, 1: 1, 3: 2}
SEARCHLIGHT_LEVELS = 6
CAPACITY_BUFF = 10503


def load(name):
    with open(os.path.join(SRC, "game_config_default_%s.json" % name), encoding="utf-8") as f:
        return json.load(f)


def main():
    system = load("systemconfig")
    soldiers = load("soldiers")
    monsters = load("world_map_monster")
    intel_rows = load("intel_config")
    radar = load("intel_radar_config")

    out_soldiers = []
    for sid in ROOKIES:
        row = soldiers[str(sid)]
        out_soldiers.append({"id": sid, "troopType": sid // 10000, "barrack": row.get("barrack_id", CAMPS[0]),
                             "barrackLevel": row["barrack_level"], "seconds": row["training_time"], "cost": row["cost_res"]})

    camps = []
    for bid in CAMPS:
        levels = sorted((r for r in load("build%d" % bid) if r.get("blv", 0) >= 1), key=lambda r: r["blv"])
        capacity = []
        for r in levels:
            buff = r.get("buff_effect") or []
            pairs = dict(zip(buff[::2], buff[1::2]))
            capacity.append(pairs[CAPACITY_BUFF])
        camps.append({"building": bid, "capacity": capacity})

    # Field beasts are ids 7100001.. (level = id - 7100000); the export drops columns at their default, so "type" and
    # "level" are missing on some rows.
    beasts = []
    for level in range(1, BEAST_LEVELS + 1):
        key = str(BEAST_BASE + level)
        row = monsters[key]
        beasts.append({"id": int(key), "level": level, "power": row["recommendPower"], "reward": row["dropid"]})

    # Each Searchlight level names the Intel it scouts (upgrade_specify); later levels name none and draw from the random
    # pools the remake does not have yet, so they keep the last named Intel.
    pve = load("intel_pve_config")
    searchlight, intel, seen, last_ids = [], [], set(), []
    for index, row in enumerate(radar[:SEARCHLIGHT_LEVELS]):
        ids = row.get("upgrade_specify") or last_ids
        last_ids = ids
        for i in ids:
            if i in seen:
                continue
            seen.add(i)
            src = intel_rows[str(i)]
            kind = INTEL_KINDS[src["type"]]
            beast = monsters.get(str(src.get("condition"))) if kind == 1 else None
            power = beast["recommendPower"] if beast else (pve[0]["power"] if kind == 2 else 0)
            intel.append({"id": i, "kind": kind, "power": power, "reward": src["beat_reward"]})
        searchlight.append({"level": index + 1, "intel": ids, "intelToNext": 0, "reward": row.get("upgrade_reward") or []})
    for index in range(len(searchlight) - 1):
        searchlight[index]["intelToNext"] = radar[index + 1]["upgrade_require"]

    out = {
        "_source": "soldiers, build321, world_map_monster, intel_config, intel_radar_config, systemconfig.world_march (extract_world.py)",
        "lighthouse": LIGHTHOUSE,
        "attackStamina": system["world_march"]["atk_monster_cost"],
        "staminaRegenSeconds": STAMINA_REGEN_SECONDS,
        "staminaCap": STAMINA_CAP,
        "soldiers": out_soldiers,
        "camps": camps,
        "beasts": beasts,
        "intel": intel,
        "searchlight": searchlight,
    }
    os.makedirs(os.path.dirname(DST), exist_ok=True)
    with open(DST, "w", encoding="utf-8", newline="\n") as f:
        json.dump(out, f, indent=1)
        f.write("\n")
    print("wrote", DST)
    print(json.dumps(out)[:1500])


if __name__ == "__main__":
    main()
