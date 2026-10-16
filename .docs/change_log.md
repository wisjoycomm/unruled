# Change Log

- 2026-10-14 - Antigravity - Centralize agent rules (`CLAUDE.md`, `AGENTS.md`, `.claude/`, `.agents/`, `.codex/`, `.cursor/`) via `project.config`, `.agents/templates/`, and `.agents/sync-rules.py` for the Unruled architecture.
- 2026-10-14 - Antigravity - Refactor `tools/` (LDPlayer driver, UI/art/sprite helpers, config extractors, asset recovery pipeline, C# editor validators, and `tools/README.md`) to be project-agnostic using `project.config` and `tools/project_cfg.py`.
- 2026-10-14 - Antigravity - Optimize and deduplicate `tools/` by centralizing intermediate directories in `project.config`, extracting shared modules (`tools/arm64_vm.py`, `tools/unityfs.py`, `tools/unity_yaml.py`, `tools/config/_common.py`, `tools/ld/_img.py`), removing tracked `tools/ui/.guid_cache.json`, and adding unit tests.
- 2026-10-14 - Antigravity - Optimize `tools/` runtime performance: add multi-core `ProcessPoolExecutor` VM bundle decryption (`decrypt_bundles.py`), mtime-validated Sprite Atlas caching and parallel `.meta` GUID scanning (`unity_yaml.py`), and ratio-preserving half-resolution NumPy vectorization (`tools/ld/_img.py`, `btnbox.py`, `card.py`, `colorbox.py`, `lightbox.py`, `stats.py`).
- 2026-10-14 - Antigravity - Remove 31 legacy/game-specific and duplicate files (~6.9 MB) from `tools/` (`source_manifest.json`, `LuaJIT-v2.1.tar.gz`, 15 game-specific `tools/config/extract_*` scripts, `import_battle_fx.py`, `build_delivery_report.py`, `tools/art/`, `tools/ldplayer/`, and duplicate `tools/ld/` & `tools/ui/` scripts).
- 2026-10-14 - Antigravity - Generalize core RE scripts (`arm64_vm.py`, `export_config_json.lua`, `export_resource_data.py`, `extract_i18n.py`, `BuildRecoveredGallery.cs`) by removing hardcoded Whiteout Survival constants and merging wrapper scripts (`research_elf.py`, `research_il2cpp.py`, `ld/i18n.py`, `ld/i18nrev.py`, `ld/luastr.py`) and completed `.docs/plan/tools-*.md` files.
- 2026-10-14 - Antigravity - Remove legacy Endless Winter docs (`.docs/overview.md`, `.docs/plan/roadmap.md`, `.docs/plan/backend-api.md`) and empty leftover directories (`tools/art/`, `tools/ldplayer/`, stale `__pycache__`).


- 2026-10-16 - Claude - Restore Unruled rule files; adopt GDD v3.1 as `.docs/unruled_master_gdd.md` and export the v3.0 xlsx to `.docs/unruled_master_data_sheets.md`.
- 2026-10-16 - Claude - Create Unity 6000.5.7f1 project, four asmdefs (Core/View/Editor/Tests.Core), embed KitWright MCP with a Unity 6000.5 compile fix.
- 2026-10-16 - Claude - Add SimTick, SimClock deterministic tick loop and delayed command queue with EditMode tests.
- 2026-10-16 - Claude - Add architecture rule 12 (pure C# ECS, fixed-point, no DOTS in Core) and start the roadmap.
- 2026-10-16 - Claude - Add hand-rolled ECS core (EntityId, World, sorted sparse-set ComponentStore) with EditMode tests.
- 2026-10-16 - Claude - Add ItemId (20 items) and fixed-buffer Stockpile ECS component; enable unsafe code in Unruled.Core.
- 2026-10-16 - Claude - Add GameBalanceConfig, DefaultGameBalance and RecipeDatabase (20 items, 10 recipes, base prices) with a data-sheet consistency test.
- 2026-10-16 - Claude - Add SplitMix64, FixedMath integer trig, SiteInfo component and deterministic SystemSeedGenerator with provisional layout defaults.
- 2026-10-16 - Claude - Add RefinerySystem (idle/active/brownout), SiteWeather, WhoQuarantineSystem; amend rule 4 for shared components.
- 2026-10-16 - Claude - Add MiningSystem and MineralDeposit (3 depths, fuel/coolant cost, planet lifecycle ESI drops) with tests.
- 2026-10-16 - Claude - Add .docs/overview.md describing the Sprint 1 core structure and tick order.
- 2026-10-16 - Claude - Sprint 1 plan finished; archived to .docs/plan/sprint-1.md.
- 2026-10-16 - Claude - Add SimulationWorld composition root (fixed system order) and CommandRouter.
- 2026-10-16 - Claude - Add PlanetLifecycleSystem (depletion, nuclear strike 3B, Terraform command 4); move lifecycle out of MiningSystem; add command target generation.
- 2026-10-16 - Claude - Add starlane tiers, StarlaneGraph (CSR), StarlaneGenerator, FixedMath.Sqrt and SimulationWorld.GenerateSystem.
- 2026-10-16 - Claude - Add NoaaWeatherEngine (seeded storm fronts, site and lane weather, irradiation S4) and world seed on SimulationWorld.
- 2026-10-16 - Claude - Add SignalLatencySolver (Dijkstra relay hops, relocation x2, R3 blackout), CommandIssuer, PlayerHq/RelayStation/FleetPresence.
- 2026-10-16 - Claude - Add FocusedRadarSystem (2-battery surge, RWR at 15, reveal at 60/120 with ECM, cancel, reverse) and its commands.
