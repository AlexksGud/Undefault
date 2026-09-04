# Tauon integration

Approved target for the first music backend. **`TauonMusicPlayer` is in `GsiHost/Players`.** Default provider is Tauon (`Music:Provider=Tauon`). Leftover Spotify types remain until `PIVOT-10`. Live process smoke is `PIVOT-9` in [roadmap.md](roadmap.md).

Tauon Music Box is a desktop player for the user's own library. Undefault controls it over HTTP. The two projects stay independent: no fork, no Tauon source changes, no outreach to the Tauon author unless a verified API gap blocks a scoped feature.

Docs: [tauonmusicbox.rocks](https://tauonmusicbox.rocks/) · API: [Remote Control API wiki](https://github.com/Taiko2k/Tauon/wiki/Remote-Control-API) · source: [`t_webserve.py`](https://github.com/Taiko2k/Tauon/blob/master/src/tauon/t_modules/t_webserve.py)

## Security

Tauon's remote API has **no authentication**, uses **GET only**, and the Tauon process binds **`0.0.0.0:7814`**. Tauon itself warns that the API is not security hardened.

- Enable remote control only on a trusted LAN.
- Do not expose port 7814 to the public internet.
- Undefault should call `http://127.0.0.1:7814` by default.

## Enable remote control in Tauon

1. Install Tauon.
2. Open Settings → **Remote, presence and sharing** (some docs still say Connections or Function).
3. Enable **Enable remote control**. Restart Tauon.
4. Confirm `http://127.0.0.1:7814/api1/status` returns JSON.

If remote is off, `/api1/*` typically returns 404. Treat that as **unavailable**, not a host crash.

## Verified endpoints (do not invent others)

| Action | Request |
|---|---|
| Play / resume | `GET /api1/play` |
| Pause | `GET /api1/pause` |
| Next | `GET /api1/next` |
| Previous | `GET /api1/back` |
| Volume | `GET /api1/setvolume/{0-100}` |
| State + track | `GET /api1/status` |

There is **no** `/resume`. Resume = `/play` while paused or stopped. Skip `/play` when status is already `playing`.

Status strings: `playing` → Playing, `paused` → Paused, `stopped` → Stopped, anything else → Unknown.

Wiki last edited 2022; source also has `GET /api1/stop`. MVP does not need stop.

## Not in the adapter (API gaps or out of MVP)

| Topic | Fact |
|---|---|
| Queue | No HTTP queue API |
| Play by track id | Only `GET /api1/start/{playlist_id}/{position}` (playlist index) |
| Auth | None |
| Port | Hardcoded 7814 in Tauon |
| API stability | Wiki: not stable, subject to change |

## Target Undefault config

```json
{
  "Music": { "Provider": "Tauon" },
  "Tauon": {
    "BaseUrl": "http://127.0.0.1:7814",
    "TimeoutSeconds": 2
  }
}
```

## Smoke (`PIVOT-9`)

This is the manual live-Tauon procedure. It has not been marked done by this change.

1. Start Tauon with remote control enabled.
2. Start Undefault with `Music:Provider=Tauon` (default). **Do not use `--intent-capture`** (it does not run `music.control_profile`). `--mvp` is rejected at startup.
3. Run `dotnet run --project .\Cs2Simulator`.
4. `round_start` resumes Tauon; `death` pauses Tauon. Watch `Playback pause/resume` and `Tauon … failed` logs. Do not treat leftover `/status` Spotify fields as Tauon proof.
5. Repeat with Tauon **not** running: host stays up, music actions fail in logs, GSI still works.
6. Keep the host up **longer than 2 minutes**. The previous named-`HttpClient` lifetime bug showed up on long runs; after the factory-per-request fix this is still a useful soak.
7. If an existing `control-profiles.json` uses duck/restore, it is not auto-migrated; product default is `round_start=resume` / `death=pause`.

`--quick` selects `Music:Provider=Mock`.
