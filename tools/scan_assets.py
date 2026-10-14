#!/usr/bin/env python3
"""Inventory this Android asset pack and test its first compressed data block.

Run with .venv/bin/python tools/scan_assets.py. A readable first block does
not prove that every object in a bundle can be exported.
"""
import csv
import json
import struct
from collections import Counter
from pathlib import Path

import lz4.block

ROOT = Path(__file__).resolve().parents[1]


def inspect_bundle(path):
    row = {"file": str(path.relative_to(ROOT)), "bytes": path.stat().st_size,
           "category": "_".join(path.stem.split("_")[:2])}
    with path.open("rb") as f:
        def cstring():
            value = bytearray()
            while True:
                c = f.read(1)
                if not c:
                    raise ValueError("Truncated string")
                if c == b"\0":
                    return value.decode("utf-8")
                value.extend(c)

        if cstring() != "UnityFS":
            raise ValueError("Not UnityFS")
        version = struct.unpack(">I", f.read(4))[0]
        cstring()
        row["unity_version"] = cstring()
        size, compressed, expanded, flags = struct.unpack(">QIII", f.read(20))
        if size != row["bytes"]:
            raise ValueError("Bundle length mismatch")
        if version >= 7:
            f.seek((f.tell() + 15) // 16 * 16)
        data_start = f.tell()
        if flags & 0x80:
            f.seek(size - compressed)
        raw = f.read(compressed)
        codec = flags & 0x3F
        if codec in (2, 3):
            info = lz4.block.decompress(raw, uncompressed_size=expanded)
        elif codec == 0:
            info = raw
        else:
            raise ValueError(f"Unsupported info codec {codec}")
        if len(info) != expanded:
            raise ValueError("Block info length mismatch")
        block_count = struct.unpack_from(">I", info, 16)[0]
        row["blocks"] = block_count
        uncompressed, compressed, block_flags = struct.unpack_from(">IIH", info, 20)
        if flags & 0x80:
            f.seek(data_start)
        if flags & 0x200:
            f.seek((f.tell() + 15) // 16 * 16)
        raw = f.read(compressed)
        row["block_info"] = "ok"
        try:
            codec = block_flags & 0x3F
            if codec in (2, 3):
                data = lz4.block.decompress(raw, uncompressed_size=uncompressed)
            elif codec == 0:
                data = raw
            else:
                raise ValueError(f"Unsupported data codec {codec}")
            if len(data) != uncompressed:
                raise ValueError("Data block length mismatch")
            row["first_data_block"] = "ok"
        except Exception as exc:
            row["first_data_block"] = "failed"
            row["error"] = f"{type(exc).__name__}: {exc}"
    return row


def main():
    report_dir = ROOT / "reports"
    report_dir.mkdir(exist_ok=True)
    rows = []
    for path in sorted((ROOT / "game_asset/assets/data").glob("*.unity3d")):
        try:
            rows.append(inspect_bundle(path))
        except Exception as exc:
            rows.append({"file": str(path.relative_to(ROOT)),
                         "bytes": path.stat().st_size, "first_data_block": "not_tested",
                         "error": f"{type(exc).__name__}: {exc}"})
    fields = ["file", "bytes", "category", "unity_version", "blocks",
              "block_info", "first_data_block", "error"]
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
        "note": "Decompression failure alone does not identify the encryption algorithm."
    }
    (report_dir / "scan_summary.json").write_text(json.dumps(summary, indent=2) + "\n")
    print(json.dumps(summary, indent=2))


if __name__ == "__main__":
    main()
