# Unruled — how the project works now

Unity 6000.5.7f1. Design: `.docs/unruled_master_gdd.md`; numbers: `.docs/unruled_master_data_sheets.md`;
rules: `.claude/rules/architecture.md`; decisions and exceptions: `.docs/plan/roadmap.md`.

State after Sprint 1: the engine-free simulation core and its tests. No views, scenes or networking yet.

## Assemblies

| Assembly | Folder | Notes |
|---|---|---|
| `Unruled.Core` | `Assets/Scripts/Core/` | `noEngineReferences`, `allowUnsafeCode` (fixed buffers). All rules and state. |
| `Unruled.View` | `Assets/Scripts/View/` | Empty. References Core only. |
| `Unruled.Editor` | `Assets/Editor/` | Empty. Editor-only. |
| `Unruled.Tests.Core` | `Assets/Tests/Core/` | EditMode NUnit tests, one folder per Core folder. |

## Core folders

- **Simulation/** — `SimTick` (1 tick = 1 s), `SimClock` (advances ticks; runs queued `DelayedPlayerCommand`s by
  `ExecuteAtTick` then enqueue order, then every `ISimSystem` in registration order), `ICommandHandler`.
  `SplitMix64` (the only RNG) and `FixedMath` (Q16 integer sin/cos, 65536 binary angle units).
  `GameBalanceConfig` holds every balance object; `DefaultGameBalance` is the one place sheet values are written.
- **Ecs/** — hand-rolled ECS: `EntityId` (index + generation, `default` = null), `World` (create/destroy with a free
  list, owns the stores), `ComponentStore<T>` (sorted sparse set of blittable structs, iterated by index in
  ascending entity order for determinism).
- **Board/** — `SiteInfo` component (position, ESI in milli-units, `PlanetLifecycleState`), `SiteWeather`
  (NOAA R/S/G levels), `SystemSeedGenerator` (seed → deterministic polar layout of site entities, min spacing
  and habitable-zone ESI), `UniverseGenBalance`.
- **Economy/** — `ItemId` (10 `RAW_*`, 10 `ITEM_*`), `Stockpile` (fixed int[20] per site; throws on negative or
  overflow), `RecipeDatabase` (items, prices, ESI bands, 10 recipes). `MiningSystem` + `MineralDeposit`
  (surface → deep crust → core fracture, fuel/coolant cost, lifecycle ESI drops). `RefinerySystem` + `Refinery`
  (10-tick cycle: NotStarted / IdleStarved / ActiveProduction / GridBrownout, upkeep accrued on the component).
- **SystemFactions/** — `WhoQuarantineSystem` + `SiteQuarantine` (site stockpile out of `RAW_WATER` or
  `ITEM_OXYGEN` for more than 30 ticks → export lock for 300 ticks).

## Tick order

Intended `SimClock.Register` order (systems are only wired one at a time in tests so far; there is no
composition root yet):

1. `MiningSystem` — raw ore into the site `Stockpile`.
2. `RefinerySystem` — recipes consume raw items, produce `ITEM_*`.
3. `WhoQuarantineSystem` — reads the stockpile after production, sets `SiteQuarantine`.

Shared components (`Stockpile`) are changed only through their own methods, in this order (rule 4).

## Tests

`Assets/Tests/Core/` covers SimClock ordering, ECS lifecycle, deterministic maths, the generator, stockpile,
recipes, mining, refinery and quarantine. `DataSheetConsistencyTests` parses the markdown data sheet and fails
if `DefaultGameBalance` drifts from it. Run EditMode tests for `Unruled.Tests.Core`.

## Tooling

- KitWright MCP is embedded at `Packages/com.kitwright.unity.mcp` with one patched `#if` for Unity 6000.5
  (see roadmap Exceptions). Its MCP config files carry an access token and are gitignored.
- Agent rule files are generated from `.agents/templates/` + `project.config` via `python .agents/sync-rules.py`.
