"""Convert capture frames to JPG (quality 90) and delete the originals.

Usage: py tools/ld/to_jpg.py [--width 450] <folder> [<folder> ...]
  PNGs become JPGs. With --width, any PNG or JPG wider than that is also resized
  (keeping the ratio: 900x1600 -> 450x800). Unreadable files are skipped and listed.
"""
import argparse
import os

from PIL import Image

ap = argparse.ArgumentParser()
ap.add_argument("folders", nargs="+")
ap.add_argument("--width", type=int, default=0)
o = ap.parse_args()

done = 0
for root in o.folders:
    for d, _, files in os.walk(root):
        for f in files:
            ext = os.path.splitext(f)[1].lower()
            if ext not in (".png", ".jpg"):
                continue
            src = os.path.join(d, f)
            dst = os.path.splitext(src)[0] + ".jpg"
            try:
                im = Image.open(src)
                if ext == ".jpg" and not (o.width and im.width > o.width):
                    continue
                im = im.convert("RGB")
                if o.width and im.width > o.width:
                    im = im.resize((o.width, round(im.height * o.width / im.width)), Image.LANCZOS)
                tmp = dst + ".tmp"
                im.save(tmp, "JPEG", quality=90)
            except OSError as e:
                print("skip", src, e, flush=True)
                continue
            if src != dst:
                os.remove(src)
            os.replace(tmp, dst)
            done += 1
            if done % 500 == 0:
                print(done, flush=True)
print("converted", done)
