"""Unit tests for tools/config/_common.py, tools/ld/_img.py, and tools/arm64_vm.py."""
import tempfile
import unittest
from pathlib import Path
from unittest import mock

from PIL import Image

from arm64_vm import LEGACY_STUB_SYMBOLS, read_elf_import_stubs
from config import _common
from ld import _img


class TestCommonHelpers(unittest.TestCase):
    def test_write_and_load_lua_json(self):
        with tempfile.TemporaryDirectory() as tmp:
            tmp_dir = Path(tmp)
            with mock.patch.object(_common, "LUA_CONFIG_DIR", tmp_dir):
                _common.write_json(tmp_dir / "game_config_default_sample.json", {"id": 42})
                self.assertEqual(_common.load_lua_json("sample"), {"id": 42})
                self.assertEqual(_common.load_lua_json("game_config_default_sample.json"), {"id": 42})

    def test_i18n_text_with_pack_arg(self):
        with mock.patch.dict(_common._I18N_CACHE, {"en": {"pack_name": "Wood x %<1>", "plain": "Hello"}}, clear=True):
            self.assertEqual(_common.i18n_text("pack_name||100"), "Wood x 100")
            self.assertEqual(_common.i18n_text("plain"), "Hello")
            with self.assertRaises(KeyError):
                _common.i18n_text("missing_key", strict=True)
            self.assertEqual(_common.i18n_text("missing_key", strict=False), "missing_key")

    def test_bright_bbox_and_load_cap(self):
        with tempfile.TemporaryDirectory() as tmp:
            img_path = Path(tmp) / "test_cap.png"
            im = Image.new("RGB", (20, 20), (10, 10, 10))
            for y in range(5, 11):
                for x in range(4, 15):
                    im.putpixel((x, y), (230, 240, 250))
            im.save(img_path)

            _, px, w, h = _img.load_cap(str(img_path))
            self.assertEqual((w, h), (20, 20))
            self.assertEqual(_img.bright_bbox(px, w, h, thresh=200), (4, 14, 5, 10))
            self.assertIsNone(_img.bright_bbox(px, w, h, thresh=255))

    def test_arm64_vm_import_stubs_fallback(self):
        stubs = read_elf_import_stubs(Path("/nonexistent/libunity.so"))
        self.assertEqual(stubs, LEGACY_STUB_SYMBOLS)
        self.assertEqual(stubs[0x50000050], "memcpy")


if __name__ == "__main__":
    unittest.main()
