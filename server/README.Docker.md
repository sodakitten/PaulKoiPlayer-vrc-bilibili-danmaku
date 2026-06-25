# Docker Deployment

Version: `1.0.3`

Build and run:

```bash
docker compose up -d --build
```

Health check:

```bash
curl http://127.0.0.1:7858/health
```

Dashboard:

```text
http://127.0.0.1:7858/
```

Cloudflare Workers experimental entry:

```bash
npm run worker:dev
npm run worker:deploy
```

Workers use `src/worker.js` and `wrangler.toml`. This mode keeps the core resolver APIs, but does not include the full Docker dashboard or persistent counters.

Playback examples:

```text
https://your-domain.example/player/?url=BV1PV7m6DE51
https://your-domain.example/player/?url=https://live.bilibili.com/1741183250
https://your-domain.example/player/?url=music.163.com/song?id=346075
https://your-domain.example/player/?url=music.163.com/playlist?id=487424073
```

Danmaku examples:

```text
https://your-domain.example/player/?__dm=1&url=BV1PV7m6DE51
https://your-domain.example/api/danmaku?url=BV1PV7m6DE51
```

Persistent runtime data:

```yaml
volumes:
  - ./data:/app/data
```

The dashboard counters are saved in `./data/stats.json`.

Optional environment variables:

- `PORT`: default `3000`
- `TZ`: default `Asia/Shanghai`
- `DISPLAY_TIME_ZONE`: default `Asia/Shanghai`
- `VIEW_CACHE_TTL_SECONDS`: default `1800`
- `VIDEO_URL_CACHE_TTL_SECONDS`: default `600`
- `DANMAKU_CACHE_TTL_SECONDS`: default `21600`
- `NETEASE_PLAYLIST_CACHE_TTL_SECONDS`: default `1800`
- `NETEASE_URL_CACHE_TTL_SECONDS`: default `600`
- `NETEASE_LEVEL`: default `standard`
- `STATS_FILE`: default `/app/data/stats.json`
- `STATS_SAVE_INTERVAL_SECONDS`: default `30`
- `BILI_COOKIE` / `BILIBILI_COOKIE`: optional Bilibili cookie string
- `BILI_USER_AGENT`: optional User-Agent override

Nginx reverse proxy example:

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
