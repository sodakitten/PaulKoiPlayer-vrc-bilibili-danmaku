# Yama Bili Danmaku Proxy

Version: `1.0.0`

A small Dockerized Node.js proxy for VRChat/YamaPlayer style playback URLs.

It accepts a `/player/?url=...` request, resolves supported Bilibili and NetEase Cloud Music links, and returns a direct `302` redirect to a playable media URL. For Bilibili danmaku requests, it returns a `#YBDM/1` TSV payload.

## Features

- Bilibili video URL, BV ID, and av ID parsing.
- Bilibili MP4 direct-link redirect for video players.
- Bilibili live room direct-link redirect to playable HLS `.m3u8` streams.
- Bilibili danmaku export as `#YBDM/1`.
- NetEase Cloud Music song and playlist parsing through `music.znnu.com`.
- NetEase playlist page selection with `p` or `page`.
- NetEase short-link support through `163cn.tv`.
- In-memory cache for Bilibili view/playurl/danmaku data and NetEase playlist/song URLs.
- Persistent dashboard counters under `/app/data/stats.json`, including Bilibili video redirects, Bilibili live redirects, NetEase redirects, danmaku requests, cache hits, and emitted danmaku rows.
- No database required.

## Quick Start

```bash
docker compose up -d --build
```

Health check:

```bash
curl http://127.0.0.1:7858/health
```

Open the dashboard:

```text
http://127.0.0.1:7858/
```

## Cloudflare Workers Experimental

This repository also includes a lightweight Cloudflare Workers entry at `src/worker.js`.

Run a syntax check:

```bash
npm run check
```

Start a local Wrangler dev server:

```bash
npm run worker:dev
```

Deploy with Wrangler after logging in to Cloudflare:

```bash
npm run worker:deploy
```

The Worker version keeps the core endpoints:

- `GET /health`
- `GET /player/?url=...`
- `GET /api/danmaku?url=...`
- `GET /api/resolve?url=...`
- `GET /api/cache/stats`

It uses the Cloudflare Cache API with an in-isolate memory fallback. Persistent dashboard counters and the full Docker dashboard are intentionally not included in the Worker version. The Docker service remains the stable deployment path.

## URL Examples

Bilibili video:

```text
https://your-domain.example/player/?url=BV1PV7m6DE51
https://your-domain.example/player/?url=https://www.bilibili.com/video/BV1PV7m6DE51
```

Bilibili live:

```text
https://your-domain.example/player/?url=https://live.bilibili.com/1741183250
```

Bilibili danmaku:

```text
https://your-domain.example/player/?__dm=1&url=BV1PV7m6DE51
https://your-domain.example/api/danmaku?url=BV1PV7m6DE51
```

NetEase Cloud Music song:

```text
https://your-domain.example/player/?url=music.163.com/song?id=346075
```

NetEase Cloud Music playlist:

```text
https://your-domain.example/player/?url=music.163.com/playlist?id=487424073
https://your-domain.example/player/?url=music.163.com/f/playlist?id=487424073
https://your-domain.example/player/?url=y.music.163.com/m/playlist?id=487424073
```

NetEase short link:

```text
https://your-domain.example/player/?url=163cn.tv/xxxx
```

Select a playlist track:

```text
https://your-domain.example/player/?url=music.163.com/playlist?id=487424073&p=2
```

The default playlist track is `p=1`.

## NetEase Hash URLs

NetEase web pages often use hash routes, for example:

```text
https://music.163.com/#/song?id=346075
https://music.163.com/#/playlist?id=487424073
```

The `#...` part is not sent to servers by browsers or HTTP clients. Do not pass raw hash URLs directly as the `url` parameter:

```text
https://your-domain.example/player/?url=https://music.163.com/#/song?id=346075
```

Use the non-hash form instead:

```text
https://your-domain.example/player/?url=music.163.com/song?id=346075
https://your-domain.example/player/?url=music.163.com/playlist?id=487424073
```

Or URL-encode the inner URL so `#` becomes `%23`.

## Environment Variables

