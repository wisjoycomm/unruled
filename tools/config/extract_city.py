"""
Extracts the chapter 1-3 city data from the original config export into the human-editable JSON the
Unity importers read (Data/...). Only values that exist in the
original config are written; names/descriptions come from the LD capture log (localization export
only has boot strings).

Run:  python tools/config/extract_city.py
"""
import math, os, re, sys

sys.stdout.reconfigure(encoding="utf-8")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _common import DATA_DIR, EXPORTED_ASSETS_DIR, load_lua_json as load, project_cfg, write_json  # noqa: E402

DST = str(DATA_DIR)
EXPORT_PREFABS = str(EXPORTED_ASSETS_DIR / "GameObject")
# Levels stop at the first one the remake cannot reach: a requirement on a building it does not have
# (Furnace Lv8 needs 321) or a fire-crystal step (pre_wish, cost item 100081). That is Lv7 for every building now.
RESOURCES = {101, 102, 103, 104, 105, 106, 107, 108}
# Furniture keeps at least the 12 levels earlier exports had, so saved furniture levels stay valid.
MIN_FURNITURE_LEVELS = 12

# Building id → (key, display name, description). Names/descriptions as seen on screen (LD log).
BUILDINGS = {
    301: ("furnace", "Furnace", "Survivors are getting cold. Increase your City's warmth!"),
    302: ("sawmill", "Sawmill", "Produces Wood. Upgrade to increase Wood Output."),
    303: ("coal_mine", "Coal Mine", "Produces Coal. Upgrade to increase Coal Output."),
    304: ("iron_mine", "Iron Mine", "Produces Iron. Upgrade to increase Iron Output."),
    305: ("hunters_hut", "Hunters' Hut", "Hunters can keep your meat pantry well-stocked. Upgrade to increase Meat Output."),
    306: ("cookhouse", "Cookhouse", "Provides fine food to satisfy survivors' appetite."),
    307: ("clinic", "Clinic", "A Survivor's answer to all manner of sickness and malady."),
    308: ("shelter_1", "Shelter 1", "A place for Survivors to sleep."),
    309: ("shelter_2", "Shelter 2", "A place for Survivors to sleep."),
    310: ("shelter_3", "Shelter 3", "A place for your weary Survivors to rest their heads."),
    311: ("shelter_4", "Shelter 4", "A place for your weary Survivors to rest their heads."),
    312: ("shelter_5", "Shelter 5", "A place for your weary Survivors to rest their heads."),
    313: ("shelter_6", "Shelter 6", "A place for your weary Survivors to rest their heads."),
    318: ("hero_hall", "Hero Hall", "A site of hero recruitment and activities."),
    319: ("chiefs_house", "Chief's House", "Publish Chief Orders to maintain order among the survivors."),
    321: ("infantry_camp", "Infantry Camp", "The Infantry Camp trains Infantry, as well as stronger classes of Infantry when upgraded."),
    328: ("lighthouse", "Lighthouse", "The icefields are home to countless unknown dangers. A lighthouse can help light the way forward."),
    332: ("barricade", "Barricade", "Protects the City. Upgrade the Barricade to enhance the durability of the defenses."),
}

# Buildings whose level rows name no model (the original swaps in up_modulename at runtime): the built model in Environment/.
MODEL_FALLBACK = {321: "city_building_infantry_quarters_01"}

# Config name key → (name, description) as captured on screen. Several slots share a key (2 bunk beds).
# Keys not seen on screen yet keep the raw key so they are easy to spot.
FURNITURE_TEXT = {
    "furniture_name_401": ("Workbench", "A table for simple woodworking."),
    "furniture_name_501": ("Sorting Station", "Facilitates the manual sorting of coal."),
    "furniture_name_301": ("Hunters' Station", "A shelf dedicated to hunting equipment."),
    "furniture_name_201": ("Stove", "A place to prepare and cook food."),
    "furniture_name_204": ("Sideboard", "Facilitate the orderly distribution of meals."),
    "furniture_name_205": ("Dining Table", "A dining table at which Survivors may eat."),
    "furniture_name_208": ("Larder", "A cabinet to store food and keep them fresh."),
    "furniture_name_101": ("Bunk Bed", "A place for Survivors to sleep."),
    "furniture_name_103": ("Desk", "A place for reading books and writing journals."),
    "furniture_name_104": ("Washbasin", "A clean receptacle for washing hands and face."),
    "furniture_name_105": ("Bookshelf", "A bookshelf for organizing books."),
    "furniture_name_701": ("Nursing Station", "A place for physicians to receive and treat the sick."),
    "furniture_name_702": ("Sickbed", "A bed for patients to rest and recover."),
    "furniture_name_601": ("Forging Bench", "A processing station for the refinement of iron ore."),
    "furniture_name_607": ("Mold", "A tool that casts iron ores into special shapes."),
}

