"""Locate a template image inside an LD capture (normalised cross-correlation).

Usage:
  python tools/ld/find.py <capture-name> <template-path> [scales]

Prints the best match position/score so taps can target real on-screen widgets
instead of guessed coordinates.
"""
import os
import sys

import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _img import OUT  # noqa: E402


def _box_sums(img, h, w):
    """Sliding-window sums of img over an h x w box, via a summed-area table.

    The cumulative sums must be accumulated in float64: a 1080p table reaches
    ~1e9, which float32 (7 significant digits) cannot hold, and the differencing
    step then cancels away what little precision is left.
    """
    img = np.asarray(img, dtype=np.float64)
    ii = np.zeros((img.shape[0] + 1, img.shape[1] + 1), dtype=np.float64)
    ii[1:, 1:] = img.cumsum(axis=0).cumsum(axis=1)
    return (ii[h:, w:] - ii[:-h, w:] - ii[h:, :-w] + ii[:-h, :-w])


def ncc(hay, needle):
    """Best normalised-cross-correlation match, computed in the frequency domain.

    Uses FFT for the correlation term and summed-area tables for the window
    mean/variance, so cost is independent of template size and memory stays flat
    (a direct sliding-window stack needs tens of GiB at 1080p).
    """
    H, W = hay.shape
    h, w = needle.shape
    if h > H or w > W:
        return -1.0, 0, 0

    n = needle - needle.mean()
    n_norm = np.sqrt((n * n).sum())
    # A featureless template matches everything equally; refuse rather than
    # return an arbitrary position.
    if n_norm < 1e-6:
        return -1.0, 0, 0

    # Correlation of the raw haystack with the zero-mean template. Linear (not
    # circular) correlation needs the transform padded to H+h-1 by W+w-1.
    hay = np.asarray(hay, dtype=np.float64)
    n = np.asarray(n, dtype=np.float64)
    ph, pw = H + h - 1, W + w - 1
    fh = np.fft.rfft2(hay, s=(ph, pw))
    fn = np.fft.rfft2(n[::-1, ::-1], s=(ph, pw))
    full = np.fft.irfft2(fh * fn, s=(ph, pw))
    corr = full[h - 1:H, w - 1:W]

    # Per-window standard deviation from summed-area tables.
    s1 = _box_sums(hay, h, w)
    s2 = _box_sums(hay * hay, h, w)
    cnt = h * w
    var = s2 - (s1 * s1) / cnt
    np.maximum(var, 0, out=var)
    norms = np.sqrt(var)

    # Flat windows (a solid-colour background panel) have ~zero variance, where
    # rounding noise in the denominator would otherwise yield scores above 1.
    # Scale the cutoff to the template's own contrast so that a genuinely
    # low-contrast target is still matchable, while true flat areas are not.
    valid = norms > max(1e-6, 0.05 * n_norm)

    with np.errstate(divide="ignore", invalid="ignore"):
        score = corr / (norms * n_norm)
    score = np.nan_to_num(score, nan=-1.0, posinf=-1.0, neginf=-1.0)
    score = np.where(valid, np.clip(score, -1.0, 1.0), -1.0)

    idx = int(np.argmax(score))
    y, x = divmod(idx, score.shape[1])
    return float(score[y, x]), x, y


def selftest():
    """Crop known patches out of a capture and confirm the matcher finds them.

    Only textured patches are meaningful: a uniform crop genuinely matches
    everywhere, so the matcher is expected to reject it.
    """
    cap = Image.open(os.path.join(OUT, f"{sys.argv[2]}.png")).convert("L")
    hay = np.asarray(cap, dtype=np.float64)
    H, W = hay.shape

    picked, tested = [], 0
    for y in range(80, H - 200, 97):
        for x in range(40, W - 200, 89):
            patch = hay[y:y + 110, x:x + 140]
            if patch.std() > 20.0:
                picked.append((x, y))
            if len(picked) >= 6:
                break
        if len(picked) >= 6:
            break

    ok = True
    for (x, y) in picked:
        patch = hay[y:y + 110, x:x + 140]
        sc, fx, fy = ncc(hay, patch)
        hit = abs(fx - x) <= 1 and abs(fy - y) <= 1
        ok &= hit
        tested += 1
        print(f"patch@({x},{y}) std={patch.std():6.1f} -> score {sc:.4f} at ({fx},{fy})  {'OK' if hit else 'MISMATCH'}")

    # A uniform patch must be refused, not matched to an arbitrary spot.
    flat = np.full((60, 60), 128.0)
    sc, _, _ = ncc(hay, flat)
    flat_ok = sc == -1.0
    print(f"uniform patch -> score {sc:.4f}  {'OK (refused)' if flat_ok else 'MISMATCH'}")

    print(f"selftest {'PASSED' if ok and flat_ok and tested else 'FAILED'} ({tested} textured patches)")


def main():
    if sys.argv[1] == "selftest":
        selftest()
        return

    cap = Image.open(os.path.join(OUT, f"{sys.argv[1]}.png")).convert("L")
    tpl_full = Image.open(sys.argv[2]).convert("L")
    scales = [float(s) for s in sys.argv[3].split(",")] if len(sys.argv) > 3 else \
        [0.5, 0.6, 0.7, 0.8, 0.9, 1.0, 1.1, 1.25, 1.5]

    hay = np.asarray(cap, dtype=np.float32)
    best = (-1.0, 0, 0, 1.0, (0, 0))
    for s in scales:
        tw, th = max(8, int(tpl_full.width * s)), max(8, int(tpl_full.height * s))
        tpl = np.asarray(tpl_full.resize((tw, th), Image.LANCZOS), dtype=np.float32)
        sc, x, y = ncc(hay, tpl)
        print(f"scale {s:<5} score {sc:.3f}  at ({x},{y})  size {tw}x{th}")
        if sc > best[0]:
            best = (sc, x, y, s, (tw, th))

    sc, x, y, s, (tw, th) = best
    print(f"\nBEST score={sc:.3f} scale={s} box=({x},{y},{x+tw},{y+th}) center=({x+tw//2},{y+th//2})")


if __name__ == "__main__":
    main()
