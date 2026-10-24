# Sim Lab: headless bot simulation + browser control

Runs the real `Unruled.Core` rules (compiled straight from `Assets/Scripts/Core/`, no copy) without Unity,
with every seat played by the Utility AI or by you from a browser page. Use it to watch a game unfold at
any speed, take over a seat, and check that empires survive long runs.

Needs the .NET 10 SDK. Run from this folder (`tools/simlab`).

## Live: watch and control a game

```powershell
dotnet run -c Release          # then open http://localhost:5080
dotnet run -c Release -- --port 5090
```

- **Header:** run/pause, speed (1 min/s … 6 h/s, MAX ≈ 6 game-hours per real second), +1h / +1d steps,
  run to day N, new game with a seed and who plays seat 1.
- **Empires table:** per player: controller, cash, debt, tech tier per branch (MIL IND LOG SIG), fleets,
  ships, sites, jump gate, core fuel / oxygen / rations, and health flags (NO FLEET, BROKE, STARVING,
  AT LIMIT, STALLED). Click a row to select that player.
- **Citadel:** THE SYSTEM's shield and when it fell / when The Directorate formed.
- **Charts:** one point per sim hour: cash, debt, highest tier, ships, sites, citadel shield.
- **Seat control:** hand the selected seat to any AI preset, or take it as a human (the AI stops acting
  for it). Commands go through `CommandIssuer` like a real order (signal latency, refusals shown):
  borrow / repay, stress test per branch, audit, retrofit, build ships, or any `CommandType` raw with
  params A/B/C and target `core` / `fleet` / `hq`.
- **Log:** AI orders (except routine vent/resupply), your commands, citadel fall, Directorate.

Seat 1 starts human by default, so it idles unless you play it or hand it to a preset.

## Server: a persistent world with an admin dashboard

```powershell
dotnet run -c Release -- server                    # then open http://localhost:5090
dotnet run -c Release -- server --port 5091 --seed 7 --save ..\..\.temp\server\other.sav
```

One world on server time (1 tick = 1 real second, speed up to x3600 for testing). It loads `--save` on start, autosaves
every 5 minutes and on stop (`POST /api/server/stop`, or Ctrl+C); a roster file next to the save remembers which
empires are bots, which are held, and the bot rules. Logins and logouts go through Core's `PresenceSystem` (offline
shield, newcomer protection). The bot manager keeps bots at half the players online (at least 5, at most 100), plus
optional random spawns; it wakes a held bot before adding a new one, and holds extras under a governor.

The dashboard (`wwwroot/server.html`) shows the server clock, players online, bots on duty, tick cost and last save,
the bot rules, and three bays of flight strips (bots on duty, bots held, players): click a strip for its controls
(preset, hold, log in or out, end newcomer protection), its core stock, shield time and recent orders. Admin routes
live under `/api/server/*` and `/api/bots/*`. Localhost only: there is no login yet.

## Batch: survival report over many seeds

```powershell
dotnet run -c Release -- batch --seeds 5 --days 30
dotnet run -c Release -- batch --seeds 10 --days 60 --first 1000 --human 4 --csv ..\..\.temp\soak.csv
```

| Flag | Default | Meaning |
|---|---|---|
| `--seeds` | 5 | seeds to run, in parallel |
| `--days` | 30 | game days per seed |
| `--first` | 1234 | first seed |
| `--human` | 2 | preset for seat 1 (0 = idle human) |
| `--parallel` | CPU count | games played at once (lower it when memory is short) |
| `--credit` | balance (10,000) | loan limit in credits; `0` = no borrowing at all |
| `--governor` | off | seat 1 runs as an offline player's governor (manages, never grows) |
| `--csv` | none | hourly samples per seed and player |

Each player gets a verdict: `ok`, or flags `NO-FLEET`, `BROKE` (no cash and in debt), `STALLED-T1`
(still tier 1 after day 3), `STARVING` (core out of fuel, oxygen or rations), `NO-EXPANSION`
(one site after day 5). Speed: about one 30-day seed per 2–4 minutes per core.

A LONG-RUN HEALTH table follows, from the hourly samples: lowest core fuel / oxygen / rations, hours with one
of them at 0, hours at the loan limit, fleets vs ships, AI orders per day, and the day each branch reached T5.
`refused%` is orders turned down for any reason but a radio storm; `blackout%` is orders that hit a storm over
their target and are simply retried next think until it clears (no cost, and the right call without LaserComms).

## Profile: cost per tick as the world grows

```powershell
dotnet run -c Release -- profile --empires 100,400,1000 --hours 1
```

For each empire count it joins that many bots into one game, runs the given game hours, and prints the cost per tick
(total and per empire), live memory, hubs, systems and lanes, and each sim system's and the AI's share of the time.

## How it fits the architecture

- Tooling only: lives in `tools/`, not in the Unity project. It only links Core's `.cs` files, so Core
  keeps `noEngineReferences` and knows nothing about it.
- It hosts the game like `GameSession` does: `Clock.Advance(1)` then `Ai.Think(tick)`; it writes the world
  only through `CommandIssuer` (player commands) and `UtilityAiEngine.Assign/Release` (seat control, the
  same host calls GameSession uses for Auto-SOP).
