"""Grep every LuaJSON config for a substring, printing file + first matching row."""
import glob
import json
import os
import sys

CFG = os.path.join("ResourcesExport", "Config", "LuaJSON")
needles = [n.lower() for n in sys.argv[1:]]
for p in sorted(glob.glob(os.path.join(CFG, "*.json"))):
    try:
        text = open(p, encoding="utf-8").read()
    except Exception:
        continue
    low = text.lower()
    if all(n in low for n in needles):
        try:
            d = json.load(open(p, encoding="utf-8"))
        except Exception:
            print(os.path.basename(p), "(unparsed)")
            continue
        rows = list(d.items())[:2] if isinstance(d, dict) else d[:2]
        print("==", os.path.basename(p))
        for r in rows:
            print("   ", json.dumps(r, ensure_ascii=False)[:300])
