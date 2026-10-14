import hashlib
import io
import json
import struct
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import download_assets as tool


class Response(io.BytesIO):
    def __init__(self, payload, status=200, headers=None):
        super().__init__(payload)
        self.status = status
        self.headers = headers or {}


class DownloadTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.payload = b"UnityFS\0verified-test-payload"
        self.row = {"path": "data/test.unity3d", "size": len(self.payload),
                    "md5": hashlib.md5(self.payload).hexdigest()}
        self.target = self.root / self.row["path"]
        self.target.parent.mkdir()
        self.partial = self.target.with_name(self.target.name + ".part")

    def test_full_download_publishes_only_verified_bytes(self):
        with patch.object(tool.urllib.request, "urlopen", return_value=Response(self.payload)):
            result = tool.fetch(self.row, "https://example.test/assets", self.root, retries=1)
        self.assertEqual(result["status"], "downloaded_verified")
        self.assertEqual(self.target.read_bytes(), self.payload)
        self.assertFalse(self.partial.exists())

    def test_resume_valid_range(self):
        self.partial.write_bytes(self.payload[:8])
        response = Response(self.payload[8:], 206, {"Content-Range": f"bytes 8-{len(self.payload)-1}/{len(self.payload)}"})
        with patch.object(tool.urllib.request, "urlopen", return_value=response) as request:
            result = tool.fetch(self.row, "https://example.test", self.root, retries=1)
        self.assertEqual(request.call_args.args[0].get_header("Range"), "bytes=8-")
        self.assertEqual(result["status"], "downloaded_verified")
        self.assertEqual(self.target.read_bytes(), self.payload)

    def test_server_ignores_range_restarts_file(self):
        self.partial.write_bytes(self.payload[:8])
        with patch.object(tool.urllib.request, "urlopen", return_value=Response(self.payload)):
            result = tool.fetch(self.row, "https://example.test", self.root, retries=1)
        self.assertEqual(result["status"], "downloaded_verified")
        self.assertEqual(self.target.read_bytes(), self.payload)

    def test_wrong_hash_never_replaces_existing_file(self):
        self.target.write_bytes(b"existing file")
        response = Response(b"X"*len(self.payload))
        with patch.object(tool.urllib.request, "urlopen", return_value=response):
            result = tool.fetch(self.row, "https://example.test", self.root, retries=1)
        self.assertEqual(result["status"], "failed")
        self.assertEqual(self.target.read_bytes(), b"existing file")

    def test_wrong_range_is_rejected(self):
        self.partial.write_bytes(self.payload[:8])
        response = Response(self.payload[8:], 206, {"Content-Range": f"bytes 0-{len(self.payload)-1}/{len(self.payload)}"})
        with patch.object(tool.urllib.request, "urlopen", return_value=response):
            result = tool.fetch(self.row, "https://example.test", self.root, retries=1)
        self.assertEqual(result["status"], "failed")
        self.assertFalse(self.target.exists())

    def test_manifest_rejects_path_traversal(self):
        data = bytearray(28)
        def u32(value): data.extend(struct.pack("<I", value))
        def string(value):
            raw = value.encode()
            u32(len(raw)); data.extend(raw)
            data.extend(b"\0"*((-len(data))%4))
        string("version"); u32(1); string("data"); u32(1)
        string("../escape"); string(".unity3d"); string("0"*32)
        for value in (100,0,0,0): u32(value)
        with self.assertRaisesRegex(ValueError, "Unsafe asset name"):
            tool.parse_manifest(bytes(data))

    def test_saved_manifest_loads_without_apk(self):
        row = {"group":"data", "name":"test", "extension":".unity3d", "md5":"0"*32,
               "path":"data/test_"+"0"*32+".unity3d", "size":100}
        path = self.root / "manifest.json"
        path.write_text(json.dumps({"assets":[row]}))
        self.assertEqual(tool.read_saved_manifest(path)["assets"], [row])

    def test_saved_manifest_rejects_path_traversal_and_duplicates(self):
        row = {"group":"data", "name":"test", "extension":".unity3d", "md5":"0"*32,
               "path":"../../escape", "size":100}
        path = self.root / "manifest.json"
        path.write_text(json.dumps({"assets":[row]}))
        with self.assertRaises(ValueError): tool.read_saved_manifest(path)
        row["path"] = "data/test_"+"0"*32+".unity3d"
        path.write_text(json.dumps({"assets":[row,row]}))
        with self.assertRaises(ValueError): tool.read_saved_manifest(path)


if __name__ == "__main__":
    unittest.main()
