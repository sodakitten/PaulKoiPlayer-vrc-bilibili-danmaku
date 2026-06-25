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
const NETEASE_PLAYLIST_CACHE_MAX_ENTRIES = intEnv("NETEASE_PLAYLIST_CACHE_MAX_ENTRIES", 100);
const NETEASE_URL_CACHE_MAX_ENTRIES = intEnv("NETEASE_URL_CACHE_MAX_ENTRIES", 500);
const ZNNU_FETCH_TIMEOUT_MS = intEnv("ZNNU_FETCH_TIMEOUT_SECONDS", 30) * 1000;
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
const inflight = new Map();
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
  legacyRejected: 0,
  errors: 0,
  emittedDanmakuRows: 0
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

    stats.totalRequests++;
    markStatsDirty();

    if (requestUrl.pathname === "/") {
      sendHtml(res, 200, renderDashboard(), noStoreHeaders());
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
  const source = requestUrl.searchParams.get("url") || "";
  if (!source) {
    sendText(res, 400, "missing url\n");
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
    sendText(res, 200, tsv, danmakuCacheHeaders());
    return;
  }

  const resolved = await resolveVideoUrl(input);
  if (!resolved.videoUrl) {
    sendText(res, 502, `no mp4 durl for this video\nbvid=${resolved.bvid}\ncid=${resolved.cid}\n`);
    return;
  }

  stats.playerRedirects++;
  res.writeHead(302, {
    "Location": resolved.videoUrl,
    ...noStoreHeaders()
  });
  res.end();
}

