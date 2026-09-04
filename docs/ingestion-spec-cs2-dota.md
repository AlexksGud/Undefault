# Ingestion spec — CS2 (v1) and Dota 2 (future)

## CS2 — snapshot extensions (target)

Add modules or structured fields on `GameSnapshot` / DTO layer (host mapping):

| Domain | Suggested module / fields | Source (GSI) |
|--------|----------------------------|--------------|
| Bomb | `BombModule`: planted, site, timer_sec, defusing | `map.round`, `player` bomb-related props — **exact JSON paths TBD per GSI cfg** |
| Pause | `GameClockSnapshot.IsGamePaused` | `map.paused` / `player.state` as available |
| Phase | `MatchPhaseNeutral` derivation | `round.phase`, `round.bomb`, scoreboard |
| Spectator | `SpectatorOrObserver` | `player.activity` / `provider` |
| Staleness | `ReceivedAtUtc` on observation wrapper | Host sets when POST received |

All mappings must be documented in this file as they are implemented (append subsections).

## Dota 2 — current state (UND-80, event logging only)

Implemented, landed:

- `POST /gsi/dota` accepts a minimal, loosely-typed `DotaGsiPayloadDto` (`provider`/`map`/`player`/`hero`, flat non-spectator shape only).
- `DotaGsiLoggingService` detects `map.game_state` changes, `hero.alive` flips, and `map.paused` flips across consecutive POSTs and appends them to the same timeline CS2 uses, under `source: "dota"` (`GsiHost/Tooling/Timeline/TimelineModels.cs`: `TimelineSources.Dota`, `TimelineDotaEvents`).
- `GameAdapterRegistration` for `dota2` (appid 570) is registered so `/diagnostics/adapters` lists it, with a description flagging "event logging only, no rules engine yet".
- No `IGameAdapter<DotaGsiPayloadDto>`, no `AdapterObservation`/`NeutralContext`/`SafetyFacts` mapping, and no music-player actions from Dota events. `GsiProcessingService`/`IRulesEngine` are not involved in the Dota path at all.

This is deliberately a smaller, separate slice from the "future shape" below — see [README.md](../README.md#dota-2-gsi-event-logging-only) for setup and the recorded event list.

## Dota 2 — future shape

- Add a separate `DotaGameAdapter : IGameAdapter<DotaPayloadDto>` (or equivalent raw JSON input type) rather than expanding CS2 mappers.
- The adapter output target is `Core/Adapters/AdapterObservation.cs`: `GameSnapshot Raw`, `GameClockSnapshot Clock`, `NeutralContext Neutral`, and `SafetyFacts`.
- Dota-specific facts may remain in Dota snapshot modules for diagnostics, but shared music behavior should consume `Clock`, `Neutral`, and `SafetyFacts`.
- No shared FSM named after CS rounds; engagement/objective pressure only.
- **Routing:** Dota gets its own typed HTTP endpoint (e.g. `/gsi/dota`) registered in `GsiHost/Program.cs` alongside the CS2 endpoint. Each title declares a `GameAdapterRegistration(TitleId, AppId, EndpointPath, Description)` and the shared `IGameAdapterRouter` exposes the registry at `GET /diagnostics/adapters`. See [multi-adapter-routing.md](multi-adapter-routing.md) for the design spike (per-endpoint chosen over per-process or single-router-by-appid). **`POST /gsi/dota` and the `dota2` registration already exist** (UND-80, event logging only, see above) — the remaining work here is wiring `IGameAdapter<DotaGsiPayloadDto>` and the rules engine behind that same endpoint (UND-45), not adding the endpoint itself.

## Versioning

Ingestion schema changes bump **`MusicEngineOptions.SchemaVersion`** or a dedicated `IngestionSchemaVersion` when options depend on new fields.
