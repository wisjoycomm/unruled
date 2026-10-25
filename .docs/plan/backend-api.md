# Backend API (game server)

How a game client talks to the server, and what the admin page uses. The server today is Sim Lab's `server` verb
(`tools/simlab`, .NET 10, localhost); the rules below are Core's and stay when it moves.

## Game client: WebSocket `/ws`

- One binary WebSocket message = one frame. A frame is the MessagePack array `[kind, payload]`; each payload is an
  array whose position is its key (like the save game). Keys and kinds are only appended: a reader skips keys and kinds
  it doesn't know, and a missing key reads as its default. Code: `Unruled.Core.Net.WireCodec`, `WireFrames.cs`.
- `WireCodec.ProtocolVersion` (now **1**) goes up only when a change can't be read by an older peer.
- Max client frame: 64 KB. Server frames are unbounded in principle (a full snapshot); the Unity client accepts 1 MB.

| Kind | Dir | Payload keys |
|---|---|---|
| 1 Hello | C→S | `[protocolVersion, token (bin)]` |
| 2 Command | C→S | `[sequence, DelayedPlayerCommand]` (the save game's command shape) |
| 3 Ping | C→S | `[nonce]` |
| 10 Welcome | S→C | `[protocolVersion, playerId, tick, worldSeed]` |
| 11 Snapshot | S→C | `[0, tick, body (bin)]` |
| 12 Delta | S→C | `[baseTick, tick, body (bin)]` |
| 13 Refused | S→C | `[reason, serverProtocolVersion]`, then the server closes |
| 14 Bulletin | S→C | `[SystemBulletin]` (reserved; bulletins travel in the view today) |
| 15 CommandResult | S→C | `[sequence, IssueResult, executeAtTick]` |
| 16 Pong | S→C | `[nonce]` |

Refuse reasons: 1 version mismatch, 2 bad token, 3 not logged in, 4 server full, 5 bad frame.

### Session

1. The client sends Hello. A wrong version or token gets Refused and the socket closes.
2. The server logs the player in (`LogIn` through `PresenceSystem`; any governor is released), sends Welcome, then a
   Snapshot. A second login for the same player closes the older session.
3. After every loop pass that ran ticks, the client gets one Delta against the last view it was sent. If its outbox
   (256 frames) is full the frame is dropped and the next one is a Snapshot.
4. Commands: the server sets the command's player to the session's player and issues it through `CommandIssuer`
   (signal latency, charts, paralysis: the same door as the AI). `LogIn`/`LogOut` from a client are refused
   (`NotAllowed`). The CommandResult says when it lands; the order then shows up in flight in the next view.
5. When the socket closes the player logs out: the offline shield rises after its delay; standing orders keep
   running. No governor is set by a disconnect (the admin page can set one).

### The view body (`Unruled.Core.Net.PlayerView`)

`[tick, the player's orders in flight [command...], channels [[upserts [[entity, component]...], removals [entity...]]...]]`

One channel per component store, in a fixed append-only order (see `PlayerView`'s constructor), each component in
its save-game shape. A delta lists only components whose encoding changed (64-bit hash per entity) and entities no
longer shown. What one player sees:

- **Own empire in full:** sites' stock, fleets (hold, route, combat state, sensors), wallet, loan, research, AI or
  governor, sanctions, radar scans, ship orders, contracts, charts.
- **Charted systems:** sites and owners, lanes, weather and storms, deposits, refineries (owner's upkeep hidden),
  quarantines, relays, jump gates, the Citadel, star systems and names.
- **Rival and THE SYSTEM's fleets** only where the player has eyes: docked at a site the player owns or at a site
  one of its fleets is docked at, or revealed by radar. Never their hold, route, debts or RWR state.
- **Rivals met:** a rival whose home system the player holds a chart of: HQ, standing, shield window and newcomer
  flag. Rivals not met aren't sent at all (a world of 10,000 doesn't send 10,000 HQs). Never wallet, stock, loan,
  research or AI.
- **Public:** market prices and the order book without owners or deposits, THE SYSTEM's shop stock, bulletins, chart
  listings, Directorate seats.

The client applies a body to a mirror `SimulationWorld` it never steps (`PlayerView.Apply`); the views read it as
they read an offline world. The mirror's `CommandIssuer.SendTo(sink)` sends orders to the server instead of its clock.

Measured (Sim Lab, 2026-10-25): snapshot about 5 KB for a 6-empire start; deltas about 150 B per tick in tests and
about 9 KB/s per client on a live server at x120 speed (so about 75 B per game second at x1).

## Admin page: HTTP (localhost only, no login yet)

- `GET /api/server/status`, `/api/server/players`, `/api/server/log?since=T`
- `POST /api/server/run {running, speed}`, `/save`, `/reset {seed, confirm}`, `/stop`
- `POST /api/server/login|logout {playerId, join|governor}`, `/tutorial-done {playerId}`
- `POST /api/server/token {playerId | join}` issues a login token (shown once; the server keeps only its SHA-256 in
  `<save>.tokens`; a new token replaces the old one)
- `GET /api/server/viewhash?playerId=N`: the hash of the player's full view at the current tick (sync checks)
- `POST /api/bots/add {preset}`, `/api/bots/set {playerId, preset, hold}`, `/api/bots/rules {min, max, autoScale, randomSpawnHours}`

## Later

Steam login replaces the token; per-platform transport checks (WebGL WebSocket); the admin API behind a login; a
region per process once one process nears its tick budget.
