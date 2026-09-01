# Quick Launch

Start `GsiHost` for local iteration.

> **Current binary:** `--quick` sets `Music:Provider=Mock` (no Tauon, no CS2 auto-setup). Default non-quick provider is Tauon (`http://127.0.0.1:7814`). See [roadmap.md](roadmap.md) and [tauon-integration.md](tauon-integration.md).

## Fastest start

```powershell
dotnet run --project .\GsiHost -- --quick
```

`--quick` mode uses `MockMusicPlayer` and skips CS2 auto-setup.

## Faster startup

```powershell
dotnet run --project .\GsiHost -- --skip-cs2-setup
```

To use the in-process mock without `--quick` (CS2 auto-setup still runs), set `Music:Provider=Mock`. `--use-mock-spotify` and `--skip-smart-track-warmup` are removed; they are not accepted aliases.

## Runtime / MVP flags

| Flag | Use when |
| --- | --- |
| `--quick` | Mock music player (`Music:Provider=Mock`); skip CS2 setup |
| `--mvp` | Legacy observe+record: `intent_capture` + Timeline + PlaybackObserver ON in memory (no automation; does not mutate `appsettings.json`) |
| `--intent-capture` | Map `/timeline` and register `PlaybackStateObserver` |
| `--scenario-playback` | Force default end-user mode (GSI rules drive `IMusicPlayer`) |
| `--skip-cs2-setup` | Start without the automatic CS2 cfg install |

`--mvp` is the **legacy** UND-64 observe+record mode, not the Tauon product MVP. Timeline notes: [manual-intent-timeline.md](manual-intent-timeline.md). HTTP table: [backend-architecture.md](backend-architecture.md).

## Credentials

None. UND-84 deleted the OAuth layer, so the host reads no `CLIENT_ID` / `CLIENT_SECRET`, keeps no encrypted secret store, and never prompts for credentials. The leftover `Spotify` section still present in `appsettings.json` is only read back by `/config` and is not used for authentication. `UseMockSpotify` is no longer a config key; Mock is `--quick` or `Music:Provider=Mock`.

## Failure handling

CS2 auto-setup is best-effort. If reading CS2 setup status or control profiles fails during the startup checklist, the host keeps running and logs a warning instead of terminating.
