"""Print a readable summary of Data/Cutscenes/cutscenes_ftue.json."""
import json
import os

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
PATH = os.path.join(ROOT, "UnityAssets", "Assets", "_EndlessWinter", "Data", "Cutscenes", "cutscenes_ftue.json")

data = json.load(open(PATH, encoding="utf-8"))["cutscenes"]
for cut in data:
    print("==", cut["name"], "length", cut["length"])
    for t in cut["tracks"]:
        states = [(s["start"], s["state"]) for s in t.get("states", [])]
        print("  ", t["target"], "|", t.get("transformClip", ""), "| states", states, t.get("clips", ""), t.get("controls", ""))
    for e in cut["events"]:
        print("   ev", e)
