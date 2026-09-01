# .NET Target Framework Decision (UND-30)

Research-only report. No `*.csproj` edits land from this issue. Implementation, if any, is filed as a separate Linear issue once the recommendation here is approved.

## Question

Should the solution stay on its current TFM mix (`net8.0` everywhere except `Core.Tests` on `net9.0`), or align all projects on a single target framework? If we align, on which one — `net8.0` (LTS) or `net9.0` (STS)?

## Current TFM state

Enumerated from each `*.csproj` on `main` at the time of writing. **Historical snapshot:** UND-46 later moved `Core.Tests` to `net8.0`. The table below is unchanged from the UND-30 report and is not current. See the UND-92 note at the end for the host Windows TFM.

| Project | SDK | Kind | `<TargetFramework>` |
|---|---|---|---|
| `Core` | `Microsoft.NET.Sdk` | library | `net8.0` |
| `GsiHost` | `Microsoft.NET.Sdk.Web` | ASP.NET Core app (entry point) | `net8.0` |
| `Cs2Simulator` | `Microsoft.NET.Sdk` (`OutputType=Exe`) | console app | `net8.0` |
| `Cs2Simulator.Runtime` | `Microsoft.NET.Sdk` | library | `net8.0` |
| `Cs2Simulator.Scenarios` | `Microsoft.NET.Sdk` | library | `net8.0` |
| `Cs2Simulator.Tests` | `Microsoft.NET.Sdk` | xunit tests | `net8.0` |
| `GsiHost.Tests` | `Microsoft.NET.Sdk` | xunit + `Microsoft.AspNetCore.Mvc.Testing` 8.0.18 | `net8.0` |
| `Core.Tests` | `Microsoft.NET.Sdk` | xunit tests | **`net9.0`** |

The repository has no `global.json`, no `Directory.Build.props`, and no `Directory.Packages.props` to pin the SDK or central-manage TFMs. The solution file (`UndefaultIt.sln`) lists all eight projects without TFM-related metadata.

Test/runtime package versions worth noting because they constrain TFM choices:

- `Microsoft.NET.Test.Sdk` 17.12.0 — supports both `net8.0` and `net9.0`.
- `xunit` 2.9.2 / `xunit.runner.visualstudio` 2.8.2 — work on both.
- `FluentAssertions` 8.8.0 / `coverlet.collector` 6.0.2 — work on both.
- `Microsoft.AspNetCore.Mvc.Testing` 8.0.18 — pinned to ASP.NET Core 8. This is what binds `GsiHost.Tests` to `net8.0`; bumping `GsiHost` to `net9.0` would force this package up too.
- `Microsoft.Extensions.*` at 10.0.x and `System.Security.Cryptography.ProtectedData` 10.0.5 — these are out-of-band Microsoft.Extensions / runtime packages and resolve correctly against both `net8.0` and `net9.0` runtimes (their own TFM lists include both).

## Why `Core.Tests` ended up on `net9.0`

Not investigated here as a separate question. The likely cause is that `Core.Tests` was created with whatever .NET SDK was current on the machine at creation time (`net9.0`), while the rest of the solution was created earlier on the .NET 8 SDK. This is an unintentional drift, not a deliberate choice — there is no `Core.Tests`-specific reason to require `net9.0` in code (no `net9.0`-only APIs, no `net9.0`-only packages).

## Risks of staying mixed

The current mix is `Core (net8) ← Core.Tests (net9)`. `Core.Tests` references `Core` across TFMs, so the test binary loads `Core` compiled for `net8.0` against the .NET 9 runtime. .NET runtimes are forward-compatible, so this works, but it has costs:

1. **Two .NET runtimes required on every dev / CI / tester machine that runs the full suite.** `dotnet test` against `Core.Tests` needs the .NET 9 runtime; everything else needs .NET 8. Forgetting one yields cryptic restore/run errors. CI must provision both, and any tester or contributor reproduction needs the same.
2. **Two SDKs in CI matrices.** `dotnet build` of the whole solution needs an SDK that can target both `net8.0` and `net9.0`. The .NET 9 SDK can target `net8.0`, so a single `dotnet-version: 9.0.x` line in CI is sufficient for build/test today. But the moment we want a "build with the .NET 8 SDK as a sanity check" job, we have to drop `Core.Tests` or pin a multi-SDK setup. The path of least resistance in CI today is "install .NET 9 SDK only and live with the implicit cross-runtime test."
3. **Drift attractor.** Once one project is on a newer TFM, future agents copy it. The next test project added without thinking will pick the SDK's default and, if that drifts to `net10.0` later, we end up with three TFMs. This has already happened once (the original net8 → mixed transition); the cost of letting it happen again is non-zero.
4. **Asymmetric coverage.** `Core` is exercised by tests running on `net9.0` runtime semantics (GC, JIT, BCL behavior), while in production `Core` runs on the .NET 8 runtime hosted by `GsiHost`. Differences here are usually invisible, but if a regression is ever introduced by a runtime-specific BCL change, tests may pass on `net9.0` and fail on `net8.0` (or vice versa). This is a small but real blind spot.
5. **Package compatibility surface is wider.** Any new package added to `Core` or `Core.Tests` must support both `net8.0` and `net9.0`. The Microsoft.Extensions ecosystem does, so this is mostly a non-issue today, but it adds one extra check to every dependency review and a future package without a `net8.0` TFM (rare for Microsoft.Extensions but real for some third-party libs) becomes a blocker.
6. **Tester / packaging story.** Packaging (UND-32) for the `GsiHost` artifact is built around `net8.0` self-contained or framework-dependent publish. The simulator and `Core` ship inside `GsiHost`'s publish output, which is fine. But the moment we publish or ship test binaries (we shouldn't, but agents may add a "diagnostic" assembly), the mismatched TFM becomes a packaging surprise.

