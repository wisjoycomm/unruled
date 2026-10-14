"""ldctl - fast LDPlayer driver: quick actions, smart capture, action log, storyboard.

Every action is logged to the current session (.temp/ld-captures/<session>/):
  frames/NNNN_<name>@2.jpg   half-size previews (exact 9:16, real = preview * 2)
  frames/NNNN_<name>.png     full-res frame (only with --full / shot --full)
  log.jsonl                  one JSON line per action / beat (machine readable)
  log.md                     same, human readable
Coordinates in the log are always REAL device pixels (900x1600).

Usage (py tools/ld/ldctl.py <cmd> ...):
  session [name]                      show / switch the current session (stored in .temp/ld-captures/.session)
  up                                  start LDPlayer + game if adb sees no device
  shot [name] [--full]                capture now
  tap X Y [name] [--s 2] [--timeout 3] [--max 20]
                                      tap, wait until the screen changed AND settled (not a fixed sleep)
                                      X Y may be a point name from points.json; --s 2 = coords from a @2 preview
  swipe X1 Y1 X2 Y2 [ms] [name] [--s 2]
  back [name] | home | text "..."     key / text input (back also waits for settle)
  advance POINT [--n 30] [--s 2]     keep tapping POINT while it changes the screen; stop on the first NOCHANGE
  beat "text"                         mark a story beat (milestone) at the current frame
  note "text"                         free note in the log (no capture)
  watch [--interval 1] [--stall 20] [--min-change 0.03] [--on-stall notify|shot|back|tap:POINT|exit]
                                      one stdout line per CHANGE / STALL (built for Claude's Monitor tool)
  sheet [--from N] [--to N] [--cols 6]    contact sheet of the session frames, labelled
  story [--from N] [--to N] [--cols 4]    wireframe storyboard: each frame with its action drawn on it
                                          (tap ring + number, swipe arrow), arrows between panels, beats as headers
  grid [name]                         latest (or named) preview with a real-coordinate grid, for picking taps
  where PX PY [--s 2]                 convert preview coords -> real coords
  points                              list named points
"""
import argparse
import json
import os
import shutil
import subprocess
import sys
import time

import numpy as np
from PIL import Image, ImageDraw, ImageFont

sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))
import project_cfg  # noqa: E402

ROOT = str(project_cfg.ROOT)
CAP_ROOT = str(project_cfg.LD_CAPTURES_DIR)
POINTS_FILE = os.path.join(os.path.dirname(__file__), "points.json")
LD_DIR = str(project_cfg.LD_DIR)
ADB = project_cfg.ADB_PATH
LDCONSOLE = project_cfg.LDCONSOLE_PATH
PACKAGE = project_cfg.REF_GAME_PACKAGE
ACTIVITY = project_cfg.REF_GAME_ACTIVITY
PREVIEW_SCALE = 2
MAX_WAIT = 60.0           # hard cap on any wait, seconds

# change detection: compare 45x80 grayscale thumbnails (1 cell = 20x20 real px)
THUMB = (45, 80)
CELL_DIFF = 10            # mean abs diff (0-255) for a cell to count as changed
CHANGE_FRAC = 0.004       # fraction of cells (~14 of 3600) => the screen "changed"
STABLE_FRAC = 0.03        # frame-to-frame change below this = "settled" (idle anims, timers keep ticking)


# ---------------------------------------------------------------- adb
_serial = None


def serial():
    global _serial
    if _serial:
        return _serial
    out = subprocess.run([ADB, "devices"], capture_output=True, text=True).stdout
    devs = [l.split()[0] for l in out.splitlines()[1:] if l.strip().endswith("device")]
    if not devs:
        raise SystemExit("no adb device - run: ldctl up")
    _serial = os.environ.get("LD_SERIAL", devs[0])
    return _serial


def adb(*args, binary=False, check=True):
    res = subprocess.run([ADB, "-s", serial()] + [str(a) for a in args], capture_output=True)
    if check and res.returncode != 0:
        raise SystemExit("adb failed: " + res.stderr.decode(errors="replace"))
    return res.stdout if binary else res.stdout.decode(errors="replace")


