const DEFAULT_USER_AGENT = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/126 Safari/537.36";
const ZNNU_SIGNATURE_DOMAIN = "music.znnu.com";
const ZNNU_REFERER = "musicParser";
const ZNNU_SIGNATURE_SECRET = "a09d0f3700a279584e1515354fbe08a7ee1c617f919543142fa625b82f1b5ad0";

const memoryCache = new Map();

export default {
  async fetch(request, env = {}, ctx = {}) {
    const url = new URL(request.url);
    try {
      if (request.method === "OPTIONS") return withCors(new Response(null, { status: 204 }));
      if (url.pathname === "/health") return text("ok\n");
      if (url.pathname === "/" || url.pathname === "") return html(renderHome(env));
      if (url.pathname === "/api/cache/stats") return json(buildWorkerStats(env));
      if (url.pathname === "/api/danmaku") return await handleApiDanmaku(request, env, ctx, url);
      if (url.pathname === "/api/resolve") return await handleResolve(request, env, ctx, url);
      if (url.pathname === "/player" || url.pathname === "/player/") return await handlePlayer(request, env, ctx, url);
      return text("not found\n", 404);
    } catch (error) {
      return text(`#YBDM/1\n#error=${escapeField(error.message || String(error))}\n`, 500, noStoreHeaders());
    }
  }
};

async function handlePlayer(request, env, ctx, requestUrl) {
  const source = requestUrl.searchParams.get("url") || "";
  if (!source) return text("missing url\n", 400);
  if (isBilibiliLikeSource(source)) return await proxyToDocker(request, env, requestUrl);

  const forcedPage = readPositiveInt(requestUrl.searchParams.get("p") || requestUrl.searchParams.get("page"), 0);
  const liveInput = await parseLiveInput(source, env, ctx);
  if (liveInput) {
    const resolved = await resolveLiveUrl(liveInput, env, ctx);
    return redirect(resolved.videoUrl);
  }

  const input = await parseInputUrl(source, env, ctx, forcedPage);
  if (input.bvid || input.aid) {
    if (isDanmakuRequest(request, requestUrl)) {
      const tsv = await getDanmakuTsv(input, env, ctx);
      return text(tsv, 200, danmakuCacheHeaders());
    }

    const resolved = await resolveVideoUrl(input, env, ctx);
    if (!resolved.videoUrl) return text(`no mp4 durl for this video\nbvid=${resolved.bvid}\ncid=${resolved.cid}\n`, 502);
    return redirect(resolved.videoUrl);
  }

  const neteaseInput = await parseNeteaseInput(source, env, ctx, forcedPage);
  if (neteaseInput) {
    const level = normalizeNeteaseLevel(requestUrl.searchParams.get("level") || requestUrl.searchParams.get("quality") || getEnv(env, "NETEASE_LEVEL", "standard"));
    const resolved = await resolveNeteaseUrl({ ...neteaseInput, level }, env, ctx);
    return redirect(resolved.audioUrl);
  }

  if (isNeteaseHashFragmentMissing(source)) {
    return text("#YBDM/1\n#error=netease_hash_fragment_not_sent\n#hint=use_music.163.com/song?id=xxx_or_encode_hash_as_%23\n", 400, noStoreHeaders());
  }

  return text("#YBDM/1\n#error=missing_bvid_or_aid\n", 400);
}

async function handleApiDanmaku(request, env, ctx, requestUrl) {
  return await proxyToDocker(request, env, requestUrl);
}

async function handleApiDanmakuLocal(request, env, ctx, requestUrl) {
  const source = requestUrl.searchParams.get("url") || "";
  const forcedPage = readPositiveInt(requestUrl.searchParams.get("p") || requestUrl.searchParams.get("page"), 0);
  const input = source ? await parseInputUrl(source, env, ctx, forcedPage) : {
    normalizedUrl: "",
    bvid: normalizeBvid(requestUrl.searchParams.get("bvid") || ""),
    aid: normalizeAid(requestUrl.searchParams.get("aid") || requestUrl.searchParams.get("avid") || requestUrl.searchParams.get("oid") || ""),
    page: forcedPage || 1
  };
  if (!input.bvid && !input.aid) return text("#YBDM/1\n#error=missing_bvid_or_aid\n", 400);
  const tsv = await getDanmakuTsv(input, env, ctx);
  return text(tsv, 200, danmakuCacheHeaders());
}

