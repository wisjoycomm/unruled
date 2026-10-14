#!/usr/bin/env python3
"""Extract the English localization table (game_languages_local_en.bytes) to JSON.

The table is a compiled LuaJIT chunk that builds one big `{ key = "value", ... }` table.
Keys and values both live in the chunk's string constant section, laid out as
alternating key/value pairs in source order, so we can recover the mapping by walking
the LuaJIT string constants in order.

Run: python tools/config/extract_i18n.py [--lang en]
"""
import argparse
import json
import os
import re
import sys

sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))
import project_cfg  # noqa: E402

ROOT = str(project_cfg.ROOT)
SRC = str(project_cfg.RESOURCES_EXPORT_DIR / "Lua" / "64")
OUT = str(project_cfg.RESOURCES_CONFIG_DIR / "i18n")

# A localization key: lowercase/digits/underscore, e.g. hero_story_50000_1, hero_ui_34, hero_btn_07.
KEY_RE = re.compile(r"^[a-z][a-z0-9_]{2,}$")


def read_uleb(buf, i):
    result = 0
    shift = 0
    while True:
        b = buf[i]
        i += 1
        result |= (b & 0x7F) << shift
        if not (b & 0x80):
            return result, i
        shift += 7


def strings_in_order(data):
    """Yield every string constant in the chunk, in the order LuaJIT stored them.

    LuaJIT writes string constants as <uleb length+5><bytes> inside each prototype's
    constant table. Rather than fully parsing the bytecode (which varies by build flags),
    scan for the length-prefixed pattern and validate that the payload is clean text.
    """
    out = []
    i = 0
    n = len(data)
    while i < n:
        try:
            ln, j = read_uleb(data, i)
        except IndexError:
            break
        if ln >= 5:
            size = ln - 5
            if 0 < size <= 20000 and j + size <= n:
                chunk = data[j:j + size]
                # Accept printable ASCII + UTF-8 text; reject anything with control bytes.
                if not any(b < 0x09 or 0x0E <= b < 0x20 for b in chunk):
                    try:
                        out.append(chunk.decode("utf-8"))
                        i = j + size
                        continue
                    except UnicodeDecodeError:
                        pass
        i += 1
    return out


def pair_up(items):
    """Walk the constant stream pairing each localization key with the string that follows it."""
    table = {}
    k = 0
    while k < len(items) - 1:
        key = items[k]
        if KEY_RE.match(key):
            value = items[k + 1]
            # The value must not itself look like a bare key, unless it is genuinely short text.
            if not (KEY_RE.match(value) and "_" in value and " " not in value):
                table[key] = value
                k += 2
                continue
        k += 1
    return table


def dump_printable_strings(paths):
    """Dump printable ASCII runs (>=4 chars) from compiled LuaJIT/binary files."""
    for p in paths:
        data = open(p, "rb").read()
        print(f"===== {os.path.basename(p)}  ({len(data)} bytes)")
        for m in re.finditer(rb"[\x20-\x7e]{4,}", data):
            print(m.group().decode("ascii"))
        print()


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--lang", default="en", help="Language code for game_languages_local_<lang>.bytes")
    ap.add_argument("--input", default=None, help="Explicit path to a LuaJIT .bytes file")
    ap.add_argument("--lookup", nargs="+", help="Look up specific i18n keys")
    ap.add_argument("--reverse", default=None, help="Reverse-search i18n keys whose value contains phrase")
    ap.add_argument("--strings", nargs="+", help="Dump printable ASCII strings from one or more .bytes files")
    args = ap.parse_args()

    if args.strings:
        dump_printable_strings(args.strings)
        return

    dest = os.path.join(OUT, f"{args.lang}.json")
    path = args.input or os.path.join(SRC, f"game_languages_local_{args.lang}.bytes")

    if (args.lookup or args.reverse) and os.path.exists(dest) and not args.input:
        table = json.load(open(dest, encoding="utf-8"))
    else:
        data = open(path, "rb").read()
        items = strings_in_order(data)
        table = pair_up(items)
        if not (args.lookup or args.reverse):
            os.makedirs(OUT, exist_ok=True)
            with open(dest, "w", encoding="utf-8") as fh:
                json.dump(table, fh, indent=1, ensure_ascii=False, sort_keys=True)
            print(f"[extract_i18n] {len(items)} constants -> {len(table)} keys -> {dest}")
            for probe in list(table.keys())[:4]:
                v = table[probe]
                print(f"  {probe} = {(v[:90] + '...') if v and len(v) > 90 else v}")
            return

    if args.lookup:
        for key in args.lookup:
            print(f"{key} = {table.get(key, '(not found)')}")
    if args.reverse:
        q = args.reverse.lower()
        for k, v in table.items():
            if q in v.lower():
                print(f"{k} = {v}")


if __name__ == "__main__":
    main()

