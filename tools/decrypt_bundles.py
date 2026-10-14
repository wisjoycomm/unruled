#!/usr/bin/env python3
"""Decode this build's bundles using its asset reader in an offline ARM64 VM.

Requires the snapshot produced by research_elf.py. No Android code runs on the
host CPU, and the VM has no filesystem, network or process access.
"""
import argparse
import io
import hashlib
import json
import struct
from pathlib import Path

import sys
import lz4.block
from unicorn import Uc, UC_ARCH_ARM64, UC_MODE_ARM, UC_HOOK_CODE
from unicorn.arm64_const import *

sys.path.insert(0, str(Path(__file__).resolve().parent))
import project_cfg  # noqa: E402

ROOT = project_cfg.ROOT
INPUT = 0x72000000
STOP = 0x5000f000


class Decoder:
    def __init__(self):
        required = {
            project_cfg.EXTRACTED_DIR / 'native/libunity.so': '31c0a92b1ff42547cbd200d7d88e8f7df9218aad6dcee147ab9dbf8e6e8788a0',
            project_cfg.REPORTS_DIR / 'elf_probe_memory.bin': 'b6edcd75b7c5ee0052be8a8a93a2bdfd158510d55bf026628e2266dd14481689',
            project_cfg.REPORTS_DIR / 'elf_probe_heap.bin': '531cffc1e0b78761725c04075d34c84dd6eabb300f4b8483711b4fe8b66d792d',
        }
        for path, digest in required.items():
            if hashlib.sha256(path.read_bytes()).hexdigest() != digest:
                raise ValueError(f'Decoder snapshot/build mismatch: {path}')
        self.uc = uc = Uc(UC_ARCH_ARM64, UC_MODE_ARM)
        for address, size in [(0, 0x1800000), (0x50000000, 0x10000),
                              (0x60000000, 0x8000000), (0x70000000, 0x100000),
                              (0x71000000, 0x10000), (INPUT, 0x1000000)]:
            uc.mem_map(address, size)
        uc.mem_write(0, (ROOT / 'reports/elf_probe_memory.bin').read_bytes())
        uc.mem_write(0x60000000, (ROOT / 'reports/elf_probe_heap.bin').read_bytes())
        uc.reg_write(UC_ARM64_REG_TPIDR_EL0, 0x71000000)
        self.heap = 0x67000000
        uc.hook_add(UC_HOOK_CODE, self.unhandled, begin=0x50000000, end=0x5000ffff)

    def unhandled(self, uc, address, size, data):
        if address == STOP:
            uc.emu_stop()
            return
        a, b, c = [uc.reg_read(r) for r in (UC_ARM64_REG_X0, UC_ARM64_REG_X1, UC_ARM64_REG_X2)]
        if address == 0x50000070:
            result = self.heap
            self.heap += (a+15)//16*16
            if self.heap > 0x68000000:
                raise RuntimeError('Decoder heap exhausted')
        elif address == 0x50000090:
            result = 0
        elif address == 0x50000080:
            result = bytes(uc.mem_read(a, 1024)).index(0)
        elif address in (0x50000050, 0x50000060):
            uc.mem_write(a, bytes(uc.mem_read(b, c)))
            result = a
        elif address == 0x50000040:
            uc.mem_write(a, bytes([b & 255])*c)
            result = a
        else:
            raise RuntimeError(f'Unexpected VM import {address:#x}')
        uc.reg_write(UC_ARM64_REG_X0, result)
        uc.reg_write(UC_ARM64_REG_PC, uc.reg_read(UC_ARM64_REG_LR))

    def decode(self, data):
        if len(data) >= 0x1000000:
            raise ValueError('Input exceeds VM buffer')
        uc = self.uc
        self.heap = 0x67000000
        uc.mem_write(INPUT, data)
        uc.reg_write(UC_ARM64_REG_X0, INPUT)
        uc.reg_write(UC_ARM64_REG_X1, len(data))
        uc.reg_write(UC_ARM64_REG_SP, 0x70080000)
        uc.reg_write(UC_ARM64_REG_LR, STOP)
        uc.emu_start(0x1406580, STOP, count=1000000)
        if uc.reg_read(UC_ARM64_REG_PC) != STOP:
            raise RuntimeError('VM instruction budget exceeded')
        return bytes(uc.mem_read(INPUT, len(data)))


