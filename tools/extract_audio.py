#!/usr/bin/env python3
"""Extract unencrypted HCA streams from ACB/AWB to Unity-importable PCM WAV.

Requires ffmpeg on PATH. Preserves bank name and waveform ID; does not
reconstruct cue sequences, mixer routing, or loop settings from CRI metadata.
"""
import concurrent.futures
import hashlib
import json
import shutil
import struct
import subprocess
import tempfile
import wave
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "UnityAssets/Assets/WhiteoutSurvival/Audio"


def afs2_streams(data, base):
    """Validate a candidate AFS2 bank before yielding ID / HCA bytes."""
    a = memoryview(data)[base:]
    if len(a) < 16 or a[:4] != b"AFS2":
        return []
    offset_width, id_width = a[5], a[6]
    count, alignment, subkey = struct.unpack_from("<IHH", a, 8)
    if offset_width not in (2, 4, 8) or id_width not in (1, 2, 4) or not 0 < count < 100000 or not alignment:
        return []
    offset_start = 16 + count * id_width
    table_end = offset_start + (count + 1) * offset_width
    if table_end > len(a):
        return []
    offsets = [int.from_bytes(a[offset_start+i*offset_width:offset_start+(i+1)*offset_width], "little")
               for i in range(count + 1)]
    if offsets[0] < table_end or offsets[-1] > len(a) or offsets != sorted(offsets):
        return []
    streams = []
    for i in range(count):
        start = (offsets[i] + alignment - 1) // alignment * alignment
        end = offsets[i + 1]
        if start >= end:
            return []
        payload = bytes(a[start:end])
        if payload[:4] != b"HCA\0":
            return []
        header_size = int.from_bytes(payload[6:8], "big")
        if not 8 <= header_size <= len(payload):
            return []
        ciph = payload[:header_size].find(b"ciph")
        if ciph < 0 or ciph + 6 > header_size:
            return []
        cipher = int.from_bytes(payload[ciph+4:ciph+6], "big")
        stream_id = int.from_bytes(a[16+i*id_width:16+(i+1)*id_width], "little")
        streams.append((stream_id, payload, cipher, subkey))
    return streams


def convert(job):
    bank, bank_offset, stream_id, payload, cipher, subkey = job
    relative = Path(bank.stem) / f"wave_{stream_id:05d}_bank_{bank_offset}.wav"
    target = OUTPUT / relative
    row = {"source": str(bank.relative_to(ROOT)), "bank_offset": bank_offset,
           "waveform_id": stream_id, "cipher": cipher, "subkey": subkey,
           "hca_sha256": hashlib.sha256(payload).hexdigest()}
    if cipher != 0:
        return {**row, "status": "skipped_encrypted"}
    try:
        target.parent.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="whiteout-audio-") as temp:
            source = Path(temp) / "source.hca"
            wav = Path(temp) / "output.wav"
            source.write_bytes(payload)
            result = subprocess.run(["ffmpeg", "-nostdin", "-hide_banner", "-loglevel", "error",
                                     "-xerror", "-i", str(source), "-map_metadata", "-1",
                                     "-c:a", "pcm_s16le", str(wav)], capture_output=True, text=True)
            if result.returncode:
                raise RuntimeError(result.stderr.strip())
            with wave.open(str(wav), "rb") as check:
                if check.getnframes() == 0:
                    raise ValueError("Empty WAV")
                row.update(channels=check.getnchannels(), sample_rate=check.getframerate(),
                           frames=check.getnframes(), duration_seconds=check.getnframes()/check.getframerate())
            shutil.copyfile(wav, target)
        row.update(status="exported", output=str(target.relative_to(ROOT)))
    except Exception as exc:
        row.update(status="failed", error=str(exc))
    return row


def main():
    if not shutil.which("ffmpeg"):
        raise SystemExit("Install ffmpeg first")
    jobs, banks = [], []
    paths = sorted((ROOT / "game_asset/assets/sound").glob('*')) + sorted((ROOT / "downloaded/assets/sound").glob('*'))
    for path in paths:
        if path.suffix not in (".acb", ".awb"):
            continue
        data = path.read_bytes()
        pos, found = 0, 0
        while True:
            pos = data.find(b"AFS2", pos)
            if pos < 0:
                break
            streams = afs2_streams(data, pos)
            jobs.extend((path, pos, *stream) for stream in streams)
            found += len(streams)
            pos += 4
        banks.append({"file": str(path.relative_to(ROOT)), "hca_streams": found})
    print(f"Found {len(jobs)} HCA streams in {len(banks)} ACB/AWB files", flush=True)
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
        rows = []
        for i, row in enumerate(pool.map(convert, jobs), 1):
            rows.append(row)
            if i % 100 == 0:
                print(f"Converted {i}/{len(jobs)}", flush=True)
    report = {"banks": banks, "streams": rows, "exported": sum(r["status"] == "exported" for r in rows),
              "failed": sum(r["status"] == "failed" for r in rows),
              "skipped_encrypted": sum(r["status"] == "skipped_encrypted" for r in rows)}
    (ROOT / "reports").mkdir(exist_ok=True)
    (ROOT / "reports/audio_export.json").write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps({k: v for k, v in report.items() if k not in ("banks", "streams")}, indent=2))


if __name__ == "__main__":
    main()
