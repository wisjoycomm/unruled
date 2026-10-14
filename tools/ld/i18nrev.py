"""Reverse-lookup: find i18n keys whose English value contains a phrase.

Usage: python tools/ld/i18nrev.py "Hero Overall Stats"
"""
import os
import re
import sys

PATH = os.path.abspath(os.path.join(
    os.path.dirname(__file__), "..", "..",
    "AllAssetsExport", "ExportedProject", "Assets", "TextAsset",
    "game_languages_local_en.bytes"))


def clean(b):
    return "".join(chr(c) if 32 <= c < 127 else "\n" for c in b)


def main():
    phrase = sys.argv[1]
    data = open(PATH, "rb").read()
    text = clean(data)
    for m in re.finditer(re.escape(phrase), text, re.I):
        s = max(0, m.start() - 160)
        window = text[s:m.end() + 60]
        keys = re.findall(r"[a-z][a-z0-9_]{5,}", window)
        print(f"@{m.start()}  ...{window[-220:]!r}")
        print("   candidate keys:", keys[-6:])
        print()


if __name__ == "__main__":
    main()
