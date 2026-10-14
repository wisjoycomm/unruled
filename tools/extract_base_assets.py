#!/usr/bin/env python3
"""Export readable base-APK textures/fonts, and inventory readable bundles.

These base assets are predominantly SDK/debug UI, not gameplay artwork.
Run scan_assets.py first. Unity engine built-in resources are excluded.
"""
import csv
import hashlib
import json
import re
import shutil
import zipfile
from collections import Counter
from pathlib import Path

import sys
import UnityPy

sys.path.insert(0, str(Path(__file__).resolve().parent))
import project_cfg  # noqa: E402

ROOT = project_cfg.ROOT


def main():
    base = ROOT / "extracted/base/assets/bin/Data"
    base.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(project_cfg.REF_APK_PATH) as archive:
        for name in archive.namelist():
            if name.startswith("assets/bin/Data/") and not name.endswith("/"):
                path = ROOT / "extracted/base" / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(archive.read(name))
    output = project_cfg.RECOVERED_ASSETS_ROOT / "BaseAPK"
    rows = []
    for path in sorted(base.iterdir()):
        if not re.fullmatch(r"[0-9a-f]{32}", path.name):
            continue
        env = UnityPy.load(str(path))
        for obj in env.objects:
            kind = obj.type.name
            if kind not in ("Texture2D", "Font"):
                continue
            row = {"source": str(path.relative_to(ROOT)), "path_id": obj.path_id, "type": kind}
            try:
                data = obj.read()
                row["name"] = data.m_Name
                safe_name = re.sub(r"[^\w.\-@]+", "_", data.m_Name).strip("._") or kind
                # Include source/path ID to prevent collisions between atlases.
                name = f"{safe_name}__{path.name[:8]}_{obj.path_id}"
                if kind == "Texture2D":
                    if data.m_Width == 0 or data.m_Height == 0:
                        row.update(status="skipped", reason="Empty runtime-generated font atlas")
                        rows.append(row)
                        continue
                    target = output / "Textures" / (name + ".png")
                    target.parent.mkdir(parents=True, exist_ok=True)
                    image = data.image
                    row.update(width=image.width, height=image.height)
                    image.save(target)
                else:
                    font = bytes(data.m_FontData)
                    if not font:
                        row.update(status="skipped", reason="No embedded font data")
                        rows.append(row)
                        continue
                    if font[:4] not in (b"\0\1\0\0", b"OTTO", b"true"):
                        raise ValueError("Unrecognized embedded font format")
                    target = output / "Fonts" / (name + (".otf" if font[:4] == b"OTTO" else ".ttf"))
                    target.parent.mkdir(parents=True, exist_ok=True)
                    target.write_bytes(font)
                row.update(status="exported", output=str(target.relative_to(ROOT)),
                           sha256=hashlib.sha256(target.read_bytes()).hexdigest())
            except Exception as exc:
                row.update(status="failed", error=str(exc))
            rows.append(row)
    output.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(ROOT / "icon.png", output / "AppIcon.png")
    bundle_rows = []
    with (ROOT / "reports/bundles.csv").open() as source:
        for row in csv.DictReader(source):
            if row["first_data_block"] != "ok":
                continue
            record = {"source": row["file"]}
            try:
                env = UnityPy.load(str(ROOT / row["file"]))
                record["object_types"] = Counter(obj.type.name for obj in env.objects)
                dependencies = set()
                for obj in env.objects:
                    for ext in obj.assets_file.externals:
                        dependencies.add(ext.path)
                record["external_dependencies"] = sorted(dependencies)
                record["status"] = "readable_with_external_dependencies"
            except Exception as exc:
                record.update(status="failed", error=str(exc))
            bundle_rows.append(record)
    report = {"base_assets": rows, "readable_bundles": bundle_rows,
              "exported_types": Counter(r["type"] for r in rows if r["status"] == "exported"),
              "extra": "AppIcon.png copied from package icon; not included in exported_types",
              "scope": "Base APK textures/fonts are primarily SDK/debug UI. No gameplay meshes or animation exported."}
    (ROOT / "reports/base_export.json").write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n")
    print(json.dumps(report["exported_types"]))


if __name__ == "__main__":
    main()