def grab(retries=3):
    """Raw framebuffer -> HxWx3 uint8 (about 0.4s, no PNG encode on device)."""
    for k in range(retries):
        data = adb("exec-out", "screencap", binary=True, check=k == retries - 1)
        if len(data) > 16:
            break
        time.sleep(0.3)
    w, h = np.frombuffer(data[:8], dtype="<u4")
    px = w * h * 4
    hdr = len(data) - px          # 12 (old) or 16 (Android 11+) bytes
    arr = np.frombuffer(data[hdr:hdr + px], dtype=np.uint8).reshape(h, w, 4)
    return arr[:, :, :3].copy()


def sig(frame):
    return np.asarray(Image.fromarray(frame).convert("L").resize(THUMB, Image.BILINEAR), dtype=np.int16)


def change(a, b):
    """Fraction of thumbnail cells that differ, plus the changed bbox in real px."""
    d = np.abs(a - b) > CELL_DIFF
    frac = float(d.mean())
    if not d.any():
        return frac, None
    ys, xs = np.nonzero(d)
    cw, ch = 900 / THUMB[0], 1600 / THUMB[1]
    return frac, [int(xs.min() * cw), int(ys.min() * ch), int((xs.max() + 1) * cw), int((ys.max() + 1) * ch)]


# ---------------------------------------------------------------- session / log
def _session_file():
    return os.path.join(CAP_ROOT, ".session")


def session_name():
    try:
        return open(_session_file(), encoding="utf-8").read().strip() or "scratch"
    except OSError:
        return "scratch"


def sdir(*parts):
    d = os.path.join(CAP_ROOT, session_name(), *parts)
    os.makedirs(d if not os.path.splitext(d)[1] else os.path.dirname(d), exist_ok=True)
    return d


def read_log():
    p = os.path.join(sdir(), "log.jsonl")
    if not os.path.exists(p):
        return []
    return [json.loads(l) for l in open(p, encoding="utf-8") if l.strip()]


def next_index():
    log = read_log()
    return (max((e.get("i", 0) for e in log), default=0)) + 1


