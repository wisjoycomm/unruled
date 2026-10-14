"""Wireframe storyboard from a continuous LD capture folder (like .temp/ld-ch1).

Input folder layout:
  frames/NNNNN_<ms>.png|jpg   continuous capture (index, ms since capture start)
  actions.log             "<ms> tap X Y label..." one action per line (same clock)
  beats.md                table rows "| # | <first>-<last> (..) | what | details |" (frame index ranges)

Output (<folder>/wire/):
  actions_NN.jpg   every action as BEFORE (tap drawn) -> AFTER (settled result, changed box), beat headers
  beats_NN.jpg     one panel per beat (first frame of its range) with the beat title

Usage: py tools/ld/wire.py <folder> [--per 12] [--beats-per 20]
"""
import argparse
import glob
import os
import re
import sys

from PIL import Image, ImageDraw

sys.path.insert(0, os.path.dirname(__file__))
from ldctl import STABLE_FRAC, CHANGE_FRAC, change, draw_action, font, sig  # noqa: E402

W, H = 180, 320


def load_frames(folder):
    out = []
    for p in sorted(glob.glob(os.path.join(folder, "frames", "*.*"))):
        m = re.match(r"(\d+)_(\d+)\.(png|jpg)$", os.path.basename(p))
        if m:
            out.append((int(m.group(1)), int(m.group(2)), p))
    return out


def load_actions(folder):
    acts = []
    for line in open(os.path.join(folder, "actions.log"), encoding="utf-8"):
        parts = line.split()
        if len(parts) < 2:
            continue
        ms, kind = int(parts[0]), parts[1]
        if kind == "tap" and len(parts) >= 4:
            acts.append({"ms": ms, "kind": "tap", "xy": [int(parts[2]), int(parts[3])], "name": " ".join(parts[4:])})
        elif kind == "swipe" and len(parts) >= 6:
            acts.append({"ms": ms, "kind": "swipe", "xy": [int(v) for v in parts[2:6]], "name": " ".join(parts[6:])})
        else:
            acts.append({"ms": ms, "kind": kind, "xy": [], "name": " ".join(parts[2:])})
    return acts


def load_beats(folder):
    beats = []
    for line in open(os.path.join(folder, "beats.md"), encoding="utf-8"):
        cells = [c.strip() for c in line.strip().strip("|").split("|")]
        if len(cells) < 3 or not cells[0].isdigit():
            continue
        m = re.match(r"(\d+)(?:\s*[–-]\s*(\d+))?", cells[1])
        if not m:
            continue
        a = int(m.group(1))
        beats.append({"n": int(cells[0]), "first": a, "last": int(m.group(2) or a),
                      "title": re.sub(r"\*\*", "", cells[2])})
    return beats


_sig_cache = {}


def fsig(path):
    if path not in _sig_cache:
        _sig_cache[path] = sig(__import__("numpy").asarray(Image.open(path).convert("RGB")))
    return _sig_cache[path]


def before_after(frames, ms):
    """Last frame at/before the action, and the first settled changed frame within 10 s after it."""
    before = None
    k0 = 0
    for k, (_, fms, _) in enumerate(frames):
        if fms <= ms:
            before, k0 = frames[k], k
        else:
            break
    if before is None:
        return None, None, None
    bs = fsig(before[2])
    after, bbox = None, None
    window = [f for f in frames[k0 + 1:] if f[1] <= ms + 10000]
    for i, f in enumerate(window):
        frac, box = change(bs, fsig(f[2]))
        if frac <= CHANGE_FRAC:
            continue
        nxt = window[i + 1] if i + 1 < len(window) else None
        if nxt is None or change(fsig(f[2]), fsig(nxt[2]))[0] <= STABLE_FRAC:
            after, bbox = f, box
            break
        after, bbox = f, box          # keep the latest changed frame if it never settles
    return before, after, bbox


def beat_for(beats, idx):
    hit = [b for b in beats if b["first"] <= idx <= b["last"]]
    if hit:
        return hit[-1]
    prior = [b for b in beats if b["first"] <= idx]
    return prior[-1] if prior else None


def thumb(path):
    if path is None:
        return Image.new("RGB", (450, 800), (40, 40, 40))
    return Image.open(path).convert("RGB").resize((450, 800), Image.LANCZOS)


