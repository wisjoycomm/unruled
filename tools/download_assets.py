#!/usr/bin/env python3
"""Read the shipped reference game manifest and download hash-verified missing files.

Examples:
  .venv/bin/python tools/download_assets.py inventory
  .venv/bin/python tools/download_assets.py download --base-url https://HOST/BUILD/ --group data --limit 1

The default CDN root was recovered from this build and verified against its
manifest. Run decrypt_bundles.py after downloading protected UnityFS bundles.
"""
import argparse
import concurrent.futures
import csv
import hashlib
import json
import re
import shutil
import ssl
import struct
import time
import urllib.error
import urllib.parse
import urllib.request
import zipfile
from collections import Counter, defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
MANIFEST_ASSET = "assets/bin/Data/2df6f43727b2e40f1bbd664ab1031bac"
VERIFIED_CDN = "https://gof-gloabl-cdn.akamaized.net/RELEASE/1_33_9/android/split/"


def tls_context():
    import certifi
    return ssl.create_default_context(cafile=certifi.where())


class Reader:
    def __init__(self, data, position=0):
        self.data, self.position = data, position

    def take(self, size):
        if size < 0 or self.position + size > len(self.data):
            raise ValueError("Truncated manifest")
        data = self.data[self.position:self.position + size]
        self.position += size
        return data

    def u32(self):
        return struct.unpack("<I", self.take(4))[0]

    def align(self):
        self.take((-self.position) % 4)

    def string(self):
        length = self.u32()
        if length > 4096:
            raise ValueError("Invalid string length")
        value = self.take(length).decode("utf-8")
        self.align()
        return value


def parse_manifest(data):
    reader = Reader(data, 28)  # MonoBehaviour base fields, including m_Script.
    if reader.string() != "version":
        raise ValueError("Expected AssetVersion named version")
    groups = reader.u32()
    if not 1 <= groups <= 100:
        raise ValueError("Invalid group count")
    rows = []
    for _ in range(groups):
        group, count = reader.string(), reader.u32()
        if not re.fullmatch(r"[a-z0-9_]+", group) or count > 1000000:
            raise ValueError("Invalid asset group")
        for _ in range(count):
            name, extension, digest = reader.string(), reader.string(), reader.string()
            size, flag1, flag2 = reader.u32(), reader.u32(), reader.u32()
            tag_length = reader.u32()
            if tag_length > 10000:
                raise ValueError("Invalid tag array")
            tags = list(reader.take(tag_length))
            reader.align()
            if not name or name in ('.', '..') or '/' in name or '\\' in name or any(ord(c) < 32 for c in name):
                raise ValueError(f"Unsafe asset name: {name!r}")
            if not re.fullmatch(r"\.[A-Za-z0-9]+", extension) or not re.fullmatch(r"[0-9a-f]{32}", digest):
                raise ValueError("Invalid extension or hash")
            filename = f"{name}_{digest}{extension}"
            rows.append({"group": group, "name": name, "extension": extension,
                         "md5": digest, "size": size, "flag1": flag1, "flag2": flag2,
                         "tags": tags, "path": f"{group}/{filename}"})
    return rows, data[reader.position:]


def read_manifest(apk):
    import UnityPy
    with zipfile.ZipFile(apk) as archive:
        payload = archive.read(MANIFEST_ASSET)
    env = UnityPy.load(payload)
    objects = [o for o in env.objects if o.type.name == "MonoBehaviour"]
    if len(objects) != 1:
        raise ValueError("Unexpected manifest object count")
    rows, trailing = parse_manifest(objects[0].get_raw_data())
    return {"source_apk": str(apk), "serialized_asset_sha256": hashlib.sha256(payload).hexdigest(),
            "assets": rows, "uninterpreted_footer_hex": trailing.hex(),
            "note": "flag1, flag2, tags and footer are preserved; their semantics have not been assumed."}