async function handleResolve(res, requestUrl) {
  stats.resolveRequests++;
  const source = requestUrl.searchParams.get("url") || "";
  if (!source) {
    sendJson(res, 400, { error: "missing url" }, noStoreHeaders());
    return;
  }

  const forcedPage = readPositiveInt(requestUrl.searchParams.get("p") || requestUrl.searchParams.get("page"), 0);
  const input = await parseInputUrl(source, forcedPage);
  if (!input.bvid && !input.aid) {
    sendJson(res, 400, { error: "missing bvid or aid", inputUrl: source }, noStoreHeaders());
    return;
  }

  const viewResult = await getVideoView(input);
  const view = viewResult.value;
  const selected = selectVideoPage(view, input);
  const video = await resolveVideoUrl({ ...input, cid: selected.cid });

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

async function handleApiDanmaku(res, requestUrl) {
  stats.apiDanmakuRequests++;
  const source = requestUrl.searchParams.get("url") || "";
  const forcedPage = readPositiveInt(requestUrl.searchParams.get("p") || requestUrl.searchParams.get("page"), 0);
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
  normalizedUrl = await expandNeteaseShortUrl(normalizedUrl);
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
    const isSongPath = /(^|\/)(?:m\/)?song(\/|$)/.test(lowerPath) || lowerPath.includes("/song/media/outer/url");

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

  return singleflight(`netease-short:${parsed.href}`, async () => {
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
  const response = await fetch(url, {
    method: options.method || "GET",
    headers: options.headers || znnuHeaders(),
    body: options.body,
    signal: AbortSignal.timeout(ZNNU_FETCH_TIMEOUT_MS)
  });
  const text = await response.text();
  if (!response.ok) throw new Error(`ZNNU request failed with HTTP ${response.status}.`);
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

  return singleflight(key, async () => {
    const current = map.get(key);
    if (current && Date.now() - current.time < ttlMs) return current.value;
    if (current) map.delete(key);

    const value = await loader();
    setCacheEntry(map, key, value, maxEntries);
    return value;
  });
}

async function parseInputUrl(rawValue, forcedPage = 0) {
  let normalizedUrl = decodeRepeatedly(rawValue);
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
  if (!parsed || parsed.hostname.toLowerCase() !== "b23.tv") return value;

  return singleflight(`short:${value}`, async () => {
    let current = value;
    for (let i = 0; i < 5; i++) {
      const response = await fetch(current, {
        method: "HEAD",
        redirect: "manual",
        headers: biliHeaders()
      });
      const location = response.headers.get("Location");
      if (!location) return current;
      current = new URL(location, current).toString();
      if (!tryParseUrl(current)?.hostname.toLowerCase().endsWith("b23.tv")) return current;
    }
    return current;
  });
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

async function getDanmakuTsv(input) {
  const viewResult = await getVideoView(input);
  const view = viewResult.value;
  const selected = selectVideoPage(view, input);
  const key = `dm:${selected.cid}`;

  return getCachedDanmaku(key, async () => {
    return buildDanmakuTsv({
      bvid: view.bvid || input.bvid || "",
      aid: view.aid || input.aid || 0,
      cid: selected.cid,
      page: selected.page,
      title: view.title || "",
      part: selected.part || "",
      duration: selected.duration || 0
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
    `#bvid=${escapeField(video.bvid || "")}`,
    `#aid=${video.aid || ""}`,
    `#cid=${video.cid}`,
    `#page=${video.page}`,
    `#title=${escapeField(video.title || "")}`,
    `#part=${escapeField(video.part || "")}`,
    `#count=${all.length}`,
    "#columns=progressMs\tmode\tcolor\tfontSize\tpool\tcontent"
  ];
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
  const response = await fetch(url, { headers: biliHeaders() });
  if (!response.ok) throw new Error(`Bilibili XML danmaku failed with HTTP ${response.status}.`);

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
  const response = await fetch(url, { headers: biliHeaders() });
  if (!response.ok) throw new Error(`Bilibili danmaku segment ${segment} failed with HTTP ${response.status}.`);
  return decodeDmSegMobileReply(new Uint8Array(await response.arrayBuffer()));
}

async function fetchJson(url) {
  const response = await fetch(url, { headers: biliHeaders() });
  const text = await response.text();
  if (!response.ok) throw new Error(`Bilibili API failed with HTTP ${response.status}.`);
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

  if (inflight.has(key)) {
    cacheStats.inflightHits++;
    return { value: await inflight.get(key), cacheHit: true };
  }

  statBucket.misses++;
  const promise = loader().then((value) => {
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

  cacheStats.danmaku.misses++;
  const promise = loader().then((value) => {
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
      danmakuEntries: danmakuCache.size
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
      danmakuMemoryFallback: Math.floor(DANMAKU_CACHE_TTL_MS / 1000),
      danmakuDiskInitial: Math.floor(DANMAKU_DISK_CACHE_INITIAL_TTL_MS / 1000),
      danmakuDiskRefresh: Math.floor(DANMAKU_DISK_CACHE_REFRESH_MS / 1000)
    },
    limits: {
      view: VIEW_CACHE_MAX_ENTRIES,
      videoUrl: VIDEO_URL_CACHE_MAX_ENTRIES,
      danmaku: DANMAKU_CACHE_MAX_ENTRIES,
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
      legacyRejected: stats.legacyRejected
    },
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

function renderDashboard() {
  pruneAllExpiredCaches();
  const uptimeSeconds = Math.floor((Date.now() - stats.startedAt) / 1000);
  const biliRedirects = Math.max(0, stats.playerRedirects - stats.neteaseRedirects - stats.liveRedirects);
  const danmakuEntries = [...danmakuCache.entries()]
    .sort((left, right) => right[1].time - left[1].time);
  const hiddenDanmakuEntries = Math.max(0, danmakuEntries.length - DASHBOARD_DANMAKU_MAX_ENTRIES);
  const cards = danmakuEntries.slice(0, DASHBOARD_DANMAKU_MAX_ENTRIES).map(([key, entry]) => {
    const text = entry.value || "";
    const expiresIn = Math.max(0, Math.floor((Number(entry.expiresAt || 0) - Date.now()) / 1000));
    const lastAccessAge = Math.max(0, Math.floor((Date.now() - Number(entry.lastAccessAt || entry.time)) / 1000));
    return `
          <article class="cache-card">
            <p class="eyebrow">&#24377;&#24149;&#32531;&#23384;</p>
            <h2>${escapeHtml(headerValue(text, "title") || key)}</h2>
            <dl class="grid">
              <div><dt>BV</dt><dd>${escapeHtml(headerValue(text, "bvid") || "-")}</dd></div>
              <div><dt>CID</dt><dd>${escapeHtml(headerValue(text, "cid") || "-")}</dd></div>
              <div><dt>&#39029;&#30721;</dt><dd>${escapeHtml(headerValue(text, "page") || "1")}</dd></div>
              <div><dt>&#24377;&#24149;&#25968;</dt><dd>${formatNumber(getDanmakuCount(text))}</dd></div>
              <div><dt>&#21097;&#20313;</dt><dd>${escapeHtml(formatDuration(expiresIn))}</dd></div>
              <div><dt>&#26368;&#36817;&#35775;&#38382;</dt><dd>${escapeHtml(formatDuration(lastAccessAge))}&#21069;</dd></div>
            </dl>
          </article>`;
  }).join("");
  const cacheSummary = hiddenDanmakuEntries > 0
    ? `<p class="subtle">&#24050;&#38544;&#34255; ${formatNumber(hiddenDanmakuEntries)} &#26465;&#26356;&#26087;&#30340;&#24377;&#24149;&#32531;&#23384;&#65292;&#20027;&#39029;&#20165;&#26174;&#31034;&#26368;&#36817; ${formatNumber(DASHBOARD_DANMAKU_MAX_ENTRIES)} &#26465;&#12290;</p>`
    : "";

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
      <div class="stat"><span>&#24377;&#24149;&#35831;&#27714;</span><strong>${formatCompactNumber(stats.playerDanmakuRequests)}</strong><small>/player #YBDM/1</small></div>
      <div class="stat"><span>&#32531;&#23384;&#21629;&#20013;</span><strong>${formatCompactNumber(cacheStats.view.hits + cacheStats.video.hits + cacheStats.danmaku.hits)}</strong><small>view/video/danmaku</small></div>
    </section>
    <section class="panel">
      <h2>&#36816;&#34892;&#20449;&#24687;</h2>
      <div class="grid">
        <code>&#36816;&#34892;&#26102;&#38271;&#65306;${escapeHtml(formatDuration(uptimeSeconds))}</code>
        <code>&#21551;&#21160;&#26102;&#38388;&#65306;${escapeHtml(formatDateTime(stats.startedAt))}</code>
        <code>view/video/danmaku cache: ${viewCache.size}/${videoUrlCache.size}/${danmakuCache.size}</code>
        <code>cache limits: ${VIEW_CACHE_MAX_ENTRIES}/${VIDEO_URL_CACHE_MAX_ENTRIES}/${DANMAKU_CACHE_MAX_ENTRIES}, dashboard: ${DASHBOARD_DANMAKU_MAX_ENTRIES}</code>
        <code>danmaku disk cache: initial ${escapeHtml(formatDuration(Math.floor(DANMAKU_DISK_CACHE_INITIAL_TTL_MS / 1000)))}, refresh ${escapeHtml(formatDuration(Math.floor(DANMAKU_DISK_CACHE_REFRESH_MS / 1000)))}</code>
        <code>inflight: ${inflight.size}</code>
        <code>NetEase song/playlist: ${formatNumber(stats.neteaseSongRedirects)}/${formatNumber(stats.neteasePlaylistRedirects)}</code>
        <code>/api/resolve: ${stats.resolveRequests}</code>
        <code>legacy rejected: ${stats.legacyRejected}</code>
      </div>
    </section>
    <section class="panel">
      <h2>&#32593;&#26131;&#20113;&#38899;&#20048;&#35299;&#26512;</h2>
      <p class="subtle">&#24863;&#35874; <a href="https://music.znnu.com/" target="_blank" rel="noopener noreferrer">music.znnu.com</a> &#25552;&#20379;&#31532;&#19977;&#26041;&#35299;&#26512;&#26381;&#21153;&#12290;</p>
    </section>
    <section class="panel">
      <h2>&#24377;&#24149;&#32531;&#23384;</h2>
      ${cacheSummary}
      <div class="cache-list">${cards || `<div class="empty">&#26242;&#26080;&#24377;&#24149;&#32531;&#23384;</div>`}</div>
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

function noStoreHeaders() {
  return { "Cache-Control": "no-store", "Pragma": "no-cache", "Expires": "0" };
}

function danmakuCacheHeaders() {
  return { "Cache-Control": "public, max-age=3600, s-maxage=86400" };
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

function intEnv(name, fallback) {
  const parsed = Number.parseInt(process.env[name] || "", 10);
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
    ? /\/(?:m\/)?playlist\/(\d+)/i
    : /\/(?:m\/)?song\/(\d+)/i;
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
