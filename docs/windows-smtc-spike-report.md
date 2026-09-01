# Windows SMTC spike report (UND-89)

Evidence-only. No adapter code, no `Core` / `GsiHost` changes. This report is the go/no-go gate for [UND-87](https://linear.app/undefault/issue/UND-87/mvp-player-onboarding-via-explicit-media-session-selection). The Linear comment on UND-87 is out of this file's ownership.

Harness: published unpackaged `SmtcSpike.exe` from [UND-88](https://linear.app/undefault/issue/UND-88/spike-harness-enumerate-smtc-sessions-and-pauseresume-by-exact-app-id) (`tools/SmtcSpike`). Not `dotnet run`.

## Decision

**Go.**

Pause and resume worked with an exact `SourceAppUserModelId` on two real, plugin-free sessions a CS2 user would actually run: **Tauon** (current product backend) and **Chrome**. The exact-id safety invariant held: truncated, case-folded, and Start-menu AppIDs exited `2` and did not touch any other session.

MusicBee / AIMP / foobar2000 plugin requirements are documented coverage gaps, not a no-go. Windows 10 was not run on this machine; that remains an unknown, not invented data.

## Decision rule used

Product-manager interpretation for this issue (not the stricter first-draft Linear text):

- Do not auto no-go because MusicBee or AIMP need a plugin.
- **Go** if pause/resume works with exact `SourceAppUserModelId` for at least one real desktop or browser session without a plugin, and the exact-id safety invariant holds.
- **No-go** only if SMTC cannot reliably pause/resume any player a CS2 user would actually run, without guessing ids.

This run met the go bar on Tauon and Chrome. Store Media Player (`Microsoft.ZuneMusic_*`) returned `True` for `TryPauseAsync` / `TryPlayAsync` here, but `PlaybackStatus` stayed `Changing`; UND-88 already recorded Playing ↔ Paused on that same id.

## Machines

| OS | Edition | Build | Run |
| --- | --- | --- | --- |
| Windows 11 | Home Single Language, 25H2 | `10.0.26200.9168` (WMI caption: Microsoft Windows 11 Home Single Language; `HKLM\...\CurrentVersion\ProductName` still reads "Windows 10 Home Single Language") | **This report.** Unpackaged x64 `SmtcSpike.exe`. |
| Windows 10 | — | — | **Not run.** No Win10 host was available. Do not treat empty Win10 cells as failures. |

`globalMediaControl`: `GlobalSystemMediaTransportControlsSessionManager.RequestAsync()` succeeded from the published unpackaged exe (no MSIX identity, no capability manifest). Empty `list` at the start of the run returned `session count: 0` and exit `0`, so the manager is reachable even when nothing is playing. The Windows 11 volume-flyout / lock-screen media UI was not visually inspected.

## How this run was executed

Worktree `C:\Users\alksg\.cursor\worktrees\und-89-smtc-spike-report`, branch `und-89/smtc-spike-report`, based on `main` `deba592`.

```text
dotnet publish tools/SmtcSpike -c Release -r win-x64 --self-contained false
.\tools\SmtcSpike\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\SmtcSpike.exe
```

Published exe: `SmtcSpike.exe`, 151552 bytes, 2026-09-01.

At spike start: Chrome was already running with no media session. Tauon was installed and not running. Store Media Player and Windows Media Player Legacy were installed and not running. Microsoft Edge browser (`msedge.exe`) was not installed. MusicBee, AIMP, and foobar2000 were not installed. Spotify Store package `SpotifyAB.SpotifyMusic` was installed and not running; no Spotify Web API client was started.

Tauon, Chrome (local mp3 in a new window), Store Media Player, and WMP Legacy were then started from this machine in order to produce sessions. Sessions still only appear once the app has media; an idle player is not a failed `list`.

## Coverage matrix (Windows 11, this machine)

`SourceAppUserModelId` values below are copied from harness output, without the surrounding quotes the harness adds.

| App | Version (this machine) | Publishes a session out of the box | Plugin required | Enabled controls (observed) | After kill / restart |
| --- | --- | --- | --- | --- | --- |
| Tauon Music Box | 11.1.1 (user-agent in `%LocalAppData%\TauonMusicBox\tauon.log`; PE `FileVersion` empty) | Yes, while the process is running and has a current track. Session present in `Stopped` / `Paused` / `Playing`. Id: `Tauon Music Box.exe` | No | Playing: `IsPlayEnabled False`, `IsPauseEnabled True`, `IsNextEnabled True`. Paused/Stopped: play enabled, pause disabled. | Kill → session count 0. Restart + play → **same id** `Tauon Music Box.exe`. |
| Google Chrome | 152.0.7977.65 | Yes, after a tab actually plays. Idle Chrome (this machine at spike start, and UND-88 implementer) produced no session. Id: `Chrome` | No | Playing: pause enabled, play disabled, next disabled (local file). | Not killed (user Chrome left running). Same id as UND-88 reviewer `list` (`Chrome`). |
| Media Player (Store / Zune) | `Microsoft.ZuneMusic` 11.2607.16.0 | Yes, once the app is loading/playing. Idle relaunch produced no session. Id: `Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic` | No | This run: status stuck `Changing`; `IsPauseEnabled True`; title/artist `COMException`. UND-88: Playing/Paused with play/pause flags flipping normally. | Kill → session gone. Idle relaunch → no session (matches "appears only after play"). Id when present matched UND-88 exactly. |
| Windows Media Player Legacy | `wmplayer.exe` 12.0.26100.8457 | **No session** after `/play` of a local mp3. Process did not stay running. Start-menu AppID `Microsoft.Windows.MediaPlayer32` is not an SMTC session. | Research: third-party helper. None installed. Not installed by this spike. | n/a | n/a |
| Microsoft Edge | not installed (`msedge.exe` missing at default paths) | Not run | — | — | — |
| Spotify desktop | Store package `SpotifyAB.SpotifyMusic` 1.296.518.0 installed, process not running | Not run. No Web API client started. | Often none (research prior) | — | — |
| MusicBee | not installed | Not run | Yes — `mb_MediaControl` (research; plugin not installed) | — | — |
| AIMP | not installed | Not run | Yes — plugin / tray bridge (research; plugin not installed) | — | — |
| foobar2000 | not installed | Not run | Yes — UVC and/or `foo_mediacontrol` (research; not installed) | — | — |

Windows 10: every row **not run**.

## Live pause / resume (this run)

Raw harness lines. `returned bool` is SMTC `TryPauseAsync` / `TryPlayAsync`. Exit `0` means that bool was `true`.

### Tauon — `Tauon Music Box.exe`

`list` after play:

```text
[0] SourceAppUserModelId : "Tauon Music Box.exe"
    PlaybackStatus       : Playing
    Title                : "?Shit Is Grim Feat. Trigga"
    Artist               : "?KGB"
    IsPlayEnabled        : False
    IsPauseEnabled       : True
    IsNextEnabled        : True
    IsCurrentSession     : True
```

The `?` prefix on title/artist is what the harness printed (`TryGetMediaPropertiesAsync`); it is not cleaned up here.

```text
target                : "Tauon Music Box.exe"
command               : TryPauseAsync
PlaybackStatus before : Playing
returned bool         : True
PlaybackStatus after  : Paused
```

Exit `0`. Tauon HTTP `GET /api1/status` was `playing` before this call and the SMTC status after was `Paused`.

```text
target                : "Tauon Music Box.exe"
command               : TryPlayAsync
PlaybackStatus before : Paused
returned bool         : True
PlaybackStatus after  : Playing
```

Exit `0`.

After kill and restart, `list` again showed `SourceAppUserModelId : "Tauon Music Box.exe"` and `PlaybackStatus : Playing` following `GET /api1/play`.

### Chrome — `Chrome`

Local mp3 in a new window (`--autoplay-policy=no-user-gesture-required`). YouTube was not used.

```text
[1] SourceAppUserModelId : "Chrome"
    PlaybackStatus       : Playing
    Title                : "04 KGB - Heads On.mp3"
```

```text
target                : "Chrome"
command               : TryPauseAsync
PlaybackStatus before : Playing
returned bool         : True
PlaybackStatus after  : Paused
```

Exit `0`. Tauon stayed `Paused`; Store Media Player stayed `Changing`. Exact-id targeting did not spill.

```text
target                : "Chrome"
command               : TryPlayAsync
PlaybackStatus before : Paused
returned bool         : True
PlaybackStatus after  : Playing
```

Exit `0`. Repeated pause at the end of the run also returned `True` (`Playing` → `Paused`).

### Store Media Player — `Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic`

This run launched the app via `shell:AppsFolder\...` without a reliably loaded track. Status never left `Changing`; metadata threw `COMException`. Commands still returned `True`:

```text
target                : "Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic"
command               : TryPauseAsync
PlaybackStatus before : Changing
returned bool         : True
PlaybackStatus after  : Changing
```

```text
target                : "Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic"
command               : TryPlayAsync
PlaybackStatus before : Changing
returned bool         : True
PlaybackStatus after  : Changing
```

Both exit `0`. Treat the bool as "SMTC accepted the call", not as a clean Playing ↔ Paused proof. That proof is UND-88's WMP run below.

### WMP Legacy — no session

`pause Microsoft.Windows.MediaPlayer32` and `pause wmplayer.exe` both printed `no matching session` and exited `2`. Chrome and Tauon sessions were still listed afterward.

## Exact-id safety (this run)

Invariant: ordinal equality only. No substring, no case fold, no Start-menu fallback.

| Argument | Target present | Result | Exit | Other sessions |
| --- | --- | --- | --- | --- |
| `Tauon Music Box.exe` | yes | pause/resume `True` | 0 | n/a |
| `Tauon` | Tauon session present | `no matching session for SourceAppUserModelId "Tauon"` | 2 | still Playing |
| `Tauon Music Box` | Tauon session present | `no matching session ... "Tauon Music Box"` | 2 | still Playing |
| `tauon music box.exe` | Tauon session present | `no matching session ... "tauon music box.exe"` | 2 | still Playing |
| `{6D809377-6AF0-444B-8957-A3773F02200E}\Tauon Music Box\Tauon Music Box.exe` (Get-StartApps AppID) | Tauon session present | `no matching session` | 2 | untouched |
| `Chrome` | yes | pause/resume `True` | 0 | Tauon untouched |
| `Chro` | Chrome session present | `no matching session for SourceAppUserModelId "Chro"` | 2 | Tauon/Zune untouched |

`GetCurrentSession()` was **not** reference-equal to the matching `GetSessions()` entry on every non-empty `list` in this run (harness printed the existing note). Compare ids, not object identity.

Duplicate-id refusal (exit `3`) was not hit live. No two sessions reported the same id.

## Prior evidence (already verified; not re-run as a substitute for the matrix)

### UND-88 implementer (Windows Media Player)

```text
SourceAppUserModelId : "Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic"
TryPlayAsync returned bool : True, PlaybackStatus after : Playing
TryPauseAsync returned bool : True, PlaybackStatus after : Paused
truncated id: no matching session, exit 2, session untouched
```

Same verbatim id as this run.

### UND-88 reviewer (published `list` only)

`session count: 1`, id `"Chrome"`, status Playing. Truncated id exited `2`. Pause/resume was not re-run in that review.

### Research priors (not product decisions)

- MusicBee needs `mb_MediaControl` (or similar). AIMP needs a plugin. foobar2000 needs Universal Volume Control and/or `foo_mediacontrol`. These are user-side requirements so the player publishes a session. Undefault does not ship or fork those plugins.
- Sessions appear only after play (or, for Tauon on this machine, after the process is up with a current track). Empty `list` with idle apps is expected.
- `SessionsChanged` is flaky in third-party wrappers; this harness does not subscribe. Not re-tested here.
- `Get-StartApps` AppIDs are not SMTC ids. Tauon Start-menu AppID ≠ `Tauon Music Box.exe`. Onboarding must copy the id from an SMTC `list`, not from the Start menu.

Tauon ships a Windows SMTC module (`TauonSMTC`); maintainer discussion [Taiko2k/Tauon#1019](https://github.com/Taiko2k/Tauon/discussions/1019) and the Windows build wiki treat it as present. This report still required a live `list`/`pause`/`resume`. On this install the DLL was not found under a `*smtc*` filename in `C:\Program Files\Tauon Music Box`; SMTC still worked. Historical log line on this machine: `SMTC sent key ID: 2`.

## What was actually run vs not run

**Run (published exe, this Windows 11 machine):**

- `list` with nothing playing → 0 sessions.
- Tauon start, HTTP play, `list`, exact pause/resume, truncated/case/Start-menu refusals, process kill, restart, play, `list` again.
- Chrome local-file playback, `list`, exact pause/resume, truncated refusal.
- Store Media Player launch, `list`, exact pause/resume while status `Changing`, kill, idle relaunch.
- WMP Legacy launch attempts; guessed ids exit `2`.
- Confirm `GetCurrentSession()` not reference-equal to `GetSessions()` entries.

**Not run:**

- Windows 10.
- Microsoft Edge (browser not installed).
- Spotify desktop / Store app (installed, not started; no Web API).
- MusicBee, AIMP, foobar2000, VLC, Winamp (not installed; plugins not installed).
- YouTube / multi-tab Chrome merge (one local-file Chrome session only).
- Duplicate same-id sessions (exit `3`).
- `SessionsChanged` subscription.
- Visual check of the OS media flyout.
- Volume (SMTC has none in this harness).

## Unknowns that survive a go

- Whether Win10 `SourceAppUserModelId` strings match Win11 for the same apps (third-party reports say newer builds changed id shape).
- Whether a YouTube tab uses the same `Chrome` id as a local file, and whether two audio tabs become one session or two (exit `3` if two sessions share `Chrome`).
- Store Media Player metadata/`Changing` on a cold launch without a file; UND-88's Playing/Paused path is the cleaner WMP data point.
- Spotify desktop SMTC id on this account (package present, process not started).
- Plugin players, if a future user insists on MusicBee/AIMP/foobar without installing their SMTC plugin: those sessions will simply not appear. That is a coverage gap for those users, not a reason to guess ids.
- Tauon title/artist showing a leading `?` through `TryGetMediaPropertiesAsync` — onboarding UI should tolerate missing or messy metadata and still key off the id.

## Implications (not work in this issue)

A go means UND-87 can keep SMTC as the onboarding/control path, with the existing invariant: commands only to a session whose `SourceAppUserModelId` matches the user's explicit selection exactly.

Observed constraints any later adapter should not "fix" by guessing:

1. Copy ids from `GetSessions()`, not from `Get-StartApps`, process names, or substrings.
2. Do not use `GetCurrentSession()` object identity.
3. Treat `IsPlayEnabled` / `IsPauseEnabled` as state at call time.
4. Empty session list is idle, not a host failure.
5. Tauon remains controllable over its HTTP API regardless of this spike; SMTC is an additional, explicit-session path, not a replacement of the current Tauon MVP.
