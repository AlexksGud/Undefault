# Tech-lead review: editorial fixes proposal (2026-09-04)

**Reviewed:** [editorial-fixes-proposal-2026-09-04.md](editorial-fixes-proposal-2026-09-04.md) (commit `32e9c4b`)
**Verdict:** Items 1 and 3 are sound and can proceed after Linear filing. Item 2 is low value as scoped; reduce it. Item 4 revises an approved decision (PIVOT-6) and needs a product-owner call plus a safety net before removal.

Baseline verified on `main` at `32e9c4b`: `Core.Tests` 79/79, `GsiHost.Tests` 190/190 (Linux run with `-p:EnableWindowsTargeting=true`; CI uses `windows-latest`). Linear MCP was not authenticated in this session, so issue state was not cross-checked.

## Claims checked

| Claim | Result |
|---|---|
| `/profiles`, `IProfileService`, track types have no runtime consumer | Confirmed. `wwwroot/app.js` only calls `/music/*` and `/setup/cs2/*`. Only tests reference them. |
| `SmartTrackStart` and `Spotify` appsettings sections are orphans | Confirmed. No code binds `SmartTrackStart`. `Spotify` is read/written only by `AppSettingsConfigurationService` for `GET/PUT /config`. |
| `VolumeDuckOptions` still needed | Confirmed. `MusicPlaybackControlCoordinator` uses it for `duck` / `restore_volume`. Code defaults (0 / 50) equal the shipped appsettings values. |
| `Core/Music` ≈ 1.7k LOC, ~60% of Core | Arithmetic is right (1705 / 2903). Attribution is not: only ~550 LOC is shadow/mixer/intent algebra. The rest (`IMusicPlayer`, coordinator, `MockMusicPlayer`, `MusicCommandResult`, …) is the live MVP path. Do not repeat the 60% figure as an overengineering argument. |
| Facade DI is unconditional, call is behind `ShadowMode` | Confirmed (`Program.cs:58`, `GsiProcessingService.cs:62`). |
| `--mvp` sets `intent_capture`, enables Timeline + Observer, skips actions | Confirmed. Note `--intent-capture` alone does **not** enable the two flags; `--mvp` is currently the only one-flag observe launch. |
| `DomainEvents` always empty, no consumer | Confirmed. `Neutral` is consumed by `EventDetector`; `Safety` only by the shadow facade. |
| Legacy alias registered "just in case" | Partly. `spotify.control_profile` was the git default from `361afaa` until `6c21da2` (2026-08-14). Owner installs may still carry it on disk. Roadmap PIVOT-6 says "keep alias" and is marked Done. |
| `RulesEngine` logs unknown action keys | **No.** `ExecuteActionsAsync` silently `continue`s. The proposal's "no-op/log" wording is wrong. |

## Findings

### Blocker

- **Item 4 alias removal without a safety net.** If an on-disk `appsettings.json` still maps `round_start`/`death` to `spotify.control_profile`, removing the alias makes Flow stop silently: no log, no startup warning. Before or with the removal, add a startup warning (or `RulesEngine` ctor warning) for every `ActionMap` key that has no registered `IEventAction`, and cover it with a test. Also record the PIVOT-6 revision in `docs/roadmap.md` (row PIVOT-6 and the "Current code" bullet) and `docs/music-provider-architecture.md:113`. This item needs explicit product-owner approval; do not bundle it with items 1–3.

### Suggestions

