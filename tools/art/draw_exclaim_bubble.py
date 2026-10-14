# Draws UnityAssets/Assets/_EndlessWinter/Art/UI/Ftue/ftue_exclaim_bubble.png (2x the captured size; a_1..a_8 show a 78x67 body + tail on a 900-wide frame).
# Usage: python tools/art/draw_exclaim_bubble.py <out.png>
import sys
from PIL import Image, ImageDraw, ImageFilter
S = 8
W, H = 90 * S, 88 * S

def body(d, inset, fill):
    # Slightly tapered rounded box: top 6..84, bottom 10..80 (the capture narrows towards the tail).
    t, b = 5 * S + inset, 72 * S - inset
    l0, r0, l1, r1 = 6 * S + inset, 84 * S - inset, 10 * S + inset, 80 * S - inset
    rad = 9 * S
    d.polygon([(l0 + rad, t), (r0 - rad, t), (r0, t + rad), (r1, b - rad), (r1 - rad, b), (l1 + rad, b), (l1, b - rad), (l0, t + rad)], fill=fill)
    for cx, cy in ((l0 + rad, t + rad), (r0 - rad, t + rad), (l1 + rad, b - rad), (r1 - rad, b - rad)):
        d.ellipse((cx - rad, cy - rad, cx + rad, cy + rad), fill=fill)

img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
shadow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
d = ImageDraw.Draw(shadow)
body(d, 0, (120, 104, 122, 170))
d.polygon([(39 * S, 71 * S), (45 * S, 82 * S), (51 * S, 71 * S)], fill=(120, 104, 122, 170))
shadow = shadow.filter(ImageFilter.GaussianBlur(1.6 * S))
img.alpha_composite(shadow)
d = ImageDraw.Draw(img)
body(d, int(1.5 * S), (250, 246, 212, 255))
d.polygon([(41 * S, 69 * S), (45 * S, 78 * S), (49 * S, 69 * S)], fill=(250, 246, 212, 255))
body(d, int(3 * S), (255, 255, 255, 255))
red = (255, 80, 80, 255)
d.polygon([(40 * S, 19 * S), (50 * S, 19 * S), (49 * S, 45 * S), (41 * S, 45 * S)], fill=red)
d.rectangle((41 * S, 50 * S, 49 * S, 55 * S), fill=red)
img = img.resize((W // 4, H // 4), Image.LANCZOS)
img.save(sys.argv[1])
