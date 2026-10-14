import struct
import sys
import unittest
from pathlib import Path

import lz4.block

sys.path.insert(0, str(Path(__file__).resolve().parent))
import decrypt_bundles  # noqa: E402
import unityfs  # noqa: E402


def _make_fake_unityfs(block_payload: bytes = b"hello-unityfs-block-data" * 4) -> bytes:
    comp_block = lz4.block.compress(block_payload, store_size=False)
    info = bytearray(16)
    info.extend(struct.pack(">I", 1))
    info.extend(struct.pack(">IIH", len(block_payload), len(comp_block), 3))
    comp_info = lz4.block.compress(bytes(info), store_size=False)

    head = bytearray()
    head.extend(b"UnityFS\0")
    head.extend(struct.pack(">I", 7))
    head.extend(b"5.x.x\0")
    head.extend(b"2021.3.15f1\0")
    # Placeholder for size, compressed, expanded, flags
    meta_pos = len(head)
    head.extend(b"\0" * 20)
    while len(head) % 16 != 0:
        head.append(0)
    head.extend(comp_info)
    head.extend(comp_block)
    total_size = len(head)
    head[meta_pos : meta_pos + 20] = struct.pack(
        ">QIII", total_size, len(comp_info), len(info), 3
    )
    return bytes(head)


class UnityFSTests(unittest.TestCase):
    def test_parse_unityfs_header_and_blocks(self):
        raw_payload = b"hello-unityfs-block-data" * 4
        bundle = _make_fake_unityfs(raw_payload)
        header = unityfs.parse_unityfs_header(bundle)
        self.assertEqual(header.format_version, 7)
        self.assertEqual(header.engine_version, "2021.3.15f1")
        self.assertEqual(len(header.blocks), 1)
        blk = header.blocks[0]
        self.assertEqual(blk.expanded, len(raw_payload))
        self.assertEqual(blk.codec, 3)
        decompressed = unityfs.decompress_block(
            bundle[blk.offset : blk.offset + blk.compressed], blk.expanded, blk.codec
        )
        self.assertEqual(decompressed, raw_payload)

    def test_decrypt_bundles_bundle_blocks_uses_unityfs(self):
        bundle = _make_fake_unityfs()
        blocks = list(decrypt_bundles.bundle_blocks(bundle))
        self.assertEqual(len(blocks), 1)
        self.assertEqual(blocks[0][3], 3)


if __name__ == "__main__":
    unittest.main()