# The Forging Bench slots (601-604) have no name, icon or attribute_show in the config; LD ch5 shows them as
# "Forging Bench" with Production [iron] 4.2 at Lv1 (= attribute 40013 / 10), like the other stations.
FORGING_BENCH = ("furniture_name_601", "furniture_pic_601", "2;195")
UNNAMED_STATIONS = {601: FORGING_BENCH, 602: FORGING_BENCH, 603: FORGING_BENCH, 604: FORGING_BENCH}


def pairs(v):
    return v if isinstance(v, list) else []


# attribute_show "code;icon|..." → which stat the panel shows. Values verified against captures:
# 40002 = time in ms (Stove 8s, Bunk Bed 60s, Workbench 5s); production ids are x10 (Workbench 220 → 22,
# Hunters' Station 6600 → 660, Sorting Station 168 → 16.8); comfort stats are raw (Bunk Bed 15, Larder 2.2).
PRODUCTION_ATTR = {192: 40010, 193: 40011, 194: 40012, 195: 40013}
STAT_ATTR = {601: 40003, 602: 40004, 603: 40005, 604: 40006}


# Furniture already standing when a building is first built: 034 Cookhouse shows Sideboard, Dining
# Table, Larder at Lv1 and the Stove unbuilt; 102 Shelter 1 Bunk Bed already Lv1, Desk unbuilt;
# 076/079 Sawmill Workbench already Lv1 (worker slot open right after build); 160 Coal Mine likewise.
# 082 Hunters' Station shows "Build", so it is not prebuilt.
PREBUILT = {302: [401], 303: [501], 304: [601], 306: [204, 205, 208], 307: [701, 702, 703], 308: [101], 309: [101],
            310: [101], 311: [101], 312: [101], 313: [101]}


def point_prefab_anchors(name):
    """Children of the original buildinfo child_point_prefab (e.g. city_building_kitchen): furniture
    anchors in building space → {name: (pos, rot quaternion, scale)}."""
    path = os.path.join(EXPORT_PREFABS, name + ".prefab")
    if not os.path.exists(path):
        return {}
    with open(path, encoding="utf-8") as f:
        docs = re.split(r"(?m)^--- ", f.read())
    names, transforms = {}, []
    for d in docs:
        m = re.match(r"!u!(\d+) &(-?\d+)", d)
        if not m:
            continue
        if m.group(1) == "1":
            names[m.group(2)] = re.search(r"m_Name: ?(.*)", d).group(1).strip()
        elif m.group(1) == "4":
            vec = lambda key: [float(v) for v in re.search(key + r": \{x: ([^,]*), y: ([^,]*), z: ([^,}]*)(?:, w: ([^}]*))?\}", d).groups() if v is not None]
            transforms.append((re.search(r"m_GameObject: \{fileID: (-?\d+)\}", d).group(1),
                               re.search(r"m_Father: \{fileID: (-?\d+)\}", d).group(1),
                               vec("m_LocalPosition"), vec("m_LocalRotation"), vec("m_LocalScale")))
    return {names.get(go, "?"): (p, r, sc) for go, father, p, r, sc in transforms if father != "0"}

