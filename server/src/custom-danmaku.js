/**
 * Custom Danmaku parser, validator, and formatter for PaulKoiPlayer.
 * Shared between Node.js server and Cloudflare Worker.
 */

const RESERVED_CUSTOM_IDS = new Set([
  "player",
  "api",
  "admin",
  "health",
  "cache",
  "stats",
  "danmaku",
  "lyrics",
  "resolve",
  "pages",
  "current",
  "set"
]);

const PROTOTYPE_BLACKLIST = new Set([
  "__proto__",
  "constructor",
  "prototype",
  "tostring",
  "valueof",
  "hasownproperty",
  "isprototypeof",
  "propertyisenumerable",
  "tolocalestring"
]);

export function parseStrictNumber(val) {
  if (typeof val === "number") {
    return Number.isFinite(val) ? val : null;
  }
  if (typeof val === "string") {
    const s = val.trim();
    if (!s) return null;
    if (!/^[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?$/.test(s)) return null;
    const num = Number(s);
    return Number.isFinite(num) ? num : null;
  }
  return null;
}

export function parseStrictInt(val) {
  if (typeof val === "number") {
    return Number.isSafeInteger(val) ? val : null;
  }
  if (typeof val === "string") {
    const s = val.trim();
    if (!s) return null;
    if (!/^[-+]?\d+$/.test(s)) return null;
    const num = Number(s);
    return Number.isSafeInteger(num) ? num : null;
  }
  return null;
}

export function validateCustomId(rawId) {
  const id = String(rawId || "").trim();
  if (!id) {
    return { ok: false, valid: false, error: "custom_id_empty", message: "Custom ID cannot be empty." };
  }
  if (id.length > 64) {
    return { ok: false, valid: false, error: "custom_id_too_long", message: "Custom ID length cannot exceed 64 characters." };
  }
  if (!/^[a-zA-Z0-9_-]+$/.test(id)) {
    return { ok: false, valid: false, error: "custom_id_invalid_characters", message: "Custom ID may only contain letters, numbers, underscores, and hyphens." };
  }
  const lower = id.toLowerCase();
  if (PROTOTYPE_BLACKLIST.has(lower)) {
    return { ok: false, valid: false, error: "custom_id_reserved", message: `Custom ID '${id}' is a reserved property name.` };
  }
  if (RESERVED_CUSTOM_IDS.has(lower)) {
    return { ok: false, valid: false, error: "custom_id_reserved", message: `Custom ID '${id}' is a reserved system path.` };
  }
  // Prevent collision / hijacking of Bilibili BV or AV identifiers
  if (/^BV[0-9A-Za-z]{10,}$/i.test(id)) {
    return { ok: false, valid: false, error: "custom_id_bilibili_conflict", message: "Custom ID cannot be a Bilibili BV identifier." };
  }
  if (/^av\d+$/i.test(id)) {
    return { ok: false, valid: false, error: "custom_id_bilibili_conflict", message: "Custom ID cannot be a Bilibili AV identifier." };
  }
  return { ok: true, valid: true, id };
}

export function validateVideoUrl(rawUrl) {
  const text = String(rawUrl || "").trim();
  if (!text) {
    return { ok: false, valid: false, error: "video_url_empty", message: "Video direct link cannot be empty." };
  }
  if (text.length > 2048) {
    return { ok: false, valid: false, error: "video_url_too_long", message: "Video direct link length cannot exceed 2048 characters." };
  }
  let parsed;
  try {
    parsed = new URL(text);
  } catch {
    return { ok: false, valid: false, error: "video_url_invalid", message: "Video direct link must be a valid URL." };
  }
  const proto = parsed.protocol.toLowerCase();
  if (proto !== "http:" && proto !== "https:") {
    return { ok: false, valid: false, error: "video_url_unsupported_protocol", message: "Video direct link must use HTTP or HTTPS protocol (local file:// or drive paths are not supported)." };
  }
  return { ok: true, valid: true, url: parsed.href };
}

export function escapeField(value) {
  return String(value || "")
    .replace(/\\/g, "\\\\")
    .replace(/\t/g, "\\t")
    .replace(/\n/g, "\\n")
    .replace(/\r/g, "\\r");
}

export function unescapeField(value) {
  const str = String(value || "");
  let result = "";
  let i = 0;
  const len = str.length;
  while (i < len) {
    if (str[i] === "\\" && i + 1 < len) {
      const nextChar = str[i + 1];
      if (nextChar === "\\") {
        result += "\\";
        i += 2;
        continue;
      }
      if (nextChar === "t") {
        result += "\t";
        i += 2;
        continue;
      }
      if (nextChar === "n") {
        result += "\n";
        i += 2;
        continue;
      }
      if (nextChar === "r") {
        result += "\r";
        i += 2;
        continue;
      }
      result += "\\" + nextChar;
      i += 2;
      continue;
    }
    result += str[i];
    i++;
  }
  return result;
}

export function isValidXmlCodePoint(code) {
  if (code === 0x9 || code === 0xa || code === 0xd) return true;
  if (code >= 0x20 && code <= 0xd7ff) return true;
  if (code >= 0xe000 && code <= 0xfffd) return true;
  if (code >= 0x10000 && code <= 0x10ffff) return true;
  return false;
}

export function validateRowData(row, formatName = "danmaku") {
  const { progressMs, mode, fontSize, color, pool } = row;
  if (typeof progressMs !== "number" || !Number.isSafeInteger(progressMs) || progressMs < 0 || progressMs > 2147483647) {
    throw new Error(`${formatName}_invalid_progressMs`);
  }
  if (typeof mode !== "number" || !Number.isSafeInteger(mode) || mode < 1 || mode > 9) {
    throw new Error(`${formatName}_invalid_mode`);
  }
  if (typeof fontSize !== "number" || !Number.isSafeInteger(fontSize) || fontSize < 1 || fontSize > 1000) {
    throw new Error(`${formatName}_invalid_fontsize`);
  }
  if (typeof color !== "number" || !Number.isSafeInteger(color) || color < 0 || color > 16777215) {
    throw new Error(`${formatName}_invalid_color`);
  }
  if (typeof pool !== "number" || !Number.isSafeInteger(pool) || pool < 0 || pool > 2) {
    throw new Error(`${formatName}_invalid_pool`);
  }
}

export function decodeStrictXmlEntities(str) {
  let result = "";
  let i = 0;
  const len = str.length;
  while (i < len) {
    if (str.startsWith("<![CDATA[", i)) {
      const cdataEnd = str.indexOf("]]>", i + 9);
      if (cdataEnd === -1) throw new Error("xml_unclosed_cdata");
      result += str.slice(i + 9, cdataEnd);
      i = cdataEnd + 3;
      continue;
    }
    if (str[i] === "&") {
      const semi = str.indexOf(";", i + 1);
      if (semi === -1 || semi - i > 12) {
        throw new Error("xml_malformed_entity");
      }
      const entity = str.slice(i, semi + 1);
      if (entity === "&quot;") {
        result += '"';
      } else if (entity === "&apos;") {
        result += "'";
      } else if (entity === "&lt;") {
        result += "<";
      } else if (entity === "&gt;") {
        result += ">";
      } else if (entity === "&amp;") {
        result += "&";
      } else if (/^&#(\d+);$/.test(entity)) {
        const code = parseInt(entity.slice(2, -1), 10);
        if (!Number.isFinite(code) || !isValidXmlCodePoint(code)) throw new Error("xml_invalid_codepoint");
        result += String.fromCodePoint(code);
      } else if (/^&#x([0-9a-fA-F]+);$/i.test(entity)) {
        const code = parseInt(entity.slice(3, -1), 16);
        if (!Number.isFinite(code) || !isValidXmlCodePoint(code)) throw new Error("xml_invalid_codepoint");
        result += String.fromCodePoint(code);
      } else {
        throw new Error(`xml_unknown_entity: ${entity}`);
      }
      i = semi + 1;
      continue;
    }
    if (str[i] === "<") {
      throw new Error("xml_unescaped_lt");
    }
    result += str[i];
    i++;
  }
  return result;
}

export function parseXmlAttributes(attrStr) {
  const attrs = Object.create(null);
  let pos = 0;
  const len = attrStr.length;
  while (pos < len) {
    while (pos < len && /\s/.test(attrStr[pos])) pos++;
    if (pos >= len) break;

    const startName = pos;
    while (pos < len && /[a-zA-Z0-9:_-]/.test(attrStr[pos])) pos++;
    if (pos === startName) {
      throw new Error(`xml_invalid_attribute_token: ${attrStr.slice(pos)}`);
    }
    const attrName = attrStr.slice(startName, pos);

    while (pos < len && /\s/.test(attrStr[pos])) pos++;
    if (pos >= len || attrStr[pos] !== "=") {
      throw new Error(`xml_missing_attribute_equals: ${attrName}`);
    }
    pos++; // skip '='

    while (pos < len && /\s/.test(attrStr[pos])) pos++;
    if (pos >= len) throw new Error(`xml_unclosed_attribute_quote: ${attrName}`);

    const quoteChar = attrStr[pos];
    if (quoteChar !== '"' && quoteChar !== "'") {
      throw new Error(`xml_unquoted_attribute_value: ${attrName}`);
    }
    pos++; // skip quote

    const valStart = pos;
    let foundClose = false;
    while (pos < len) {
      if (attrStr[pos] === quoteChar) {
        foundClose = true;
        break;
      }
      pos++;
    }
    if (!foundClose) {
      throw new Error(`xml_unclosed_attribute_quote: ${attrName}`);
    }
    const rawVal = attrStr.slice(valStart, pos);
    pos++; // skip closing quote

    if (Object.prototype.hasOwnProperty.call(attrs, attrName)) {
      throw new Error(`xml_duplicate_attribute: ${attrName}`);
    }

    attrs[attrName] = decodeStrictXmlEntities(rawVal);
  }
  return attrs;
}

export function parseXmlDanmaku(xmlText) {
  if (typeof xmlText !== "string") {
    throw new Error("xml_must_be_string");
  }
  const trimmed = xmlText.trim();
  if (!trimmed) {
    throw new Error("empty_xml");
  }

  // Strict security check: reject DTD / Entity definitions / XXE
  if (
    /<!DOCTYPE/i.test(trimmed) ||
    /<!ENTITY/i.test(trimmed) ||
    /SYSTEM\s+["']/i.test(trimmed) ||
    /PUBLIC\s+["']/i.test(trimmed)
  ) {
    throw new Error("xml_dtd_forbidden");
  }

  let pos = 0;
  const len = trimmed.length;

  // Skip XML declaration if present: <?xml ... ?>
  if (trimmed.startsWith("<?xml")) {
    const declEnd = trimmed.indexOf("?>", 5);
    if (declEnd === -1) throw new Error("xml_malformed_declaration");
    pos = declEnd + 2;
  }

  const tagStack = [];
  let rootEncountered = false;
  let rootClosed = false;
  let title = "";
  const rows = [];
  let idx = 0;

  while (pos < len) {
    if (rootClosed) {
      const rest = trimmed.slice(pos).trim();
      if (rest.length > 0) throw new Error("xml_trailing_garbage");
      break;
    }

    const nextLt = trimmed.indexOf("<", pos);
    if (nextLt === -1) {
      if (tagStack.length > 0) throw new Error("xml_unclosed_tag");
      break;
    }

    if (nextLt > pos) {
      const textChunk = trimmed.slice(pos, nextLt);
      if (tagStack.length === 0) {
        if (textChunk.trim()) throw new Error("xml_text_outside_root");
      } else {
        // Validate entities in text chunk between tags
        decodeStrictXmlEntities(textChunk);
      }
    }

    pos = nextLt;

    // Check comment <!-- ... -->
    if (trimmed.startsWith("<!--", pos)) {
      const commentEnd = trimmed.indexOf("-->", pos + 4);
      if (commentEnd === -1) throw new Error("xml_unclosed_comment");
      pos = commentEnd + 3;
      continue;
    }

    // Check CDATA outside element
    if (trimmed.startsWith("<![CDATA[", pos)) {
      if (tagStack.length === 0) throw new Error("xml_cdata_outside_root");
    }

    // Closing tag </tag>
    if (trimmed.startsWith("</", pos)) {
      const closeGt = trimmed.indexOf(">", pos + 2);
      if (closeGt === -1) throw new Error("xml_malformed_closing_tag");
      const closeName = trimmed.slice(pos + 2, closeGt).trim();
      if (!tagStack.length) throw new Error("xml_unexpected_closing_tag");
      const expected = tagStack.pop();
      if (expected.name !== closeName) {
        throw new Error(`xml_mismatched_tag: expected </${expected.name}>, found </${closeName}>`);
      }
      if (tagStack.length === 0) {
        rootClosed = true;
      }
      pos = closeGt + 1;
      continue;
    }

    // Start tag <tag ...>
    const nextGt = trimmed.indexOf(">", pos + 1);
    if (nextGt === -1) throw new Error("xml_malformed_tag");

    const isSelfClosing = trimmed[nextGt - 1] === "/";
    const tagInner = isSelfClosing ? trimmed.slice(pos + 1, nextGt - 1).trim() : trimmed.slice(pos + 1, nextGt).trim();
    const spaceMatch = tagInner.match(/\s/);
    const spaceIdx = spaceMatch ? spaceMatch.index : -1;
    const tagName = (spaceIdx === -1 ? tagInner : tagInner.slice(0, spaceIdx)).trim();
    if (!/^[a-zA-Z0-9:_-]+$/.test(tagName)) throw new Error(`xml_invalid_tag_name: ${tagName}`);

    if (!rootEncountered) {
      rootEncountered = true;
      const lowerName = tagName.toLowerCase();
      if (lowerName !== "i" && lowerName !== "xml") {
        throw new Error(`xml_invalid_root_tag: ${tagName}`);
      }
      if (isSelfClosing) {
        throw new Error("xml_empty_root_no_danmaku");
      }
    } else if (tagStack.length === 0) {
      throw new Error("xml_multiple_roots");
    }

    const attrs = spaceIdx !== -1 ? parseXmlAttributes(tagInner.slice(spaceIdx).trim()) : Object.create(null);

    if (isSelfClosing) {
      pos = nextGt + 1;
      continue;
    }

    tagStack.push({ name: tagName });
    pos = nextGt + 1;

    // Danmaku 'd' element: must be direct child of root
    if (tagName === "d") {
      if (tagStack.length !== 2) {
        throw new Error("xml_d_not_direct_child_of_root");
      }

      // Read element body supporting CDATA containing literal </d>
      let dBody = "";
      let dClosed = false;
      const closeTag = "</d>";
      while (pos < len) {
        if (trimmed.startsWith("<![CDATA[", pos)) {
          const cdataEnd = trimmed.indexOf("]]>", pos + 9);
          if (cdataEnd === -1) throw new Error("xml_unclosed_cdata");
          dBody += trimmed.slice(pos + 9, cdataEnd);
          pos = cdataEnd + 3;
          continue;
        }
        if (trimmed.startsWith(closeTag, pos)) {
          dClosed = true;
          pos += closeTag.length;
          break;
        }
        const innerLt = trimmed.indexOf("<", pos);
        if (innerLt === -1) throw new Error("xml_unclosed_d_tag");
        const chunk = trimmed.slice(pos, innerLt);
        dBody += decodeStrictXmlEntities(chunk);
        pos = innerLt;
        if (!trimmed.startsWith("<![CDATA[", pos) && !trimmed.startsWith(closeTag, pos)) {
          throw new Error("xml_unexpected_tag_in_d");
        }
      }
      if (!dClosed) throw new Error("xml_unclosed_d_tag");
      tagStack.pop();

      const pAttr = attrs["p"];
      if (typeof pAttr !== "string") {
        throw new Error("xml_missing_p_attribute");
      }
      const pParts = pAttr.split(",");
      if (pParts.length < 4) {
        throw new Error("xml_malformed_p_attribute");
      }

      const sec = parseStrictNumber(pParts[0]);
      if (sec === null || sec < 0) throw new Error("xml_invalid_progress");
      const progressMs = Math.round(sec * 1000);

      const mode = parseStrictInt(pParts[1]);
      if (mode === null) throw new Error("xml_invalid_mode");

      const fontSize = parseStrictInt(pParts[2]);
      if (fontSize === null) throw new Error("xml_invalid_fontsize");

      const color = parseStrictInt(pParts[3]);
      if (color === null) throw new Error("xml_invalid_color");

      let pool = 0;
      if (pParts.length > 5 && pParts[5] !== undefined && pParts[5].trim() !== "") {
        const parsedPool = parseStrictInt(pParts[5]);
        if (parsedPool === null) throw new Error("xml_invalid_pool");
        pool = parsedPool;
      }

      validateRowData({ progressMs, mode, fontSize, color, pool }, "xml");

      const content = dBody.trim();
      if (!content) continue;

      // Existing frontend ignore rules:
      // pool 2 (special pool), modes 7/8/9 (code/special) ignored
      if (pool === 2 || mode === 7 || mode === 8 || mode === 9) continue;

      rows.push({
        _idx: idx++,
        progressMs,
        mode,
        fontSize,
        color,
        pool,
        content: content.slice(0, 500)
      });
      continue;
    }

    if (tagName === "title" && tagStack.length === 2) {
      const closeTag = "</title>";
      const endIdx = trimmed.indexOf(closeTag, pos);
      if (endIdx === -1) throw new Error("xml_unclosed_title_tag");
      const rawTitle = trimmed.slice(pos, endIdx);
      tagStack.pop();
      pos = endIdx + closeTag.length;
      title = decodeStrictXmlEntities(rawTitle).trim();
      continue;
    }
  }

  if (tagStack.length > 0) throw new Error("xml_unclosed_tag");
  if (!rootClosed) throw new Error("xml_missing_root_closure");
  if (rows.length === 0) throw new Error("xml_no_danmaku_rows");

  return { title, rows };
}

export function parseJsonDanmaku(jsonText) {
  let parsed;
  try {
    parsed = typeof jsonText === "string" ? JSON.parse(jsonText) : jsonText;
  } catch (err) {
    throw new Error(`invalid_json_syntax: ${err.message}`);
  }

  let title = "";
  let list = [];
  if (Array.isArray(parsed)) {
    list = parsed;
  } else if (parsed && typeof parsed === "object") {
    title = String(parsed.meta?.title || parsed.title || "").trim();
    if (Array.isArray(parsed.danmaku)) {
      list = parsed.danmaku;
    } else if (Array.isArray(parsed.items)) {
      list = parsed.items;
    } else if (Array.isArray(parsed.list)) {
      list = parsed.list;
    } else {
      throw new Error("missing_danmaku_array");
    }
  } else {
    throw new Error("invalid_json_structure");
  }

  const rows = [];
  let idx = 0;
  for (const item of list) {
    if (!item || typeof item !== "object") continue;

    let progressMs = -1;
    // Explicit supported fields:
    // 1. progress_sec: seconds
    // 2. progressMs or timeMs: milliseconds
    // 3. progress: strictly fixed as Bilibili standard milliseconds (no heuristic guessing)
    if (item.progress_sec !== undefined && item.progress_sec !== null) {
      const sec = parseStrictNumber(item.progress_sec);
      if (sec === null || sec < 0) throw new Error("json_invalid_progress_sec");
      progressMs = Math.round(sec * 1000);
    } else if (item.progressMs !== undefined && item.progressMs !== null) {
      const ms = parseStrictNumber(item.progressMs);
      if (ms === null || ms < 0) throw new Error("json_invalid_progressMs");
      progressMs = Math.round(ms);
    } else if (item.timeMs !== undefined && item.timeMs !== null) {
      const ms = parseStrictNumber(item.timeMs);
      if (ms === null || ms < 0) throw new Error("json_invalid_timeMs");
      progressMs = Math.round(ms);
    } else if (item.progress !== undefined && item.progress !== null) {
      const ms = parseStrictNumber(item.progress);
      if (ms === null || ms < 0) throw new Error("json_invalid_progress");
      progressMs = Math.round(ms);
    } else {
      throw new Error("json_missing_supported_time_field");
    }

    let mode = 1;
    if (item.mode !== undefined && item.mode !== null) {
      const m = parseStrictInt(item.mode);
      if (m === null) throw new Error("json_invalid_mode");
      mode = m;
    }

    let fontSize = 25;
    const rawFs = item.fontsize !== undefined ? item.fontsize : item.fontSize;
    if (rawFs !== undefined && rawFs !== null) {
      const fs = parseStrictInt(rawFs);
      if (fs === null) throw new Error("json_invalid_fontsize");
      fontSize = fs;
    }

    let color = 16777215;
    if (item.color_int !== undefined && item.color_int !== null) {
      const c = parseStrictInt(item.color_int);
      if (c === null) throw new Error("json_invalid_color_int");
      color = c;
    } else if (item.color_hex !== undefined && item.color_hex !== null) {
      const hex = String(item.color_hex).replace(/^#/, "").trim();
      if (!/^[0-9a-fA-F]{1,6}$/.test(hex)) throw new Error("json_invalid_color_hex");
      color = parseInt(hex, 16);
    } else if (item.color !== undefined && item.color !== null) {
      if (typeof item.color === "string" && item.color.startsWith("#")) {
        const hex = item.color.slice(1).trim();
        if (!/^[0-9a-fA-F]{1,6}$/.test(hex)) throw new Error("json_invalid_color_hex");
        color = parseInt(hex, 16);
      } else {
        const c = parseStrictInt(item.color);
        if (c === null) throw new Error("json_invalid_color");
        color = c;
      }
    }

    let pool = 0;
    if (item.pool !== undefined && item.pool !== null) {
      const p = parseStrictInt(item.pool);
      if (p === null) throw new Error("json_invalid_pool");
      pool = p;
    }

    validateRowData({ progressMs, mode, fontSize, color, pool }, "json");

    const content = String(item.content !== undefined ? item.content : (item.text !== undefined ? item.text : (item.message !== undefined ? item.message : ""))).trim();
    if (!content) continue;

    // Frontend ignore rule
    if (pool === 2 || mode === 7 || mode === 8 || mode === 9) continue;

    rows.push({
      _idx: idx++,
      progressMs,
      mode,
      fontSize,
      color,
      pool,
      content: content.slice(0, 500)
    });
  }

  if (rows.length === 0) throw new Error("json_no_danmaku_rows");

  return { title, rows };
}

export function parseYbdmDanmaku(ybdmText) {
  if (typeof ybdmText !== "string") {
    throw new Error("ybdm_must_be_string");
  }
  const lines = ybdmText.replace(/\r\n/g, "\n").replace(/\r/g, "\n").split("\n");
  if (!lines[0] || !lines[0].startsWith("#YBDM/1")) {
    throw new Error("invalid_ybdm_header");
  }

  let title = "";
  const rows = [];
  let idx = 0;
  for (const line of lines) {
    if (!line) continue;
    if (line.startsWith("#")) {
      if (line.startsWith("#title=")) {
        title = unescapeField(line.slice(7));
      }
      continue;
    }
    const parts = line.split("\t");
    if (parts.length < 6) {
      throw new Error("ybdm_invalid_column_count: line must have at least 6 tab-separated columns");
    }

    const progressMs = parseStrictInt(parts[0]);
    const mode = parseStrictInt(parts[1]);
    const color = parseStrictInt(parts[2]);
    const fontSize = parseStrictInt(parts[3]);
    const pool = parseStrictInt(parts[4]);

    if (progressMs === null || mode === null || color === null || fontSize === null || pool === null) {
      throw new Error("ybdm_invalid_numeric_fields");
    }

    validateRowData({ progressMs, mode, fontSize, color, pool }, "ybdm");

    const content = unescapeField(parts[5]).trim();
    if (!content) continue;

    if (pool === 2 || mode === 7 || mode === 8 || mode === 9) continue;

    rows.push({
      _idx: idx++,
      progressMs,
      mode,
      fontSize,
      color,
      pool,
      content: content.slice(0, 500)
    });
  }

  if (rows.length === 0) throw new Error("ybdm_no_danmaku_rows");

  return { title, rows };
}

export function parseUploadedDanmaku(rawContent, fileName = "") {
  try {
    const text = String(rawContent || "").trim();
    if (!text) {
      return { success: false, ok: false, error: "empty_danmaku_input", message: "Danmaku content is empty" };
    }
    if (Buffer.byteLength(text, "utf8") > 16 * 1024 * 1024) {
      return { success: false, ok: false, error: "danmaku_file_too_large", message: "Danmaku content exceeds 16MB" };
    }

    let result;
    if (text.startsWith("#YBDM/1")) {
      result = parseYbdmDanmaku(text);
    } else if (text.startsWith("<")) {
      result = parseXmlDanmaku(text);
    } else if (text.startsWith("{") || text.startsWith("[")) {
      result = parseJsonDanmaku(text);
    } else {
      return { success: false, ok: false, error: "unsupported_danmaku_format", message: "File must be XML, JSON, or #YBDM/1 format" };
    }

    const { title, rows } = result;
    if (!rows || rows.length === 0) {
      return { success: false, ok: false, error: "no_valid_danmaku_found", message: "No valid danmaku entries found in file" };
    }

    // Stable sort by progressMs ascending, then original index
    const sorted = rows.slice().sort((a, b) => a.progressMs - b.progressMs || (a._idx || 0) - (b._idx || 0));
    // Cap at 4096 lines to match frontend limit
    const limited = sorted.slice(0, 4096);
    const truncatedCount = Math.max(0, rows.length - limited.length);

    return {
      success: true,
      ok: true,
      title: title || "",
      rows: limited,
      count: limited.length,
      rawCount: rows.length,
      truncatedCount
    };
  } catch (err) {
    return {
      success: false,
      ok: false,
      error: err.message || "danmaku_parse_failed",
      message: err.message || "Failed to parse danmaku file"
    };
  }
}

export function formatYbdmDanmaku(optionsOrRows, maybeTitle = "", maybeCustomId = "") {
  let customId = "";
  let title = "";
  let vcrid = 0;
  let vcridMax = 1000000;
  let rows = [];

  if (Array.isArray(optionsOrRows)) {
    rows = optionsOrRows;
    title = maybeTitle || "";
    customId = maybeCustomId || "";
  } else if (optionsOrRows && typeof optionsOrRows === "object") {
    rows = Array.isArray(optionsOrRows.rows) ? optionsOrRows.rows : [];
    title = optionsOrRows.title || "";
    customId = optionsOrRows.customId || "";
    vcrid = optionsOrRows.vcrid || 0;
    vcridMax = optionsOrRows.vcridMax || 1000000;
  }

  const sorted = rows.slice().sort((a, b) => a.progressMs - b.progressMs || (a._idx || 0) - (b._idx || 0));
  const limited = sorted.slice(0, 4096);

  const header = [
    "#YBDM/1",
    "#manifest_type=bilibili-video",
    "#provider=custom"
  ];
  if (vcrid > 0) {
    header.push(`#vcrid=${vcrid}`);
    header.push(`#vcrid_max=${vcridMax}`);
  }
  if (customId) {
    header.push(`#custom_id=${escapeField(customId)}`);
  }
  if (title) {
    header.push(`#title=${escapeField(title)}`);
  }
  header.push(`#count=${limited.length}`);
  header.push("#columns=progressMs\tmode\tcolor\tfontSize\tpool\tcontent");

  const body = limited.map((item) => [
    item.progressMs,
    item.mode,
    item.color,
    item.fontSize,
    item.pool,
    escapeField(item.content)
  ].join("\t"));

  return `${header.join("\n")}\n${body.join("\n")}\n`;
}

export function updateYbdmHeader(tsvText, updates = {}) {
  const lines = String(tsvText || "").replace(/\r\n/g, "\n").replace(/\r/g, "\n").split("\n");
  const newHeader = [];
  let inHeader = true;
  const bodyLines = [];

  for (const line of lines) {
    if (!line && !inHeader) continue;
    if (inHeader && line.startsWith("#")) {
      if (line.startsWith("#title=") && updates.title !== undefined) {
        newHeader.push(`#title=${escapeField(updates.title)}`);
      } else if (line.startsWith("#vcrid=") && updates.vcrid !== undefined) {
        newHeader.push(`#vcrid=${updates.vcrid}`);
      } else if (line.startsWith("#vcrid_max=") && updates.vcridMax !== undefined) {
        newHeader.push(`#vcrid_max=${updates.vcridMax}`);
      } else if (line.startsWith("#custom_id=") && updates.customId !== undefined) {
        newHeader.push(`#custom_id=${escapeField(updates.customId)}`);
      } else {
        newHeader.push(line);
      }
    } else {
      inHeader = false;
      if (line) bodyLines.push(line);
    }
  }

  return `${newHeader.join("\n")}\n${bodyLines.join("\n")}\n`;
}