def read_saved_manifest(path):
    """Use the exported manifest so a development clone does not require an APK."""
    manifest = json.loads(path.read_text())
    seen = set()
    for row in manifest["assets"]:
        group, name, extension, digest = (row[k] for k in ("group", "name", "extension", "md5"))
        if not re.fullmatch(r"[a-z0-9_]+", group):
            raise ValueError("Invalid asset group")
        if not name or name in ('.', '..') or '/' in name or '\\' in name or any(ord(c) < 32 for c in name):
            raise ValueError("Unsafe asset name")
        if not re.fullmatch(r"\.[A-Za-z0-9]+", extension) or not re.fullmatch(r"[0-9a-f]{32}", digest):
            raise ValueError("Invalid extension or hash")
        expected = f"{group}/{name}_{digest}{extension}"
        if row["path"] != expected or expected in seen:
            raise ValueError("Unsafe or duplicate manifest path")
        if type(row["size"]) is not int or row["size"] < 0:
            raise ValueError("Invalid asset size")
        seen.add(expected)
    return manifest


def md5_file(path):
    digest = hashlib.md5()
    with path.open("rb") as stream:
        while chunk := stream.read(1024*1024):
            digest.update(chunk)
    return digest.hexdigest()


def matches(path, row):
    return path.is_file() and path.stat().st_size == row["size"] and md5_file(path) == row["md5"]


def inventory(manifest, roots):
    rows = []
    for asset in manifest["assets"]:
        row = dict(asset)
        candidates = [root / row["path"] for root in roots]
        valid = next((p for p in candidates if matches(p, row)), None)
        row["status"] = "present_verified" if valid else "mismatch" if any(p.exists() for p in candidates) else "missing"
        row["local_file"] = str(valid) if valid else ""
        rows.append(row)
    return rows


def fetch(row, base_url, output, retries=3):
    target = output / row["path"]
    if matches(target, row):
        return {"path": row["path"], "status": "already_verified"}
    target.parent.mkdir(parents=True, exist_ok=True)
    partial = target.with_name(target.name + ".part")
    url = base_url.rstrip("/") + "/" + urllib.parse.quote(row["path"], safe="/")
    last_error = ""
    for attempt in range(retries):
        try:
            offset = partial.stat().st_size if partial.exists() else 0
            if offset >= row["size"]:
                if matches(partial, row):
                    partial.replace(target)
                    return {"path": row["path"], "status": "downloaded_verified"}
                partial.write_bytes(b"")
                offset = 0
            headers = {"User-Agent": "AssetInventory/1.0", "Accept-Encoding": "identity"}
            if offset:
                headers["Range"] = f"bytes={offset}-"
            request = urllib.request.Request(url, headers=headers)
            with urllib.request.urlopen(request, timeout=60, context=tls_context()) as response:
                if response.status == 206:
                    content_range = response.headers.get("Content-Range", "")
                    match = re.fullmatch(r"bytes (\d+)-(\d+)/(\d+)", content_range)
                    if not match or int(match[1]) != offset or int(match[3]) != row["size"]:
                        raise ValueError("Unexpected Content-Range")
                    mode = "ab"
                elif response.status == 200:
                    mode, offset = "wb", 0
                else:
                    raise ValueError(f"Unexpected HTTP status {response.status}")
                with partial.open(mode) as stream:
                    total = offset
                    while chunk := response.read(1024*1024):
                        total += len(chunk)
                        if total > row["size"]:
                            raise ValueError("Response exceeds manifest size")
                        stream.write(chunk)
            if not matches(partial, row):
                if partial.stat().st_size >= row["size"]:
                    partial.write_bytes(b"")
                raise ValueError("Downloaded size or MD5 does not match manifest")
            partial.replace(target)
            return {"path": row["path"], "status": "downloaded_verified", "url": url}
        except (OSError, ValueError, urllib.error.URLError) as exc:
            last_error = str(exc)
            if attempt + 1 < retries:
                time.sleep(min(2**attempt, 4))
    return {"path": row["path"], "status": "failed", "error": last_error, "url": url}