async function handleResolve(_request, env, ctx, requestUrl) {
  const source = requestUrl.searchParams.get("url") || "";
  if (!source) return json({ error: "missing url" }, 400);
  if (isBilibiliLikeSource(source)) return await proxyToDocker(_request, env, requestUrl);
  const forcedPage = readPositiveInt(requestUrl.searchParams.get("p") || requestUrl.searchParams.get("page"), 0);
  const liveInput = await parseLiveInput(source, env, ctx);
  if (liveInput) {
    const resolved = await resolveLiveUrl(liveInput, env, ctx);
    return json({ type: "bilibili-live", roomId: liveInput.roomId, realRoomId: resolved.realRoomId, videoUrlPreview: previewUrl(resolved.videoUrl), actualQuality: resolved.actualQuality });
  }
  const input = await parseInputUrl(source, env, ctx, forcedPage);
  if (!input.bvid && !input.aid) return json({ error: "missing bvid or aid", inputUrl: source }, 400);
  const view = await getVideoView(input, env, ctx);
  const selected = selectVideoPage(view, input);
  const video = await resolveVideoUrl({ ...input, cid: selected.cid }, env, ctx);
  return json({
    inputUrl: source,
    normalizedUrl: input.normalizedUrl,
    bvid: view.bvid || input.bvid || "",
    aid: view.aid || input.aid || 0,
    selectedPage: selected.page,
    cid: selected.cid,
    title: view.title || "",
    videoUrlPreview: previewUrl(video.videoUrl)
  });
}

async function parseInputUrl(rawValue, env, ctx, forcedPage = 0) {
  let normalizedUrl = decodeRepeatedly(rawValue);
  normalizedUrl = unwrapKnownPlayerUrl(normalizedUrl);
  normalizedUrl = decodeRepeatedly(normalizedUrl);
  normalizedUrl = extractSupportedUrlFromText(normalizedUrl);
  normalizedUrl = await expandB23Url(normalizedUrl, env, ctx);
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

  return { normalizedUrl, bvid, aid, page: Math.max(1, page || 1) };
}

async function parseLiveInput(rawValue, env, ctx) {
  let normalizedUrl = decodeRepeatedly(rawValue);
  normalizedUrl = unwrapKnownPlayerUrl(normalizedUrl);
  normalizedUrl = decodeRepeatedly(normalizedUrl);
  normalizedUrl = extractSupportedUrlFromText(normalizedUrl);
  normalizedUrl = await expandB23Url(normalizedUrl, env, ctx);
  normalizedUrl = unwrapKnownPlayerUrl(normalizedUrl);
  normalizedUrl = decodeRepeatedly(normalizedUrl);
  const parsed = tryParseUrl(normalizedUrl);
  if (!parsed || parsed.hostname.toLowerCase() !== "live.bilibili.com") return null;
  const roomId = parseLiveRoomId(parsed);
  return roomId ? { type: "live", roomId, normalizedUrl } : null;
}

function parseLiveRoomId(url) {
  const fromQuery = normalizeAid(url.searchParams.get("room_id") || url.searchParams.get("roomid") || "");
  if (fromQuery) return String(fromQuery);
  for (const segment of url.pathname.split("/").map((part) => part.trim()).filter(Boolean)) {
    if (/^\d+$/.test(segment)) return segment;
  }
  return "";
}

