# Architecture rules (Unruled)

Every code task follows these rules. The locked design and data tables are in
`.docs/unruled_master_gdd.md` and `.docs/unruled_master_data_sheets.md`; the roadmap is in `.docs/plan/roadmap.md`; how the
project works today is in `.docs/overview.md`. Keep this file, the overview and the
roadmap in agreement.

In one line: **data in config, rules in a pure C# ECS (`Unruled.Core`), state in one place,
views (`Unruled.View`) only draw, and every change goes through one door.**

## Rules

1. **Views only draw.** A MonoBehaviour or UI Toolkit controller in `Unruled.View` shows
   state and forwards player commands to the simulation/service layer. It never computes
   economic recipes, combat outcomes or weather rolls, and never mutates simulation state
   directly. UI uses Unity UI Toolkit (`.uxml` / `.uss`) with 100% opaque backgrounds
   (no blur) and high-contrast text; board rendering uses `LineRenderer`/Mesh on a
   single-layer astronomical plane.
2. **Rules are pure C#.** Simulation, board, economy, military and system-faction rules
   live in `Unruled.Core` (`Assets/Scripts/Core/`) with `noEngineReferences: true` (no
   `UnityEngine`). Pass in deterministic tick (`SimClock`) and seed. Every new rule gets
   an EditMode NUnit test in `Assets/Tests/Core/`.
3. **One door for every change.** Simulation state changes only through commands queued
   into `SimClock` / authoritative handlers. UI and renderers never write simulation
   fields directly.
4. **One owner per piece of state.** Board (`SiteNode`, `StarlaneEdge`), weather
   (`NoaaWeatherEngine`), economy (`WtoMarketEngine`, `ConvoyRouter`), military
   (`FleetUnit`, `SupplyLineSolver`, `LanchesterCombat`), and system factions
   (`SystemAuthorityManager`, `SystemDefenseEvaluator`, `VictoryEvaluator`): each domain
   has one owning system, and each component type has one owning system that defines its
   invariants. Shared resource components (e.g. `Stockpile`) change only through their own
   methods, inside systems running in the fixed `SimClock` order. Others read state or
   subscribe to events.
5. **No hidden lookups.** No new `static Current`, singletons, `FindAnyObjectByType`
   or `FindObjectsByType` in game code. Core classes take dependencies in their
   constructor; views get them via explicit `Bind(...)`.
6. **Numbers come from config.** Balance numbers, 20-item recipes (`10 RAW_*` → `10 ITEM_*`),
   ESI thresholds, NOAA storm scales and tech gate requirements come from
   `GameBalanceConfig` / `RecipeDatabase` and `.docs/unruled_master_data_sheets.md`. No magic balance
   literals scattered in logic.
7. **Time is deterministic (`1 Tick = 1 Second`).** Track simulation time in integer
   ticks via `SimClock` (`60 Ticks = 1m`, `3,600 Ticks = 1h`, `86,400 Ticks = 1d`), never
   floating-point `Time.time` countdowns inside `Unruled.Core`.
8. **Modules stay decoupled.** Subsystems in `Unruled.Core` interact through well-defined
   domain interfaces/structs and events.
9. **Saved and network data only grows.** MessagePack int `[Key(n)]` only; only
   append keys, never reuse, renumber or remove one. Bump `SchemaVersion` and add a
   migration test when a shape changes.
10. **Multi-platform & AOT safe.** Keep code compatible with WebGL, Desktop and Mobile
    IL2CPP/HybridCLR: no dynamic MessagePack resolvers (use generated resolvers) and no
    reflection-built unconstrained open generics.
11. **Dependencies point one way:** `Unruled.View → Unruled.Core`. `Unruled.Core` has
    `noEngineReferences: true` and never references `Unruled.View` or `UnityEngine`.
    Editor tooling stays in `Assets/Editor/` (`Unruled.Editor`).
