# LDPlayer & Unity MCP reference

To compare UI, tap flow, or on-screen numbers with a reference game on Android, use the LDPlayer that is already running. Do not use other tools to read process memory, hook the game, or decrypt assets.

## LDPlayer

- adb: `D:\LDPlayer\LDPlayer14\adb.exe`
- Reference game package: `com.gof.global`
- Before tapping, run `adb devices` to confirm the emulator is connected.
- Tap and capture with ldctl: `py tools/ld/ldctl.py`, run from the repo root, not raw adb. Read section 1 of `tools/README.md` before first use.
  - Start each run with `session <name>`; every action is logged to `.temp/ld-captures/<name>/` (log.jsonl, log.md, frames/).
  - `tap X Y`, `back`, `swipe`, `advance dialog` wait until the screen changed and settled, and return `CHANGED` / `NOCHANGE` / `MOVING`. `NOCHANGE` means the tap missed: check `grid`, then tap again.
  - Previews are 450x800 (`@2`): real coords = preview coords x 2, or pass `--s 2`. Read only the `@2` previews. Captures are saved as JPG; add `--full` for an exact 900x1600 PNG only when you need pixel-exact colours or borders.
  - Mark story beats with `beat "..."`; at the end of a run, make `story` (wireframe) and `sheet`.
  - For long waits (cutscenes, loading), run `watch --stall 20` under Monitor instead of fixed sleeps.
  - For continuous capture folders (frames + actions.log + beats.md, e.g. `.temp/ld-ch1`), run `py tools/ld/wire.py <folder>` for before/after wireframes.
- Read only what is visible on screen. For numbers you can't see, ask the user; don't infer them from the game's hidden files.

## Unity MCP

- Tool rules: `AGENTS.md` (KitWright section) and the skill `.claude/skills/unity-mcp-workflow`.
- The connection is the open project's own `.mcp.json` (Claude Code / Antigravity; Cursor uses `.cursor/mcp.json`; Codex syncs `.codex/config.toml`). KitWright writes it while that project's Unity Editor is running. Keep it; never replace it with another project's mcp.json.
- LDPlayer and Unity MCP are separate channels. LDPlayer shows the Android emulator. Unity MCP shows the edited project's (`D:\GameProjects\unruled`) hierarchy, prefabs, Play Mode and screenshots.
- The edited project's Unity Editor must be open for MCP to return data.
- Compare reference screenshots with Unity Play Mode screenshots. Don't change UI just because an image looks different until you have read the actual values through MCP.
