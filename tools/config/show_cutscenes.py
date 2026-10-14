"""Print a readable summary of Data/Cutscenes/cutscenes_ftue.json."""
import json
import os
import sys

sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))
import project_cfg  # noqa: E402

ROOT = str(project_cfg.ROOT)
PATH = str(project_cfg.DATA_DIR / "Cutscenes" / "cutscenes_ftue.json")

data = json.load(open(PATH, encoding="utf-8"))["cutscenes"]
for cut in data:
    print("==", cut["name"], "length", cut["length"])
    for t in cut["tracks"]:
        states = [(s["start"], s["state"]) for s in t.get("states", [])]
        print("  ", t["target"], "|", t.get("transformClip", ""), "| states", states, t.get("clips", ""), t.get("controls", ""))
    for e in cut["events"]:
        print("   ev", e)
