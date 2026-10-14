"""Shared Unity YAML prefab/asset parser, `.meta` GUID cache, and Sprite Atlas indexer.

Eliminates duplicated regex parsers across `tools/ui/`, `tools/ld/`, `export_spine.py`,
`prepare_unity_3d.py`, and `import_battle_fx.py`.
"""
from __future__ import annotations

import json
import os
import re
import sys
from pathlib import Path
from typing import Iterable

sys.path.insert(0, str(Path(__file__).resolve().parent))
import project_cfg  # noqa: E402

GUID_RE = re.compile(r"guid: ([a-f0-9]{32})")
DOC_HEADER_RE = re.compile(r"^--- !u!(\d+) &(-?\d+).*$", re.M)
NUM_RE = r"-?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?"

SPRITE_ENTRY_RE = re.compile(
    r"- serializedVersion: \d+\s*\n\s*name: (\S+).*?"
    r"rect:\s*\n\s*serializedVersion: \d+\s*\n\s*x: ([\d.]+)\s*\n\s*y: ([\d.]+)\s*\n\s*width: ([\d.]+)\s*\n\s*height: ([\d.]+)"
    r"(?:.*?border: \{x: ([-\d.]+), y: ([-\d.]+), z: ([-\d.]+), w: ([-\d.]+)\})?",
    re.S,
)
FILEID_TABLE_RE = re.compile(r"- first:\s*\n\s*\d+: (-?\d+)\s*\n\s*second: (\S+)")
INTERNAL_ID_RE = re.compile(r"name: (\S+)\n(?:.*\n){0,12}?\s*internalID: (-?\d+)")


def parse_yaml_docs(
    source: str | Path, *, int_class: bool = False, include_header: bool = False
) -> dict[str, tuple[int | str, str]]:
    """Split a Unity YAML file (`.prefab`, `.unity`, `.asset`, `.playable`) into `{fileID: (class_id, body)}`."""
    if isinstance(source, Path) or (isinstance(source, str) and "\n" not in source and os.path.exists(source)):
        text = Path(source).read_text(encoding="utf-8", errors="ignore")
    else:
        text = str(source)
    matches = list(DOC_HEADER_RE.finditer(text))
    docs: dict[str, tuple[int | str, str]] = {}
    for i, m in enumerate(matches):
        start = m.start() if include_header else m.end()
        end = matches[i + 1].start() if i + 1 < len(matches) else len(text)
        cls: int | str = int(m.group(1)) if int_class else m.group(1)
        docs[m.group(2)] = (cls, text[start:end])
    return docs


def yaml_field(body: str, key: str, default: str | None = None) -> str | None:
    """Extract a scalar YAML field value `key: value` from a document chunk."""
    m = re.search(rf"^\s*{re.escape(key)}:\s*(.+)$", body, re.M)
    return m.group(1).strip() if m else default


def yaml_vec2(
    body: str, key: str, default: tuple[float, float] | None = None
) -> tuple[float, float] | None:
    """Extract a 2D vector `{x: ..., y: ...}` from a document chunk."""
    m = re.search(rf"{re.escape(key)}:\s*\{{x:\s*({NUM_RE}),\s*y:\s*({NUM_RE})", body)
    return (float(m.group(1)), float(m.group(2))) if m else default


def yaml_color_hex(body: str, key: str) -> str | None:
    """Extract a Unity RGBA color (inline or multiline) as `#RRGGBB/a.aa`."""
    m = re.search(
        rf"{re.escape(key)}:\s*\n\s+r: ({NUM_RE})\s*\n\s+g: ({NUM_RE})\s*\n\s+b: ({NUM_RE})\s*\n\s+a: ({NUM_RE})",
        body,
    )
    if not m:
        m = re.search(
            rf"{re.escape(key)}:\s*\{{r:\s*({NUM_RE}),\s*g:\s*({NUM_RE}),\s*b:\s*({NUM_RE}),\s*a:\s*({NUM_RE})",
            body,
        )
    if not m:
        return None
    r, g, b, a = (float(x) for x in m.groups())
    return f"#{int(r * 255 + 0.5):02X}{int(g * 255 + 0.5):02X}{int(b * 255 + 0.5):02X}/{a:.2f}"


