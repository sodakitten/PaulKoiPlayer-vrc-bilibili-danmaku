# Changelog

## 1.0.3 - 2026-06-25

- Added persistent Bilibili danmaku disk cache under `/app/data/danmaku-cache`.
- Changed new danmaku cache entries to expire after 1 day by default, with non-stacking 1-day refresh on cache hits.
- Added bounded cache limits and dashboard display limits to avoid unbounded cache growth.
- Updated Docker image tag to `paulkoi-danmaku-server:1.0.3`.

## 1.0.0 - 2026-06-19

- Added unified `/player/?url=...` playback and danmaku handling for VRChat video players.
- Added Bilibili video direct-link redirects and `#YBDM/1` danmaku output.
- Added Bilibili live room direct-link redirects to playable HLS `.m3u8` streams.
- Added NetEase Cloud Music song, playlist, and short-link redirects through `music.znnu.com`.
- Added dashboard counters, cache diagnostics, and persistent stats under `/app/data/stats.json`.
- Removed legacy room state dependency; `/api/current` and `/api/set` now return `410 Gone`.
- Added Docker deployment with container port `3000` and host port `7858`.
