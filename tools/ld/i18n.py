"""Look up i18n keys in the game's English string table.

Usage: python tools/ld/i18n.py hero_ui_33 hero_ui_34 ...
The table is a flat LuaJIT blob with length-prefixed strings, so a key and its
value often land inside the same printable run; we search the raw bytes and
print the surrounding window.
"""
import os
import re
import sys

PATH = os.path.abspath(os.path.join(
    os.path.dirname(__file__), "..", "..",
    "AllAssetsExport", "ExportedProject", "Assets", "TextAsset",
    "game_languages_local_en.bytes"))


def clean(b):
    return "".join(chr(c) if 32 <= c < 127 else "." for c in b)


def main():
    data = open(PATH, "rb").read()
    for key in sys.argv[1:]:
        pat = key.encode()
        print(f"===== {key}")
        found = False
        for m in re.finditer(re.escape(pat), data):
            s = m.start()
            # Guard against hero_ui_3 matching inside hero_ui_33.
            nxt = data[m.end():m.end() + 1]
            if nxt.isdigit():
                continue
            found = True
            print("  @", s, repr(clean(data[s: s + 120])))
            break
        if not found:
            print("  (not found)")


if __name__ == "__main__":
    main()
