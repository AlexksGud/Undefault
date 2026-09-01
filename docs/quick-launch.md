# Quick Launch

Start `GsiHost` for local iteration.

> **Leftover Spotify.** `--use-mock-spotify` and `/spotify/status` are leftover until `PIVOT-11`. They are not the product launch path. UND-84 deleted the OAuth layer, so there is no longer a real-Spotify launch mode, credential prompt, or secret store.
>
> **Current binary:** `--quick` sets `Music:Provider=Mock` (no Tauon, no CS2 auto-setup). Default non-quick provider is Tauon (`http://127.0.0.1:7814`). Leftover Spotify is always the mock client. See [roadmap.md](roadmap.md) and [tauon-integration.md](tauon-integration.md).

## Fastest start

```powershell
dotnet run --project .\GsiHost -- --quick
```

`--quick` mode uses `MockMusicPlayer`, skips CS2 auto-setup and Smart Track warmup, and keeps leftover Spotify on the mock client.

## Faster startup

```powershell
dotnet run --project .\GsiHost -- --skip-cs2-setup
dotnet run --project .\GsiHost -- --skip-smart-track-warmup
```

## Spotify mode overrides

- `--use-mock-spotify` forces `Music:Provider=Mock`.

## Runtime / MVP flags

| Flag | Use when |
| --- | --- |
| `--quick` | Mock music player (`Music:Provider=Mock`); skip CS2 setup and Smart Track warmup |
| `--mvp` | Legacy observe+record: `intent_capture` + Timeline + PlaybackObserver ON in memory (no automation; does not mutate `appsettings.json`) |
| `--intent-capture` | Map `/timeline` and register `PlaybackStateObserver` |
| `--scenario-playback` | Force default end-user mode (GSI rules drive `IMusicPlayer`, not Spotify) |
| `--skip-cs2-setup` | Start without the automatic CS2 cfg install |
| `--skip-smart-track-warmup` | Faster startup without Smart Track preload |

`--mvp` is the **legacy** UND-64 observe+record mode, not the Tauon product MVP. Timeline notes: [manual-intent-timeline.md](manual-intent-timeline.md). HTTP table: [backend-architecture.md](backend-architecture.md).

## Spotify credentials

None. UND-84 deleted the OAuth layer, so the host reads no `CLIENT_ID` / `CLIENT_SECRET`, keeps no encrypted secret store, and never prompts for credentials. The `Spotify` section still present in `appsettings.json` is only read back by `/config` and is not used for authentication.

## Failure handling

CS2 auto-setup and Smart Track warmup are best-effort. If reading CS2 setup status or control profiles fails during the startup checklist, the host keeps running and logs a warning instead of terminating.