def furniture_anchors(bid, building_info, road, points, furniture_rows):
    """fid → anchor in building space. The anchor is the point-prefab child named after the furniture
    model (hearth_1..3, bunk_bed_1..2, ...); between same-named ones (or when no name matches, e.g. the
    Hunters' Station tools_shelf on hunting_tool_N) the one nearest the fid's road point wins."""
    info = building_info.get(bid)
    if not info or not info.get("child_point_prefab"):
        return []
    anchors = point_prefab_anchors(info["child_point_prefab"])
    origin = info.get("coordinates", [0, 0, 0])
    out = []
    for fid_text, point_ids in points.get(str(bid), {}).items():
        fid = int(fid_text)
        rows = furniture_rows.get(fid)
        point = road.get(str(point_ids[0])) if point_ids else None
        if not rows or not anchors or point is None:
            continue
        base = re.sub(r"^city_inter_fur_|_\d+$", "", rows[0].get("furniture_module", ""))
        local = (point["x"] - origin[0], point["z"] - origin[2])
        named = [n for n in anchors if n == base or re.fullmatch(re.escape(base) + r"_\d+", n)]
        best = min(named or anchors, key=lambda n: math.hypot(anchors[n][0][0] - local[0], anchors[n][0][2] - local[1]))
        pos, rot, scale = anchors[best]
        out.append({"fid": fid, "anchor": best, "position": pos, "rotation": rot, "scale": scale})
    return sorted(out, key=lambda a: a["fid"])

def level_row(r, show=None):
    attrs = pairs(r.get("attribute"))
    attr = {attrs[i]: attrs[i + 1] for i in range(0, len(attrs) - 1, 2)}
    out = {"level": r.get("lv", 1), "cost": pairs(r.get("costRes")), "exp": r.get("exp", 0),
           "seconds": attr.get(40002, 0) / 1000.0, "statLabel": "", "statIcon": 0, "statValue": 0,
           "model": r.get("furniture_module", "")}
    for part in (r.get("attribute_show") or show or "").split("|"):
        if ";" not in part:
            continue
        code, icon = part.split(";")
        icon = int(icon)
        if code in ("1", "2") and icon in PRODUCTION_ATTR:
            out.update(statLabel="Production", statIcon=icon, statValue=attr.get(PRODUCTION_ATTR[icon], 0) / 10.0)
        elif code == "8" and icon in STAT_ATTR:
            out.update(statLabel="Stats", statIcon=icon, statValue=attr.get(STAT_ATTR[icon], 0))
        if code in ("4", "5"):
            out['healingRate'] = attr.get(40008, 0) / 100.0
        if code == "8" and icon == 607:
            out.update(statLabel="Stats", statIcon=icon, statValue=attr.get(40007, 0))
    return out


# Cooking stations (attribute_show codes 3/7/9 = Production / Consumption / Stats) show the kitchen_menu
# default dish instead of a stat row (capture 035: Production [dish] 1, Consumption [meat] have/50,
# Stats [stomach] 40, Time 8s). One portion per cook as on screen; the menu row has no count.
COOK_CODES = {"3", "7", "9"}

def station_menu(row, kitchen_menu):
    codes = {p.split(";")[0] for p in (row.get("attribute_show") or "").split("|") if ";" in p}
    dish = next((m for m in kitchen_menu if m.get("default")), None)
    if not COOK_CODES <= codes or dish is None:
        return None
    satiety = pairs(dish.get("satiety"))
    return {"dish": dish.get("small_pic", ""), "portions": 1, "cost": pairs(dish.get("cost")),
            "statIcon": satiety[0], "stat": satiety[1]}

def reachable_levels(building_rows):
    """bid -> highest level whose requirements all name a building in BUILDINGS at a reachable level and that
    has no fire-crystal step (pre_wish, or a cost outside the resources). Repeats until no building drops."""
    reach = {bid: len(rows) for bid, rows in building_rows.items()}
    changed = True
    while changed:
        changed = False
        for bid, rows in building_rows.items():
            top = 0
            for row in rows:
                pre = pairs(row.get("preCond"))
                cost = pairs(row.get("costRes"))
                if (row.get("pre_wish") or any(c not in RESOURCES for c in cost[::2])
                        or any(pre[i] not in reach or pre[i + 1] > reach[pre[i]] for i in range(0, len(pre), 2))):
                    break
                top = row["blv"]
            if top < reach[bid]:
                reach[bid] = top
                changed = True
    return reach


def furniture_levels(rows, show):
    """Level rows for the panel; a row without a furniture_module keeps the model of the level below."""
    out = []
    model = ""
    for r in rows:
        row = level_row(r, show)
        model = row["model"] or model
        row["model"] = model
        out.append(row)
    return out


