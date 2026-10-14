#!/usr/bin/env python3
"""
Regenerates Data/Heroes/heroes.json from the original LuaJSON config:
  - game_config_default_hero_config.json  (roster, quality, model/codename, skill slot ids, pv video)
  - game_config_default_hero_level.json   (80-level stat curve + Hero XP cost)
  - game_config_default_hero_star.json    (31 star tiers + debris/shard cost)
  - game_config_default_skill_config.json (per-skill value arrays)
  - game_config_default_hero_story.json   (4 story entries per hero + their star gates)
  - game_config_default_hero_skill_upgrade.json (skill power by hero quality + skill level)

All display text (hero names, skill names/descriptions, story prose, UI strings) is resolved
through the game's own English localization table, recovered by tools/config/extract_i18n.py
into ${RESOURCES_CONFIG_DIR}/i18n/en.json. Nothing here is invented: if a key is missing the
build fails loudly rather than substituting placeholder text.

Run: python tools/config/extract_i18n.py && python tools/config/extract_heroes.py
"""
import math
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _common import DATA_DIR, i18n_text as i18n, load_lua_json, project_cfg, write_json  # noqa: E402

OUT = str(DATA_DIR / "Heroes" / "heroes.json")

# The 17 released heroes, in roster/quality order: 4 R (quality 3) + 9 SR (quality 4) + 4 SSR (quality 5).
# SSR (quality 5) heroes omit the "quality" field in hero_config (it is implied by the 5xx order block);
# the four generalist/base SSR are beastmaster/nicolas/molly/builder (orders 500-503).
BASE_IDS = ["50000", "50001", "50002", "50003", "50004", "50005", "50006",
            "50007", "50008", "50027", "50028", "50029", "50037",
            "50009", "50011", "50012", "50017"]

# Every fixed UI string the Hero screens render, exported alongside the roster so the Unity side
# never hard-codes English. Verified one-by-one against the live screens in LDPlayer.
UI_KEYS = [
    "hero_ui_01",   # "Heroes" (roster title)
    "hero_ui_02",   # "Not recruited" (locked card)
    "hero_ui_04",   # "Level"
    "hero_ui_05",   # "Stat Details" (BtnDetail tip title)
    "hero_ui_06", "hero_ui_07", "hero_ui_08", "hero_ui_09",   # Attack / Defense / Health / Command
    "hero_ui_14",   # "Hero Class: %{leader_type_name}"
    "hero_ui_19",   # "Hero Power"
    "hero_ui_23",   # "To unlock: ascend to %{star_level}-Star" (locked skill)
    "hero_ui_24",   # "Upgrade Preview"
    "hero_ui_25", "hero_ui_26", "hero_ui_27",                 # Escort / Escorts / Troops Capacity
    "hero_ui_32",   # "Level maxed!"
    "hero_ui_33",   # "To ascend: reach Hero Lv. %{level}"
    "hero_ui_34",   # "Needs %{num} Hero Shard(s) to ascend"
    "hero_ui_36",   # "Redeemed"
    "hero_ui_37", "hero_ui_38",                               # Exploration / Expedition bonus headers
    "hero_ui_39",   # "Unlock skill to activate"
    "hero_ui_42",   # "Ascension available! Upgrade hero first"
    "hero_ui_43",   # "Insufficient %{item_name}"
    "hero_ui_49",   # "To upgrade: ascend to %{num}-Star"
    "hero_ui_51",   # "Upgrade Skill"
    "hero_ui_52",   # "%{n1}-Star (Tier %{n2})"
    "hero_btn_01", "hero_btn_02", "hero_btn_03", "hero_btn_04",   # Recruit Hero / Stats / Skills / Recruit
    "hero_btn_05", "hero_btn_06", "hero_btn_07", "hero_btn_08",   # Upgrade / Bonus Details / Ascend / Redeem
    "hero_story_ui_01",   # "Ascend Hero to %{num}-Star to unlock story"
    "soldier_type_01", "soldier_type_02", "soldier_type_03",      # Infantry / Lancer / Marksman
]


# The source writes values as %{n1} / %{n2} wrapped in <color=#00f183>; TMP understands the colour
# tag as-is, so only the placeholders are rewritten into a .NET composite format string.
_PLACEHOLDER = re.compile(r"%\{n(\d+)\}")


