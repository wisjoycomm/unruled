import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import unity_yaml  # noqa: E402

SAMPLE_PREFAB = """%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!1 &100
GameObject:
  m_Name: HeroRoot
  m_IsActive: 1
--- !u!224 &200
RectTransform:
  m_GameObject: {fileID: 100}
  m_AnchoredPosition: {x: 12.5, y: -34}
  m_Color: {r: 1, g: 0.5, b: 0, a: 1}
"""

SAMPLE_META = """fileFormatVersion: 2
guid: a1b2c3d4e5f60718293a4b5c6d7e8f90
TextureImporter:
  internalIDToNameTable:
  - first:
      213: 21300000
    second: common_btn_001
  spriteSheet:
    sprites:
    - serializedVersion: 2
      name: common_btn_001
      rect:
        serializedVersion: 2
        x: 10
        y: 20
        width: 100
        height: 50
      alignment: 0
      pivot: {x: 0.5, y: 0.5}
      border: {x: 12, y: 14, z: 16, w: 18}
"""


class UnityYamlTests(unittest.TestCase):
    def test_parse_yaml_docs_and_helpers(self):
        docs = unity_yaml.parse_yaml_docs(SAMPLE_PREFAB, int_class=True)
        self.assertEqual(set(docs.keys()), {"100", "200"})
        self.assertEqual(docs["100"][0], 1)
        self.assertEqual(unity_yaml.yaml_field(docs["100"][1], "m_Name"), "HeroRoot")
        self.assertEqual(
            unity_yaml.yaml_vec2(docs["200"][1], "m_AnchoredPosition"), (12.5, -34.0)
        )
        self.assertEqual(unity_yaml.yaml_color_hex(docs["200"][1], "m_Color"), "#FF8000/1.00")

    def test_parse_and_index_atlas_meta(self):
        with tempfile.TemporaryDirectory() as tmp:
            meta = Path(tmp) / "test_atlas.png.meta"
            meta.write_text(SAMPLE_META, encoding="utf-8")
            guid, fid_map, sprites = unity_yaml.parse_atlas_meta(meta)
            self.assertEqual(guid, "a1b2c3d4e5f60718293a4b5c6d7e8f90")
            self.assertEqual(fid_map["21300000"], "common_btn_001")
            self.assertEqual(
                sprites["common_btn_001"],
                (10.0, 20.0, 100.0, 50.0, 12.0, 14.0, 16.0, 18.0),
            )
            idx = unity_yaml.index_atlas_sprites([tmp])
            self.assertIn("common_btn_001", idx)
            self.assertEqual(idx["common_btn_001"][1:], (10.0, 20.0, 100.0, 50.0, 12.0, 14.0, 16.0, 18.0))


if __name__ == "__main__":
    unittest.main()
