# Unruled — how the project works now

Unity 6000.5.7f1. Design: `.docs/unruled_master_gdd.md`; numbers: `.docs/unruled_master_data_sheets.md`;
rules: `.claude/rules/architecture.md`; decisions and exceptions: `.docs/plan/roadmap.md`.

State after Sprint 2: the engine-free simulation core (economy, board, weather, latency, radar) and its tests.
No views, scenes or networking yet.

## Assemblies

| Assembly | Folder | Notes |
|---|---|---|
| `Unruled.Core` | `Assets/Scripts/Core/` | `noEngineReferences`, `allowUnsafeCode` (fixed buffers). All rules and state. |
| `Unruled.View` | `Assets/Scripts/View/` | Empty. References Core only. |
| `Unruled.Editor` | `Assets/Editor/` | Empty. Editor-only. |
| `Unruled.Tests.Core` | `Assets/Tests/Core/` | EditMode NUnit tests, one folder per Core folder. |

## Composition

`SimulationWorld(GameBalanceConfig, worldSeed)` is the composition root. It creates the `World`, every component
store, the `StarlaneGraph`, every system and the `CommandRouter`, and registers the systems on `SimClock` in the
fixed order below. `GenerateSystem(systemSeed, clusterId)` creates a star system's sites, lanes and weather
components, then rebuilds the graph. Orders enter through `CommandIssuer.Issue`, which stamps the signal latency
and queues them; when due, `SimClock` sends them through `CommandRouter` to the owning system.

## Tick order

1. `NoaaWeatherEngine`: expires and rolls storm fronts, writes `SiteWeather` and `LaneWeather`.
2. `MiningSystem`: ore into site `Stockpile`s.
3. `PlanetLifecycleSystem`: depletion states and ESI; also nuclear strikes and the `Terraform` command.
4. `RefinerySystem`: recipes and brownouts (reads `SiteWeather`).
5. `WhoQuarantineSystem`: export lock when water or oxygen runs out.
6. `FocusedRadarSystem`: scan timelines; handles `ScanBelt`, `CancelScan`, `EcmJam` and `Reverse`.

In each tick, due commands run before the systems. Shared components (`Stockpile`) change only through their own
methods, in this order (rule 4).

## Core folders

- **Simulation/**: `SimTick`, `SimClock`, `DelayedPlayerCommand` (target = entity index + generation),
  `CommandRouter`, `SimulationWorld`. `SplitMix64` is the only RNG; `FixedMath` provides Q16 sin/cos and an exact
  integer sqrt. `GameBalanceConfig` holds every balance object, and `DefaultGameBalance` is the one place sheet
  values are written.
- **Ecs/**: `EntityId`, `World` (create/destroy, owns the stores) and `ComponentStore<T>` (sorted sparse set,
  iterated in ascending entity order).
- **Board/**:
  - `SiteInfo` (position, ESI in milli-units, lifecycle, `IrradiatedUntilTick`).
  - `SystemSeedGenerator` (site layout) and `PlanetLifecycleSystem` + `LifecycleBalance`.
  - `StarlaneEdge` (3 tiers), `StarlaneBalance` (tab 04 rules), `StarlaneGenerator` (orbit-chain highways plus
    seeded bypasses and shortcuts) and `StarlaneGraph` (CSR adjacency).
  - `StormFront`, `SiteWeather`, `LaneWeather`, `NoaaWeatherEngine` + `NoaaBalance`.
- **Economy/**: `ItemId`, `Stockpile`, `RecipeDatabase`, `MiningSystem` + `MineralDeposit`, `RefinerySystem` +
  `Refinery`.
- **Military/**:
  - `PlayerHq` (core base, capital convoy flag), `RelayStation`, and `FleetPresence` (a placeholder until Sprint 3).
  - `SignalLatencySolver`: Dijkstra over lanes, (15 + hops × 35, or × 8 when relayed) × 2 during relocation; an
    R3+ storm blocks the order.
  - `CommandIssuer`.
  - `FocusedRadarSystem` + `RadarScan` + `RadarBalance`.
- **SystemFactions/**: `WhoQuarantineSystem` + `SiteQuarantine`.

## Tests

`Assets/Tests/Core/` covers the clock, ECS, maths, generators, graph, weather, lifecycle, mining, refinery,
quarantine, latency, radar and an end-to-end `SimulationWorld` run (same seeds → same state).
`DataSheetConsistencyTests` parses the markdown data sheet (tabs 01, 02, 04) and fails if `DefaultGameBalance`
drifts from it. Run EditMode tests for `Unruled.Tests.Core`.

## Tooling

- KitWright MCP is embedded at `Packages/com.kitwright.unity.mcp` with one patched `#if` for Unity 6000.5
  (see roadmap Exceptions). Its MCP config files carry an access token and are gitignored.
- Agent rule files are generated from `.agents/templates/` + `project.config` via `python .agents/sync-rules.py`.
