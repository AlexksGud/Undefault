# SMTC onboarding decision — 2026-09-01

Locked product-owner direction after the UND-89 SMTC spike **go** and the UND-87 onboarding slice. This note records what shipped in code (checked 2026-09-01 against `main` `829add0`) and what is still unproven. It does not replace [product-pivot-2026-08-14.md](product-pivot-2026-08-14.md) except where a 2026-08-14 non-goal is marked superseded below.

Spike evidence (no adapter): [windows-smtc-spike-report.md](windows-smtc-spike-report.md). Adapter runbook: [windows-smtc-integration.md](windows-smtc-integration.md). Contracts: [music-provider-architecture.md](music-provider-architecture.md).

## What Undefault is

Undefault is a **local Windows event→action engine**. It reads game state, detects events, and tells the user's **existing** music player what to do.

It is not a CS2 companion, jukebox, soundtrack, Spotify product, or catalog owner.

Default (Flow): `round_start → resume`, `death → pause`. Focus (`round_start → pause`, `death → resume`) is a **preset, not the product**.

First game: CS2. Playback backends: Tauon via `IMusicPlayer` (default), Mock for `--quick`, SMTC via exact `SourceAppUserModelId` when `Music:Provider=Smtc`.

## SMTC direction

SMTC is an **additional** `IMusicPlayer` adapter, not a replacement of the Tauon default.

- Target one session by exact ordinal `SourceAppUserModelId`.
- Never use `GetCurrentSession()` as a command target.
- Volume is unsupported.
- Dubya `WindowsMediaController` 2.5.6 is the WinRT implementation **behind** `ISmtcSessionSource` only.
- Reattach is `ForceUpdate` on command-miss plus a 2s timer while the selected id is absent. It is not `SessionsChanged` alone.
- Display names come from vendored `GsiHost/Data/players.win.json`. No runtime HTTP to that catalog.

Do not fork Tauon. Do not add a Spotify adapter.

## Onboarding surface (supersedes “no UI in that slice”)

The 2026-08-14 pivot listed UI as out of the Tauon MVP slice. That non-goal is **knowingly superseded** on 2026-09-01 by the localhost onboarding page (UND-96), served as static files from `GsiHost/wwwroot` (`UseDefaultFiles` + `UseStaticFiles`; `GET /` → `index.html`).

That page is local host UI: pick a present SMTC session, test pause/resume, choose Flow or Focus. It is not a desktop app, overlay, or CS2 companion.

HTTP (UND-95): `/music/sessions`, `/music/session`, `/music/test/pause`, `/music/test/resume`, `/music/last-command`, `/music/preset`.

Residual: `ISmtcSessionSource` is registered only for `Music:Provider=Smtc`. Default Tauon: session list empty, `POST /music/session` 409.

## Host shape that shipped

- Dual TFM: GsiHost `net8.0-windows10.0.19041.0;net8.0`. Ship/run is the Windows TFM. Plain net8.0 is a compile shim for `Cs2Simulator.Tests`. Core is net8.0, WinRT-free.
- Provider resolver: `Tauon | Smtc | Mock`. Unknown throws. Blank → Tauon. Appsettings default remains Tauon.
- Control profiles: shipped ids `flow` (default) and `focus`. `console-default` remaps to Flow in memory when `flow` exists.
- Local counters (UND-98): `{ContentRoot}/go-no-go-counters.json` for game-triggered `IMusicPlaybackControl` only. Onboarding tests via `IMusicPlayer` are not in that file. A game session is the first Applied this process, or Applied after ≥30 minutes idle; restarting the host during a match increments the session count on the next Applied.
- Dota: `POST /gsi/dota` logs only (UND-80). No full Dota runtime.

## Still unproven (owner-run)

In-repo tests are not these checks.

| Item | Status |
|---|---|
| UND-83 live Tauon + CS2 (or simulator) `round_start` resume / `death` pause | Owner-run |
| UND-94 live pause/resume on a **published** exe against a real SMTC session | Unproven / owner-run |
| UND-97 live kill/restart of the player on a **published** exe | Unproven / owner-run |
| UND-92 unpackaged publish on **Windows 10** (`/status` up) | Unproven |

## Linear

Parent: [UND-87](https://linear.app/undefault/issue/UND-87/mvp-player-onboarding-via-explicit-media-session-selection). Docs pass: [UND-99](https://linear.app/undefault/issue/UND-99/narrow-docs-and-linear-update-for-what-actually-shipped). Milestone close is a PM step after merge, not part of this file.
