# Unruled — how the project works now

Unity 6000.5.7f1. Design: `.docs/unruled_master_gdd.md`; numbers: `.docs/unruled_master_data_sheets.md`;
rules: `.claude/rules/architecture.md`; decisions and exceptions: `.docs/plan/roadmap.md`.

State after Sprint 3: the engine-free simulation core (economy, board, weather, latency, radar, fleets, salvo
combat, capital relocation) and its tests.
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
components, then rebuilds the graph. `CreateFleet(unit, site)` delegates to `FleetFactory`, which adds every fleet
component and an empty cargo hold. Orders enter through `CommandIssuer.Issue`, which stamps the signal latency
and queues them; when due, `SimClock` sends them through `CommandRouter` to the owning system.

## Tick order

1. `NoaaWeatherEngine`: expires and rolls storm fronts, writes `SiteWeather` and `LaneWeather`.
2. `MiningSystem`: ore into site `Stockpile`s.
3. `PlanetLifecycleSystem`: depletion states and ESI; also nuclear strikes and the `Terraform` command.
4. `RefinerySystem`: recipes and brownouts (reads `SiteWeather`).
5. `WhoQuarantineSystem`: export lock when water or oxygen runs out.
6. `FleetUpkeepSystem`: hourly per-ship upkeep from the cargo hold; `Mothball` / `Activate` and warm-up.
7. `FleetMovementSystem`: lane travel with fuel paid at departure; `Move`, `Hold`, `Reverse`.
8. `SalvoCombatEngine`: a salvo every 5 ticks between hostile fleets at one site or lane; `SetRof`, `SetFocus`,
   `VentCoolant`.
9. `CapitalRelocationSystem`: teardown → capital convoy → bootstrap; `RelocateBase`.
10. `FocusedRadarSystem`: scan timelines and the Tier-5 IAEA reveal; `ScanBelt`, `CancelScan`, `EcmJam`.

`MiningSystem` also handles `RetractDrills` and `PlanetLifecycleSystem` handles `Terraform`.

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
- **Economy/**: `ItemId`, `Stockpile`, `RecipeDatabase`, `MiningSystem` + `MineralDeposit` (drill retract:
  rigs freed 180 ticks after `RetractDrills`), `RefinerySystem` + `Refinery`.
- **Military/**:
  - `PlayerHq` (core base, capital convoy flag), `RelayStation`, `CapitalRelocation` (phase, convoy, cooldown,
    paralysis).
  - Fleet components (`FleetComponents.cs`), one owner each: `FleetUnit` (ship counts by class; combat),
    `FleetMotion` (site or lane progress; movement), `FleetReadiness` (mothball, warm-up, unsupplied; upkeep),
    `FleetCombatState` (ROF, focus, temperature, damage carry; combat), `FleetSensors` (reveal, RWR; radar). A
    fleet's `Stockpile` is its cargo hold. `FleetFactory` creates them all.
  - `FleetBalance` (tab 03 per tier), `CombatBalance`, `RelocationBalance`.
  - `FleetUpkeepSystem`, `FleetMovementSystem`, `SalvoCombatEngine` (Psi − Omega with fortification, flank,
    tender/vault resupply, thermal lock and cook-off), `CapitalRelocationSystem` (hostile lock, escort, upfront
    costs, convoy loss → order paralysis, 48 h cooldown).
  - `SignalLatencySolver`: Dijkstra over lanes, (15 + hops × 35, or × 8 when relayed) × 2 during relocation; an
    R3+ storm blocks the order.
  - `CommandIssuer` (refuses every order while the player is paralysed).
  - `FocusedRadarSystem` + `RadarScan` + `RadarBalance`.
- **SystemFactions/**: `WhoQuarantineSystem` + `SiteQuarantine`.

## Tests

`Assets/Tests/Core/` covers the clock, ECS, maths, generators, graph, weather, lifecycle, mining, refinery,
quarantine, latency, radar, fleet movement/upkeep, salvo combat, drill retract, relocation and an end-to-end `SimulationWorld` run (same seeds → same state).
`DataSheetConsistencyTests` parses the markdown data sheet (tabs 01, 02, 04) and fails if `DefaultGameBalance`
drifts from it (tab 03 included). Run EditMode tests for `Unruled.Tests.Core`.

## Tooling

- KitWright MCP is embedded at `Packages/com.kitwright.unity.mcp` with one patched `#if` for Unity 6000.5
  (see roadmap Exceptions). Its MCP config files carry an access token and are gitignored.
- Agent rule files are generated from `.agents/templates/` + `project.config` via `python .agents/sync-rules.py`.