async function expandB23Url(value, env, ctx) {
  const parsed = tryParseUrl(value);
  if (!parsed || !isBilibiliShortHost(parsed.hostname)) return value;
  return cachedText(`short:${value}`, 1800, env, ctx, async () => {
    let current = value;
    for (let i = 0; i < 5; i++) {
      const response = await fetch(current, { method: "HEAD", redirect: "manual", headers: biliHeaders(env) });
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

  const urls = text.match(/https?:\/\/[^\s"'<>]+/ig) || [];
  for (const rawUrl of urls) {
    const candidate = cleanupSharedUrl(rawUrl);
    const parsed = tryParseUrl(candidate);
    if (!parsed) continue;
    if (isSupportedSourceUrl(parsed)) return candidate;
  }

  return text;
}

function cleanupSharedUrl(value) {
  return String(value || "").trim().replace(/[)\]}>，。！？、；：]+$/u, "");
}

function isSupportedSourceUrl(parsed) {
  const host = normalizeHost(parsed.hostname);
  if (host === "b23.tv") return true;
  if (host === "bili2233.cn") return true;
  if (host === "biliplayer.91vrchat.com") return true;
  if (host === "live.bilibili.com") return true;
  if (host === "bilibili.com" || host.endsWith(".bilibili.com")) return true;
  if (host === "163cn.tv") return true;
  if (isNeteaseHost(host)) return true;
  return parsed.pathname.replace(/\/+$/, "") === "/player" && parsed.searchParams.has("url");
}

function isBilibiliShortHost(value) {
  const host = normalizeHost(value);
  return host === "b23.tv" || host === "bili2233.cn";
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
    const isKnownPlayer = host === "biliplayer.91vrchat.com" || (parsed.pathname.replace(/\/+$/, "") === "/player" && parsed.searchParams.has("url"));
    if (!isKnownPlayer) return current;
    const inner = parsed.searchParams.get("url");
    if (!inner) return current;
    current = decodeRepeatedly(inner);
  }
  return current;
}

async function getVideoView(input, env, ctx) {
  const key = input.bvid ? `bvid:${normalizeBvid(input.bvid)}` : `aid:${input.aid}`;
  return cachedJson(`view:${key}`, intEnv(env, "VIEW_CACHE_TTL_SECONDS", 1800), env, ctx, async () => {
    const url = new URL("https://api.bilibili.com/x/web-interface/view");
    if (input.bvid) url.searchParams.set("bvid", normalizeBvid(input.bvid));
    else url.searchParams.set("aid", String(input.aid));
    const payload = await fetchJson(url, env);
    if (payload.code !== 0 || !payload.data || !Array.isArray(payload.data.pages) || payload.data.pages.length === 0) {
      throw new Error(`Bilibili view API failed: ${payload.message || payload.code}`);
    }
    return payload.data;
  });
}

function selectVideoPage(view, input) {
  const pages = Array.isArray(view.pages) ? view.pages : [];
  if (!pages.length) throw new Error("video has no pages.");
  const cid = normalizeAid(input.cid);
  if (cid) {
    const byCid = pages.find((page) => String(page.cid) === String(cid));
    if (byCid) return normalizeSelectedPage(byCid, view, input);
  }
  const pageIndex = Math.max(0, Math.min((input.page || 1) - 1, pages.length - 1));
  return normalizeSelectedPage(pages[pageIndex], view, input);
}

function normalizeSelectedPage(selected, view, input) {
  return {
    page: Number(selected.page || input.page || 1),
    cid: Number(selected.cid),
    part: selected.part || "",
    duration: Number(selected.duration || view.duration || 0)
  };
}

async function resolveVideoUrl(input, env, ctx) {
  const view = await getVideoView(input, env, ctx);
  const selected = selectVideoPage(view, input);
  const key = `${view.bvid || input.bvid || `av${view.aid || input.aid}`}:${selected.cid}`;
  return cachedJson(`video:${key}`, intEnv(env, "VIDEO_URL_CACHE_TTL_SECONDS", 600), env, ctx, async () => {
    const videoUrl = await fetchMp4Durl({ aid: view.aid || input.aid, bvid: view.bvid || input.bvid || "", cid: selected.cid }, env);
    return { videoUrl, bvid: view.bvid || input.bvid || "", aid: view.aid || input.aid || 0, cid: selected.cid, page: selected.page, title: view.title || "" };
  });
}

async function fetchMp4Durl({ aid, bvid, cid }, env) {
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
    const payload = await fetchJson(url, env);
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

async function resolveLiveUrl(input, env, ctx) {
  return cachedJson(`live:${input.roomId}`, intEnv(env, "VIDEO_URL_CACHE_TTL_SECONDS", 600), env, ctx, async () => {
    const room = await getLiveRoomInfo(input.roomId, env);
    if (!room || !room.room_id) throw new Error("live room info not found");
    if (room.live_status !== 1) throw new Error("live room is not streaming");
    const playInfo = await getLivePlayInfo(room.room_id, 10000, env);
    const direct = normalizeLivePlayResult(playInfo);
    if (!direct.directUrl) throw new Error("live stream url not found");
    return { videoUrl: direct.directUrl, backupUrls: direct.backupUrls, actualQuality: direct.actualQuality, roomId: input.roomId, realRoomId: room.room_id };
  });
}

async function getLiveRoomInfo(roomId, env) {
  const params = new URLSearchParams({ id: String(roomId) });
  const payload = await fetchJson(`https://api.live.bilibili.com/room/v1/Room/room_init?${params}`, env);
  if (payload.code !== 0 || !payload.data) throw new Error(payload.message || payload.msg || "get live room info failed");
  return payload.data;
}

async function getLivePlayInfo(realRoomId, quality, env) {
  const params = new URLSearchParams({ room_id: String(realRoomId), protocol: "0,1", format: "1", codec: "0,1", qn: String(quality), platform: "h5", ptype: "8" });
  const payload = await fetchJson(`https://api.live.bilibili.com/xlive/web-room/v2/index/getRoomPlayInfo?${params}`, env);
  if (payload.code !== 0 || !payload.data) throw new Error(payload.message || payload.msg || "get live stream failed");
  return payload.data;
}

function normalizeLivePlayResult(data) {
  const streams = data?.playurl_info?.playurl?.stream;
  if (!Array.isArray(streams)) return { directUrl: "", actualQuality: 0, backupUrls: [] };
  const candidates = [];
  for (const stream of streams) {
    const protocolName = stream?.protocol_name || "";
    for (const format of Array.isArray(stream?.format) ? stream.format : []) {
      const formatName = format?.format_name || "";
      for (const codec of Array.isArray(format?.codec) ? format.codec : []) {
        const baseUrl = codec?.base_url || codec?.baseUrl || "";
        const codecName = codec?.codec_name || "";
        const quality = readPositiveInt(codec?.current_qn, 0);
        for (const urlInfo of Array.isArray(codec?.url_info) ? codec.url_info : []) {
          const fullUrl = combineLiveUrl(urlInfo?.host || "", baseUrl, urlInfo?.extra || "");
          if (!fullUrl) continue;
          candidates.push({ url: fullUrl, quality, score: liveCandidateScore(protocolName, formatName, codecName, quality) });
        }
      }
    }
  }
  candidates.sort((a, b) => b.score - a.score);
  const selected = candidates[0];
  return { directUrl: selected ? selected.url : "", actualQuality: selected ? selected.quality : 0, backupUrls: candidates.slice(1, 6).map((item) => item.url) };
}

function combineLiveUrl(host, baseUrl, extra) {
  if (!host || !baseUrl) return "";
  return (host.endsWith("/") ? host.slice(0, -1) : host) + (baseUrl.startsWith("/") ? baseUrl : "/" + baseUrl) + (extra || "");
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

async function getDanmakuTsv(input, env, ctx) {
  const view = await getVideoView(input, env, ctx);
  const selected = selectVideoPage(view, input);
  return cachedText(`dm:${selected.cid}`, intEnv(env, "DANMAKU_CACHE_TTL_SECONDS", 21600), env, ctx, async () => {
    return buildDanmakuTsv({ bvid: view.bvid || input.bvid || "", aid: view.aid || input.aid || 0, cid: selected.cid, page: selected.page, title: view.title || "", part: selected.part || "", duration: selected.duration || 0 }, env);
  });
}

async function buildDanmakuTsv(video, env) {
  let all = await fetchXmlDanmaku(video.cid, env).catch(() => []);
  if (all.length === 0) {
    const segmentCount = Math.max(1, Math.ceil(Number(video.duration || 0) / 360));
    all = [];
    for (let segment = 1; segment <= segmentCount; segment++) {
      const rows = await fetchDanmakuSegment({ aid: video.aid, cid: video.cid, segment }, env);
      all.push(...rows);
    }
  }
  all.sort((a, b) => a.progress - b.progress || a.id - b.id);
  const header = ["#YBDM/1", `#bvid=${escapeField(video.bvid || "")}`, `#aid=${video.aid || 0}`, `#cid=${video.cid}`, `#page=${video.page || 1}`, `#title=${escapeField(video.title || "")}`, `#part=${escapeField(video.part || "")}`, `#count=${all.length}`, "#columns=progressMs\tmode\tcolor\tfontSize\tpool\tcontent"].join("\n");
  const rows = all.map((item) => [item.progress, item.mode, item.color, item.fontsize, item.pool || 0, escapeField(item.content)].join("\t"));
  return `${header}\n${rows.join("\n")}\n`;
}

async function fetchXmlDanmaku(cid, env) {
  const url = new URL("https://api.bilibili.com/x/v1/dm/list.so");
  url.searchParams.set("oid", String(cid));
  const response = await fetch(url, { headers: biliHeaders(env) });
  if (!response.ok) throw new Error(`Bilibili XML danmaku failed with HTTP ${response.status}.`);
  const xml = await response.text();
  const rows = [];
  const itemPattern = /<d\s+p="([^"]*)">([\s\S]*?)<\/d>/g;
  let match;
  let id = 0;
  while ((match = itemPattern.exec(xml)) !== null) {
    const attrs = match[1].split(",");
    if (attrs.length < 8) continue;
    rows.push({ id: id++, progress: Math.round(Number.parseFloat(attrs[0] || "0") * 1000), mode: Number.parseInt(attrs[1] || "1", 10), fontsize: Number.parseInt(attrs[2] || "25", 10), color: Number.parseInt(attrs[3] || "16777215", 10), pool: Number.parseInt(attrs[5] || "0", 10), content: htmlDecode(match[2]) });
  }
  return rows;
}

async function fetchDanmakuSegment({ aid, cid, segment }, env) {
  const url = new URL("https://api.bilibili.com/x/v2/dm/web/seg.so");
  url.searchParams.set("type", "1");
  url.searchParams.set("oid", String(cid));
  url.searchParams.set("pid", String(aid));
  url.searchParams.set("segment_index", String(segment));
  const response = await fetch(url, { headers: biliHeaders(env) });
  if (!response.ok) throw new Error(`Bilibili danmaku segment ${segment} failed with HTTP ${response.status}.`);
  return decodeDmSegMobileReply(new Uint8Array(await response.arrayBuffer()));
}

async function parseNeteaseInput(rawValue, env, ctx, forcedPage = 0) {
  let normalizedUrl = decodeRepeatedly(rawValue);
  normalizedUrl = unwrapKnownPlayerUrl(normalizedUrl);
  normalizedUrl = decodeRepeatedly(normalizedUrl);
  normalizedUrl = extractSupportedUrlFromText(normalizedUrl);
  normalizedUrl = unwrapKnownPlayerUrl(normalizedUrl);
  normalizedUrl = decodeRepeatedly(normalizedUrl);
  normalizedUrl = await expandNeteaseShortUrl(normalizedUrl, env, ctx);
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
    } catch {}
  }
  for (const candidate of candidates) {
    const path = String(candidate.pathname || "");
    const lowerPath = path.toLowerCase();
    const isPlaylistPath = /(^|\/)(?:f\/|m\/)?playlist(\/|$)/.test(lowerPath) || lowerPath.includes("toplist");
    const isSongPath = /(^|\/)(?:m\/)?song(\/|$)/.test(lowerPath) || lowerPath.includes("/song/media/outer/url");
    if (isPlaylistPath) {
      const playlistId = extractNeteaseId(candidate, "playlist");
      if (playlistId) return { type: "playlist", normalizedUrl, rawInput: rawValue, playlistId, page: Math.max(1, forcedPage || 1) };
    }
    if (isSongPath) {
      const songId = extractNeteaseId(candidate, "song");
      if (songId) return { type: "song", normalizedUrl, rawInput: rawValue, songId, page: 1 };
    }
  }
  return null;
}

