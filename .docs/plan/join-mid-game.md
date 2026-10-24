# Plan: join mid-game + three play-test fixes (approved 2026-10-24: "yes fix it")

From the 5-day Sim Lab play-test. No save shape change in any step.

- [x] J1. Core `NewGameSetup.Join(sim, archetype)`: a player (or bot) joins a running game: next free player id, a free
      home star nearest the hub with the fewest homes, home system + starting kit (same as a new game), a dormant gate
      to a free hub orbit (never the Citadel), its own frontier linked from the gate's far side, catalog hyperlanes for
      the new systems, charts (home, hub, frontier). Done: tests (joins mid-game, ticks, AI plays it, charts, save
      round-trip).
- [x] J2. Upkeep at home: a fleet docked at its owner's site pays what its hold lacks from the site's stock (only the
      shortfall), so a fleet parked at an outpost or the capital never starves. Done: tests.
- [x] J3. Why a move fails: `FleetMovementSystem.CheckMove(fleet, laneOrSite, tollFree, tick)` returns the reason
      (not ready, uncharted, no route, tier, fuel, toll) with the fuel and toll needed; the FLEET card / Sim Lab show it.
      Done: tests, recompile clean.
- [x] J4. THE SYSTEM opens with a treasury and a stock (WtoBalance, PROVISIONAL), granted at new-game setup, so
      selling to and buying from it works from day 1. Done: tests.
- [x] J5. Soak 5x30d not worse, docs (overview, roadmap why, change log).
