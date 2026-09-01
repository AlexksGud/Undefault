# Product pivot — 2026-08-14

Locked product-owner decision. `PIVOT-1`–`PIVOT-8` are implemented in-repo. This document remains the source of truth for product direction. Older MVP notes (UND-64 `intent_capture`, Spotify-required) are historical.

## What Undefault is

Undefault is a **game-aware music automation layer**. It reads game state, detects events, and tells the user's existing music player what to do.

Undefault owns:

- game integrations
- event detection
- rules
- actions
- playback orchestration

Undefault does **not** own the music catalog.

## What Undefault is not

- a Spotify controller
- a synchronized game soundtrack
- a music player
- a recommendation or playlist product

## First playback backend

**Tauon Music Box**, via its remote HTTP API. Undefault and Tauon stay independent projects. Do not fork or patch Tauon.

Spotify is **dropped**. Playback control via the Web API is a Streaming SDA; game-adjacent use, synchronization with visuals, and commercial Streaming SDAs are restricted. Development Mode is a 5-user tinkering sandbox, not a ship path. See [spotify-constraints.md](spotify-constraints.md). Leftover Spotify code is deletion-only (UND-84). Do not wrap it as a user-facing provider or file new Spotify work.

## Approved MVP loop

```text
CS2 round_start → resume music
CS2 death       → pause music
```

That replaces the previous default `round_start → duck` / `death → restore_volume`.

Not in this MVP: `round_end`, victory/defeat tracks, playlist/queue/track-id playback, live safety mixer, Dota automation, UI, packaging.

## Current code vs this decision

`PIVOT-1`–`PIVOT-8` are in-repo. Live automation is `music.control_profile` with `round_start → resume` / `death → pause` through `IMusicPlayer` (Tauon default, Mock for `--quick`). Leftover Spotify types remain until excision (`PIVOT-10` / UND-84). Live Tauon smoke is `PIVOT-9` / UND-83.

## Linear (2026-09-01)

Linear workspace **Undefault** is connected. Source of truth: project [undefault](https://linear.app/undefault/project/undefault-2856eb466d02), decision [MVP decision — 2026-09-01](https://linear.app/undefault/document/mvp-decision-2026-09-01-cs2-tauon-spotify-dropped-ae332adffdfc).

Current MVP umbrella: [UND-82](https://linear.app/undefault/issue/UND-82/mvp-cs2-player-control-rulesevents-tauon). Old intent_capture/Spotify MVP ([UND-64](https://linear.app/undefault/issue/UND-64/mvp-minimal-playable-slice-connect-hotkeys-record-pauseresume-intent)) is canceled.

Optional later (not this MVP): a seam for an external, user-operated music import companion ([UND-86](https://linear.app/undefault/issue/UND-86/design-optional-external-music-import-companion)). Design only until approved. Undefault still does not own the catalog.