async function expandNeteaseShortUrl(value, env, ctx) {
  const parsed = tryParseFlexibleUrl(value);
  if (!parsed || normalizeHost(parsed.hostname) !== "163cn.tv") return value;
  return cachedText(`netease-short:${parsed.href}`, 1800, env, ctx, async () => {
    const json = await znnuGetJson("/api/redirect", { url: parsed.href }, env);
    if (json && json.code === 200 && typeof json.redirectUrl === "string" && json.redirectUrl) return json.redirectUrl;
    throw new Error(json?.msg || json?.message || "NetEase short link redirect failed.");
  });
}

async function resolveNeteaseUrl(input, env, ctx) {
  if (input.type === "playlist") {
    const playlist = await getNeteasePlaylist(input, env, ctx);
    if (!playlist.tracks.length) throw new Error(`NetEase playlist ${input.playlistId} has no tracks.`);
    const index = Math.min(Math.max(input.page || 1, 1), playlist.tracks.length) - 1;
    const track = playlist.tracks[index];
    if (!track?.id) throw new Error(`NetEase playlist ${input.playlistId} track ${index + 1} has no song id.`);
    const song = await getNeteaseSongDirect({ songId: String(track.id), rawInput: String(track.id), level: input.level }, env, ctx);
    return { ...song, playlistId: String(playlist.id || input.playlistId), playlistName: playlist.name || "", selectedPage: index + 1, totalTracks: playlist.tracks.length };
  }
  return getNeteaseSongDirect(input, env, ctx);
}

