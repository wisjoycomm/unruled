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
| `--csv` | none | hourly samples per seed and player |

Each player gets a verdict: `ok`, or flags `NO-FLEET`, `BROKE` (no cash and in debt), `STALLED-T1`
(still tier 1 after day 3), `STARVING` (core out of fuel, oxygen or rations), `NO-EXPANSION`
(one site after day 5). Speed: about one 30-day seed per 2–4 minutes per core.

## How it fits the architecture

- Tooling only: lives in `tools/`, not in the Unity project. It only links Core's `.cs` files, so Core
  keeps `noEngineReferences` and knows nothing about it.
- It hosts the game like `GameSession` does: `Clock.Advance(1)` then `Ai.Think(tick)`; it writes the world
  only through `CommandIssuer` (player commands) and `UtilityAiEngine.Assign/Release` (seat control, the
  same host calls GameSession uses for Auto-SOP).
