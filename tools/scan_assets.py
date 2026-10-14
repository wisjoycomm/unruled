#!/usr/bin/env python3
"""Inventory this Android asset pack and test its first compressed data block.

Run with `python tools/scan_assets.py`. A readable first block does
not prove that every object in a bundle can be exported.
"""
import concurrent.futures
import csv
import json
import sys
from collections import Counter
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import project_cfg  # noqa: E402
import unityfs  # noqa: E402

ROOT = project_cfg.ROOT


def inspect_bundle(path: Path) -> dict:
    row = {
        "file": str(path.relative_to(ROOT)),
        "bytes": path.stat().st_size,
        "category": "_".join(path.stem.split("_")[:2]),
    }
    with path.open("rb") as f:
        header = unityfs.parse_unityfs_header(f, total_bytes=row["bytes"])
        row["unity_version"] = header.engine_version
        row["blocks"] = len(header.blocks)
        row["block_info"] = "ok"
        first = header.blocks[0]
        f.seek(first.offset)
        raw = f.read(first.compressed)
        try:
            unityfs.decompress_block(raw, first.expanded, first.codec)
            row["first_data_block"] = "ok"
        except Exception as exc:
            row["first_data_block"] = "failed"
            row["error"] = f"{type(exc).__name__}: {exc}"
    return row


def _safe_inspect(path: Path) -> dict:
    try:
        return inspect_bundle(path)
    except Exception as exc:
        return {
            "file": str(path.relative_to(ROOT)),
            "bytes": path.stat().st_size,
            "first_data_block": "not_tested",
            "error": f"{type(exc).__name__}: {exc}",
        }


def main():
    report_dir = project_cfg.REPORTS_DIR
    report_dir.mkdir(exist_ok=True)
    paths = sorted((project_cfg.RAW_GAME_ASSETS_DIR / "data").glob("*.unity3d"))
    with concurrent.futures.ThreadPoolExecutor() as pool:
        rows = list(pool.map(_safe_inspect, paths))
    fields = [
        "file",
        "bytes",
        "category",
        "unity_version",
        "blocks",
        "block_info",
        "first_data_block",
        "error",
    ]
    with (report_dir / "bundles.csv").open("w", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=fields)
        writer.writeheader()
        writer.writerows(rows)
    summary = {
        "bundle_count": len(rows),
        "bundle_bytes": sum(r["bytes"] for r in rows),
        "versions": Counter(r.get("unity_version", "unknown") for r in rows),
        "first_data_block": Counter(r["first_data_block"] for r in rows),
        "categories_inferred_from_filename": Counter(r.get("category", "unknown") for r in rows),
        "scope": "Every bundle header and block-info table; first data block only.",
        "note": "Decompression failure alone does not identify the encryption algorithm.",
    }
    (report_dir / "scan_summary.json").write_text(json.dumps(summary, indent=2) + "\n")
    print(json.dumps(summary, indent=2))


if __name__ == "__main__":
    main()
