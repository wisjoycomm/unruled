"""Report brightness statistics and the dominant colours of a capture, as a
sanity check that a screenshot actually contains the UI being looked for.

Usage:
  python tools/ld/stats.py <name> [name2 ...]
"""
import os
import sys
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _img import cap_path, load_cap_np  # noqa: E402


def main():
    for name in sys.argv[1:]:
        path = cap_path(name)
        if not os.path.exists(path):
            print(f"{name}: MISSING")
            continue
        arr, _, _, _ = load_cap_np(name)
        r = arr[:, :, 0].astype(np.int32)
        g = arr[:, :, 1].astype(np.int32)
        b = arr[:, :, 2].astype(np.int32)
        lum = (r * 299 + g * 587 + b * 114) // 1000
        n = lum.size
        bright = int((lum >= 170).sum())
        mid = int(((lum >= 60) & (lum < 170)).sum())
        dark = n - bright - mid
        # Quantise to a 32-level cube so near-identical shades group together.
        qr, qg, qb = (r // 32 * 32).ravel(), (g // 32 * 32).ravel(), (b // 32 * 32).ravel()
        packed = (qr << 16) | (qg << 8) | qb
        vals, counts = np.unique(packed, return_counts=True)
        order = np.argsort(-counts)[:4]
        top = ", ".join(
            f"({int(vals[i] >> 16)}, {int((vals[i] >> 8) & 255)}, {int(vals[i] & 255)}):{counts[i] * 100.0 / n:.0f}%"
            for i in order
        )
        print(f"{name}: mean_lum={float(lum.mean()):.1f} "
              f"bright={bright * 100.0 / n:.1f}% mid={mid * 100.0 / n:.1f}% "
              f"dark={dark * 100.0 / n:.1f}%")
        print(f"    top colours: {top}")


if __name__ == "__main__":
    main()