def parse_atlas_meta(
    source: str | Path,
) -> tuple[str | None, dict[str, str], dict[str, tuple[float, ...]]]:
    """Parse a `.png.meta` file into `(guid, {fileID: sprite_name}, {sprite_name: (x, y, w, h, bx, by, bz, bw)})`."""
    if isinstance(source, Path) or (isinstance(source, str) and "\n" not in source and os.path.exists(source)):
        text = Path(source).read_text(encoding="utf-8", errors="ignore")
    else:
        text = str(source)
    gm = GUID_RE.search(text[:400])
    guid = gm.group(1) if gm else None
    fid_map: dict[str, str] = {}
    for m in FILEID_TABLE_RE.finditer(text):
        fid_map[m.group(1)] = m.group(2)
    for m in INTERNAL_ID_RE.finditer(text):
        fid_map.setdefault(m.group(2), m.group(1))
    sprites: dict[str, tuple[float, ...]] = {}
    for m in SPRITE_ENTRY_RE.finditer(text):
        name = m.group(1)
        vals = [float(v) if v is not None else 0.0 for v in m.groups()[1:]]
        sprites[name] = tuple(vals)
    return guid, fid_map, sprites


def index_atlas_sprites(
    folders: Iterable[str | Path] | None = None,
) -> dict[str, tuple[str, float, float, float, float, float, float, float, float]]:
    """Index all `.png.meta` sprite entries across `folders` (defaults to `[project_cfg.ATLAS_DIR]`).

    Returns `{sprite_name: (png_path, x, y, w, h, bx, by, bz, bw)}`.
    """
    if folders is None:
        folders = [project_cfg.ATLAS_DIR]
    out: dict[str, tuple[str, float, float, float, float, float, float, float, float]] = {}
    for folder in folders:
        folder_path = Path(folder)
        if not folder_path.is_dir():
            continue
        for meta in sorted(folder_path.glob("*.meta")):
            if not (meta.name.endswith(".png.meta") or meta.name.startswith("sactx-")):
                continue
            _, _, sprites = parse_atlas_meta(meta)
            png_path = str(meta)[:-5]
            for name, vals in sprites.items():
                out.setdefault(name, (png_path, *vals))  # type: ignore[arg-type]
    return out


def build_guid_map(assets_root: str | Path) -> dict[str, Path]:
    """Scan all `.meta` files under `assets_root` and return `{guid: asset_path}`."""
    root = Path(assets_root)
    by_guid: dict[str, Path] = {}
    for p in root.rglob("*.meta"):
        try:
            head = p.read_text(encoding="utf-8", errors="ignore")[:512]
        except OSError:
            continue
        m = GUID_RE.search(head)
        if m:
            by_guid[m.group(1)] = Path(str(p)[:-5])
    return by_guid


def cached_sprite_guid_index(assets_root: str | Path | None = None) -> dict[str, dict]:
    """Build or load cached `{guid: {'file': filename, 'names': {fileID: sprite_name}}}` in `.temp/`."""
    root = Path(assets_root) if assets_root else project_cfg.EXPORTED_ASSETS_DIR
    cache_path = project_cfg.TEMP_DIR / "sprite_guid_index.json"
    if cache_path.exists():
        return json.loads(cache_path.read_text(encoding="utf-8"))
    idx: dict[str, dict] = {}
    tex_dir = root / "Texture2D"
    if tex_dir.is_dir():
        for meta in sorted(tex_dir.glob("*.meta")):
            guid, fid_map, _ = parse_atlas_meta(meta)
            if guid:
                idx[guid] = {"file": meta.name[:-5], "names": fid_map}
    cache_path.parent.mkdir(parents=True, exist_ok=True)
    cache_path.write_text(json.dumps(idx), encoding="utf-8")
    return idx