async function getNeteasePlaylist(input, env, ctx) {
  return cachedJson(`netease-playlist:${input.playlistId}`, intEnv(env, "NETEASE_PLAYLIST_CACHE_TTL_SECONDS", 1800), env, ctx, async () => {
    const ip = await getZnnuIp(env, ctx);
    const decoded = await postZnnuForm("/api/playlist", { act: "playlist", id: input.playlistId, rawInput: input.normalizedUrl || input.rawInput || input.playlistId, ip }, env, ctx);
    if (decoded.code !== 200) throw new Error(decoded.msg || decoded.message || `NetEase playlist ${input.playlistId} resolve failed.`);
    const data = decoded.data || {};
    return { id: data.id || input.playlistId, name: data.name || "", trackCount: Number(data.trackCount || 0), tracks: Array.isArray(data.tracks) ? data.tracks : [] };
  });
}

async function getNeteaseSongDirect(input, env, ctx) {
  const level = normalizeNeteaseLevel(input.level || getEnv(env, "NETEASE_LEVEL", "standard"));
  return cachedJson(`netease-song:${input.songId}:${level}`, intEnv(env, "NETEASE_URL_CACHE_TTL_SECONDS", 600), env, ctx, async () => {
    const ip = await getZnnuIp(env, ctx);
    const decoded = await postZnnuForm("/api/song", { act: "song", id: input.songId, level, rawInput: input.normalizedUrl || input.rawInput || input.songId, ip }, env, ctx);
    if (decoded.code !== 200) throw new Error(decoded.msg || decoded.message || `NetEase song ${input.songId} resolve failed.`);
    const data = decoded.data || {};
    const audioUrl = normalizePlayableUrl(data.url);
    if (!audioUrl) throw new Error(decoded.msg || decoded.message || `NetEase song ${input.songId} has no playable url.`);
    return { provider: "netease", audioUrl, songId: String(input.songId), name: data.name || "", artist: data.artist || "", album: data.album || "", level: data.level || level };
  });
}

async function postZnnuForm(path, payload, env, ctx) {
  const session = await getZnnuKeySession(env, ctx);
  const signed = await signZnnuPayload(payload);
  const body = new URLSearchParams({ ...payload, signature: signed.signature, timestamp: String(signed.timestamp), domain: signed.domain });
  const json = await znnuFetchJson(path, env, { method: "POST", headers: znnuHeaders(env, { "Content-Type": "application/x-www-form-urlencoded", "X-Key-Token": session.keyToken }), body });
  return decodeZnnuResponse(json, session.key);
}

async function getZnnuKeySession(env, ctx) {
  return cachedJson("znnu-key-session", 300, env, ctx, async () => {
    const json = await znnuGetJson("/api/key", {}, env);
    const data = json?.data || null;
    if (json.code !== 200 || !data?.key || !data?.keyToken || !data?.expireAt) throw new Error(json?.msg || json?.message || "Failed to get ZNNU key.");
    return { key: data.key, keyToken: data.keyToken, expireAt: readPositiveInt(data.expireAt, 0) };
  });
}

