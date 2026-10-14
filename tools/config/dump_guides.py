"""Flatten the original guide behaviour trees into an ordered, readable step list.

Usage: python dump_guides.py Guide_Plot_StoryLine_1 Guide_SnowStormFirst_1 ...
Each NpcDialogList id is expanded through new_player_plot (and its nextIndex chain) to the
English line, so the list reads like the recorded story.
"""
import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
LUA = os.path.join(ROOT, "ResourcesExport", "Config", "LuaJSON")
LANG = os.path.join(ROOT, "ResourcesExport", "Lua", "32", "game_languages_local_en.bytes")
KEYS = ["ScenarioName", "Id", "FunctionName", "ParamName", "MsgId", "WaitTime", "ConstructionBID",
        "CustomPositon", "ViewID", "BtnName", "TransformParam", "HelperParam"]

_text = open(LANG, "rb").read()
PLOT = json.load(open(os.path.join(LUA, "game_config_default_new_player_plot.json"), encoding="utf-8"))


def _uleb(pos):
    value, shift = 0, 0
    while True:
        b = _text[pos]
        value |= (b & 0x7F) << shift
        shift += 7
        pos += 1
        if b < 0x80:
            return value, pos


def tr(key):
    """LuaJIT bytecode string constants: uleb128(len + 5) then the bytes; the value follows the key."""
    k = key.encode()
    head = bytes([len(k) + 5]) + k
    i = _text.find(head)
    if i < 0:
        return key
    length, start = _uleb(i + len(head))
    return _text[start:start + length - 5].decode("utf-8", "ignore")


def chain(pid):
    """Plot ids played by one NpcDialogList: a 'nextIndex' key continues (to the listed id, else id+1)."""
    ids = []
    while pid and str(pid) in PLOT and pid not in ids:
        ids.append(pid)
        e = PLOT[str(pid)]
        if "nextIndex" not in e:
            break
        nxt = e["nextIndex"]
        pid = nxt[0] if isinstance(nxt, list) and nxt and nxt[0] > 1000 else pid + 1
    return ids


def dialog(pid):
    out = []
    for i in chain(pid):
        e = PLOT[str(i)]
        lines = e.get("content") or []
        if isinstance(lines, dict):
            lines = []
        side = "R" if e.get("pos_type") == 2 else "L"
        for key in lines:
            out.append(f"      [{i} spk{e.get('speakerId', '-')} {side}] {tr(key)}")
        if e.get("type") == 1:
            out.append(f"      [{i} CHOICE] {tr(e.get('title', ''))} icons={e.get('icon')}")
    return out


def walk(n, depth, out):
    p = n.get("param", {}) or {}
    cls = n.get("cls", "").replace("Guide_", "")
    info = " ".join(f"{k}={p[k]}" for k in KEYS if p.get(k) not in (None, "", 0, False))
    pre = n.get("precondition")
    cond = ""
    if pre:
        def c(x):
            if "childs" in x:
                return x["cls"].replace("BTreeNodePrecondition", "") + "(" + ",".join(c(y) for y in x["childs"]) + ")"
            q = x.get("param", {})
            vals = "/".join(str(q[k]) for k in ("HelperFuncName", "HelperParam", "ViewID", "SceneName", "IsNot", "MsgId") if q.get(k) not in (None, "", False))
            return x.get("cls", "").replace("Guide_", "").replace("_Condition", "") + ":" + vals
        cond = "  IF " + c(pre)
    if cls.startswith("BTree") and not p.get("node_name"):
        label = ""
    else:
        label = f"- {p.get('node_name', '')} [{cls}] {info}{cond}"
    if label:
        out.append("  " * depth + label)
    if cls == "NpcDialogList_Action" and p.get("Id"):
        out.extend(dialog(int(p["Id"])))
    for ch in n.get("childs", []):
        walk(ch, depth + (1 if label else 0), out)


def main(names):
    for name in names:
        path = os.path.join(LUA, "game_config_btree_guide_" + name + ".json")
        out = []
        walk(json.load(open(path, encoding="utf-8")), 0, out)
        print("########## " + name)
        print("\n".join(out))


if __name__ == "__main__":
    main(sys.argv[1:])