def to_template(text):
    """Rewrite one localized skill description into a .NET composite format string."""
    return _PLACEHOLDER.sub(lambda m: "{" + str(int(m.group(1)) - 1) + "}", text)


# hero_config.leader_type -> hero class. The config omits leader_type for Marksman heroes
# (the default), so a missing value means 3. Confirmed in LD: Smith (leader_type 1) shows
# "Hero Class: Infantry", Charlie (2) "Lancer", Bahiti (absent) "Marksman".
LEADER_COLUMN = {1: "shields", 2: "bikers", 3: "snipers"}
LEADER_KEY = {1: "soldier_type_01", 2: "soldier_type_02", 3: "soldier_type_03"}


def leader_type(cfg):
    return int(cfg.get("leader_type", 3))

# hero_config keeps a default row (the Lua table's fallback for absent keys); the JSON export drops it.
# Read from Lua/64/game_config_default_hero_config.bytes. The SR rows omit every ratio, so
# they take 80 here, which LD confirms: Bahiti Lv1 = 5,000 x 80% + 10,000 x 80% + 2 x 720 skill = 13,440.
HERO_CONFIG_DEFAULT = {
    "uplv_atk_ratio": 80, "uplv_def_ratio": 100, "uplv_hp_ratio": 80, "uplv_power_ratio": 80,
    "uplv_soldier_atk_ratio": 80, "uplv_soldier_def_ratio": 100, "uplv_soldier_hp_ratio": 80,
    "star_atk_ratio": 80, "star_def_ratio": 100, "star_hp_ratio": 80, "star_power_ratio": 80,
    "star_soldier_atk_ratio": 80, "star_soldier_def_ratio": 100, "star_soldier_hp_ratio": 80,
}

def ratio(cfg, key):
    return int(cfg.get(key, HERO_CONFIG_DEFAULT[key]))

def exchange_cost(cfg):
    """hero_config.exchange_cost; the default row holds 500220 (SSR generic shard)."""
    return int(cfg.get("exchange_cost", 500220))

# hero_skill_upgrade default row (game_config_default_hero_skill_upgrade.bytes): the SSR Lv5 rows (515..565)
# omit power, which falls back to 16,920.
SKILL_UPGRADE_DEFAULT = {"hero_quallity": 5, "skill_num": 6, "skill_level": 5, "need_star": 12, "power": 16920}

def skill_power_curve(skill_upgrade):
    """Skill power by hero quality and skill level. Every skill slot of a quality shares the same numbers;
    the build fails if a slot ever disagrees."""
    table = {}
    for row in skill_upgrade.values():
        def g(key):
            return int(row.get(key, SKILL_UPGRADE_DEFAULT[key]))
        quality, level, power = g("hero_quallity"), g("skill_level"), g("power")
        levels = table.setdefault(quality, {})
        if levels.setdefault(level, power) != power:
            raise ValueError("skill power differs between slots for quality %d level %d" % (quality, level))
    curve = []
    for quality in sorted(table):
        levels = table[quality]
        if sorted(levels) != list(range(1, len(levels) + 1)):
            raise ValueError("skill levels have a gap for quality %d" % quality)
        curve.append({"quality": quality, "power": [levels[lv] for lv in sorted(levels)]})
    return curve


def troop_column(cfg):
    """hero_level/hero_star stat column: Infantry = shields, Lancer = bikers, Marksman = snipers."""
    return LEADER_COLUMN[leader_type(cfg)]


def split_values(raw, groups):
    """skill_config stores interleaved value groups as one flat array of 5*groups entries."""
    nums = [float(v) for v in raw]
    if groups <= 1:
        return nums, []
    half = len(nums) // 2
    return nums[:half], nums[half:]


def build_skills(cfg, skill_cfg, skill_unlock):
    """HeroSkillSubPanel: Skill1..3 (left arc) = skill_01..03 Exploration, Skill4..6 (right arc) =
    skill_04..06 Expedition. Unlock star per slot comes from hero_skill_unlock (slot 2/5 -> 6, 3/6 -> 12).
    skill_type 2 = growth (city/economy) skill -> hero_skill_icontype_02 badge, else combat -> _01."""
    slots = []
    for slot in range(1, 7):
        sid = cfg.get("skill_0%d" % slot)
        if sid is None:
            continue
        key = str(sid)
        entry = skill_cfg.get(key, {})
        raw = entry.get("value", []) or []
        value_names = entry.get("value_name", []) or []
        groups = 2 if len(raw) == 10 and len(value_names) >= 2 else 1
        values, values2 = split_values(raw, groups)
        unlock_star = int(skill_unlock.get(str(slot), {}).get("need_star_lv", 0))

        slots.append({
            "id": int(sid),
            "slot": slot,
            "name": i18n("hero_skill_name_" + key),
            "descriptionTemplate": to_template(i18n("hero_skill_desc_" + key)),
            "icon": entry.get("skill_icon", "hero_skill_icon_" + key),
            "skillType": int(entry.get("skill_type", 1)),
            "isExpedition": slot >= 4,
            "unlockStar": unlock_star,
            "values": values,
            "values2": values2,
        })
    return slots


