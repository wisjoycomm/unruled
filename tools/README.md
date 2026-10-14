# tools/ — what is here

Run Python with `python` (or `py`).
All scripts load project paths, package names, and emulator settings from `project.config` (and optional `.env` / environment variable overrides) via `tools/project_cfg.py`.

Cleaned 2026-10-14: one-off and superseded scripts were removed (promo*/solve*/stars* measurements,
old cap.py/.sh capture helpers, duplicate dumpers). Everything listed below is current and project-agnostic.

---

## 1. LDPlayer driver — `tools/ld/ldctl.py` (start here)

One tool for playing a reference game on LDPlayer: quick actions, smart capture,
an action log, contact sheets and wireframe storyboards.

### Basics
```bash
L="python tools/ld/ldctl.py"
$L up                       # start LDPlayer + game if no device
$L session ch6-ftue         # all output goes to .temp/ld-captures/ch6-ftue/
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
e.g. `.temp/ld-ch1`):
```bash
python tools/ld/wire.py .temp/ld-ch1     # -> <folder>/wire/
```
- `actions_NN.jpg` — every logged action as **before -> after**: the tap/swipe drawn on the screen it was
  done on, the settled result next to it with the changed area boxed, the beat it belongs to as a yellow
  header, and `CHANGED +Ns / NOCHANGE` with the frame numbers.
- `beats_NN.jpg` — one panel per beat (first frame of its range) with the beat title.

## 3. Reading captures and reference assets

All capture analysis scripts in `tools/ld/` automatically analyze at **450x800** (`_img.py`, exact 9:16 ratio) using vectorized `numpy` and scale coordinates back to real device pixels and `640x1136` design units.

| File | What |
|---|---|
| `ld/_img.py` | shared `450x800` NumPy loader (`load_cap_np`), path resolver, and `bright_bbox` |
| `ld/view.py`, `stats.py`, `rows.py`, `cols.py`, `edge.py` | inspect a capture: strips/2x zoom box, luminance/dominant colours, row/column runs, colour steps |
| `ld/find.py` | FFT normalised cross-correlation template matcher inside a capture |
| `ld/cmp.py`, `ld/sbs.py` | compare an LD capture with a reference / Unity capture side by side |
| `ld/btnbox.py`, `cards.py`, `colorbox.py`, `lightbox.py`, `lightscan.py`, `panelbox.py` | measure bounding boxes of buttons / cards / colour regions / light panels in a capture |
| `ld/i18n.py`, `ld/i18nrev.py` | look up localization text by i18n key, or keys by text |
| `ld/luastr.py` | extract string constants from compiled LuaJIT bytecode modules |
| `ld/animcurve.py` | dump an `.anim` clip's transform/float curves |
| `ui/dump_prefab.py`, `dump_prefab_sprites.py` | dump Unity UI prefab hierarchy with rects, anchors, sprites, and TMP texts |
| `ui/crop_sprites.py`, `sprite_sheet.py`, `has_sprites.py`, `sprite_border.py` | atlas sprites: crop, contact sheet, existence check, 9-slice borders |
| `ui/import_atlas.py` | import an atlas page + sprite slices into Unity |
| `ui/locate_sprites.py`, `match_ref_sprites.py` | match which atlas sprite corresponds to a region of a reference screenshot |

## 4. Config & localization utilities (`tools/config/`)

| File | What |
|---|---|
| `_common.py` | shared LuaJSON loader (`load_lua_json`), cached localization lookup (`load_i18n`, `i18n_text`), and JSON writer (`write_json`) |
| `extract_i18n.py` | extract localization string table from LuaJIT bytecode into `${RESOURCES_CONFIG_DIR}/i18n/en.json` |
| `dump_timeline.py` | summarize an exported Unity `TimelineAsset` (`.playable`): tracks, clips, timing, and referenced assets |
| `_grep_cfg.py` | grep every LuaJSON config table in `${LUA_CONFIG_DIR}` for a substring |

## 5. Core RE & asset recovery pipeline (repo root `tools/`)

| File | What |
|---|---|
| `project_cfg.py` | central configuration loader (`project.config` + `.env` overrides) |
| `arm64_vm.py` | shared Unicorn ARM64 ELF constructor emulator and `.dynsym` import stub resolver |
| `unityfs.py` (+ `test_unityfs.py`) | shared `UnityFS` bundle header parser and LZ4/LZMA block decompressor |
| `unity_yaml.py` (+ `test_unity_yaml.py`) | shared Unity YAML document parser, parallel `.meta` GUID cache, and mtime-cached Sprite Atlas indexer |
| `download_assets.py` (+ `test_download_assets.py`) | download missing files from a shipped manifest, hash-verified |
| `decrypt_bundles.py` | multi-core (`ProcessPoolExecutor`) bundle decrypter using the game's reader in an offline ARM64 VM |
| `research_elf.py`, `research_il2cpp.py`, `research_metadata.py` | offline probes of native `.so` constructors and IL2CPP `global-metadata.dat` |
| `scan_assets.py`, `inventory_all_resources.py` | multi-threaded inventory of asset packs and bundles |
| `extract_base_assets.py`, `extract_audio.py`, `extract_video.py`, `export_spine.py` | extract textures/fonts, audio, video, and Spine skeletons |
| `export_resource_data.py`, `export_config_json.lua`, `config_json.py` | export ScriptableObjects and Lua config tables to strict JSON |
| `prepare_unity_3d.py`, `BuildRecoveredGallery.cs`, `ValidateRecoveredScenes.cs`, `verify_packages.py` | build and validate recovered Unity 3D prefabs, scenes, and `.unitypackage` archives |

