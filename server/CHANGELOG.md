# Changelog

## 1.0.0 - 2026-06-19

- Added unified `/player/?url=...` playback and danmaku handling for VRChat/YamaPlayer.
- Added Bilibili video direct-link redirects and `#YBDM/1` danmaku output.
- Added Bilibili live room direct-link redirects to playable HLS `.m3u8` streams.
- Added NetEase Cloud Music song, playlist, and short-link redirects through `music.znnu.com`.
- Added dashboard counters, cache diagnostics, and persistent stats under `/app/data/stats.json`.
- Removed legacy room state dependency; `/api/current` and `/api/set` now return `410 Gone`.
- Added Docker deployment with container port `3000` and host port `7858`.
