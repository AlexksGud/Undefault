# Agent notes

Internal constraints for Cursor / coding agents working on this repo. Not part of the public overview — see [README.md](README.md).

## Project

- `UndefaultIt` is a Windows-first local .NET backend: game events drive an external music player.
- Current runtime focus: `CS2` + `GsiHost`.
- **Approved target (2026-08-14):** Tauon via `IMusicPlayer`; default rules `round_start → resume`, `death → pause`. See [docs/product-pivot-2026-08-14.md](docs/product-pivot-2026-08-14.md).
- **Current code:** `music.control_profile` with `round_start → resume` / `death → pause` through `IMusicPlayer` (Tauon default, Mock for `--quick`). OAuth/real-client Spotify is gone ([UND-84](https://linear.app/undefault/issue/UND-84/delete-leftover-spotify-oauthclientaction-paths)); remaining mock observe path is [UND-101](https://linear.app/undefault/issue/UND-101/delete-the-remaining-ispotifyclient-observe-path-and-the-corespotify). Do not add a Spotify adapter. Live Tauon smoke is [UND-83](https://linear.app/undefault/issue/UND-83/live-tauon-smoke-round-start-resume-death-pause).

## Modules

- `Core/` — models, diffing, event detection, rules, playback abstractions, `Core/Music/` contracts. Must not contain Tauon HTTP or Spotify OAuth.
- `GsiHost/` — HTTP host, GSI mapping, CS2 setup, player adapters, leftover Spotify OAuth.
- `Cs2Simulator*` — local CS2 GSI simulator; see [docs/cs2-simulator.md](docs/cs2-simulator.md).
- `*.Tests/` — unit and integration coverage.

## Runtime flow

`CS2 GSI` → `POST /gsi` → `GsiProcessingService` → adapter → `EventDetector` → `RulesEngine` → `IEventAction` → (target) `IMusicPlaybackControl` → `IMusicPlayer`

## Constraints

- No YAML scenario engine.
- Do not fork Tauon. Adapter uses the verified remote HTTP API only ([docs/tauon-integration.md](docs/tauon-integration.md)).
- One orchestration entry applies playback side effects per GSI tick.
- Playback policy: local control of the user's player, not a synchronized soundtrack ([docs/spotify-playback-policy-boundary.md](docs/spotify-playback-policy-boundary.md)).
- Spotify is dropped ([docs/spotify-constraints.md](docs/spotify-constraints.md)). Do not add Spotify features, OAuth, apps, or adapters. Leftover code is deletion-only (UND-84).
- Safety-first music architecture is documented; do not wire live mixer side effects in the Tauon MVP.
- No full Dota 2 runtime: `POST /gsi/dota` logs only (UND-80); UND-45 is later.
- Linear workspace Undefault is the source of truth. Current MVP: [UND-82](https://linear.app/undefault/issue/UND-82/mvp-cs2-player-control-rulesevents-tauon). In-repo `PIVOT-*` IDs in [docs/roadmap.md](docs/roadmap.md) map to those issues.

## Read first

- [docs/product-pivot-2026-08-14.md](docs/product-pivot-2026-08-14.md)
- [docs/roadmap.md](docs/roadmap.md)
- [docs/music-provider-architecture.md](docs/music-provider-architecture.md)
- [docs/tauon-integration.md](docs/tauon-integration.md)
- [docs/spotify-constraints.md](docs/spotify-constraints.md)
- [docs/README.md](docs/README.md)
- [docs/backend-architecture.md](docs/backend-architecture.md)
- [docs/quick-launch.md](docs/quick-launch.md)