def build_stories(hero_id, hero_story):
    """The 4 HeroStoryPopupPanel tabs (I..IV) for one hero.

    hero_story rows carry need_hero_star in *sub-tiers* (0 / 6 / 12); the popup shows that as whole
    stars ("Ascend Hero to 1-Star to unlock story" for 6), matching HeroStoryPopupView's
    math.floor(need_hero_star / 6). Story IV has no need_hero_star key at all and stays locked
    behind the hero's max tier, so it is reported as the last gate the config does define.
    """
    rows = [r for r in hero_story.values() if r.get("hero_id") == int(hero_id)]
    gated = [r for r in rows if "story_id" in r]
    if len(rows) != 4 or len(gated) != 3:
        raise ValueError("unexpected hero_story shape for %s: %d rows" % (hero_id, len(rows)))

    stories = []
    for index in range(1, 5):
        key = "hero_story_%s_%d" % (hero_id, index)
        row = next((r for r in rows if r.get("story_key") == key), None)
        if row is None:
            raise ValueError("missing story row " + key)
        stories.append({
            "index": index,
            "title": i18n("hero_story_title_%02d" % index),
            "body": i18n(key),
            # Sub-tier gate; HeroStoryPopup divides by 6 for the "n-Star" lock label.
            "needStar": int(row.get("need_hero_star", 0)),
            # Story IV ships no story_id/need_hero_star: it is the collection-complete entry and
            # carries no claimable chest.
            "hasReward": "story_id" in row,
        })
    return stories


def camera_pos(cfg, key):
    """hero_config.camera_position<n> as floats, or None when the row omits it."""
    raw = cfg.get(key)
    return [float(v) for v in raw] if raw and len(raw) == 3 else None


# HeroExhibitStage's own fallback, matching the recovered hero_exhibit_scene CameraHRoot.
STAGE_DEFAULT_CAM = [0.0, 1.3, -5.0]


def camera_rig(cfg):
    """The three CameraHRoot positions HeroDetailView's CameraPosFunMap tweens between.

    GetHeroAttrPos -> camera_position1 (Stats tab), GetHeroSkillPos -> camera_position2 (Skills
    tab), GetHeroEquipPos -> camera_position3, which OnResetAttrPos also uses while
    isDisplayStarView is set (the Ascend sheet slides the hero aside to free the left half for the
    Promotion Preview column).

    Only 4 of the released rows define 1 and 2; every row except Ling Shuang defines 3. The two
    centred positions are therefore derived from 3 rather than aliased onto it -- aliasing is what
    made every hero open off-centre. Across all four fully-defined rows (50002/50009/50012/50029)
    position 1 keeps position 3's framing with x zeroed, and position 2 pulls back exactly 0.5 and
    lifts ~0.05, so that is the derivation used when a row omits them.
    """
    # Ling Shuang (50037) is the one released row with no camera_position3 at all; fall back to the
    # stage default framing at the roster's most common aside-offset so the Ascend sheet still slides.
    cam3 = camera_pos(cfg, "camera_position3") or [0.4, STAGE_DEFAULT_CAM[1], STAGE_DEFAULT_CAM[2]]
    cam1 = camera_pos(cfg, "camera_position1") or [0.0, cam3[1], cam3[2]]
    cam2 = camera_pos(cfg, "camera_position2") or [0.0, round(cam1[1] + 0.05, 4), round(cam1[2] - 0.5, 4)]
    return cam1, cam2, cam3


def stat_row(row, prefix=""):
    def g(name):
        return int(row.get(prefix + name, 0))
    return {
        "atkShields": g("atk_shields"), "defShields": g("def_shields"), "hpShields": g("hp_shields"),
        "atkBikers": g("atk_bikers"), "defBikers": g("def_bikers"), "hpBikers": g("hp_bikers"),
        "atkSnipers": g("atk_snipers"), "defSnipers": g("def_snipers"), "hpSnipers": g("hp_snipers"),
    }


