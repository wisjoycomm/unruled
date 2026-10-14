#!/usr/bin/env python3
"""Decode this build's bundles using its asset reader in an offline ARM64 VM.

Requires the snapshot produced by research_elf.py. No Android code runs on the
host CPU, and the VM has no filesystem, network or process access.
"""
import argparse
import hashlib
import json
import os
import sys
from pathlib import Path

from unicorn import UC_ARCH_ARM64, UC_HOOK_CODE, UC_MODE_ARM, Uc
from unicorn.arm64_const import (
    UC_ARM64_REG_LR,
    UC_ARM64_REG_PC,
    UC_ARM64_REG_SP,
    UC_ARM64_REG_TPIDR_EL0,
    UC_ARM64_REG_X0,
    UC_ARM64_REG_X1,
    UC_ARM64_REG_X2,
)

sys.path.insert(0, str(Path(__file__).resolve().parent))
import arm64_vm  # noqa: E402
import project_cfg  # noqa: E402
import unityfs  # noqa: E402

ROOT = project_cfg.ROOT
INPUT = 0x72000000
STOP = 0x5000F000


class Decoder:
    def __init__(self, verify_snapshot_hashes: bool = True):
        lib_path = project_cfg.EXTRACTED_DIR / "native/libunity.so"
        mem_path = project_cfg.REPORTS_DIR / "elf_probe_memory.bin"
        heap_path = project_cfg.REPORTS_DIR / "elf_probe_heap.bin"
        if verify_snapshot_hashes:
            required = {
                lib_path: "31c0a92b1ff42547cbd200d7d88e8f7df9218aad6dcee147ab9dbf8e6e8788a0",
                mem_path: "b6edcd75b7c5ee0052be8a8a93a2bdfd158510d55bf026628e2266dd14481689",
                heap_path: "531cffc1e0b78761725c04075d34c84dd6eabb300f4b8483711b4fe8b66d792d",
            }
            for path, digest in required.items():
                if hashlib.sha256(path.read_bytes()).hexdigest() != digest:
                    raise ValueError(f"Decoder snapshot/build mismatch: {path}")
        self.imports = arm64_vm.read_elf_import_stubs(lib_path)
        self.uc = uc = Uc(UC_ARCH_ARM64, UC_MODE_ARM)
        for address, size in [
            (0, 0x1800000),
            (0x50000000, 0x10000),
            (0x60000000, 0x8000000),
            (0x70000000, 0x100000),
            (0x71000000, 0x10000),
            (INPUT, 0x1000000),
        ]:
            uc.mem_map(address, size)
        uc.mem_write(0, mem_path.read_bytes())
        uc.mem_write(0x60000000, heap_path.read_bytes())
        uc.reg_write(UC_ARM64_REG_TPIDR_EL0, 0x71000000)
        self.heap = 0x67000000
        uc.hook_add(UC_HOOK_CODE, self.unhandled, begin=0x50000000, end=0x5000FFFF)

    def unhandled(self, uc, address, size, data):
        if address == STOP:
            uc.emu_stop()
            return
        imports = getattr(self, "imports", arm64_vm.LEGACY_STUB_SYMBOLS)
        sym = imports.get(address) or arm64_vm.LEGACY_STUB_SYMBOLS.get(address)
        a, b, c = [uc.reg_read(r) for r in (UC_ARM64_REG_X0, UC_ARM64_REG_X1, UC_ARM64_REG_X2)]
        if sym in ("malloc", "_Znwm", "_Znam"):
            result = self.heap
            self.heap += (a + 15) // 16 * 16
            if self.heap > 0x68000000:
                raise RuntimeError("Decoder heap exhausted")
        elif sym in ("free", "__cxa_atexit", "__cxa_finalize"):
            result = 0
        elif sym == "strlen":
            result = bytes(uc.mem_read(a, 1024)).index(0)
        elif sym in ("memcpy", "memmove"):
            uc.mem_write(a, bytes(uc.mem_read(b, c)))
            result = a
        elif sym == "memset":
            uc.mem_write(a, bytes([b & 255]) * c)
            result = a
        else:
            raise RuntimeError(f"Unexpected VM import {address:#x} ({sym})")
        uc.reg_write(UC_ARM64_REG_X0, result)
        uc.reg_write(UC_ARM64_REG_PC, uc.reg_read(UC_ARM64_REG_LR))

    def decode(self, data: bytes) -> bytes:
        if len(data) >= 0x1000000:
            raise ValueError("Input exceeds VM buffer")
        uc = self.uc
        self.heap = 0x67000000
        uc.mem_write(INPUT, data)
        uc.reg_write(UC_ARM64_REG_X0, INPUT)
        uc.reg_write(UC_ARM64_REG_X1, len(data))
        uc.reg_write(UC_ARM64_REG_SP, 0x70080000)
        uc.reg_write(UC_ARM64_REG_LR, STOP)
        uc.emu_start(0x1406580, STOP, count=1000000)
        if uc.reg_read(UC_ARM64_REG_PC) != STOP:
            raise RuntimeError("VM instruction budget exceeded")
        return bytes(uc.mem_read(INPUT, len(data)))


