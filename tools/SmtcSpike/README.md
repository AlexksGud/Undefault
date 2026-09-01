# SmtcSpike

Throwaway evidence harness for [UND-88](https://linear.app/undefault/issue/UND-88). It answers one
question: can Windows System Media Transport Controls (SMTC) enumerate the media apps our users
actually run, and pause/resume a chosen one by its exact app id?

This is not a product feature. It is not wired into `GsiHost`, it registers nothing with dependency
injection, and it must not be treated as a music provider. The current player backend is still Tauon
over local HTTP.

## Build and run

```powershell
dotnet publish tools/SmtcSpike -c Release -r win-x64 --self-contained false
.\tools\SmtcSpike\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\SmtcSpike.exe list
```

Run the published exe rather than `dotnet run`, so the harness is exercised the same way a shipped
unpackaged desktop process would be.

## Commands

| Command | Effect |
| --- | --- |
| `list` | Enumerates every SMTC session and prints its id, playback status, title/artist, and dynamic controls. |
| `pause <SourceAppUserModelId>` | Calls `TryPauseAsync()` on the one session with that exact id. |
| `resume <SourceAppUserModelId>` | Calls `TryPlayAsync()` on the one session with that exact id. |

Pass the app id exactly as `list` printed it, without the surrounding quotes. Ids contain `!` and
`_`, so quote the argument in PowerShell if it would otherwise be interpreted.

## The matching rule

`pause` and `resume` act only on a session whose `SourceAppUserModelId` is ordinally equal to the
argument. There is no substring match, no case-insensitive match, and no nearest-match fallback. If
nothing matches, the harness prints `no matching session` and exits non-zero without touching any
other session. If two sessions report the same id, it refuses rather than picking one.

The reason is asymmetric cost: pausing the intended player is a small win, while pausing a call, a
video, or an unrelated browser tab is the kind of failure a user does not forgive. Any future
product code built on this evidence should keep the same rule.

## Exit codes

| Code | Meaning |
| --- | --- |
| 0 | Command succeeded. For `pause`/`resume`, SMTC also returned `true`. |
| 1 | Usage error. |
| 2 | No session matched the given id exactly. |
| 3 | More than one session reports that id, so it cannot address a single session. |
| 4 | The session was found but `TryPauseAsync()`/`TryPlayAsync()` returned `false`. |
| 5 | Unexpected error. |

## Reading the output

- `IsPlayEnabled` / `IsPauseEnabled` are the app's currently advertised controls, not static
  capabilities. A playing session usually reports `IsPlayEnabled: False`, so a capability check has
  to be made against the state at the time of the call.
- `PlaybackStatus after` is best effort. SMTC applies a command asynchronously, and the harness only
  waits briefly before re-reading.
- `IsCurrentSession` is decided by exact id equality. On the machine used for the first run,
  `GetCurrentSession()` returned an object that was not reference-equal to the matching entry from
  `GetSessions()`, so reference identity is not a usable comparison here.
- Title and artist come from `TryGetMediaPropertiesAsync()`, which some sessions refuse. Failures are
  printed inline as `<unreadable: ...>` rather than being swallowed.

## Known gaps

Volume, next/previous, retries, and session-change subscriptions are deliberately absent. So is any
evidence about apps that were not running during a given run: an app that registers no SMTC session
simply does not appear, and this harness cannot distinguish "not controllable" from "not running".