def level_rows(hero_level):
    rows = []
    for i, row in enumerate(hero_level):
        base = stat_row(row)
        pve = stat_row(row, "pve_troop_")
        cost = row.get("uplv_cost", [])
        xp = int(cost[1]) if isinstance(cost, list) and len(cost) >= 2 else 0
        rows.append({
            "level": i + 1, "power": int(row.get("power", 0)),
            "atkShields": base["atkShields"], "defShields": base["defShields"], "hpShields": base["hpShields"],
            "atkBikers": base["atkBikers"], "defBikers": base["defBikers"], "hpBikers": base["hpBikers"],
            "atkSnipers": base["atkSnipers"], "defSnipers": base["defSnipers"], "hpSnipers": base["hpSnipers"],
            "pveAtkShields": pve["atkShields"], "pveDefShields": pve["defShields"], "pveHpShields": pve["hpShields"],
            "pveAtkBikers": pve["atkBikers"], "pveDefBikers": pve["defBikers"], "pveHpBikers": pve["hpBikers"],
            "pveAtkSnipers": pve["atkSnipers"], "pveDefSnipers": pve["defSnipers"], "pveHpSnipers": pve["hpSnipers"],
            "pveTroopNum": int(row.get("pve_troop_num", 0)), "troopNum": int(row.get("troop_num", 0)),
            "upgradeCostHeroXp": xp,
        })
    return rows


def star_rows(hero_star):
    rows = []
    for star in sorted(hero_star.keys(), key=int):
        row = hero_star[star]
        base = stat_row(row)
        pve = stat_row(row, "pve_troop_")
        rows.append({
            "star": int(star), "power": int(row.get("power", 0)),
            "atkShields": base["atkShields"], "defShields": base["defShields"], "hpShields": base["hpShields"],
            "atkBikers": base["atkBikers"], "defBikers": base["defBikers"], "hpBikers": base["hpBikers"],
            "atkSnipers": base["atkSnipers"], "defSnipers": base["defSnipers"], "hpSnipers": base["hpSnipers"],
            "pveAtkShields": pve["atkShields"], "pveDefShields": pve["defShields"], "pveHpShields": pve["hpShields"],
            "pveAtkBikers": pve["atkBikers"], "pveDefBikers": pve["defBikers"], "pveHpBikers": pve["hpBikers"],
            "pveAtkSnipers": pve["atkSnipers"], "pveDefSnipers": pve["defSnipers"], "pveHpSnipers": pve["hpSnipers"],
            "debrisCost": int(row.get("debris_cost", 0)),
        })
    return rows


