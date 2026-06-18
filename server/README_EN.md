# VRChat Danmaku Resolver Server

[简体中文](README.md) | [English](README_EN.md) | [Project home](../README_EN.md)

This Docker service accompanies the Unity danmaku component. A VRChat world only needs one endpoint:

```text
https://your-domain.example/player/?url=<video or music URL>
```

For normal video requests, the server returns a `302` redirect to a playable media URL. For `VRCStringDownloader` requests, the same endpoint returns `#YBDM/1` text. No room name, world name, or instance ID is required, so different worlds and videos can be processed concurrently.

## Supported inputs

- Bilibili video URLs, BV IDs, and av IDs
- Bilibili MP4 media URL resolution
- XML and segmented protobuf danmaku exported as `#YBDM/1`
- NetEase Cloud Music songs, playlists, and `163cn.tv` short links
- Playlist track selection with `p` or `page`
- In-memory caches for metadata, media URLs, danmaku, and playlists
- In-flight request deduplication for concurrent requests to the same resource
- No database dependency

## Quick deployment

Node.js does not need to be installed on the host. Node.js 20 is included in the Docker image.

```bash
docker compose up -d --build
```

Default port mapping:

```text
host 7858 -> container 3000
```

Check the service:

```bash
docker compose ps
curl http://127.0.0.1:7858/health
```

Dashboard:

```text
http://127.0.0.1:7858/
```

Cache statistics:

```text
http://127.0.0.1:7858/api/cache/stats
```

Update the deployment:

```bash
git pull
docker compose up -d --build
```

## Usage

### Bilibili

```text
https://your-domain.example/player/?url=BV1PV7m6DE51
https://your-domain.example/player/?url=https://www.bilibili.com/video/BV1PV7m6DE51
```

Force a danmaku response for diagnostics:

```text
https://your-domain.example/player/?__dm=1&url=BV1PV7m6DE51
https://your-domain.example/api/danmaku?url=BV1PV7m6DE51
```

Normal playback requests return `302 Location`. Danmaku requests return:

```text
#YBDM/1
#columns=progressMs	mode	color	fontSize	pool	content
```

Danmaku requests are detected through `__dm=1`, text-oriented request headers, and VRChat string downloader User-Agent patterns.

### NetEase Cloud Music

```text
https://your-domain.example/player/?url=music.163.com/song?id=346075
https://your-domain.example/player/?url=music.163.com/playlist?id=487424073
https://your-domain.example/player/?url=music.163.com/playlist?id=487424073&p=2
https://your-domain.example/player/?url=163cn.tv/xxxx
```

Playlists default to `p=1`. NetEase web URLs commonly use `#/song` and `#/playlist` fragments. Fragments are not sent to servers, so use the non-hash form or fully encode the nested URL.

## Endpoints

| Path | Purpose |
| --- | --- |
| `GET /player/?url=...` | Unified playback and danmaku endpoint |
| `GET /player/?__dm=1&url=...` | Force a danmaku response |
| `GET /api/danmaku?url=...` | Fetch danmaku directly |
| `GET /api/resolve?url=...` | Return resolver diagnostics |
| `GET /api/cache/stats` | Return cache and hit statistics |
| `GET /health` | Docker health check |

Legacy `/api/current` and `/api/set` endpoints have been removed and return `410 Gone`. Do not use `room=main`.

## Environment variables

| Variable | Default | Description |
| --- | ---: | --- |
| `PORT` | `3000` | HTTP port inside the container |
| `VIEW_CACHE_TTL_SECONDS` | `1800` | Bilibili video metadata cache TTL |
| `VIDEO_URL_CACHE_TTL_SECONDS` | `600` | Bilibili media URL cache TTL |
| `DANMAKU_CACHE_TTL_SECONDS` | `21600` | Bilibili danmaku cache TTL |
| `NETEASE_PLAYLIST_CACHE_TTL_SECONDS` | `1800` | NetEase playlist cache TTL |
| `NETEASE_URL_CACHE_TTL_SECONDS` | `600` | NetEase media URL cache TTL |
| `NETEASE_LEVEL` | `standard` | Default NetEase quality level |
| `ZNNU_BASE_URL` | `https://music.znnu.com` | NetEase resolver backend |
| `ZNNU_FETCH_TIMEOUT_SECONDS` | `30` | NetEase API timeout in seconds |
| `BILI_COOKIE` / `BILIBILI_COOKIE` | empty | Optional Bilibili cookie string |
| `BILI_USER_AGENT` | browser-like UA | Optional upstream User-Agent |

Never commit a real cookie in `docker-compose.yml`. Store it in a server-local `.env` file and restrict that file's permissions.

## Nginx reverse proxy

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

Enable HTTPS for the domain afterward. With Cloudflare, point DNS to the server and ensure that the origin certificate and Cloudflare SSL mode agree.

## Caching and concurrency

Cache keys include the video, part, quality, and other resolver parameters. There is no global `room` state. Different worlds playing different videos do not overwrite each other, while concurrent requests for the same resource share the same in-flight upstream request and then use the memory cache.

The cache is held in memory and is cleared when the container restarts. This does not affect correctness; the first request simply fetches the resource again.

## Local development

To run without Docker:

```bash
npm run check
npm start
```

Node.js 20 or newer is required.

## Disclaimer

This service does not store or host Bilibili or NetEase media. It only parses URLs, requests metadata, converts danmaku, and generates redirects. Changes to third-party APIs, CDNs, regional restrictions, or access policies may affect results. Follow the applicable service terms and local laws.
