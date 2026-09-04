# Documentation

Long-form documentation for UndefaultIt. The [repository README](../README.md) is the short overview. Start with the [product pivot](product-pivot-2026-08-14.md) if you need the 2026-08-14 direction, then the [SMTC onboarding decision](smtc-onboarding-decision-2026-09-01.md) for what shipped after that.

**Code vs docs:** live automation is `IMusicPlayer` (Tauon default, SMTC, Mock). Spotify is not a provider (excision UND-84 / UND-101). Owner-run live checks: [roadmap.md](roadmap.md).

## Product

| Document | What it covers |
|----------|----------------|
| [Product pivot (2026-08-14)](product-pivot-2026-08-14.md) | Locked direction: automation layer, Tauon first |
| [SMTC onboarding decision (2026-09-01)](smtc-onboarding-decision-2026-09-01.md) | SMTC adapter + localhost onboarding; UI non-goal superseded |
| [Roadmap](roadmap.md) | `PIVOT-*` tasks, current vs target, unproven live checks |
| [Music provider architecture](music-provider-architecture.md) | Registry, capability matrix, coordinator gate |
| [Tauon integration](tauon-integration.md) | Remote API, security, setup (`TauonMusicPlayer` in host) |
| [Windows SMTC integration](windows-smtc-integration.md) | Exact-id adapter, onboarding HTTP, ForceUpdate reattach |
| [Windows SMTC spike report](windows-smtc-spike-report.md) | UND-89 go/no-go evidence (harness only) |
| [Playback policy](spotify-playback-policy-boundary.md) | Local control, not a soundtrack |
| [Spotify constraints](spotify-constraints.md) | Why Spotify is not a product backend; agent do-nots |

## Guides (current binary)

| Document | What it covers |
|----------|----------------|
| [Backend architecture](backend-architecture.md) | Pipeline, HTTP API, config as implemented today |
| [Quick launch](quick-launch.md) | Flags (`--quick`, `--mvp`) on the current host |
| [CS2 GSI events](cs2-gsi-events.md) | Practical CS2 signal space vs current mapping |
| [CS2 GSI simulator](cs2-simulator.md) | Local scenarios that post to `POST /gsi` |
| [Continuous integration](ci.md) | GitHub Actions build/test |
| [Release pipeline design](release-pipeline-design.md) | UND-31 packaging design; Spotify secret sections are obsolete |
| [.NET TFM decision](dotnet-tfm-decision.md) | UND-30/46 research plus UND-92 Windows TFM note |

## Later (not Tauon MVP)

These specs stay valid for a future safety/mixer engine. They are **not** current implementation work.

| Document | What it covers |
|----------|----------------|
| [Music safety state](music-safety-state-spec.md) | `Unknown` / `Safe` / `Danger` |
| [Failure safety](failure-safety-spec.md) | Stale GSI, device failure |
| [Volume composition](volume-composition-spec.md) | Merge algebra |
| [Stability & device layer](stability-and-device-layer-spec.md) | Coalescing, emergency lane |
| [Mixer contract](mixer-contract-and-device-wiring.md) | `IMusicMixer` (contracts only) |
| [Music engine config v1](music-engine-config-schema-v1.md) | Options + debug snapshot |
| [Rules engine migration](rules-engine-migration.md) | One side-effect path per tick; shadow facade |
| [Neutral signals & GameClock](neutral-signals-and-game-clock.md) | Cross-title clock |
| [Ingestion spec (CS2 / Dota)](ingestion-spec-cs2-dota.md) | Snapshot extensions |
| [Multi-adapter routing](multi-adapter-routing.md) | One host, per-title endpoints |
| [Mandatory CS2 ingestion](mandatory-cs2-ingestion-checklist.md) | Inputs before a richer music controller |

## Working notes (not product docs)

| Document | What it covers |
|----------|----------------|
| [Editorial fixes proposal (2026-09-04)](editorial-fixes-proposal-2026-09-04.md) | Four leftover/overengineering items; revised after review |
| [Editorial fixes review (2026-09-04)](editorial-fixes-review-2026-09-04.md) | Tech-lead review + second-pass validation; source of truth for implementation |

## Archive

Superseded slices and long Spotify research: [archive/](archive/README.md).

## In-repo pointers

- **Host entry and routes:** `GsiHost/Program.cs`
- **GSI processing:** `GsiHost/Services/GsiProcessingService.cs`
- **Console bootstrap:** `GsiHost/Services/ConsoleLaunchBootstrap.cs`
- **Onboarding page:** `GsiHost/wwwroot/index.html`
- **Per-area notes:** `GsiHost/Endpoints/README.md`, `GsiHost/Middleware/README.md`
