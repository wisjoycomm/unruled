# Plan: Unruled — Sprint 1 (deterministic ECS core + supply chain)

Source: `.docs/unruled_master_gdd.md` §3, §5 Sprint 1; numbers from `.docs/unruled_master_data_sheets.md`.

## Step 0 — prerequisites 
- [x] Fix rule files: working tree rewrote CLAUDE.md / AGENTS.md / `.claude|.agents|.codex|.cursor/rules/*` to **LittlePlanet**; restore the Unruled versions (or re-run sync with Unruled config). Done when: `grep -ri littleplanet CLAUDE.md .claude/rules` returns nothing.
- [x] Rename docs: GDD v3.1 → `.docs/unruled_master_gdd.md`, xlsx → exported `.docs/unruled_master_data_sheets.md` (6 tabs as markdown tables) so `project.config` paths resolve. Done when: both files exist and are committed.
- [x] Create the Unity project (6000.5.7f1) at repo root and open it so KitWright MCP connects. Done when: `request_recompile` answers with no errors.

## Sprint 1 steps
- [x] 1. Asmdefs: `Unruled.Core` (noEngineReferences), `Unruled.View`, `Unruled.Editor`, `Unruled.Tests.Core`. Done: compiles, references point one way.
- [x] 2. `SimTick` + `SimClock` (long ticks, command queue with `ExecuteAtTick`, `DelayedPlayerCommand`). Done: tests for ordering/same-tick determinism pass.
- [x] 3. ECS core (`Core/Ecs`): `EntityId` (index+generation), `World` (create/destroy, free list), `ComponentStore<T>` (sorted sparse set, ascending iteration, stale-id guard). Done: lifecycle/order/stale tests pass.
- [x] 4. `SystemSeedGenerator` (SplitMix64 stream; polar layout r_i/phi_i, min spacing 120, ESI_0 formula, fixed-point) → spawns site entities with `SiteInfo` component. Done: same seed → identical world; spacing + ESI-band tests pass.
- [x] 5. `ItemId` (20 items) + stockpile storage (flat int slab per entity). Done: tests pass.
- [x] 6. `GameBalanceConfig` + `RecipeDatabase` from data-sheet values. Done: all 10 recipes + base prices present; tests pass.
- [x] 7. `RefinerySystem` + `Refinery` component (10-tick cycle: IdleStarved 20% upkeep, ActiveProduction, GridBrownout w/ battery, WhoQuarantined 30→300 ticks). Done: one test per transition.
- [x] 8. `MiningSystem` + `MineralDeposit` component (3 depths, DepthMult, fuel/coolant mult, ESI drops, lifecycle 1–3A). Done: depletion-path tests pass.
- [x] 9. Docs: `.docs/overview.md`, change_log. Done: overview reflects what exists.

Each step: recompile clean + EditMode tests green + architecture checklist → commit.
