# Release / smoke checklist

The product MVP after [2026-08-14](product-pivot-2026-08-14.md) is Tauon automation, not Spotify observe+record.

**This checklist is `PIVOT-9` (manual live Tauon smoke).** Automated coverage for `PIVOT-1`–`PIVOT-8` is in-repo (`dotnet test`). The old UND-64 checklist is in [archive/release-checklist-intent-capture-mvp.md](archive/release-checklist-intent-capture-mvp.md).

## Target prerequisites

- Windows 10/11 x64, .NET 8 SDK.
- Tauon installed, **Enable remote control** on, restarted.
- `GET http://127.0.0.1:7814/api1/status` returns JSON.
- CS2 or `Cs2Simulator`.

## Target smoke (after PIVOT-1–8)

Launch **without `--intent-capture`**. `--mvp` is rejected at startup. `--intent-capture` is leftover observe+record and does not run `music.control_profile`.

Existing `control-profiles.json` with duck/restore is **not** auto-migrated; the host warns if the active profile is not `round_start=resume` / `death=pause`.

Keep the host running **longer than 2 minutes** during smoke. The previous named-`HttpClient` lifetime bug showed up on long runs; after the factory-per-request fix this is still a useful soak.

### Tauon running

- [ ] Host starts with `Music:Provider=Tauon` (default), no `--intent-capture`.
- [ ] `GET http://127.0.0.1:5292/status` → 200. Use `musicProvider` / `musicPlayerAvailable` / `playbackState`. Do not treat leftover Spotify fields as Tauon proof.
- [ ] Simulator (or CS2) emits `round_start` → Tauon resumes (`/api1/play` if paused/stopped). Watch `Playback resume` and Tauon logs, not leftover Spotify.
- [ ] `death` → Tauon pauses (`/api1/pause`). Watch `Playback pause`.
- [ ] Repeat `death` while already paused → no extra pause storm (idempotent).
- [ ] If Tauon is down, expect `Tauon … failed` (or status-failure) logs; GSI still returns.

### Tauon not running

- [ ] Host starts.
- [ ] GSI / simulator still processed.
- [ ] Music actions log failure; process does not crash.

### Mock

- [ ] `--quick` or `Music:Provider=Mock` runs the same event→action flow without Tauon.

Do not treat `--intent-capture` (observe) as the product demo. `--mvp` is rejected at startup.
