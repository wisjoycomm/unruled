# Backend API — Endless Winter

Status: **draft for review** (2026-10-10). Owner: planner. This is the contract between the Unity client and the
game server. The local fake server (`EW.ServerCore` hosted by `LocalGameApi`) implements exactly this contract, so
the real backend replaces only transport and storage.

## 1. Principles

1. **The server owns all player state.** The client never uploads state. It sends **commands** ("upgrade building
   101"), the server validates them against its own state, config and clock, applies them, and answers with what
   changed. `SavePlayer` is removed.
2. **One rule implementation.** Rules live in `EW.Domain` (pure C#, no Unity). The backend hosts the same
   `EW.Contracts + EW.Domain + EW.ServerCore` assemblies in .NET 8. The client runs the same Domain code only to
   *preview* (button states, costs, timers); the server's answer is final.
3. **Server time only.** Every timer is an absolute `endMs` on the server clock. The client learns the clock offset
   from `serverTimeMs` in every response. The device clock decides nothing.
4. **Config is versioned.** Every command runs against one config version; the client must hold the same one.
5. **Idempotent.** Every command carries a `reqId`; a resend returns the cached result instead of charging twice.
6. **Append-only contracts.** MessagePack with int keys; keys and enum values are never reused or renumbered.

## 2. Transport

| Channel | Use |
|---|---|
| HTTPS `POST /auth/login` | `{provider: Guest/Google/Apple, credential}` → `{token, playerId, gatewayUrl}` |
| HTTPS `POST /auth/link` | Link Google/Apple to the current guest account (keeps progress) |
| CDN | Hot DLLs, Addressables, `config.bin` |
| WebSocket (gateway) | Everything else: requests, responses, pushes |

**Frame:** `[opcode:u16][seq:u32][body:MessagePack]`.
- Response reuses the request's `seq`. Push has `seq = 0`.
- Local dev uses a loopback transport with the same frames (no socket).

**Every response** (`Response<T>`):

| Key | Field | Meaning |
|---|---|---|
| 0 | `error` | `ApiError`, 0 = ok |
| 1 | `serverTimeMs` | Server clock at reply |
| 2 | `rev` | Player state revision after this command |
| 3 | `delta` | `StateDelta`: only the parts of state that changed |
| 4 | `events` | `GameEvent[]`: what happened (for UI, FTUE, missions) |
| 5 | `body` | Command-specific result (e.g. recruit results), often empty |
| 6 | `message` | Debug detail for logs; never shown to the player |

`rev` increases by 1 per applied command. A gap → client calls `GetPlayer` (full resync).

## 3. Errors (`ApiError`, append only)

| Value | Name | When |
|---|---|---|
| 0 | None | |
| 1 | InvalidRequest | Malformed / unknown id |
| 2 | NotFound | New player, missing entity |
| 3 | NotAllowed | Rule says no (locked, already upgrading, queue full, already claimed) |
| 4 | MissingResources | Not enough resources/items/gems |
| 5 | NotModified | Config version already current |
| 6 | StorageFailed | Server write failed; nothing applied |
| 7 | ConfigOutdated *(new)* | Client config version ≠ server; reload config |
| 8 | UpdateRequired *(new)* | Client build too old |
| 9 | Unauthorized *(new)* | Bad or expired token |
| 10 | Busy *(new)* | Same player has a command in flight; retry |
| 11 | Duplicate *(new)* | `reqId` already processed (body = cached result) |

A failed command changes nothing.

## 4. Session

| Op | Request | Response body | Notes |
|---|---|---|---|
| `Hello` | `token, clientVersion, configVersion, lastRev` | `serverTimeMs, configVersion, rev` | First frame on connect. Answers `UpdateRequired` / `ConfigOutdated` / `Unauthorized`. |
| `GetConfig` | `knownVersion` | `ConfigBundle` or `NotModified` | Dev/local only; production downloads `config.bin` from CDN. |
| `GetPlayer` | — | **core** `PlayerState` (see 5.2) | After Hello, or on `rev` gap. A new player is **created by the server** here (starting resources + `init_item` from config, chapter 1, FTUE step 0). |
| `Heartbeat` | — | `delta` only | Every 60 s while the city is open, and on app resume. The server simulates to now and returns what time changed (vitals, coal, timers, storms). Read-only; never carries client state. |
| `GetModule` | `module` (`Mail`, `Events`, `HeroDetail`, …) | that module's state | Lazy modules (5.2; none exist yet), loaded the first time their screen opens; afterwards kept current by deltas. |

## 5. Player state (server-owned)

`PlayerState` (replaces `PlayerSave` + `CityProgressData`):

| Section | Contents |
|---|---|
| Meta | `schemaVersion, rev, playerId, createdMs, configVersion, lastSimMs` |
| Economy | `resources[]` (food, wood, coal, iron, steel, gems, hero XP…), `items[]` |
| Stats | `power` (derived on server, sent for display) |
| Buildings | `{id, level, upgradeStartMs, upgradeEndMs}`; builder queues |
| Furniture | `{buildingId, slot, level}`, `busy {key, endMs}`, building furniture exp |
| Furnace | `lit, on, maxPower, coal accrual {lastMs}` |
| Survivors | `{id, workplace, vitals(sleep, comfort, mood, satiety, health), sick, sickUntilMs, healEndMs, inSickbed}`, `deliveredArrivalWaves[]`, `nextIllnessMs` |
| Day/night | `dayStartMs` (day count and minute of day are derived from server time) |
| Weather | `storm {comingAtMs, endsAtMs}` |
| Missions | `chapterId, claimedMissionIds[], claimedGrowthIds[]`, counters used by progress |
| Exploration | `clearedStages, idleLastClaimMs` |
| Heroes | `{id, owned, level, star, shards}`, `claimedStories[]` |
| Recruitment | `{poolId, freeUsed, freeResetMs, pityCount}` (random seed stays server-side, never sent) |
| FTUE | `ftueStep, chapterFlags[]` (story checkpoints that affect gameplay) |

### 5.2 Core vs lazy modules
`GetPlayer` returns only the **core**: what the city screen needs on its first frame. Everything above is core
today (chapters 1–5 are small: a few tens of KB). Later modules are **lazy**: not in `GetPlayer`, loaded by
`GetModule` when their screen opens, then kept current by `delta`.

| Core (in `GetPlayer`) | Lazy (`GetModule`, when built) |
|---|---|
| Everything in the table above, plus `badges` (red-dot counts per lazy module, e.g. unread mail = 3) | Mail, event details and rankings, hero gear/skills detail, purchase/VIP history, alliance, world map (pushed), chat |

Rules for the backend:
- A delta only carries a lazy module's changes if the client has loaded it this session; otherwise it updates the
  badge count only.
- New features decide core vs lazy in their task. Default is lazy unless the city HUD needs it on the first frame.

**Not in server state** (device only, never decides gameplay): seen-dialog/popup flags, settings (audio, quality,
language), camera position.

### Lazy simulation
Time-driven state (coal burn, survivor vitals and illness, idle income, timers finishing, storms, day/night) is not
ticked on the server. Before every command the server runs `Simulate(state, config, lastSimMs → nowMs)` (pure
Domain), then applies the command. `GetPlayer` also simulates first. This also gives offline catch-up for free.
The client runs the same simulation locally between responses for smooth display, and snaps to the server state on
each response.

## 6. Commands

All commands: request = `{reqId, ...args}`, response = `Response<T>` with `delta` + `events`.
"Validates" lists what the server checks; any failure → error, nothing applied.

### 6.1 Buildings
| Op | Args | Validates (Domain) | Effect / events |
|---|---|---|---|
| `BuildingUpgrade` | `buildingId` | next level exists, not upgrading, free builder queue, prerequisite buildings, furniture progress, cost (`BuildingUpgradeRules`) | spend cost, set `upgradeEndMs` (instant if 0 s) → `BuildingUpgradeStarted` / `BuildingLevelUp` |
| `BuildingFinish` | `buildingId` | upgrading and `upgradeEndMs <= now` | level +1 → `BuildingLevelUp`. (Also applied automatically by Simulate; explicit op exists for the "tap to collect" moment.) |
| `BuildingSpeedup` | `buildingId, itemId, count` | upgrading, item is a construction speedup, owns `count`, count not above what's needed (`SpeedupRules`) | spend items, reduce `upgradeEndMs`, finish if ≤ now → `ItemsSpent`, maybe `BuildingLevelUp` |
| `BuildingFinishGems` | `buildingId, expectedGems` | upgrading, gem cost (server computes; must equal `expectedGems` so price can't change silently) | spend gems, level +1 |

### 6.2 Furniture
| Op | Args | Validates | Effect |
|---|---|---|---|
| `FurnitureUpgrade` | `buildingId, slot` | slot unlocked, below max level, no furniture busy, cost | spend, set busy timer (or instant) → `FurnitureLevelUp` + building furniture exp |
| `FurnitureFinish` | — | busy and `endMs <= now` | complete |

### 6.3 Furnace
| Op | Args | Validates | Effect |
|---|---|---|---|
| `FurnaceLight` | — | FTUE/chapter allows, not lit | lit, on |
| `FurnaceSetOn` | `on` | lit | toggle; coal burn accrual starts/stops |
| `FurnaceSetMaxPower` | `on` | unlocked by level | toggle |

### 6.4 Survivors
| Op | Args | Validates | Effect |
|---|---|---|---|
| `SurvivorAssign` | `buildingId` | free work slot, an idle healthy survivor exists | assign one → `WorkersChanged` |
| `SurvivorUnassign` | `buildingId` | building has a worker | unassign one |
| `SurvivorAcceptArrivals` | `waveId, choice` | wave conditions met (shelter level, beds), not delivered (`SurvivorArrivalRules`) | add survivors → `SurvivorsArrived` |
| `SurvivorHeal` | `survivorId` | sick, sickbed free (`HealthcareRules`) | start heal timer |

Vitals, illness, death and recovery happen in Simulate, not by command.

### 6.5 Items
| Op | Args | Validates | Effect |
|---|---|---|---|
| `ItemUse` | `itemId, count` | owns, item is usable outside a target (resource packs) | spend, grant contents |

Target-bound items use their own op (`BuildingSpeedup`, hero shard redeem).

### 6.6 Missions
| Op | Args | Validates | Effect |
|---|---|---|---|
| `MissionClaim` | `missionId` | in current chapter, progress ≥ target (computed on server from state), not claimed | grant reward → `MissionClaimed`, `RewardsGranted` |
| `MissionClaimAll` | — | — | claims every completed mission in the chapter |
| `ChapterClaim` | `chapterId` | is current chapter, all missions claimed | grant chapter reward, advance chapter → `ChapterCompleted` |
| `GrowthClaim` | `missionId` | unlocked, chain order (`GrowthChainRules`), progress, not claimed | grant reward |

Progress is never sent by the client.

### 6.7 Exploration
| Op | Args | Validates | Effect |
|---|---|---|---|
| `ExplorationBattle` | `stage, heroIds[]` | stage = next, heroes owned | server runs the seeded battle sim (`BattleSim`, Domain) → on win grant first-clear reward, `clearedStages+1`, start idle accrual → `StageCleared` + battle result/replay seed in `body` |
| `ExplorationClaimIdle` | — | elapsed > 0 (`IdleIncomeRules`, 9 h cap) | grant steel + hero XP, reset `idleLastClaimMs` |

Until `BattleSim` is in Domain (other dev), the server accepts the win if the team's power ≥ stage power. Marked
as a known gap.

### 6.8 Heroes
| Op | Args | Validates | Effect |
|---|---|---|---|
| `HeroRecruit` | `poolId, count` | keys or free draw available (`RecruitmentRules`) | server rolls with its own seed → results in `body`, shards/heroes granted |
| `HeroLevelUp` | `heroId` | owned, below cap, hero XP cost | level +1 |
| `HeroStarUp` | `heroId` | shards ≥ cost | star +1 |
| `HeroUnlock` | `heroId` | shards ≥ unlock cost | owned |
| `HeroRedeemShards` | `heroId, amount` | owns generic shards | convert |
| `HeroClaimStory` | `heroId, storyId` | condition met, not claimed | grant gems |

### 6.9 FTUE / story
| Op | Args | Validates | Effect |
|---|---|---|---|
| `FtueAdvance` | `toStep` | `toStep == current + 1`, the step's condition is met on server state (e.g. "Furnace Lv2") | set step; grant the step's scripted rewards/levels from config (`ftue_steps` table) → `FtueStepChanged` |
| `SetSeenFlag` | `flag` | — | *local only*, not sent (listed so nobody adds it) |

Scripted story grants (chapter 1–3 free levels, cutscene rewards) move from `FtueDirector.RestoreLevel` into the
`ftue_steps` config table and are applied by the server. Replaying a save no longer rebuilds state from the step.

### 6.10 Commerce (later)
`PurchaseVerify {sku, receipt}` → server verifies with the store, grants once per `transactionId`. Not in scope now;
gem buttons stay disabled.

## 7. Pushes (server → client, `seq = 0`)

| Op | When |
|---|---|
| `StateDelta` | State changed without a client command (timer finished during Simulate on another trigger, admin grant, mail) |
| `Kick` | Logged in elsewhere / banned / maintenance |
| `ConfigChanged` | New config live; client reloads at a safe point |

World / alliance / chat pushes come with those features.

## 8. Events (`GameEvent`, append only)

`BuildingUpgradeStarted, BuildingLevelUp, FurnitureLevelUp, ResourcesChanged, ItemsSpent, RewardsGranted,
WorkersChanged, SurvivorsArrived, SurvivorSick, SurvivorHealed, SurvivorDied, StormStarted, StormEnded, IdleIncomeClaimed,
MissionClaimed, ChapterCompleted, StageCleared, HeroRecruited, HeroLevelUp, HeroStarUp, FtueStepChanged`.

The client's `ClientState.Apply(delta, events)` updates local state and publishes these on the event bus. A
`LegacySignalAdapter` keeps raising today's FTUE signal strings (`building_302_1`, `explore_win_3`…) from the
events, so the FTUE directors keep working unchanged.

## 9. Client flow

```
tap Upgrade → BuildingService.UpgradeAsync(id, ct)
  → preview check with Domain (disable button if it would fail)
  → IGameApi.BuildingUpgradeAsync({reqId, id}, ct)
  → Response → ClientState.Apply(delta, events) → bus → views redraw
  → on error: show reason, state untouched
```
- The local server answers in the same frame, so FTUE timing stays as today.
- An editor toggle adds artificial latency (e.g. 300 ms) to catch code that assumes instant replies.
- No optimistic writes in phase 1; add per-command later only where latency is felt.

## 10. Mapping to today's code

| Today (client writes state) | Becomes |
|---|---|
| `BuildingService.TryStartUpgrade / TrySpeedUp / TryFinishWithGems` | `BuildingUpgrade / BuildingSpeedup / BuildingFinishGems` |
| `FurnitureService.TryUpgrade` | `FurnitureUpgrade` |
| `FurnaceService.Light / SetOn / SetMaxPower` | `FurnaceLight / FurnaceSetOn / FurnaceSetMaxPower` |
| `SurvivorService.TryAddWorker / TryRemoveWorker / AcceptArrivals`, vitals `Tick` | `SurvivorAssign / Unassign / AcceptArrivals`, Simulate |
| `ItemService.Grant / TrySpend / GrantStartItems` | server-only (rewards, `GetPlayer` new-player creation) |
| `ChapterMissionService.TryClaim / ClaimAll / TryClaimChapter`, `GrowthMissionService.TryClaim` | `MissionClaim / MissionClaimAll / ChapterClaim / GrowthClaim` |
| `ExplorationService.CompleteStage / ClaimIdleIncome / SkipStage` | `ExplorationBattle / ExplorationClaimIdle` (skip = dev cheat, local server only) |
| `RecruitmentService.TryRecruit`, `HeroRoster.TryLevelUp / TryPromoteStar / TryRecruit / TryRedeemGenericShards / TryClaimStory` | `Hero*` ops |
| `FtueDirector` step + `RestoreLevel` | `FtueAdvance` + `ftue_steps` config |
| `WeatherService.StartStorm / EndStorm` | storm schedule in config, applied by Simulate |
| `CityClock` day/minute from save | derived from `dayStartMs` + server time |
| `GameSession.Save` → `SavePlayerAsync` (every 5 s) | **removed** |
| PlayerPrefs FTUE step / checkpoints / guest id | `PlayerState.ftue*`; guest id → login token |

## 11. Dev-only ops (local server only, rejected by production)

`DevGrant {resources, items}`, `DevSetBuildingLevel`, `DevSetFtueStep`, `DevSkipStage`, `DevSetTime {offsetMs}`.
Used by tests, FTUE replay and the battle bypass. Compiled out of production servers.

## 12. Decisions (2026-10-10)

1. **Backend stack: .NET 8 hosting `EW.ServerCore`, for now.** To keep the option cheap, `EW.Contracts`,
   `EW.Domain` and `EW.ServerCore` stay engine-free (`noEngineReferences`), .NET Standard 2.1, no UniTask/Unity types,
   no static state. If the backend team picks another language, every rule must be rewritten there, so this must be
   confirmed before the first command lands (task 031).
2. **Battle: server-run seeded sim when the other developer's battle code is merged.** Until then the server checks
   team power against stage power (task 037). The battle developer should be told now that `BattleSim` must live in
   `EW.Domain` (no MonoBehaviour, no `UnityEngine.Random`, seed in, result out); otherwise the merge means a rewrite.
3. **Login: Guest, Google and Apple.** Guest first, then bind: a guest account can later be linked to Google/Apple
   and keeps its progress. One player can have several linked identities. iOS requires Sign in with Apple whenever a
   third-party login is offered. The local fake server accepts any token; real verification (Google ID token, Apple
   identity token) is the backend's job. Task 042.
4. **Survivor vitals: heartbeat, not per frame.**
   - The **server** simulates vitals in fixed game-minute steps from timestamps (`Simulate(lastSimMs -> now)`), so the
     result does not depend on how often anyone asks. It runs before every command, in `GetPlayer`, and on
     `Heartbeat`.
   - **`Heartbeat`** (client every 60 s while the city is open, and on resume): returns the current delta (vitals,
     coal, timers, storms). It is a read, not a write: the client never sends vitals.
   - The **client** interpolates locally between heartbeats with the same Domain code for display (bars, faces,
     walkers), and snaps to the server value on each response.
   - Why not let the client own vitals: they decide illness, death, production and happiness, which feed missions
     and rewards.