async function getZnnuIp(env, ctx) {
  return cachedText("znnu-ip", 3600, env, ctx, async () => {
    try {
      const json = await znnuGetJson("/api/ip", {}, env);
      return typeof json?.ip === "string" ? json.ip : "";
    } catch {
      return "";
    }
  });
}

async function znnuGetJson(path, query = {}, env) {
  const url = new URL(path, getEnv(env, "ZNNU_BASE_URL", "https://music.znnu.com"));
  for (const [key, value] of Object.entries(query)) {
    if (value !== undefined && value !== null && value !== "") url.searchParams.set(key, String(value));
  }
  return znnuFetchJson(url, env);
}

async function znnuFetchJson(urlOrPath, env, options = {}) {
  const url = urlOrPath instanceof URL ? urlOrPath : new URL(urlOrPath, getEnv(env, "ZNNU_BASE_URL", "https://music.znnu.com"));
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), intEnv(env, "ZNNU_FETCH_TIMEOUT_SECONDS", 30) * 1000);
  try {
    const response = await fetch(url, { method: options.method || "GET", headers: options.headers || znnuHeaders(env), body: options.body, signal: controller.signal });
    const responseText = await response.text();
    if (!response.ok) throw new Error(`ZNNU API failed with HTTP ${response.status}: ${responseText.slice(0, 200)}`);
    return JSON.parse(responseText);
  } finally {
    clearTimeout(timeout);
  }
}

function znnuHeaders(env, extra = {}) {
  return { "Accept": "application/json, text/plain, */*", "User-Agent": getUserAgent(env), "X-Referer": ZNNU_REFERER, ...extra };
}

async function signZnnuPayload(payload) {
  const timestamp = Math.floor(Date.now() / 1000);
  const cleanPayload = { ...payload };
  delete cleanPayload.signature;
  delete cleanPayload.timestamp;
  delete cleanPayload.domain;
  delete cleanPayload.ver;
  const signString = Object.keys(cleanPayload).sort().reduce((result, key) => result + key + "=" + cleanPayload[key], String(timestamp) + ZNNU_SIGNATURE_DOMAIN);
  const key = await crypto.subtle.importKey("raw", encodeUtf8(ZNNU_SIGNATURE_SECRET), { name: "HMAC", hash: "SHA-256" }, false, ["sign"]);
  const signatureBytes = new Uint8Array(await crypto.subtle.sign("HMAC", key, encodeUtf8(signString)));
  return { signature: bytesToHex(signatureBytes), timestamp, domain: ZNNU_SIGNATURE_DOMAIN };
}

async function decodeZnnuResponse(json, keyBase64) {
  if (!json?.data || json.data.enc !== 1 || json.data.alg !== "AES-256-GCM") return json;
  const keyBytes = base64ToBytes(String(keyBase64 || ""));
  const iv = base64ToBytes(String(json.data.iv || ""));
  const ciphertext = base64ToBytes(String(json.data.ciphertext || ""));
  const tag = base64ToBytes(String(json.data.tag || ""));
  const combined = new Uint8Array(ciphertext.length + tag.length);
  combined.set(ciphertext, 0);
  combined.set(tag, ciphertext.length);
  const key = await crypto.subtle.importKey("raw", keyBytes, { name: "AES-GCM" }, false, ["decrypt"]);
  const decrypted = await crypto.subtle.decrypt({ name: "AES-GCM", iv }, key, combined);
  return { ...json, data: JSON.parse(decodeUtf8(new Uint8Array(decrypted))) };
}

async function fetchJson(url, env) {
  const response = await fetch(url, { headers: biliHeaders(env) });
  const responseText = await response.text();
  if (!response.ok) throw new Error(`Bilibili API failed with HTTP ${response.status}.`);
  return JSON.parse(responseText);
}

function biliHeaders(env) {
  const headers = { "User-Agent": getUserAgent(env), "Referer": "https://www.bilibili.com/", "Accept": "*/*" };
  const cookie = getEnv(env, "BILIBILI_COOKIE", "") || getEnv(env, "BILI_COOKIE", "");
  if (cookie) headers.Cookie = cookie;
  return headers;
}

async function cachedJson(key, ttlSeconds, env, ctx, loader) {
  const textValue = await cachedText(`json:${key}`, ttlSeconds, env, ctx, async () => JSON.stringify(await loader()));
  return JSON.parse(textValue);
}

async function cachedText(key, ttlSeconds, _env, ctx, loader) {
  const now = Date.now();
  const memory = memoryCache.get(key);
  if (memory && now - memory.time < ttlSeconds * 1000) return memory.value;

  const cache = globalThis.caches?.default;
  const cacheRequest = new Request(`https://paulkoi-worker-cache.local/${encodeURIComponent(key)}`);
  if (cache) {
    const cached = await cache.match(cacheRequest);
    if (cached) return cached.text();
  }

  const value = String(await loader());
  memoryCache.set(key, { time: now, value });
  if (cache) {
    const response = new Response(value, { headers: { "Cache-Control": `public, max-age=${ttlSeconds}` } });
    const put = cache.put(cacheRequest, response);
    if (ctx && typeof ctx.waitUntil === "function") ctx.waitUntil(put);
    else await put;
  }
  return value;
}

