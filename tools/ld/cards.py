"""Locate hero-card rectangles in a Heroes-tab capture by finding columns/rows
whose colour differs from the panel's flat background.

The Heroes list sits on a flat blue sheet, so projecting "non-background" pixel
counts onto each axis gives clean card bands.

Usage:
  python tools/ld/cards.py <name> <xlo> <ylo> <xhi> <yhi> [bgTolerance]
"""
import os
import sys
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _img import DH, DW, load_cap_np, to_real_span  # noqa: E402


def bands(counts, lo, floor, scale):
    """Contiguous index ranges where count exceeds floor, scaled back to real px."""
    out, start = [], None
    min_span = max(3, int(round(8 / scale)))
    for i, c in enumerate(counts):
        if c > floor:
            if start is None:
                start = i
        elif start is not None:
            if (i - 1) - start >= min_span:
                out.append(to_real_span(start + lo, i - 1 + lo, scale))
            start = None
    if start is not None and (len(counts) - 1) - start >= min_span:
        out.append(to_real_span(start + lo, len(counts) - 1 + lo, scale))
    return out


def main():
    name = sys.argv[1]
    xlo_r, ylo_r, xhi_r, yhi_r = (int(v) for v in sys.argv[2:6])
    tol = int(sys.argv[6]) if len(sys.argv) > 6 else 40

    arr, w, h, scale = load_cap_np(name)
    sh, sw = arr.shape[:2]
    xlo, ylo = max(0, int(round(xlo_r / scale))), max(0, int(round(ylo_r / scale)))
    xhi, yhi = min(sw, int(round(xhi_r / scale))), min(sh, int(round(yhi_r / scale)))

    sub = arr[ylo:yhi, xlo:xhi].astype(np.int16)
    flat = sub.reshape(-1, 3)
    packed = (flat[:, 0].astype(np.int32) << 16) | (flat[:, 1].astype(np.int32) << 8) | flat[:, 2].astype(np.int32)
    vals, cnts = np.unique(packed, return_counts=True)
    bg_val = int(vals[np.argmax(cnts)])
    bg = np.array([bg_val >> 16, (bg_val >> 8) & 255, bg_val & 255], dtype=np.int16)

    diff = np.abs(sub - bg).sum(axis=2) > tol
    colc = diff.sum(axis=0)
    rowc = diff.sum(axis=1)

    sx, sy = DW / w, DH / h
    print(f"background ({int(bg[0])}, {int(bg[1])}, {int(bg[2])})")
    print("columns:")
    for a, b in bands(colc, xlo, (yhi - ylo) * 0.06, scale):
        print(f"  x {a}..{b} (w={b - a + 1})  design w={(b - a + 1) * sx:.1f} "
              f"cx={((a + b) / 2 - w / 2) * sx:+.1f}")
    print("rows:")
    for a, b in bands(rowc, ylo, (xhi - xlo) * 0.06, scale):
        print(f"  y {a}..{b} (h={b - a + 1})  design h={(b - a + 1) * sy:.1f} "
              f"cyFromBottom={(h - (a + b) / 2) * sy:.1f}")


if __name__ == "__main__":
    main()
