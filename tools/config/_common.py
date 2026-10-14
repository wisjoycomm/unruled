"""Shared config extraction helpers (LuaJSON loader, i18n lookup, JSON writer)."""
import json
import os
from pathlib import Path
import sys

_CONFIG_DIR = Path(__file__).resolve().parent
_TOOLS_DIR = _CONFIG_DIR.parent
for _p in (str(_CONFIG_DIR), str(_TOOLS_DIR)):
    if _p not in sys.path:
        sys.path.insert(0, _p)

import project_cfg  # noqa: E402

ROOT = project_cfg.ROOT
LUA_CONFIG_DIR = project_cfg.LUA_CONFIG_DIR
RESOURCES_CONFIG_DIR = project_cfg.RESOURCES_CONFIG_DIR
DATA_DIR = project_cfg.DATA_DIR
EXPORTED_ASSETS_DIR = project_cfg.EXPORTED_ASSETS_DIR

_I18N_CACHE = {}


def load_lua_json(name: str):
    """Load a table from LUA_CONFIG_DIR by short name ('hero_config') or full filename."""
    filename = name if name.endswith(".json") else f"game_config_default_{name}.json"
    path = LUA_CONFIG_DIR / filename
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)


def load_i18n(lang: str = "en") -> dict:
    """Load and cache the extracted localization dictionary (e.g. Config/i18n/en.json)."""
    if lang not in _I18N_CACHE:
        path = RESOURCES_CONFIG_DIR / "i18n" / f"{lang}.json"
        with open(path, "r", encoding="utf-8") as f:
            _I18N_CACHE[lang] = json.load(f)
    return _I18N_CACHE[lang]


def i18n_text(key: str, lang: str = "en", strict: bool = True) -> str:
    """Resolve a localization key (including 'key||arg' -> '%<1>' pack substitutions)."""
    if not key:
        return ""
    table = load_i18n(lang)
    base, _, arg = key.partition("||")
    value = table.get(base)
    if value is None:
        if strict:
            raise KeyError(f"Missing localization key: {base}")
        return key
    return value.replace("%<1>", arg) if arg else value


def write_json(path, data, indent: int = 1, ensure_ascii: bool = False):
    """Write JSON to disk with parent directory creation and trailing newline."""
    target = Path(path)
    target.parent.mkdir(parents=True, exist_ok=True)
    with open(target, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=indent, ensure_ascii=ensure_ascii)
        f.write("\n")
    return target
