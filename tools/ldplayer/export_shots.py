"""Copy selected LD captures into .docs/ftue_screens as compressed JPGs.
usage: python export_shots.py src=dst [src=dst ...]   (src relative to ld-captures, dst without extension)
"""
import os, sys
from PIL import Image

sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))
import project_cfg  # noqa: E402

CAP = str(project_cfg.LD_CAPTURES_DIR)
OUT = str(project_cfg.DOCS_DIR / "ftue_screens")
os.makedirs(OUT, exist_ok=True)
for arg in sys.argv[1:]:
    src, dst = arg.split("=")
    im = Image.open(os.path.join(CAP, src)).convert("RGB")
    if im.width > 900:
        im = im.resize((900, int(im.height * 900 / im.width)))
    im.save(os.path.join(OUT, dst + ".jpg"), quality=78)
    print(dst)
