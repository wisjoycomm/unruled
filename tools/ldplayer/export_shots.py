"""Copy selected LD captures into .docs/ftue_screens as compressed JPGs.
usage: python export_shots.py src=dst [src=dst ...]   (src relative to ld-captures, dst without extension)
"""
import os, sys
from PIL import Image

CAP = r"D:\UnityProjects\endlesswinter\UnityAssets\.temp\ld-captures"
OUT = r"D:\UnityProjects\endlesswinter\UnityAssets\.docs\ftue_screens"
os.makedirs(OUT, exist_ok=True)
for arg in sys.argv[1:]:
    src, dst = arg.split("=")
    im = Image.open(os.path.join(CAP, src)).convert("RGB")
    if im.width > 900:
        im = im.resize((900, int(im.height * 900 / im.width)))
    im.save(os.path.join(OUT, dst + ".jpg"), quality=78)
    print(dst)
