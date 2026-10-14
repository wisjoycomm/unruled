"""Find the bounding box of pixels matching a target color in a capture.

Usage:
  python tools/ld/colorbox.py <capture> <#RRGGBB> [tol] [l t r b]
"""
import os
import sys
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _img import load_cap_np, to_real_span  # noqa: E402


def main():
    name = sys.argv[1]
    hexv = sys.argv[2].lstrip("#")
    target = np.array([int(hexv[i:i + 2], 16) for i in (0, 2, 4)], dtype=np.int16)
    tol = int(sys.argv[3]) if len(sys.argv) > 3 else 12

    arr, w, h, scale = load_cap_np(name)
    if len(sys.argv) > 7:
        l, t, r, b = (int(round(int(v) / scale)) for v in sys.argv[4:8])
    else:
        l, t, r, b = 0, 0, arr.shape[1], arr.shape[0]

    sub = arr[t:b, l:r].astype(np.int16)
    mask = np.max(np.abs(sub - target), axis=2) <= tol
    if not mask.any():
        print("no match")
        return

    ys, xs = np.nonzero(mask)
    x0, x1 = to_real_span(int(xs.min() + l), int(xs.max() + l), scale)
    y0, y1 = to_real_span(int(ys.min() + t), int(ys.max() + t), scale)
    print(f"bbox l={x0} t={y0} r={x1} b={y1} "
          f"w={x1-x0+1} h={y1-y0+1} px={int(round(int(mask.sum()) * scale * scale))}")

    # widest rows help identify the true panel body vs. stray matches
    row_sums = mask.sum(axis=1)
    nonzero_rows = np.nonzero(row_sums)[0]
    top_idx = nonzero_rows[np.argsort(-row_sums[nonzero_rows])[:5]]
    print("widest rows:", ", ".join(f"y={int(round((int(y) + t) * scale))}:{int(round(int(row_sums[y]) * scale))}px" for y in top_idx))


if __name__ == "__main__":
    main()
