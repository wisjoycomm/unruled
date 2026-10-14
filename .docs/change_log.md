# Change Log

- 2026-10-14 - Antigravity - Centralize agent rules (`CLAUDE.md`, `AGENTS.md`, `.claude/`, `.agents/`, `.codex/`, `.cursor/`) via `project.config`, `.agents/templates/`, and `.agents/sync-rules.py` for the Unruled architecture.
- 2026-10-14 - Antigravity - Refactor `tools/` (LDPlayer driver, UI/art/sprite helpers, config extractors, asset recovery pipeline, C# editor validators, and `tools/README.md`) to be project-agnostic using `project.config` and `tools/project_cfg.py`.
- 2026-10-14 - Antigravity - Optimize and deduplicate `tools/` by centralizing intermediate directories in `project.config`, extracting shared modules (`tools/arm64_vm.py`, `tools/unityfs.py`, `tools/unity_yaml.py`, `tools/config/_common.py`, `tools/ld/_img.py`), removing tracked `tools/ui/.guid_cache.json`, and adding unit tests.
- 2026-10-14 - Antigravity - Optimize `tools/` runtime performance: add multi-core `ProcessPoolExecutor` VM bundle decryption (`decrypt_bundles.py`), mtime-validated Sprite Atlas caching and parallel `.meta` GUID scanning (`unity_yaml.py`), and ratio-preserving half-resolution NumPy vectorization (`tools/ld/_img.py`, `btnbox.py`, `card.py`, `colorbox.py`, `lightbox.py`, `stats.py`).
- 2026-10-14 - Antigravity - Remove 31 legacy/game-specific and duplicate files (~6.9 MB) from `tools/` (`source_manifest.json`, `LuaJIT-v2.1.tar.gz`, 15 game-specific `tools/config/extract_*` scripts, `import_battle_fx.py`, `build_delivery_report.py`, `tools/art/`, `tools/ldplayer/`, and duplicate `tools/ld/` & `tools/ui/` scripts).
- 2026-10-14 - Antigravity - Generalize core RE scripts (`arm64_vm.py`, `export_config_json.lua`, `export_resource_data.py`, `extract_i18n.py`, `BuildRecoveredGallery.cs`) by removing hardcoded Whiteout Survival constants and merging wrapper scripts (`research_elf.py`, `research_il2cpp.py`, `ld/i18n.py`, `ld/i18nrev.py`, `ld/luastr.py`) and completed `.docs/plan/tools-*.md` files.
- 2026-10-14 - Antigravity - Remove legacy Endless Winter docs (`.docs/overview.md`, `.docs/plan/roadmap.md`, `.docs/plan/backend-api.md`) and empty leftover directories (`tools/art/`, `tools/ldplayer/`, stale `__pycache__`).


