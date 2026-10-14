"""Print the hero unlock table: who is recruitable, from which banner, and at what cost.

Everything is derived from the original configs rather than hand-written:
  * Data/Heroes/heroes.json                     - hero list (id/name/quality/class/shards)
  * game_config_default_hero_search_client.json - the two recruit banners
  * game_config_default_hero_search_config.json - banner rotation -> drop pool ids
  * game_config_default_drop_conf.json          - drop pool contents + weights
  * ResourcesExport/Config/i18n/en.json         - banner display names

The original game has exactly two recruit banners, Advanced and Epic ("Recruit
Hero"); there is no Drill Camp entry in hero_search_client. Each banner rotates
its drop pool as the server ages (hero_search_config.battle_day_count), so a hero
is reported with the earliest server day on which that banner can produce them.

Usage:
  python tools/config/hero_unlock_table.py [--md] [--day N]
"""
import json
import os
import sys

sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))
import project_cfg  # noqa: E402

ROOT = str(project_cfg.ROOT)
CFG = str(project_cfg.LUA_CONFIG_DIR)
HEROES = str(project_cfg.DATA_DIR / "Heroes" / "heroes.json")
I18N = str(project_cfg.RESOURCES_CONFIG_DIR / "i18n" / "en.json")

# hero_config.quality, matching HeroDefinition.QualityLabel: 3 R, 4 SR, >=5 SSR.
QUALITY = {3: "R", 4: "SR", 5: "SSR"}
CLASS = {1: "Infantry", 2: "Lancer", 3: "Marksman"}
# hero_search_config.type -> hero_search_client.order
BANNER_TYPE = {1: 1, 2: 2}


def jload(path):
    with open(path, encoding="utf-8") as fh:
        return json.load(fh)


def cfg(name):
    return jload(os.path.join(CFG, f"game_config_default_{name}.json"))


def quality_label(q):
    return QUALITY.get(q, "SSR" if q and q >= 5 else f"?{q}")


def parse_rates(entry):
    """drop_conf.rates is a flat [itemId, count, weight, ...] triple stream."""
    out = {}
    for i in range(0, len(entry.get("rates") or []) - 2, 3):
        rates = entry["rates"]
        out[rates[i]] = out.get(rates[i], 0) + rates[i + 2]
    return out


def main():
    day_filter = None
    if "--day" in sys.argv:
        day_filter = int(sys.argv[sys.argv.index("--day") + 1])

    data = jload(HEROES)
    heroes = data["heroes"]
    drops = cfg("drop_conf")
    client = cfg("hero_search_client")
    search = cfg("hero_search_config")
    i18n = jload(I18N) if os.path.exists(I18N) else {}

    names = {b.get("order"): i18n.get(b.get("name"), b.get("name")) for b in client}

    # Every (banner, from-day, pool) the recruit screen can show.
    banners = []
    for row in search:
        order = BANNER_TYPE.get(row.get("type"))
        if order is None:
            continue  # the third row per bucket is the shared/paid pool, not a banner
        pool = parse_rates(drops.get(str(row["dropid"]), {}))
        if not pool:
            continue
        # The last rotation row carries no battle_day_count (it is the open-ended
        # tail). Defaulting it to 0 would make it look like the *earliest* bucket
        # and shadow the real day-1 pool, so push it to the end instead.
        day = row.get("battle_day_count")
        banners.append({
            "name": names.get(order, f"banner {order}"),
            "day": day if day is not None else float("inf"),
            "pool": pool,
            "total": sum(pool.values()),
        })

    rows = []
    for h in sorted(heroes, key=lambda x: (x.get("quality", 0), x.get("order", 0))):
        hid = h["id"]
        shard_id = 500000 + (hid - 50000)

        # Best (earliest, then highest rate) appearance per banner name.
        best = {}
        for b in banners:
            if day_filter is not None and b["day"] > day_filter:
                continue
            w, ws = b["pool"].get(hid, 0), b["pool"].get(shard_id, 0)
            if not (w or ws):
                continue
            cur = best.get(b["name"])
            if cur is None or b["day"] < cur["day"]:
                best[b["name"]] = {
                    "day": b["day"],
                    "hero": 100.0 * w / b["total"],
                    "shard": 100.0 * ws / b["total"],
                }

        sources = []
        for name in sorted(best, key=lambda n: best[n]["day"]):
            e = best[name]
            bits = []
            if e["hero"]:
                bits.append(f"hero {e['hero']:.2f}%")
            if e["shard"]:
                bits.append(f"shard {e['shard']:.2f}%")
            if e["day"] == float("inf"):
                day = "final rotation"
            elif e["day"] <= 1:
                day = "day 1"
            else:
                day = f"server day {e['day']}+"
            sources.append(f"{name} from {day} ({', '.join(bits)})")

        rows.append({
            "id": hid,
            "name": h.get("name", "?"),
            "quality": quality_label(h.get("quality")),
            "cls": h.get("className") or CLASS.get(h.get("leaderType", 0), "?"),
            "troop": h.get("troopColumn", "?"),
            "shards": h.get("recruitShards", 0),
            "sources": sources or ["not obtainable from Recruit Hero (event / shop only)"],
            "skills": len(h.get("skills") or []),
            "stories": len(h.get("stories") or []),
            "pv": bool(h.get("promotionVideo")),
        })

    if "--md" in sys.argv:
        print("| ID | Hero | Rarity | Class | Troop | Shards | Unlocked from | Skills | Stories | PV |")
        print("|---|---|---|---|---|---|---|---|---|---|")
        for r in rows:
            print(f"| {r['id']} | {r['name']} | {r['quality']} | {r['cls']} | {r['troop']} | {r['shards']} | "
                  f"{'<br>'.join(r['sources'])} | {r['skills']} | {r['stories']} | {'yes' if r['pv'] else '-'} |")
    else:
        print(f"Recruit banners: {', '.join(str(v) for v in names.values())}"
              f"   (no Drill Camp in hero_search_client)")
        if day_filter is not None:
            print(f"Filtered to server day <= {day_filter}")
        print()
        hdr = (f"{'ID':<7}{'Hero':<13}{'Rar':<5}{'Class':<10}{'Troop':<9}"
               f"{'Shards':<8}{'Sk':<4}{'St':<4}{'PV':<5}Unlocked from")
        print(hdr)
        print("-" * 118)
        for r in rows:
            head = (f"{r['id']:<7}{r['name']:<13}{r['quality']:<5}{r['cls']:<10}{r['troop']:<9}"
                    f"{r['shards']:<8}{r['skills']:<4}{r['stories']:<4}"
                    f"{('yes' if r['pv'] else '-'):<5}")
            print(head + r["sources"][0])
            for extra in r["sources"][1:]:
                print(" " * len(head) + extra)

    print(f"\n{len(rows)} heroes, {len(banners)} banner rotations")


if __name__ == "__main__":
    main()
