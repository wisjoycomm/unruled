# Unruled roadmap

Where the project is heading, why, and rule exceptions. Build order follows GDD §5 (Sprint 1–5).

## Decisions

- 2026-10-16 — **Simulation is a hand-rolled pure C# ECS in `Unruled.Core`, not Unity DOTS.** Performance with flat struct arrays, while staying deterministic, engine-free (runs on the headless Server 2 and WebGL) and portable to Rust later; DOTS would tie the sim to Unity APIs and non-deterministic Burst floats. DOTS is allowed only in `Unruled.View` for rendering if profiling demands it.
- 2026-10-16 — **Fixed-point instead of the GDD §3 `float` fields** (`BaseEsi`, `PosX/PosY`, `HullIntegrityPct`, …). The GDD class shapes become ECS components with integer/milli-unit values for cross-platform determinism.
- 2026-10-16 — **System generator uses SplitMix64 only (GDD says SplitMix64 + ChaCha8) and integer trig (`FixedMath`).** ChaCha8 adds no gameplay value; integer math keeps layouts bit-identical across platforms and in a Rust port. The RNG draw order is part of the save contract.
- 2026-10-16 — **Universe layout numbers are provisional** (`DefaultGameBalance.CreateUniverseGenBalance`: 8 orbits, r 300 + 180·i ± 25, R_HZ 840, W_HZ 700, ±0.05 atmo noise, ±10° jitter); only the 120 min spacing is from the GDD. Move them into the data sheet when designed.
- 2026-10-16 — **WHO quarantine is a per-site system (`WhoQuarantineSystem`), not a refinery state.** GDD §2.2 A.4 says it locks export launches off the planet, so production keeps running; exports check `SiteQuarantine.IsExportLocked`.
- 2026-10-16 — **Shared components (e.g. `Stockpile`) may be changed by several systems** through their own methods, in the fixed `SimClock` order (rule 4 amended). Refinery upkeep is accrued in `Refinery.AccruedUpkeep` until a credits ledger exists; base upkeep 10/cycle and 1 battery per storm cycle are provisional.
- 2026-10-16 — **Mining numbers are partly provisional** (`DefaultGameBalance.CreateMiningBalance`): depth speeds, fuel/coolant multipliers and ESI drops come from data sheet tab 02; the 10-tick cycle, 10 ore and 1 fuel + 1 coolant per rig per cycle are proposed until the sheet defines them. Depths are mined surface → deep crust → core fracture (only when unlocked), never mixed in one cycle.
- 2026-10-16 — **`PlanetLifecycleSystem` owns site lifecycle and ESI** (moved out of `MiningSystem`, rule 4). A nuclear strike enters through `ApplyNuclearStrike`, called by the Sprint 3 combat engine, not by a player command, so it can't be fired for free. Terraform is a command, paid from the site stockpile; its cost (100 water/N2/biomass + 1 nuclear core) and instant ESI → 0.60 are provisional, and the Tier 5 gate waits for the Research module.
- 2026-10-16 — **`DelayedPlayerCommand` carries `TargetGeneration`** with `TargetEntityId`, so a command aimed at a destroyed entity misses instead of hitting a reused index. Nothing is serialized yet; the MessagePack keys get assigned when networking starts.

## Exceptions

- 2026-10-16 — `Packages/com.kitwright.unity.mcp` is an embedded copy with one patched `#if` (UNITY_6000_6 → 6000_5) so it compiles on Unity 6000.5.7f1. Drop it once upstream ships the fix.
