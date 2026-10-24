# Plan: Per-player star charts, sharing and chart markets (drafted 2026-10-24, done 2026-10-24)

Decisions (user, 2026-10-24): systems you don't know are hidden (shown as UNCHARTED, no routing there); charts sell at a
fixed price per listing, and each buyer pays once and keeps the chart forever; only the first surveyor may list a
system on THE SYSTEM's world shop, which charges an STA fee; anyone who knows a system may list it on their own shop; the AI
trades charts in this feature.
The world stays one shared, deterministic simulation. What is per player is only who knows which system.

- [x] D1. Core `SystemChart` store (one entity per player+system: PlayerId, ClusterId, KnownTick, Source:
      spawn/survey/arrival/share/purchase) owned by `GalaxySystem` (`Knows(player, cluster)`, index rebuilt on load);
      `StarSystem.FirstSurveyorPlayerId` appended. New game: each player knows their home, the hub and the frontiers
      linked to them. Survey: the surveyor learns it (a survey of an already-charted star only adds it to their chart,
      for fuel). A fleet arriving at a site learns that system. Done: tests.
- [x] D2. Knowledge gates orders: `CommandIssuer`/`RoutePlanner` refuse moves into a system the player doesn't know
      (new `IssueResult.Uncharted`); `CanSurvey` becomes per player. Done: tests, soak still reaches gates and fights.
- [x] D3. `ShareChart` command (giver → player, free) and `ChartMarket` (Core/Galaxy or Economy): `ListChart`
      (own shop: any knower; world shop: first surveyor only, STA listing fee and sales cut into the STA pool),
      `BuyChart` (pay once, learn the system, seller keeps selling), `CancelChartListing`. Numbers in
      `GalaxyBalance` (PROVISIONAL). Bulletin on world-shop registration. Done: tests incl. fee, double-buy refused.
- [x] D4. Save schema 7: append stores (SystemChart, ChartListing) and the StarSystem field; migration gives old
      saves' players knowledge of every system they hold a site in, plus the hub. Done: migration test, old save loads.
- [x] D5. AI: lists charts it first surveyed on the world shop and others on its own shop at a preset price; buys charts
      of systems next to its known ones when it has nowhere to expand and the cash; shares with nobody. Done: soak
      5x30d not worse; AI chart trades seen.
- [x] D6. View: board, region view and exits draw only known systems (others as UNCHARTED catalog stars); a "CHARTS"
      group (MARKET tab) lists world-shop and player-shop offers with Buy, and the SITE/system card gets Share / Sell
      chart (price prompt). Done: recompile clean, Play Mode captures of hidden system, buy, share.
- [x] D7. Docs: roadmap (decision + why), overview, change log.
