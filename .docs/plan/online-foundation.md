# Plan: online foundation (approved 2026-10-24 with changes)

User decisions 2026-10-24: online first, single-player later; 1b: a shield after logout with a weekly limit;
2: offline empires run on standing orders plus an optional governor; bots scale with players (5 per 10, max 100).
All numbers below are PROVISIONAL and live in balance config.

- [x] O1. Offline shield (Core): new `PlayerPresence` component on the player HQ, owned by a new `PresenceSystem`.
      - The host sends `LogIn` / `LogOut` as commands with no latency (one door).
      - The shield rises 10 min after logout and lasts at most 8 h per logout, 48 h per week.
      - Newcomers are fully protected (online too) until they finish the tutorial; they may give it up early
        (`DropProtection`). Until there is a tutorial, the host marks it finished.
      - It starts late if the player was in combat in the last 10 min, so logging out never escapes a fight.
      - While shielded, the player's own sites and anything docked there can't be fought, claimed, struck or raided.
        Fleets out in space are not covered, so a shield can't be used to block lanes.
      - Logging in, or any hostile order from the shielded player, drops the shield.
      - Save schema 8 plus a migration test. Done: tests for each rule.
- [x] O2. Governor (Core AI): a `Governor` mode for Auto-SOP (`SetStandingOrders`).
      - It thinks every 10 min instead of every 10 ticks, so it costs about 1/60 of a bot.
      - Manages only what the empire already has: supplies, rigs, refineries, convoys, defend and retreat. It never
        builds ships or new sites, raids, besieges, claims or strikes.
      - Standing orders (convoy routes, production) keep running with or without it. Done: tests; a soak with
        offline players under a governor shows no starving.
- [x] O3. Why cost grows faster than empires (4x empires took 7x the time): profile 100 / 400 / 1,000 empires and
      fix the hot spots. Done: cost per empire roughly flat; numbers in the roadmap.
- [x] O4. Server host (Sim Lab `server` mode):
      - one persistent world on server time; autosave, and load on start;
      - logins and logouts go to `PresenceSystem`;
      - a bot manager keeps bots at half the online players, max 100: it adds them with `Join` and retires extras by
        handing them to the governor. Done: a 3-day run with simulated logins.
- [x] O5. Admin dashboard: the server and bot strip board from the earlier draft, plus shield and governor status
      per player. Done: tested in a browser at desktop and phone width.
- [x] O6. Next plan (separate, Opus): per-player view over WebSocket, the Unity client joins the server, login.

Each step: Core tests, View compile check, Unity recompile, soak not worse, change log, commit.
