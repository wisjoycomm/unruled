"""Shared image, downsampled NumPy array & design-space helpers for LDPlayer tools.

Supports automatic half-resolution analysis (`max_width=540`, keeping exact 9:16 ratio)
and `@2.jpg` session previews from `ldctl.py`, then scales coordinates back to real
device pixels (`real = small * scale`) and 640x1136 design units.
"""
import glob
import os
import re
import numpy as np
from PIL import Image

OUT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".ldcap"))
DW, DH = 640.0, 1136.0
DEFAULT_MAX_WIDTH = 540


def cap_path(name: str, ext: str = ".png") -> str:
    """Resolve a capture name ('001', path, or ldctl frame prefix) to an image file."""
    if os.path.exists(name):
        return name
    for cand_ext in (ext, "@2.jpg", ".jpg", ".png"):
        p = os.path.join(OUT, f"{name}{cand_ext}")
        if os.path.exists(p):
            return p
    # Also check active ldctl session frames if present
    cap_root = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".temp", "ld-captures"))
    sess_file = os.path.join(cap_root, ".session")
    if os.path.exists(sess_file):
        sess = open(sess_file, encoding="utf-8").read().strip() or "scratch"
        frames_dir = os.path.join(cap_root, sess, "frames")
        hits = sorted(glob.glob(os.path.join(frames_dir, f"*{name}*.*")))
        if hits:
            return hits[-1]
    return os.path.join(OUT, f"{name}{ext}")


def load_cap(name: str, mode: str = "RGB"):
    """Open a capture image in the given mode and return (im, px, w, h)."""
    im = Image.open(cap_path(name)).convert(mode)
    return im, im.load(), im.width, im.height


def load_cap_np(name: str, max_width: int = DEFAULT_MAX_WIDTH):
    """Load capture as a downsampled RGB uint8 NumPy array while preserving aspect ratio.

    Returns `(arr, real_w, real_h, scale)` where:
    - `arr` has shape `(h_small, w_small, 3)`
    - `real_w, real_h` are full device pixel dimensions (e.g. 900x1600 or 1080x1920)
    - `scale` is the integer factor (`real_coord = small_coord * scale`)
    """
    path = cap_path(name)
    im = Image.open(path).convert("RGB")
    w, h = im.size

    # Detect if file is already a @N preview from ldctl (e.g. 0001_home@2.jpg)
    m = re.search(r"@(\d+)\.(?:jpg|jpeg|png)$", os.path.basename(path), re.I)
    if m:
        file_scale = int(m.group(1))
        return np.asarray(im, dtype=np.uint8), w * file_scale, h * file_scale, file_scale

    if max_width and w > max_width:
        scale = max(1, round(w / max_width))
        if scale > 1 and w % scale == 0 and h % scale == 0:
            im_small = im.resize((w // scale, h // scale), Image.BILINEAR)
            return np.asarray(im_small, dtype=np.uint8), w, h, scale

    return np.asarray(im, dtype=np.uint8), w, h, 1


def bright_bbox(px_or_arr, w: int, h: int, thresh: int = 200, window=None, scale: int = 1):
    """Vectorized bounding box `(x0, x1, y0, y1)` of pixels with R,G,B >= thresh in real coords."""
    if isinstance(px_or_arr, np.ndarray):
        arr = px_or_arr
    else:
        # Fallback if a PIL PixelAccess was passed: copy into numpy array
        sh, sw = h // scale, w // scale
        arr = np.array([[px_or_arr[x, y] for x in range(sw)] for y in range(sh)], dtype=np.uint8)

    sh, sw = arr.shape[:2]
    if window is not None:
        xlo, ylo, xhi, yhi = (max(0, v // scale) for v in window)
        xhi, yhi = min(sw, xhi), min(sh, yhi)
    else:
        xlo, ylo, xhi, yhi = 0, 0, sw, sh

    sub = arr[ylo:yhi, xlo:xhi]
    if sub.size == 0:
        return None
    mask = (sub[:, :, 0] >= thresh) & (sub[:, :, 1] >= thresh) & (sub[:, :, 2] >= thresh)
    if not mask.any():
        return None
    ys, xs = np.nonzero(mask)
    x0 = int(xs.min() + xlo) * scale
    x1 = int(xs.max() + xlo + 1) * scale - 1
    y0 = int(ys.min() + ylo) * scale
    y1 = int(ys.max() + ylo + 1) * scale - 1
    return x0, x1, y0, y1
