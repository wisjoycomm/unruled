"""Dump an Unity .anim clip as (path, attribute) -> [(time, value), ...].

Unity writes each curve as a block of keyframes followed by the 'attribute' and
'path' it drives, so the file is parsed by buffering keys until that trailer.

Usage:
  python tools/ld/animcurve.py <clip.anim> [pathFilter]
"""
import re
import sys

KEY_TIME = re.compile(r"^\s*- serializedVersion: 3\s*$")
NUM = r"-?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?"


def main():
    path = sys.argv[1]
    want = sys.argv[2].lower() if len(sys.argv) > 2 else None

    keys = []          # keys buffered for the curve currently being read
    pending = []       # (time, value) pairs
    attribute = None
    out = []

    time_re = re.compile(rf"^\s*time:\s*({NUM})\s*$")
    value_re = re.compile(rf"^\s*value:\s*({NUM})\s*$")
    attr_re = re.compile(r"^\s*attribute:\s*(\S+)\s*$")
    path_re = re.compile(r"^\s*path:\s*(.*?)\s*$")

    cur_time = None
    with open(path, encoding="utf-8", errors="ignore") as fh:
        for line in fh:
            m = time_re.match(line)
            if m:
                cur_time = float(m.group(1))
                continue
            m = value_re.match(line)
            if m and cur_time is not None:
                pending.append((cur_time, float(m.group(1))))
                cur_time = None
                continue
            m = attr_re.match(line)
            if m:
                attribute = m.group(1)
                continue
            m = path_re.match(line)
            if m and attribute is not None:
                node = m.group(1) or "<root>"
                if pending:
                    out.append((node, attribute, list(pending)))
                pending, attribute = [], None
                continue

    for node, attr, ks in out:
        if want and want not in node.lower():
            continue
        pretty = ", ".join(f"t={t:.3f}:{v:g}" for t, v in ks)
        print(f"{node:<24s} {attr:<26s} {pretty}")


if __name__ == "__main__":
    main()
