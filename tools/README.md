# tools/ — what is here

Run Python with `py` (Python 3.14; `python` is not on PATH in Git Bash).
Paths below are relative to the repo root `D:\UnityProjects\endlesswinter`.

Cleaned 2026-10-14: one-off and superseded scripts were removed (promo*/solve*/stars* measurements,
old cap.py/.sh capture helpers, duplicate dumpers). Everything listed below is current.

---

## 1. LDPlayer driver — `tools/ld/ldctl.py` (start here)

One tool for playing the original game on LDPlayer: quick actions, smart capture,
an action log, contact sheets and wireframe storyboards.

### Basics
```bash
L="py tools/ld/ldctl.py"
$L up                       # start LDPlayer + game if no device
$L session ch6-ftue         # all output goes to UnityAssets/.temp/ld-captures/ch6-ftue/
$L shot intro               # capture now
$L tap dialog               # tap a named point (tools/ld/points.json)
$L tap 225 700 --s 2        # tap at preview coords (x2 = real 450,1400)
$L advance dialog --n 30    # tap until the screen stops reacting (dialog chains)
$L beat "Bahiti asks for wood"   # mark a story beat
$L story                    # wireframe storyboard of what happened
```

### Captures and coordinates
- The device is **900x1600**. Each action stores a **450x800 JPG preview** (`NNNN_name@2.jpg`,
  exact 9:16, scale 2). Real = preview x 2, always.
- **Save format: JPG by default, PNG only on request.** JPG is ~13x smaller and fine for looking,
  sheets, wireframes and diffs. Add `--full` to also keep an exact 900x1600 PNG when you need
  pixel-exact work (reading a UI colour, measuring a 1 px border, template matching).
- Older continuous-capture folders (`.temp/ld-ch*/frames`, `unity-ch1/frames`) were converted to
  450x800 JPG q90 on 2026-10-14 (coordinates in their logs stay in real 900x1600 px).
  `py tools/ld/to_jpg.py --width 450 <folder>` does the same for new ones.
- Commands that take coordinates read REAL pixels by default; add `--s 2` to give preview pixels.
- `grid [name]` draws a grid labelled in REAL pixels on the latest preview — read tap points off it.
- `where 225 700 --s 2` converts preview -> real.
- Named points live in `tools/ld/points.json` (`dialog`, `quest_bar`, `close_offer`, `center`). Add more as you find them.

### Smart waits (why it is fast)
`tap`, `swipe`, `back`, `advance` don't sleep a fixed time. They poll raw frames (~0.4 s each,
no PNG encoding) and return as soon as the screen **changed and then settled**:
- `CHANGED` — new screen, frame saved.
- `NOCHANGE` — nothing happened within `--timeout` (default 3 s); no frame saved (add `--keep` to save one).
- `MOVING` — still animating after `--max` (default 8 s); last frame saved.

Idle animations (snow, the city, timers) change less than 3% of the screen and are ignored.

### Watching for stalls
```bash
$L watch --interval 1 --stall 20 --on-stall notify
```
Prints one line per event, so it works with Claude's Monitor tool:
- `CHANGE 0042 change=0.31 bbox=[...] <frame>` — the screen moved on (frame saved + logged).
- `STALL 20s` — nothing changed for 20 s. Responses: `notify` (default), `shot`, `back`, `tap:POINT`, `exit`.
- `--min-change 0.03` sets how much of the screen must change to count.

### Log, sheets, storyboard
Each session folder holds:
| File | What |
|---|---|
| `log.jsonl` | one JSON line per action: index, time, kind, real coords, result, seconds, changed bbox, frame |
| `log.md` | the same, readable; `beat` = `##` heading, `note` = quote |
| `frames/` | `NNNN_<name>@2.jpg` previews (+ `.png` with `--full`) |
| `sheet_A-B.jpg` | `sheet [--from N --to N --cols 6]` — labelled contact sheet |
| `story_A-B.jpg` | `story [--from N --to N --cols 4]` — **wireframe storyboard** |

The storyboard shows, for every action, the screen it was done on with the action drawn on it:
red ring = tap, red arrow = swipe, blue box = what changed afterwards, yellow header = beat,
arrows between panels for the order, and the result/time under each panel
(green CHANGED, red NOCHANGE, orange MOVING).

Other commands: `home`, `text "..."`, `note "..."`, `points`, `session` (no arg = show current).

---

## 2. Wireframe from a continuous capture — `tools/ld/wire.py`