def actions_pages(folder, out, per):
    frames, acts, beats = load_frames(folder), load_actions(folder), load_beats(folder)
    rows_data = []
    for i, a in enumerate(acts, 1):
        b, af, bbox = before_after(frames, a["ms"])
        a.update(i=i, before=b, after=af, bbox=bbox,
                 result="CHANGED" if af else "NOCHANGE",
                 secs=round((af[1] - a["ms"]) / 1000, 1) if af else "",
                 beat=beat_for(beats, b[0]) if b else None)
        rows_data.append(a)
        print(f"{i:3d} {a['kind']} {a['xy']} {a['name'][:30]:30s} before={b[0] if b else '-'} after={af[0] if af else '-'}",
              flush=True)
    cols, GAP, PAD, HEAD, LAB = 3, 26, 30, 34, 44
    pw = 2 * W + GAP
    f1, f2, f3 = font(13), font(15), font(22)
    pages = []
    for p in range(0, len(rows_data), per):
        chunk = rows_data[p:p + per]
        nrows = (len(chunk) + cols - 1) // cols
        sheet = Image.new("RGB", (PAD + cols * (pw + PAD), 50 + nrows * (HEAD + H + LAB + PAD) + PAD), (18, 20, 26))
        d = ImageDraw.Draw(sheet)
        d.text((PAD, 14), f"{os.path.basename(folder)}  actions {chunk[0]['i']}-{chunk[-1]['i']} of {len(rows_data)}"
                          "   red = tap/swipe   blue = what changed   yellow = beat",
               fill=(230, 230, 230), font=f2)
        for k, a in enumerate(chunk):
            r, c = divmod(k, cols)
            x0 = PAD + c * (pw + PAD)
            y0 = 50 + r * (HEAD + H + LAB + PAD) + HEAD
            bim = draw_action(thumb(a["before"][2] if a["before"] else None), {"kind": a["kind"], "xy": a["xy"]}, 2)
            aim = thumb(a["after"][2] if a["after"] else None)
            if a["bbox"]:
                ImageDraw.Draw(aim).rectangle([v / 2 for v in a["bbox"]], outline=(80, 200, 255), width=3)
            sheet.paste(bim.resize((W, H), Image.LANCZOS), (x0, y0))
            sheet.paste(aim.resize((W, H), Image.LANCZOS), (x0 + W + GAP, y0))
            ya = y0 + H // 2
            d.line([x0 + W + 3, ya, x0 + W + GAP - 6, ya], fill=(255, 90, 90), width=3)
            d.polygon([(x0 + W + GAP - 3, ya), (x0 + W + GAP - 11, ya - 6), (x0 + W + GAP - 11, ya + 6)], fill=(255, 90, 90))
            if a["beat"]:
                d.rectangle([x0, y0 - HEAD + 4, x0 + pw, y0 - 4], fill=(255, 200, 60))
                d.text((x0 + 4, y0 - HEAD + 8), f"B{a['beat']['n']} {a['beat']['title']}"[:52], fill=(0, 0, 0), font=f1)
            d.text((x0, y0 + H + 4), f"{a['i']:02d} {a['kind']} {' '.join(map(str, a['xy']))}  {a['name']}"[:50],
                   fill=(235, 235, 235), font=f1)
            col = (120, 220, 120) if a["result"] == "CHANGED" else (255, 110, 110)
            fb = a["before"][0] if a["before"] else "-"
            fa = a["after"][0] if a["after"] else "-"
            d.text((x0, y0 + H + 22), f"{a['result']} +{a['secs']}s   frames {fb} -> {fa}", fill=col, font=f1)
            d.text((x0 + 2, y0 + 2), "before", fill=(255, 255, 255), font=f1)
            d.text((x0 + W + GAP + 2, y0 + 2), "after", fill=(255, 255, 255), font=f1)
        path = os.path.join(out, f"actions_{p // per + 1:02d}.jpg")
        sheet.save(path, quality=85)
        pages.append(path)
    return pages


def beats_pages(folder, out, per):
    frames, beats = load_frames(folder), load_beats(folder)
    by_idx = {f[0]: f for f in frames}
    cols, PAD, LAB = 5, 18, 58
    f1, f2 = font(13), font(15)
    pages = []
    for p in range(0, len(beats), per):
        chunk = beats[p:p + per]
        nrows = (len(chunk) + cols - 1) // cols
        sheet = Image.new("RGB", (PAD + cols * (W + PAD), 44 + nrows * (H + LAB + PAD) + PAD), (18, 20, 26))
        d = ImageDraw.Draw(sheet)
        d.text((PAD, 12), f"{os.path.basename(folder)}  beats {chunk[0]['n']}-{chunk[-1]['n']}", fill=(230, 230, 230), font=f2)
        for k, b in enumerate(chunk):
            r, c = divmod(k, cols)
            x0, y0 = PAD + c * (W + PAD), 44 + r * (H + LAB + PAD)
            f = by_idx.get(b["first"]) or min(frames, key=lambda fr: abs(fr[0] - b["first"]))
            sheet.paste(thumb(f[2]).resize((W, H), Image.LANCZOS), (x0, y0))
            d.rectangle([x0, y0 + H, x0 + W, y0 + H + LAB - 4], fill=(255, 200, 60))
            words, line, lines = b["title"].split(), "", []
            for w_ in words:
                if len(line) + len(w_) > 24:
                    lines.append(line)
                    line = ""
                line += w_ + " "
            lines.append(line)
            d.text((x0 + 3, y0 + H + 2), f"B{b['n']}  fr {b['first']}-{b['last']}", fill=(60, 30, 0), font=f1)
            for j, ln in enumerate(lines[:2]):
                d.text((x0 + 3, y0 + H + 18 + j * 16), ln, fill=(0, 0, 0), font=f1)
        path = os.path.join(out, f"beats_{p // per + 1:02d}.jpg")
        sheet.save(path, quality=85)
        pages.append(path)
    return pages


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("folder")
    ap.add_argument("--per", type=int, default=12)
    ap.add_argument("--beats-per", type=int, default=20)
    o = ap.parse_args()
    out = os.path.join(o.folder, "wire")
    os.makedirs(out, exist_ok=True)
    for p in actions_pages(o.folder, out, o.per) + beats_pages(o.folder, out, o.beats_per):
        print(p)


if __name__ == "__main__":
    main()
