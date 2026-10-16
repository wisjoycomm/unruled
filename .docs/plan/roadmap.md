# Unruled roadmap

Where the project is heading, why, and rule exceptions. Build order follows GDD §5 (Sprint 1–5).

## Decisions

- 2026-10-16 — **Simulation is a hand-rolled pure C# ECS in `Unruled.Core`, not Unity DOTS.** Performance with flat struct arrays, while staying deterministic, engine-free (runs on the headless Server 2 and WebGL) and portable to Rust later; DOTS would tie the sim to Unity APIs and non-deterministic Burst floats. DOTS is allowed only in `Unruled.View` for rendering if profiling demands it.
- 2026-10-16 — **Fixed-point instead of the GDD §3 `float` fields** (`BaseEsi`, `PosX/PosY`, `HullIntegrityPct`, …). The GDD class shapes become ECS components with integer/milli-unit values for cross-platform determinism.
- 2026-10-16 — **System generator uses SplitMix64 only (GDD says SplitMix64 + ChaCha8) and integer trig (`FixedMath`).** ChaCha8 adds no gameplay value; integer math keeps layouts bit-identical across platforms and in a Rust port. The RNG draw order is part of the save contract.
- 2026-10-16 — **Universe layout numbers are provisional** (`DefaultGameBalance.CreateUniverseGenBalance`: 8 orbits, r 300 + 180·i ± 25, R_HZ 840, W_HZ 700, ±0.05 atmo noise, ±10° jitter); only the 120 min spacing is from the GDD. Move them into the data sheet when designed.

## Exceptions

- 2026-10-16 — `Packages/com.kitwright.unity.mcp` is an embedded copy with one patched `#if` (UNITY_6000_6 → 6000_5) so it compiles on Unity 6000.5.7f1. Drop it once upstream ships the fix.
