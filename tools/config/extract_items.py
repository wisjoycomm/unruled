"""
Extracts the non-resource items granted by chapter 1-5 rewards (item_config + en.json text)
into Data/Items/items.json.

Ids that the client already keeps as ResourceType wallet entries (keys 600001/600002 and the
Bahiti shard 500006, owned by the hero code) are not exported here, so a reward has exactly one owner.

Run:
    python tools/config/extract_items.py
"""
import json
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import project_cfg  # noqa: E402

ROOT = project_cfg.ROOT
SRC = project_cfg.LUA_CONFIG_DIR
I18N = project_cfg.RESOURCES_CONFIG_DIR / "i18n" / "en.json"
DST = project_cfg.DATA_DIR / "Items" / "items.json"

# Items met in chapter 1-5 mission and exploration stage rewards (task 006 research, 2026-10-09).
ITEM_IDS = [
    200201,  # 1m Construction Speedup
    500003,  # Cloris Shard
    500200,  # Mythic Exploration Manual
    500201,  # Epic Exploration Manual
    500202,  # Rare Exploration Manual
    100021,  # 100 Wood pack
    100031,  # 100 Coal pack
    # Chapter 6-7 mission rewards and exploration stages 11-50.
    100041,  # 100 Iron pack
    200301,  # 1m Training Speedup
    500000,  # Eugene Shard
    500001,  # Smith Shard
    500002,  # Charlie Shard
    500004,  # Sergey Shard
    500222,  # Rare General Hero Shard
    610002,  # Chief Stamina
    600004,  # Explosive Arrowheads (Lighthouse Intel rewards)
]

# systemconfig.speed_up_give_change kind for the construction queue (item_desc_200201).
CONSTRUCTION_SPEEDUP_KIND = "3"


def load(name):
    with open(SRC / f"game_config_default_{name}.json", encoding="utf-8") as f:
        return json.load(f)


def text(en, key):
    """Resolves 'key||arg' (packs) by substituting %<1> with arg."""
    if not key:
        return ""
    base, _, arg = key.partition("||")
    value = en.get(base)
    if value is None:
        raise KeyError(f"Missing i18n key {base}")
    return value.replace("%<1>", arg) if arg else value


def main():
    items = load("item_config")
    system = load("systemconfig")
    with open(I18N, encoding="utf-8") as f:
        en = json.load(f)

    construction_item = system["speed_up_give_change"][CONSTRUCTION_SPEEDUP_KIND]
    # systemconfig.init_item: flat [id, amount, ...] the original grants a new player (resources included;
    # only the item ids exported here are kept, the client's starting resources live in GameSession.Settings).
    init = system["init_item"]
    start_amounts = {init[i]: init[i + 1] for i in range(0, len(init) - 1, 2)}
    rows = []
    for item_id in ITEM_IDS:
        src = items.get(str(item_id))
        if src is None:
            raise KeyError(f"item_config has no row {item_id}")
        use_type = src.get("use_type", 0)
        condition = src.get("result_condition", [])
        row = {
            "id": item_id,
            # Display text for reference only; the client reads it from localization, not the config bundle.
            "_name": text(en, src.get("name")),
            "quality": src.get("quality", 1),
            "useType": use_type,
            # use_type 3: speedup seconds; 0 otherwise.
            "speedupSeconds": condition[0] if use_type == 3 and condition else 0,
            # use_type 1: resource pack [resourceId, amount].
            "packResource": condition[0] if use_type == 1 and len(condition) == 2 else 0,
            "packAmount": condition[1] if use_type == 1 and len(condition) == 2 else 0,
            "constructionSpeedup": item_id == construction_item,
            "startAmount": start_amounts.get(item_id, 0),
        }
        rows.append(row)

    out = {
        "_source": f"{project_cfg.CFG.get('LUA_CONFIG_DIR', 'Config/LuaJSON')} item_config + systemconfig.speed_up_give_change/init_item, i18n/en.json",
        "items": rows,
    }
    DST.parent.mkdir(parents=True, exist_ok=True)
    with open(DST, "w", encoding="utf-8", newline="\n") as f:
        json.dump(out, f, ensure_ascii=False, indent=1)
        f.write("\n")
    print(f"Wrote {len(rows)} items to {DST}")


if __name__ == "__main__":
    main()
