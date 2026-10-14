#!/usr/bin/env python3
"""Shared UnityFS bundle header & LZ4 block-table parser.

Used by `scan_assets.py` and `decrypt_bundles.py` to avoid duplicating binary
stream parsing and block decompression logic.
"""
from __future__ import annotations

import io
import struct
from dataclasses import dataclass
from typing import BinaryIO

import lz4.block


@dataclass(frozen=True)
class UnityFSBlock:
    offset: int
    compressed: int
    expanded: int
    flags: int

    @property
    def codec(self) -> int:
        return self.flags & 0x3F


@dataclass(frozen=True)
class UnityFSHeader:
    format_version: int
    player_version: str
    engine_version: str
    bundle_size: int
    compressed_info_size: int
    expanded_info_size: int
    flags: int
    blocks: list[UnityFSBlock]


def _read_cstring(stream: BinaryIO) -> str:
    buf = bytearray()
    while True:
        c = stream.read(1)
        if not c:
            raise ValueError("Truncated UnityFS string")
        if c == b"\0":
            return buf.decode("utf-8")
        buf.extend(c)


def decompress_block(raw: bytes, expanded: int, codec: int) -> bytes:
    """Decompress a UnityFS info table or data block using its 6-bit codec ID."""
    if codec in (2, 3):
        data = lz4.block.decompress(raw, uncompressed_size=expanded)
    elif codec == 0:
        data = raw
    else:
        raise ValueError(f"Unsupported UnityFS codec {codec}")
    if len(data) != expanded:
        raise ValueError(f"UnityFS block length mismatch ({len(data)} != {expanded})")
    return data


def parse_unityfs_header(source: bytes | BinaryIO, total_bytes: int | None = None) -> UnityFSHeader:
    """Parse a UnityFS header and its compressed block-info table."""
    if isinstance(source, (bytes, bytearray, memoryview)):
        total_bytes = len(source)
        stream: BinaryIO = io.BytesIO(source)
    else:
        stream = source
        if total_bytes is None:
            pos = stream.tell()
            stream.seek(0, io.SEEK_END)
            total_bytes = stream.tell()
            stream.seek(pos)

    if _read_cstring(stream) != "UnityFS":
        raise ValueError("Not UnityFS")
    version = struct.unpack(">I", stream.read(4))[0]
    player_version = _read_cstring(stream)
    engine_version = _read_cstring(stream)
    size, compressed, expanded, flags = struct.unpack(">QIII", stream.read(20))
    if size != total_bytes:
        raise ValueError("Bundle length mismatch")
    if version >= 7:
        stream.seek((stream.tell() + 15) // 16 * 16)
    data_start = stream.tell()
    if flags & 0x80:
        stream.seek(size - compressed)
    raw_info = stream.read(compressed)
    info = decompress_block(raw_info, expanded, flags & 0x3F)

    if flags & 0x80:
        stream.seek(data_start)
    if flags & 0x200:
        stream.seek((stream.tell() + 15) // 16 * 16)

    block_count = struct.unpack_from(">I", info, 16)[0]
    offset = stream.tell()
    blocks: list[UnityFSBlock] = []
    for index in range(block_count):
        blk_expanded, blk_compressed, blk_flags = struct.unpack_from(">IIH", info, 20 + 10 * index)
        blocks.append(UnityFSBlock(offset, blk_compressed, blk_expanded, blk_flags))
        offset += blk_compressed

    return UnityFSHeader(
        format_version=version,
        player_version=player_version,
        engine_version=engine_version,
        bundle_size=size,
        compressed_info_size=compressed,
        expanded_info_size=expanded,
        flags=flags,
        blocks=blocks,
    )
