"""Slice a .ldcap capture into readable strips for the agent to inspect.

Usage:
  python tools/ld/view.py <name> [rows]      -> .ldcap/<name>_v0.png .. _v{rows-1}.png
  python tools/ld/view.py <name> box l t r b -> .ldcap/<name>_box.png (2x zoom)
"""
import os
import sys

from PIL import Image

OUT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".ldcap"))


def main():
    name = sys.argv[1]
    im = Image.open(os.path.join(OUT, f"{name}.png")).convert("RGB")

    if len(sys.argv) > 2 and sys.argv[2] == "box":
        l, t, r, b = (int(v) for v in sys.argv[3:7])
        crop = im.crop((l, t, r, b))
        crop = crop.resize((crop.width * 2, crop.height * 2), Image.LANCZOS)
        path = os.path.join(OUT, f"{name}_box.png")
        crop.save(path)
        print(path, crop.size)
        return

    rows = int(sys.argv[2]) if len(sys.argv) > 2 else 3
    step = im.height // rows
    for i in range(rows):
        crop = im.crop((0, i * step, im.width, min(im.height, (i + 1) * step)))
        w = 810
        crop = crop.resize((w, int(crop.height * w / crop.width)), Image.LANCZOS)
        path = os.path.join(OUT, f"{name}_v{i}.png")
        crop.save(path)
        print(path, crop.size)


if __name__ == "__main__":
    main()
