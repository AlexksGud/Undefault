# Spotify constraints (agent)

Researched 2026-08-14 against current Spotify developer docs. **Spotify is dropped** as a product backend (confirmed 2026-09-01). Leftover `Core/Spotify` / OAuth code is deletion-only (UND-84). Do not add Spotify features, adapters, OAuth, apps, or docs. Do not file Linear work that revives Spotify.

Primary player: Tauon. See [product-pivot-2026-08-14.md](product-pivot-2026-08-14.md).

## Why (policy)

Undefault calling `PUT /v1/me/player/play|pause|volume` (or next/previous) is a **Streaming SDA**.

| Rule | Source | Effect on Undefault |
|---|---|---|
| Do not create a game | [Policy III.2](https://developer.spotify.com/policy); [Compliance Tips](https://developer.spotify.com/compliance-tips) | CS2 companion that drives Spotify is game-adjacent. Do not market or build “Spotify in/for the game.” |
| Do not synchronize recordings with visual media | Policy III.6 | CS2 is visual media. No beat/drop/seek aligned to round events. Same rule applies to **any** player ([playback policy](spotify-playback-policy-boundary.md)). |
| Streaming SDAs may not be commercialized | Policy IV; Compliance Tips example: paid home-automation that triggers music | Cannot sell, ads-on, or ship as a commercial Spotify controller. |
| Streaming = controlling a background Spotify app | [Terms](https://developer.spotify.com/terms) def. 14–15 | Pause/resume/volume of the user’s Spotify app still counts as Streaming. |

Player endpoints still exist on the Web API. Policy, not missing endpoints, is the blocker.

## Why (platform access, Feb 2026)

[Blog 2026-02-06](https://developer.spotify.com/blog/2026-02-06-update-on-developer-access-and-platform-security) · [Migration guide](https://developer.spotify.com/documentation/web-api/tutorials/february-2026-migration-guide)

Development Mode (default for a new/hobby app):

- App owner needs Spotify Premium; app dies if Premium lapses
- New apps: 5 authorized users
- Intended for personal tinkering, not a business foundation
- Extended Quota Mode is a separate review; not guaranteed

Do not plan testers, packaging, or a public Spotify provider on Dev Mode.

## Agent rules

- Do not implement `SpotifyMusicPlayer` as a user-facing provider.
- Do not expand OAuth, quotas, Smart Track Start, or `spotify.profile`.
- OAuth/real-client code is gone (UND-84). Remaining mock observe leftovers are UND-101. Do not keep a compatibility adapter.
- If touching leftover Spotify files before deletion: no token/secret logging; no new scopes; no new player endpoints.
- Game→music automation uses Tauon/mock only.

## Sources

- https://developer.spotify.com/policy
- https://developer.spotify.com/terms (v10, 15 May 2025)
- https://developer.spotify.com/compliance-tips
- https://developer.spotify.com/blog/2026-02-06-update-on-developer-access-and-platform-security
- https://developer.spotify.com/documentation/web-api/tutorials/february-2026-migration-guide
