"""Shared project configuration loader for all scripts in `tools/`.

Reads key-value settings from `project.config` at the repository root,
overrides with `.env` (if present) and OS environment variables, and exposes
convenient `Path` and string constants so tools work with any game project.
"""

from __future__ import annotations

import os
from pathlib import Path

ROOT: Path = Path(__file__).resolve().parent.parent


def _parse_env_file(path: Path) -> dict[str, str]:
    values: dict[str, str] = {}
    if not path.exists():
        return values
    for raw_line in path.read_text(encoding="utf-8").splitlines():
        line = raw_line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, val = line.split("=", 1)
        key = key.strip()
        val = val.strip()
        if len(val) >= 2 and val[0] == val[-1] and val[0] in ("'", '"'):
            val = val[1:-1]
        values[key] = val
    return values


def load_cfg() -> dict[str, str]:
    cfg = _parse_env_file(ROOT / "project.config")
    cfg.update(_parse_env_file(ROOT / ".env"))
    for k in list(cfg.keys()):
        if k in os.environ:
            cfg[k] = os.environ[k]
    return cfg


CFG: dict[str, str] = load_cfg()

PROJECT_NAME: str = CFG.get("PROJECT_NAME", "GameProject")
PROJECT_SLUG: str = CFG.get("PROJECT_SLUG", PROJECT_NAME.lower())

UNITY_SUBDIR: str = CFG.get("UNITY_PROJECT_SUBDIR", ".")
UNITY_ROOT: Path = (ROOT / UNITY_SUBDIR).resolve()
ASSETS_ROOT: Path = UNITY_ROOT / "Assets"

GAME_ASSETS_SUBDIR: str = CFG.get("GAME_ASSETS_SUBDIR", f"_{PROJECT_NAME}")
GAME_ASSETS_ROOT: Path = ASSETS_ROOT / GAME_ASSETS_SUBDIR if GAME_ASSETS_SUBDIR not in ("", ".") else ASSETS_ROOT

RECOVERED_SUBDIR: str = CFG.get("RECOVERED_ASSETS_SUBDIR", "RecoveredAssets")
RECOVERED_ASSETS_ROOT: Path = ASSETS_ROOT / RECOVERED_SUBDIR

DATA_DIR: Path = UNITY_ROOT / CFG.get("DATA_DIR", "Assets/Data")
SCRIPTS_ROOT: Path = UNITY_ROOT / CFG.get("SCRIPTS_ROOT", "Assets/Scripts")
ART_DIR: Path = UNITY_ROOT / CFG.get("ART_DIR", "Assets/Art")
ATLAS_DIR: Path = ART_DIR / "UI" / "Atlas"
PREFABS_DIR: Path = UNITY_ROOT / CFG.get("PREFABS_DIR", "Assets/Prefabs")

DOCS_DIR: Path = UNITY_ROOT / CFG.get("DOCS_DIR", ".docs")
TEMP_DIR: Path = UNITY_ROOT / CFG.get("TEMP_DIR", ".temp")
LD_CAPTURES_DIR: Path = TEMP_DIR / "ld-captures"

LD_DIR: Path = Path(CFG.get("LD_DIR", r"C:\LDPlayer"))
ADB_PATH: str = CFG.get("ADB_PATH", str(LD_DIR / "adb.exe"))
LDCONSOLE_PATH: str = CFG.get("LDCONSOLE_PATH", str(LD_DIR / "ldconsole.exe"))

REF_GAME_PACKAGE: str = CFG.get("REF_GAME_PACKAGE", "com.example.game")
REF_GAME_ACTIVITY: str = CFG.get(
    "REF_GAME_ACTIVITY", f"{REF_GAME_PACKAGE}/com.unity3d.player.UnityPlayerActivity"
)
REF_APK_PATH: Path = ROOT / CFG.get("REF_APK_NAME", f"{REF_GAME_PACKAGE}.apk")

EXPORTED_ASSETS_DIR: Path = ROOT / CFG.get(
    "EXPORTED_ASSETS_DIR", "AllAssetsExport/ExportedProject/Assets"
)
ASSET_RIPPER_EXPORT_DIR: Path = ROOT / CFG.get(
    "ASSET_RIPPER_EXPORT_DIR", "AssetRipperExport/ExportedProject/Assets"
)
RESOURCES_EXPORT_DIR: Path = ROOT / CFG.get("RESOURCES_EXPORT_DIR", "ResourcesExport")
LUA_CONFIG_DIR: Path = ROOT / CFG.get("LUA_CONFIG_DIR", "ResourcesExport/Config/LuaJSON")
RESOURCES_CONFIG_DIR: Path = ROOT / CFG.get("RESOURCES_CONFIG_DIR", "ResourcesExport/Config")
RAW_GAME_ASSETS_DIR: Path = ROOT / CFG.get("RAW_GAME_ASSETS_DIR", "game_asset/assets")
DOWNLOADED_ASSETS_DIR: Path = ROOT / CFG.get("DOWNLOADED_ASSETS_DIR", "downloaded/assets")
EXTRACTED_DIR: Path = ROOT / CFG.get("EXTRACTED_DIR", "extracted")
DECRYPTED_DIR: Path = ROOT / CFG.get("DECRYPTED_DIR", "decrypted/assets/data")
REPORTS_DIR: Path = ROOT / CFG.get("REPORTS_DIR", "reports")
