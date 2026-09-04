# Roadmap

Product direction: [product-pivot-2026-08-14.md](product-pivot-2026-08-14.md). SMTC + localhost onboarding: [smtc-onboarding-decision-2026-09-01.md](smtc-onboarding-decision-2026-09-01.md). Locked Linear decision: [MVP decision — 2026-09-01](https://linear.app/undefault/document/mvp-decision-2026-09-01-cs2-tauon-spotify-dropped-ae332adffdfc).

Undefault is a local Windows event→action engine. Default player backend is Tauon. SMTC is an additional backend when `Music:Provider=Smtc`. Spotify is dropped: leftover `appsettings.json` keys are deletion-only, not a provider to keep or revive.

Linear (workspace Undefault, project `undefault`) is the source of truth for open work. In-repo `PIVOT-*` IDs below map to those issues.

## How to read this

| Column | Meaning |
|---|---|
| **Current code** | What the tree does today |
| **Approved target** | What we migrated to |
| **PIVOT-n** | Implementation task |
| **Unproven** | In-repo tests exist or code shipped; live owner-run proof has not been recorded |

## Current code (honest)

- Pipeline: CS2 GSI → adapter → detector → `RulesEngine` → `music.control_profile`
- Default rules (Flow): `round_start → resume`, `death → pause`. Focus preset: `round_start → pause`, `death → resume`. Shipped profile ids `flow` (default) and `focus`. `console-default` remaps to Flow in memory when `flow` exists.
- Device: `IMusicPlayer` — `TauonMusicPlayer` (default), `SmtcMusicPlayer` (`Music:Provider=Smtc`, Windows TFM), `MockMusicPlayer` (`--quick`)
- Provider resolver: `Tauon | Smtc | Mock`. Unknown throws. Blank → Tauon. Appsettings default remains Tauon.
- Dual TFM: GsiHost `net8.0-windows10.0.19041.0;net8.0`. Ship/run: `dotnet run --project GsiHost -f net8.0-windows10.0.19041.0`. Plain net8.0 is a compile shim for `Cs2Simulator.Tests`. Core is net8.0, WinRT-free.
- Localhost onboarding: `UseDefaultFiles` + `UseStaticFiles`; `GET /` serves `wwwroot/index.html`. Routes: `/music/sessions`, `/music/session`, `/music/test/pause`, `/music/test/resume`, `/music/last-command`, `/music/preset`.
- Residual: `ISmtcSessionSource` only when `Music:Provider=Smtc`. Default Tauon: `GET /music/sessions` empty, `POST /music/session` 409.
- Local counters: `{ContentRoot}/go-no-go-counters.json` for game-triggered `IMusicPlaybackControl` only. Onboarding test pause/resume via `IMusicPlayer` are not in that file. Game session = first Applied this process, or Applied after ≥30 min idle; host restart during a match increments the session count.
- Spotify OAuth and leftover observe client types were deleted (UND-84, UND-101). Do not add a Spotify adapter.
- `--mvp` is rejected at startup. Leftover observe+record is `--intent-capture` (canceled UND-64), not the product MVP
- Dota: `POST /gsi/dota` logs only (UND-80)
- Safety/mixer: shadow diagnostics only (`MusicOrchestration:ShadowMode` default `false`)

## Approved target MVP

```text
CS2 round_start → resume
CS2 death       → pause
```

via `IMusicPlayer` → default `TauonMusicPlayer` → Tauon HTTP. Mock for tests and `--quick`. SMTC when the user selects `Music:Provider=Smtc` and an exact session id.

## Unproven / owner-run

These are **not** claimed done by in-repo tests.

| Item | Linear | What is missing |
|---|---|---|
| Live Tauon + CS2 (or simulator) resume/pause | [UND-83](https://linear.app/undefault/issue/UND-83/live-tauon-smoke-round-start-resume-death-pause) | Owner-run against real Tauon HTTP |
| Live SMTC pause/resume on a **published** exe vs a real session | [UND-94](https://linear.app/undefault/issue/UND-94/smtcmusicplayer-with-exact-sourceappusermodelid-session-selection) | Adapter + fake-source tests shipped; published-exe live check unproven |
| Live SMTC reattach after kill/restart on a **published** exe | [UND-97](https://linear.app/undefault/issue/UND-97/reattach-the-selected-media-session-after-the-player-restarts) | ForceUpdate + 2s timer unit-tested; live published path unproven |
| Unpackaged publish on **Windows 10** | [UND-92](https://linear.app/undefault/issue/UND-92/host-readiness-for-the-smtc-provider-tfm-bump-and-single-source) | Windows TFM shipped; Win10 unpackaged `/status` start unproven |

## Milestone 0 — Docs (this change)

| ID | Task | Status |
|---|---|---|
| PIVOT-0 | Reframe docs, archive stale Spotify text, publish this backlog | Done in-repo |

## Milestone 1 — Provider abstraction

Do not redesign GSI/rules. Smallest `IMusicPlayer` only.

| ID | Task | Depends | Acceptance | Status |
|---|---|---|---|---|
| PIVOT-1 | Add `IMusicPlayer`, `MusicPlaybackState`, `PlaybackStatus`, `MusicTrack`, `MusicPlayerCapabilities` in Core. No playlist/queue/seek. | PIVOT-0 | Types compile; Core has no Tauon HTTP | Done in-repo |
| PIVOT-2 | Evolve `ISpotifyPlaybackControl` → `IMusicPlaybackControl` using `IMusicPlayer`. Drop Spotify auth checks. Keep idempotent pause/resume, duck state, fail-soft. Keep `ISpotifyPlaybackControl` as a thin alias if that avoids a noisy diff. | PIVOT-1 | Existing coordinator tests retargeted; no `IsAuthenticated` on the generic path | Done in-repo |
| PIVOT-3 | `MockMusicPlayer` from `MockSpotifyClient` patterns. Always available. `--quick` / `Music:Provider=Mock`. | PIVOT-1 | Core tests run without Tauon or Spotify | Done in-repo |

**Non-goals:** live mixer, new command architecture, adding a Spotify provider.

## Milestone 2 — Tauon adapter

| ID | Task | Depends | Acceptance | Status |
|---|---|---|---|---|
| PIVOT-4 | `TauonMusicPlayer` in the host/adapter layer (not Core). Verified `/api1` paths only. `HttpClient`, timeout, CT. 404 / refused / timeout / malformed JSON → unavailable. No retries. Resume = `/play` unless already playing. | PIVOT-1 | Unit tests with mocked HTTP; no live Tauon in CI | Done in-repo |
| PIVOT-5 | Host DI + config: `Music:Provider` = `Tauon` \| `Mock`. Default Tauon. `Tauon:BaseUrl` = `http://127.0.0.1:7814`. Do not register a user-facing Spotify provider. | PIVOT-2, PIVOT-3, PIVOT-4 | Switching provider does not put Tauon URLs in Core | Done in-repo; later: `Smtc` is a third accepted name on the Windows TFM |

## Milestone 3 — Product defaults

| ID | Task | Depends | Acceptance | Status |
|---|---|---|---|---|
| PIVOT-6 | Canonical action `music.control_profile`. Default profile `round_start → resume`, `death → pause`. The `spotify.control_profile` alias was removed; unknown ActionMap keys warn at `RulesEngine` construction. | PIVOT-2 | Simulator round/death drives those commands | Done in-repo; alias removed |
| PIVOT-7 | Add `next` / `previous` to `MusicControlCommands` and the coordinator. | PIVOT-2, PIVOT-4 | Commands route; Tauon uses `/next` and `/back` | Done in-repo |

**Non-goals:** `round_end`, victory music, playlist rules.

## Milestone 4 — Tests and proof

| ID | Task | Depends | Acceptance | Status |
|---|---|---|---|---|
| PIVOT-8 | Core: round_start→resume, death→pause, repeated events are idempotent. Tauon: play/pause/resume/next/previous/state/track/volume + refused/timeout/404/malformed/unexpected status. | PIVOT-3–7 | `dotnet test` green without Tauon | Done in-repo |
| PIVOT-9 | Manual proof: Tauon up → simulator resume/pause; Tauon down → host + GSI still run. | PIVOT-8 | Matches Definition of Done in the pivot note | Linear: [UND-83](https://linear.app/undefault/issue/UND-83/live-tauon-smoke-round-start-resume-death-pause) **owner-run, unproven in this repo** |

## Milestone 5 — Spotify excision (after green)

Spotify will not return as a product backend. These tasks delete leftovers; they are not a Spotify track.

| ID | Task | Depends | Acceptance | Linear |
|---|---|---|---|---|
| PIVOT-10 | Delete unused Spotify OAuth/client/action paths once Tauon+mock own playback. Do not add `SpotifyMusicPlayer`. | PIVOT-9 | Automation path has no Spotify types | [UND-84](https://linear.app/undefault/issue/UND-84/delete-leftover-spotify-oauthclientaction-paths) Done; leftover observe path [UND-101](https://linear.app/undefault/issue/UND-101/delete-the-remaining-ispotifyclient-observe-path-and-the-corespotify) Done |
| PIVOT-11 | Rename leftover `--quick` / `/spotify/*` flags and comments to player/mock wording. | PIVOT-10 | README/quick-launch match running flags | [UND-85](https://linear.app/undefault/issue/UND-85/rename-leftover-quick-spotify-flags-and-comments) Done |

Current MVP umbrella: [UND-82](https://linear.app/undefault/issue/UND-82/mvp-cs2-player-control-rulesevents-tauon). Onboarding parent: [UND-87](https://linear.app/undefault/issue/UND-87/mvp-player-onboarding-via-explicit-media-session-selection).

## Later (not scheduled)

Keep as specs, not current build work:

- optional external music import companion (design only: [UND-86](https://linear.app/undefault/issue/UND-86/design-optional-external-music-import-companion))
- `round_end` detector (CS2 `phase=over` is mapped to `Unknown` today)
- live safety mixer / coalescer (shadow already exists)
- extra CS2 scenarios / visualization (deferred Linear milestone)
- Dota adapter beyond logging (UND-45)
- packaging
- Jellyfin or other **non-Spotify** players
- desktop / non-localhost UI (localhost onboarding page already ships)

## Guardrails

- Evolution, not a rewrite.
- One playback side-effect path per GSI tick.
- Do not rebuild a music player, a catalog, or a Spotify clone. Do not file Spotify work.
- Tauon API is unstable; keep the adapter thin.
- SMTC: exact ordinal id only; no volume; ForceUpdate reattach, not SessionsChanged alone.
- Safety still dominates adaptivity when that engine is wired; it is not the Tauon MVP.
