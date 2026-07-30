import http from "node:http";
import { createDecipheriv, createHmac } from "node:crypto";
import { existsSync, mkdirSync, readdirSync, readFileSync, renameSync, unlinkSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { URL } from "node:url";

const PORT = intEnv("PORT", 3000);
const VIEW_CACHE_TTL_MS = intEnv("VIEW_CACHE_TTL_SECONDS", 1800) * 1000;
const VIDEO_URL_CACHE_TTL_MS = intEnv("VIDEO_URL_CACHE_TTL_SECONDS", 600) * 1000;
const DANMAKU_CACHE_TTL_MS = intEnv("DANMAKU_CACHE_TTL_SECONDS", 21600) * 1000;
const VIEW_CACHE_MAX_ENTRIES = intEnv("VIEW_CACHE_MAX_ENTRIES", 500);
const VIDEO_URL_CACHE_MAX_ENTRIES = intEnv("VIDEO_URL_CACHE_MAX_ENTRIES", 500);
const DANMAKU_CACHE_MAX_ENTRIES = intEnv("DANMAKU_CACHE_MAX_ENTRIES", 50);
const DASHBOARD_DANMAKU_MAX_ENTRIES = intEnv("DASHBOARD_DANMAKU_MAX_ENTRIES", 20);
const DANMAKU_DISK_CACHE_DIR = process.env.DANMAKU_DISK_CACHE_DIR || "/app/data/danmaku-cache";
const DANMAKU_DISK_CACHE_INITIAL_TTL_MS = intEnv("DANMAKU_DISK_CACHE_INITIAL_TTL_SECONDS", 86400) * 1000;
const DANMAKU_DISK_CACHE_REFRESH_MS = intEnv("DANMAKU_DISK_CACHE_REFRESH_SECONDS", 86400) * 1000;
const USER_AGENT = process.env.BILI_USER_AGENT ||
  "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/126 Safari/537.36";
const BILIBILI_COOKIE = process.env.BILIBILI_COOKIE || process.env.BILI_COOKIE || "";
const ZNNU_BASE_URL = process.env.ZNNU_BASE_URL || "https://music.znnu.com";
const ZNNU_SIGNATURE_DOMAIN = "music.znnu.com";
const ZNNU_REFERER = "musicParser";
const ZNNU_SIGNATURE_SECRET = "a09d0f3700a279584e1515354fbe08a7ee1c617f919543142fa625b82f1b5ad0";
const NETEASE_DEFAULT_LEVEL = normalizeNeteaseLevel(process.env.NETEASE_LEVEL || "standard");
const NETEASE_PLAYLIST_CACHE_TTL_MS = intEnv("NETEASE_PLAYLIST_CACHE_TTL_SECONDS", 1800) * 1000;
const NETEASE_URL_CACHE_TTL_MS = intEnv("NETEASE_URL_CACHE_TTL_SECONDS", 600) * 1000;
const NETEASE_LYRICS_CACHE_TTL_MS = intEnv("NETEASE_LYRICS_CACHE_TTL_SECONDS", 86400) * 1000;
const NETEASE_PLAYLIST_CACHE_MAX_ENTRIES = intEnv("NETEASE_PLAYLIST_CACHE_MAX_ENTRIES", 100);
const NETEASE_URL_CACHE_MAX_ENTRIES = intEnv("NETEASE_URL_CACHE_MAX_ENTRIES", 500);
const NETEASE_LYRICS_CACHE_MAX_ENTRIES = intEnv("NETEASE_LYRICS_CACHE_MAX_ENTRIES", 500);
const ZNNU_FETCH_TIMEOUT_MS = intEnv("ZNNU_FETCH_TIMEOUT_SECONDS", 30) * 1000;
const BILI_FETCH_TIMEOUT_MS = intEnv("BILI_FETCH_TIMEOUT_SECONDS", 12) * 1000;
const RATE_LIMIT_WINDOW_MS = intEnv("RATE_LIMIT_WINDOW_SECONDS", 60) * 1000;
const RATE_LIMIT_COOLDOWN_MS = intEnv("RATE_LIMIT_COOLDOWN_SECONDS", 120) * 1000;
const RATE_LIMIT_GENERAL = intEnv("RATE_LIMIT_GENERAL_PER_WINDOW", 300);
const RATE_LIMIT_HOME = intEnv("RATE_LIMIT_HOME_PER_WINDOW", 60);
const RATE_LIMIT_PLAYER = intEnv("RATE_LIMIT_PLAYER_PER_WINDOW", 120);
const RATE_LIMIT_API_DANMAKU = intEnv("RATE_LIMIT_API_DANMAKU_PER_WINDOW", 80);
const RATE_LIMIT_API_RESOLVE = intEnv("RATE_LIMIT_API_RESOLVE_PER_WINDOW", 60);
const RATE_LIMIT_API_PAGES = intEnv("RATE_LIMIT_API_PAGES_PER_WINDOW", 60);
const RATE_LIMIT_API_LYRICS = intEnv("RATE_LIMIT_API_LYRICS_PER_WINDOW", 80);
const RATE_LIMIT_CACHE_STATS = intEnv("RATE_LIMIT_CACHE_STATS_PER_WINDOW", 20);
const FAILURE_CACHE_TTL_MS = intEnv("FAILURE_CACHE_TTL_SECONDS", 180) * 1000;
const FAILURE_CACHE_MAX_ENTRIES = intEnv("FAILURE_CACHE_MAX_ENTRIES", 500);
const VCRID_MIN = 1;
const VCRID_MAX = intEnv("VCRID_MAX", 1000000);
const VCRID_STORE_FILE = process.env.VCRID_STORE_FILE || "/app/data/vcrid-store.json";
const VCRID_GC_ENABLED = String(process.env.VCRID_GC_ENABLED || "true").toLowerCase() !== "false";
const VCRID_GC_USAGE_THRESHOLD = numberEnv("VCRID_GC_USAGE_THRESHOLD", 0.8);
const VCRID_GC_TARGET_USAGE = numberEnv("VCRID_GC_TARGET_USAGE", 0.7);
const VCRID_GC_MIN_UNUSED_DAYS = intEnv("VCRID_GC_MIN_UNUSED_DAYS", 90);
const HISTORY_STORE_FILE = process.env.HISTORY_STORE_FILE || "/app/data/history-store.json";
const DASHBOARD_HISTORY_PAGE_SIZE = intEnv("DASHBOARD_HISTORY_PAGE_SIZE", 20);
const STATS_FILE = process.env.STATS_FILE || "/app/data/stats.json";
const STATS_SAVE_INTERVAL_MS = intEnv("STATS_SAVE_INTERVAL_SECONDS", 30) * 1000;
const DISPLAY_TIME_ZONE = process.env.DISPLAY_TIME_ZONE || process.env.TZ || "Asia/Shanghai";
const PAULKOI_LOGO_PATH = "/assets/paulkoi_logo_transparent.png";
const PAULKOI_LOGO_PNG = readFileSync(new URL("./assets/paulkoi_logo_transparent.png", import.meta.url));

const viewCache = new Map();
const videoUrlCache = new Map();
const danmakuCache = new Map();
const neteasePlaylistCache = new Map();
const neteaseUrlCache = new Map();
const neteaseLyricsCache = new Map();
const bilibiliListCache = new Map();
const failureCache = new Map();
const rateLimitBuckets = new Map();
const inflight = new Map();
let vcrIdStore = null;
let historyStore = null;
let znnuKeySession = null;
let znnuIp = null;

const cacheStats = {
  view: { hits: 0, misses: 0 },
  video: { hits: 0, misses: 0 },
  danmaku: { hits: 0, misses: 0 },
  inflightHits: 0
};

const stats = {
  startedAt: Date.now(),
  totalRequests: 0,
  healthRequests: 0,
  playerRequests: 0,
  playerRedirects: 0,
  liveRedirects: 0,
  neteaseRedirects: 0,
  neteaseSongRedirects: 0,
  neteasePlaylistRedirects: 0,
  playerDanmakuRequests: 0,
  apiDanmakuRequests: 0,
  resolveRequests: 0,
  apiPagesRequests: 0,
  apiLyricsRequests: 0,
  bilibiliPagesManifestRequests: 0,
  bilibiliListManifestRequests: 0,
  neteasePlaylistManifestRequests: 0,
  neteaseSongManifestRequests: 0,
  vcridGcRuns: 0,
  vcridGcDeleted: 0,
  unsupportedUrlRejected: 0,
  legacyRejected: 0,
  errors: 0,
  emittedDanmakuRows: 0,
  rateLimited: 0,
  failureCacheHits: 0,
  failureCacheWrites: 0,
  upstreamTimeouts: 0,
  upstreamErrors: 0
};
let statsDirty = false;

loadPersistedStats();
setInterval(savePersistedStatsIfDirty, STATS_SAVE_INTERVAL_MS).unref();
process.once("SIGTERM", () => {
  savePersistedStats();
  process.exit(0);
});
process.once("SIGINT", () => {
  savePersistedStats();
  process.exit(0);
});

const server = http.createServer(async (req, res) => {
  try {
    const requestUrl = new URL(req.url || "/", `http://${req.headers.host || "localhost"}`);
    setCors(res);

    if (req.method === "OPTIONS") {
      res.writeHead(204);
      res.end();
      return;
    }

    const rateLimit = checkRateLimit(req, requestUrl);
    if (!rateLimit.allowed) {
      stats.totalRequests++;
      stats.rateLimited++;
      markStatsDirty();
      sendText(res, 429, `too many requests\nretry_after=${rateLimit.retryAfterSeconds}\n`, {
        "Retry-After": String(rateLimit.retryAfterSeconds),
        ...noStoreHeaders()
      });
      return;
    }

    stats.totalRequests++;
    markStatsDirty();

    if (requestUrl.pathname === "/") {
      await backfillDashboardBilibiliTitles();
      sendHtml(res, 200, renderDashboard(requestUrl), noStoreHeaders());
      return;
    }

    if (requestUrl.pathname === "/health") {
      stats.healthRequests++;
      sendText(res, 200, "ok\n");
      return;
    }

    if (requestUrl.pathname === PAULKOI_LOGO_PATH) {
      sendBuffer(res, 200, PAULKOI_LOGO_PNG, "image/png", {
        "Cache-Control": "public, max-age=86400"
      });
      return;
    }

    if (requestUrl.pathname === "/api/cache/stats") {
      sendJson(res, 200, buildCacheStats(), noStoreHeaders());
      return;
    }

    if (requestUrl.pathname === "/api/pages") {
      await handlePages(req, res, requestUrl);
      return;
    }

    if (requestUrl.pathname === "/api/lyrics") {
      await handleLyrics(res, requestUrl);
      return;
    }

    if (requestUrl.pathname === "/api/resolve") {
      await handleResolve(res, requestUrl);
      return;
    }

    if (requestUrl.pathname === "/player/" || requestUrl.pathname === "/player") {
      await handlePlayer(req, res, requestUrl);
      return;
    }

    if (requestUrl.pathname === "/api/danmaku") {
      await handleApiDanmaku(res, requestUrl);
      return;
    }

    if (requestUrl.pathname === "/api/current" || requestUrl.pathname === "/api/set") {
      stats.legacyRejected++;
      console.log("[legacy-rejected]", JSON.stringify({ path: requestUrl.pathname }));
      sendText(res, 410, "#YBDM/1\n#error=legacy_room_endpoint_removed\n#use=/player/?url=<Bilibili URL>\n", noStoreHeaders());
      return;
    }

    sendText(res, 404, "not found\n");
  } catch (error) {
    stats.errors++;
    console.error(error);
    sendText(res, 500, `#YBDM/1\n#error=${escapeField(error.message || String(error))}\n`);
  }
});

server.listen(PORT, () => {
  console.log(`PaulKoiPlayer danmaku server listening on http://localhost:${PORT}`);
});

async function handlePlayer(req, res, requestUrl) {
  stats.playerRequests++;
  const vcridResult = parseVcridParam(requestUrl);
  if (vcridResult.present) {
    if (!vcridResult.ok) {
      sendText(res, 400, "#YBDM/1\n#error=invalid_vcrid\n", noStoreHeaders());
      return;
    }
    await handleVcridPlayer(req, res, requestUrl, vcridResult.vcrid);
    return;
  }

  const source = requestUrl.searchParams.get("url") || "";
  if (!source) {
    sendText(res, 400, "missing url\n");
    return;
  }
  const whitelist = validateInputSource(source);
  if (!whitelist.allowed) {
    sendWhitelistRejection(res, whitelist);
    return;
  }

  const forcedPage = readPositiveInt(requestUrl.searchParams.get("p") || requestUrl.searchParams.get("page"), 0);
  const input = await parseInputUrl(source, forcedPage);
  const liveInput = await parseLiveInput(source);
  if (liveInput) {
    const resolved = await resolveLiveUrl(liveInput);
    logPlayerRequest(req, requestUrl, {
      mode: "live-redirect",
      provider: "bilibili-live",
      roomId: liveInput.roomId,
      realRoomId: resolved.realRoomId,
      quality: resolved.actualQuality
    });
    stats.playerRedirects++;
    stats.liveRedirects++;
    recordHistory({
      provider: "bilibili",
      contentType: "live",
      roomId: resolved.realRoomId || liveInput.roomId,
      title: `B站直播 ${resolved.realRoomId || liveInput.roomId}`,
      sourceUrl: source,
      normalizedUrl: liveInput.normalizedUrl || source
    }, "player_redirect");
    res.writeHead(302, {
      "Location": resolved.videoUrl,
      ...noStoreHeaders()
    });
    res.end();
    return;
  }

  if (!input.bvid && !input.aid) {
    const neteaseInput = await parseNeteaseInput(source, forcedPage);
    if (neteaseInput) {
      const level = normalizeNeteaseLevel(requestUrl.searchParams.get("level") || requestUrl.searchParams.get("quality") || NETEASE_DEFAULT_LEVEL);
      const danmakuMode = isDanmakuRequest(req, requestUrl);
      if (danmakuMode) {
        logPlayerRequest(req, requestUrl, {
          mode: "netease-manifest",
          provider: "netease",
          neteaseType: neteaseInput.type,
          playlistId: neteaseInput.playlistId || "",
          songId: neteaseInput.songId || "",
          requestedPage: neteaseInput.page,
          level
        });
        stats.playerDanmakuRequests++;
        const origin = getRequestOrigin(req, requestUrl);
        if (neteaseInput.type === "playlist") {
          const playlist = await getNeteasePlaylist(neteaseInput);
          stats.neteasePlaylistManifestRequests++;
          markStatsDirty();
          const payload = buildNeteasePlaylistResponse({
            source,
            input: { ...neteaseInput, level },
            playlist,
            origin,
            selectedIndex: forcedPage || neteaseInput.page || 1
          });
          recordNeteasePlaylistHistory(payload, "player_manifest");
          sendJson(res, 200, payload, noStoreHeaders());
          return;
        }

        stats.neteaseSongManifestRequests++;
        markStatsDirty();
        const payload = buildNeteaseSongResponse({
          source,
          input: { ...neteaseInput, level },
          origin
        });
        recordNeteaseSongHistory(payload, "player_manifest");
        sendJson(res, 200, payload, noStoreHeaders());
        return;
      }

      const resolved = await resolveNeteaseUrl({ ...neteaseInput, level });
      logPlayerRequest(req, requestUrl, {
        mode: "netease-redirect",
        provider: "netease",
        neteaseType: neteaseInput.type,
        playlistId: neteaseInput.playlistId || "",
        songId: resolved.songId || "",
        requestedPage: resolved.selectedPage || neteaseInput.page,
        level
      });
      stats.playerRedirects++;
      stats.neteaseRedirects++;
      if (neteaseInput.type === "playlist") stats.neteasePlaylistRedirects++;
      if (neteaseInput.type === "song") stats.neteaseSongRedirects++;
      if (neteaseInput.type === "playlist") {
        recordHistory({
          provider: "netease",
          contentType: "playlist",
          playlistId: neteaseInput.playlistId,
          title: resolved.playlistName || neteaseInput.playlistId,
          sourceUrl: source,
          normalizedUrl: neteaseInput.normalizedUrl,
          pagesCount: resolved.totalTracks || 0,
          level
        }, "player_redirect");
      }
      recordHistory({
        provider: "netease",
        contentType: "song",
        songId: resolved.songId || neteaseInput.songId,
        playlistId: neteaseInput.playlistId,
        title: resolved.name || "",
        name: resolved.name || "",
        artist: resolved.artist || "",
        album: resolved.album || "",
        sourceUrl: source,
        normalizedUrl: neteaseInput.normalizedUrl,
        level: resolved.level || level
      }, "player_redirect");
      res.writeHead(302, {
        "Location": resolved.audioUrl,
        ...noStoreHeaders()
      });
      res.end();
      return;
    }

    if (isNeteaseHashFragmentMissing(source)) {
      sendText(res, 400, "#YBDM/1\n#error=netease_hash_fragment_not_sent\n#hint=use_music.163.com/song?id=xxx_or_encode_hash_as_%23\n", noStoreHeaders());
      return;
    }

    sendText(res, 400, "#YBDM/1\n#error=missing_bvid_or_aid\n");
    return;
  }

  const danmakuMode = isDanmakuRequest(req, requestUrl);
  logPlayerRequest(req, requestUrl, {
    mode: danmakuMode ? "danmaku" : "redirect",
    bvid: input.bvid,
    aid: input.aid,
    requestedPage: input.page,
    forcedDanmaku: requestUrl.searchParams.get("__dm") === "1"
  });

  if (danmakuMode) {
    stats.playerDanmakuRequests++;
    const tsv = await getDanmakuTsv(input);
    stats.emittedDanmakuRows += getDanmakuCount(tsv);
    recordBilibiliHistoryFromTsv(tsv, source, input.normalizedUrl, "player_danmaku");
    sendText(res, 200, tsv, danmakuCacheHeaders());
    return;
  }

  const resolved = await resolveVideoUrl(input);
  if (!resolved.videoUrl) {
    sendText(res, 502, `no mp4 durl for this video\nbvid=${resolved.bvid}\ncid=${resolved.cid}\n`);
    return;
  }

  stats.playerRedirects++;
  recordHistory({
    provider: "bilibili",
    contentType: "video",
    sourceUrl: source,
    normalizedUrl: input.normalizedUrl,
    bvid: resolved.bvid,
    aid: resolved.aid,
    cid: resolved.cid,
    page: resolved.page,
    title: resolved.title,
    part: resolved.part,
    duration: resolved.duration,
    pagesCount: resolved.pagesCount || 0
  }, "player_redirect");
  res.writeHead(302, {
    "Location": resolved.videoUrl,
    ...noStoreHeaders()
  });
  res.end();
}

async function handleResolve(res, requestUrl) {
  stats.resolveRequests++;
  const vcridResult = parseVcridParam(requestUrl);
  if (vcridResult.present) {
    if (!vcridResult.ok) {
      sendJson(res, 400, { error: "invalid_vcrid" }, noStoreHeaders());
      return;
    }
    await handleVcridResolve(res, vcridResult.vcrid);
    return;
  }

  const source = requestUrl.searchParams.get("url") || "";
  if (!source) {
    sendJson(res, 400, { error: "missing url" }, noStoreHeaders());
    return;
  }
  const whitelist = validateInputSource(source);
  if (!whitelist.allowed) {
    stats.unsupportedUrlRejected++;
    markStatsDirty();
    sendJson(res, 400, {
      error: whitelist.error,
      host: whitelist.host,
      path: whitelist.path,
      inputUrl: source
    }, noStoreHeaders());
    return;
  }

  const forcedPage = readPositiveInt(requestUrl.searchParams.get("p") || requestUrl.searchParams.get("page"), 0);
  const input = await parseInputUrl(source, forcedPage);
  if (!input.bvid && !input.aid) {
    const neteaseInput = await parseNeteaseInput(source, forcedPage);
    if (neteaseInput) {
      const level = normalizeNeteaseLevel(requestUrl.searchParams.get("level") || requestUrl.searchParams.get("quality") || NETEASE_DEFAULT_LEVEL);
      const resolved = await resolveNeteaseUrl({ ...neteaseInput, level });
      if (neteaseInput.type === "playlist") {
        recordHistory({
          provider: "netease",
          contentType: "playlist",
          playlistId: neteaseInput.playlistId,
          title: resolved.playlistName || neteaseInput.playlistId,
          sourceUrl: source,
          normalizedUrl: neteaseInput.normalizedUrl,
          pagesCount: resolved.totalTracks || 0,
          level
        }, "api_resolve");
      }
      recordHistory({
        provider: "netease",
        contentType: "song",
        songId: resolved.songId || neteaseInput.songId,
        playlistId: neteaseInput.playlistId,
        title: resolved.name || "",
        name: resolved.name || "",
        artist: resolved.artist || "",
        album: resolved.album || "",
        sourceUrl: source,
        normalizedUrl: neteaseInput.normalizedUrl,
        level: resolved.level || level
      }, "api_resolve");
      sendJson(res, 200, {
        type: "netease",
        provider: "netease",
        inputUrl: source,
        normalizedUrl: neteaseInput.normalizedUrl,
        neteaseType: neteaseInput.type,
        playlistId: resolved.playlistId || neteaseInput.playlistId || "",
        playlistName: resolved.playlistName || "",
        songId: resolved.songId,
        selectedPage: resolved.selectedPage || neteaseInput.page || 1,
        totalTracks: resolved.totalTracks || 1,
        name: resolved.name || "",
        artist: resolved.artist || "",
        album: resolved.album || "",
        level: resolved.level || level,
        audioUrlPreview: resolved.audioUrl ? `${resolved.audioUrl.slice(0, 120)}...` : ""
      }, noStoreHeaders());
      return;
    }

    sendJson(res, 400, { error: "missing bvid or aid", inputUrl: source }, noStoreHeaders());
    return;
  }

  const viewResult = await getVideoView(input);
  const view = viewResult.value;
  const selected = selectVideoPage(view, input);
  const video = await resolveVideoUrl({ ...input, cid: selected.cid });
  recordHistory({
    provider: "bilibili",
    contentType: "video",
    sourceUrl: source,
    normalizedUrl: input.normalizedUrl,
    bvid: view.bvid || input.bvid || "",
    aid: view.aid || input.aid || 0,
    cid: selected.cid,
    page: selected.page,
    title: view.title || "",
    part: selected.part || "",
    duration: selected.duration || 0,
    pagesCount: Array.isArray(view.pages) ? view.pages.length : 0
  }, "api_resolve");

  sendJson(res, 200, {
    inputUrl: source,
    normalizedUrl: input.normalizedUrl,
    bvid: view.bvid || input.bvid || "",
    aid: view.aid || input.aid || 0,
    requestedPage: input.page,
    selectedPage: selected.page,
    cid: selected.cid,
    title: view.title || "",
    part: selected.part || "",
    duration: selected.duration || 0,
    pagesCount: Array.isArray(view.pages) ? view.pages.length : 0,
    videoUrlPreview: video.videoUrl ? `${video.videoUrl.slice(0, 120)}...` : "",
    cacheHit: {
      view: viewResult.cacheHit,
      video: video.cacheHit
    }
  }, noStoreHeaders());
}

async function handleVcridPlayer(req, res, requestUrl, vcrid) {
  const record = vcrIdStore.get(vcrid);
  if (!record) {
    sendText(res, 404, "#YBDM/1\n#error=vcrid_not_found\n", noStoreHeaders());
    return;
  }

  const danmakuMode = isDanmakuRequest(req, requestUrl);
  if (record.provider === "netease") {
    if (danmakuMode) {
      stats.playerDanmakuRequests++;
      const tsv = await buildNeteaseDanmakuTsv(record);
      stats.emittedDanmakuRows += getDanmakuCount(tsv);
      recordHistoryFromVcrRecord(record, "vcrid_danmaku");
      sendText(res, 200, tsv, danmakuCacheHeaders());
      return;
    }

    const resolved = await resolveNeteaseRecord(record, { played: true });
    recordHistoryFromVcrRecord({ ...record, name: resolved.name || record.name, artist: resolved.artist || record.artist, album: resolved.album || record.album }, "vcrid_player");
    stats.playerRedirects++;
    stats.neteaseRedirects++;
    stats.neteaseSongRedirects++;
    res.writeHead(302, { "Location": resolved.audioUrl, ...noStoreHeaders() });
    res.end();
    return;
  }

  const input = await inputFromVcrRecord(record);
  if (danmakuMode) {
    stats.playerDanmakuRequests++;
    const tsv = await getDanmakuTsv(input, { vcrid, manifestKey: record.manifestKey || "" });
    stats.emittedDanmakuRows += getDanmakuCount(tsv);
    recordBilibiliHistoryFromTsv(tsv, record.normalizedUrl || "", input.normalizedUrl, "vcrid_danmaku");
    sendText(res, 200, tsv, danmakuCacheHeaders());
    return;
  }

  const resolved = await resolveVideoUrl(input);
  vcrIdStore.updateRecord(vcrid, {
    title: resolved.title || record.title || "",
    part: resolved.part || record.part || "",
    lastAccessAt: Date.now()
  });
  vcrIdStore.touch(vcrid, { played: true });
  recordHistory({
    provider: "bilibili",
    contentType: "video",
    bvid: resolved.bvid,
    aid: resolved.aid,
    cid: resolved.cid,
    page: resolved.page,
    title: resolved.title,
    part: resolved.part,
    vcrid
  }, "vcrid_player");
  stats.playerRedirects++;
  res.writeHead(302, { "Location": resolved.videoUrl, ...noStoreHeaders() });
  res.end();
}

async function handleVcridResolve(res, vcrid) {
  const record = vcrIdStore.get(vcrid);
  if (!record) {
    sendJson(res, 404, { error: "vcrid_not_found", vcrid }, noStoreHeaders());
    return;
  }

  if (record.provider === "netease") {
    const resolved = await resolveNeteaseRecord(record);
    recordHistoryFromVcrRecord(record, "vcrid_resolve");
    sendJson(res, 200, {
      type: "netease-vcrid",
      provider: "netease",
      vcrid,
      songId: resolved.songId,
      playlistId: record.playlistId || "",
      page: record.page || 1,
      name: resolved.name || record.name || "",
      artist: resolved.artist || record.artist || "",
      album: resolved.album || record.album || "",
      level: resolved.level || record.level || NETEASE_DEFAULT_LEVEL,
      audioUrlPreview: resolved.audioUrl ? `${resolved.audioUrl.slice(0, 120)}...` : ""
    }, noStoreHeaders());
    return;
  }

  const input = await inputFromVcrRecord(record);
  const resolved = await resolveVideoUrl(input);
  recordHistory({
    provider: "bilibili",
    contentType: "video",
    bvid: resolved.bvid,
    aid: resolved.aid,
    cid: resolved.cid,
    page: resolved.page,
    title: resolved.title,
    part: resolved.part,
    vcrid
  }, "vcrid_resolve");
  sendJson(res, 200, {
    type: "bilibili-vcrid",
    vcrid,
    bvid: resolved.bvid,
    aid: resolved.aid,
    cid: resolved.cid,
    page: resolved.page,
    title: resolved.title,
    part: resolved.part,
    videoUrlPreview: resolved.videoUrl ? `${resolved.videoUrl.slice(0, 120)}...` : ""
  }, noStoreHeaders());
}

async function handleVcridDanmaku(res, vcrid) {
  const record = vcrIdStore.get(vcrid);
  if (!record) {
    sendText(res, 404, "#YBDM/1\n#error=vcrid_not_found\n", noStoreHeaders());
    return;
  }

  if (record.provider === "netease") {
    const tsv = await buildNeteaseDanmakuTsv(record);
    stats.emittedDanmakuRows += getDanmakuCount(tsv);
    recordHistoryFromVcrRecord(record, "vcrid_danmaku");
    sendText(res, 200, tsv, danmakuCacheHeaders());
    return;
  }

  const input = await inputFromVcrRecord(record);
  const tsv = await getDanmakuTsv(input, { vcrid, manifestKey: record.manifestKey || "" });
  stats.emittedDanmakuRows += getDanmakuCount(tsv);
  recordBilibiliHistoryFromTsv(tsv, record.normalizedUrl || "", input.normalizedUrl, "vcrid_danmaku");
  sendText(res, 200, tsv, danmakuCacheHeaders());
}

async function handleLyrics(res, requestUrl) {
  stats.apiLyricsRequests++;
  markStatsDirty();
  const target = await resolveLyricsTarget(requestUrl);
  if (target.error) {
    sendJson(res, target.status || 400, { error: target.error }, noStoreHeaders());
    return;
  }

  const lyrics = await getNeteaseLyrics(target.songId);
  sendJson(res, 200, {
    type: "netease-lyrics",
    provider: "netease",
    vcrid: target.vcrid || 0,
    songId: target.songId,
    name: lyrics.name || target.name || "",
    artist: lyrics.artist || target.artist || "",
    album: lyrics.album || target.album || "",
    coverUrl: lyrics.coverUrl || "",
    lyric: lyrics.lyric || "",
    translatedLyric: lyrics.translatedLyric || "",
    lines: lyrics.lines || [],
    nolyric: Boolean(lyrics.nolyric),
    uncollected: Boolean(lyrics.uncollected)
  }, {
    "Cache-Control": `public, max-age=${Math.floor(NETEASE_LYRICS_CACHE_TTL_MS / 1000)}`,
    "Access-Control-Allow-Origin": "*"
  });
}

async function handlePages(req, res, requestUrl) {
  stats.apiPagesRequests++;
  const source = requestUrl.searchParams.get("url") || "";
  if (!source) {
    sendJson(res, 400, { error: "missing url" }, noStoreHeaders());
    return;
  }

  const whitelist = validateInputSource(source);
  if (!whitelist.allowed) {
    stats.unsupportedUrlRejected++;
    markStatsDirty();
    sendJson(res, 400, {
      error: whitelist.error,
      host: whitelist.host,
      path: whitelist.path,
      inputUrl: source
    }, noStoreHeaders());
    return;
  }

  const forcedPage = readPositiveInt(requestUrl.searchParams.get("p") || requestUrl.searchParams.get("page"), 0);
  const input = await parseInputUrl(source, forcedPage);
  const cid = normalizeAid(requestUrl.searchParams.get("cid") || "");
  if (cid) input.cid = cid;

  if (!input.bvid && !input.aid) {
    const listInput = parseBilibiliListInput(source);
    if (listInput) {
      try {
        const list = await getBilibiliList(listInput);
        const origin = getRequestOrigin(req, requestUrl);
        stats.bilibiliListManifestRequests++;
        markStatsDirty();
        const payload = buildBilibiliListResponse({
          source,
          input: listInput,
          list,
          origin,
          selectedIndex: forcedPage || 1
        });
        recordHistory({
          provider: "bilibili",
          contentType: "list",
          sourceUrl: source,
          normalizedUrl: listInput.normalizedUrl,
          listId: firstNonEmpty(listInput.mediaId, listInput.sid, listInput.mid, listInput.normalizedUrl),
          title: payload.title || "",
          pagesCount: payload.totalPages || 0
        }, "api_pages");
        sendJson(res, 200, payload, noStoreHeaders());
      } catch (error) {
        sendJson(res, 502, {
          error: error.message || String(error),
          inputUrl: source,
          listType: listInput.type
        }, noStoreHeaders());
      }
      return;
    }

    const neteaseInput = await parseNeteaseInput(source, forcedPage);
    if (neteaseInput) {
      try {
        const level = normalizeNeteaseLevel(requestUrl.searchParams.get("level") || requestUrl.searchParams.get("quality") || NETEASE_DEFAULT_LEVEL);
        const origin = getRequestOrigin(req, requestUrl);
        if (neteaseInput.type === "playlist") {
          const playlist = await getNeteasePlaylist(neteaseInput);
          stats.neteasePlaylistManifestRequests++;
          markStatsDirty();
          const payload = buildNeteasePlaylistResponse({
            source,
            input: { ...neteaseInput, level },
            playlist,
            origin,
            selectedIndex: forcedPage || 1
          });
          recordNeteasePlaylistHistory(payload, "api_pages");
          sendJson(res, 200, payload, noStoreHeaders());
          return;
        }

        stats.neteaseSongManifestRequests++;
        markStatsDirty();
        const payload = buildNeteaseSongResponse({
          source,
          input: { ...neteaseInput, level },
          origin
        });
        recordNeteaseSongHistory(payload, "api_pages");
        sendJson(res, 200, payload, noStoreHeaders());
      } catch (error) {
        sendJson(res, 502, {
          error: error.message || String(error),
          inputUrl: source,
          provider: "netease"
        }, noStoreHeaders());
      }
      return;
    }

    sendJson(res, 400, { error: "missing bvid or aid", inputUrl: source }, noStoreHeaders());
    return;
  }

  const viewResult = await getVideoView(input);
  const view = viewResult.value;
  const pages = Array.isArray(view.pages) ? view.pages : [];
  if (pages.length === 0) {
    sendJson(res, 400, { error: "no pages", inputUrl: source }, noStoreHeaders());
    return;
  }

  const selected = selectVideoPage(view, input);
  const origin = getRequestOrigin(req, requestUrl);
  const vcrRecords = registerVideoViewPages(view, input);
  const pageItems = pages.map((pageItem, index) => {
    const pageNumber = Number(pageItem.page || index + 1);
    const pageVcrid = vcrRecords.get(Number(pageItem.cid))?.vcrid || 0;
    const urls = buildPlaybackUrls({
      origin,
      vcrid: pageVcrid,
      sourceUrl: input.normalizedUrl || source,
      page: pageNumber
    });
    return {
      page: pageNumber,
      cid: Number(pageItem.cid || 0),
      part: pageItem.part || "",
      duration: Number(pageItem.duration || 0),
      vcrid: pageVcrid,
      playUrl: urls.playUrl,
      danmakuUrl: urls.danmakuUrl,
      resolveUrl: urls.resolveUrl
    };
  });

  const payload = {
    type: "bilibili-pages",
    inputUrl: source,
    normalizedUrl: input.normalizedUrl,
    bvid: view.bvid || input.bvid || "",
    aid: view.aid || input.aid || 0,
    title: view.title || "",
    selectedPage: selected.page,
    selectedCid: selected.cid,
    selectedVcrid: vcrRecords.get(Number(selected.cid))?.vcrid || 0,
    totalPages: pages.length,
    pages: pageItems,
    cacheHit: {
      view: viewResult.cacheHit
    }
  };
  stats.bilibiliPagesManifestRequests++;
  markStatsDirty();
  recordHistory({
    provider: "bilibili",
    contentType: "video",
    sourceUrl: source,
    normalizedUrl: input.normalizedUrl,
    bvid: payload.bvid,
    aid: payload.aid,
    cid: payload.selectedCid,
    page: payload.selectedPage,
    title: payload.title,
    pagesCount: payload.totalPages,
    vcrid: payload.selectedVcrid
  }, "api_pages");
  if (vcrRecords.unavailable) {
    payload.warning = "vcrid_store_unavailable";
    payload.vcridStoreError = vcrRecords.unavailable;
  }
  sendJson(res, 200, payload, noStoreHeaders());
}

async function handleApiDanmaku(res, requestUrl) {
  stats.apiDanmakuRequests++;
  const vcridResult = parseVcridParam(requestUrl);
  if (vcridResult.present) {
    if (!vcridResult.ok) {
      sendText(res, 400, "#YBDM/1\n#error=invalid_vcrid\n", noStoreHeaders());
      return;
    }
    await handleVcridDanmaku(res, vcridResult.vcrid);
    return;
  }

  const source = requestUrl.searchParams.get("url") || "";
  const forcedPage = readPositiveInt(requestUrl.searchParams.get("p") || requestUrl.searchParams.get("page"), 0);
  if (source) {
    const whitelist = validateInputSource(source);
    if (!whitelist.allowed) {
      sendWhitelistRejection(res, whitelist);
      return;
    }
  }
  let input = source ? await parseInputUrl(source, forcedPage) : {
    normalizedUrl: "",
    bvid: normalizeBvid(requestUrl.searchParams.get("bvid") || ""),
    aid: normalizeAid(requestUrl.searchParams.get("aid") || requestUrl.searchParams.get("avid") || requestUrl.searchParams.get("oid") || ""),
    page: forcedPage || 1
  };

  if (requestUrl.searchParams.get("bvid")) input.bvid = normalizeBvid(requestUrl.searchParams.get("bvid") || "");
  if (requestUrl.searchParams.get("aid") || requestUrl.searchParams.get("avid") || requestUrl.searchParams.get("oid")) {
    input.aid = normalizeAid(requestUrl.searchParams.get("aid") || requestUrl.searchParams.get("avid") || requestUrl.searchParams.get("oid") || "");
  }
  if (!input.page || input.page < 1) input.page = 1;

  if (!input.bvid && !input.aid) {
    sendText(res, 400, "#YBDM/1\n#error=missing_bvid_or_aid\n");
    return;
  }

  const tsv = await getDanmakuTsv(input);
  stats.emittedDanmakuRows += getDanmakuCount(tsv);
  recordBilibiliHistoryFromTsv(tsv, source, input.normalizedUrl, "api_danmaku");
  sendText(res, 200, tsv, danmakuCacheHeaders());
}

function loadPersistedStats() {
  try {
    if (!existsSync(STATS_FILE)) return;
    const payload = JSON.parse(readFileSync(STATS_FILE, "utf8").replace(/^\uFEFF/, ""));
    mergeNumericFields(stats, payload.stats);
    mergeNumericFields(cacheStats.view, payload.cacheStats?.view);
    mergeNumericFields(cacheStats.video, payload.cacheStats?.video);
    mergeNumericFields(cacheStats.danmaku, payload.cacheStats?.danmaku);
    if (Number.isFinite(Number(payload.cacheStats?.inflightHits))) {
      cacheStats.inflightHits = Number(payload.cacheStats.inflightHits);
    }
    stats.startedAt = Date.now();
  } catch (error) {
    console.warn("[stats] failed to load persisted stats:", error.message || error);
  }
}

function mergeNumericFields(target, source) {
  if (!source || typeof source !== "object") return;
  for (const key of Object.keys(target)) {
    if (key === "startedAt") continue;
    const value = Number(source[key]);
    if (Number.isFinite(value) && value >= 0) target[key] = value;
  }
}

function firstNonEmpty(...values) {
  for (const value of values) {
    const text = String(value ?? "").trim();
    if (text) return text;
  }
  return "";
}

function historyKey(input = {}) {
  const provider = String(input.provider || "").trim();
  const contentType = String(input.contentType || "").trim();
  if (provider === "bilibili") {
    const bvid = normalizeBvid(input.bvid || "");
    if (bvid) return `bilibili:video:${bvid}`;
    const aid = normalizeAid(input.aid || "");
    if (aid) return `bilibili:video:av${aid}`;
    const roomId = normalizeAid(input.roomId || "");
    if (roomId) return `bilibili:live:${roomId}`;
    if (contentType === "list") {
      const listId = firstNonEmpty(input.listId, input.mediaId, input.sid, input.normalizedUrl, input.sourceUrl);
      if (listId) return `bilibili:list:${safeCacheFileName(listId)}`;
    }
  }
  if (provider === "netease") {
    const playlistId = normalizeNumericId(input.playlistId || "");
    if (contentType === "playlist" && playlistId) return `netease:playlist:${playlistId}`;
    const songId = normalizeNumericId(input.songId || "");
    if (songId) return `netease:song:${songId}`;
  }
  return "";
}

function recordHistory(input, event = "seen", options = {}) {
  try {
    return historyStore?.upsert(input, event, options);
  } catch (error) {
    console.warn(`[history] record failed: ${error.message || error}`);
    return null;
  }
}

function seedHistoryFromExistingStores() {
  if (!historyStore?.available || !vcrIdStore?.available) return;
  let changed = false;
  for (const record of Object.values(vcrIdStore.data.records || {})) {
    if (!record || !record.provider) continue;
    if (record.provider === "bilibili") {
      const payload = {
        provider: "bilibili",
        contentType: "video",
        bvid: record.bvid,
        aid: record.aid,
        cid: record.cid,
        page: record.page,
        title: record.title,
        part: record.part,
        duration: record.duration,
        vcrid: record.vcrid
      };
      if (!historyStore.data.records[historyKey(payload)]) {
        recordHistory(payload, "imported", { deferSave: true });
        changed = true;
      }
      continue;
    }
    if (record.provider === "netease") {
      if (record.playlistId) {
        const playlistPayload = {
          provider: "netease",
          contentType: "playlist",
          playlistId: record.playlistId,
          title: record.playlistId,
          level: record.level,
          vcrid: record.vcrid
        };
        if (!historyStore.data.records[historyKey(playlistPayload)]) {
          recordHistory(playlistPayload, "imported", { deferSave: true });
          changed = true;
        }
      }
      const songPayload = {
        provider: "netease",
        contentType: "song",
        songId: record.songId,
        playlistId: record.playlistId,
        title: record.name || record.part,
        name: record.name,
        artist: record.artist,
        album: record.album,
        duration: record.duration,
        level: record.level,
        vcrid: record.vcrid
      };
      if (!historyStore.data.records[historyKey(songPayload)]) {
        recordHistory(songPayload, "imported", { deferSave: true });
        changed = true;
      }
    }
  }
  if (changed) historyStore.save();
}

class VcrIdStore {
  constructor(filePath, maxId) {
    this.filePath = filePath;
    this.maxId = maxId;
    this.data = {
      version: 1,
      nextId: VCRID_MIN,
      records: {},
      contentIndex: {},
      manifests: {}
    };
    this.available = true;
    this.lastGc = {
      checkedAt: 0,
      ranAt: 0,
      deleted: 0,
      before: 0,
      after: 0,
      reason: ""
    };
    this.load();
  }

  load() {
    try {
      if (!existsSync(this.filePath)) return;
      const payload = JSON.parse(readFileSync(this.filePath, "utf8").replace(/^\uFEFF/, ""));
      this.data = {
        version: 1,
        nextId: Math.max(VCRID_MIN, Number(payload.nextId || VCRID_MIN)),
        records: payload.records && typeof payload.records === "object" ? payload.records : {},
        contentIndex: payload.contentIndex && typeof payload.contentIndex === "object" ? payload.contentIndex : {},
        manifests: payload.manifests && typeof payload.manifests === "object" ? payload.manifests : {}
      };
      this.rebuildIndex();
    } catch (error) {
      this.available = false;
      console.warn(`[vcrid] failed to load store: ${error.message}`);
    }
  }

  rebuildIndex() {
    this.data.contentIndex = {};
    for (const record of Object.values(this.data.records)) {
      if (record?.contentKey) this.data.contentIndex[record.contentKey] = record.vcrid;
    }
  }

  allocate(input) {
    if (!this.available) throw new Error("vcrid_store_unavailable");
    const now = Date.now();
    const keys = this.lookupKeys(input);
    for (const key of keys) {
      const existingId = this.data.contentIndex[key];
      if (existingId && this.data.records[String(existingId)]) {
        const record = this.data.records[String(existingId)];
        this.updateRecord(existingId, { ...input, contentKey: input.contentKey || key, lastAccessAt: now });
        return record;
      }
    }

    this.runGcIfNeeded(now);
    const vcrid = this.nextFreeId();
    const contentKey = input.contentKey || keys[0];
    const record = {
      vcrid,
      provider: input.provider || "bilibili",
      contentType: input.contentType || "page",
      contentKey,
      bvid: input.bvid || "",
      aid: Number(input.aid || 0),
      cid: Number(input.cid || 0),
      songId: input.songId ? String(input.songId) : "",
      playlistId: input.playlistId ? String(input.playlistId) : "",
      page: Math.max(1, Number(input.page || 1)),
      title: input.title || "",
      part: input.part || "",
      name: input.name || "",
      artist: input.artist || "",
      album: input.album || "",
      level: input.level || "",
      duration: Number(input.duration || 0),
      manifestKey: input.manifestKey || "",
      createdAt: now,
      updatedAt: now,
      lastAccessAt: now
    };
    this.data.records[String(vcrid)] = record;
    this.data.contentIndex[contentKey] = vcrid;
    for (const key of keys) if (!this.data.contentIndex[key]) this.data.contentIndex[key] = vcrid;
    this.data.nextId = Math.min(this.maxId + 1, vcrid + 1);
    if (!this.save()) {
      delete this.data.records[String(vcrid)];
      this.rebuildIndex();
      throw new Error("vcrid_store_unavailable");
    }
    return record;
  }

  recordCount() {
    return Object.keys(this.data.records || {}).length;
  }

  usageRatio() {
    return this.maxId > 0 ? this.recordCount() / this.maxId : 0;
  }

  runGcIfNeeded(now = Date.now()) {
    this.lastGc.checkedAt = now;
    if (!VCRID_GC_ENABLED) {
      this.lastGc.reason = "disabled";
      return;
    }
    if (this.usageRatio() < VCRID_GC_USAGE_THRESHOLD) {
      this.lastGc.reason = "below_threshold";
      return;
    }
    this.collectGarbage(now);
  }

  collectGarbage(now = Date.now()) {
    const before = this.recordCount();
    const targetCount = Math.max(0, Math.floor(this.maxId * VCRID_GC_TARGET_USAGE));
    const deleteLimit = Math.max(0, before - targetCount);
    const minUnusedMs = Math.max(1, VCRID_GC_MIN_UNUSED_DAYS) * 86400 * 1000;
    if (deleteLimit <= 0) {
      this.lastGc = { checkedAt: now, ranAt: now, deleted: 0, before, after: before, reason: "target_not_below_current" };
      return;
    }

    const candidates = Object.values(this.data.records || {})
      .filter((record) => {
        const lastAccessAt = Number(record?.lastAccessAt || record?.updatedAt || record?.createdAt || 0);
        return record?.vcrid && lastAccessAt > 0 && now - lastAccessAt >= minUnusedMs;
      })
      .sort((left, right) => {
        const leftTime = Number(left.lastAccessAt || left.updatedAt || left.createdAt || 0);
        const rightTime = Number(right.lastAccessAt || right.updatedAt || right.createdAt || 0);
        return leftTime - rightTime || Number(left.vcrid || 0) - Number(right.vcrid || 0);
      })
      .slice(0, deleteLimit);

    if (!candidates.length) {
      this.lastGc = { checkedAt: now, ranAt: now, deleted: 0, before, after: before, reason: "no_old_records" };
      return;
    }

    const deletedIds = new Set();
    for (const record of candidates) {
      const id = String(record.vcrid);
      if (this.data.records[id]) {
        delete this.data.records[id];
        deletedIds.add(Number(record.vcrid));
      }
    }
    this.sanitizeManifests(deletedIds);
    this.rebuildIndex();
    this.data.nextId = VCRID_MIN;
    if (!this.save()) throw new Error("vcrid_store_unavailable");

    const after = this.recordCount();
    const deleted = before - after;
    stats.vcridGcRuns++;
    stats.vcridGcDeleted += deleted;
    markStatsDirty();
    this.lastGc = { checkedAt: now, ranAt: now, deleted, before, after, reason: "threshold" };
    console.log("[vcrid-gc]", JSON.stringify(this.lastGc));
  }

  sanitizeManifests(deletedIds) {
    if (!deletedIds?.size) return;
    for (const manifest of Object.values(this.data.manifests || {})) {
      if (!Array.isArray(manifest?.items)) continue;
      for (const item of manifest.items) {
        if (deletedIds.has(Number(item?.vcrid || 0))) item.vcrid = 0;
      }
      manifest.updatedAt = Date.now();
    }
  }

  lookupKeys(input) {
    const keys = [];
    if (input.contentKey) keys.push(input.contentKey);
    if (input.provider === "netease") {
      if (input.songId) keys.push(neteaseSongContentKey(input.songId, input.level));
      return [...new Set(keys.filter(Boolean))];
    }
    if (input.cid) keys.push(videoContentKey(input.bvid, input.aid, input.cid));
    keys.push(tempVideoContentKey(input.bvid, input.aid, input.page || 1));
    return [...new Set(keys.filter(Boolean))];
  }

  nextFreeId() {
    for (let id = Math.max(VCRID_MIN, Number(this.data.nextId || VCRID_MIN)); id <= this.maxId; id++) {
      if (!this.data.records[String(id)]) return id;
    }
    for (let id = VCRID_MIN; id < Math.max(VCRID_MIN, Number(this.data.nextId || VCRID_MIN)); id++) {
      if (!this.data.records[String(id)]) return id;
    }
    throw new Error("vcrid_pool_exhausted");
  }

  get(vcrid) {
    return this.data.records[String(vcrid)] || null;
  }

  touch(vcrid, options = {}) {
    const record = this.get(vcrid);
    if (!record) return;
    const now = Date.now();
    record.lastAccessAt = now;
    if (options.played) {
      record.lastPlayedAt = now;
      record.playCount = Number(record.playCount || 0) + 1;
    }
    this.save();
  }

  updateRecord(vcrid, patch) {
    const record = this.get(vcrid);
    if (!record) return null;
    const previousKey = record.contentKey;
    Object.assign(record, patch, {
      vcrid: record.vcrid,
      updatedAt: Date.now(),
      lastAccessAt: patch.lastAccessAt || Date.now()
    });
    if (previousKey && previousKey !== record.contentKey && this.data.contentIndex[previousKey] === record.vcrid) {
      delete this.data.contentIndex[previousKey];
    }
    if (record.contentKey) this.data.contentIndex[record.contentKey] = record.vcrid;
    if (record.provider === "netease") {
      if (record.songId) this.data.contentIndex[neteaseSongContentKey(record.songId, record.level)] = record.vcrid;
    } else {
      if (record.cid) this.data.contentIndex[videoContentKey(record.bvid, record.aid, record.cid)] = record.vcrid;
      this.data.contentIndex[tempVideoContentKey(record.bvid, record.aid, record.page)] = record.vcrid;
    }
    this.save();
    return record;
  }

  saveManifest(key, manifest) {
    this.data.manifests[key] = { ...manifest, updatedAt: Date.now() };
    if (!this.save()) throw new Error("vcrid_store_unavailable");
  }

  getManifest(key) {
    return this.data.manifests[key] || null;
  }

  save() {
    try {
      mkdirSync(dirname(this.filePath), { recursive: true });
      const tmpFile = `${this.filePath}.tmp`;
      writeFileSync(tmpFile, `${JSON.stringify(this.data, null, 2)}\n`, "utf8");
      renameSync(tmpFile, this.filePath);
      return true;
    } catch (error) {
      this.available = false;
      console.warn(`[vcrid] failed to save store: ${error.message}`);
      return false;
    }
  }
}

vcrIdStore = new VcrIdStore(VCRID_STORE_FILE, VCRID_MAX);

class HistoryStore {
  constructor(filePath) {
    this.filePath = filePath;
    this.data = {
      version: 1,
      nextId: 1,
      records: {}
    };
    this.available = true;
    this.load();
  }

  load() {
    try {
      if (!existsSync(this.filePath)) return;
      const payload = JSON.parse(readFileSync(this.filePath, "utf8").replace(/^\uFEFF/, ""));
      this.data = {
        version: 1,
        nextId: Math.max(1, Number(payload.nextId || 1)),
        records: payload.records && typeof payload.records === "object" ? payload.records : {}
      };
    } catch (error) {
      this.available = false;
      console.warn(`[history] failed to load store: ${error.message}`);
    }
  }

  upsert(input = {}, event = "seen", options = {}) {
    if (!this.available) return null;
    const key = input.key || historyKey(input);
    if (!key) return null;
    const now = Date.now();
    const current = this.data.records[key] || {
      id: this.data.nextId++,
      key,
      provider: input.provider || "",
      contentType: input.contentType || "",
      title: "",
      firstSeenAt: now,
      lastSeenAt: now,
      seenCount: 0,
      events: {},
      vcrids: []
    };

    current.provider = input.provider || current.provider || "";
    current.contentType = input.contentType || current.contentType || "";
    current.title = firstNonEmpty(input.title, input.name, current.title, input.part);
    current.sourceUrl = firstNonEmpty(input.sourceUrl, current.sourceUrl);
    current.normalizedUrl = firstNonEmpty(input.normalizedUrl, current.normalizedUrl);
    current.bvid = firstNonEmpty(input.bvid, current.bvid);
    current.aid = normalizeAid(input.aid || current.aid || "");
    current.cid = normalizeAid(input.cid || current.cid || "");
    current.page = readPositiveInt(input.page || current.page, 0);
    current.pagesCount = Math.max(Number(current.pagesCount || 0), Number(input.pagesCount || input.totalPages || 0));
    current.songId = firstNonEmpty(input.songId, current.songId);
    current.playlistId = firstNonEmpty(input.playlistId, current.playlistId);
    current.artist = firstNonEmpty(input.artist, current.artist);
    current.album = firstNonEmpty(input.album, current.album);
    current.level = firstNonEmpty(input.level, current.level);
    current.duration = Math.max(Number(current.duration || 0), Number(input.duration || 0));
    current.lastSeenAt = now;
    current.seenCount = Number(current.seenCount || 0) + 1;
    current.events = current.events && typeof current.events === "object" ? current.events : {};
    current.events[event] = Number(current.events[event] || 0) + 1;
    current.vcrids = Array.isArray(current.vcrids) ? current.vcrids : [];
    if (input.vcrid) {
      const id = Number(input.vcrid);
      if (id > 0 && !current.vcrids.includes(id)) current.vcrids.push(id);
      current.vcrids = current.vcrids.slice(-200);
    }

    this.data.records[key] = current;
    if (!options.deferSave) this.save();
    return current;
  }

  list({ page = 1, pageSize = 20, provider = "", contentType = "" } = {}) {
    const normalizedPageSize = Math.min(100, Math.max(1, Number(pageSize || 20)));
    const all = Object.values(this.data.records || {})
      .filter((record) => !provider || record.provider === provider)
      .filter((record) => !contentType || record.contentType === contentType)
      .sort((left, right) => Number(right.lastSeenAt || 0) - Number(left.lastSeenAt || 0));
    const total = all.length;
    const totalPages = Math.max(1, Math.ceil(total / normalizedPageSize));
    const currentPage = Math.min(Math.max(1, Number(page || 1)), totalPages);
    const start = (currentPage - 1) * normalizedPageSize;
    return {
      page: currentPage,
      pageSize: normalizedPageSize,
      total,
      totalPages,
      items: all.slice(start, start + normalizedPageSize)
    };
  }

  save() {
    try {
      mkdirSync(dirname(this.filePath), { recursive: true });
      const tmpFile = `${this.filePath}.tmp`;
      writeFileSync(tmpFile, `${JSON.stringify(this.data, null, 2)}\n`, "utf8");
      renameSync(tmpFile, this.filePath);
      return true;
    } catch (error) {
      this.available = false;
      console.warn(`[history] failed to save store: ${error.message}`);
      return false;
    }
  }
}

historyStore = new HistoryStore(HISTORY_STORE_FILE);
seedHistoryFromExistingStores();

function recordBilibiliHistoryFromTsv(tsv, sourceUrl = "", normalizedUrl = "", event = "danmaku") {
  recordHistory({
    provider: "bilibili",
    contentType: "video",
    sourceUrl,
    normalizedUrl,
    bvid: headerValue(tsv, "bvid"),
    aid: headerValue(tsv, "aid"),
    cid: headerValue(tsv, "cid"),
    page: headerValue(tsv, "page"),
    title: headerValue(tsv, "title"),
    part: headerValue(tsv, "part"),
    vcrid: headerValue(tsv, "vcrid")
  }, event);
}

function recordNeteasePlaylistHistory(payload, event = "seen") {
  recordHistory({
    provider: "netease",
    contentType: "playlist",
    sourceUrl: payload.inputUrl || "",
    normalizedUrl: payload.normalizedUrl || "",
    playlistId: payload.playlistId || "",
    title: payload.title || payload.playlistId || "",
    pagesCount: payload.totalPages || payload.totalTracks || 0,
    level: payload.level || NETEASE_DEFAULT_LEVEL,
    vcrid: payload.selectedVcrid || 0
  }, event);
}

function recordNeteaseSongHistory(payload, event = "seen") {
  const page = Array.isArray(payload.pages) ? payload.pages[0] : null;
  recordHistory({
    provider: "netease",
    contentType: "song",
    sourceUrl: payload.inputUrl || "",
    normalizedUrl: payload.normalizedUrl || "",
    songId: payload.selectedSongId || payload.songId || page?.songId || "",
    playlistId: payload.playlistId || "",
    title: page?.title || page?.name || payload.title || payload.name || "",
    name: page?.name || payload.name || "",
    artist: page?.artist || payload.artist || "",
    album: page?.album || payload.album || "",
    duration: page?.duration || payload.duration || 0,
    level: payload.level || page?.level || NETEASE_DEFAULT_LEVEL,
    vcrid: payload.selectedVcrid || page?.vcrid || 0
  }, event);
}

function recordHistoryFromVcrRecord(record, event = "vcrid") {
  if (!record) return;
  if (record.provider === "netease") {
    if (record.playlistId) {
      recordHistory({
        provider: "netease",
        contentType: "playlist",
        playlistId: record.playlistId,
        title: record.playlistId,
        level: record.level,
        vcrid: record.vcrid
      }, event);
    }
    recordHistory({
      provider: "netease",
      contentType: "song",
      songId: record.songId,
      playlistId: record.playlistId,
      title: record.name || record.part,
      name: record.name,
      artist: record.artist,
      album: record.album,
      duration: record.duration,
      level: record.level,
      vcrid: record.vcrid
    }, event);
    return;
  }
  if (record.provider === "bilibili") {
    recordHistory({
      provider: "bilibili",
      contentType: "video",
      bvid: record.bvid,
      aid: record.aid,
      cid: record.cid,
      page: record.page,
      title: record.title,
      part: record.part,
      duration: record.duration,
      vcrid: record.vcrid
    }, event);
  }
}

function markStatsDirty() {
  statsDirty = true;
}

function savePersistedStatsIfDirty() {
  if (!statsDirty) return;
  savePersistedStats();
}

function savePersistedStats() {
  try {
    mkdirSync(dirname(STATS_FILE), { recursive: true });
    const payload = {
      version: 1,
      savedAt: new Date().toISOString(),
      stats: {
        totalRequests: stats.totalRequests,
        healthRequests: stats.healthRequests,
        playerRequests: stats.playerRequests,
        playerRedirects: stats.playerRedirects,
        liveRedirects: stats.liveRedirects,
        neteaseRedirects: stats.neteaseRedirects,
        neteaseSongRedirects: stats.neteaseSongRedirects,
        neteasePlaylistRedirects: stats.neteasePlaylistRedirects,
        playerDanmakuRequests: stats.playerDanmakuRequests,
        apiDanmakuRequests: stats.apiDanmakuRequests,
        resolveRequests: stats.resolveRequests,
        apiPagesRequests: stats.apiPagesRequests,
        apiLyricsRequests: stats.apiLyricsRequests,
        bilibiliPagesManifestRequests: stats.bilibiliPagesManifestRequests,
        bilibiliListManifestRequests: stats.bilibiliListManifestRequests,
        neteasePlaylistManifestRequests: stats.neteasePlaylistManifestRequests,
        neteaseSongManifestRequests: stats.neteaseSongManifestRequests,
        vcridGcRuns: stats.vcridGcRuns,
        vcridGcDeleted: stats.vcridGcDeleted,
        legacyRejected: stats.legacyRejected,
        errors: stats.errors,
        emittedDanmakuRows: stats.emittedDanmakuRows
      },
      cacheStats: {
        view: { hits: cacheStats.view.hits, misses: cacheStats.view.misses },
        video: { hits: cacheStats.video.hits, misses: cacheStats.video.misses },
        danmaku: { hits: cacheStats.danmaku.hits, misses: cacheStats.danmaku.misses },
        inflightHits: cacheStats.inflightHits
      }
    };
    const tmpFile = `${STATS_FILE}.tmp`;
    writeFileSync(tmpFile, `${JSON.stringify(payload, null, 2)}\n`, "utf8");
    renameSync(tmpFile, STATS_FILE);
    statsDirty = false;
  } catch (error) {
    console.warn("[stats] failed to save persisted stats:", error.message || error);
  }
}

async function parseNeteaseInput(rawValue, forcedPage = 0) {
  let normalizedUrl = decodeRepeatedly(rawValue);
  normalizedUrl = unwrapKnownPlayerUrl(normalizedUrl);
  normalizedUrl = decodeRepeatedly(normalizedUrl);
  normalizedUrl = extractSupportedUrlFromText(normalizedUrl);
  normalizedUrl = unwrapKnownPlayerUrl(normalizedUrl);
  normalizedUrl = decodeRepeatedly(normalizedUrl);
  normalizedUrl = await expandNeteaseShortUrl(normalizedUrl);
  normalizedUrl = decodeRepeatedly(normalizedUrl);
  normalizedUrl = unwrapKnownPlayerUrl(normalizedUrl);
  normalizedUrl = decodeRepeatedly(normalizedUrl);

  const parsed = tryParseFlexibleUrl(normalizedUrl);
  if (!parsed || !isNeteaseHost(parsed.hostname)) return null;

  const candidates = [{ pathname: parsed.pathname, searchParams: parsed.searchParams }];
  const hash = parsed.hash ? parsed.hash.replace(/^#\/?/, "/") : "";
  if (hash) {
    try {
      const hashUrl = new URL(hash, "https://music.163.com");
      candidates.push({ pathname: hashUrl.pathname, searchParams: hashUrl.searchParams });
    } catch {
      // Ignore malformed hash routers and fall back to the normal URL fields.
    }
  }

  for (const candidate of candidates) {
    const path = String(candidate.pathname || "");
    const lowerPath = path.toLowerCase();
    const isPlaylistPath = /(^|\/)(?:f\/|m\/)?playlist(\/|$)/.test(lowerPath) || lowerPath.includes("toplist");
    const isSongPath = /(^|\/)(?:f\/|m\/)?song(\/|$)/.test(lowerPath) || lowerPath.includes("/song/media/outer/url");

    if (isPlaylistPath) {
      const playlistId = extractNeteaseId(candidate, "playlist");
      if (playlistId) {
        return {
          type: "playlist",
          normalizedUrl,
          rawInput: rawValue,
          playlistId,
          page: Math.max(1, forcedPage || 1)
        };
      }
    }

    if (isSongPath) {
      const songId = extractNeteaseId(candidate, "song");
      if (songId) {
        return {
          type: "song",
          normalizedUrl,
          rawInput: rawValue,
          songId,
          page: 1
        };
      }
    }
  }

  return null;
}

async function expandNeteaseShortUrl(value) {
  const parsed = tryParseFlexibleUrl(value);
  if (!parsed || normalizeHost(parsed.hostname) !== "163cn.tv") return value;

  return singleflightWithFailureCache(`netease-short:${parsed.href}`, async () => {
    const json = await znnuGetJson("/api/redirect", { url: parsed.href });
    if (json && json.code === 200 && typeof json.redirectUrl === "string" && json.redirectUrl) {
      return json.redirectUrl;
    }
    throw new Error(json?.msg || json?.message || "NetEase short link redirect failed.");
  });
}

async function resolveNeteaseUrl(input) {
  if (input.type === "playlist") {
    const playlist = await getNeteasePlaylist(input);
    if (!playlist.tracks.length) throw new Error(`NetEase playlist ${input.playlistId} has no tracks.`);

    const index = Math.min(Math.max(input.page || 1, 1), playlist.tracks.length) - 1;
    const track = playlist.tracks[index];
    if (!track?.id) throw new Error(`NetEase playlist ${input.playlistId} track ${index + 1} has no song id.`);

    const song = await getNeteaseSongDirect({
      songId: String(track.id),
      rawInput: String(track.id),
      level: input.level
    });
    return {
      ...song,
      playlistId: String(playlist.id || input.playlistId),
      playlistName: playlist.name || "",
      selectedPage: index + 1,
      totalTracks: playlist.tracks.length
    };
  }

  return getNeteaseSongDirect(input);
}

async function getNeteasePlaylist(input) {
  return getSimpleCached(neteasePlaylistCache, `netease-playlist:${input.playlistId}`, NETEASE_PLAYLIST_CACHE_TTL_MS, NETEASE_PLAYLIST_CACHE_MAX_ENTRIES, async () => {
    const ip = await getZnnuIp();
    const decoded = await postZnnuForm("/api/playlist", {
      act: "playlist",
      id: input.playlistId,
      rawInput: input.normalizedUrl || input.rawInput || input.playlistId,
      ip
    });

    if (decoded.code !== 200) {
      throw new Error(decoded.msg || decoded.message || `NetEase playlist ${input.playlistId} resolve failed.`);
    }

    const data = decoded.data || {};
    return {
      id: data.id || input.playlistId,
      name: data.name || "",
      trackCount: Number(data.trackCount || 0),
      tracks: Array.isArray(data.tracks) ? data.tracks : []
    };
  });
}

async function getNeteaseSongDirect(input) {
  const level = normalizeNeteaseLevel(input.level || NETEASE_DEFAULT_LEVEL);
  return getSimpleCached(neteaseUrlCache, `netease-song:${input.songId}:${level}`, NETEASE_URL_CACHE_TTL_MS, NETEASE_URL_CACHE_MAX_ENTRIES, async () => {
    const ip = await getZnnuIp();
    const decoded = await postZnnuForm("/api/song", {
      act: "song",
      id: input.songId,
      level,
      rawInput: input.normalizedUrl || input.rawInput || input.songId,
      ip
    });

    if (decoded.code !== 200) {
      throw new Error(decoded.msg || decoded.message || `NetEase song ${input.songId} resolve failed.`);
    }

    const data = decoded.data || {};
    const audioUrl = normalizePlayableUrl(data.url);
    if (!audioUrl) throw new Error(decoded.msg || decoded.message || `NetEase song ${input.songId} has no playable url.`);

    return {
      provider: "netease",
      audioUrl,
      songId: String(input.songId),
      name: data.name || "",
      artist: data.artist || "",
      album: data.album || "",
      type: data.type || "",
      level: data.level || level,
      size: data.size || "",
      selectedPage: input.page || 1
    };
  });
}

function buildNeteasePlaylistResponse({ source, input, playlist, origin, selectedIndex }) {
  const tracks = normalizeNeteaseTracks(playlist.tracks || []);
  const total = tracks.length;
  const selectedPage = total ? Math.min(Math.max(readPositiveInt(selectedIndex, 1), 1), total) : 0;
  const level = normalizeNeteaseLevel(input.level || NETEASE_DEFAULT_LEVEL);
  const playlistId = String(playlist.id || input.playlistId || "");
  const manifestKey = `netease-playlist:${playlistId}:${level}`;
  let warning = "";
  let vcridStoreError = "";
  const records = tracks.map((track, index) => {
    if (warning) return null;
    try {
      return vcrIdStore.allocate({
        provider: "netease",
        contentType: "playlist-item",
        contentKey: neteaseSongContentKey(track.songId, level),
        songId: track.songId,
        playlistId,
        page: index + 1,
        part: track.name,
        name: track.name,
        artist: track.artist,
        album: track.album,
        level,
        duration: track.duration,
        manifestKey
      });
    } catch (error) {
      warning = "vcrid_store_unavailable";
      vcridStoreError = error.message || warning;
      return null;
    }
  });
  if (!warning) {
    try {
      vcrIdStore.saveManifest(manifestKey, {
        manifestType: "netease-playlist",
        provider: "netease",
        playlistId,
        title: playlist.name || "",
        level,
        items: tracks.map((track, index) => ({ ...track, vcrid: records[index]?.vcrid || 0, index: index + 1 }))
      });
    } catch (error) {
      warning = "vcrid_store_unavailable";
      vcridStoreError = error.message || warning;
    }
  }

  const selected = tracks[selectedPage - 1] || null;
  const selectedRecord = records[selectedPage - 1] || null;
  const pages = tracks.map((track, index) => {
    const pageNumber = index + 1;
    const pageVcrid = records[index]?.vcrid || 0;
    const urls = buildPlaybackUrls({
      origin,
      vcrid: pageVcrid,
      sourceUrl: neteaseSongUrlForTrack(track),
      page: 1
    });
    return {
      page: pageNumber,
      songId: track.songId,
      title: track.name,
      name: track.name,
      artist: track.artist,
      album: track.album,
      duration: track.duration,
      level,
      vcrid: pageVcrid,
      playUrl: urls.playUrl,
      danmakuUrl: urls.danmakuUrl,
      resolveUrl: urls.resolveUrl
    };
  });

  const response = {
    type: "netease-playlist",
    provider: "netease",
    inputUrl: source,
    normalizedUrl: input.normalizedUrl,
    playlistId,
    title: playlist.name || "",
    selectedPage,
    selectedSongId: selected?.songId || "",
    selectedVcrid: selectedRecord?.vcrid || 0,
    totalPages: total,
    totalTracks: total,
    level,
    pages
  };
  if (warning) {
    response.warning = warning;
    response.vcridStoreError = vcridStoreError;
  }
  return response;
}

function buildNeteaseSongResponse({ source, input, origin }) {
  const level = normalizeNeteaseLevel(input.level || NETEASE_DEFAULT_LEVEL);
  const track = {
    songId: String(input.songId || ""),
    name: "",
    artist: "",
    album: "",
    duration: 0
  };
  let warning = "";
  let vcridStoreError = "";
  let record = null;
  try {
    record = vcrIdStore.allocate({
      provider: "netease",
      contentType: "song",
      contentKey: neteaseSongContentKey(track.songId, level),
      songId: track.songId,
      page: 1,
      part: track.name,
      name: track.name,
      artist: track.artist,
      album: track.album,
      level,
      duration: track.duration
    });
  } catch (error) {
    warning = "vcrid_store_unavailable";
    vcridStoreError = error.message || warning;
  }
  const urls = buildPlaybackUrls({
    origin,
    vcrid: record?.vcrid || 0,
    sourceUrl: input.normalizedUrl || source,
    page: 1
  });
  const response = {
    type: "netease-song",
    provider: "netease",
    inputUrl: source,
    normalizedUrl: input.normalizedUrl,
    songId: track.songId,
    selectedPage: 1,
    selectedSongId: track.songId,
    selectedVcrid: record?.vcrid || 0,
    totalPages: 1,
    totalTracks: 1,
    level,
    pages: [{
      page: 1,
      songId: track.songId,
      title: track.name,
      name: track.name,
      artist: track.artist,
      album: track.album,
      duration: track.duration,
      level,
      vcrid: record?.vcrid || 0,
      playUrl: urls.playUrl,
      danmakuUrl: urls.danmakuUrl,
      resolveUrl: urls.resolveUrl
    }]
  };
  if (warning) {
    response.warning = warning;
    response.vcridStoreError = vcridStoreError;
  }
  return response;
}

function normalizeNeteaseTracks(tracks) {
  if (!Array.isArray(tracks)) return [];
  return tracks.map(normalizeNeteaseTrack).filter((track) => track.songId);
}

function normalizeNeteaseTrack(track) {
  const song = track?.song || track?.resource || track || {};
  const songId = String(normalizeNumericId(song.id || song.songId || track?.id || track?.songId || "") || "");
  const artistValue = song.artist || song.artists || song.ar || track?.artist || track?.artists || track?.ar || "";
  const albumValue = song.album || song.al || track?.album || track?.al || "";
  return {
    songId,
    name: String(song.name || song.title || track?.name || track?.title || "").trim(),
    artist: normalizeNeteaseArtist(artistValue),
    album: normalizeNeteaseAlbum(albumValue),
    duration: normalizeNeteaseDurationSeconds(song.duration || song.dt || track?.duration || track?.dt || 0)
  };
}

function normalizeNeteaseArtist(value) {
  if (Array.isArray(value)) {
    return value.map((item) => item?.name || item?.alias || item).filter(Boolean).join(" / ");
  }
  if (value && typeof value === "object") return String(value.name || value.alias || "").trim();
  return String(value || "").trim();
}

function normalizeNeteaseAlbum(value) {
  if (value && typeof value === "object") return String(value.name || value.title || "").trim();
  return String(value || "").trim();
}

function normalizeNeteaseDurationSeconds(value) {
  const number = Number(value || 0);
  if (!Number.isFinite(number) || number <= 0) return 0;
  return Math.floor(number > 86400 ? number / 1000 : number);
}

function neteaseSongContentKey(songId, level) {
  return `netease:song:${normalizeNumericId(songId)}:level:${normalizeNeteaseLevel(level || NETEASE_DEFAULT_LEVEL)}`;
}

function neteaseSongUrlForTrack(track) {
  return `https://music.163.com/song?id=${encodeURIComponent(track.songId || "")}`;
}

async function resolveNeteaseRecord(record, options = {}) {
  if (!record || record.provider !== "netease" || !record.songId) throw new Error("vcrid_not_found");
  const level = normalizeNeteaseLevel(record.level || NETEASE_DEFAULT_LEVEL);
  const resolved = await getNeteaseSongDirect({
    songId: String(record.songId),
    rawInput: String(record.songId),
    level,
    page: record.page || 1
  });
  vcrIdStore.touch(record.vcrid, options);
  return {
    ...resolved,
    name: resolved.name || record.name || "",
    artist: resolved.artist || record.artist || "",
    album: resolved.album || record.album || "",
    level: resolved.level || level
  };
}

async function buildNeteaseDanmakuTsv(record) {
  const lyrics = await getNeteaseLyrics(record.songId);
  const lines = normalizeLyricDanmakuLines(lyrics.lines || []);
  const title = lyrics.name || record.name || record.part || "";
  const artist = lyrics.artist || record.artist || "";
  const album = lyrics.album || record.album || "";
  const header = [
    "#YBDM/1",
    `#manifest_type=${record.manifestKey ? "netease-playlist" : "netease-song"}`,
    "#provider=netease",
    `#vcrid=${record.vcrid || ""}`,
    `#vcrid_max=${VCRID_MAX}`,
    `#song_id=${escapeField(record.songId || "")}`,
    `#playlist_id=${escapeField(record.playlistId || "")}`,
    `#page=${record.page || 1}`,
    `#title=${escapeField(title)}`,
    `#artist=${escapeField(artist)}`,
    `#album=${escapeField(album)}`,
    `#cover=${escapeField(lyrics.coverUrl || "")}`,
    `#level=${escapeField(record.level || NETEASE_DEFAULT_LEVEL)}`,
    `#count=${lines.length}`,
    "#columns=progressMs\tmode\tcolor\tfontSize\tpool\tcontent",
  ];
  if (lyrics.nolyric) header.push("#notice=nolyric");
  if (lyrics.uncollected) header.push("#notice=uncollected");
  const body = lines.map((line) => [
    line.timeMs,
    4,
    16777215,
    25,
    0,
    escapeField(line.text)
  ].join("\t"));
  return `${header.join("\n")}\n${body.join("\n")}\n`;
}

function normalizeLyricDanmakuLines(lines) {
  const out = [];
  const seen = new Set();
  for (const line of Array.isArray(lines) ? lines : []) {
    const timeMs = Math.max(0, Number(line?.timeMs || 0));
    const original = String(line?.text || "").trim();
    const translation = String(line?.translation || "").trim();
    for (const text of [original, translation && translation !== original ? translation : ""]) {
      if (!text) continue;
      const key = `${timeMs}:${text}`;
      if (seen.has(key)) continue;
      seen.add(key);
      out.push({ timeMs, text });
    }
  }
  return out.sort((left, right) => left.timeMs - right.timeMs);
}

async function resolveLyricsTarget(requestUrl) {
  const vcridResult = parseVcridParam(requestUrl);
  if (vcridResult.present) {
    if (!vcridResult.ok) return { error: "invalid_vcrid", status: 400 };
    const record = vcrIdStore.get(vcridResult.vcrid);
    if (!record) return { error: "vcrid_not_found", status: 404 };
    if (record.provider !== "netease" || !record.songId) {
      return { error: "lyrics_not_supported_for_provider", status: 400 };
    }
    vcrIdStore.touch(record.vcrid);
    return {
      vcrid: record.vcrid,
      songId: String(record.songId),
      name: record.name || record.part || "",
      artist: record.artist || "",
      album: record.album || ""
    };
  }

  const songId = normalizeNumericId(requestUrl.searchParams.get("songId") || requestUrl.searchParams.get("id") || "");
  if (songId) return { songId };

  const source = requestUrl.searchParams.get("url") || "";
  if (source) {
    const whitelist = validateInputSource(source);
    if (!whitelist.allowed) return { error: whitelist.error || "unsupported_url", status: 400 };
    const forcedPage = readPositiveInt(requestUrl.searchParams.get("p") || requestUrl.searchParams.get("page"), 1);
    const input = await parseNeteaseInput(source, forcedPage);
    if (!input) return { error: "unsupported_lyrics_url", status: 400 };
    if (input.type === "song") return { songId: input.songId };
    const playlist = await getNeteasePlaylist(input);
    const tracks = normalizeNeteaseTracks(playlist.tracks || []);
    const index = Math.min(Math.max(input.page || 1, 1), tracks.length) - 1;
    const track = tracks[index] || null;
    if (!track?.songId) return { error: "playlist_track_not_found", status: 404 };
    return {
      songId: track.songId,
      name: track.name,
      artist: track.artist,
      album: track.album
    };
  }

  return { error: "missing_vcrid_or_song_id", status: 400 };
}

async function getNeteaseLyrics(songId) {
  const id = normalizeNumericId(songId);
  if (!id) throw new Error("missing_song_id");
  return getSimpleCached(neteaseLyricsCache, `netease-lyrics:${id}`, NETEASE_LYRICS_CACHE_TTL_MS, NETEASE_LYRICS_CACHE_MAX_ENTRIES, async () => {
    const [lyrics, detail] = await Promise.all([
      fetchNeteaseLyricPayload(id),
      fetchNeteaseSongDetail(id).catch(() => ({}))
    ]);
    const lyric = String(lyrics?.lrc?.lyric || "");
    const translatedLyric = String(lyrics?.tlyric?.lyric || "");
    const song = detail.song || {};
    return {
      songId: id,
      name: song.name || "",
      artist: song.artist || "",
      album: song.album || "",
      coverUrl: song.coverUrl || "",
      lyric,
      translatedLyric,
      lines: parseLrcLines(lyric, translatedLyric),
      nolyric: Boolean(lyrics?.nolyric),
      uncollected: Boolean(lyrics?.uncollected)
    };
  });
}

async function fetchNeteaseLyricPayload(songId) {
  const url = new URL("https://music.163.com/api/song/lyric");
  url.searchParams.set("id", String(songId));
  url.searchParams.set("lv", "1");
  url.searchParams.set("kv", "1");
  url.searchParams.set("tv", "-1");
  return fetchNeteaseJson(url, "NetEase lyric");
}

async function fetchNeteaseSongDetail(songId) {
  const url = new URL("https://music.163.com/api/song/detail");
  url.searchParams.set("ids", `[${songId}]`);
  const payload = await fetchNeteaseJson(url, "NetEase song detail");
  const song = Array.isArray(payload?.songs) ? payload.songs[0] : null;
  if (!song) return {};
  const artists = Array.isArray(song.artists || song.ar)
    ? (song.artists || song.ar).map((item) => item?.name || item).filter(Boolean).join(" / ")
    : "";
  const album = song.album || song.al || {};
  return {
    song: {
      name: song.name || "",
      artist: artists,
      album: album.name || "",
      coverUrl: normalizePlayableUrl(album.picUrl || album.pic || "")
    }
  };
}

async function fetchNeteaseJson(url, label) {
  const response = await fetchWithTimeout(url, {
    headers: {
      "Accept": "application/json, text/plain, */*",
      "Referer": "https://music.163.com/",
      "User-Agent": USER_AGENT
    }
  }, ZNNU_FETCH_TIMEOUT_MS, label);
  const text = await response.text();
  if (!response.ok) {
    stats.upstreamErrors++;
    markStatsDirty();
    throw new Error(`${label} failed with HTTP ${response.status}.`);
  }
  try {
    return JSON.parse(text);
  } catch {
    throw new Error(`${label} returned non-JSON response.`);
  }
}

function parseLrcLines(lyric, translatedLyric = "") {
  const translated = new Map();
  for (const line of parseSingleLrc(translatedLyric)) {
    if (line.text && !translated.has(line.timeMs)) translated.set(line.timeMs, line.text);
  }
  return parseSingleLrc(lyric)
    .map((line) => ({ ...line, translation: translated.get(line.timeMs) || "" }))
    .sort((left, right) => left.timeMs - right.timeMs);
}

function parseSingleLrc(text) {
  const out = [];
  for (const rawLine of String(text || "").split(/\r?\n/)) {
    const stamps = [...rawLine.matchAll(/\[(\d{1,2}):(\d{2})(?:\.(\d{1,3}))?\]/g)];
    if (!stamps.length) continue;
    const content = rawLine.replace(/\[[^\]]+\]/g, "").trim();
    for (const stamp of stamps) {
      const minute = Number(stamp[1] || 0);
      const second = Number(stamp[2] || 0);
      const fraction = String(stamp[3] || "0").padEnd(3, "0").slice(0, 3);
      out.push({
        timeMs: (minute * 60 + second) * 1000 + Number(fraction),
        text: content
      });
    }
  }
  return out;
}

async function postZnnuForm(path, payload) {
  const session = await getZnnuKeySession();
  const signed = signZnnuPayload(payload);
  const body = new URLSearchParams({
    ...payload,
    signature: signed.signature,
    timestamp: String(signed.timestamp),
    domain: signed.domain
  });
  const json = await znnuFetchJson(path, {
    method: "POST",
    headers: znnuHeaders({
      "Content-Type": "application/x-www-form-urlencoded",
      "X-Key-Token": session.keyToken
    }),
    body
  });
  return decodeZnnuResponse(json, session.key);
}

async function getZnnuKeySession() {
  const now = Math.floor(Date.now() / 1000);
  if (znnuKeySession && readPositiveInt(znnuKeySession.expireAt, 0) - 5 > now) return znnuKeySession;

  const json = await znnuGetJson("/api/key");
  const data = json?.data || null;
  if (json.code !== 200 || !data?.key || !data?.keyToken || !data?.expireAt) {
    throw new Error(json?.msg || json?.message || "Failed to get ZNNU key.");
  }

  znnuKeySession = {
    key: data.key,
    keyToken: data.keyToken,
    expireAt: readPositiveInt(data.expireAt, 0)
  };
  return znnuKeySession;
}

async function getZnnuIp() {
  if (znnuIp !== null) return znnuIp;

  try {
    const json = await znnuGetJson("/api/ip");
    znnuIp = typeof json?.ip === "string" ? json.ip : "";
  } catch {
    znnuIp = "";
  }

  return znnuIp;
}

async function znnuGetJson(path, query = {}) {
  const url = new URL(path, ZNNU_BASE_URL);
  for (const [key, value] of Object.entries(query)) {
    if (value !== undefined && value !== null && value !== "") url.searchParams.set(key, String(value));
  }
  return znnuFetchJson(url);
}

async function znnuFetchJson(urlOrPath, options = {}) {
  const url = urlOrPath instanceof URL ? urlOrPath : new URL(urlOrPath, ZNNU_BASE_URL);
  const response = await fetchWithTimeout(url, {
    method: options.method || "GET",
    headers: options.headers || znnuHeaders(),
    body: options.body
  }, ZNNU_FETCH_TIMEOUT_MS, "ZNNU");
  const text = await response.text();
  if (!response.ok) {
    stats.upstreamErrors++;
    markStatsDirty();
    throw new Error(`ZNNU request failed with HTTP ${response.status}.`);
  }
  try {
    return JSON.parse(text);
  } catch {
    throw new Error("ZNNU returned non-JSON response.");
  }
}

function znnuHeaders(extra = {}) {
  return {
    "Accept": "application/json, text/plain, */*",
    "User-Agent": USER_AGENT,
    "X-Referer": ZNNU_REFERER,
    ...extra
  };
}

function signZnnuPayload(payload) {
  const timestamp = Math.floor(Date.now() / 1000);
  const cleanPayload = { ...payload };
  delete cleanPayload.signature;
  delete cleanPayload.timestamp;
  delete cleanPayload.domain;
  delete cleanPayload.ver;

  const signString = Object.keys(cleanPayload)
    .sort()
    .reduce((result, key) => result + key + "=" + cleanPayload[key], String(timestamp) + ZNNU_SIGNATURE_DOMAIN);

  return {
    signature: createHmac("sha256", ZNNU_SIGNATURE_SECRET).update(signString).digest("hex"),
    timestamp,
    domain: ZNNU_SIGNATURE_DOMAIN
  };
}

function decodeZnnuResponse(json, keyBase64) {
  if (!json?.data || json.data.enc !== 1 || json.data.alg !== "AES-256-GCM") return json;

  const key = Buffer.from(String(keyBase64 || ""), "base64");
  const iv = Buffer.from(String(json.data.iv || ""), "base64");
  const ciphertext = Buffer.from(String(json.data.ciphertext || ""), "base64");
  const tag = Buffer.from(String(json.data.tag || ""), "base64");
  const decipher = createDecipheriv("aes-256-gcm", key, iv);
  decipher.setAuthTag(tag);
  const decrypted = Buffer.concat([decipher.update(ciphertext), decipher.final()]).toString("utf8");
  return { ...json, data: JSON.parse(decrypted) };
}

async function getSimpleCached(map, key, ttlMs, maxEntries, loader) {
  const cached = map.get(key);
  if (cached && Date.now() - cached.time < ttlMs) return cached.value;
  if (cached) map.delete(key);
  throwCachedFailure(key);

  return singleflight(key, async () => {
    const current = map.get(key);
    if (current && Date.now() - current.time < ttlMs) return current.value;
    if (current) map.delete(key);

    const value = await loadWithFailureCache(key, loader);
    setCacheEntry(map, key, value, maxEntries);
    return value;
  });
}

async function parseInputUrl(rawValue, forcedPage = 0) {
  let normalizedUrl = decodeRepeatedly(rawValue);
  normalizedUrl = unwrapKnownPlayerUrl(normalizedUrl);
  normalizedUrl = decodeRepeatedly(normalizedUrl);
  normalizedUrl = extractSupportedUrlFromText(normalizedUrl);
  normalizedUrl = await expandB23Url(normalizedUrl);
  normalizedUrl = unwrapKnownPlayerUrl(normalizedUrl);
  normalizedUrl = decodeRepeatedly(normalizedUrl);

  let bvid = "";
  let aid = 0;
  let page = forcedPage || 1;
  const parsed = tryParseUrl(normalizedUrl);

  if (parsed) {
    const pathBvid = parsed.pathname.match(/\/video\/(BV[0-9A-Za-z]{10,})/i);
    const pathAid = parsed.pathname.match(/\/video\/av(\d+)/i);
    if (pathBvid) bvid = normalizeBvid(pathBvid[1]);
    else if (pathAid) aid = normalizeAid(pathAid[1]);

    if (!bvid && parsed.searchParams.get("bvid")) bvid = normalizeBvid(parsed.searchParams.get("bvid") || "");
    if (!aid) aid = normalizeAid(parsed.searchParams.get("aid") || parsed.searchParams.get("avid") || parsed.searchParams.get("oid") || "");
    if (!forcedPage) page = readPositiveInt(parsed.searchParams.get("p") || parsed.searchParams.get("page"), 1);
  } else {
    bvid = normalizeBvid(normalizedUrl);
    aid = normalizeAid(extractAid(normalizedUrl));
  }

  return {
    normalizedUrl,
    bvid,
    aid,
    page: Math.max(1, page || 1)
  };
}

async function parseLiveInput(rawValue) {
  let normalizedUrl = decodeRepeatedly(rawValue);
  normalizedUrl = unwrapKnownPlayerUrl(normalizedUrl);
  normalizedUrl = decodeRepeatedly(normalizedUrl);
  normalizedUrl = extractSupportedUrlFromText(normalizedUrl);
  normalizedUrl = await expandB23Url(normalizedUrl);
  normalizedUrl = unwrapKnownPlayerUrl(normalizedUrl);
  normalizedUrl = decodeRepeatedly(normalizedUrl);

  const parsed = tryParseUrl(normalizedUrl);
  if (!parsed || parsed.hostname.toLowerCase() !== "live.bilibili.com") return null;

  const roomId = parseLiveRoomId(parsed);
  if (!roomId) return null;

  return {
    type: "live",
    roomId,
    normalizedUrl
  };
}

function parseBilibiliListInput(rawValue) {
  let normalizedUrl = decodeRepeatedly(rawValue);
  normalizedUrl = unwrapKnownPlayerUrl(normalizedUrl);
  normalizedUrl = decodeRepeatedly(normalizedUrl);
  normalizedUrl = extractSupportedUrlFromText(normalizedUrl);
  normalizedUrl = unwrapKnownPlayerUrl(normalizedUrl);
  normalizedUrl = decodeRepeatedly(normalizedUrl);

  const parsed = tryParseFlexibleUrl(normalizedUrl);
  if (!parsed) return null;

  const host = normalizeHost(parsed.hostname);
  const path = parsed.pathname.replace(/\/+$/, "") || "/";
  const sid = normalizeAid(parsed.searchParams.get("sid") || parsed.searchParams.get("series_id") || parsed.searchParams.get("season_id") || "");
  const type = String(parsed.searchParams.get("type") || "").trim().toLowerCase();

  const mediaMatch = path.match(/^\/(?:list|medialist\/play)\/ml(\d+)$/i);
  if (mediaMatch) {
    return {
      type: "media-list",
      mediaId: normalizeAid(mediaMatch[1]),
      normalizedUrl: parsed.href
    };
  }

  if (host === "bilibili.com" || host === "m.bilibili.com") {
    const listMatch = path.match(/^\/list\/(\d+)$/i);
    if (listMatch && sid) {
      return {
        type: type === "season" ? "season" : "series",
        mid: normalizeAid(listMatch[1]),
        sid,
        normalizedUrl: parsed.href
      };
    }
  }

  if (host === "space.bilibili.com") {
    const listPath = path.match(/^\/(\d+)\/lists(?:\/(\d+))?$/i);
    if (listPath) {
      const pathSid = normalizeAid(listPath[2] || "");
      const effectiveSid = sid || pathSid;
      if (effectiveSid) {
        return {
          type: type === "season" ? "season" : "series",
          mid: normalizeAid(listPath[1]),
          sid: effectiveSid,
          normalizedUrl: parsed.href
        };
      }
    }
  }

  if (isAllowedBilibiliPath(parsed, host) && (/^\/(?:list|medialist)\//i.test(path) || host === "space.bilibili.com")) {
    return {
      type: "page-source",
      normalizedUrl: parsed.href
    };
  }

  return null;
}

async function getBilibiliList(input) {
  const key = `bili-list:${input.type}:${input.mediaId || ""}:${input.mid || ""}:${input.sid || ""}:${input.normalizedUrl || ""}`;
  return getSimpleCached(bilibiliListCache, key, VIEW_CACHE_TTL_MS, VIEW_CACHE_MAX_ENTRIES, async () => {
    const strategies = [];
    if (input.type === "series") strategies.push(() => fetchBilibiliSeriesList(input));
    if (input.type === "season") strategies.push(() => fetchBilibiliSeasonList(input));
    if (input.type === "media-list") strategies.push(() => fetchBilibiliMediaList(input));
    strategies.push(() => fetchBilibiliListFromPageSource(input.normalizedUrl));

    let lastError = null;
    for (const strategy of strategies) {
      try {
        const list = await strategy();
        if (list?.items?.length) return list;
      } catch (error) {
        lastError = error;
      }
    }
    throw lastError || new Error("Bilibili list has no videos.");
  });
}

async function fetchBilibiliSeriesList(input) {
  const items = [];
  let page = 1;
  let title = "";
  for (; page <= 20; page++) {
    const url = new URL("https://api.bilibili.com/x/series/archives");
    url.searchParams.set("mid", String(input.mid));
    url.searchParams.set("series_id", String(input.sid));
    url.searchParams.set("only_normal", "true");
    url.searchParams.set("sort", "asc");
    url.searchParams.set("pn", String(page));
    url.searchParams.set("ps", "30");
    const payload = await fetchJson(url);
    if (payload.code !== 0) throw new Error(`Bilibili series API failed: ${payload.message || payload.code}`);
    const data = payload.data || {};
    title ||= data.meta?.name || data.series?.name || "";
    const archives = normalizeBilibiliArchiveArray(data.archives || data.aids || data.items || data.list);
    items.push(...archives);
    if (!archives.length || items.length >= Number(data.page?.total || data.total || items.length)) break;
  }
  return { type: "bilibili-list", sourceType: "series", title, items: uniqueBilibiliListItems(items) };
}

async function fetchBilibiliSeasonList(input) {
  const items = [];
  let page = 1;
  let title = "";
  for (; page <= 20; page++) {
    const url = new URL("https://api.bilibili.com/x/polymer/web-space/seasons_archives_list");
    url.searchParams.set("mid", String(input.mid));
    url.searchParams.set("season_id", String(input.sid));
    url.searchParams.set("sort_reverse", "false");
    url.searchParams.set("page_num", String(page));
    url.searchParams.set("page_size", "30");
    const payload = await fetchJson(url);
    if (payload.code !== 0) throw new Error(`Bilibili season API failed: ${payload.message || payload.code}`);
    const data = payload.data || {};
    title ||= data.meta?.name || data.season?.title || "";
    const archives = normalizeBilibiliArchiveArray(data.archives || data.items || data.list);
    items.push(...archives);
    if (!archives.length || items.length >= Number(data.page?.total || data.total || items.length)) break;
  }
  return { type: "bilibili-list", sourceType: "season", title, items: uniqueBilibiliListItems(items) };
}

async function fetchBilibiliMediaList(input) {
  const items = [];
  let page = 1;
  let title = "";
  for (; page <= 20; page++) {
    const url = new URL("https://api.bilibili.com/x/v3/fav/resource/list");
    url.searchParams.set("media_id", String(input.mediaId));
    url.searchParams.set("pn", String(page));
    url.searchParams.set("ps", "20");
    url.searchParams.set("keyword", "");
    url.searchParams.set("order", "mtime");
    url.searchParams.set("type", "0");
    url.searchParams.set("tid", "0");
    const payload = await fetchJson(url);
    if (payload.code !== 0) throw new Error(`Bilibili media list API failed: ${payload.message || payload.code}`);
    const data = payload.data || {};
    title ||= data.info?.title || data.info?.media_list_title || "";
    const archives = normalizeBilibiliArchiveArray(data.medias || data.items || data.list);
    items.push(...archives);
    if (!archives.length || !data.has_more) break;
  }
  return { type: "bilibili-list", sourceType: "media-list", title, items: uniqueBilibiliListItems(items) };
}

async function fetchBilibiliListFromPageSource(urlValue) {
  const parsed = tryParseFlexibleUrl(urlValue);
  if (!parsed) throw new Error("Invalid Bilibili list URL.");
  const response = await fetchWithTimeout(parsed, { headers: biliHeaders() }, BILI_FETCH_TIMEOUT_MS, "Bilibili list page");
  if (!response.ok) throw new Error(`Bilibili list page failed with HTTP ${response.status}.`);
  const html = await response.text();
  const jsonBlocks = extractEmbeddedJsonBlocks(html);
  const items = [];
  let title = "";
  for (const block of jsonBlocks) {
    title ||= findFirstStringField(block, ["title", "name"]);
    items.push(...findBilibiliArchivesInObject(block));
  }
  return { type: "bilibili-list", sourceType: "page-source", title, items: uniqueBilibiliListItems(items) };
}

function buildBilibiliListResponse({ source, input, list, origin, selectedIndex }) {
  const items = uniqueBilibiliListItems(list.items || []);
  const total = items.length;
  const selectedPage = total ? Math.min(Math.max(readPositiveInt(selectedIndex, 1), 1), total) : 0;
  const manifestKey = `list:${input.type}:${input.mediaId || ""}:${input.mid || ""}:${input.sid || ""}:${input.normalizedUrl || ""}`;
  let warning = "";
  let vcridStoreError = "";
  const records = items.map((item) => {
    if (warning) return null;
    try {
      return vcrIdStore.allocate({
        provider: "bilibili",
        contentType: "list-item",
        bvid: item.bvid || "",
        aid: item.aid || 0,
        cid: item.cid || 0,
        page: 1,
        title: item.title || item.part || "",
        part: item.title || item.part || "",
        duration: item.duration || 0,
        manifestKey,
        contentKey: item.cid ? videoContentKey(item.bvid, item.aid, item.cid) : tempVideoContentKey(item.bvid, item.aid, 1)
      });
    } catch (error) {
      warning = "vcrid_store_unavailable";
      vcridStoreError = error.message || warning;
      return null;
    }
  });
  if (!warning) {
    try {
      vcrIdStore.saveManifest(manifestKey, {
        manifestType: "bilibili-list",
        sourceType: list.sourceType || input.type,
        title: list.title || "",
        items: items.map((item, index) => ({ ...item, vcrid: records[index]?.vcrid || 0, index: index + 1 }))
      });
    } catch (error) {
      warning = "vcrid_store_unavailable";
      vcridStoreError = error.message || warning;
    }
  }

  const selected = items[selectedPage - 1] || null;
  const selectedRecord = records[selectedPage - 1] || null;
  const pages = items.map((item, index) => {
    const pageNumber = index + 1;
    const pageVcrid = records[index]?.vcrid || 0;
    const urls = buildPlaybackUrls({
      origin,
      vcrid: pageVcrid,
      sourceUrl: bilibiliVideoUrlForItem(item),
      page: 1
    });
    return {
      page: pageNumber,
      aid: item.aid || 0,
      bvid: item.bvid || "",
      cid: item.cid || 0,
      part: item.title || item.part || "",
      duration: item.duration || 0,
      vcrid: pageVcrid,
      playUrl: urls.playUrl,
      danmakuUrl: urls.danmakuUrl,
      resolveUrl: urls.resolveUrl
    };
  });

  const response = {
    type: "bilibili-list",
    sourceType: list.sourceType || input.type,
    inputUrl: source,
    normalizedUrl: input.normalizedUrl,
    title: list.title || "",
    selectedPage,
    selectedBvid: selected?.bvid || "",
    selectedAid: selected?.aid || 0,
    selectedCid: selected?.cid || 0,
    selectedVcrid: selectedRecord?.vcrid || 0,
    totalPages: total,
    pages
  };
  if (warning) {
    response.warning = warning;
    response.vcridStoreError = vcridStoreError;
  }
  return response;
}

function buildPlaybackUrls({ origin, vcrid, sourceUrl, page }) {
  if (vcrid > 0) {
    return {
      playUrl: `${origin}/player/?vcrid=${vcrid}`,
      danmakuUrl: `${origin}/api/danmaku?vcrid=${vcrid}`,
      resolveUrl: `${origin}/api/resolve?vcrid=${vcrid}`
    };
  }
  const encodedUrl = encodeURIComponent(sourceUrl || "");
  const pageSuffix = page ? `&p=${Math.max(1, Number(page || 1))}` : "";
  return {
    playUrl: `${origin}/player/?url=${encodedUrl}${pageSuffix}`,
    danmakuUrl: `${origin}/api/danmaku?url=${encodedUrl}${pageSuffix}`,
    resolveUrl: `${origin}/api/resolve?url=${encodedUrl}${pageSuffix}`
  };
}

function bilibiliVideoUrlForItem(item) {
  if (item?.bvid) return `https://www.bilibili.com/video/${item.bvid}/`;
  if (item?.aid) return `https://www.bilibili.com/video/av${item.aid}/`;
  return "";
}

function normalizeBilibiliArchiveArray(value) {
  if (!Array.isArray(value)) return [];
  return value
    .map((item) => normalizeBilibiliArchiveItem(item))
    .filter((item) => item.bvid || item.aid);
}

function normalizeBilibiliArchiveItem(item) {
  if (!item || typeof item !== "object") return {};
  const archive = item.archive || item.arc || item.video || item.ugc || item;
  const bvid = normalizeBvid(archive.bvid || item.bvid || "");
  const aid = normalizeAid(archive.aid || archive.id || item.aid || item.id || "");
  return {
    bvid,
    aid,
    cid: normalizeAid(archive.cid || item.cid || ""),
    title: String(archive.title || archive.name || item.title || item.name || "").trim(),
    part: String(archive.part || item.part || "").trim(),
    duration: normalizeDurationSeconds(archive.duration || archive.length || item.duration || item.length || 0)
  };
}

function uniqueBilibiliListItems(items) {
  const seen = new Set();
  const out = [];
  for (const item of normalizeBilibiliArchiveArray(items)) {
    const key = item.bvid || `av${item.aid}`;
    if (!key || seen.has(key)) continue;
    seen.add(key);
    out.push(item);
  }
  return out;
}

function registerVideoViewPages(view, input = {}) {
  const records = new Map();
  const bvid = view.bvid || input.bvid || "";
  const aid = view.aid || input.aid || 0;
  const title = view.title || "";
  const pages = Array.isArray(view.pages) ? view.pages : [];
  for (const pageItem of pages) {
    const page = Number(pageItem.page || 1);
    const cid = Number(pageItem.cid || 0);
    try {
      const record = vcrIdStore.allocate({
        provider: "bilibili",
        contentType: "page",
        contentKey: videoContentKey(bvid, aid, cid),
        bvid,
        aid,
        cid,
        page,
        title,
        part: pageItem.part || title || "",
        duration: Number(pageItem.duration || view.duration || 0)
      });
      records.set(cid, record);
    } catch (error) {
      records.unavailable = error.message || "vcrid_store_unavailable";
    }
  }
  return records;
}

async function inputFromVcrRecord(record, options = {}) {
  if (!record || record.provider !== "bilibili") throw new Error("vcrid_not_found");
  const input = {
    normalizedUrl: record.bvid ? `https://www.bilibili.com/video/${record.bvid}/` : "",
    bvid: record.bvid || "",
    aid: Number(record.aid || 0),
    cid: Number(record.cid || 0),
    page: Math.max(1, Number(record.page || 1))
  };

  if (!input.cid) {
    const view = (await getVideoView(input)).value;
    const selected = selectVideoPage(view, input);
    const contentKey = videoContentKey(view.bvid || input.bvid, view.aid || input.aid, selected.cid);
    vcrIdStore.updateRecord(record.vcrid, {
      bvid: view.bvid || input.bvid,
      aid: view.aid || input.aid,
      cid: selected.cid,
      page: selected.page,
      title: view.title || record.title || "",
      part: selected.part || record.part || "",
      duration: selected.duration || record.duration || 0,
      contentKey
    });
    input.bvid = view.bvid || input.bvid;
    input.aid = view.aid || input.aid;
    input.cid = selected.cid;
    input.page = selected.page;
  }

  vcrIdStore.touch(record.vcrid, options);
  return input;
}

function videoContentKey(bvid, aid, cid) {
  return `bili:${normalizeBvid(bvid) || `av${normalizeAid(aid)}`}:cid:${Number(cid)}`;
}

function tempVideoContentKey(bvid, aid, page) {
  return `bili:${normalizeBvid(bvid) || `av${normalizeAid(aid)}`}:page:${Math.max(1, Number(page || 1))}`;
}

function normalizeDurationSeconds(value) {
  if (typeof value === "number") return Math.max(0, Math.floor(value));
  const text = String(value || "").trim();
  if (!text) return 0;
  if (/^\d+$/.test(text)) return Number.parseInt(text, 10);
  const parts = text.split(":").map((part) => Number.parseInt(part, 10));
  if (parts.some((part) => !Number.isFinite(part))) return 0;
  return parts.reduce((total, part) => total * 60 + part, 0);
}

function extractEmbeddedJsonBlocks(html) {
  const blocks = [];
  const initialMatch = html.match(/window\.__INITIAL_STATE__\s*=\s*({[\s\S]*?})\s*;\s*\(function\(\)/);
  if (initialMatch) {
    try {
      blocks.push(JSON.parse(initialMatch[1]));
    } catch {
      // Ignore malformed embedded state.
    }
  }

  const nextMatch = html.match(/<script[^>]+id=["']__NEXT_DATA__["'][^>]*>([\s\S]*?)<\/script>/i);
  if (nextMatch) {
    try {
      blocks.push(JSON.parse(htmlDecode(nextMatch[1])));
    } catch {
      // Ignore malformed embedded state.
    }
  }

  return blocks;
}

function findBilibiliArchivesInObject(value, out = [], depth = 0) {
  if (!value || depth > 8) return out;
  if (Array.isArray(value)) {
    for (const item of value) {
      const archive = normalizeBilibiliArchiveItem(item);
      if (archive.bvid || archive.aid) out.push(archive);
      findBilibiliArchivesInObject(item, out, depth + 1);
    }
    return out;
  }
  if (typeof value !== "object") return out;
  for (const child of Object.values(value)) findBilibiliArchivesInObject(child, out, depth + 1);
  return out;
}

function findFirstStringField(value, names, depth = 0) {
  if (!value || depth > 5) return "";
  if (Array.isArray(value)) {
    for (const item of value) {
      const found = findFirstStringField(item, names, depth + 1);
      if (found) return found;
    }
    return "";
  }
  if (typeof value !== "object") return "";
  for (const name of names) {
    const text = value[name];
    if (typeof text === "string" && text.trim()) return text.trim();
  }
  for (const child of Object.values(value)) {
    const found = findFirstStringField(child, names, depth + 1);
    if (found) return found;
  }
  return "";
}

function parseLiveRoomId(url) {
  const fromQuery = normalizeAid(url.searchParams.get("room_id") || url.searchParams.get("roomid") || "");
  if (fromQuery) return String(fromQuery);

  const segments = url.pathname
    .split("/")
    .map((part) => part.trim())
    .filter(Boolean);

  for (const segment of segments) {
    const match = segment.match(/^(\d+)$/);
    if (match) return match[1];
  }

  return "";
}

async function expandB23Url(value) {
  const parsed = tryParseUrl(value);
  if (!parsed || !isBilibiliShortHost(parsed.hostname)) return value;

  return singleflightWithFailureCache(`short:${value}`, async () => {
    let current = value;
    for (let i = 0; i < 5; i++) {
      const response = await fetchWithTimeout(current, {
        method: "HEAD",
        redirect: "manual",
        headers: biliHeaders()
      }, BILI_FETCH_TIMEOUT_MS, "Bilibili short link");
      const location = response.headers.get("Location");
      if (!location) return current;
      current = new URL(location, current).toString();
      const currentHost = tryParseUrl(current)?.hostname || "";
      if (!isBilibiliShortHost(currentHost)) return current;
    }
    return current;
  });
}

function extractSupportedUrlFromText(value) {
  const text = String(value || "").trim();
  if (!text) return text;
  if (tryParseUrl(text)) return text;

  const urls = extractUrlCandidates(text);
  for (const rawUrl of urls) {
    const candidate = cleanSharedUrl(rawUrl);
    const parsed = tryParseFlexibleUrl(candidate);
    if (!parsed) continue;
    if (isSupportedSourceUrl(parsed)) return candidate;
  }

  return text;
}

function validateInputSource(value) {
  const text = decodeRepeatedly(value);
  const urls = extractUrlCandidates(text);
  const parsedWhole = tryParseFlexibleUrl(text);
  if (parsedWhole && !urls.some((candidate) => tryParseFlexibleUrl(candidate)?.href === parsedWhole.href)) {
    urls.unshift(text);
  }

  if (urls.length === 0) return { allowed: true };

  for (const rawUrl of urls) {
    const candidate = cleanSharedUrl(rawUrl);
    const parsed = tryParseFlexibleUrl(candidate);
    if (!parsed) return { allowed: false, error: "unsupported_url", host: "", path: "" };
    const result = validateSupportedUrl(parsed);
    if (!result.allowed) return result;
  }

  return { allowed: true };
}

function validateSupportedUrl(parsed, depth = 0) {
  const host = normalizeHost(parsed.hostname);
  const path = parsed.pathname || "/";
  if (depth > 4) return { allowed: false, error: "url_nested_too_deep", host, path };

  if (host === "biliplayer.91vrchat.com") {
    if (path.replace(/\/+$/, "") !== "/player" || !parsed.searchParams.has("url")) {
      return { allowed: false, error: "unsupported_player_url", host, path };
    }
    const inner = decodeRepeatedly(parsed.searchParams.get("url") || "");
    const innerParsed = tryParseFlexibleUrl(inner);
    if (!innerParsed && (normalizeBvid(inner) || extractAid(inner))) return { allowed: true };
    if (!innerParsed) return { allowed: false, error: "unsupported_player_inner_url", host, path };
    return validateSupportedUrl(innerParsed, depth + 1);
  }

  if (isBilibiliShortHost(host)) {
    return path.replace(/\/+$/, "").length > 1
      ? { allowed: true }
      : { allowed: false, error: "unsupported_bilibili_short_path", host, path };
  }

  if (host === "163cn.tv") {
    return path.replace(/\/+$/, "").length > 1
      ? { allowed: true }
      : { allowed: false, error: "unsupported_netease_short_path", host, path };
  }

  if (isAllowedBilibiliHost(host)) {
    return isAllowedBilibiliPath(parsed, host)
      ? { allowed: true }
      : { allowed: false, error: "unsupported_bilibili_path", host, path };
  }

  if (isNeteaseHost(host)) {
    return isAllowedNeteasePath(parsed, host)
      ? { allowed: true }
      : { allowed: false, error: "unsupported_netease_path", host, path };
  }

  return { allowed: false, error: "unsupported_url", host, path };
}

function extractUrlCandidates(value) {
  const text = String(value || "").trim();
  const candidates = text.match(/https?:\/\/[^\s"'<>]+/ig) || [];
  const schemelessPattern = /(?:^|[\s"'(<])((?:bilibili\.com|www\.bilibili\.com|m\.bilibili\.com|live\.bilibili\.com|space\.bilibili\.com|t\.bilibili\.com|b23\.tv|bili2233\.cn|biliplayer\.91vrchat\.com|music\.163\.com|y\.music\.163\.com|163cn\.tv)\/[^\s"'<>]*)/ig;
  let match;
  while ((match = schemelessPattern.exec(text)) !== null) {
    if (match[1]) candidates.push(match[1]);
  }
  return [...new Set(candidates.map(cleanSharedUrl).filter(Boolean))];
}

function cleanSharedUrl(value) {
  return String(value || "").trim().replace(/[)\]}>.,!?;:\u3002\uFF0C\uFF01\uFF1F\u3001\uFF1B\uFF1A]+$/u, "");
}

function cleanupSharedUrl(value) {
  return String(value || "").trim().replace(/[)\]}>，。！？、；：]+$/u, "");
}

function isSupportedSourceUrl(parsed) {
  return validateSupportedUrl(parsed).allowed;
}

function isBilibiliShortHost(value) {
  const host = normalizeHost(value);
  return host === "b23.tv" || host === "bili2233.cn";
}

function isAllowedBilibiliHost(value) {
  const host = normalizeHost(value);
  return host === "bilibili.com" ||
    host === "m.bilibili.com" ||
    host === "live.bilibili.com" ||
    host === "space.bilibili.com" ||
    host === "t.bilibili.com";
}

function isAllowedBilibiliPath(parsed, host) {
  const path = parsed.pathname.replace(/\/+$/, "") || "/";
  if (host === "live.bilibili.com") {
    return /^\/\d+/.test(path) || parsed.searchParams.has("room_id") || parsed.searchParams.has("roomid");
  }
  if (host === "space.bilibili.com" || host === "t.bilibili.com") {
    return path !== "/";
  }
  return /^\/video\/(?:BV[0-9A-Za-z]{10,}|av\d+)/i.test(path) ||
    /^\/bangumi\/play\//i.test(path) ||
    /^\/list\//i.test(path) ||
    /^\/medialist\//i.test(path) ||
    /^\/opus\//i.test(path);
}

function isAllowedNeteasePath(parsed, host) {
  const path = parsed.pathname.replace(/\/+$/, "") || "/";
  if (host === "y.music.163.com") {
    return /^\/m\/(?:song|playlist)$/i.test(path) && normalizeNumericId(parsed.searchParams.get("id") || "");
  }

  if (host !== "music.163.com") return false;

  if (/^\/(?:f\/)?(?:song|playlist)$/i.test(path) && normalizeNumericId(parsed.searchParams.get("id") || "")) {
    return true;
  }

  if (!parsed.hash) return false;
  try {
    const hashUrl = new URL(parsed.hash.replace(/^#\/?/, "/"), "https://music.163.com");
    return /^\/(?:song|playlist)$/i.test(hashUrl.pathname.replace(/\/+$/, "")) &&
      normalizeNumericId(hashUrl.searchParams.get("id") || "");
  } catch {
    return false;
  }
}

function unwrapKnownPlayerUrl(value) {
  let current = String(value || "").trim();
  for (let i = 0; i < 4; i++) {
    let parsed = tryParseUrl(current);
    if (!parsed) {
      current = decodeRepeatedly(current);
      parsed = tryParseUrl(current);
    }
    if (!parsed) return current;

    const host = parsed.hostname.toLowerCase();
    const isKnownPlayer = host === "biliplayer.91vrchat.com" ||
      (parsed.pathname.replace(/\/+$/, "") === "/player" && parsed.searchParams.has("url"));
    if (!isKnownPlayer) return current;

    const inner = parsed.searchParams.get("url");
    if (!inner) return current;
    current = decodeRepeatedly(inner);
  }
  return current;
}

async function getVideoView(input) {
  const key = input.bvid ? `bvid:${normalizeBvid(input.bvid)}` : `aid:${input.aid}`;
  return getCached(viewCache, `view:${key}`, VIEW_CACHE_TTL_MS, cacheStats.view, VIEW_CACHE_MAX_ENTRIES, async () => {
    const url = new URL("https://api.bilibili.com/x/web-interface/view");
    if (input.bvid) url.searchParams.set("bvid", normalizeBvid(input.bvid));
    else url.searchParams.set("aid", String(input.aid));

    const payload = await fetchJson(url);
    if (payload.code !== 0 || !payload.data || !Array.isArray(payload.data.pages) || payload.data.pages.length === 0) {
      throw new Error(`Bilibili view API failed: ${payload.message || payload.code}`);
    }
    return payload.data;
  });
}

function selectVideoPage(view, input) {
  const pages = Array.isArray(view.pages) ? view.pages : [];
  if (pages.length === 0) throw new Error("Bilibili view has no pages.");

  let selected = null;
  if (input.cid) {
    selected = pages.find((item) => Number(item.cid) === Number(input.cid)) || null;
  }

  if (!selected) {
    const requestedPage = Math.max(1, Number.parseInt(input.page || "1", 10) || 1);
    const pageIndex = Math.min(Math.max(requestedPage - 1, 0), pages.length - 1);
    selected = pages[pageIndex];
  }

  return {
    page: Number(selected.page || input.page || 1),
    cid: Number(selected.cid),
    part: selected.part || "",
    duration: Number(selected.duration || view.duration || 0)
  };
}

async function resolveVideoUrl(input) {
  const viewResult = await getVideoView(input);
  const view = viewResult.value;
  const selected = selectVideoPage(view, input);
  const key = `${view.bvid || input.bvid || `av${view.aid || input.aid}`}:${selected.cid}`;

  const cached = videoUrlCache.get(`video:${key}`);
  const result = await getCached(videoUrlCache, `video:${key}`, VIDEO_URL_CACHE_TTL_MS, cacheStats.video, VIDEO_URL_CACHE_MAX_ENTRIES, async () => {
    const videoUrl = await fetchMp4Durl({
      aid: view.aid || input.aid,
      bvid: view.bvid || input.bvid || "",
      cid: selected.cid
    });
    return {
      videoUrl,
      bvid: view.bvid || input.bvid || "",
      aid: view.aid || input.aid || 0,
      cid: selected.cid,
      page: selected.page,
      title: view.title || "",
      part: selected.part || ""
    };
  });
  return {
    ...result.value,
    cacheHit: Boolean(cached && Date.now() - cached.time < VIDEO_URL_CACHE_TTL_MS)
  };
}

async function fetchMp4Durl({ aid, bvid, cid }) {
  const qns = [80, 64, 32, 16];
  let lastMessage = "";
  for (const qn of qns) {
    const url = new URL("https://api.bilibili.com/x/player/playurl");
    url.searchParams.set("avid", String(aid));
    if (bvid) url.searchParams.set("bvid", bvid);
    url.searchParams.set("cid", String(cid));
    url.searchParams.set("qn", String(qn));
    url.searchParams.set("type", "mp4");
    url.searchParams.set("otype", "json");
    url.searchParams.set("fnver", "0");
    url.searchParams.set("fnval", "0");
    url.searchParams.set("fourk", "1");
    url.searchParams.set("platform", "html5");
    url.searchParams.set("high_quality", "1");

    const payload = await fetchJson(url);
    if (payload.code !== 0) {
      lastMessage = payload.message || String(payload.code);
      continue;
    }
    const durl = payload.data?.durl;
    if (Array.isArray(durl) && durl[0]?.url) return durl[0].url;
    lastMessage = "no durl in response";
  }
  throw new Error(`no mp4 durl for this video: ${lastMessage}`);
}

async function resolveLiveUrl(input) {
  const key = `live:${input.roomId}`;
  const cached = videoUrlCache.get(key);
  const result = await getCached(videoUrlCache, key, VIDEO_URL_CACHE_TTL_MS, cacheStats.video, VIDEO_URL_CACHE_MAX_ENTRIES, async () => {
    const room = await getLiveRoomInfo(input.roomId);
    if (!room || !room.room_id) {
      throw new Error("live room info not found");
    }

    if (room.live_status !== 1) {
      throw new Error("live room is not streaming");
    }

    const playInfo = await getLivePlayInfo(room.room_id, 10000);
    const direct = normalizeLivePlayResult(playInfo);
    if (!direct.directUrl) {
      throw new Error("live stream url not found");
    }

    return {
      videoUrl: direct.directUrl,
      backupUrls: direct.backupUrls,
      actualQuality: direct.actualQuality,
      roomId: input.roomId,
      realRoomId: room.room_id,
      title: room.title || ""
    };
  });

  return {
    ...result.value,
    cacheHit: Boolean(cached && Date.now() - cached.time < VIDEO_URL_CACHE_TTL_MS)
  };
}

async function getLiveRoomInfo(roomId) {
  const params = new URLSearchParams({ id: String(roomId) });
  const payload = await fetchJson(`https://api.live.bilibili.com/room/v1/Room/room_init?${params}`);
  if (payload.code !== 0 || !payload.data) {
    throw new Error(payload.message || payload.msg || "get live room info failed");
  }
  return payload.data;
}

async function getLivePlayInfo(realRoomId, quality) {
  const params = new URLSearchParams({
    room_id: String(realRoomId),
    protocol: "0,1",
    format: "1",
    codec: "0,1",
    qn: String(quality),
    platform: "h5",
    ptype: "8"
  });

  const payload = await fetchJson(`https://api.live.bilibili.com/xlive/web-room/v2/index/getRoomPlayInfo?${params}`);
  if (payload.code !== 0 || !payload.data) {
    throw new Error(payload.message || payload.msg || "get live stream failed");
  }
  return payload.data;
}

function normalizeLivePlayResult(data) {
  const streams = data?.playurl_info?.playurl?.stream;
  if (!Array.isArray(streams)) {
    return { directUrl: "", actualQuality: 0, backupUrls: [] };
  }

  const candidates = [];

  for (const stream of streams) {
    const protocolName = stream?.protocol_name || "";
    const formats = Array.isArray(stream?.format) ? stream.format : [];

    for (const format of formats) {
      const formatName = format?.format_name || "";
      const codecs = Array.isArray(format?.codec) ? format.codec : [];

      for (const codec of codecs) {
        const baseUrl = codec?.base_url || codec?.baseUrl || "";
        const urlInfos = Array.isArray(codec?.url_info) ? codec.url_info : [];
        const codecName = codec?.codec_name || "";
        const quality = readPositiveInt(codec?.current_qn, 0);

        for (const urlInfo of urlInfos) {
          const fullUrl = combineLiveUrl(urlInfo?.host || "", baseUrl, urlInfo?.extra || "");
          if (!fullUrl) continue;

          candidates.push({
            url: fullUrl,
            quality,
            score: liveCandidateScore(protocolName, formatName, codecName, quality)
          });
        }
      }
    }
  }

  candidates.sort((a, b) => b.score - a.score);
  const selected = candidates[0];

  return {
    directUrl: selected ? selected.url : "",
    actualQuality: selected ? selected.quality : 0,
    backupUrls: candidates.slice(1, 6).map((item) => item.url)
  };
}

function combineLiveUrl(host, baseUrl, extra) {
  if (!host || !baseUrl) return "";
  const normalizedHost = host.endsWith("/") ? host.slice(0, -1) : host;
  const normalizedBase = baseUrl.startsWith("/") ? baseUrl : "/" + baseUrl;
  return normalizedHost + normalizedBase + (extra || "");
}

function liveCandidateScore(protocolName, formatName, codecName, quality) {
  let score = readPositiveInt(quality, 0);

  if (protocolName === "http_hls") score += 100000;
  if (formatName === "fmp4") score += 20000;
  if (formatName === "ts") score += 10000;
  if (formatName === "flv") score += 1000;
  if (codecName === "avc") score += 500;
  if (codecName === "hevc") score += 100;

  return score;
}

async function getDanmakuTsv(input, options = {}) {
  const viewResult = await getVideoView(input);
  const view = viewResult.value;
  const selected = selectVideoPage(view, input);
  const key = options.manifestKey
    ? `dm:${selected.cid}:manifest:${options.manifestKey}:v:${options.vcrid || 0}`
    : `dm:${selected.cid}:pages`;

  return getCachedDanmaku(key, async () => {
    const pageRecords = registerVideoViewPages(view, input);
    return buildDanmakuTsv({
      bvid: view.bvid || input.bvid || "",
      aid: view.aid || input.aid || 0,
      cid: selected.cid,
      page: selected.page,
      title: view.title || "",
      part: selected.part || "",
      duration: selected.duration || 0,
      vcrid: options.vcrid || pageRecords.get(Number(selected.cid))?.vcrid || 0,
      pageItems: (Array.isArray(view.pages) ? view.pages : []).map((pageItem, index) => {
        const record = pageRecords.get(Number(pageItem.cid));
        return {
          page: Number(pageItem.page || index + 1),
          cid: Number(pageItem.cid || 0),
          duration: Number(pageItem.duration || view.duration || 0),
          part: pageItem.part || "",
          vcrid: record?.vcrid || 0
        };
      }),
      listManifest: options.manifestKey ? vcrIdStore.getManifest(options.manifestKey) : null
    });
  });
}

async function buildDanmakuTsv(video) {
  let all = await fetchXmlDanmaku(video.cid).catch(() => []);

  if (all.length === 0) {
    const segmentCount = Math.max(1, Math.ceil(Number(video.duration || 0) / 360));
    all = [];
    for (let segment = 1; segment <= segmentCount; segment++) {
      const rows = await fetchDanmakuSegment({
        aid: video.aid,
        cid: video.cid,
        segment
      });
      all.push(...rows);
    }
  }

  all.sort((a, b) => a.progress - b.progress || a.id - b.id);

  const header = [
    "#YBDM/1",
    `#manifest_type=${video.listManifest ? "bilibili-list" : "bilibili-pages"}`,
    `#vcrid=${video.vcrid || ""}`,
    `#vcrid_max=${VCRID_MAX}`,
    `#bvid=${escapeField(video.bvid || "")}`,
    `#aid=${video.aid || ""}`,
    `#cid=${video.cid}`,
    `#page=${video.page}`,
    `#title=${escapeField(video.title || "")}`,
    `#part=${escapeField(video.part || "")}`,
    `#count=${all.length}`,
    "#columns=progressMs\tmode\tcolor\tfontSize\tpool\tcontent"
  ];
  if (video.listManifest) {
    header.push(`#source_type=${escapeField(video.listManifest.sourceType || "")}`);
    header.push(`#pages_count=${Array.isArray(video.listManifest.items) ? video.listManifest.items.length : 0}`);
    for (const item of video.listManifest.items || []) {
      header.push([
        `#list_item=${item.index || 1}`,
        item.aid || 0,
        escapeField(item.bvid || ""),
        item.cid || 0,
        item.duration || 0,
        escapeField(item.title || item.part || ""),
        item.vcrid || 0
      ].join("\t"));
    }
  } else {
    for (const item of video.pageItems || []) {
      header.push([
        `#page_item=${item.page}`,
        item.cid || 0,
        item.duration || 0,
        escapeField(item.part || ""),
        item.vcrid || 0
      ].join("\t"));
    }
  }
  const body = all.map((item) => [
    item.progress,
    item.mode,
    item.color,
    item.fontsize,
    item.pool,
    escapeField(item.content)
  ].join("\t"));
  return `${header.join("\n")}\n${body.join("\n")}\n`;
}

async function fetchVideoView(input) {
  return (await getVideoView(input)).value;
}

async function fetchXmlDanmaku(cid) {
  const url = new URL("https://api.bilibili.com/x/v1/dm/list.so");
  url.searchParams.set("oid", String(cid));
  const response = await fetchWithTimeout(url, { headers: biliHeaders() }, BILI_FETCH_TIMEOUT_MS, "Bilibili XML danmaku");
  if (!response.ok) {
    stats.upstreamErrors++;
    markStatsDirty();
    throw new Error(`Bilibili XML danmaku failed with HTTP ${response.status}.`);
  }

  const xml = await response.text();
  const rows = [];
  const itemPattern = /<d\s+p="([^"]*)">([\s\S]*?)<\/d>/g;
  let match;
  let id = 0;
  while ((match = itemPattern.exec(xml)) !== null) {
    const attrs = match[1].split(",");
    if (attrs.length < 8) continue;
    rows.push({
      id: id++,
      progress: Math.round(Number.parseFloat(attrs[0] || "0") * 1000),
      mode: Number.parseInt(attrs[1] || "1", 10),
      fontsize: Number.parseInt(attrs[2] || "25", 10),
      color: Number.parseInt(attrs[3] || "16777215", 10),
      pool: Number.parseInt(attrs[5] || "0", 10),
      content: htmlDecode(match[2])
    });
  }
  return rows.filter((item) => item.content);
}

async function fetchDanmakuSegment({ aid, cid, segment }) {
  const url = new URL("https://api.bilibili.com/x/v2/dm/web/seg.so");
  url.searchParams.set("type", "1");
  url.searchParams.set("oid", String(cid));
  url.searchParams.set("pid", String(aid));
  url.searchParams.set("segment_index", String(segment));
  const response = await fetchWithTimeout(url, { headers: biliHeaders() }, BILI_FETCH_TIMEOUT_MS, `Bilibili danmaku segment ${segment}`);
  if (!response.ok) {
    stats.upstreamErrors++;
    markStatsDirty();
    throw new Error(`Bilibili danmaku segment ${segment} failed with HTTP ${response.status}.`);
  }
  return decodeDmSegMobileReply(new Uint8Array(await response.arrayBuffer()));
}

async function fetchJson(url) {
  const response = await fetchWithTimeout(url, { headers: biliHeaders() }, BILI_FETCH_TIMEOUT_MS, "Bilibili API");
  const text = await response.text();
  if (!response.ok) {
    stats.upstreamErrors++;
    markStatsDirty();
    throw new Error(`Bilibili API failed with HTTP ${response.status}.`);
  }
  return JSON.parse(text);
}

function biliHeaders() {
  const headers = {
    "User-Agent": USER_AGENT,
    "Referer": "https://www.bilibili.com/",
    "Accept": "*/*"
  };
  if (BILIBILI_COOKIE) headers.Cookie = BILIBILI_COOKIE;
  return headers;
}

async function getCached(map, key, ttlMs, statBucket, maxEntries, loader) {
  const cached = map.get(key);
  if (cached && Date.now() - cached.time < ttlMs) {
    statBucket.hits++;
    return { value: cached.value, cacheHit: true };
  }
  if (cached) map.delete(key);
  throwCachedFailure(key);

  if (inflight.has(key)) {
    cacheStats.inflightHits++;
    return { value: await inflight.get(key), cacheHit: true };
  }

  statBucket.misses++;
  const promise = loadWithFailureCache(key, loader).then((value) => {
    setCacheEntry(map, key, value, maxEntries);
    return value;
  }).finally(() => inflight.delete(key));
  inflight.set(key, promise);
  return { value: await promise, cacheHit: false };
}

async function getCachedDanmaku(key, loader) {
  const now = Date.now();
  const memoryEntry = getValidDanmakuMemoryEntry(key, now);
  if (memoryEntry) {
    cacheStats.danmaku.hits++;
    refreshDanmakuEntry(key, memoryEntry, now);
    return memoryEntry.value;
  }

  if (inflight.has(key)) {
    cacheStats.inflightHits++;
    return inflight.get(key);
  }

  const diskEntry = readDanmakuDiskEntry(key, now);
  if (diskEntry) {
    cacheStats.danmaku.hits++;
    setDanmakuMemoryEntry(key, diskEntry);
    refreshDanmakuEntry(key, diskEntry, now);
    return diskEntry.value;
  }
  throwCachedFailure(key);

  cacheStats.danmaku.misses++;
  const promise = loadWithFailureCache(key, loader).then((value) => {
    const createdAt = Date.now();
    const entry = {
      key,
      value,
      time: createdAt,
      createdAt,
      lastAccessAt: createdAt,
      expiresAt: createdAt + DANMAKU_DISK_CACHE_INITIAL_TTL_MS
    };
    setDanmakuMemoryEntry(key, entry);
    writeDanmakuDiskEntry(entry);
    trimDanmakuDiskCache();
    return value;
  }).finally(() => inflight.delete(key));
  inflight.set(key, promise);
  return promise;
}

function getValidDanmakuMemoryEntry(key, now) {
  const entry = danmakuCache.get(key);
  if (!entry) return null;
  if (!entry.expiresAt) entry.expiresAt = Number(entry.time || 0) + DANMAKU_CACHE_TTL_MS;
  if (now >= entry.expiresAt) {
    danmakuCache.delete(key);
    deleteDanmakuDiskEntry(key);
    return null;
  }
  return entry;
}

function setDanmakuMemoryEntry(key, entry) {
  if (danmakuCache.has(key)) danmakuCache.delete(key);
  danmakuCache.set(key, entry);
  trimCache(danmakuCache, DANMAKU_CACHE_MAX_ENTRIES);
}

function refreshDanmakuEntry(key, entry, now) {
  entry.lastAccessAt = now;
  const nextExpiresAt = now + DANMAKU_DISK_CACHE_REFRESH_MS;
  const shouldWrite = !entry.expiresAt || entry.expiresAt < nextExpiresAt;
  if (shouldWrite) entry.expiresAt = nextExpiresAt;
  if (danmakuCache.has(key)) {
    danmakuCache.delete(key);
    danmakuCache.set(key, entry);
    trimCache(danmakuCache, DANMAKU_CACHE_MAX_ENTRIES);
  }
  if (shouldWrite) writeDanmakuDiskEntry(entry);
}

function readDanmakuDiskEntry(key, now) {
  const filePath = danmakuDiskPath(key);
  if (!existsSync(filePath)) return null;
  const entry = readDanmakuDiskEntryFromPath(filePath, now);
  if (entry && entry.key === key) return entry;
  deleteDanmakuDiskEntry(key);
  return null;
}

function readDanmakuDiskEntryFromPath(filePath, now) {
  try {
    const entry = JSON.parse(readFileSync(filePath, "utf8").replace(/^\uFEFF/, ""));
    if (!entry?.key || typeof entry.value !== "string" || !Number.isFinite(Number(entry.expiresAt))) {
      unlinkSync(filePath);
      return null;
    }
    if (now >= Number(entry.expiresAt)) {
      unlinkSync(filePath);
      return null;
    }
    return {
      key: String(entry.key),
      value: entry.value,
      time: Number(entry.time || entry.createdAt || now),
      createdAt: Number(entry.createdAt || entry.time || now),
      lastAccessAt: Number(entry.lastAccessAt || entry.time || now),
      expiresAt: Number(entry.expiresAt)
    };
  } catch {
    try {
      unlinkSync(filePath);
    } catch {
      // Best-effort cleanup.
    }
    return null;
  }
}

function writeDanmakuDiskEntry(entry) {
  try {
    mkdirSync(DANMAKU_DISK_CACHE_DIR, { recursive: true });
    const filePath = danmakuDiskPath(entry.key);
    const tmpFile = `${filePath}.tmp`;
    writeFileSync(tmpFile, `${JSON.stringify({
      key: entry.key,
      time: entry.time,
      createdAt: entry.createdAt,
      lastAccessAt: entry.lastAccessAt,
      expiresAt: entry.expiresAt,
      value: entry.value
    })}\n`, "utf8");
    renameSync(tmpFile, filePath);
  } catch (error) {
    console.warn(`[danmaku-cache] write failed: ${error.message}`);
  }
}

function deleteDanmakuDiskEntry(key) {
  try {
    const filePath = danmakuDiskPath(key);
    if (existsSync(filePath)) unlinkSync(filePath);
  } catch {
    // Best-effort cache cleanup.
  }
}

function trimDanmakuDiskCache() {
  const limit = Math.max(0, Number(DANMAKU_CACHE_MAX_ENTRIES || 0));
  if (!limit || !existsSync(DANMAKU_DISK_CACHE_DIR)) return;
  try {
    const now = Date.now();
    const entries = readdirSync(DANMAKU_DISK_CACHE_DIR)
      .filter((name) => name.endsWith(".json"))
      .map((name) => {
        const filePath = join(DANMAKU_DISK_CACHE_DIR, name);
        try {
          const entry = JSON.parse(readFileSync(filePath, "utf8").replace(/^\uFEFF/, ""));
          if (!entry?.key || now >= Number(entry.expiresAt || 0)) {
            unlinkSync(filePath);
            return null;
          }
          return { filePath, lastAccessAt: Number(entry.lastAccessAt || entry.time || 0) };
        } catch {
          unlinkSync(filePath);
          return null;
        }
      })
      .filter(Boolean)
      .sort((left, right) => left.lastAccessAt - right.lastAccessAt);
    while (entries.length > limit) {
      const oldest = entries.shift();
      if (oldest) unlinkSync(oldest.filePath);
    }
  } catch (error) {
    console.warn(`[danmaku-cache] trim failed: ${error.message}`);
  }
}

function danmakuDiskPath(key) {
  return join(DANMAKU_DISK_CACHE_DIR, `${safeCacheFileName(key)}.json`);
}

function safeCacheFileName(value) {
  return String(value || "").replace(/[^0-9A-Za-z_.-]/g, "_").slice(0, 120);
}

function setCacheEntry(map, key, value, maxEntries) {
  if (map.has(key)) map.delete(key);
  map.set(key, { time: Date.now(), value });
  trimCache(map, maxEntries);
}

function trimCache(map, maxEntries) {
  const limit = Math.max(0, Number(maxEntries || 0));
  if (!limit) return;
  while (map.size > limit) {
    const oldestKey = map.keys().next().value;
    if (oldestKey === undefined) break;
    map.delete(oldestKey);
  }
}

function pruneExpiredCache(map, ttlMs) {
  const now = Date.now();
  for (const [key, entry] of map.entries()) {
    const expiresAt = Number(entry?.expiresAt || 0);
    if (!entry || (expiresAt ? now >= expiresAt : now - entry.time >= ttlMs)) {
      map.delete(key);
      if (map === danmakuCache) deleteDanmakuDiskEntry(key);
    }
  }
}

function pruneAllExpiredCaches() {
  pruneExpiredCache(viewCache, VIEW_CACHE_TTL_MS);
  pruneExpiredCache(videoUrlCache, VIDEO_URL_CACHE_TTL_MS);
  syncDanmakuMemoryFromDisk();
  pruneExpiredCache(danmakuCache, DANMAKU_CACHE_TTL_MS);
  trimDanmakuDiskCache();
  pruneExpiredCache(neteasePlaylistCache, NETEASE_PLAYLIST_CACHE_TTL_MS);
  pruneExpiredCache(neteaseUrlCache, NETEASE_URL_CACHE_TTL_MS);
  pruneExpiredCache(neteaseLyricsCache, NETEASE_LYRICS_CACHE_TTL_MS);
  pruneFailureCache();
}

function syncDanmakuMemoryFromDisk() {
  if (!existsSync(DANMAKU_DISK_CACHE_DIR)) return;
  const now = Date.now();
  try {
    const entries = readdirSync(DANMAKU_DISK_CACHE_DIR)
      .filter((name) => name.endsWith(".json"))
      .map((name) => readDanmakuDiskEntryFromPath(join(DANMAKU_DISK_CACHE_DIR, name), now))
      .filter(Boolean)
      .sort((left, right) => right.lastAccessAt - left.lastAccessAt)
      .slice(0, DANMAKU_CACHE_MAX_ENTRIES);
    for (const entry of entries.reverse()) {
      if (!danmakuCache.has(entry.key)) setDanmakuMemoryEntry(entry.key, entry);
    }
  } catch (error) {
    console.warn(`[danmaku-cache] sync failed: ${error.message}`);
  }
}

async function fetchWithTimeout(url, options = {}, timeoutMs = BILI_FETCH_TIMEOUT_MS, label = "upstream") {
  try {
    return await fetch(url, {
      ...options,
      signal: options.signal || AbortSignal.timeout(timeoutMs)
    });
  } catch (error) {
    if (isTimeoutError(error)) {
      stats.upstreamTimeouts++;
      markStatsDirty();
      throw new Error(`${label} timed out after ${Math.ceil(timeoutMs / 1000)}s.`);
    }
    stats.upstreamErrors++;
    markStatsDirty();
    throw error;
  }
}

function isTimeoutError(error) {
  return error?.name === "AbortError" ||
    error?.name === "TimeoutError" ||
    /timed out|timeout/i.test(String(error?.message || ""));
}

async function loadWithFailureCache(key, loader) {
  throwCachedFailure(key);
  try {
    return await loader();
  } catch (error) {
    setFailureCacheEntry(key, error);
    throw error;
  }
}

function throwCachedFailure(key) {
  const cached = failureCache.get(key);
  if (!cached) return;
  const now = Date.now();
  if (now >= cached.expiresAt) {
    failureCache.delete(key);
    return;
  }
  stats.failureCacheHits++;
  markStatsDirty();
  const error = new Error(cached.message);
  error.cachedFailure = true;
  error.originalName = cached.name;
  throw error;
}

function setFailureCacheEntry(key, error) {
  if (!FAILURE_CACHE_TTL_MS) return;
  const message = String(error?.message || error || "upstream failed").slice(0, 500);
  const entry = {
    key,
    name: String(error?.name || "Error"),
    message,
    time: Date.now(),
    expiresAt: Date.now() + FAILURE_CACHE_TTL_MS
  };
  if (failureCache.has(key)) failureCache.delete(key);
  failureCache.set(key, entry);
  trimCache(failureCache, FAILURE_CACHE_MAX_ENTRIES);
  stats.failureCacheWrites++;
  markStatsDirty();
}

function pruneFailureCache(now = Date.now()) {
  for (const [key, entry] of failureCache.entries()) {
    if (!entry || now >= Number(entry.expiresAt || 0)) failureCache.delete(key);
  }
}

function failureCacheEntries(now = Date.now()) {
  pruneFailureCache(now);
  return [...failureCache.entries()].map(([key, entry]) => ({
    key,
    message: entry.message,
    ageSeconds: Math.floor((now - entry.time) / 1000),
    expiresInSeconds: Math.max(0, Math.floor((entry.expiresAt - now) / 1000))
  }));
}

async function singleflightWithFailureCache(key, loader) {
  throwCachedFailure(key);
  return singleflight(key, () => loadWithFailureCache(key, loader));
}

async function singleflight(key, loader) {
  if (inflight.has(key)) {
    cacheStats.inflightHits++;
    return inflight.get(key);
  }
  const promise = loader().finally(() => inflight.delete(key));
  inflight.set(key, promise);
  return promise;
}

function isDanmakuRequest(req, requestUrl) {
  if (requestUrl.searchParams.get("__dm") === "1") return true;
  const accept = String(req.headers.accept || "").toLowerCase();
  const userAgent = String(req.headers["user-agent"] || "").toLowerCase();
  const requestedWith = String(req.headers["x-requested-with"] || "").toLowerCase();
  const secFetchDest = String(req.headers["sec-fetch-dest"] || "").toLowerCase();
  const range = String(req.headers.range || "").toLowerCase();
  if (accept.includes("text/plain")) return true;
  if (userAgent.includes("vrcstringdownloader")) return true;
  if (userAgent.includes("vrcstring")) return true;
  if (requestedWith.includes("vrcstringdownloader")) return true;
  if (userAgent.includes("unityplayer") && accept.includes("*/*") && !range) return true;
  if (secFetchDest === "empty" && accept.includes("*/*") && userAgent.includes("unity")) return true;
  return false;
}

function decodeDmSegMobileReply(bytes) {
  const out = [];
  const reader = new ProtoReader(bytes);
  while (!reader.eof()) {
    const tag = reader.varint();
    const field = tag >> 3;
    const wire = tag & 7;
    if (wire === 4 || wire > 5) break;
    if (field === 1 && wire === 2) {
      const elemBytes = reader.bytes();
      try {
        out.push(decodeDanmakuElem(elemBytes));
      } catch {
        // Keep the segment usable when one element has an unknown edge case.
      }
    } else {
      reader.skip(wire);
    }
  }
  return out.filter((item) => item.content);
}

function decodeDanmakuElem(bytes) {
  const item = { id: 0, progress: 0, mode: 1, fontsize: 25, color: 16777215, pool: 0, content: "" };
  const reader = new ProtoReader(bytes);
  while (!reader.eof()) {
    const tag = reader.varint();
    const field = tag >> 3;
    const wire = tag & 7;
    if (wire === 4 || wire > 5) break;
    switch (field) {
      case 1:
        if (wire === 0) item.id = Number(reader.varint());
        else reader.skip(wire);
        break;
      case 2:
        if (wire === 0) item.progress = Number(reader.varint());
        else reader.skip(wire);
        break;
      case 3:
        if (wire === 0) item.mode = Number(reader.varint());
        else reader.skip(wire);
        break;
      case 4:
        if (wire === 0) item.fontsize = Number(reader.varint());
        else reader.skip(wire);
        break;
      case 5:
        if (wire === 0) item.color = Number(reader.varint());
        else reader.skip(wire);
        break;
      case 7:
        if (wire === 2) item.content = reader.string();
        else reader.skip(wire);
        break;
      case 11:
        if (wire === 0) item.pool = Number(reader.varint());
        else reader.skip(wire);
        break;
      default:
        reader.skip(wire);
        break;
    }
  }
  return item;
}

class ProtoReader {
  constructor(bytes) {
    this.bytesArray = bytes;
    this.pos = 0;
  }

  eof() {
    return this.pos >= this.bytesArray.length;
  }

  varint() {
    let shift = 0;
    let result = 0;
    while (this.pos < this.bytesArray.length) {
      const byte = this.bytesArray[this.pos++];
      result += (byte & 0x7f) * 2 ** shift;
      if ((byte & 0x80) === 0) return result;
      shift += 7;
    }
    throw new Error("Unexpected end of protobuf varint.");
  }

  bytes() {
    const length = Number(this.varint());
    const end = this.pos + length;
    if (end > this.bytesArray.length) throw new Error("Unexpected end of protobuf bytes field.");
    const value = this.bytesArray.slice(this.pos, end);
    this.pos = end;
    return value;
  }

  string() {
    return new TextDecoder("utf-8").decode(this.bytes());
  }

  skip(wire) {
    switch (wire) {
      case 0:
        this.varint();
        break;
      case 1:
        this.pos += 8;
        break;
      case 2:
        this.pos += Number(this.varint());
        break;
      case 3:
        this.skipGroup();
        break;
      case 4:
        break;
      case 5:
        this.pos += 4;
        break;
      default:
        this.pos = this.bytesArray.length;
        break;
    }
    if (this.pos > this.bytesArray.length) throw new Error("Unexpected end while skipping protobuf field.");
  }

  skipGroup() {
    while (!this.eof()) {
      const tag = this.varint();
      const wire = tag & 7;
      if (wire === 4) return;
      this.skip(wire);
    }
  }
}

function buildCacheStats() {
  pruneAllExpiredCaches();
  const now = Date.now();
  return {
    caches: {
      viewEntries: viewCache.size,
      videoUrlEntries: videoUrlCache.size,
      danmakuEntries: danmakuCache.size,
      neteaseLyricsEntries: neteaseLyricsCache.size,
      failureEntries: failureCache.size,
      vcridEntries: vcrIdStore ? Object.keys(vcrIdStore.data.records || {}).length : 0,
      vcridManifests: vcrIdStore ? Object.keys(vcrIdStore.data.manifests || {}).length : 0,
      vcridStoreAvailable: Boolean(vcrIdStore?.available),
      vcridUsageRatio: vcrIdStore ? vcrIdStore.usageRatio() : 0,
      vcridLastGc: vcrIdStore ? vcrIdStore.lastGc : null,
      vcridStoreFile: VCRID_STORE_FILE,
      historyEntries: historyStore ? Object.keys(historyStore.data.records || {}).length : 0,
      historyStoreAvailable: Boolean(historyStore?.available),
      historyStoreFile: HISTORY_STORE_FILE,
      rateLimitBuckets: rateLimitBuckets.size
    },
    hits: {
      view: cacheStats.view.hits,
      video: cacheStats.video.hits,
      danmaku: cacheStats.danmaku.hits,
      inflight: cacheStats.inflightHits
    },
    misses: {
      view: cacheStats.view.misses,
      video: cacheStats.video.misses,
      danmaku: cacheStats.danmaku.misses
    },
    inflight: inflight.size,
    ttlSeconds: {
      view: Math.floor(VIEW_CACHE_TTL_MS / 1000),
      videoUrl: Math.floor(VIDEO_URL_CACHE_TTL_MS / 1000),
      failure: Math.floor(FAILURE_CACHE_TTL_MS / 1000),
      danmakuMemoryFallback: Math.floor(DANMAKU_CACHE_TTL_MS / 1000),
      danmakuDiskInitial: Math.floor(DANMAKU_DISK_CACHE_INITIAL_TTL_MS / 1000),
      danmakuDiskRefresh: Math.floor(DANMAKU_DISK_CACHE_REFRESH_MS / 1000),
      neteaseLyrics: Math.floor(NETEASE_LYRICS_CACHE_TTL_MS / 1000)
    },
    limits: {
      view: VIEW_CACHE_MAX_ENTRIES,
      videoUrl: VIDEO_URL_CACHE_MAX_ENTRIES,
      failure: FAILURE_CACHE_MAX_ENTRIES,
      danmaku: DANMAKU_CACHE_MAX_ENTRIES,
      neteaseLyrics: NETEASE_LYRICS_CACHE_MAX_ENTRIES,
      vcridMax: VCRID_MAX,
      vcridGc: {
        enabled: VCRID_GC_ENABLED,
        usageThreshold: VCRID_GC_USAGE_THRESHOLD,
        targetUsage: VCRID_GC_TARGET_USAGE,
        minUnusedDays: VCRID_GC_MIN_UNUSED_DAYS
      },
      dashboardHistoryPageSize: DASHBOARD_HISTORY_PAGE_SIZE,
      dashboardDanmaku: DASHBOARD_DANMAKU_MAX_ENTRIES,
      danmakuDiskDir: DANMAKU_DISK_CACHE_DIR
    },
    requests: {
      playerRedirects: stats.playerRedirects,
      liveRedirects: stats.liveRedirects,
      neteaseRedirects: stats.neteaseRedirects,
      neteaseSongRedirects: stats.neteaseSongRedirects,
      neteasePlaylistRedirects: stats.neteasePlaylistRedirects,
      playerDanmakuRequests: stats.playerDanmakuRequests,
      apiDanmakuRequests: stats.apiDanmakuRequests,
      resolveRequests: stats.resolveRequests,
      apiPagesRequests: stats.apiPagesRequests,
      apiLyricsRequests: stats.apiLyricsRequests,
      bilibiliPagesManifestRequests: stats.bilibiliPagesManifestRequests,
      bilibiliListManifestRequests: stats.bilibiliListManifestRequests,
      neteasePlaylistManifestRequests: stats.neteasePlaylistManifestRequests,
      neteaseSongManifestRequests: stats.neteaseSongManifestRequests,
      vcridGcRuns: stats.vcridGcRuns,
      vcridGcDeleted: stats.vcridGcDeleted,
      unsupportedUrlRejected: stats.unsupportedUrlRejected,
      legacyRejected: stats.legacyRejected,
      rateLimited: stats.rateLimited,
      failureCacheHits: stats.failureCacheHits,
      failureCacheWrites: stats.failureCacheWrites,
      upstreamTimeouts: stats.upstreamTimeouts,
      upstreamErrors: stats.upstreamErrors
    },
    rateLimit: {
      windowSeconds: Math.floor(RATE_LIMIT_WINDOW_MS / 1000),
      cooldownSeconds: Math.floor(RATE_LIMIT_COOLDOWN_MS / 1000),
      policies: {
        general: RATE_LIMIT_GENERAL,
        home: RATE_LIMIT_HOME,
        player: RATE_LIMIT_PLAYER,
        apiDanmaku: RATE_LIMIT_API_DANMAKU,
        apiResolve: RATE_LIMIT_API_RESOLVE,
        apiPages: RATE_LIMIT_API_PAGES,
        apiLyrics: RATE_LIMIT_API_LYRICS,
        cacheStats: RATE_LIMIT_CACHE_STATS
      }
    },
    failureEntries: failureCacheEntries(now),
    danmakuEntries: [...danmakuCache.entries()].map(([key, entry]) => {
      const text = entry.value || "";
      return {
        key,
        bvid: headerValue(text, "bvid"),
        aid: Number(headerValue(text, "aid") || 0),
        cid: Number(headerValue(text, "cid") || 0),
        page: Number(headerValue(text, "page") || 1),
        title: headerValue(text, "title"),
        part: headerValue(text, "part"),
        count: getDanmakuCount(text),
        ageSeconds: Math.floor((now - entry.time) / 1000),
        expiresInSeconds: Math.max(0, Math.floor((Number(entry.expiresAt || 0) - now) / 1000)),
        lastAccessAgeSeconds: Math.floor((now - Number(entry.lastAccessAt || entry.time)) / 1000)
      };
    })
  };
}

function buildBilibiliVideoSummaries(limit = 12) {
  const groups = new Map();
  const cachedTitles = cachedBilibiliTitlesByBvid();
  const ensureGroup = ({ bvid, aid }) => {
    const normalizedBvid = normalizeBvid(bvid);
    const normalizedAid = normalizeAid(aid);
    const key = normalizedBvid || (normalizedAid ? `av${normalizedAid}` : "");
    if (!key) return null;
    let group = groups.get(key);
    if (!group) {
      group = {
        key,
        bvid: normalizedBvid,
        aid: normalizedAid,
        title: "",
        firstPart: "",
        pages: new Set(),
        playedPages: new Set(),
        danmakuPages: new Set(),
        vcrids: 0,
        playCount: 0,
        danmakuRows: 0,
        lastAccessAt: 0,
        lastPlayedAt: 0,
        lastDanmakuAt: 0
      };
      groups.set(key, group);
    }
    return group;
  };

  const records = Object.values(vcrIdStore?.data?.records || {});
  for (const record of records) {
    if (!record || record.provider !== "bilibili") continue;
    const group = ensureGroup({ bvid: record.bvid, aid: record.aid });
    if (!group) continue;
    if (!group.title) group.title = String(record.title || cachedTitles.get(group.bvid) || "").trim();
    if (!group.firstPart && record.part) group.firstPart = String(record.part || "").trim();
    const pageKey = record.cid ? `cid:${record.cid}` : `page:${Math.max(1, Number(record.page || 1))}`;
    group.pages.add(pageKey);
    group.vcrids++;
    group.playCount += Number(record.playCount || 0);
    if (Number(record.playCount || 0) > 0 || Number(record.lastPlayedAt || 0) > 0) {
      group.playedPages.add(pageKey);
    }
    group.lastAccessAt = Math.max(group.lastAccessAt, Number(record.lastAccessAt || record.updatedAt || record.createdAt || 0));
    group.lastPlayedAt = Math.max(group.lastPlayedAt, Number(record.lastPlayedAt || 0));
  }

  for (const entry of danmakuCache.values()) {
    const text = entry?.value || "";
    const bvid = normalizeBvid(headerValue(text, "bvid"));
    const aid = normalizeAid(headerValue(text, "aid"));
    const group = ensureGroup({ bvid, aid });
    if (!group) continue;
    const cid = normalizeAid(headerValue(text, "cid"));
    const page = readPositiveInt(headerValue(text, "page"), 1);
    const pageKey = cid ? `cid:${cid}` : `page:${page}`;
    group.danmakuPages.add(pageKey);
    if (!group.title) group.title = headerValue(text, "title");
    if (!group.firstPart) group.firstPart = headerValue(text, "part");
    group.danmakuRows += getDanmakuCount(text);
    group.lastDanmakuAt = Math.max(group.lastDanmakuAt, Number(entry.lastAccessAt || entry.time || 0));
    group.lastAccessAt = Math.max(group.lastAccessAt, Number(entry.lastAccessAt || entry.time || 0));
  }

  const items = [...groups.values()]
    .map((group) => ({
      key: group.key,
      bvid: group.bvid,
      aid: group.aid,
      title: group.title || (group.pages.size === 1 ? group.firstPart : "") || group.bvid || `av${group.aid}`,
      subtitle: group.bvid || `av${group.aid}`,
      pagesCount: group.pages.size,
      playedPagesCount: group.playedPages.size,
      danmakuPagesCount: group.danmakuPages.size,
      vcrids: group.vcrids,
      playCount: group.playCount,
      danmakuRows: group.danmakuRows,
      lastAccessAt: group.lastAccessAt,
      lastPlayedAt: group.lastPlayedAt,
      lastDanmakuAt: group.lastDanmakuAt,
      url: group.bvid
        ? `https://www.bilibili.com/video/${group.bvid}/`
        : `https://www.bilibili.com/video/av${group.aid}/`
    }))
    .sort((left, right) => (right.lastPlayedAt || right.lastAccessAt) - (left.lastPlayedAt || left.lastAccessAt));

  return {
    total: items.length,
    hidden: Math.max(0, items.length - limit),
    items: items.slice(0, limit)
  };
}

function cachedBilibiliTitlesByBvid() {
  const titles = new Map();
  for (const entry of danmakuCache.values()) {
    const text = entry?.value || "";
    const bvid = normalizeBvid(headerValue(text, "bvid"));
    const title = headerValue(text, "title");
    if (bvid && title && !titles.has(bvid)) titles.set(bvid, title);
  }
  return titles;
}

async function backfillDashboardBilibiliTitles(limit = 4) {
  if (!vcrIdStore?.available) return;
  pruneAllExpiredCaches();
  const cachedTitles = cachedBilibiliTitlesByBvid();
  const candidates = [];
  const seen = new Set();
  for (const record of Object.values(vcrIdStore.data.records || {})) {
    if (!record || record.provider !== "bilibili") continue;
    const bvid = normalizeBvid(record.bvid);
    const aid = normalizeAid(record.aid);
    const key = bvid || (aid ? `av${aid}` : "");
    if (!key || seen.has(key)) continue;
    if (String(record.title || cachedTitles.get(bvid) || "").trim()) continue;
    seen.add(key);
    candidates.push({
      bvid,
      aid,
      lastAccessAt: Number(record.lastAccessAt || record.updatedAt || record.createdAt || 0)
    });
  }

  for (const item of candidates
    .sort((left, right) => right.lastAccessAt - left.lastAccessAt)
    .slice(0, limit)) {
    try {
      const view = (await getVideoView({
        bvid: item.bvid,
        aid: item.aid,
        normalizedUrl: item.bvid
          ? `https://www.bilibili.com/video/${item.bvid}/`
          : `https://www.bilibili.com/video/av${item.aid}/`
      })).value;
      const title = String(view.title || "").trim();
      if (!title) continue;
      let changed = false;
      for (const record of Object.values(vcrIdStore.data.records || {})) {
        const sameBvid = item.bvid && normalizeBvid(record.bvid) === item.bvid;
        const sameAid = item.aid && normalizeAid(record.aid) === item.aid;
        if (record?.provider === "bilibili" && (sameBvid || sameAid) && !String(record.title || "").trim()) {
          record.title = title;
          record.updatedAt = Date.now();
          changed = true;
        }
      }
      if (changed) vcrIdStore.save();
    } catch (error) {
      console.warn(`[dashboard] failed to backfill title for ${item.bvid || `av${item.aid}`}: ${error.message || error}`);
    }
  }
}

function buildHistoryDashboard(requestUrl) {
  const page = readPositiveInt(requestUrl?.searchParams?.get("historyPage"), 1);
  const pageSize = readPositiveInt(requestUrl?.searchParams?.get("historyPageSize"), DASHBOARD_HISTORY_PAGE_SIZE);
  const history = historyStore?.list({ page, pageSize }) || { page: 1, pageSize, total: 0, totalPages: 1, items: [] };
  const cards = history.items.map((item) => renderHistoryCard(item)).join("");
  return {
    ...history,
    cards,
    summary: `<p class="subtle">&#20849; ${formatNumber(history.total)} &#26465;&#21382;&#21490;&#35760;&#24405;&#65292;&#31532; ${formatNumber(history.page)} / ${formatNumber(history.totalPages)} &#39029;&#12290;</p>`,
    pagination: renderHistoryPagination(history)
  };
}

function renderHistoryPagination(history) {
  if (!history || history.totalPages <= 1) return "";
  const pages = [];
  const start = Math.max(1, history.page - 2);
  const end = Math.min(history.totalPages, history.page + 2);
  if (history.page > 1) pages.push(`<a class="page-link" href="/?historyPage=${history.page - 1}">&#19978;&#19968;&#39029;</a>`);
  for (let page = start; page <= end; page++) {
    pages.push(page === history.page
      ? `<span class="page-link active">${formatNumber(page)}</span>`
      : `<a class="page-link" href="/?historyPage=${page}">${formatNumber(page)}</a>`);
  }
  if (history.page < history.totalPages) pages.push(`<a class="page-link" href="/?historyPage=${history.page + 1}">&#19979;&#19968;&#39029;</a>`);
  return `<nav class="pagination">${pages.join("")}</nav>`;
}

function renderHistoryCard(item) {
  const providerLabel = item.provider === "netease" ? "网易云" : item.provider === "bilibili" ? "B 站" : item.provider || "-";
  const typeLabel = historyTypeLabel(item);
  const title = firstNonEmpty(item.title, item.bvid, item.songId, item.playlistId, item.key);
  const eventSummary = Object.entries(item.events || {})
    .sort((left, right) => right[1] - left[1])
    .slice(0, 4)
    .map(([name, count]) => `${historyEventLabel(name)} ${formatNumber(count)}`)
    .join(" / ") || "-";
  const link = historySourceLink(item);
  return `
          <article class="cache-card">
            <p class="eyebrow">${escapeHtml(providerLabel)} · ${escapeHtml(typeLabel)}</p>
            <h2>${escapeHtml(title)}</h2>
            <dl class="grid">
              <div><dt>&#26631;&#35782;</dt><dd>${escapeHtml(historyIdentity(item))}</dd></div>
              <div><dt>&#35760;&#24405;&#27425;&#25968;</dt><dd>${formatNumber(item.seenCount)}</dd></div>
              <div><dt>&#20107;&#20214;</dt><dd>${escapeHtml(eventSummary)}</dd></div>
              <div><dt>vcrid ID</dt><dd>${formatNumber((item.vcrids || []).length)}</dd></div>
              <div><dt>P / &#27468;&#26354;&#25968;</dt><dd>${formatNumber(item.pagesCount || 0)}</dd></div>
              <div><dt>&#26368;&#36817;&#35760;&#24405;</dt><dd>${item.lastSeenAt ? `${escapeHtml(formatDuration(Math.floor((Date.now() - item.lastSeenAt) / 1000)))}&#21069;` : "-"}</dd></div>
              <div><dt>&#39318;&#27425;&#35760;&#24405;</dt><dd>${item.firstSeenAt ? escapeHtml(formatDateTime(item.firstSeenAt)) : "-"}</dd></div>
              <div><dt>&#38142;&#25509;</dt><dd>${link ? `<a href="${escapeHtml(link)}" target="_blank" rel="noopener noreferrer">${escapeHtml(linkLabel(item))}</a>` : "-"}</dd></div>
            </dl>
          </article>`;
}

function historyTypeLabel(item) {
  if (item.provider === "bilibili" && item.contentType === "video") return "视频";
  if (item.provider === "bilibili" && item.contentType === "live") return "直播";
  if (item.provider === "bilibili" && item.contentType === "list") return "列表/合集";
  if (item.provider === "netease" && item.contentType === "playlist") return "歌单";
  if (item.provider === "netease" && item.contentType === "song") return "单曲";
  return item.contentType || "-";
}

function historyEventLabel(value) {
  return ({
    imported: "导入",
    player_redirect: "播放",
    player_manifest: "文本",
    player_danmaku: "弹幕",
    api_pages: "清单",
    api_resolve: "解析",
    api_danmaku: "弹幕",
    vcrid_player: "vcrid播放",
    vcrid_danmaku: "vcrid弹幕",
    vcrid_resolve: "vcrid解析"
  })[value] || value;
}

function historyIdentity(item) {
  if (item.provider === "bilibili") return firstNonEmpty(item.bvid, item.aid ? `av${item.aid}` : "", item.key);
  if (item.provider === "netease" && item.contentType === "playlist") return item.playlistId || item.key;
  if (item.provider === "netease" && item.contentType === "song") return item.songId || item.key;
  return item.key || "-";
}

function historySourceLink(item) {
  if (item.normalizedUrl) return item.normalizedUrl;
  if (item.sourceUrl && /^https?:\/\//i.test(item.sourceUrl)) return item.sourceUrl;
  if (item.provider === "bilibili" && item.bvid) return `https://www.bilibili.com/video/${item.bvid}/`;
  if (item.provider === "bilibili" && item.aid) return `https://www.bilibili.com/video/av${item.aid}/`;
  if (item.provider === "netease" && item.contentType === "playlist" && item.playlistId) return `https://music.163.com/playlist?id=${item.playlistId}`;
  if (item.provider === "netease" && item.contentType === "song" && item.songId) return `https://music.163.com/song?id=${item.songId}`;
  return "";
}

function linkLabel(item) {
  if (item.provider === "bilibili") return firstNonEmpty(item.bvid, item.aid ? `av${item.aid}` : "", "打开");
  if (item.provider === "netease" && item.contentType === "playlist") return `playlist ${item.playlistId || ""}`.trim();
  if (item.provider === "netease" && item.contentType === "song") return `song ${item.songId || ""}`.trim();
  return "打开";
}

function buildProviderHistoryDashboard(requestUrl) {
  const rawProvider = String(requestUrl?.searchParams?.get("historyProvider") || "bilibili").toLowerCase();
  const provider = rawProvider === "netease" ? "netease" : "bilibili";
  const page = readPositiveInt(requestUrl?.searchParams?.get("historyPage"), 1);
  const pageSize = readPositiveInt(requestUrl?.searchParams?.get("historyPageSize"), DASHBOARD_HISTORY_PAGE_SIZE);
  const history = historyStore?.list({ page, pageSize, provider }) || { page: 1, pageSize, total: 0, totalPages: 1, items: [] };
  const providerTitle = provider === "netease" ? "&#32593;&#26131;&#20113;&#38899;&#20048;" : "B &#31449;";
  return {
    ...history,
    provider,
    cards: history.items.map((item) => renderProviderHistoryCard(item)).join(""),
    tabs: renderProviderHistoryTabs(provider),
    summary: `<p class="subtle">${providerTitle} &#20849; ${formatNumber(history.total)} &#26465;&#21382;&#21490;&#35760;&#24405;&#65292;&#31532; ${formatNumber(history.page)} / ${formatNumber(history.totalPages)} &#39029;&#12290;</p>`,
    pagination: renderProviderHistoryPagination(history, provider),
    jumpForm: renderProviderHistoryJumpForm(history, provider)
  };
}

function renderProviderHistoryTabs(provider) {
  return `<nav class="tabs">
    <a class="tab-link ${provider === "bilibili" ? "active" : ""}" href="/?historyProvider=bilibili">B &#31449;</a>
    <a class="tab-link ${provider === "netease" ? "active" : ""}" href="/?historyProvider=netease">&#32593;&#26131;&#20113;&#38899;&#20048;</a>
  </nav>`;
}

function providerHistoryPageHref(provider, page) {
  return `/?historyProvider=${encodeURIComponent(provider)}&historyPage=${Math.max(1, Number(page || 1))}`;
}

function renderProviderHistoryPagination(history, provider) {
  if (!history || history.totalPages <= 1) return "";
  const links = [];
  const start = Math.max(1, history.page - 2);
  const end = Math.min(history.totalPages, history.page + 2);
  if (history.page > 1) links.push(`<a class="page-link" href="${providerHistoryPageHref(provider, history.page - 1)}">&#19978;&#19968;&#39029;</a>`);
  for (let page = start; page <= end; page++) {
    links.push(page === history.page
      ? `<span class="page-link active">${formatNumber(page)}</span>`
      : `<a class="page-link" href="${providerHistoryPageHref(provider, page)}">${formatNumber(page)}</a>`);
  }
  if (history.page < history.totalPages) links.push(`<a class="page-link" href="${providerHistoryPageHref(provider, history.page + 1)}">&#19979;&#19968;&#39029;</a>`);
  return `<nav class="pagination">${links.join("")}</nav>`;
}

function renderProviderHistoryJumpForm(history, provider) {
  if (!history || history.totalPages <= 1) return "";
  return `<form class="jump-form" method="get" action="/">
    <input type="hidden" name="historyProvider" value="${escapeHtml(provider)}">
    <label for="historyPageJump">&#36339;&#21040;</label>
    <input id="historyPageJump" name="historyPage" type="number" min="1" max="${history.totalPages}" value="${history.page}">
    <span>/ ${formatNumber(history.totalPages)} &#39029;</span>
    <button type="submit">&#36339;&#36716;</button>
  </form>`;
}

function renderProviderHistoryCard(item) {
  const providerLabel = item.provider === "netease" ? "&#32593;&#26131;&#20113;" : item.provider === "bilibili" ? "B &#31449;" : escapeHtml(item.provider || "-");
  const typeLabel = providerHistoryTypeLabel(item);
  const title = firstNonEmpty(item.title, item.bvid, item.songId, item.playlistId, item.key);
  const eventSummary = Object.entries(item.events || {})
    .sort((left, right) => right[1] - left[1])
    .slice(0, 4)
    .map(([name, count]) => `${providerHistoryEventLabel(name)} ${formatNumber(count)}`)
    .join(" / ") || "-";
  const link = historySourceLink(item);
  return `
          <article class="cache-card">
            <p class="eyebrow">${providerLabel} · ${typeLabel}</p>
            <h2>${escapeHtml(title)}</h2>
            <dl class="grid">
              <div><dt>&#26631;&#35782;</dt><dd>${escapeHtml(historyIdentity(item))}</dd></div>
              <div><dt>&#35760;&#24405;&#27425;&#25968;</dt><dd>${formatNumber(item.seenCount)}</dd></div>
              <div><dt>&#20107;&#20214;</dt><dd>${eventSummary}</dd></div>
              <div><dt>vcrid ID</dt><dd>${formatNumber((item.vcrids || []).length)}</dd></div>
              <div><dt>P / &#27468;&#26354;&#25968;</dt><dd>${formatNumber(item.pagesCount || 0)}</dd></div>
              <div><dt>&#26368;&#36817;&#35760;&#24405;</dt><dd>${item.lastSeenAt ? `${escapeHtml(formatDuration(Math.floor((Date.now() - item.lastSeenAt) / 1000)))}&#21069;` : "-"}</dd></div>
              <div><dt>&#39318;&#27425;&#35760;&#24405;</dt><dd>${item.firstSeenAt ? escapeHtml(formatDateTime(item.firstSeenAt)) : "-"}</dd></div>
              <div><dt>&#38142;&#25509;</dt><dd>${link ? `<a href="${escapeHtml(link)}" target="_blank" rel="noopener noreferrer">${escapeHtml(linkLabel(item))}</a>` : "-"}</dd></div>
            </dl>
          </article>`;
}

function providerHistoryTypeLabel(item) {
  if (item.provider === "bilibili" && item.contentType === "video") return "&#35270;&#39057;";
  if (item.provider === "bilibili" && item.contentType === "live") return "&#30452;&#25773;";
  if (item.provider === "bilibili" && item.contentType === "list") return "&#21015;&#34920;/&#21512;&#38598;";
  if (item.provider === "netease" && item.contentType === "playlist") return "&#27468;&#21333;";
  if (item.provider === "netease" && item.contentType === "song") return "&#21333;&#26354;";
  return escapeHtml(item.contentType || "-");
}

function providerHistoryEventLabel(value) {
  return ({
    imported: "&#23548;&#20837;",
    player_redirect: "&#25773;&#25918;",
    player_manifest: "&#25991;&#26412;",
    player_danmaku: "&#24377;&#24149;",
    api_pages: "&#28165;&#21333;",
    api_resolve: "&#35299;&#26512;",
    api_danmaku: "&#24377;&#24149;",
    vcrid_player: "vcrid&#25773;&#25918;",
    vcrid_danmaku: "vcrid&#24377;&#24149;",
    vcrid_resolve: "vcrid&#35299;&#26512;"
  })[value] || escapeHtml(value);
}

function renderDashboard(requestUrl = null) {
  pruneAllExpiredCaches();
  const uptimeSeconds = Math.floor((Date.now() - stats.startedAt) / 1000);
  const biliRedirects = Math.max(0, stats.playerRedirects - stats.neteaseRedirects - stats.liveRedirects);
  const vcridCount = vcrIdStore ? Object.keys(vcrIdStore.data.records || {}).length : 0;
  const vcridUsagePercent = vcrIdStore ? Math.round(vcrIdStore.usageRatio() * 10000) / 100 : 0;
  const vcridFreeCount = Math.max(0, VCRID_MAX - vcridCount);
  const vcridFreePercent = Math.max(0, Math.round((1 - (vcrIdStore ? vcrIdStore.usageRatio() : 0)) * 10000) / 100);
  const lastGc = vcrIdStore?.lastGc || {};
  const historyDashboard = buildProviderHistoryDashboard(requestUrl || new URL("http://localhost/"));
  const historyTotal = Object.keys(historyStore?.data?.records || {}).length;
  const bilibiliHistoryCount = Object.values(historyStore?.data?.records || {}).filter((record) => record.provider === "bilibili").length;
  const neteaseHistoryCount = Object.values(historyStore?.data?.records || {}).filter((record) => record.provider === "netease").length;
  return `<!doctype html>
<html lang="zh-CN">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <meta http-equiv="refresh" content="15">
  <link rel="icon" type="image/png" href="${PAULKOI_LOGO_PATH}">
  <link rel="apple-touch-icon" href="${PAULKOI_LOGO_PATH}">
  <title>PaulKoiPlayer &#35299;&#26512;&#26381;&#21153;</title>
  <style>
    :root { --bg:#f6f7f9; --panel:#fff; --soft:#eef6f4; --text:#172026; --muted:#65717b; --line:#dce3e8; --green:#0f8f72; --shadow:0 16px 40px rgba(25,38,49,.10); }
    * { box-sizing:border-box; }
    body { margin:0; min-height:100vh; font-family:ui-sans-serif,system-ui,-apple-system,BlinkMacSystemFont,"Segoe UI","Microsoft YaHei",sans-serif; background:var(--bg); color:var(--text); }
    .shell { max-width:1180px; margin:0 auto; padding:32px 20px 48px; }
    header { display:flex; justify-content:space-between; align-items:flex-end; gap:24px; padding-bottom:24px; border-bottom:1px solid var(--line); }
    .brand { display:flex; align-items:center; gap:16px; min-width:0; }
    .brand-logo { width:72px; height:72px; object-fit:contain; flex:0 0 auto; filter:drop-shadow(0 10px 18px rgba(0,138,210,.18)); }
    .brand-copy { min-width:0; }
    h1 { margin:0; font-size:34px; line-height:1.15; }
    h2 { margin:0 0 16px; font-size:20px; }
    .eyebrow { margin:0 0 8px; color:var(--green); font-size:13px; font-weight:700; }
    .subtle { margin:10px 0 0; color:var(--muted); font-size:15px; }
    .badge { display:inline-flex; align-items:center; gap:8px; min-height:36px; padding:0 12px; border:1px solid #b9ddd5; border-radius:8px; background:var(--soft); color:#0a604e; font-weight:700; white-space:nowrap; }
    .badge:before { content:""; width:8px; height:8px; border-radius:999px; background:var(--green); }
    .stats { display:grid; grid-template-columns:repeat(auto-fit,minmax(210px,1fr)); gap:14px; margin:24px 0; }
    .stat,.panel,.cache-card { border:1px solid var(--line); border-radius:8px; background:var(--panel); box-shadow:var(--shadow); }
    .stat { min-height:118px; padding:18px; }
    .stat span { display:block; color:var(--muted); font-size:14px; }
    .stat strong { display:block; margin-top:10px; font-size:32px; line-height:1; }
    .stat small { display:block; margin-top:12px; color:var(--muted); }
    .stat-primary { grid-column:1 / -1; min-height:150px; padding:24px 28px; }
    .stat-primary span { font-size:16px; }
    .stat-primary strong { font-size:54px; line-height:.95; letter-spacing:0; }
    .panel { margin-top:16px; padding:22px; }
    .grid { display:grid; grid-template-columns:repeat(2,minmax(0,1fr)); gap:12px 18px; margin:0; }
    dt { color:var(--muted); font-size:13px; }
    dd { margin:4px 0 0; overflow-wrap:anywhere; font-size:15px; font-weight:650; }
    code { display:block; overflow:auto; padding:12px; border:1px solid var(--line); border-radius:8px; background:#f2f5f7; color:#20313d; font-family:"Cascadia Mono",Consolas,monospace; font-size:13px; white-space:nowrap; }
    a { color:var(--green); font-weight:700; text-decoration:none; }
    a:hover { text-decoration:underline; }
    .cache-list { display:grid; gap:14px; }
    .cache-card { padding:18px; background:#fbfcfd; }
    .tabs { display:flex; flex-wrap:wrap; gap:10px; margin:0 0 14px; }
    .tab-link { display:inline-flex; align-items:center; min-height:38px; padding:0 16px; border:1px solid var(--line); border-radius:8px; background:#fff; color:#20313d; font-weight:800; }
    .tab-link.active { background:var(--green); border-color:var(--green); color:#fff; }
    .pagination { display:flex; flex-wrap:wrap; gap:8px; margin:16px 0 0; }
    .page-link { display:inline-flex; align-items:center; min-height:34px; padding:0 12px; border:1px solid var(--line); border-radius:8px; background:#fff; color:#20313d; font-weight:700; }
    .page-link.active { background:var(--green); border-color:var(--green); color:#fff; }
    .jump-form { display:flex; flex-wrap:wrap; align-items:center; gap:8px; margin:14px 0 0; color:var(--muted); font-size:14px; }
    .jump-form input[type="number"] { width:96px; min-height:34px; padding:0 10px; border:1px solid var(--line); border-radius:8px; background:#fff; color:var(--text); font:inherit; }
    .jump-form button { min-height:34px; padding:0 14px; border:0; border-radius:8px; background:var(--green); color:#fff; font-weight:800; cursor:pointer; }
    .empty { padding:28px; border:1px dashed #b8c4cc; border-radius:8px; color:var(--muted); background:#fbfcfd; text-align:center; }
    @media (max-width:880px) { header { align-items:flex-start; flex-direction:column; } .brand-logo { width:60px; height:60px; } .stats,.grid { grid-template-columns:1fr; } h1 { font-size:28px; } }
  </style>
</head>
<body>
  <main class="shell">
    <header>
      <div class="brand">
        <img class="brand-logo" src="${PAULKOI_LOGO_PATH}" alt="PaulKoiPlayer">
        <div class="brand-copy">
          <p class="eyebrow">VRChat &#24377;&#24149;&#20195;&#29702;</p>
          <h1>PaulKoiPlayer&#35299;&#26512;&#26381;&#21153;</h1>
        </div>
      </div>
      <div class="badge">&#26381;&#21153;&#27491;&#24120;</div>
    </header>
    <section class="stats">
      <div class="stat stat-primary"><span>&#24050;&#21457;&#23556;&#24377;&#24149;</span><strong>${formatNumber(stats.emittedDanmakuRows)}</strong><small>&#32047;&#35745;&#36820;&#22238;&#34892;&#25968;</small></div>
      <div class="stat"><span>B &#31449;&#30452;&#38142;&#36339;&#36716;</span><strong>${formatCompactNumber(biliRedirects)}</strong><small>/player 302 durl</small></div>
      <div class="stat"><span>B &#31449;&#30452;&#25773;&#36339;&#36716;</span><strong>${formatCompactNumber(stats.liveRedirects)}</strong><small>/player 302 m3u8</small></div>
      <div class="stat"><span>&#32593;&#26131;&#20113;&#35299;&#26512;</span><strong>${formatCompactNumber(stats.neteaseRedirects)}</strong><small>302 music.126.net</small></div>
      <div class="stat"><span>B &#31449;&#35270;&#39057;&#28165;&#21333;&#35299;&#26512;</span><strong>${formatNumber(stats.bilibiliPagesManifestRequests)}</strong><small>/api/pages bilibili</small></div>
      <div class="stat"><span>&#32593;&#26131;&#20113;&#27468;&#21333;&#35299;&#26512;</span><strong>${formatNumber(stats.neteasePlaylistManifestRequests)}</strong><small>/api/pages netease-playlist</small></div>
      <div class="stat"><span>&#21382;&#21490;&#35760;&#24405;</span><strong>${formatNumber(historyTotal)}</strong><small>B &#31449; / &#32593;&#26131;&#20113;&#32479;&#19968;&#35760;&#24405;</small></div>
      <div class="stat"><span>B &#31449;&#35760;&#24405;</span><strong>${formatNumber(bilibiliHistoryCount)}</strong><small>&#35270;&#39057;&#12289;&#30452;&#25773;&#12289;&#21015;&#34920;</small></div>
      <div class="stat"><span>&#32593;&#26131;&#20113;&#35760;&#24405;</span><strong>${formatNumber(neteaseHistoryCount)}</strong><small>&#21333;&#26354;&#21644;&#27468;&#21333;</small></div>
      <div class="stat"><span>vcrid ID &#20351;&#29992;</span><strong>${formatNumber(vcridCount)}</strong><small>&#21097;&#20313; ${formatNumber(vcridFreeCount)} / ${vcridFreePercent}%</small></div>
      <div class="stat"><span>&#24377;&#24149;&#35831;&#27714;</span><strong>${formatCompactNumber(stats.playerDanmakuRequests)}</strong><small>/player #YBDM/1</small></div>
      <div class="stat"><span>&#32531;&#23384;&#21629;&#20013;</span><strong>${formatCompactNumber(cacheStats.view.hits + cacheStats.video.hits + cacheStats.danmaku.hits)}</strong><small>view/video/danmaku</small></div>
      <div class="stat"><span>&#38480;&#27969;&#25318;&#25130;</span><strong>${formatCompactNumber(stats.rateLimited)}</strong><small>429 too many requests</small></div>
      <div class="stat"><span>URL &#30333;&#21517;&#21333;&#25318;&#25130;</span><strong>${formatCompactNumber(stats.unsupportedUrlRejected)}</strong><small>unsupported_url</small></div>
      <div class="stat"><span>&#22833;&#36133;&#32531;&#23384;</span><strong>${formatCompactNumber(stats.failureCacheHits)}</strong><small>${failureCache.size} active entries</small></div>
      <div class="stat"><span>&#19978;&#28216;&#36229;&#26102;</span><strong>${formatCompactNumber(stats.upstreamTimeouts)}</strong><small>Bilibili/NetEase timeout</small></div>
    </section>
    <section class="panel">
      <h2>&#36816;&#34892;&#20449;&#24687;</h2>
      <div class="grid">
        <code>&#36816;&#34892;&#26102;&#38271;&#65306;${escapeHtml(formatDuration(uptimeSeconds))}</code>
        <code>&#21551;&#21160;&#26102;&#38388;&#65306;${escapeHtml(formatDateTime(stats.startedAt))}</code>
        <code>view/video/danmaku cache: ${viewCache.size}/${videoUrlCache.size}/${danmakuCache.size}</code>
        <code>cache limits: ${VIEW_CACHE_MAX_ENTRIES}/${VIDEO_URL_CACHE_MAX_ENTRIES}/${DANMAKU_CACHE_MAX_ENTRIES}, dashboard: ${DASHBOARD_DANMAKU_MAX_ENTRIES}</code>
        <code>danmaku disk cache: initial ${escapeHtml(formatDuration(Math.floor(DANMAKU_DISK_CACHE_INITIAL_TTL_MS / 1000)))}, refresh ${escapeHtml(formatDuration(Math.floor(DANMAKU_DISK_CACHE_REFRESH_MS / 1000)))}</code>
        <code>rate limit: /player ${RATE_LIMIT_PLAYER}/${Math.floor(RATE_LIMIT_WINDOW_MS / 1000)}s, cooldown ${Math.floor(RATE_LIMIT_COOLDOWN_MS / 1000)}s</code>
        <code>failure cache: ${failureCache.size}/${FAILURE_CACHE_MAX_ENTRIES}, ttl ${Math.floor(FAILURE_CACHE_TTL_MS / 1000)}s</code>
        <code>inflight: ${inflight.size}</code>
        <code>vcrid store: ${vcridCount}/${VCRID_MAX}, ${vcridUsagePercent}%, ${vcrIdStore?.available ? "available" : "unavailable"}</code>
        <code>vcrid gc: ${VCRID_GC_ENABLED ? "enabled" : "disabled"}, threshold ${Math.round(VCRID_GC_USAGE_THRESHOLD * 100)}%, target ${Math.round(VCRID_GC_TARGET_USAGE * 100)}%, idle ${VCRID_GC_MIN_UNUSED_DAYS}d</code>
        <code>vcrid gc result: runs ${formatNumber(stats.vcridGcRuns)}, deleted ${formatNumber(stats.vcridGcDeleted)}, last ${escapeHtml(lastGc.reason || "none")}</code>
        <code>history store: ${formatNumber(historyTotal)}, ${historyStore?.available ? "available" : "unavailable"}</code>
        <code>NetEase song/playlist: ${formatNumber(stats.neteaseSongRedirects)}/${formatNumber(stats.neteasePlaylistRedirects)}</code>
        <code>/api/resolve: ${stats.resolveRequests}</code>
        <code>/api/pages: ${stats.apiPagesRequests}</code>
        <code>/api/lyrics: ${stats.apiLyricsRequests}, cache ${neteaseLyricsCache.size}/${NETEASE_LYRICS_CACHE_MAX_ENTRIES}</code>
        <code>unsupported url rejected: ${stats.unsupportedUrlRejected}</code>
        <code>legacy rejected: ${stats.legacyRejected}</code>
      </div>
    </section>
    <section class="panel">
      <h2>&#32593;&#26131;&#20113;&#38899;&#20048;&#35299;&#26512;</h2>
      <p class="subtle">&#24863;&#35874; <a href="https://music.znnu.com/" target="_blank" rel="noopener noreferrer">music.znnu.com</a> &#25552;&#20379;&#31532;&#19977;&#26041;&#35299;&#26512;&#26381;&#21153;&#12290;</p>
    </section>
    <section class="panel">
      <h2>&#35299;&#26512;&#21382;&#21490;&#35760;&#24405;</h2>
      ${historyDashboard.tabs}
      ${historyDashboard.summary}
      <div class="cache-list">${historyDashboard.cards || `<div class="empty">&#26242;&#26080;&#35299;&#26512;&#21382;&#21490;&#35760;&#24405;</div>`}</div>
      ${historyDashboard.pagination}
      ${historyDashboard.jumpForm}
    </section>
    <section class="panel">
      <h2>&#25509;&#21475;</h2>
      <div class="grid">
        <code>GET /player/?url=&lt;Bilibili URL&gt;</code>
        <code>GET /player/?__dm=1&amp;url=&lt;Bilibili URL&gt;</code>
        <code>GET /api/resolve?url=&lt;Bilibili URL&gt;</code>
        <code>GET /api/cache/stats</code>
      </div>
    </section>
  </main>
</body>
</html>`;
}

function setCors(res) {
  res.setHeader("Access-Control-Allow-Origin", "*");
  res.setHeader("Access-Control-Allow-Methods", "GET, HEAD, OPTIONS");
  res.setHeader("Access-Control-Allow-Headers", "Content-Type, Range");
}

function checkRateLimit(req, requestUrl) {
  const policy = rateLimitPolicy(requestUrl.pathname);
  if (!policy.limit) return { allowed: true, retryAfterSeconds: 0 };

  const now = Date.now();
  pruneRateLimitBuckets(now);

  const ip = getClientIp(req);
  const key = `${policy.name}:${ip}`;
  let bucket = rateLimitBuckets.get(key);
  if (!bucket || now - bucket.windowStart >= RATE_LIMIT_WINDOW_MS) {
    bucket = { windowStart: now, count: 0, blockedUntil: 0 };
  }

  if (bucket.blockedUntil && now < bucket.blockedUntil) {
    rateLimitBuckets.set(key, bucket);
    return {
      allowed: false,
      retryAfterSeconds: Math.max(1, Math.ceil((bucket.blockedUntil - now) / 1000))
    };
  }

  bucket.count++;
  if (bucket.count > policy.limit) {
    bucket.blockedUntil = now + RATE_LIMIT_COOLDOWN_MS;
    rateLimitBuckets.set(key, bucket);
    console.warn("[rate-limited]", JSON.stringify({
      ip,
      path: requestUrl.pathname,
      policy: policy.name,
      limit: policy.limit,
      windowSeconds: Math.floor(RATE_LIMIT_WINDOW_MS / 1000),
      cooldownSeconds: Math.floor(RATE_LIMIT_COOLDOWN_MS / 1000)
    }));
    return {
      allowed: false,
      retryAfterSeconds: Math.max(1, Math.ceil(RATE_LIMIT_COOLDOWN_MS / 1000))
    };
  }

  rateLimitBuckets.set(key, bucket);
  return { allowed: true, retryAfterSeconds: 0 };
}

function rateLimitPolicy(pathname) {
  const path = String(pathname || "/").replace(/\/+$/, "") || "/";
  if (path === "/health" || path === PAULKOI_LOGO_PATH) {
    return { name: "light", limit: RATE_LIMIT_GENERAL };
  }
  if (path === "/") return { name: "home", limit: RATE_LIMIT_HOME };
  if (path === "/player") return { name: "player", limit: RATE_LIMIT_PLAYER };
  if (path === "/api/danmaku") return { name: "api-danmaku", limit: RATE_LIMIT_API_DANMAKU };
  if (path === "/api/resolve") return { name: "api-resolve", limit: RATE_LIMIT_API_RESOLVE };
  if (path === "/api/pages") return { name: "api-pages", limit: RATE_LIMIT_API_PAGES };
  if (path === "/api/lyrics") return { name: "api-lyrics", limit: RATE_LIMIT_API_LYRICS };
  if (path === "/api/cache/stats") return { name: "cache-stats", limit: RATE_LIMIT_CACHE_STATS };
  return { name: "general", limit: RATE_LIMIT_GENERAL };
}

function getClientIp(req) {
  const cfIp = firstHeaderValue(req.headers["cf-connecting-ip"]);
  if (cfIp) return cfIp;

  const forwarded = firstHeaderValue(req.headers["x-forwarded-for"]);
  if (forwarded) return forwarded;

  return String(req.socket?.remoteAddress || "unknown").replace(/^::ffff:/, "");
}

function firstHeaderValue(value) {
  const raw = Array.isArray(value) ? value[0] : value;
  return String(raw || "").split(",")[0].trim();
}

function getRequestOrigin(req, requestUrl) {
  const forwardedProto = firstHeaderValue(req.headers["x-forwarded-proto"]);
  const forwardedHost = firstHeaderValue(req.headers["x-forwarded-host"]);
  const proto = /^https?$/i.test(forwardedProto)
    ? forwardedProto.toLowerCase()
    : String(requestUrl.protocol || "http:").replace(/:$/, "") || "http";
  const host = forwardedHost || firstHeaderValue(req.headers.host) || requestUrl.host || `localhost:${PORT}`;
  return `${proto}://${host}`;
}

function pruneRateLimitBuckets(now = Date.now()) {
  if (rateLimitBuckets.size <= 2000) return;
  for (const [key, bucket] of rateLimitBuckets.entries()) {
    const expiredWindow = now - Number(bucket.windowStart || 0) > RATE_LIMIT_WINDOW_MS * 2;
    const expiredBlock = !bucket.blockedUntil || now > Number(bucket.blockedUntil || 0) + RATE_LIMIT_WINDOW_MS;
    if (expiredWindow && expiredBlock) rateLimitBuckets.delete(key);
  }
}

function noStoreHeaders() {
  return { "Cache-Control": "no-store", "Pragma": "no-cache", "Expires": "0" };
}

function danmakuCacheHeaders() {
  return { "Cache-Control": "public, max-age=3600, s-maxage=86400" };
}

function sendWhitelistRejection(res, result) {
  stats.unsupportedUrlRejected++;
  markStatsDirty();
  sendText(res, 400, [
    "#YBDM/1",
    `#error=${escapeField(result.error || "unsupported_url")}`,
    `#host=${escapeField(result.host || "")}`,
    `#path=${escapeField(result.path || "")}`,
    ""
  ].join("\n"), noStoreHeaders());
}

function sendText(res, status, body, extraHeaders = {}) {
  res.writeHead(status, { "Content-Type": "text/plain; charset=utf-8", ...extraHeaders });
  res.end(body);
}

function sendHtml(res, status, body, extraHeaders = {}) {
  res.writeHead(status, { "Content-Type": "text/html; charset=utf-8", ...extraHeaders });
  res.end(body);
}

function sendJson(res, status, body, extraHeaders = {}) {
  res.writeHead(status, { "Content-Type": "application/json; charset=utf-8", ...extraHeaders });
  res.end(`${JSON.stringify(body, null, 2)}\n`);
}

function sendBuffer(res, status, body, contentType, extraHeaders = {}) {
  res.writeHead(status, {
    "Content-Type": contentType,
    "Content-Length": body.length,
    ...extraHeaders
  });
  res.end(body);
}

function logPlayerRequest(req, requestUrl, extra) {
  console.log("[/player]", JSON.stringify({
    path: requestUrl.pathname,
    query: redactUrlSearch(requestUrl.search),
    ...extra,
    "user-agent": req.headers["user-agent"] || "",
    accept: req.headers.accept || "",
    range: req.headers.range || "",
    "x-forwarded-for": req.headers["x-forwarded-for"] || "",
    "cf-connecting-ip": req.headers["cf-connecting-ip"] || ""
  }));
}

function redactUrlSearch(search) {
  const params = new URLSearchParams(search || "");
  const source = params.get("url");
  if (source) {
    params.set("url", normalizeBvid(source) || extractAid(source) || "unrecognized");
  }
  return params.toString() ? `?${params.toString()}` : "";
}

function normalizeBvid(value) {
  const match = String(value || "").match(/BV[0-9A-Za-z]{10,}/i);
  return match ? match[0].replace(/^bv/i, "BV") : "";
}

function normalizeAid(value) {
  const match = String(value || "").match(/\d+/);
  return match ? Number.parseInt(match[0], 10) : 0;
}

function extractAid(value) {
  const text = String(value || "");
  const match = text.match(/(?:^|[?&/])av(\d+)/i) || text.match(/[?&](?:aid|avid|oid)=(\d+)/i);
  return match ? match[1] : "";
}

function readPositiveInt(value, fallback) {
  const parsed = Number.parseInt(value || "", 10);
  return Number.isFinite(parsed) && parsed >= 1 ? parsed : fallback;
}

function parseVcridParam(requestUrl) {
  if (!requestUrl.searchParams.has("vcrid")) return { present: false, ok: false, vcrid: 0 };
  const raw = String(requestUrl.searchParams.get("vcrid") || "").trim();
  if (!/^\d+$/.test(raw)) return { present: true, ok: false, vcrid: 0 };
  const vcrid = Number.parseInt(raw, 10);
  return {
    present: true,
    ok: vcrid >= VCRID_MIN && vcrid <= VCRID_MAX,
    vcrid
  };
}

function intEnv(name, fallback) {
  const parsed = Number.parseInt(process.env[name] || "", 10);
  return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback;
}

function numberEnv(name, fallback) {
  const parsed = Number.parseFloat(process.env[name] || "");
  return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback;
}

function decodeRepeatedly(value) {
  let current = String(value || "").trim();
  for (let i = 0; i < 4; i++) {
    try {
      const decoded = decodeURIComponent(current);
      if (decoded === current) break;
      current = decoded;
    } catch {
      break;
    }
  }
  return current;
}

function tryParseUrl(value) {
  try {
    return new URL(value);
  } catch {
    return null;
  }
}

function tryParseFlexibleUrl(value) {
  const text = String(value || "").trim();
  if (!text) return null;
  const parsed = tryParseUrl(text);
  if (parsed) return parsed;
  if (/^[a-z0-9.-]+\//i.test(text) || /^[a-z0-9.-]+\?/i.test(text)) {
    return tryParseUrl(`https://${text}`);
  }
  return null;
}

function normalizeHost(value) {
  return String(value || "").trim().toLowerCase().replace(/^www\./, "");
}

function isNeteaseHost(value) {
  const host = normalizeHost(value);
  return host === "music.163.com" || host === "y.music.163.com" || host === "m.music.163.com";
}

function isNeteaseHashFragmentMissing(value) {
  const parsed = tryParseFlexibleUrl(decodeRepeatedly(value));
  if (!parsed || !isNeteaseHost(parsed.hostname)) return false;
  if (parsed.hash || parsed.searchParams.get("id")) return false;

  const path = parsed.pathname.replace(/\/+$/, "");
  return path === "" || path === "/";
}

function extractNeteaseId(candidate, type) {
  const fromQuery = normalizeNumericId(candidate.searchParams.get("id") || "");
  if (fromQuery) return fromQuery;

  const path = String(candidate.pathname || "");
  const pattern = type === "playlist"
    ? /\/(?:f\/|m\/)?playlist\/(\d+)/i
    : /\/(?:f\/|m\/)?song\/(\d+)/i;
  const match = path.match(pattern);
  return match ? match[1] : "";
}

function normalizeNumericId(value) {
  const match = String(value || "").match(/^\d+$/);
  return match ? match[0] : "";
}

function normalizePlayableUrl(value) {
  const normalized = String(value || "").replace(/`/g, "").trim().replace(/^http:\/\//i, "https://");
  return /^https?:\/\//i.test(normalized) ? normalized : "";
}

function normalizeNeteaseLevel(value) {
  const text = String(value || "").trim().toLowerCase();
  return ["standard", "exhigh", "lossless", "hires", "sky", "jyeffect", "jymaster"].includes(text)
    ? text
    : "standard";
}

function headerValue(text, name) {
  const match = String(text || "").match(new RegExp(`^#${name}=([^\\n\\r]*)`, "m"));
  return match ? match[1] : "";
}

function getDanmakuCount(tsv) {
  const match = String(tsv || "").match(/^#count=(\d+)$/m);
  return match ? Number.parseInt(match[1], 10) : 0;
}

function htmlDecode(value) {
  return String(value || "")
    .replace(/&lt;/g, "<")
    .replace(/&gt;/g, ">")
    .replace(/&quot;/g, "\"")
    .replace(/&#39;/g, "'")
    .replace(/&apos;/g, "'")
    .replace(/&amp;/g, "&");
}

function escapeField(value) {
  return String(value ?? "")
    .replace(/\\/g, "\\\\")
    .replace(/\t/g, "\\t")
    .replace(/\r/g, "\\r")
    .replace(/\n/g, "\\n");
}

function escapeHtml(value) {
  return String(value ?? "")
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;");
}

function formatNumber(value) {
  return new Intl.NumberFormat("zh-CN").format(Number(value || 0));
}

function formatCompactNumber(value) {
  const number = Number(value || 0);
  if (!Number.isFinite(number)) return "0";
  if (Math.abs(number) >= 100000000) {
    const yi = number / 100000000;
    return `${formatCompactDecimal(yi)}&#20159;`;
  }
  if (Math.abs(number) >= 1000000) {
    const wan = number / 10000;
    return `${formatCompactDecimal(wan)}&#19975;`;
  }
  if (Math.abs(number) >= 1000) {
    const thousand = number / 1000;
    return `${formatCompactDecimal(thousand)}K`;
  }
  return formatNumber(number);
}

function formatCompactDecimal(value) {
  const rounded = Math.abs(value) >= 10 ? Math.round(value) : Math.round(value * 10) / 10;
  return new Intl.NumberFormat("zh-CN", {
    maximumFractionDigits: 1
  }).format(rounded);
}

function formatDateTime(value) {
  return new Intl.DateTimeFormat("zh-CN", {
    timeZone: DISPLAY_TIME_ZONE,
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
    hour12: false
  }).format(new Date(value));
}

function formatDuration(seconds) {
  const days = Math.floor(seconds / 86400);
  const hours = Math.floor((seconds % 86400) / 3600);
  const minutes = Math.floor((seconds % 3600) / 60);
  const rest = seconds % 60;
  const parts = [];
  if (days) parts.push(`${days}d`);
  if (hours) parts.push(`${hours}h`);
  if (minutes) parts.push(`${minutes}m`);
  parts.push(`${rest}s`);
  return parts.join(" ");
}