| Variable | Default | Description |
| --- | --- | --- |
| `PORT` | `3000` | HTTP port inside the container. |
| `TZ` | `Asia/Shanghai` | Container timezone used by runtime libraries. |
| `DISPLAY_TIME_ZONE` | `Asia/Shanghai` | Dashboard display timezone for dates such as service start time. |
| `VIEW_CACHE_TTL_SECONDS` | `1800` | Bilibili video view cache TTL. |
| `VIDEO_URL_CACHE_TTL_SECONDS` | `600` | Bilibili direct video URL cache TTL. |
| `DANMAKU_CACHE_TTL_SECONDS` | `21600` | Bilibili danmaku cache TTL. |
| `NETEASE_PLAYLIST_CACHE_TTL_SECONDS` | `1800` | NetEase playlist metadata cache TTL. |
| `NETEASE_URL_CACHE_TTL_SECONDS` | `600` | NetEase song direct URL cache TTL. |
| `NETEASE_LEVEL` | `standard` | Default NetEase quality level. Supported values include `standard`, `exhigh`, `lossless`, `hires`, `sky`, `jyeffect`, `jymaster`. |
| `ZNNU_BASE_URL` | `https://music.znnu.com` | NetEase parsing backend. |
| `ZNNU_FETCH_TIMEOUT_SECONDS` | `30` | Timeout for ZNNU API requests. |
| `STATS_FILE` | `/app/data/stats.json` | Persistent dashboard counter file. |
| `STATS_SAVE_INTERVAL_SECONDS` | `30` | How often changed counters are flushed to disk. |
| `BILI_COOKIE` / `BILIBILI_COOKIE` | empty | Optional Bilibili cookie string. Useful when anonymous access returns incomplete data. |
| `BILI_USER_AGENT` | browser-like UA | Optional User-Agent override for Bilibili and ZNNU requests. |

## Persistent Counters

The dashboard counters are saved to `STATS_FILE`. The included Compose file mounts:

```yaml
volumes:
  - ./data:/app/data
```

This keeps cumulative counters across container rebuilds and restarts. Runtime data in `data/` is ignored by Git.

## External Services Used

This project does not host Bilibili or NetEase media itself. It resolves metadata and redirects clients to third-party media URLs.

The service depends on these external services:

| Service | Used For | Notes |
| --- | --- | --- |
| `api.bilibili.com` | Bilibili video metadata, MP4 play URLs, XML danmaku, protobuf danmaku segments. | Endpoints include `/x/web-interface/view`, `/x/player/playurl`, `/x/v1/dm/list.so`, `/x/v2/dm/web/seg.so`. |
| `api.live.bilibili.com` | Bilibili live room metadata and live stream URLs. | Endpoints include `/room/v1/Room/room_init` and `/xlive/web-room/v2/index/getRoomPlayInfo`. |
| `www.bilibili.com` | Bilibili referer header. | Sent when requesting Bilibili APIs. |
| `b23.tv` | Bilibili short-link expansion. | Expanded with manual redirect handling. |
| Bilibili CDN domains, such as `*.bilivideo.com` | Final Bilibili video and live playback URLs. | `/player/` redirects clients to these URLs. |
| `music.znnu.com` | NetEase song, playlist, and short-link parsing. | Uses `/api/key`, `/api/ip`, `/api/redirect`, `/api/playlist`, and `/api/song`. |
| `music.163.com` and `y.music.163.com` | Accepted input URL domains and NetEase referer context. | Raw hash routes must be converted or URL-encoded before being nested in `/player/?url=`. |
| `163cn.tv` | Accepted NetEase short-link input. | Resolved through `music.znnu.com` before parsing. |
| NetEase CDN domains, such as `*.music.126.net` | Final NetEase playback URLs. | `/player/` redirects clients to these URLs. |
| `docker.m.daocloud.io/library/node:20-alpine` | Docker base image. | Change the `FROM` line in `Dockerfile` if you prefer Docker Hub or another registry mirror. |

Because these are third-party services, availability, response format, rate limits, geo restrictions, and media playback behavior may change outside this project's control.

## API Notes

`/player/?url=...` has Bilibili priority. If an input can be parsed as Bilibili, it follows the Bilibili path first. NetEase parsing is only attempted when no Bilibili BV/av ID is found.

Normal playback requests return `302 Location: ...`.

Bilibili live room URLs always return a `302` live stream redirect before danmaku detection, because live playback does not use the Bilibili VOD danmaku TSV path.

Danmaku requests are detected by `__dm=1`, text-like request headers, or VRC string downloader style user agents, and return plain text:

```text
#YBDM/1
#columns=progressMs	mode	color	fontSize	pool	content
```

Legacy room endpoints such as `/api/current` and `/api/set` return `410 Gone`.

## Reverse Proxy Example

```nginx
server {
  server_name danmaku.example.com;

  location / {
    proxy_pass http://127.0.0.1:7858;
    proxy_set_header Host $host;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto $scheme;
  }
}
```

Then enable HTTPS with your preferred certificate tool.

## Development

Run a syntax check:

```bash
npm run check
```

Run locally:

```bash
npm start
```

The app requires Node.js 20 or newer.

## Disclaimer

Use this project responsibly and follow the terms and policies of the services you access through it. This project only performs URL parsing, metadata lookup, danmaku formatting, and redirect generation.