- **Item 1 — `/config` is a wire-shape change.** `PUT /config` currently requires `{ spotify, gsi }`. Removing `SpotifySystemConfig` changes the request/response contract. No UI consumer exists, so proceed, but list it explicitly and extend `ConfigEndpoint_DoesNotExposeUseMockSpotify` to assert `spotify` is absent.
- **Item 1 — strip orphans on save.** `AppSettingsConfigurationService.SaveAsync` already removes `UseMockSpotify`. Do the same for `Spotify` and `SmartTrackStart` nodes so owner on-disk files converge; otherwise the git file is clean and every install is not.
- **Item 1 — docs sweep is underestimated.** `docs/backend-architecture.md` has 47 Spotify mentions and still describes `SpotifyControlProfileAction`, `ISpotifyPlaybackControl`, `JsonSmartTrackStartService`, `spotify.profile`, `spotify.volume_duck`, `/profiles`. Most of that is already wrong today. Either name the sections to rewrite or split the docs sweep into its own step; "docs приведены в соответствие" is not verifiable as written.
- **Add `.cursor/rules` to the sweep.** `core-architecture.mdc:14` and `code-reviewer.mdc:13–14,36` still say `ISpotifyClient` types remain until PIVOT-10 and that GsiHost holds Spotify OAuth. Same "misleads agents" argument as item 1; same PR.
- **Item 3 — do not silently remove `--mvp`.** Silent removal turns `dotnet run -- --mvp` into a live automation launch, the opposite of what the tester expected. Fail fast with a message. Keep a one-flag observe launch (`--intent-capture` should imply `Timeline:Enabled` + `PlaybackObserver:Enabled`, or add `--observe`), otherwise owner tooling regresses. Update `ConsoleLaunchSettings.IsMvpLaunch`, the two `Program.cs` checklist lines, `PlaybackObserverOptions.cs:26`, `docs/quick-launch.md`, `docs/release-checklist.md`, `docs/tauon-integration.md:70`, `docs/README.md:26`, `docs/backend-architecture.md:75`, `.cursor/rules/product-boundaries.mdc`, `.cursor/rules/hotkeys-timeline.mdc`. Archived docs under `docs/archive/` may keep the historical flag.
- **Item 4 — `DomainEvents` removal touches more than listed.** `AdapterObservation` constructors in `EventDetectorTests`, `ProfileRoutingTests`, `ShadowMusicOrchestrationFacadeTests`, `Cs2GameAdapterTests`; docs `ingestion-spec-cs2-dota.md:31`, `rules-engine-migration.md:27`, `multi-adapter-routing.md`. Low value, moderate churn; acceptable but keep it a separate commit from the alias.

### Superfluous

- **Item 2 — conditional DI and `Deferred/` move.** `ShadowMusicOrchestrationFacade` is a stateless singleton; conditional registration adds a branch for no runtime gain, and `IShadowMusicSnapshotSink` must stay for `/diagnostics/music-shadow` anyway. Moving files does not change behavior and would split `MusicSafetyState` / `TransportIntentNeutral` (used by the live observation contract) from the types that depend on them. Drop both sub-items.
- **Item 2 — the default flip itself is optional.** It does not end "two orchestration stories"; docs do, and `backend-architecture.md:328` already says do not implement Phase B/C. `/diagnostics/music-shadow` is the only place that surfaces `SafetyFacts` (stale timestamps, dead/live) during the still-unrecorded UND-83 live smoke. Recommendation: keep `true` until UND-83 is recorded, then flip in a one-line change plus the test fixture default. If flipped now, it is a two-line diff; do not make it a Linear issue of its own.
- **Item 3 — the "~500+ LOC fork" argument.** Timeline + Observer + recorder is ~800 LOC, but item 3 renames a flag and touches ~20 lines of code. The LOC argument belongs to a deletion the item explicitly does not do. Keep the item; drop the argument.

## Answers to the proposal's questions

1. **Hard error on `--mvp`**, with the message pointing to the default launch and to the observe flag. Silent removal is unsafe (see above).
2. **`ShadowMode=false` only, and only after UND-83.** No `Deferred/` move, no conditional DI.
3. **Rename `SpotifyVolumeDuck` → `VolumeDuck` in the same item-1 PR.** No dual binding: code defaults equal the shipped values, so an unrenamed on-disk section falls back to identical behavior unless the owner customised it; state that in the PR.
4. **One umbrella issue with sub-issues per item**, four PRs. Item 4's sub-issue is blocked on the PIVOT-6 decision and must not gate 1–3.

## Additions to the plan

- Startup/ctor warning for `ActionMap` keys with no registered action, with test (prerequisite for item 4).
- `.cursor/rules/*.mdc` stale Spotify statements in the item-1 sweep.
- `AppSettingsConfigurationService.SaveAsync` strips `Spotify` / `SmartTrackStart` nodes.
- Explicit file list for the docs sweep, with `docs/backend-architecture.md` sections named.
- Roadmap PIVOT-6 row and `music-provider-architecture.md` alias line updated when item 4 lands.
- `--intent-capture` (or a new `--observe`) becomes the one-flag observe launch before `--mvp` is removed.

## Revised order

`1 → 3 → (2, optional) → 4 (after PO decision)`

Items 1 and 4 both edit the `BuildAppSettingsJson` fixture in `GsiHostIntegrationTests`; expect a small rebase when 4 lands.

## Acceptance (amended)

- `dotnet test UndefaultIt.sln` green on `windows-latest`.
- Default launch without flags still does Flow pause/resume through Mock/Tauon (existing integration tests cover this).
- `dotnet run -- --mvp` exits with a non-zero code and a message; no player call is made.
- A host started with `ActionMap` → `spotify.control_profile` logs a warning at startup (item 4 only).
- No new features; no Spotify provider; no mixer → player wiring.
