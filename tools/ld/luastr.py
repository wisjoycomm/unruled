"""Dump printable strings out of the game's compiled LuaJIT view modules.

Usage: python tools/ld/luastr.py <name> [...]   (name = file stem under Assets/TextAsset,
with or without the game_module_ui_view_ prefix)
"""
import os
import re
import sys

ROOT = os.path.abspath(
    os.path.join(
        os.path.dirname(__file__), "..", "..",
        "AllAssetsExport", "ExportedProject", "Assets", "TextAsset",
    )
)


def resolve(name):
    for cand in (name, "game_module_ui_view_" + name, "game_config_default_" + name):
        p = os.path.join(ROOT, cand + ".bytes")
        if os.path.exists(p):
            return p
    raise SystemExit(f"not found: {name}")


def main():
    for name in sys.argv[1:]:
        path = resolve(name)
        data = open(path, "rb").read()
        print(f"===== {os.path.basename(path)}  ({len(data)} bytes)")
        for m in re.finditer(rb"[\x20-\x7e]{4,}", data):
            print(m.group().decode("ascii"))
        print()


if __name__ == "__main__":
    main()
