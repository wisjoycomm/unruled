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

    def test_downsampled_1080p_and_at2_preview(self):
        import numpy as np
        with tempfile.TemporaryDirectory() as tmp:
            # 1) 1080x1920 image with a white box at x=200..399, y=400..599
            arr_full = np.full((1920, 1080, 3), 10, dtype=np.uint8)
            arr_full[400:600, 200:400] = 240
            full_path = Path(tmp) / "full_1080p.png"
            Image.fromarray(arr_full).save(full_path)

            arr_small, rw, rh, scale = _img.load_cap_np(str(full_path))
            self.assertEqual((rw, rh, scale), (1080, 1920, 2))
            self.assertEqual(arr_small.shape, (960, 540, 3))
            box = _img.bright_bbox(arr_small, rw, rh, thresh=200, scale=scale)
            self.assertEqual(box, (200, 399, 400, 599))

            # 2) @2.png preview (450x800 -> real 900x1600, scale=2)
            at2_path = Path(tmp) / "0001_test@2.png"
            Image.fromarray(arr_small[:800, :450]).save(at2_path)
            _, rw2, rh2, scale2 = _img.load_cap_np(str(at2_path))
            self.assertEqual((rw2, rh2, scale2), (900, 1600, 2))

    def test_arm64_vm_import_stubs_fallback(self):
        stubs = read_elf_import_stubs(Path("/nonexistent/libunity.so"))
        self.assertEqual(stubs, LEGACY_STUB_SYMBOLS)
        self.assertEqual(stubs[0x50000050], "memcpy")


if __name__ == "__main__":
    unittest.main()
