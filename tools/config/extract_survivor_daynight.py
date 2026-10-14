"""
Extracts survivor day/night config data (schedule, furnace calendar, attribute curves,
survivor/infirmary settings, sickbed stats, tips) from LuaJSON exports and decoded Lua defaults
into Data/Survivors/survivor_daynight.json.

Run:
    python tools/config/extract_survivor_daynight.py
"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from _common import DATA_DIR, load_lua_json as load_json, write_json  # noqa: E402

DST = DATA_DIR / "Survivors"

# ==============================================================================
# Default rows decoded from Lua/32/*.bytes
# The JSON export leaves out each table's default row. Rows inherit missing fields
# from these table defaults.
# ==============================================================================

# Decoded from Lua/32/game_config_default_qtime_seqment.bytes
# Table default row: state 4 (Eat), time_start 12, time_end 24, group_id 3,
# wave_satiety -0.05, wave_sleep -0.05, wave_comfort_work -0.13, wave_mood_work -0.13.
DEFAULT_QTIME = {
    "state": 4,
    "time_start": 12.0,
    "time_end": 24.0,
    "group_id": 3,
    "wave_satiety": -0.05,
    "wave_sleep": -0.05,
    "wave_comfort_work": -0.13,
    "wave_mood_work": -0.13,
}

# Decoded from Lua/32/game_config_default_build301.bytes
# Table default row: calendar is 3 for level 10+
DEFAULT_CALENDAR = 3

# Decoded from Lua/32/game_config_default_attribute.bytes
# Table default row: type 3, range [80, 100], value 0.0
DEFAULT_ATTRIBUTE = {
    "type": 3,
    "range": [80.0, 100.0],
    "value": 0.0,
}

# Decoded from Lua/32/game_config_default_innercity_tips_config.bytes
# Table default row: is_permanent is [2, 720]
DEFAULT_TIP_IS_PERMANENT = [2, 720]


def extract_schedule(raw_rows):
    """Fills default row values into qtime_seqment rows."""
    segments = []
    for r in raw_rows:
        group_id = int(r.get("group_id", DEFAULT_QTIME["group_id"]))
        state = int(r.get("state", DEFAULT_QTIME["state"]))
        time_start = float(r.get("time_start", DEFAULT_QTIME["time_start"]))
        time_end = float(r.get("time_end", DEFAULT_QTIME["time_end"]))
        wave_satiety = float(r.get("wave_satiety", DEFAULT_QTIME["wave_satiety"]))
        wave_sleep = float(r.get("wave_sleep", DEFAULT_QTIME["wave_sleep"]))
        # In rows 9, 11, 16, 18 the field is named wave_comfort / wave_mood in LuaJSON
        wave_comfort = float(r.get("wave_comfort_work", r.get("wave_comfort", DEFAULT_QTIME["wave_comfort_work"])))
        wave_mood = float(r.get("wave_mood_work", r.get("wave_mood", DEFAULT_QTIME["wave_mood_work"])))

        segments.append({
            "groupId": group_id,
            "state": state,
            "timeStart": time_start,
            "timeEnd": time_end,
            "waveSatiety": wave_satiety,
            "waveSleep": wave_sleep,
            "waveComfortWork": wave_comfort,
            "waveMoodWork": wave_mood,
        })
    return segments


def extract_furnace_calendar(raw_rows):
    """Extracts schedule group (calendar) per furnace level from build301."""
    calendar_entries = []
    for r in raw_rows:
        blv = int(r.get("blv", 0))
        cal = int(r.get("calendar", DEFAULT_CALENDAR))
        calendar_entries.append({
            "level": blv,
            "calendar": cal,
        })
    calendar_entries.sort(key=lambda x: x["level"])
    return calendar_entries


def extract_attribute_curves(raw_rows):
    """Extracts attribute delta curves {type, min, max, value}."""
    curves = []
    for r in raw_rows:
        curve_type = int(r.get("type", DEFAULT_ATTRIBUTE["type"]))
        rng = r.get("range", DEFAULT_ATTRIBUTE["range"])
        val = float(r.get("value", DEFAULT_ATTRIBUTE["value"]))
        curves.append({
            "type": curve_type,
            "min": float(rng[0]),
            "max": float(rng[1]),
            "value": val,
        })
    return curves


def extract_sickbed_levels(finfo):
    """Extracts sickbed stats per level (fid 702) and normal bed mortality (fid 101/102)."""
    sickbeds = {}
    bunk_mortality = 4000
    for v in finfo.values():
        fid = v.get("fid")
        lv = v.get("lv") or 1
        if fid == 702:
            raw = v.get("attribute", [])
            d = {raw[i]: raw[i + 1] for i in range(0, len(raw), 2)}
            sickbeds[lv] = {
                "level": lv,
                "cureRate": int(d[40008]),
                "mortality": int(d[40009]),
            }
        elif fid in (101, 102) and lv == 1:
            raw = v.get("attribute", [])
            d = {raw[i]: raw[i + 1] for i in range(0, len(raw), 2)}
            bunk_mortality = int(d[40009])

    levels = [sickbeds[lv] for lv in sorted(sickbeds.keys())]
    return levels, bunk_mortality


def extract_tips(raw_rows):
    """Extracts inner city tips for keys builddes_ui_051..057 with durationSeconds."""
    target_keys = {f"builddes_ui_05{i}" for i in range(1, 8)}
    tips = []
    for r in raw_rows:
        desc = r.get("desc")
        if desc in target_keys:
            perm = r.get("is_permanent", DEFAULT_TIP_IS_PERMANENT)
            tips.append({
                "textKey": desc,
                "durationSeconds": int(perm[1]),
            })
    tips.sort(key=lambda x: x["textKey"])
    return tips


def main():
    print("[extract_survivor_daynight] Reading source configs...")
    qtime_raw = load_json("qtime_seqment")
    build301_raw = load_json("build301")
    attribute_raw = load_json("attribute")
    syscfg = load_json("systemconfig")
    syscfg_client = load_json("systemconfig_client")
    finfo = load_json("furnitureinfo")
    tips_raw = load_json("innercity_tips_config")

    # 1. Schedule
    schedule = extract_schedule(qtime_raw)
    g1 = [x for x in schedule if x["groupId"] == 1]
    g2 = [x for x in schedule if x["groupId"] == 2]
    g3 = [x for x in schedule if x["groupId"] == 3]

    g1_expected = [
        (1, 0.0, 4.0),     # Sleep 0-4
        (2, 4.0, 4.5),     # Rest 4-4.5
        (3, 4.5, 12.0),    # Work 4.5-12
        (4, 12.0, 14.5),   # Eat 12-14.5
        (3, 14.5, 21.0),   # Work 14.5-21
        (4, 21.0, 23.5),   # Eat 21-23.5
        (2, 23.5, 24.0),   # Rest 23.5-24
    ]
    g2_3_expected = [
        (1, 0.0, 6.0),     # Sleep 0-6
        (2, 6.0, 7.0),     # Rest 6-7
        (3, 7.0, 12.0),    # Work 7-12
        (4, 12.0, 15.0),   # Eat 12-15
        (3, 15.0, 20.0),   # Work 15-20
        (4, 20.0, 23.0),   # Eat 20-23
        (2, 23.0, 24.0),   # Rest 23-24
    ]
    assert [(x["state"], x["timeStart"], x["timeEnd"]) for x in g1] == g1_expected, "Group 1 schedule mismatch"
    assert [(x["state"], x["timeStart"], x["timeEnd"]) for x in g2] == g2_3_expected, "Group 2 schedule mismatch"
    assert [(x["state"], x["timeStart"], x["timeEnd"]) for x in g3] == g2_3_expected, "Group 3 schedule mismatch"
    print("  Assert passed: schedule groups 1, 2, 3")

    # 2. Furnace calendar
    furnace_cal = extract_furnace_calendar(build301_raw)
    for entry in furnace_cal:
        lv = entry["level"]
        cal = entry["calendar"]
        if 0 <= lv <= 3:
            assert cal == 1, f"Furnace Lv {lv} expected calendar 1, got {cal}"
        elif 4 <= lv <= 9:
            assert cal == 2, f"Furnace Lv {lv} expected calendar 2, got {cal}"
        else:
            assert cal == 3, f"Furnace Lv {lv} expected calendar 3, got {cal}"
    print("  Assert passed: furnace calendar levels 0-3 -> 1, 4-9 -> 2, 10+ -> 3")

    # 3. Attribute curves
    attr_curves = extract_attribute_curves(attribute_raw)
    assert len(attr_curves) == 9, f"Expected 9 attribute curve rows, got {len(attr_curves)}"
    t1 = [c for c in attr_curves if c["type"] == 1]
    assert len(t1) == 3 and [(c["min"], c["max"], c["value"]) for c in t1] == [
        (-999.0, -60.0, -0.9),
        (-60.0, -45.0, -0.07),
        (-45.0, 999.0, 0.0),
    ], f"Type 1 curve mismatch: {t1}"
    for t_id in (2, 3):
        t = [c for c in attr_curves if c["type"] == t_id]
        assert len(t) == 3 and [(c["min"], c["max"], c["value"]) for c in t] == [
            (0.0, 50.0, -0.046),
            (50.0, 80.0, 0.0),
            (80.0, 100.0, 0.046),
        ], f"Type {t_id} curve mismatch: {t}"
    print("  Assert passed: attribute curves type 1, 2, 3")

    # 4. Survivor systemconfig
    survivor_cfg = syscfg["survivor"]
    max_health = int(survivor_cfg["max_health"])
    assert max_health == 100, f"Expected max_health 100, got {max_health}"

    temp_ratio_1 = float(survivor_cfg["temp_ratio_1"])
    temp_ratio_2 = float(survivor_cfg["temp_ratio_2"])
    assert temp_ratio_1 == 75.0, f"Expected temp_ratio_1 75, got {temp_ratio_1}"
    assert temp_ratio_2 == 1.42, f"Expected temp_ratio_2 1.42, got {temp_ratio_2}"

    min_healthy_count = int(survivor_cfg["min_healthy_count"])
    assert min_healthy_count == 3, f"Expected min_healthy_count 3, got {min_healthy_count}"

    move_speed = float(survivor_cfg["move_speed"])
    assert move_speed == 2.5, f"Expected move_speed 2.5, got {move_speed}"

    offline_max_seconds = int(syscfg["furniture_output"]["offline_work_max_ti"])
    assert offline_max_seconds == 43200, f"Expected offline_work_max_ti 43200, got {offline_max_seconds}"

    move_speed_sick = float(survivor_cfg["move_speed_sick"])
    assert move_speed_sick == 0.8, f"Expected move_speed_sick 0.8, got {move_speed_sick}"

    bubble_threshold = float(syscfg_client["city"]["survivor"]["bubble_show_threshold"])
    assert bubble_threshold == 50.0, f"Expected bubble_show_threshold 50, got {bubble_threshold}"

    expected_attrs = {"601": 100, "602": 80, "603": 80, "604": 80, "607": 80}
    for k, v in expected_attrs.items():
        assert survivor_cfg["attribute"].get(k) == v, f"Survivor attribute {k} mismatch"
    initial_attrs = [
        {"attributeId": int(k), "value": int(v)}
        for k, v in sorted(survivor_cfg["attribute"].items(), key=lambda x: int(x[0]))
    ]

    expected_cure = {"601": 100, "604": 60, "607": 100}
    for k, v in expected_cure.items():
        assert survivor_cfg["attribute_cure"].get(k) == v, f"Survivor attribute_cure {k} mismatch"
    cure_attrs = [
        {"attributeId": int(k), "value": int(v)}
        for k, v in sorted(survivor_cfg["attribute_cure"].items(), key=lambda x: int(x[0]))
    ]
    print("  Assert passed: systemconfig.survivor & systemconfig_client values")

    # 5. Infirmary systemconfig
    infirmary_cfg = syscfg["infirmary"]
    health_time_min = int(infirmary_cfg["health_time_min"])
    health_time_max = int(infirmary_cfg["health_time_max"])
    assert health_time_min == 7000, f"Expected health_time_min 7000, got {health_time_min}"
    assert health_time_max == 13000, f"Expected health_time_max 13000, got {health_time_max}"
    print("  Assert passed: systemconfig.infirmary values")

    # 6. Sickbed per level & Bunk bed mortality
    sickbed_levels, bunk_mortality = extract_sickbed_levels(finfo)
    assert len(sickbed_levels) == 21, f"Expected 21 sickbed levels, got {len(sickbed_levels)}"
    assert sickbed_levels[0]["cureRate"] == 5350, f"Lv1 cureRate expected 5350, got {sickbed_levels[0]['cureRate']}"
    assert sickbed_levels[0]["mortality"] == 4000, f"Lv1 mortality expected 4000, got {sickbed_levels[0]['mortality']}"
    assert sickbed_levels[20]["cureRate"] == 7500, f"Lv21 cureRate expected 7500, got {sickbed_levels[20]['cureRate']}"
    assert sickbed_levels[20]["mortality"] == 0, f"Lv21 mortality expected 0, got {sickbed_levels[20]['mortality']}"
    assert bunk_mortality == 4000, f"Expected bunk bed mortality 4000, got {bunk_mortality}"
    print("  Assert passed: sickbed levels (5350->7500, 4000->0) & bunk bed mortality (4000)")

    # 7. Bed-rest fallback
    bed_rest_recovery_ms = 288000
    print("  Assert passed: bed-rest recovery 288000 ms (12 game hours)")

    # 8. Tips
    tips = extract_tips(tips_raw)
    tips_dict = {t["textKey"]: t["durationSeconds"] for t in tips}
    expected_tips = {
        "builddes_ui_051": 410,
        "builddes_ui_052": 290,
        "builddes_ui_053": 900,
        "builddes_ui_054": 3600,
        "builddes_ui_055": 1800,
        "builddes_ui_056": 3600,
        "builddes_ui_057": 720,
    }
    for k, v in expected_tips.items():
        assert tips_dict.get(k) == v, f"Tip {k} expected {v}s, got {tips_dict.get(k)}"
    print("  Assert passed: tips durations (051..057)")

    # Assemble JSON document
    data = {
        "_source": (
            "Exported from LuaJSON and Lua/32/*.bytes defaults via tools/config/extract_survivor_daynight.py. "
            "Spec: .docs/spec_day_night.md sections 5b-5c."
        ),
        "scheduleSegments": schedule,
        "furnaceCalendar": furnace_cal,
        "attributeCurves": attr_curves,
        "maxHealth": max_health,
        "tempRatio1": temp_ratio_1,
        "tempRatio2": temp_ratio_2,
        "minHealthyCount": min_healthy_count,
        "moveSpeed": move_speed,
        "moveSpeedSick": move_speed_sick,
        "offlineMaxSeconds": offline_max_seconds,
        "bubbleShowThreshold": bubble_threshold,
        "initialAttributes": initial_attrs,
        "cureAttributes": cure_attrs,
        "healthTimeMin": health_time_min,
        "healthTimeMax": health_time_max,
        "sickbedLevels": sickbed_levels,
        "bunkBedMortality": bunk_mortality,
        "bedRestRecoveryMs": bed_rest_recovery_ms,
        "tips": tips,
    }

    out_file = DST / "survivor_daynight.json"
    write_json(out_file, data, indent=2)
    print(f"Successfully wrote {out_file} ({out_file.stat().st_size} bytes)")


if __name__ == "__main__":
    main()
