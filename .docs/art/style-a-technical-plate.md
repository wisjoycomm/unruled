STYLE A — HARD-LINE TECHNICAL PLATE (locked style guide for every Unruled art batch)

ANCHOR: D:\GameProjects\unruled\.temp\agy-style\anchor_ship.png. Every image must look like it came from the
same technical manual as this anchor. Look at it before drawing.

Rules:
- Engineering-drawing look: crisp uniform outer contour (bright), thinner inner detail lines, diagonal hatching for
  shadowed/cut surfaces, flat tinted fills (2–3 tones max), NO painterly shading, NO soft gradients, NO glow bloom,
  NO photo texture, NO 3D render look.
- Linework colour carries meaning: neutral structure in pale steel blue #9FC4E8 / ink #E6EDF7; heat/energy/danger
  accents only in #FF8A3D / #E5543B; life/terraform #5BD47A; authority gold #F2B634. Fills are dark desaturated
  navy/steel (#16233A, #22324D) so the bright lines read.
- Consistent light from the upper-left; shadow side shown by hatching only.
- Same line weight across a set: outer contour ~6 px, inner ~2–3 px at 512 px.
- NO text, letters, numbers or logos. Callout leader lines with small dots are allowed only on card plates.
- Background: PURE BLACK #000000 unless the brief says otherwise; nothing touches the edges (~10% padding).

Pipeline:
- Generate one family per batch (all planets, all ship plates...) with this guide + the anchor in every brief.
- Filled sprites: key with `.temp/key_silhouette.py` (flood-fill the black border so dark fills stay opaque),
  trim to content, 128 px, Sprite, no mips. Line icons: `.temp/key_alpha.py`.
- HUD frames, panels and gauges are USS, not images.
- Board planets ship as full-colour plates; the owner colour is the ring drawn around them in code.