def main():
    furniture_rows = load("game_config_default_furnitureinfo.json")
    by_fid = {}
    for row in furniture_rows.values():
        fid = row.get("fid")
        if fid is None:
            continue
        by_fid.setdefault(fid, []).append(row)
    for rows in by_fid.values():
        rows.sort(key=lambda r: r.get("lv", 1))

    building_info = {row["bid"]: row for row in load("game_config_default_buildinfo.json") if "bid" in row}
    road = load("game_config_custom_city_map_road.json")
    points = load("game_config_custom_city_building_data.json")["furnitures"]

    building_rows = {bid: sorted((r for r in load("game_config_default_build%d.json" % bid) if r.get("blv", 0) >= 1),
                                 key=lambda r: r["blv"]) for bid in BUILDINGS}
    reach = reachable_levels(building_rows)

    buildings = []
    slot_caps = {}
    for bid, (key, name, desc) in BUILDINGS.items():
        levels = []
        model = MODEL_FALLBACK.get(bid, "")
        for row in building_rows[bid][:reach[bid]]:
            lv = row["blv"]
            furn = pairs(row.get("furnitureinfo"))
            slots = [{"fid": furn[i], "maxLevel": furn[i + 1]} for i in range(0, len(furn), 2)]
            for slot in slots:
                slot_caps[slot["fid"]] = max(slot_caps.get(slot["fid"], 0), slot["maxLevel"])
            # Sparse rows keep the model of the level below (Cookhouse Lv6 has no modulename).
            model = row.get("modulename") or model
            pre = pairs(row.get("preCond"))
            levels.append({
                "level": lv,
                "cost": pairs(row.get("costRes")),
                "seconds": row.get("costTime", 0),
                "power": row.get("power", 0),
                "needExp": row.get("need_exp", 0),
                "requires": [{"building": pre[i], "level": pre[i + 1]} for i in range(0, len(pre), 2)],
                "unlocks": pairs(row.get("unlock_build")),
                "furniture": slots,
                "model": model,
                "fuel": pairs(row.get("fuel")),
                "warm": row.get("warm", 0),
            })
        buildings.append({"id": bid, "key": key, "name": name, "description": desc, "levels": levels,
                          "prebuilt": PREBUILT.get(bid, []),
                          "anchors": furniture_anchors(bid, building_info, road, points, by_fid)})

    # A building listed in a Furnace level's unlock_build needs that Furnace level to be built
    # (capture: Coal Mine panel "Requires Furnace Level 3"; config row has no preCond).
    furnace = next(b for b in buildings if b["id"] == 301)
    for flv in furnace["levels"]:
        for unlocked in flv["unlocks"]:
            for b in buildings:
                if b["id"] == unlocked and b["levels"] and not b["levels"][0]["requires"]:
                    b["levels"][0]["requires"] = [{"building": 301, "level": flv["level"]}]

    kitchen_menu = load("game_config_default_kitchen_menu.json")
    furniture = []
    for fid in sorted(slot_caps):
        rows = by_fid.get(fid, [])
        name_key, icon, show = UNNAMED_STATIONS.get(fid, (None, None, None))
        name_key = name_key or (rows[0].get("furniture_name", str(fid)) if rows else str(fid))
        name, desc = FURNITURE_TEXT.get(name_key, (name_key, ""))
        entry = {
            "fid": fid,
            "nameKey": name_key,
            "name": name,
            "description": desc,
            "icon": icon or (rows[0].get("furniture_icon", "") if rows else ""),
            "isStation": bool(rows[0].get("isstation")) if rows else False,
            "type": rows[0].get("type", 0) if rows else 0,
            # Beds (type 1) sleep `waypoints` survivors: 2 per Bunk Bed → HUD capacity 4 → 6 → 8 in the captures.
            "beds": rows[0].get("waypoints", 0) if rows and rows[0].get("type") == 1 else 0,
            "levels": furniture_levels(rows[:max(MIN_FURNITURE_LEVELS, slot_caps[fid])], show),
        }
        menu = station_menu(rows[0], kitchen_menu) if rows else None
        if menu:
            entry["menu"] = menu
        furniture.append(entry)

    out = {
        "_source": f"{project_cfg.CFG.get('LUA_CONFIG_DIR', 'Config/LuaJSON')} build3xx + furnitureinfo (extract_city.py)",
        "buildings": buildings,
        "furniture": furniture,
    }
    path = os.path.join(DST, "Buildings", "city_buildings_ch1-3.json")
    write_json(path, out)
    print("wrote", path, len(buildings), "buildings,", len(furniture), "furniture types")


if __name__ == "__main__":
    main()
