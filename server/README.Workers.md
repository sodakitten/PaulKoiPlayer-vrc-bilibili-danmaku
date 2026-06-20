# Cloudflare Workers Deployment

This is an experimental low-cost deployment path for the PaulKoiPlayer resolver server.

The Docker server remains the stable deployment target. The Worker version keeps the core resolver APIs and removes Docker-only behavior such as filesystem-backed persistent counters.

## What Works

- `GET /health`
- `GET /player/?url=...`
- Bilibili video redirects
- Bilibili live `.m3u8` redirects
- Bilibili `#YBDM/1` danmaku responses
- NetEase Cloud Music song, playlist, and `163cn.tv` redirects
- `GET /api/danmaku?url=...`
- `GET /api/resolve?url=...`
- `GET /api/cache/stats`

## Cache Model

Workers do not provide one global process-level `Map` shared by every request worldwide.

This Worker uses:

- Cloudflare Cache API for cross-request cached text and JSON results.
- In-isolate memory cache as a local fallback.

There is no global inflight de-duplication yet. If many uncached requests for the same video arrive at the same instant in different edge isolates, they may still hit upstream APIs separately. Add Durable Objects later if global singleflight becomes necessary.

## Local Test

```bash
npm run check
npm run worker:dev
```

Then test:

```bash
curl http://127.0.0.1:8787/health
curl -I "http://127.0.0.1:8787/player/?url=https%3A%2F%2Fwww.bilibili.com%2Fvideo%2FBV1nQSfB4Ed2%2F"
curl -H "Accept: text/plain" "http://127.0.0.1:8787/player/?url=https%3A%2F%2Fwww.bilibili.com%2Fvideo%2FBV1nQSfB4Ed2%2F"
```

## Deploy

Log in to Cloudflare:

```bash
npx wrangler login
```

Deploy:

```bash
npm run worker:deploy
```

After deployment, set the Unity URL prefix to:

```text
https://<your-worker-domain>/player/?url=
```

## Notes

Cloudflare Workers can handle concurrent requests, but each request has CPU limits. Large danmaku payloads should be cached aggressively. If a specific video is too heavy on the free plan, use the Docker server or upgrade the Worker plan.
