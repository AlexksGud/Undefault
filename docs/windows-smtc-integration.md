# Windows SMTC integration

Shipped `IMusicPlayer` backend for `Music:Provider=Smtc` (Windows TFM only). Default provider is still Tauon. This page is the SMTC counterpart of [tauon-integration.md](tauon-integration.md). Direction: [smtc-onboarding-decision-2026-09-01.md](smtc-onboarding-decision-2026-09-01.md). Spike evidence: [windows-smtc-spike-report.md](windows-smtc-spike-report.md).

Undefault commands **one** Windows media session: the session whose `SourceAppUserModelId` the user selected. It does not own the player, catalog, or queue.

Do not fork Tauon. Do not add a Spotify adapter. A Spotify row may appear in the SMTC session list if that app publishes a session; that is Windows media control, not a Spotify product provider.

## Runtime

Ship and run:

```powershell
dotnet run --project GsiHost -f net8.0-windows10.0.19041.0
```

`GsiHost` multi-targets `net8.0-windows10.0.19041.0;net8.0`. The Windows TFM is the ship/run build. Plain `net8.0` is a compile shim so `Cs2Simulator.Tests` (net8.0) can reference GsiHost. Core is net8.0 and WinRT-free.

`SmtcMusicPlayer` and `ISmtcSessionSource` are compiled only under the `WINDOWS` define. `ISmtcSessionSource` is registered **only** when `Music:Provider=Smtc`.

With the default Tauon provider (or Mock): `GET /music/sessions` returns `sessions: []`; `POST /music/session` returns HTTP 409 and does not persist. Session listing needs the SMTC provider.

## Exact-id invariant

Commands and selection use **ordinal** equality on `SourceAppUserModelId`.

- Copy the id from an enumerated session (`GET /music/sessions` / `ISmtcSessionSource.GetSessionsAsync`).
- Do not substring-match, case-fold, trim, or fall back to Start-menu AppIDs, process names, or “closest” ids.
- `GetCurrentSession()` / `GetFocusedSession()` is **never** a command target. The adapter may use a focused-id **hint** only for `isWindowsCurrent` on the onboarding list.
- Missing selection, absent id, or zero matches → `Unavailable`; no command is issued to any other session.
- Two sessions with the same id → `Rejected` (ambiguous); no command.

Display names are separate from targeting. Friendly names come from the vendored snapshot `GsiHost/Data/players.win.json` (also embedded as `GsiHost.Data.players.win.json`). The host does **not** HTTP-fetch that catalog at runtime. Lookup prefers ordinal match, then a case-insensitive fallback for display only. Unknown ids show as the raw id. Upstream notice: `GsiHost/Data/NOTICE.media-players.txt`.

## WinRT wrapper

The sole WinRT implementation of `ISmtcSessionSource` is `WindowsMediaControllerSessionSource`, which wraps NuGet `Dubya.WindowsMediaController` **2.5.6**. The adapter, selection logic, and tests talk to `ISmtcSessionSource`, not to `MediaManager`.

Do not float the package version. Do not vendor the library source.

## Volume

SMTC cannot set volume. `SmtcMusicPlayer.Capabilities.CanSetVolume` is `false`. `SetVolumeAsync` returns `Unsupported`. The coordinator capability gate (UND-91) blocks duck-state mutation on this provider. There is no WASAPI per-process duck in this adapter.

## Reattach (UND-97)

Dubya `SessionsChanged` is **not** subscribed and is not the reattach signal.

When the selected id is absent:

1. The command path calls `ISmtcSessionSource.ForceUpdate()`, then enumerates again. Still missing → `Unavailable`.
2. A 2-second timer (`SmtcMusicPlayer.DefaultReattachPollInterval`) calls `ForceUpdate` while the selected id remains absent. The timer does nothing when no id is selected or when the session is already present.

Unit tests cover ForceUpdate on command-miss and the timer. **Live kill/restart of a player against a published exe is unproven / owner-run.**

## Onboarding HTTP (UND-95)

Mapped in `MusicOnboardingEndpoints`. Static files: `UseDefaultFiles` + `UseStaticFiles`. `GET /` serves `wwwroot/index.html`.

| Method | Path | Shipped behavior |
|---|---|---|
| GET | `/music/sessions` | Canonical provider name, selected id, present sessions. Empty list is idle. |
| POST | `/music/session` | Body `{ "appId": "<exact id>" }`. Empty → 400. Id not present (ordinal) → 409, no persist, no command. Present → persist `Music:Smtc:SourceAppUserModelId` in `appsettings.json`. |
| POST | `/music/test/pause` | Test pause via `IMusicPlayer.PauseAsync`. HTTP 200 with outcome; never 5xx for ordinary player failure. |
| POST | `/music/test/resume` | Test resume via `IMusicPlayer.ResumeAsync`. Same HTTP rule. |
| GET | `/music/last-command` | In-memory last test or game command. All-null before any command. |
| GET/POST | `/music/preset` | `Flow` or `Focus`. Unknown name → 400. |

The page also calls existing CS2 setup routes (`GET /setup/cs2/status`, `POST /setup/cs2/install`). Those are not SMTC-specific.

Test pause/resume go through `IMusicPlayer` and `MusicLastCommandStore` with source `test`. They do **not** write `{ContentRoot}/go-no-go-counters.json`. That file records game-triggered `IMusicPlaybackControl` only (UND-98).

## Target Undefault config

```json
{
  "Music": {
    "Provider": "Smtc",
    "Smtc": {
      "SourceAppUserModelId": ""
    }
  }
}
```

`SourceAppUserModelId` is empty until the user selects a present session (or edits the file with an exact id). Empty means no session is selected; transport returns `Unavailable`.

## Not in the adapter

| Topic | Fact |
|---|---|
| Volume | Unsupported |
| Guessed ids | Forbidden |
| `GetCurrentSession` as target | Forbidden |
| `SessionsChanged` as sole reattach | Not used |
| Queue / play by track id | Not on `IMusicPlayer` |
| Spotify Web API | Not a backend |
| Windows 10 unpackaged publish | Code TFM is Win10 19041+; **live Win10 publish is unproven** (UND-92) |
| Live pause/resume vs a real session on published exe | **Unproven / owner-run** (UND-94) |

## Smoke (owner-run)

In-repo tests use `FakeSmtcSessionSource`. They do not replace a live published-exe check.

1. Run the **Windows TFM** host with `Music:Provider=Smtc`.
2. Start a player that publishes an SMTC session (spike evidence: Tauon and Chrome on one Windows 11 machine).
3. Open `http://127.0.0.1:5292/`. Select the exact session. Test pause/resume.
4. Confirm an unrelated session is not commanded (ordinal match only).
5. Optional: kill and restart the player and see whether ForceUpdate reattaches. That live path is **unproven** on a published exe.

Tauon remains controllable over HTTP when `Music:Provider=Tauon`, independent of this adapter.