def bundle_blocks(payload):
    stream = io.BytesIO(payload)
    def cstring():
        result = bytearray()
        while True:
            c = stream.read(1)
            if not c:
                raise ValueError('Truncated bundle header')
            if c == b'\0':
                return bytes(result)
            result.extend(c)
    if cstring() != b'UnityFS':
        raise ValueError('Not UnityFS')
    version = struct.unpack('>I', stream.read(4))[0]
    cstring()
    cstring()
    size, compressed, expanded, flags = struct.unpack('>QIII', stream.read(20))
    if size != len(payload):
        raise ValueError('Bundle size mismatch')
    if version >= 7:
        stream.seek((stream.tell()+15)//16*16)
    start = stream.tell()
    if flags & 0x80:
        stream.seek(size-compressed)
    info = stream.read(compressed)
    codec = flags & 0x3f
    if codec in (2, 3):
        info = lz4.block.decompress(info, uncompressed_size=expanded)
    elif codec != 0:
        raise ValueError(f'Unsupported info codec {codec}')
    if flags & 0x80:
        stream.seek(start)
    if flags & 0x200:
        stream.seek((stream.tell()+15)//16*16)
    count = struct.unpack_from('>I', info, 16)[0]
    offset = stream.tell()
    for index in range(count):
        expanded, compressed, flags = struct.unpack_from('>IIH', info, 20+10*index)
        yield offset, compressed, expanded, flags & 0x3f
        offset += compressed


def decrypt_bundle(decoder, payload):
    result = bytearray(payload)
    for offset, size, expanded, codec in bundle_blocks(payload):
        raw = payload[offset:offset+size]
        if b'\xee\xdd' in raw[:74]:
            raw = decoder.decode(raw)
            result[offset:offset+size] = raw
        if codec in (2, 3):
            decoded = lz4.block.decompress(raw, uncompressed_size=expanded)
        elif codec == 0:
            decoded = raw
        else:
            raise ValueError(f'Unsupported block codec {codec}')
        if len(decoded) != expanded:
            raise ValueError('Expanded block size mismatch')
    return bytes(result)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--input', type=Path, default=project_cfg.RAW_GAME_ASSETS_DIR/'data')
    parser.add_argument('--output', type=Path, default=project_cfg.DECRYPTED_DIR)
    parser.add_argument('--filter', default='*.unity3d')
    parser.add_argument('--limit', type=int)
    parser.add_argument('--resume', action='store_true', help='Validate and reuse existing plaintext output')
    parser.add_argument('--report', type=Path, default=project_cfg.REPORTS_DIR/'decryption.json')
    args = parser.parse_args()
    decoder = Decoder()
    args.output.mkdir(parents=True, exist_ok=True)
    rows = []
    paths = sorted(args.input.glob(args.filter))
    if args.limit:
        paths = paths[:args.limit]
    for index, path in enumerate(paths):
        row = {'file': path.name}
        try:
            target = args.output/path.name
            if args.resume and target.exists():
                decoded = decrypt_bundle(decoder, target.read_bytes())
            else:
                decoded = decrypt_bundle(decoder, path.read_bytes())
                target.write_bytes(decoded)
            row['status'] = 'ok'
        except Exception as exc:
            row.update(status='failed', error=f'{type(exc).__name__}: {exc}')
            print(row, flush=True)
        rows.append(row)
        if index % 100 == 0:
            print(f'{index+1}/{len(paths)}', flush=True)
    args.report.write_text(json.dumps(rows, indent=2)+'\n')
    print(f"Decoded {sum(r['status']=='ok' for r in rows)}/{len(rows)}")


if __name__ == '__main__':
    main()
