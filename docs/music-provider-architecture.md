# Music provider architecture

Approved 2026-08-14; SMTC added 2026-09-01. This page describes the **shipped** `IMusicPlayer` registry, not a target sketch.

Live automation: `IEventAction` → `IMusicPlaybackControl` → `IMusicPlayer` → Tauon (default), SMTC, or Mock. Spotify is not a provider. See [product-pivot-2026-08-14.md](product-pivot-2026-08-14.md) and [smtc-onboarding-decision-2026-09-01.md](smtc-onboarding-decision-2026-09-01.md).

## Principle

Undefault does not care where the music comes from. Core talks to a small player abstraction. HTTP, WinRT, and vendor URLs stay in adapters.

```text
Game Adapter → EventDetector → RulesEngine → IEventAction
  → IMusicPlaybackControl
  → IMusicPlayer
       ├── TauonMusicPlayer     (default; Music:Provider=Tauon)
       ├── SmtcMusicPlayer      (Windows TFM; Music:Provider=Smtc)
       └── MockMusicPlayer      (--quick or Music:Provider=Mock)
```

Do not add a Spotify adapter. See [spotify-constraints.md](spotify-constraints.md). Leftover `Spotify` keys in `appsettings.json`, if still present, are deletion-only and are not a registered provider.

Core must not contain `if provider == Tauon` (or SMTC) in game/event/rule logic.

## Preserve

The existing GSI pipeline stays:

`POST /gsi` → `Cs2GameAdapter` → `AdapterObservation` → `RulesEngine` → `EventDetector` → `ActionMap` → `IEventAction`

Do not redesign that pipeline for a provider change.

## Provider registry

`MusicProviderResolver` accepts `Tauon`, `Smtc`, and `Mock` (case-insensitive). Blank or missing `Music:Provider` becomes `Tauon`. Unknown names throw at startup; they never become Tauon.

Default `GsiHost/appsettings.json` is still `"Provider": "Tauon"`. `--quick` overrides to `Mock`.

`Smtc` is registered only on the Windows TFM (`net8.0-windows10.0.19041.0`). The plain `net8.0` GsiHost build is a compile shim for `Cs2Simulator.Tests`. On that shim, `Smtc` is a valid name but `PlayerIsRegistered` is false, and startup throws “not registered” rather than falling back to Tauon.

Ship and run the host as:

```powershell
dotnet run --project GsiHost -f net8.0-windows10.0.19041.0
```

`ISmtcSessionSource` is registered only when `Music:Provider=Smtc`. With the default Tauon (or Mock) provider, `GET /music/sessions` returns an empty list and `POST /music/session` returns HTTP 409. That is residual onboarding behavior, not a host failure. See [windows-smtc-integration.md](windows-smtc-integration.md).

`Core` stays `net8.0` and WinRT-free.

## `IMusicPlayer`

Capabilities only. No playlist, queue, seek, or play-by-track-id.

| Member | Role |
|---|---|
| `IsAvailableAsync` | Player reachable (not OAuth) |
| `GetStateAsync` | status + optional current track + volume |
| `PlayAsync` / `PauseAsync` / `ResumeAsync` | transport |
| `NextAsync` / `PreviousAsync` | skip |
| `SetVolumeAsync` | 0–100 when the adapter supports volume |
| `Capabilities` | static ceiling for this adapter |

`Resume` on Tauon maps to `GET /api1/play`. `Resume` on SMTC maps to the session play command. Do not invent Tauon endpoints.

Onboarding test pause/resume call `IMusicPlayer` directly. Game-triggered commands go through `IMusicPlaybackControl`.

## Two-level capabilities (UND-91)

There are two gates. They are not the same object.

### 1. Static ceiling — `MusicPlayerCapabilities`

Declared on the adapter. `MusicPlaybackControlCoordinator` reads it **before** transport calls and **before** any duck-state mutation. If the flag is false, the coordinator returns `Unsupported` and does not change duck session state.

Shipped values:

| Flag | Tauon (`MusicPlayerCapabilities.Mvp`) | Mock (`Mvp`) | SMTC (`SmtcMusicPlayer.SmtcCapabilities`) |
|---|---|---|---|
| `CanPlay` | true | true | true |
| `CanPause` | true | true | true |
| `CanResume` | true | true | true |
| `CanSkip` | true | true | true |
| `CanSetVolume` | true | true | **false** |

SMTC `SetVolumeAsync` returns `Unsupported` (`"SMTC cannot set volume."`) without talking to WinRT. Duck/restore on the SMTC provider therefore stop at the coordinator gate.

### 2. Dynamic session controls

Per-session flags at call time. These live on the SMTC snapshot (`IsPlayEnabled`, `IsPauseEnabled`, `IsNextEnabled`, `IsPreviousEnabled`), not on `MusicPlayerCapabilities`.

`SmtcMusicPlayer` checks the relevant flag immediately before issuing a command. A disabled control returns `Unsupported` with a reason; it does not command another session. The coordinator does not read these flags.

Tauon and Mock have no SMTC-style dynamic control object. Their ceiling is `Mvp`; ordinary HTTP or in-memory failure is a command result, not a second capability matrix.

## `IMusicPlaybackControl`

Session behavior stays here:

- idempotent pause/resume
- skip redundant commands
- duck/restore volume state (kept in code; not the default Flow/Focus rules)
- static capability gate (above)
- graceful failure (log, do not crash the host)

Provider HTTP and WinRT do **not** live here.

Game-triggered outcomes on this interface are what `{ContentRoot}/go-no-go-counters.json` records (UND-98). Onboarding test pause/resume do not pass this decorator, so they are not written to that file.

## Action keys

Canonical: `music.control_profile`.

Compatibility alias: `spotify.control_profile` (still registered; not a Spotify provider).

Control commands stay in `MusicControlCommands`: `pause` / `resume` / `duck` / `restore_volume` / `next` / `previous`.

Shipped control-profile ids: `flow` (default; `round_start → resume`, `death → pause`) and `focus` (`round_start → pause`, `death → resume`). Focus is a preset, not the product. `console-default` is a legacy id: if it is the active id and a `flow` profile exists, the store remaps to `flow` in memory. Default seed writes `flow` + `focus` only.

## Configuration (shipped)

```json
{
  "Music": {
    "Provider": "Tauon",
    "Smtc": {
      "SourceAppUserModelId": ""
    }
  },
  "Tauon": {
    "BaseUrl": "http://127.0.0.1:7814",
    "TimeoutSeconds": 2
  }
}
```

Providers: `Tauon` (default) | `Smtc` | `Mock`.

`--quick` selects the mock player (`Music:Provider=Mock`). Set `Music:Provider=Mock` without `--quick` if you need the mock while keeping CS2 setup.

Default bind for Tauon is loopback `127.0.0.1`. Do not default to `0.0.0.0`. Tauon's own process may listen on all interfaces; Undefault should still call loopback. See [tauon-integration.md](tauon-integration.md).

SMTC selection is the exact `SourceAppUserModelId` string only. See [windows-smtc-integration.md](windows-smtc-integration.md).

## Failure rule

If the player is down, GSI and the host keep running. One bounded request per action. No retry storms.

## Out of scope here

Live `IMusicMixer` / safety facade side effects. Shadow diagnostics may stay. One orchestration entry still applies playback per GSI tick ([rules-engine-migration.md](rules-engine-migration.md)).