For folders recorded as a continuous frame stream (`frames/NNNNN_<ms>.png` + `actions.log` + `beats.md`,
e.g. `UnityAssets/.temp/ld-ch1`):
```bash
py tools/ld/wire.py UnityAssets/.temp/ld-ch1     # -> <folder>/wire/
```
- `actions_NN.jpg` — every logged action as **before -> after**: the tap/swipe drawn on the screen it was
  done on, the settled result next to it with the changed area boxed, the beat it belongs to as a yellow
  header, and `CHANGED +Ns / NOCHANGE` with the frame numbers.
- `beats_NN.jpg` — one panel per beat (first frame of its range) with the beat title.

## 3. Reading captures and the original's data

| File | What |
|---|---|
| `ld/view.py`, `map.py`, `probe.py`, `stats.py`, `rows.py`, `cols.py`, `edge.py` | read a capture as text: strips, ASCII map, pixel grid, colours, row/column runs |
| `ld/find.py` | find a template image inside a capture (where is this button) |
| `ld/cmp.py`, `ld/sbs.py` | compare an LD capture with a reference / Unity capture side by side |
| `ld/btnbox.py`, `card.py`, `cards.py`, `colorbox.py`, `lightbox.py`, `lightscan.py`, `panelbox.py` | measure boxes of buttons / cards / panels in a capture |
| `ld/i18n.py`, `ld/i18nrev.py` | look up English text by i18n key, or keys by text |
| `ld/luastr.py` | strings from compiled LuaJIT view modules |
| `ld/pflayout.py`, `pfdetail.py`, `pftmp.py` | original prefab: RectTransform tree / sprites + fonts / TMP texts |
| `ld/animcurve.py` | dump an `.anim` clip's curves |
| `ld/spritecut.py`, `ld/sprites.py` | cut a sprite from an atlas / check the Hero builder's sprites exist |
| `ui/dump_prefab.py`, `dump_prefab_sprites.py` | original UI prefab hierarchy with rects, sprites, texts |
| `ui/check_prefab_active.py` | root active flag of Hero UI prefabs |
| `ui/crop_sprites.py`, `sprite_sheet.py`, `has_sprites.py`, `sprite_border.py` | atlas sprites: crop, sheet, existence, 9-slice borders |
| `ui/import_atlas.py` | import an original atlas page + slices into Unity |
| `ui/locate_sprites.py`, `match_ref_sprites.py` | which atlas sprite matches a region of an LD screenshot |
| `ldplayer/peek.py` | print any LuaJSON config table (`peek.py <file.json> [keys]`) |
| `ldplayer/export_shots.py` | copy chosen captures into `.docs/ftue_screens` as JPGs |
| `art/draw_exclaim_bubble.py` | draws the FTUE exclaim bubble PNG |

## 4. Config -> Unity data (`tools/config/`)

Regenerate `Assets/_EndlessWinter/Data/...` from `ResourcesExport/Config/LuaJSON`.

| File | Output |
|---|---|
| `extract_chapters.py`, `extract_chapter4.py` | chapter quests / ch4 recruitment |
| `extract_city.py` | ch1-3 city data |
| `extract_cutscenes.py`, `show_cutscenes.py` | FTUE camp cutscenes JSON / print it |
| `extract_rescue_timeline.py` | ch3 rescue timeline cue sheets |
| `extract_plot_dialog.py` | FTUE story dialog |
| `extract_heroes.py`, `hero_unlock_table.py` | heroes.json / unlock table |
| `extract_i18n.py` | English string table |
| `extract_items.py`, `extract_growth.py`, `extract_stages.py`, `extract_survivor_daynight.py` | items, growth chains, exploration stages, day/night |
| `dump_guides.py`, `dump_timeline.py` | readable guide steps / timeline summary |
| `_grep_cfg.py` | grep every LuaJSON config for a substring |

## 5. Asset recovery pipeline (repo root `tools/`)

| File | What |
|---|---|
| `download_assets.py` (+ `test_download_assets.py`) | download missing files from the shipped manifest, hash-verified |
| `decrypt_bundles.py` | decode bundles with the game's reader in an offline ARM64 VM |
| `research_elf.py`, `research_il2cpp.py`, `research_metadata.py` | offline probes of the native code / metadata |
| `scan_assets.py`, `inventory_all_resources.py` | inventory asset packs and bundles |
| `extract_base_assets.py`, `extract_audio.py`, `extract_video.py`, `export_spine.py` | textures/fonts, audio, video, Spine |
| `export_resource_data.py`, `export_config_json.lua`, `config_json.py` | config + Lua export, strict JSON |
| `prepare_unity_3d.py`, `import_battle_fx.py`, `BuildRecoveredGallery.cs`, `ValidateRecoveredScenes.cs` | build Unity art library, battle FX, galleries |
| `verify_packages.py`, `build_delivery_report.py` | check exported packages, write the handoff report |
