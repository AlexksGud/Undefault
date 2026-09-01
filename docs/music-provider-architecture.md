# Music provider architecture (target)

Approved 2026-08-14. Implemented in-repo (`PIVOT-1`–`PIVOT-8`). Live path: `IEventAction` → `IMusicPlaybackControl` → `IMusicPlayer` → `TauonMusicPlayer` or `MockMusicPlayer`. Leftover Spotify types remain until `PIVOT-10`. See [product-pivot-2026-08-14.md](product-pivot-2026-08-14.md).

## Principle

Undefault does not care where the music comes from. Core talks to a small player abstraction. HTTP, OAuth, and vendor URLs stay in adapters.

```text
Game Adapter → EventDetector → RulesEngine → IEventAction
  → IMusicPlaybackControl
  → IMusicPlayer
       ├── TauonMusicPlayer   (default)
       └── MockMusicPlayer
```

Do not add a Spotify adapter. See [spotify-constraints.md](spotify-constraints.md).

Core must not contain `if provider == Tauon` or `if SpotifyEnabled` in game/event/rule logic.

## Preserve

The existing GSI pipeline stays:

`POST /gsi` → `Cs2GameAdapter` → `AdapterObservation` → `RulesEngine` → `EventDetector` → `ActionMap` → `IEventAction`

Do not redesign that pipeline for this pivot.

## `IMusicPlayer` (MVP size)

Capabilities only. No playlist, queue, seek, or play-by-track-id.

| Member | Role |
|---|---|
| `IsAvailableAsync` | Player reachable (not Spotify OAuth) |
| `GetStateAsync` | status + optional current track + volume |
| `PlayAsync` / `PauseAsync` / `ResumeAsync` | transport |
| `NextAsync` / `PreviousAsync` | skip |
| `SetVolumeAsync` | 0–100 |
| `Capabilities` | what this adapter supports |

`Resume` on Tauon maps to `GET /api1/play`. Do not invent endpoints.

## `IMusicPlaybackControl`

Evolution of today's `ISpotifyPlaybackControl` / `SpotifyPlaybackControlCoordinator`.

Session behavior stays here:

- idempotent pause/resume
- skip redundant commands
- duck/restore volume state (keep the code; not the MVP default rules)
- graceful failure (log, do not crash the host)

Provider HTTP does **not** live here.

## Action keys

Canonical: `music.control_profile`.

Compatibility alias: `spotify.control_profile` for one migration window.

Control commands stay in `MusicControlCommands`: existing `pause` / `resume` / `duck` / `restore_volume`, plus `next` / `previous`.

## Configuration (target)

```json
{
  "Music": { "Provider": "Tauon" },
  "Tauon": {
    "BaseUrl": "http://127.0.0.1:7814",
    "TimeoutSeconds": 2
  }
}
```

Providers: `Tauon` (default) | `Mock`.

`--quick` selects the mock player (`Music:Provider=Mock`). There is no `--use-mock-spotify` alias; set `Music:Provider=Mock` without `--quick` if you need the mock while keeping CS2 setup.

Default bind for Tauon is loopback `127.0.0.1`. Do not default to `0.0.0.0`. Tauon's own process may listen on all interfaces; Undefault should still call loopback. See [tauon-integration.md](tauon-integration.md).

## Failure rule

If the player is down, GSI and the host keep running. One bounded request per action. No retry storms.

## Out of scope here

Live `IMusicMixer` / safety facade side effects. Shadow diagnostics may stay. One orchestration entry still applies playback per GSI tick ([rules-engine-migration.md](rules-engine-migration.md)).