None of these are immediate-correctness risks. They're all "small persistent tax" risks.

## Realistic alignment options

### Option A — Align everything on `net8.0`

Action: change `Core.Tests` `<TargetFramework>` from `net9.0` to `net8.0`. Nothing else changes.

Pros:

- `net8.0` is the .NET LTS release (supported through Nov 2026 per Microsoft's release schedule). Standard / Term / Long Term Support: .NET 8 = LTS, .NET 9 = STS (Standard Term Support, ~18 months, ends ~May 2026).
- Matches what `GsiHost`, `Cs2Simulator`, and every other production assembly already targets. No cross-runtime tests.
- A tester or contributor only needs the .NET 8 runtime to run the full suite; CI needs only the .NET 8 SDK. Simpler artifact story for UND-32 (see "Implications for downstream issues" below).
- `Microsoft.AspNetCore.Mvc.Testing` 8.0.x stays consistent with `GsiHost`'s ASP.NET Core 8.0 runtime. Bumping `GsiHost` later means bumping `MVC.Testing` in lockstep — easier to do once than to keep two pinned versions.
- Aligns with the `package-versions` workspace rule's spirit ("least surprise, fewest moving parts") even though that rule is about NuGet versions, not TFMs.

Cons:

- Forfeits any `net9.0`-only API or runtime improvement. `Core` does not currently use any `net9.0`-only API, so this is hypothetical cost only.
- LTS ends Nov 2026. We will eventually have to migrate. But that migration is forced by Microsoft's support window, not by this decision.

Blockers: none identified. `Microsoft.NET.Test.Sdk` 17.12.0, xunit 2.9.2, FluentAssertions 8.8.0, coverlet 6.0.2 all support `net8.0`. No `net9.0`-only API is referenced in `Core` or `Core.Tests`.

### Option B — Align everything on `net9.0`

Action: change `Core` from `net8.0` to `net9.0`, plus `GsiHost`, `Cs2Simulator`, `Cs2Simulator.Runtime`, `Cs2Simulator.Scenarios`, `Cs2Simulator.Tests`, `GsiHost.Tests`. Bump `Microsoft.AspNetCore.Mvc.Testing` from 8.0.18 to a 9.0.x line so the testing harness matches `GsiHost`'s ASP.NET Core runtime. Bump any ASP.NET Core implicit references that come from `Microsoft.NET.Sdk.Web` to the 9.x line.

Pros:

- Eliminates the cross-runtime coverage gap.
- One SDK, one runtime, simplest CI matrix.
- `net9.0` brings a few platform improvements (`System.Threading.Lock`, performance tweaks) that we don't currently need but that future code in `Core` might want without ceremony.

Cons:

- `net9.0` is **STS, not LTS**. STS releases reach end of support roughly 18 months after release. .NET 9 went GA Nov 2024 → end of support around May 2026. As of today (May 2026) we are *at* the STS end-of-support window for `net9.0`. Aligning everything to `net9.0` right now would put us on a runtime that Microsoft is in the process of (or about to stop) servicing. We would have to immediately plan a migration to `net10.0` (LTS, GA Nov 2025).
- The right LTS-to-LTS path is `net8.0 → net10.0`, not `net8.0 → net9.0`.
- More moving parts: bumping ASP.NET Core, `MVC.Testing`, and rechecking every dependency.
- For tester drops (UND-32), we ship more runtime: a self-contained .NET 9 publish is a few MB larger than .NET 8 (low-double-digit-MB delta). Not decisive.

Blockers: `Microsoft.AspNetCore.Mvc.Testing` 8.0.18 must be bumped to a 9.0.x version. No other blockers; Microsoft.Extensions 10.0.x already supports `net9.0`.

### Option C (sketch only) — Wait and align everything on `net10.0`

Not formally compared because it requires moving production code to a runtime that has only just GA'd. Mentioned for completeness: when `net8.0` leaves LTS in Nov 2026, the natural successor is `net10.0` (LTS), not `net9.0` (already EOL). At that point `Core.Tests`'s current `net9.0` target needs to move regardless. Aligning to `net10.0` is the next-but-one decision, not the current one.

## Recommendation

**Option A. Align everything on `net8.0`.**

Rationale, kept short:

- `Core.Tests` on `net9.0` is unintentional drift, not a deliberate dependency on a `net9.0`-only API.
- `net8.0` is LTS; `net9.0` is STS that ends support around now. Moving the rest of the solution *up* to `net9.0` would put us on a runtime Microsoft is about to stop servicing.
- The migration we will eventually need is `net8.0 → net10.0` (next LTS). Doing `net8.0 → net9.0 → net10.0` is two migrations for the price of one and gives us nothing in return.
- Pulling `Core.Tests` back to `net8.0` is a one-line change with no expected blockers, and it removes the cross-runtime test execution entirely.

Behavior outside this decision is unchanged: 163 tests stay green, no production semantics change, no API surface change.

## Implications for downstream issues

These are written so UND-31 / UND-32 / UND-33 can pin and reference them without re-deriving the answer:

- **CI SDK channel (UND-31, UND-33).** Pin the .NET SDK to the **`8.0.x` channel** for build and test (`dotnet-version: 8.0.x` in `actions/setup-dotnet@v4`, or the team's pinned major). Once `net10.0` becomes the next LTS migration target, the SDK pin moves to `10.0.x`. Until then, do not run a `9.0.x` SDK matrix.
- **Tester artifact runtime (UND-32).** `dotnet publish` runs against `net8.0`. If self-contained, ship the .NET 8 runtime; if framework-dependent, document ".NET 8 Desktop / ASP.NET Core 8 Runtime" as the tester prerequisite.
- **Future net10 migration.** Filed as a follow-up criterion below; not part of this issue.

## Follow-up criteria (suggested, not created)

A separate Linear implementation issue can be filed when the user wants the change to land. Suggested AC for that future issue:

1. `Core.Tests/Core.Tests.csproj` updates `<TargetFramework>` from `net9.0` to `net8.0`.
2. No other `*.csproj` is touched.
3. `dotnet build` and `dotnet test` (whole solution) succeed with only the .NET 8 SDK installed.
4. `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, `FluentAssertions`, `coverlet.collector` are kept at their current versions (no opportunistic upgrades in the same change).
5. All 163 tests stay green; no test count changes; CS2 baseline `round_start → duck` / `death → restore` regressions absent.
6. Optional, low-cost hardening: add a minimal `global.json` pinning `"sdk": { "version": "8.0.<latest>", "rollForward": "latestFeature" }` so future SDK installs on contributor machines do not silently re-create `net9.0` projects. Add only if the user wants it; not required by this AC.

A second, much later follow-up — "migrate the whole solution from `net8.0` to `net10.0`" — is filed when:

- `net8.0` is within ~6 months of LTS end (i.e. mid-2026 onwards),
- or a feature in `net10.0` is materially needed,
- whichever comes first.

That migration's AC will include bumping `Microsoft.AspNetCore.Mvc.Testing` to the matching ASP.NET Core 10 line and re-running the CS2 baseline.

## Out of scope for this report

- No `*.csproj` is edited as part of UND-30. Implementation lands in the follow-up issue above, after user approval.
- Central package management (`Directory.Packages.props`) is a separate question.
- ASP.NET Core / Kestrel runtime upgrade beyond what TFM alignment requires is not part of this decision.
- UND-37 territory (`Core/Adapters/*`, `Core/Music/*`, `Core/Rules/*`, `GsiHost/Adapters/Cs2GameAdapter.cs`, `GsiHost/Services/GsiProcessingService.cs`) is untouched.

## Host Windows TFM for SMTC (UND-92, 2026-09-01)

`GsiHost` and `GsiHost.Tests` now target `net8.0-windows10.0.19041.0` so the host can consume WinRT/SMTC APIs. `GsiHost` also still builds `net8.0` because `Cs2Simulator.Tests` (which stays `net8.0`) references GsiHost DTOs/mapping; a `net8.0` test project cannot reference a Windows-only TFM. `Core`, `Core.Tests`, `Cs2Simulator`, `Cs2Simulator.Runtime`, `Cs2Simulator.Scenarios`, and `Cs2Simulator.Tests` remain `net8.0`. Core stays WinRT-free. No WinRT types or Windows Runtime packages are in GsiHost yet (the SMTC adapter is a later issue).

This does not reopen UND-30 / UND-46. `Core.Tests` was aligned to `net8.0` in UND-46; the table in "Current TFM state" above is the UND-30 snapshot and is not current.
