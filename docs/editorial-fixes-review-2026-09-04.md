# Tech-lead review: editorial fixes proposal (2026-09-04)

**Reviewed:** [editorial-fixes-proposal-2026-09-04.md](editorial-fixes-proposal-2026-09-04.md) (commit `32e9c4b`)
**Second pass:** validated against `main` at `88cb03e` (code + tests + docs; Linear MCP still unauthenticated)
**Verdict:** Items 1 and 3 are sound and can proceed after Linear filing. Item 2 is low value as scoped; reduce it to an optional two-line default flip. Item 4 mixes two changes: `DomainEvents` can proceed; `spotify.control_profile` alias removal revises PIVOT-6 and needs a product-owner call plus a safety net.

Baseline reported by the first reviewer on `32e9c4b`: `Core.Tests` 79/79, `GsiHost.Tests` 190/190 (Linux run with `-p:EnableWindowsTargeting=true`; CI uses `windows-latest`). Second pass did not re-run the suites. Attribute counts on this tree: 77 `[Fact]`/`[Theory]` in `Core.Tests`, 152 in `GsiHost.Tests` (xUnit case totals are higher because of `[InlineData]`). Use a fresh `dotnet test UndefaultIt.sln` as the implementation baseline.

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
| `RulesEngine` logs unknown action keys | **No.** `ExecuteActionsAsync` silently `continue`s when the action key is missing from `_actionsByKey`. The proposal's "no-op/log" wording is wrong. |

## Findings

### Blocker

- **Item 4 alias removal without a safety net.** If an on-disk `appsettings.json` still maps `round_start`/`death` to `spotify.control_profile`, removing the alias makes Flow stop silently: no log, no startup warning. Before or with the removal, add a startup warning (or `RulesEngine` ctor warning) for every `ActionMap` key that has no registered `IEventAction`, and cover it with a test. Also record the PIVOT-6 revision in `docs/roadmap.md` (row PIVOT-6 and the "Current code" bullet) and `docs/music-provider-architecture.md:113`. **Alias removal** needs explicit product-owner approval; do not bundle it with items 1–3. **`DomainEvents` is not a PIVOT-6 decision** and is not blocked on that call (see below).

### Suggestions

- **Item 1 — `/config` is a wire-shape change.** `SystemConfig` is `(Spotify, Gsi)`; `PUT /config` currently requires `{ spotify, gsi }`. Removing `SpotifySystemConfig` changes the request/response contract. No UI consumer exists, so proceed, but list it explicitly and extend `ConfigEndpoint_DoesNotExposeUseMockSpotify` to assert `spotify` is absent.
- **Item 1 — strip orphans on save.** `AppSettingsConfigurationService.SaveAsync` already removes `UseMockSpotify`. Do the same for `Spotify` and `SmartTrackStart` nodes so owner on-disk files converge; otherwise the git file is clean and every install is not.
- **Item 1 — docs sweep is underestimated.** `docs/backend-architecture.md` has 47 Spotify mentions and still describes `SpotifyControlProfileAction`, `ISpotifyPlaybackControl`, `JsonSmartTrackStartService`, `spotify.profile`, `spotify.volume_duck`, `/profiles`. Most of that is already wrong today. Either name the sections to rewrite or split the docs sweep into its own step; "docs приведены в соответствие" is not verifiable as written. Also drop the stale "Leftover Spotify flags remain until `PIVOT-11`" line in `docs/tauon-integration.md:77` (PIVOT-11 is Done).
- **Add `.cursor/rules` to the sweep.** `core-architecture.mdc:14` and `code-reviewer.mdc:13–14,36` still say `ISpotifyClient` types remain until PIVOT-10 and that GsiHost holds Spotify OAuth. **Also `gsihost-architecture.mdc:12`** ("Spotify OAuth" as a current host adapter). Same "misleads agents" argument as item 1; same PR.
- **Item 3 — do not silently remove `--mvp`.** Silent removal turns `dotnet run -- --mvp` into a live automation launch, the opposite of what the tester expected (ASP.NET will not fail on an unused switch; `ConsoleLaunchBootstrap` would simply ignore it). Fail fast with a message. Keep a one-flag observe launch (`--intent-capture` should imply `Timeline:Enabled` + `PlaybackObserver:Enabled`, or add `--observe`), otherwise owner tooling regresses. Update `ConsoleLaunchSettings.IsMvpLaunch`, the two `Program.cs` checklist lines, `PlaybackObserverOptions.cs:26`, `ConsoleLaunchBootstrapTests` (today they treat `--mvp` as the one-flag observe launch), `docs/quick-launch.md`, `docs/release-checklist.md`, `docs/tauon-integration.md:70`, `docs/README.md:26`, `docs/backend-architecture.md:75`, `.cursor/rules/product-boundaries.mdc`, `.cursor/rules/hotkeys-timeline.mdc`. Archived docs under `docs/archive/` may keep the historical flag.
- **Item 4 — split `DomainEvents` from the alias.** `AdapterObservation` constructors in `EventDetectorTests`, `ProfileRoutingTests`, `ShadowMusicOrchestrationFacadeTests`, `Cs2GameAdapterTests`; docs `ingestion-spec-cs2-dota.md:31`, `rules-engine-migration.md:27`, `multi-adapter-routing.md`. Low value, moderate churn. Acceptable as a separate commit from the alias; **not blocked on the PIVOT-6 PO call.**