def bundle_blocks(payload: bytes):
    header = unityfs.parse_unityfs_header(payload)
    for b in header.blocks:
        yield b.offset, b.compressed, b.expanded, b.codec


def decrypt_bundle(decoder: Decoder, payload: bytes) -> bytes:
    result = bytearray(payload)
    for offset, size, expanded, codec in bundle_blocks(payload):
        raw = payload[offset : offset + size]
        if b"\xee\xdd" in raw[:74]:
            raw = decoder.decode(raw)
            result[offset : offset + size] = raw
        unityfs.decompress_block(raw, expanded, codec)
    return bytes(result)


_WORKER_DECODER: Decoder | None = None


def _init_worker() -> None:
    global _WORKER_DECODER
    _WORKER_DECODER = Decoder()


def _process_one(task: tuple[Path, Path, bool]) -> dict[str, str]:
    path, target, resume = task
    decoder = _WORKER_DECODER or Decoder()
    row = {"file": path.name}
    try:
        if resume and target.exists():
            data = target.read_bytes()
            for offset, size, expanded, codec in bundle_blocks(data):
                raw = data[offset : offset + size]
                if b"\xee\xdd" in raw[:74]:
                    raise ValueError("Target still contains encrypted block")
                unityfs.decompress_block(raw, expanded, codec)
        else:
            decoded = decrypt_bundle(decoder, path.read_bytes())
            target.write_bytes(decoded)
        row["status"] = "ok"
    except Exception as exc:
        row.update(status="failed", error=f"{type(exc).__name__}: {exc}")
    return row


def main():
    from concurrent.futures import ProcessPoolExecutor

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=Path, default=project_cfg.RAW_GAME_ASSETS_DIR / "data")
    parser.add_argument("--output", type=Path, default=project_cfg.DECRYPTED_DIR)
    parser.add_argument("--filter", default="*.unity3d")
    parser.add_argument("--limit", type=int)
    parser.add_argument(
        "--workers",
        type=int,
        default=min(8, os.cpu_count() or 4),
        help="Number of parallel VM worker processes (1 = single-process)",
    )
    parser.add_argument(
        "--resume", action="store_true", help="Validate and reuse existing plaintext output"
    )
    parser.add_argument(
        "--report", type=Path, default=project_cfg.REPORTS_DIR / "decryption.json"
    )
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    paths = sorted(args.input.glob(args.filter))
    if args.limit:
        paths = paths[: args.limit]
    tasks = [(p, args.output / p.name, args.resume) for p in paths]
    rows = []
    if args.workers <= 1 or len(tasks) <= 1:
        _init_worker()
        iterator = (_process_one(t) for t in tasks)
        for index, row in enumerate(iterator):
            if row["status"] != "ok":
                print(row, flush=True)
            rows.append(row)
            if index % 100 == 0:
                print(f"{index + 1}/{len(paths)}", flush=True)
    else:
        with ProcessPoolExecutor(max_workers=args.workers, initializer=_init_worker) as pool:
            for index, row in enumerate(pool.map(_process_one, tasks, chunksize=8)):
                if row["status"] != "ok":
                    print(row, flush=True)
                rows.append(row)
                if index % 100 == 0:
                    print(f"{index + 1}/{len(paths)}", flush=True)
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(rows, indent=2) + "\n")
    print(f"Decoded {sum(r['status'] == 'ok' for r in rows)}/{len(rows)}")


if __name__ == "__main__":
    main()