function isDanmakuRequest(request, requestUrl) {
  if (requestUrl.searchParams.get("__dm") === "1") return true;
  const accept = String(request.headers.get("accept") || "").toLowerCase();
  const userAgent = String(request.headers.get("user-agent") || "").toLowerCase();
  const requestedWith = String(request.headers.get("x-requested-with") || "").toLowerCase();
  const secFetchDest = String(request.headers.get("sec-fetch-dest") || "").toLowerCase();
  const range = String(request.headers.get("range") || "").toLowerCase();
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
      } catch {}
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
      case 1: if (wire === 0) item.id = Number(reader.varint()); else reader.skip(wire); break;
      case 2: if (wire === 0) item.progress = Number(reader.varint()); else reader.skip(wire); break;
      case 3: if (wire === 0) item.mode = Number(reader.varint()); else reader.skip(wire); break;
      case 4: if (wire === 0) item.fontsize = Number(reader.varint()); else reader.skip(wire); break;
      case 5: if (wire === 0) item.color = Number(reader.varint()); else reader.skip(wire); break;
      case 7: if (wire === 2) item.content = reader.string(); else reader.skip(wire); break;
      case 11: if (wire === 0) item.pool = Number(reader.varint()); else reader.skip(wire); break;
      default: reader.skip(wire); break;
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
    return decodeUtf8(this.bytes());
  }
  skip(wire) {
    switch (wire) {
      case 0: this.varint(); break;
      case 1: this.pos += 8; break;
      case 2: this.pos += Number(this.varint()); break;
      case 3: this.skipGroup(); break;
      case 4: break;
      case 5: this.pos += 4; break;
      default: this.pos = this.bytesArray.length; break;
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

function buildWorkerStats(env) {
  return {
    runtime: "cloudflare-workers",
    cache: "Cache API with per-isolate memory fallback",
    bilibiliOrigin: getDockerOrigin(env),
    persistentCounters: false,
    ttlSeconds: {
      view: intEnv(env, "VIEW_CACHE_TTL_SECONDS", 1800),
      videoUrl: intEnv(env, "VIDEO_URL_CACHE_TTL_SECONDS", 600),
      danmaku: intEnv(env, "DANMAKU_CACHE_TTL_SECONDS", 21600),
      neteasePlaylist: intEnv(env, "NETEASE_PLAYLIST_CACHE_TTL_SECONDS", 1800),
      neteaseUrl: intEnv(env, "NETEASE_URL_CACHE_TTL_SECONDS", 600)
    },
    memoryEntries: memoryCache.size
  };
}

async function proxyToDocker(request, env, requestUrl) {
  const target = new URL(`${requestUrl.pathname}${requestUrl.search}`, getDockerOrigin(env));
  const headers = new Headers();
  for (const name of ["accept", "accept-language", "content-type", "range", "user-agent"]) {
    const value = request.headers.get(name);
    if (value) headers.set(name, value);
  }
  const cfIp = request.headers.get("cf-connecting-ip");
  const forwardedFor = request.headers.get("x-forwarded-for");
  if (cfIp) headers.set("CF-Connecting-IP", cfIp);
  if (forwardedFor || cfIp) headers.set("X-Forwarded-For", forwardedFor || cfIp);
  headers.set("X-Worker-Proxy", "paulkoi-danmaku-worker");

  const response = await fetch(target, {
    method: request.method,
    headers,
    redirect: "manual"
  });
  return withCors(response);
}

function isBilibiliLikeSource(value) {
  const decoded = decodeRepeatedly(value);
  const unwrapped = unwrapKnownPlayerUrl(decoded);
  const extracted = extractSupportedUrlFromText(decodeRepeatedly(unwrapped));
  const candidates = [value, decoded, unwrapped, decodeRepeatedly(unwrapped), extracted];
  return candidates.some((candidate) => {
    const textValue = String(candidate || "");
    if (/BV[0-9A-Za-z]{10,}/i.test(textValue) || /(?:^|[?&/])av\d+/i.test(textValue)) return true;
    const parsed = tryParseFlexibleUrl(textValue);
    if (!parsed) return false;
    const host = normalizeHost(parsed.hostname);
    return host === "bilibili.com" ||
      host.endsWith(".bilibili.com") ||
      isBilibiliShortHost(host);
  });
}

function getDockerOrigin(env) {
  return normalizeOrigin(getEnv(env, "BILI_DOCKER_ORIGIN", "https://danmaku.paulkoishi.com"));
}

function normalizeOrigin(value) {
  const fallback = "https://danmaku.paulkoishi.com";
  const parsed = tryParseUrl(String(value || "").trim());
  if (!parsed || !/^https?:$/.test(parsed.protocol)) return fallback;
  return parsed.origin;
}

function renderHome(env) {
  return `<!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>PaulKoiPlayer Worker</title><style>body{font-family:system-ui,"Microsoft YaHei",sans-serif;margin:0;background:#f6f7f9;color:#172026}.shell{max-width:880px;margin:0 auto;padding:40px 20px}.panel{background:#fff;border:1px solid #dce3e8;border-radius:8px;padding:24px;box-shadow:0 16px 40px rgba(25,38,49,.1)}code{display:block;background:#f2f5f7;border:1px solid #dce3e8;border-radius:8px;padding:12px;overflow:auto}</style></head><body><main class="shell"><section class="panel"><h1>PaulKoiPlayer Worker</h1><p>Cloudflare Workers 核心解析版。时间显示：${escapeHtml(getEnv(env, "DISPLAY_TIME_ZONE", "Asia/Shanghai"))}</p><code>GET /player/?url=&lt;Bilibili / Live / NetEase URL&gt;</code><code>GET /api/danmaku?url=&lt;Bilibili URL&gt;</code><code>GET /health</code></section></main></body></html>`;
}

function redirect(location) {
  return withCors(new Response(null, { status: 302, headers: { Location: location, ...noStoreHeaders() } }));
}

function text(body, status = 200, extraHeaders = {}) {
  return withCors(new Response(body, { status, headers: { "Content-Type": "text/plain; charset=utf-8", ...extraHeaders } }));
}

function html(body, status = 200) {
  return withCors(new Response(body, { status, headers: { "Content-Type": "text/html; charset=utf-8", ...noStoreHeaders() } }));
}

function json(body, status = 200, extraHeaders = {}) {
  return withCors(new Response(`${JSON.stringify(body, null, 2)}\n`, { status, headers: { "Content-Type": "application/json; charset=utf-8", ...extraHeaders } }));
}

function withCors(response) {
  const headers = new Headers(response.headers);
  headers.set("Access-Control-Allow-Origin", "*");
  headers.set("Access-Control-Allow-Methods", "GET, HEAD, OPTIONS");
  headers.set("Access-Control-Allow-Headers", "Content-Type, Range");
  return new Response(response.body, { status: response.status, statusText: response.statusText, headers });
}

function noStoreHeaders() {
  return { "Cache-Control": "no-store", "Pragma": "no-cache", "Expires": "0" };
}

function danmakuCacheHeaders() {
  return { "Cache-Control": "public, max-age=3600, s-maxage=86400" };
}

function getUserAgent(env) {
  return getEnv(env, "BILI_USER_AGENT", DEFAULT_USER_AGENT);
}

function getEnv(env, name, fallback = "") {
  const value = env?.[name];
  return value === undefined || value === null || value === "" ? fallback : String(value);
}

function intEnv(env, name, fallback) {
  const parsed = Number.parseInt(getEnv(env, name, ""), 10);
  return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback;
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
  const textValue = String(value || "");
  const match = textValue.match(/(?:^|[?&/])av(\d+)/i) || textValue.match(/[?&](?:aid|avid|oid)=(\d+)/i);
  return match ? match[1] : "";
}

function readPositiveInt(value, fallback) {
  const parsed = Number.parseInt(value || "", 10);
  return Number.isFinite(parsed) && parsed >= 1 ? parsed : fallback;
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
  const textValue = String(value || "").trim();
  if (!textValue) return null;
  const parsed = tryParseUrl(textValue);
  if (parsed) return parsed;
  if (/^[a-z0-9.-]+\//i.test(textValue) || /^[a-z0-9.-]+\?/i.test(textValue)) return tryParseUrl(`https://${textValue}`);
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
  return parsed.pathname.replace(/\/+$/, "") === "";
}

function extractNeteaseId(candidate, type) {
  const fromQuery = normalizeNumericId(candidate.searchParams.get("id") || "");
  if (fromQuery) return fromQuery;
  const path = String(candidate.pathname || "");
  const pattern = type === "playlist" ? /\/(?:m\/)?playlist\/(\d+)/i : /\/(?:m\/)?song\/(\d+)/i;
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
  const textValue = String(value || "").trim().toLowerCase();
  return ["standard", "exhigh", "lossless", "hires", "sky", "jyeffect", "jymaster"].includes(textValue) ? textValue : "standard";
}

function previewUrl(url) {
  return url ? `${String(url).slice(0, 120)}...` : "";
}

function htmlDecode(value) {
  return String(value || "").replace(/&lt;/g, "<").replace(/&gt;/g, ">").replace(/&quot;/g, "\"").replace(/&#39;/g, "'").replace(/&apos;/g, "'").replace(/&amp;/g, "&");
}

function escapeField(value) {
  return String(value ?? "").replace(/\\/g, "\\\\").replace(/\t/g, "\\t").replace(/\r/g, "\\r").replace(/\n/g, "\\n");
}

function escapeHtml(value) {
  return String(value ?? "").replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");
}

function encodeUtf8(value) {
  return new TextEncoder().encode(String(value));
}

function decodeUtf8(bytes) {
  return new TextDecoder("utf-8").decode(bytes);
}

function bytesToHex(bytes) {
  return [...bytes].map((byte) => byte.toString(16).padStart(2, "0")).join("");
}

function base64ToBytes(value) {
  const binary = atob(value);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
  return bytes;
}