### Superfluous

- **Item 2 — conditional DI and `Deferred/` move.** `ShadowMusicOrchestrationFacade` is a stateless singleton; conditional registration adds a branch for no runtime gain, and `IShadowMusicSnapshotSink` must stay for `/diagnostics/music-shadow` anyway (`GsiProcessingService` and `GsiResetService` take it in the ctor). Moving files does not change behavior and would split `MusicSafetyState` / `TransportIntentNeutral` (used by the live observation contract) from the types that depend on them. Drop both sub-items.
- **Item 2 — the default flip itself is optional.** It does not end "two orchestration stories"; docs do, and `backend-architecture.md:328` already says do not implement Phase B/C. Observe-only cost is one extra call per GSI tick behind a try/catch. Do not make this a Linear issue of its own. If flipped, it is a two-line diff (`MusicOrchestrationOptions` + `appsettings.json`) plus the test fixture default. **Do not gate the flip on UND-83.** UND-83 DoD is live Tauon resume/pause via the simulator (`docs/tauon-integration.md` smoke: default launch, no `--mvp`). `/diagnostics/music-shadow` is not in that procedure.
- **Item 3 — the "~500+ LOC fork" argument.** Timeline + Observer + recorder is ~800 LOC, but item 3 renames/errors a flag and touches a small bootstrap surface. The LOC argument belongs to a deletion the item explicitly does not do. Keep the item; drop the argument.

## Answers to the proposal's questions

1. **Hard error on `--mvp`**, with the message pointing to the default launch and to the observe flag. Silent removal is unsafe (see above).
2. **Do not treat `ShadowMode=false` as required work.** No `Deferred/` move, no conditional DI. Optional two-line flip only, not blocked on UND-83.
3. **Rename `SpotifyVolumeDuck` → `VolumeDuck` in the same item-1 PR.** No dual binding: code defaults equal the shipped values, so an unrenamed on-disk section falls back to identical behavior unless the owner customised it; state that in the PR.
4. **One umbrella issue with sub-issues per item**, separate PRs. Alias sub-issue is blocked on the PIVOT-6 decision and must not gate 1–3. `DomainEvents` can be a commit under item 1 or a tiny follow-up PR; it does not need its own PO gate.

## Additions to the plan

- Startup/ctor warning for `ActionMap` keys with no registered action, with test (prerequisite for alias removal).
- `.cursor/rules/*.mdc` stale Spotify statements in the item-1 sweep, including `gsihost-architecture.mdc`.
- `AppSettingsConfigurationService.SaveAsync` strips `Spotify` / `SmartTrackStart` nodes.
- Explicit file list for the docs sweep, with `docs/backend-architecture.md` sections named; include `docs/tauon-integration.md` PIVOT-11 leftover line.
- Roadmap PIVOT-6 row and `music-provider-architecture.md` alias line updated when the alias lands.
- `--intent-capture` (or a new `--observe`) becomes the one-flag observe launch **before** `--mvp` is turned into a hard error.

## Revised order

`1 → 3 → DomainEvents (unblocked) → (2, optional) → alias (after PO decision + ActionMap warning)`

Items 1 and the alias both edit the `BuildAppSettingsJson` fixture in `GsiHostIntegrationTests`; expect a small rebase when the alias lands.

## Acceptance (amended)

- `dotnet test UndefaultIt.sln` green on `windows-latest`.
- Default launch without flags still does Flow pause/resume through Mock/Tauon (existing integration tests cover this).
- `dotnet run -- --mvp` exits with a non-zero code and a message; no player call is made.
- `--intent-capture` (or `--observe`) is a one-flag observe launch: `intent_capture` + Timeline + PlaybackObserver.
- A host started with `ActionMap` → `spotify.control_profile` logs a warning at startup (**alias item only**).
- No new features; no Spotify provider; no mixer → player wiring.

## Second-pass corrections (this file)

| First-pass claim | Correction |
|---|---|
| Rules sweep: `core-architecture.mdc` + `code-reviewer.mdc` | Also `gsihost-architecture.mdc:12`. |
| Item 4 wholly blocked on PIVOT-6 PO | Only the alias. `DomainEvents` is unused scaffolding, not an approved keep. |
| Keep `ShadowMode=true` until UND-83 is recorded | UND-83 does not use the shadow endpoint. Optional flip; no UND-83 gate. |
| Item 1 docs list | Add `docs/tauon-integration.md:77` (PIVOT-11 leftover). |