12. **Simulation is a hand-rolled pure C# ECS.** Built for performance and so it can be
    ported to Rust later. Entities are `EntityId` (int index + generation). Components
    are blittable `struct`s (no object references, no managed arrays; variable-size data
    lives in its own store) kept in dense sparse-set arrays (`ComponentStore<T>`). Systems
    implement `ISimSystem`, keep no hidden state, run on `SimClock` in a fixed explicit
    order, and iterate in ascending entity order for determinism. No class-based
    entities, inheritance trees, LINQ, reflection, virtual calls or allocations inside
    system loops. Simulation quantities are integers or fixed-point (e.g. ESI as `int`
    milli-units); `float` only in `Unruled.View`. No Unity DOTS (`Unity.Entities`, Burst)
    in `Unruled.Core`; DOTS may appear only in `Unruled.View` for rendering, after profiling
    shows the need and the roadmap records it. No third-party ECS library without approval.

## Where code goes

Paths are under `Assets/Scripts/` (plus `Assets/Editor/` and `Assets/Tests/Core/`).

| Writing | Folder | Assembly |
|---|---|---|
| Tick clock, command queue & balance config | `Assets/Scripts/Core/Simulation/` (`SimClock.cs`, `GameBalanceConfig.cs`) | `Unruled.Core` |
| ECS core: entity ids, component stores, world | `Assets/Scripts/Core/Ecs/` (`EntityId.cs`, `ComponentStore.cs`, `World.cs`) | `Unruled.Core` |
| Map sites (ESI, 3 mining depths), starlanes & NOAA weather (`R/S/G 1–5`) | `Assets/Scripts/Core/Board/` (`SiteNode.cs`, `StarlaneEdge.cs`, `NoaaWeatherEngine.cs`) | `Unruled.Core` |
| 20 Items (`10 RAW_*`, `10 ITEM_*`), Leontief recipes, convoys & WTO market | `Assets/Scripts/Core/Economy/` (`ItemId.cs`, `RecipeDatabase.cs`, `ConvoyRouter.cs`, `WtoMarketEngine.cs`) | `Unruled.Core` |
| 3-part fleets (Line/EW/Supply), supply lines & Lanchester salvo combat | `Assets/Scripts/Core/Military/` (`FleetUnit.cs`, `SupplyLineSolver.cs`, `LanchesterCombat.cs`) | `Unruled.Core` |
| `THE SYSTEM` (STA, WTO, WHO, IAEA, NOAA), defense tiers & victory evaluator | `Assets/Scripts/Core/SystemFactions/` (`SystemAuthorityManager.cs`, `SystemDefenseEvaluator.cs`, `VictoryEvaluator.cs`) | `Unruled.Core` |
| 1-layer astronomical board renderer (`LineRenderer`/Mesh) | `Assets/Scripts/View/` (`MapBoardRenderer.cs`) | `Unruled.View` |
| UI Toolkit 4-zone HUD (`.uxml`/`.uss`: TopTelemetry, LeftInventory, ContractAndMarket) | `Assets/Scripts/View/UI/` (`TopTelemetryHud.cs`, `ContractAndMarketUI.cs`) | `Unruled.View` |
| Editor builders, importers, menus | `Assets/Editor/` | `Unruled.Editor` |
| EditMode NUnit tests (simulation, economy, combat, system factions) | `Assets/Tests/Core/<Feature>/` | `Unruled.Tests.Core` |

## Before finishing a task (the one checklist)

- [ ] `request_recompile` shows no compile errors; the related EditMode NUnit tests pass.
- [ ] The diff adds no `.Current`, `Find*Object*`, singleton or upward dependency
      (`grep -nE "\.Current\b|Find(Any)?Object" <changed files>`).
- [ ] Pure simulation logic is in `Unruled.Core` (zero `UnityEngine` references) and tested; state changes go through `SimClock` / owning engine.
- [ ] New simulation state is ECS: blittable struct components in a `ComponentStore<T>`, one owning system, integer/fixed-point math, no allocations in system loops.
- [ ] Asmdef references still point one way (`Unruled.View → Unruled.Core`).
- [ ] Changed C# meets the checklist in `unity-perf.md`.
- [ ] Saved/network data changes: keys only appended, `SchemaVersion` bumped, migration test added.
- [ ] The diff has no accidental or unrelated changes.

## Exceptions

When a rule blocks a task, don't bend it silently. Record the exception and the
reason in `.docs/plan/roadmap.md`, and tell the user.