def main():
    hero_cfg = load_lua_json("hero_config")
    hero_level = load_lua_json("hero_level")
    hero_star = load_lua_json("hero_star")
    skill_cfg = load_lua_json("skill_config")
    skill_unlock = load_lua_json("hero_skill_unlock")
    hero_story = load_lua_json("hero_story")
    skill_upgrade = load_lua_json("hero_skill_upgrade")
    item_cfg = load_lua_json("item_config")

    heroes = []
    for hid in BASE_IDS:
        cfg = hero_cfg[hid]
        codename = cfg.get("model", "").replace("3d_hero_exhibit_", "")
        recruit = cfg.get("recruit_cost", []) or []
        leader = leader_type(cfg)
        cam_attr, cam_skill, cam_promotion = camera_rig(cfg)
        heroes.append({
            "id": int(hid),
            "name": i18n("hero_name_" + hid),
            "codename": codename,
            "quality": int(cfg.get("quality", 5)),
            "order": int(cfg.get("order", 0)),
            "troopColumn": troop_column(cfg),
            # hero_config.leader_type -> HeroInfoPopup "Hero Class: Infantry/Lancer/Marksman".
            "leaderType": leader,
            "className": i18n(LEADER_KEY[leader]),
            # hero_trait: absent/1 = Battle (hero_skill_icontype_01), 2 = Growth (_02)
            # -> HeroDetailPanel FeaturesSkillGroup banner (hero_ui_17 / hero_ui_18).
            "trait": int(cfg.get("hero_trait", 1)),
            "traitLabel": i18n("hero_ui_17" if int(cfg.get("hero_trait", 1)) == 1 else "hero_ui_18"),
            "pveTroopNum": int(hero_level[0].get("pve_troop_num", 0)),
            "avatar": cfg.get("avatar", "hero_headpic_" + hid),
            "bust": cfg.get("bustImg", "hero_bustpic_" + hid),
            "model": cfg.get("model", ""),
            # hero_config.pv: only the SSR heroes ship a promotion video (HeroInfoPopupView BtnPV).
            "promotionVideo": cfg.get("pv", ""),
            "recruitShards": int(recruit[1]) if len(recruit) >= 2 else 10,
            # HeroDetailView CameraPosFunMap: Stats tab -> camera_position1 (centred), Skills tab ->
            # camera_position2 (centred, pulled back), Ascend sheet -> camera_position3 (hero slid aside).
            "cameraAttr": cam_attr,
            "cameraSkill": cam_skill,
            "cameraPromotion": cam_promotion,
            # hero_config.*_ratio: the hero's share of the shared hero_level/hero_star curves, as a
            # percentage. Verified against LD (Cloris, Lv21, hero_star 10, snipers column): attack,
            # health and both escort attack/health are floor(part * ratio) summed per curve part,
            # while defense and escort defense take the raw curve value with no ratio applied.
            #   atk  floor(182*.65) + floor(401*.65) = 118 + 260 = 378  (LD 378)
            #   hp   floor(1125*.65) + floor(2475*.65) = 731 + 1608 = 2339  (LD 2,339)
            #   def  150 + 330 = 480 raw  (LD 480)
            # Power uses the same scheme: floor(levelPower * uplvPowerRatio%) + floor(starPower * starPowerRatio%)
            # (LD Cloris Lv12 star 0: 17,900 x 65% = 11,635 and 10,000 x 65% = 6,500).
            "uplvAtkRatio": ratio(cfg, "uplv_atk_ratio"),
            "uplvHpRatio": ratio(cfg, "uplv_hp_ratio"),
            "uplvPowerRatio": ratio(cfg, "uplv_power_ratio"),
            "uplvSoldierAtkRatio": ratio(cfg, "uplv_soldier_atk_ratio"),
            "uplvSoldierHpRatio": ratio(cfg, "uplv_soldier_hp_ratio"),
            "starAtkRatio": ratio(cfg, "star_atk_ratio"),
            "starHpRatio": ratio(cfg, "star_hp_ratio"),
            "starPowerRatio": ratio(cfg, "star_power_ratio"),
            "starSoldierAtkRatio": ratio(cfg, "star_soldier_atk_ratio"),
            "starSoldierHpRatio": ratio(cfg, "star_soldier_hp_ratio"),
            "smallTalk": [i18n(k) for k in (cfg.get("small_talk") or [])],
            "skills": build_skills(cfg, skill_cfg, skill_unlock),
            "stories": build_stories(hid, hero_story),
            # recruit_cost[0] is the hero's own shard item; exchange_cost the generic shard it redeems from
            # (0 = cannot redeem; absent = the default row's 500220).
            "shardItemId": int(recruit[0]),
            "shardIcon": item_cfg[str(recruit[0])]["icon"],
            "genericShardItemId": exchange_cost(cfg),
            "canRedeemGenericShards": exchange_cost(cfg) != 0,
        })

    out = {
        "_source": f"{project_cfg.CFG.get('LUA_CONFIG_DIR', 'Config/LuaJSON')} hero_config/hero_level/hero_star/skill_config/hero_story "
                   "(tools/config/extract_heroes.py). Every display string is resolved from the game's own "
                   "English localization table (tools/config/extract_i18n.py -> "
                   f"{project_cfg.CFG.get('RESOURCES_CONFIG_DIR', 'Config')}/i18n/en.json); no text is authored here.",
        "ui": {key: i18n(key) for key in UI_KEYS},
        "heroes": heroes,
        "levelCurve": level_rows(hero_level),
        "starCurve": star_rows(hero_star),
        # Hero Power popup "Skill Power": each unlocked skill slot adds power[skill level - 1] of the hero's quality.
        "skillPowerCurve": skill_power_curve(skill_upgrade),
    }

    write_json(OUT, out)
    print("[extract_heroes] {} heroes, {} levels, {} star tiers -> {}".format(
        len(heroes), len(out["levelCurve"]), len(out["starCurve"]), OUT))


if __name__ == "__main__":
    main()
