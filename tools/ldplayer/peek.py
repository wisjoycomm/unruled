import json, os, sys
sys.stdout.reconfigure(encoding="utf-8")
D = r"D:\UnityProjects\endlesswinter\ResourcesExport\Config\LuaJSON"
name = sys.argv[1]
keys = sys.argv[2:]
data = json.load(open(os.path.join(D, name), encoding="utf-8"))
if isinstance(data, list):
    print("list", len(data))
    for x in data[:6]:
        print(json.dumps(x)[:700])
else:
    print("dict", len(data), "keys sample", list(data.keys())[:15])
    for k in (keys or list(data.keys())[:6]):
        print(k, json.dumps(data.get(k))[:900])
