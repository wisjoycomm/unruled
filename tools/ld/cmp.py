"""Compare an LD capture against a reference image (the user's screenshot).

Usage:
  python tools/ld/cmp.py <capture-name> <reference-path>
Reports per-region mean colour so the agent can tell which screen is showing
without relying on eyeballing a downscaled PNG.
"""
import os
import sys

from PIL import Image

OUT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".ldcap"))


def grid_stats(im, rows=8, cols=4):
    w, h = im.size
    out = []
    for r in range(rows):
        line = []
        for c in range(cols):
            box = (c * w // cols, r * h // rows, (c + 1) * w // cols, (r + 1) * h // rows)
            crop = im.crop(box)
            px = list(crop.getdata())
            n = len(px)
            mr = sum(p[0] for p in px) // n
            mg = sum(p[1] for p in px) // n
            mb = sum(p[2] for p in px) // n
            line.append((mr, mg, mb))
        out.append(line)
    return out


def main():
    cap = Image.open(os.path.join(OUT, f"{sys.argv[1]}.png")).convert("RGB")
    ref = Image.open(sys.argv[2]).convert("RGB")
    ref = ref.resize(cap.size, Image.LANCZOS)

    a = grid_stats(cap)
    b = grid_stats(ref)
    total = 0
    print(f"{'cell':<8}{'capture':<18}{'reference':<18}dist")
    for r in range(len(a)):
        for c in range(len(a[0])):
            p, q = a[r][c], b[r][c]
            d = sum(abs(p[i] - q[i]) for i in range(3))
            total += d
            print(f"r{r}c{c:<5}{str(p):<18}{str(q):<18}{d}")
    print(f"\nmean cell distance = {total / (len(a) * len(a[0])):.1f}")


if __name__ == "__main__":
    main()