def save_frame(frame, idx, name, full=False):
    base = f"{idx:04d}_{name}"
    im = Image.fromarray(frame)
    prev = os.path.join(sdir("frames"), f"{base}@{PREVIEW_SCALE}.jpg")
    im.resize((im.width // PREVIEW_SCALE, im.height // PREVIEW_SCALE), Image.LANCZOS).save(prev, quality=82)
    if full:
        im.save(os.path.join(sdir("frames"), base + ".png"))
    return os.path.relpath(prev, sdir())


def log(entry):
    entry.setdefault("t", time.strftime("%H:%M:%S"))
    with open(os.path.join(sdir(), "log.jsonl"), "a", encoding="utf-8") as fh:
        fh.write(json.dumps(entry, ensure_ascii=False) + "\n")
    with open(os.path.join(sdir(), "log.md"), "a", encoding="utf-8") as fh:
        if entry["kind"] == "beat":
            fh.write(f"\n## [{entry['i']:04d}] {entry['text']}\n\n")
        elif entry["kind"] == "note":
            fh.write(f"> {entry['text']}\n\n")
        else:
            arg = " ".join(str(v) for v in entry.get("xy", []))
            fh.write(f"- `{entry['i']:04d}` {entry['t']} **{entry['kind']}** {arg} -> {entry.get('result','')}"
                     f" ({entry.get('secs','')}s) {entry.get('frame','')}\n")


# ---------------------------------------------------------------- points / coords
def points():
    try:
        return json.load(open(POINTS_FILE, encoding="utf-8"))
    except OSError:
        return {}


def resolve(args, n, scale):
    """Pull n coordinates (or one point name per pair) off the front of args."""
    out, pts, a = [], points(), list(args)
    while len(out) < n:
        tok = a.pop(0)
        if tok in pts:
            out += pts[tok]
        else:
            out += [int(round(float(tok) * scale)), int(round(float(a.pop(0)) * scale))]
    return out, a


# ---------------------------------------------------------------- settle
def settle(before_sig, timeout, max_wait, stable_needed=2):
    """Poll until the screen changed vs before and then holds still.
    Returns (result, frame, frac, bbox): CHANGED | NOCHANGE | MOVING."""
    t0 = time.time()
    changed, stable, prev = False, 0, before_sig
    frame = None
    while True:
        frame = grab()
        s = sig(frame)
        if not changed:
            frac, _ = change(before_sig, s)
            changed = frac > CHANGE_FRAC
        else:
            frac, _ = change(prev, s)
            stable = stable + 1 if frac <= STABLE_FRAC else 0
            if stable >= stable_needed:
                f, bbox = change(before_sig, s)
                return "CHANGED", frame, f, bbox
        prev = s
        el = time.time() - t0
        if not changed and el >= timeout:
            return "NOCHANGE", frame, 0.0, None
        if el >= max_wait:
            f, bbox = change(before_sig, s)
            return "MOVING", frame, f, bbox


def act(kind, xy, name, opts, do):
    before = grab()
    bsig = sig(before)
    idx = next_index()
    t0 = time.time()
    do()
    res, frame, frac, bbox = settle(bsig, opts.timeout, min(opts.max, MAX_WAIT))
    secs = round(time.time() - t0, 2)
    entry = {"i": idx, "kind": kind, "xy": xy, "name": name, "result": res, "secs": secs,
             "change": round(frac, 4), "bbox": bbox}
    if res != "NOCHANGE" or opts.keep:
        entry["frame"] = save_frame(frame, idx, name or kind, opts.full)
    log(entry)
    print(f"{res} {kind} {' '.join(map(str, xy))} {secs}s change={frac:.3f} bbox={bbox} "
          f"{os.path.join(sdir(), entry['frame']) if 'frame' in entry else ''}", flush=True)
    return res


# ---------------------------------------------------------------- drawing
def font(size):
    for f in ("arialbd.ttf", "arial.ttf"):
        try:
            return ImageFont.truetype(f, size)
        except OSError:
            pass
    return ImageFont.load_default()


def draw_action(im, e, scale):
    """Wireframe overlay of one action on a preview image (scale = real/preview)."""
    d = ImageDraw.Draw(im)
    xy = [v / scale for v in e.get("xy", [])]
    col = (255, 60, 60)
    if e["kind"] in ("tap", "advance") and len(xy) >= 2:
        x, y = xy[:2]
        for r in (10, 18):
            d.ellipse([x - r, y - r, x + r, y + r], outline=col, width=3)
        d.line([x - 26, y, x - 12, y], fill=col, width=2)
        d.line([x + 12, y, x + 26, y], fill=col, width=2)
    elif e["kind"] == "swipe" and len(xy) >= 4:
        x1, y1, x2, y2 = xy[:4]
        d.line([x1, y1, x2, y2], fill=col, width=5)
        v = np.array([x2 - x1, y2 - y1], float)
        v /= max(np.linalg.norm(v), 1)
        n = np.array([-v[1], v[0]])
        tip = np.array([x2, y2])
        d.polygon([tuple(tip), tuple(tip - v * 22 + n * 12), tuple(tip - v * 22 - n * 12)], fill=col)
        d.ellipse([x1 - 8, y1 - 8, x1 + 8, y1 + 8], outline=col, width=3)
    if e.get("bbox") and e["kind"] != "shot":
        b = [v / scale for v in e["bbox"]]
        d.rectangle(b, outline=(80, 200, 255), width=2)
    return im


def cmd_story(o):
    """Storyboard: frame BEFORE each action carries the action drawn on it."""
    log_ = [e for e in read_log() if o.frm <= e["i"] <= o.to]
    steps, beat = [], None
    last_frame = None
    for e in log_:
        if e["kind"] == "beat":
            beat = e["text"]
            last_frame = e.get("frame") or last_frame
            continue
        if e["kind"] in ("note", "shot", "watch"):
            last_frame = e.get("frame") or last_frame
            continue
        # the action happened on the previous visible frame
        steps.append({"e": e, "on": last_frame, "beat": beat})
        beat = None
        if e.get("frame"):
            last_frame = e["frame"]
    if not steps:
        raise SystemExit("nothing logged in range")
    W, H, PAD, LAB, HEAD = 225, 400, 40, 52, 30
    cols = o.cols
    rows = (len(steps) + cols - 1) // cols
    sheet = Image.new("RGB", (cols * (W + PAD) + PAD, rows * (H + LAB + HEAD + PAD) + PAD), (18, 20, 26))
    d = ImageDraw.Draw(sheet)
    f1, f2 = font(14), font(16)
    centers = []
    for k, s in enumerate(steps):
        e = s["e"]
        r, c = divmod(k, cols)
        x0 = PAD + c * (W + PAD)
        y0 = PAD + r * (H + LAB + HEAD + PAD) + HEAD
        if s["on"] and os.path.exists(os.path.join(sdir(), s["on"])):
            im = Image.open(os.path.join(sdir(), s["on"])).convert("RGB")
        else:
            im = Image.new("RGB", (450, 800), (40, 40, 40))
        im = draw_action(im, e, 900 / im.width).resize((W, H), Image.LANCZOS)
        sheet.paste(im, (x0, y0))
        d.rectangle([x0 - 1, y0 - 1, x0 + W, y0 + H], outline=(90, 90, 110))
        if s["beat"]:
            d.rectangle([x0, y0 - HEAD + 2, x0 + W, y0 - 4], fill=(255, 200, 60))
            d.text((x0 + 4, y0 - HEAD + 6), s["beat"][:30], fill=(0, 0, 0), font=f2)
        res_col = {"CHANGED": (120, 220, 120), "NOCHANGE": (255, 110, 110), "MOVING": (255, 200, 80)}.get(e.get("result"), (200, 200, 200))
        d.text((x0, y0 + H + 4), f"{e['i']:04d} {e['kind']} {' '.join(map(str, e.get('xy', [])))}", fill=(230, 230, 230), font=f1)
        d.text((x0, y0 + H + 22), f"{e.get('name') or ''}"[:28], fill=(170, 170, 190), font=f1)
        d.text((x0 + W - 90, y0 + H + 22), f"{e.get('result','')} {e.get('secs','')}s", fill=res_col, font=f1)
        centers.append((x0, y0, c))
    for k in range(len(centers) - 1):
        x0, y0, c = centers[k]
        nx, ny, nc = centers[k + 1]
        if nc == c + 1:
            y = y0 + H // 2
            d.line([x0 + W + 4, y, nx - 6, y], fill=(255, 200, 60), width=3)
            d.polygon([(nx - 4, y), (nx - 14, y - 7), (nx - 14, y + 7)], fill=(255, 200, 60))
    out = os.path.join(sdir(), f"story_{steps[0]['e']['i']:04d}-{steps[-1]['e']['i']:04d}.jpg")
    sheet.save(out, quality=85)
    print(out)


def cmd_sheet(o):
    entries = [e for e in read_log() if e.get("frame") and o.frm <= e["i"] <= o.to]
    if not entries:
        raise SystemExit("no frames in range")
    W, H, LAB = 180, 320, 34
    cols = o.cols
    rows = (len(entries) + cols - 1) // cols
    sheet = Image.new("RGB", (cols * W, rows * (H + LAB)), (0, 0, 0))
    d = ImageDraw.Draw(sheet)
    f = font(13)
    for k, e in enumerate(entries):
        r, c = divmod(k, cols)
        im = Image.open(os.path.join(sdir(), e["frame"])).convert("RGB").resize((W, H))
        sheet.paste(im, (c * W, r * (H + LAB)))
        lab = e.get("text") or f"{e['kind']} {e.get('name') or ''}"
        d.text((c * W + 3, r * (H + LAB) + H + 2), f"{e['i']:04d} {lab}"[:26], fill=(255, 255, 255), font=f)
        d.text((c * W + 3, r * (H + LAB) + H + 17), f"{e.get('result','')} {e.get('secs','')}s", fill=(160, 200, 160), font=f)
    out = os.path.join(sdir(), f"sheet_{entries[0]['i']:04d}-{entries[-1]['i']:04d}.jpg")
    sheet.save(out, quality=82)
    print(out)


def cmd_grid(o):
    frames = sorted(os.listdir(sdir("frames")))
    frames = [f for f in frames if f.endswith(".jpg") and (not o.name or o.name in f)]
    if not frames:
        frame = grab()
        idx = next_index()
        rel = save_frame(frame, idx, "grid")
        log({"i": idx, "kind": "shot", "name": "grid", "frame": rel, "result": "", "secs": 0})
        path = os.path.join(sdir(), rel)
    else:
        path = os.path.join(sdir("frames"), frames[-1])
    im = Image.open(path).convert("RGB")
    s = 900 / im.width
    d = ImageDraw.Draw(im, "RGBA")
    f = font(10)
    for real in range(0, 1601, 50):
        p = real / s
        strong = real % 100 == 0
        if real <= 900:
            d.line([p, 0, p, im.height], fill=(255, 255, 0, 110 if strong else 45))
            if strong:
                d.text((p + 2, 2), str(real), fill=(255, 255, 0), font=f)
        d.line([0, p, im.width, p], fill=(255, 255, 0, 110 if strong else 45))
        if strong:
            d.text((2, p + 1), str(real), fill=(255, 255, 0), font=f)
    out = os.path.splitext(path)[0] + "_grid.jpg"
    im.save(out, quality=85)
    print(out, "(labels are REAL px)")


def cmd_watch(o):
    prev = grab()
    psig = sig(prev)
    last_change = time.time()
    n = 0
    print(f"WATCH start session={session_name()} interval={o.interval}s stall={o.stall}s", flush=True)
    while True:
        time.sleep(o.interval)
        frame = grab()
        s = sig(frame)
        frac, bbox = change(psig, s)
        now = time.time()
        if frac > o.min_change:
            n += 1
            idx = next_index()
            rel = save_frame(frame, idx, "watch")
            log({"i": idx, "kind": "watch", "xy": [], "name": "watch", "result": "CHANGED",
                 "secs": round(now - last_change, 1), "change": round(frac, 4), "bbox": bbox, "frame": rel})
            print(f"CHANGE {idx:04d} change={frac:.3f} bbox={bbox} {os.path.join(sdir(), rel)}", flush=True)
            psig, last_change = s, now
        elif now - last_change >= o.stall:
            print(f"STALL {int(now - last_change)}s last={idx if n else '-'}", flush=True)
            act_ = o.on_stall
            if act_ == "exit":
                return
            if act_ == "shot":
                idx = next_index()
                log({"i": idx, "kind": "shot", "name": "stall", "result": "STALL", "secs": 0,
                     "frame": save_frame(frame, idx, "stall")})
            elif act_ == "back":
                adb("shell", "input", "keyevent", "4")
            elif act_.startswith("tap:"):
                (x, y), _ = resolve([act_[4:]], 2, 1)
                adb("shell", "input", "tap", x, y)
            psig, last_change = s, now      # re-arm on the current frame so slow drift doesn't fire CHANGE


def cmd_up(_o):
    out = subprocess.run([ADB, "devices"], capture_output=True, text=True).stdout
    if not any(l.strip().endswith("device") for l in out.splitlines()[1:]):
        print("launching LDPlayer...", flush=True)
        subprocess.run([LDCONSOLE, "launch", "--index", "0"])
        t0 = time.time()
        while time.time() - t0 < 120:
            time.sleep(3)
            out = subprocess.run([ADB, "devices"], capture_output=True, text=True).stdout
            if any(l.strip().endswith("device") for l in out.splitlines()[1:]):
                break
        else:
            raise SystemExit("emulator did not come up in 120s")
    focus = adb("shell", "dumpsys", "window", check=False)
    if PACKAGE not in "".join(l for l in focus.splitlines() if "mCurrentFocus" in l):
        adb("shell", "am", "start", "-n", ACTIVITY)
        print("game started; first screen is usually the offer popup (tap close_offer)")
    print(f"UP serial={serial()}")


# ---------------------------------------------------------------- main
def main():
    ap = argparse.ArgumentParser(add_help=False)
    ap.add_argument("cmd", nargs="?")
    ap.add_argument("args", nargs="*")
    ap.add_argument("--s", type=float, default=1.0, help="coords come from a preview at this scale")
    ap.add_argument("--timeout", type=float, default=3.0, help="give up if nothing changes within this")
    ap.add_argument("--max", type=float, default=8.0, help="max wait for the screen to settle")
    ap.add_argument("--full", action="store_true")
    ap.add_argument("--keep", action="store_true", help="save a frame even on NOCHANGE")
    ap.add_argument("--n", type=int, default=30)
    ap.add_argument("--interval", type=float, default=1.0)
    ap.add_argument("--stall", type=float, default=20.0)
    ap.add_argument("--min-change", dest="min_change", type=float, default=STABLE_FRAC,
                    help="watch: fraction of the screen that must change to count (idle anims stay below)")
    ap.add_argument("--on-stall", dest="on_stall", default="notify")
    ap.add_argument("--from", dest="frm", type=int, default=0)
    ap.add_argument("--to", type=int, default=10 ** 9)
    ap.add_argument("--cols", type=int, default=0)
    ap.add_argument("-h", "--help", action="store_true")
    o = ap.parse_args()
    if o.help or not o.cmd:
        print(__doc__)
        return
    c, a = o.cmd, o.args
    o.name = None

    if c == "session":
        if a:
            os.makedirs(CAP_ROOT, exist_ok=True)
            open(_session_file(), "w", encoding="utf-8").write(a[0])
        print(f"session={session_name()} dir={sdir()} entries={len(read_log())}")
    elif c == "up":
        cmd_up(o)
    elif c == "shot":
        frame = grab()
        idx = next_index()
        name = a[0] if a else "shot"
        rel = save_frame(frame, idx, name, o.full)
        log({"i": idx, "kind": "shot", "xy": [], "name": name, "frame": rel, "result": "", "secs": 0})
        print(os.path.join(sdir(), rel))
    elif c == "tap":
        xy, rest = resolve(a, 2, o.s)
        act("tap", xy, rest[0] if rest else (a[0] if a[0] in points() else None), o,
            lambda: adb("shell", "input", "tap", *xy))
    elif c == "swipe":
        xy, rest = resolve(a, 4, o.s)
        ms = rest.pop(0) if rest and rest[0].isdigit() else "300"
        act("swipe", xy, rest[0] if rest else None, o,
            lambda: adb("shell", "input", "swipe", *xy, ms))
    elif c == "back":
        act("back", [], a[0] if a else None, o, lambda: adb("shell", "input", "keyevent", "4"))
    elif c == "home":
        adb("shell", "input", "keyevent", "3")
        log({"i": next_index(), "kind": "home", "xy": [], "result": "", "secs": 0})
    elif c == "text":
        adb("shell", "input", "text", a[0].replace(" ", "%s"))
        log({"i": next_index(), "kind": "text", "xy": [], "name": a[0], "result": "", "secs": 0})
    elif c == "advance":
        xy, _ = resolve(a, 2, o.s)
        for k in range(o.n):
            r = act("advance", xy, f"{a[0]}_{k + 1}", o, lambda: adb("shell", "input", "tap", *xy))
            if r == "NOCHANGE":
                print(f"ADVANCE stopped after {k + 1} taps (no change)")
                break
    elif c in ("beat", "note"):
        idx = next_index()
        e = {"i": idx, "kind": c, "text": " ".join(a)}
        if c == "beat":
            e["frame"] = save_frame(grab(), idx, "beat")
        log(e)
        print(f"{c.upper()} {idx:04d} {e['text']}")
    elif c == "watch":
        cmd_watch(o)
    elif c == "sheet":
        o.cols = o.cols or 6
        cmd_sheet(o)
    elif c == "story":
        o.cols = o.cols or 4
        cmd_story(o)
    elif c == "grid":
        o.name = a[0] if a else None
        cmd_grid(o)
    elif c == "where":
        print(round(float(a[0]) * o.s), round(float(a[1]) * o.s))
    elif c == "points":
        for k, v in points().items():
            print(f"{k:16s} {v}")
    else:
        print(__doc__)


if __name__ == "__main__":
    main()
