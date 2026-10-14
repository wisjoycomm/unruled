"""Report brightness statistics and the dominant colours of a capture, as a
sanity check that a screenshot actually contains the UI being looked for.

Usage:
  python tools/ld/stats.py <name> [name2 ...]
"""
import os
import sys
from collections import Counter

from PIL import Image

OUT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".ldcap"))


def main():
    for name in sys.argv[1:]:
        path = os.path.join(OUT, f"{name}.png")
        if not os.path.exists(path):
            print(f"{name}: MISSING")
            continue
        im = Image.open(path).convert("RGB")
        px = list(im.getdata())
        lum = [(r * 299 + g * 587 + b * 114) // 1000 for r, g, b in px]
        n = len(lum)
        bright = sum(1 for v in lum if v >= 170)
        mid = sum(1 for v in lum if 60 <= v < 170)
        dark = n - bright - mid
        # Quantise to a 32-level cube so near-identical shades group together.
        common = Counter((r // 32 * 32, g // 32 * 32, b // 32 * 32) for r, g, b in px)
        top = ", ".join(f"{c}:{k * 100.0 / n:.0f}%" for c, k in common.most_common(4))
        print(f"{name}: mean_lum={sum(lum) / n:.1f} "
              f"bright={bright * 100.0 / n:.1f}% mid={mid * 100.0 / n:.1f}% "
              f"dark={dark * 100.0 / n:.1f}%")
        print(f"    top colours: {top}")


if __name__ == "__main__":
    main()