def main():
    import project_cfg

    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("command", choices=["inventory", "download"])
    source = parser.add_mutually_exclusive_group()
    source.add_argument("--apk", type=Path, help="Read a manifest from the original APK")
    source.add_argument("--manifest", type=Path, help="Read an exported JSON manifest without requiring an APK")
    parser.add_argument("--local", type=Path, default=ROOT / "game_asset/assets")
    parser.add_argument("--output", type=Path, default=ROOT / "downloaded/assets")
    parser.add_argument("--base-url", default=VERIFIED_CDN, help="CDN root containing data/, sound/, etc.; default verified for build 1.33.9")
    parser.add_argument("--group", action="append", help="Download selected group; repeat to select several")
    parser.add_argument("--filter", default="", help="Substring of asset name")
    parser.add_argument("--limit", type=int, default=0, help="Maximum files to download; 0 means no limit")
    parser.add_argument("--workers", type=int, default=2, choices=range(1,9))
    args = parser.parse_args()
    if args.limit < 0:
        parser.error("--limit must be nonnegative")
    apk = args.apk or project_cfg.REF_APK_PATH
    if args.manifest is not None:
        manifest = read_saved_manifest(args.manifest)
    elif args.apk is not None or apk.exists():
        manifest = read_manifest(apk)
    else:
        manifest = read_saved_manifest(ROOT / "tools/source_manifest.json")
    report_dir = ROOT / "reports"
    report_dir.mkdir(exist_ok=True)
    (report_dir / "asset_manifest.json").write_text(json.dumps(manifest, indent=2)+"\n")
    rows = inventory(manifest, [args.local, args.output])
    fields = ["group", "name", "path", "md5", "size", "status", "local_file"]
    with (report_dir / "asset_inventory.csv").open("w", newline="") as stream:
        writer = csv.DictWriter(stream, fields, extrasaction="ignore")
        writer.writeheader()
        writer.writerows(rows)
    summary = {"total": len(rows), "status": dict(Counter(r["status"] for r in rows)), "groups": {}}
    for group in sorted({r["group"] for r in rows}):
        selected = [r for r in rows if r["group"] == group]
        summary["groups"][group] = {"total": len(selected), "missing_or_invalid": sum(r["status"] != "present_verified" for r in selected),
                                    "missing_or_invalid_bytes": sum(r["size"] for r in selected if r["status"] != "present_verified")}
    (report_dir / "download_inventory_summary.json").write_text(json.dumps(summary, indent=2)+"\n")
    print(json.dumps(summary, indent=2), flush=True)
    if args.command == "inventory":
        return
    if not args.base_url or urllib.parse.urlparse(args.base_url).scheme not in ("https", "http"):
        parser.error("download requires --base-url from a verified game CDN request")
    selected = [r for r in rows if r["status"] != "present_verified"
                and (not args.group or r["group"] in args.group) and args.filter in r["name"]]
    if args.limit:
        selected = selected[:args.limit]
    needed = sum(r["size"] for r in selected)
    args.output.mkdir(parents=True, exist_ok=True)
    if shutil.disk_usage(args.output).free < needed + 256*1024*1024:
        raise SystemExit("Insufficient disk space for selected assets")
    print(f"Downloading {len(selected)} files, up to {needed} bytes", flush=True)
    results = []
    with concurrent.futures.ThreadPoolExecutor(max_workers=args.workers) as pool:
        futures = [pool.submit(fetch, row, args.base_url, args.output) for row in selected]
        for future in concurrent.futures.as_completed(futures):
            result = future.result()
            results.append(result)
            if result["status"] == "failed" or len(results) % 50 == 0:
                print(f"{len(results)}/{len(selected)} {result['status']} {result['path']}", flush=True)
    (report_dir / "download_results.json").write_text(json.dumps(results, indent=2)+"\n")
    if any(r["status"] == "failed" for r in results):
        raise SystemExit(1)


if __name__ == "__main__":
    main()
