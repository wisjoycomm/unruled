# Plan: Sim Lab (bot simulation + browser control)

Local .NET 10 server `tools/simlab/` compiles `Assets/Scripts/Core/**/*.cs` (no copy) and serves a browser page.

- [x] 1. Server: one SimulationWorld + NewGameSetup, runs the clock + AI on a background loop at a chosen speed (x1..MAX), pause/step/reset(seed). Done when: `dotnet run` and `GET /api/state` returns tick, players, citadel.
- [x] 2. Metrics: per player every sim hour (cash, debt, 4 branch tiers, ships, sites owned, key stock), citadel shield, AI order counts. Done when: `/api/history` returns a series.
- [x] 3. Control: take/release a seat (AI preset or human), issue commands (borrow, repay, stress test, build ships, claim, …) through `sim.Issuer` with the issue result shown. Done when: a borrow from the page shows in the next state.
- [x] 4. Web page `tools/simlab/wwwroot/index.html`: run bar, player table, charts (canvas, no deps), event/AI log, seat controls. Done when: page shows a game running and charts filling.
- [x] 5. Batch: `dotnet run -- batch --seeds 5 --days 30` prints a survival report (broke, stalled at T1, tiers, citadel fall day). Done when: report prints for 5 seeds.
- [x] 6. Docs (`.docs/dev-setup.md`, change log), commit.
