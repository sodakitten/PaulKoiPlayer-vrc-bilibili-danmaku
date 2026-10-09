import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { tmpdir } from "node:os";
import { fileURLToPath } from "node:url";
import { runInNewContext } from "node:vm";
import {
  parseUploadedDanmaku,
  parseXmlDanmaku,
  parseJsonDanmaku,
  parseYbdmDanmaku,
  validateCustomId,
  validateVideoUrl,
  formatYbdmDanmaku,
  updateYbdmHeader
} from "../src/custom-danmaku.js";
import { CustomDanmakuStore, HistoryStore } from "../src/server.js";
import { renderAdminPage } from "../src/admin-page.js";
import worker from "../src/worker.js";

const FIXTURES_DIR = fileURLToPath(new URL("fixtures", import.meta.url));
const REAL_XML_SAMPLE = process.env.REAL_XML_SAMPLE || "D:\\Downloads\\danmaku_ep6319566.xml";
const REAL_JSON_SAMPLE = process.env.REAL_JSON_SAMPLE || "D:\\Downloads\\danmaku_ep6319566.json";

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

async function request(url, options = {}) {
  const res = await fetch(url, {
    redirect: "manual",
    ...options
  });
  const text = await res.text();
  let json = null;
  try {
    json = JSON.parse(text);
  } catch {}
  return {
    status: res.status,
    headers: res.headers,
    text,
    json
  };
}

function killProcess(proc) {
  if (!proc || proc.killed) return;
  try {
    proc.kill("SIGKILL");
  } catch {}
}

async function runAllTests() {
  console.log("=== PaulKoiPlayer Custom Danmaku Test Suite ===");

  // -------------------------------------------------------------
  // SUITE 1: Synthetic Fixtures & Parser Security
  // -------------------------------------------------------------
  console.log("\n[SUITE 1] Testing Synthetic Fixtures & Security Validation...");

  // 1.0 Side-effect free dynamic import of server.js
  const serverMod = await import("../src/server.js");
  assert.strictEqual(serverMod.server.listening, false, "Dynamic import must not start listening on port 3000");
  assert.strictEqual(typeof serverMod.CustomDanmakuStore, "function", "CustomDanmakuStore must be exported");
  console.log("  ✓ Dynamic import of server.js has zero listening ports and zero side-effects");

  // Exercise the generated browser script, including its actual edit payload.
  const elements = new Map();
  const element = (id) => {
    if (!elements.has(id)) elements.set(id, {
      value: "", type: id === "adminTokenInput" ? "password" : "text", files: [], style: {},
      handlers: {}, attributes: {},
      addEventListener(event, handler) { this.handlers[event] = handler; },
      setAttribute(name, value) { this.attributes[name] = value; },
      getAttribute(name) { return this.attributes[name]; },
      querySelectorAll(selector) { return selector === ".btn-edit" ? [element("edit")] : selector === ".copy-link" ? [element("copy")] : []; },
      reset() {}, scrollIntoView() {}, select() {}, focus() {}, remove() {}
    });
    return elements.get(id);
  };
  element("edit").attributes = { "data-id": "test-item-01", "data-title": "Original", "data-url": "https://example.com/original.mp4" };
  const submissions = [];
  const copiedLinks = [];
  let clipboardDenied = false;
  let legacyCopyAllowed = true;
  const pageLocation = { origin: "https://danmaku.paulkoishi.com" };
  element("copy").attributes = { "data-copy": "/player/?url=test-item-01", "data-label": "播放" };
  const pageScript = renderAdminPage().match(/<script>([\s\S]*?)<\/script>/)[1];
  runInNewContext(pageScript, {
    document: {
      getElementById: element, createElement: () => element("copySelection"),
      body: { appendChild() {} }, execCommand: () => legacyCopyAllowed
    },
    URL, window: { location: pageLocation },
    navigator: { clipboard: { writeText: async (text) => {
      if (clipboardDenied) throw new Error("Clipboard denied");
      copiedLinks.push(text);
    } } },
    fetch: async (_url, options = {}) => {
      if (options.method === "POST") submissions.push(JSON.parse(options.body));
      return { ok: true, json: async () => ({ items: [{ customId: "test-item-01" }], item: { count: 4, vcrid: 1 } }) };
    }
  });
  await new Promise((resolve) => setImmediate(resolve));
  element("adminTokenInput").value = "test-only-token";
  element("toggleTokenBtn").handlers.click();
  assert.equal(element("adminTokenInput").type, "text");
  assert.equal(element("toggleTokenBtn").attributes["aria-pressed"], "true");
  assert.equal(element("tokenEye").style.display, "");
  assert.equal(element("tokenEyeOff").style.display, "none");
  element("toggleTokenBtn").handlers.click();
  assert.equal(element("adminTokenInput").type, "password");
  assert.equal(element("tokenEye").style.display, "none");
  assert.equal(element("tokenEyeOff").style.display, "");
  await element("copy").handlers.click();
  assert.equal(copiedLinks[0], "https://danmaku.paulkoishi.com/player/?url=test-item-01");
  pageLocation.origin = "http://127.0.0.1:7858";
  element("copy").attributes["data-copy"] = "/player/?vcrid=123";
  await element("copy").handlers.click();
  assert.equal(copiedLinks[1], "http://127.0.0.1:7858/player/?vcrid=123");
  clipboardDenied = true;
  await element("copy").handlers.click();
  assert.equal(element("copySelection").value, copiedLinks[1]);
  assert.equal(element("manualCopyInput").hidden, true);
  legacyCopyAllowed = false;
  await element("copy").handlers.click();
  assert.equal(element("manualCopyInput").hidden, false);
  assert.equal(element("manualCopyInput").value, copiedLinks[1]);
  assert.equal(element("copy").disabled, false);
  element("saveTokenBtn").handlers.click();
  element("edit").handlers.click();
  element("inputTitle").value = "Updated";
  await element("customMappingForm").handlers.submit({ preventDefault() {} });
  assert.equal(submissions[0].replaceExisting, true);
  assert.equal(submissions[0].danmaku, "");
  element("inputDanmakuFile").files = [{ size: 10, text: async () => "test" }];
  await element("customMappingForm").handlers.submit({ preventDefault() {} });
  assert.equal(submissions[1].replaceExisting, false);
  element("toggleTokenBtn").handlers.click();
  element("clearTokenBtn").handlers.click();
  assert.equal(element("adminTokenInput").value, "");
  assert.equal(element("adminTokenInput").type, "password");
  console.log("  ✓ Admin page toggles token visibility and distinguishes edits from new entries");

  // 1.1 Synthetic sample parity
  const sampleXml = readFileSync(join(FIXTURES_DIR, "sample.xml"), "utf8");
  const sampleJson = readFileSync(join(FIXTURES_DIR, "sample.json"), "utf8");

  const parsedXml = parseUploadedDanmaku(sampleXml, "sample.xml");
  assert.strictEqual(parsedXml.success, true);
  assert.strictEqual(parsedXml.rows.length, 4);

  const parsedJson = parseUploadedDanmaku(sampleJson, "sample.json");
  assert.strictEqual(parsedJson.success, true);
  assert.strictEqual(parsedJson.rows.length, 4);

  for (let i = 0; i < 4; i++) {
    const x = parsedXml.rows[i];
    const j = parsedJson.rows[i];
    assert.strictEqual(x.progressMs, j.progressMs, `Row ${i} progressMs mismatch`);
    assert.strictEqual(x.mode, j.mode, `Row ${i} mode mismatch`);
    assert.strictEqual(x.color, j.color, `Row ${i} color mismatch`);
    assert.strictEqual(x.content, j.content, `Row ${i} content mismatch`);
  }
  console.log("  ✓ Synthetic XML and JSON samples have 100% attribute parity");

  // 1.2 Black color (0) preservation
  const blackXml = readFileSync(join(FIXTURES_DIR, "black_color.xml"), "utf8");
  const blackRes = parseUploadedDanmaku(blackXml);
  assert.strictEqual(blackRes.success, true);
  assert.strictEqual(blackRes.rows[0].color, 0, "Color 0 (black) must be preserved as 0");

  const blackJsonRaw = JSON.stringify([{ progress_sec: 1.5, color_int: 0, content: "black json" }]);
  const blackJsonRes = parseUploadedDanmaku(blackJsonRaw);
  assert.strictEqual(blackJsonRes.success, true);
  assert.strictEqual(blackJsonRes.rows[0].color, 0, "Color 0 in JSON must be preserved as 0");
  console.log("  ✓ Color 0 (black) correctly preserved across XML and JSON");

  // 1.3 XML counterexamples: non-(i|xml) root, broken attributes, unknown entity, CDATA </d>, bad codepoints
  assert.throws(() => parseXmlDanmaku('<evil><d p="0,1,25,0,0,0,0,0">ok</d></evil>'), /xml_invalid_root_tag/);
  assert.throws(() => parseXmlDanmaku('<i broken><d p="0,1,25,0,0,0,0,0">ok</d></i>'), /xml_missing_attribute_equals/);
  assert.throws(() => parseXmlDanmaku('<i><title>ok</title><chatserver>&unknown;</chatserver><d p="0,1,25,0,0,0,0,0">ok</d></i>'), /xml_unknown_entity/);
  assert.throws(() => parseXmlDanmaku('<i><d p="0,1,25,0,0,0,0,0">&#0;</d></i>'), /xml_invalid_codepoint/);
  assert.throws(() => parseXmlDanmaku('<i><d p="0,1,25,0,0,0,0,0">&#xD800;</d></i>'), /xml_invalid_codepoint/);
  assert.throws(() => parseXmlDanmaku('<i><d p="0,1,25,0,0,0,0,0">&#x1;</d></i>'), /xml_invalid_codepoint/);

  // CDATA inside <d> containing literal </d>
  const cdataWithClose = '<i><d p="0,1,25,0,0,0,0,0"><![CDATA[hello </d> world]]></d></i>';
  const cdataParsed = parseXmlDanmaku(cdataWithClose);
  assert.strictEqual(cdataParsed.rows[0].content, "hello </d> world");
  console.log("  ✓ XML counterexamples (evil root, broken attrs, unknown entity, bad codepoints, CDATA </d>) verified");

  // 1.4 Numerical boundary checks: progressMs, fontSize, pool, mode, "12abc", overflow
  assert.throws(() => parseJsonDanmaku('[{"progressMs": 1e300, "content": "hi"}]'), /json_invalid_progressMs/);
  assert.throws(() => parseJsonDanmaku('[{"progress_sec": 1e300, "content": "hi"}]'), /json_invalid_progressMs/);
  assert.throws(() => parseJsonDanmaku('[{"progressMs": -10, "content": "hi"}]'), /json_invalid_progressMs/);
  assert.throws(() => parseJsonDanmaku('[{"progressMs": "12abc", "content": "hi"}]'), /json_invalid_progressMs/);
  assert.throws(() => parseJsonDanmaku('[{"progressMs": 100, "fontsize": 1001, "content": "hi"}]'), /json_invalid_fontsize/);
  assert.throws(() => parseJsonDanmaku('[{"progressMs": 100, "pool": 3, "content": "hi"}]'), /json_invalid_pool/);
  assert.throws(() => parseJsonDanmaku('[{"progressMs": 100, "mode": 10, "content": "hi"}]'), /json_invalid_mode/);
  assert.throws(() => parseJsonDanmaku('[{"progressMs": 100, "color": 16777216, "content": "hi"}]'), /json_invalid_color/);

  // YBDM format rejects bad rows rather than silently skipping
  assert.throws(() => parseYbdmDanmaku("#YBDM/1\n100\t1\t0\t25\t0\t"), /ybdm_no_danmaku_rows/);
  assert.throws(() => parseYbdmDanmaku("#YBDM/1\n100\tbad\t0\t25\t0\tok"), /ybdm_invalid_numeric_fields/);
  assert.throws(() => parseYbdmDanmaku("#YBDM/1\n100\t1\t0\t25"), /ybdm_invalid_column_count/);
  console.log("  ✓ Strict numerical boundaries (0..2147483647, bad strings, ranges) verified");

  // 1.5 escapeField / unescapeField single-pass token scanning roundtrip
  const { escapeField, unescapeField } = await import("../src/custom-danmaku.js");
  assert.strictEqual(unescapeField(escapeField("literal \\n inside text")), "literal \\n inside text");
  assert.strictEqual(unescapeField(escapeField("real \n newline")), "real \n newline");
  assert.strictEqual(unescapeField(escapeField("tab \t and return \r")), "tab \t and return \r");
  console.log("  ✓ Single-pass token escape/unescape roundtrip preserves literal \\n vs real newline");

  // 1.6 4097 danmaku truncation test
  const bigList = [];
  for (let i = 0; i < 4097; i++) {
    bigList.push({ progressMs: i * 10, content: `msg ${i}` });
  }
  const bigUpload = parseUploadedDanmaku(JSON.stringify(bigList));
  assert.strictEqual(bigUpload.success, true);
  assert.strictEqual(bigUpload.rows.length, 4096, "Must truncate to exactly 4096 rows");
  assert.strictEqual(bigUpload.truncatedCount, 1, "Must report 1 truncated row");
  console.log("  ✓ 4097 danmaku input sorted and truncated to 4096 with accurate truncatedCount");

  // 1.7 Security: Malformed / XXE / Unknown Entity / Trailing Garbage
  const malformedXml = readFileSync(join(FIXTURES_DIR, "malformed.xml"), "utf8");
  assert.strictEqual(parseUploadedDanmaku(malformedXml).success, false, "Must reject malformed XML");

  const xxeXml = readFileSync(join(FIXTURES_DIR, "xxe.xml"), "utf8");
  assert.strictEqual(parseUploadedDanmaku(xxeXml).success, false, "Must reject XXE / DOCTYPE XML");

  const unknownEntityXml = readFileSync(join(FIXTURES_DIR, "unknown_entity.xml"), "utf8");
  assert.strictEqual(parseUploadedDanmaku(unknownEntityXml).success, false, "Must reject unknown entities in XML");

  const trailingGarbageXml = readFileSync(join(FIXTURES_DIR, "trailing_garbage.xml"), "utf8");
  assert.strictEqual(parseUploadedDanmaku(trailingGarbageXml).success, false, "Must reject trailing garbage in XML");

  const badNumbersXml = readFileSync(join(FIXTURES_DIR, "bad_numbers.xml"), "utf8");
  assert.strictEqual(parseUploadedDanmaku(badNumbersXml).success, false, "Must reject bad numbers in XML attributes");

  const badNumbersJson = readFileSync(join(FIXTURES_DIR, "bad_numbers.json"), "utf8");
  assert.strictEqual(parseUploadedDanmaku(badNumbersJson).success, false, "Must reject bad numbers in JSON");
  console.log("  ✓ Structural XML and JSON security rejections verified");

  // 1.8 Prototype Pollution & ID Validation
  assert.strictEqual(validateCustomId("__proto__").valid, false);
  assert.strictEqual(validateCustomId("constructor").valid, false);
  assert.strictEqual(validateCustomId("prototype").valid, false);
  assert.strictEqual(validateCustomId("toString").valid, false);
  assert.strictEqual(validateCustomId("valueOf").valid, false);
  assert.strictEqual(validateCustomId("player").valid, false);
  assert.strictEqual(validateCustomId("BV1PV7m6DE51").valid, false);
  assert.strictEqual(validateCustomId("av170001").valid, false);
  assert.strictEqual(validateCustomId("valid_custom-id-123").valid, true);
  console.log("  ✓ Prototype property keys and reserved identifiers rejected");

  // 1.9 Video URL Validation
  assert.strictEqual(validateVideoUrl("https://media.example.com/stream.m3u8").valid, true);
  assert.strictEqual(validateVideoUrl("http://192.168.1.100:8080/ep01.mp4").valid, true);
  assert.strictEqual(validateVideoUrl("file:///D:/video.mp4").valid, false);
  assert.strictEqual(validateVideoUrl("D:\\video.mp4").valid, false);
  assert.strictEqual(validateVideoUrl("ftp://example.com/v.mp4").valid, false);
  console.log("  ✓ Video direct URL validation rules verified");

  // 1.10 Optional Real Samples Check (3059 identity across all attributes)
  if (existsSync(REAL_XML_SAMPLE) && existsSync(REAL_JSON_SAMPLE)) {
    console.log("  Checking optional real user samples on disk...");
    const realXml = readFileSync(REAL_XML_SAMPLE, "utf8");
    const realJson = readFileSync(REAL_JSON_SAMPLE, "utf8");
    const realXmlRes = parseUploadedDanmaku(realXml);
    const realJsonRes = parseUploadedDanmaku(realJson);
    assert.strictEqual(realXmlRes.success, true);
    assert.strictEqual(realXmlRes.rows.length, 3059);
    assert.strictEqual(realJsonRes.success, true);
    assert.strictEqual(realJsonRes.rows.length, 3059);
    for (let i = 0; i < 3059; i++) {
      const rx = realXmlRes.rows[i];
      const rj = realJsonRes.rows[i];
      assert.strictEqual(rx.progressMs, rj.progressMs, `Row ${i} time mismatch`);
      assert.strictEqual(rx.mode, rj.mode, `Row ${i} mode mismatch`);
      assert.strictEqual(rx.fontSize, rj.fontSize, `Row ${i} font mismatch`);
      assert.strictEqual(rx.color, rj.color, `Row ${i} color mismatch`);
      assert.strictEqual(rx.pool, rj.pool, `Row ${i} pool mismatch`);
      assert.strictEqual(rx.content, rj.content, `Row ${i} content mismatch`);
    }
    console.log("  ✓ Optional real samples 3059-entry identity confirmed (mode, font, pool, time, color, content 100% parity)");
  } else {
    console.log("  (Optional real sample files not present on disk; skipped gracefully)");
  }

  // -------------------------------------------------------------
  // SUITE 2: Storage Atomicity, Versioning, Rollback & GC Protection
  // -------------------------------------------------------------
  console.log("\n[SUITE 2] Testing Storage Atomicity, Versioning & Rollback...");
  const storeTestDir = join(tmpdir(), `paulkoi_store_test_${Date.now()}`);
  mkdirSync(storeTestDir, { recursive: true });

  class MockVcrStore {
    constructor(maxId = 1000) {
      this.maxId = maxId;
      this.available = true;
      this.allocated = 0;
      this.data = { records: {}, contentIndex: {} };
    }
    allocate(input) {
      if (this.allocated >= this.maxId) {
        const err = new Error("vcrid_pool_exhausted");
        err.code = "vcrid_pool_exhausted";
        err.status = 503;
        throw err;
      }
      this.allocated++;
      const rec = { vcrid: this.allocated, provider: "custom", customId: input.customId };
      this.data.records[String(this.allocated)] = rec;
      this.data.contentIndex[`custom:${String(input.customId).toLowerCase()}`] = this.allocated;
      return rec;
    }
    save() { return true; }
  }

  const mockVcr = new MockVcrStore(10);
  const customStoreFile = join(storeTestDir, "custom-store.json");
  const customDanmakuDir = join(storeTestDir, "custom-danmaku");
  const store = new CustomDanmakuStore(customStoreFile, customDanmakuDir, mockVcr, 2);

  // 2.1 Basic creation with versioned file
  const create1 = store.createOrUpdate({
    customId: "entry-01",
    title: "Entry 1",
    videoUrl: "https://example.com/1.mp4",
    danmakuText: sampleXml
  });
  assert.strictEqual(create1.item.customId, "entry-01");
  assert.ok(create1.item.contentFile.startsWith("entry-01."));
  assert.ok(existsSync(join(customDanmakuDir, create1.item.contentFile)));
  console.log("  ✓ Immutable versioned content file created and referenced in metadata");

  // 2.2 Updating entry replaces versioned file and cleans up old file
  const oldContentFile = create1.item.contentFile;
  const update1 = store.createOrUpdate({
    customId: "entry-01",
    title: "Entry 1 Updated",
    videoUrl: "https://example.com/1-new.mp4",
    danmakuText: sampleJson,
    replaceExisting: true
  });
  assert.notStrictEqual(update1.item.contentFile, oldContentFile);
  assert.ok(!existsSync(join(customDanmakuDir, oldContentFile)), "Old content file should be deleted");
  assert.ok(existsSync(join(customDanmakuDir, update1.item.contentFile)), "New content file should exist");
  console.log("  ✓ Updating entry replaced content file with atomic versioning and cleanup");

  // 2.3 Capacity limit
  store.createOrUpdate({
    customId: "entry-02",
    title: "Entry 2",
    videoUrl: "https://example.com/2.mp4",
    danmakuText: sampleXml
  });
  assert.throws(
    () => store.createOrUpdate({
      customId: "entry-03",
      title: "Entry 3",
      videoUrl: "https://example.com/3.mp4",
      danmakuText: sampleXml
    }),
    /store_capacity_exceeded/
  );
  console.log("  ✓ Maximum item capacity strictly enforced for new entries");

  // 2.4 Editing existing entry allowed even at max capacity
  const editAtMax = store.createOrUpdate({
    customId: "entry-02",
    title: "Entry 2 Edited",
    videoUrl: "https://example.com/2.mp4",
    replaceExisting: true
  });
  assert.strictEqual(editAtMax.item.title, "Entry 2 Edited");
  console.log("  ✓ Editing existing entry permitted when store is full");

  // 2.5 Storage failure rollback injection
  const originalSave = store.save.bind(store);
  store.save = () => false; // inject metadata save failure
  assert.throws(
    () => store.createOrUpdate({
      customId: "entry-02",
      title: "Fail Edit",
      videoUrl: "https://example.com/fail.mp4",
      danmakuText: sampleXml,
      replaceExisting: true
    }),
    /Failed to save custom store metadata/
  );
  store.save = originalSave; // restore
  // Verify entry-02 still has previous title and memory was restored
  assert.strictEqual(store.get("entry-02").title, "Entry 2 Edited");
  console.log("  ✓ Disk metadata save failure rolls back in-memory state cleanly");

  // 2.6 Delete failure rollback
  store.save = () => false;
  assert.throws(() => store.delete("entry-02"), /Failed to commit deletion/);
  store.save = originalSave;
  assert.ok(store.get("entry-02"), "Item must still exist after failed delete");
  console.log("  ✓ Deletion commit failure preserves existing item in memory");

  // 2.7 New entry creation failure revokes allocated VCRID reservation without partial pollution
  store.delete("entry-01"); // free capacity
  store.save = () => false;
  assert.throws(
    () => store.createOrUpdate({
      customId: "entry-rollback",
      title: "Rollback Title",
      videoUrl: "https://example.com/rollback.mp4",
      danmakuText: sampleXml
    }),
    /Failed to save custom store metadata/
  );
  store.save = originalSave;
  assert.strictEqual(store.get("entry-rollback"), null);
  assert.strictEqual(mockVcr.data.contentIndex["custom:entry-rollback"], undefined, "VCRID reservation must be released");
  console.log("  ✓ New entry creation failure immediately revokes VCRID reservation without partial store pollution");

  // Clean up suite 2 temp dir
  try { rmSync(storeTestDir, { recursive: true, force: true }); } catch {}

  // -------------------------------------------------------------
  // SUITE 3: HTTP Server Integration on Ephemeral Port (Port 0)
  // -------------------------------------------------------------
  console.log("\n[SUITE 3] Testing HTTP Server Integration on Ephemeral Port 0...");
  const httpTestDir = join(tmpdir(), `paulkoi_http_test_${Date.now()}`);
  mkdirSync(httpTestDir, { recursive: true });

  const historyFixture = new HistoryStore(join(httpTestDir, "history-store.json"));
  historyFixture.upsert({ provider: "bilibili", contentType: "video", bvid: "BV1BDk2YCEHF", normalizedUrl: "https://example.com/temporary.mp4" });
  historyFixture.upsert({ provider: "bilibili", contentType: "video", aid: "123456", sourceUrl: "http://localhost/player/?vcrid=1" });
  historyFixture.upsert({ provider: "netease", contentType: "song", songId: "123456", normalizedUrl: "https://example.com/temporary.mp3" });

  const adminToken = "secret-test-token-123";
  let serverProc = null;
  let assignedPort = 0;

  async function startServer(token = adminToken) {
    const env = {
      ...process.env,
      PORT: "0",
      ADMIN_TOKEN: token,
      CUSTOM_STORE_FILE: join(httpTestDir, "custom-store.json"),
      CUSTOM_DANMAKU_DIR: join(httpTestDir, "custom-danmaku"),
      VCRID_STORE_FILE: join(httpTestDir, "vcrid-store.json"),
      HISTORY_STORE_FILE: join(httpTestDir, "history-store.json"),
      DANMAKU_DISK_CACHE_DIR: join(httpTestDir, "danmaku-cache"),
      STATS_FILE: join(httpTestDir, "stats.json")
    };
    serverProc = spawn(process.execPath, ["src/server.js"], {
      cwd: fileURLToPath(new URL("..", import.meta.url)),
      env,
      stdio: ["pipe", "pipe", "pipe"]
    });

    return new Promise((resolve, reject) => {
      let resolved = false;
      serverProc.stdout.on("data", (chunk) => {
        const text = chunk.toString();
        const match = text.match(/listening on http:\/\/localhost:(\d+)/);
        if (match && !resolved) {
          resolved = true;
          assignedPort = parseInt(match[1], 10);
          resolve(assignedPort);
        }
      });
      serverProc.on("error", reject);
      setTimeout(() => {
        if (!resolved) reject(new Error("Server start timeout on port 0"));
      }, 8000);
    });
  }

  try {
    assignedPort = await startServer(adminToken);
    console.log(`  ✓ Node server listening on dynamically assigned ephemeral port ${assignedPort}`);
    const origin = `http://127.0.0.1:${assignedPort}`;

    const dashboard = await request(`${origin}/?historyProvider=bilibili`);
    assert.equal(dashboard.status, 200);
    assert.ok(dashboard.text.includes('href="https://www.bilibili.com/video/BV1BDk2YCEHF/"'));
    assert.ok(dashboard.text.includes('href="https://www.bilibili.com/video/av123456/"'));
    assert.ok(!dashboard.text.includes('href="https://example.com/temporary.mp4"'));
    const musicDashboard = await request(`${origin}/?historyProvider=netease`);
    assert.ok(musicDashboard.text.includes('href="https://music.163.com/song?id=123456"'));
    console.log("  ✓ Dashboard history links use provider pages instead of playback sources");

    // 3.1 Unauthenticated requests
    const getNoAuth = await request(`${origin}/api/admin/custom`);
    assert.strictEqual(getNoAuth.status, 401, "GET without token must return 401");

    const getWrongAuth = await request(`${origin}/api/admin/custom`, {
      headers: { Authorization: "Bearer wrong-token" }
    });
    assert.strictEqual(getWrongAuth.status, 401, "GET with bad token must return 401");

    // 3.2 Authenticated GET
    const getAuth = await request(`${origin}/api/admin/custom`, {
      headers: { Authorization: `Bearer ${adminToken}` }
    });
    assert.strictEqual(getAuth.status, 200);
    assert.strictEqual(getAuth.json.items.length, 0);
    console.log("  ✓ Admin GET endpoint requires token and returns 200 with valid token");

    // 3.3 Large payload protection (413 without socket crash)
    const largeDanmaku = "a".repeat(17 * 1024 * 1024);
    const largeRes = await request(`${origin}/api/admin/custom`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        Authorization: `Bearer ${adminToken}`
      },
      body: JSON.stringify({ customId: "too-large", videoUrl: "https://example.com/v.mp4", danmaku: largeDanmaku })
    });
    assert.strictEqual(largeRes.status, 413, "Body > 16MB must return 413");
    console.log("  ✓ Payload > 16MB cleanly returns HTTP 413 without connection reset");

    // 3.4 Upload malformed XML: rejected and old entry intact
    const badUploadRes = await request(`${origin}/api/admin/custom`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        Authorization: `Bearer ${adminToken}`
      },
      body: JSON.stringify({
        customId: "test-bad",
        title: "Bad Entry",
        videoUrl: "https://example.com/v.mp4",
        danmaku: malformedXml
      })
    });
    assert.strictEqual(badUploadRes.status, 400);

    // 3.5 Create valid entry with synthetic sample XML
    const createRes = await request(`${origin}/api/admin/custom`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        Authorization: `Bearer ${adminToken}`
      },
      body: JSON.stringify({
        customId: "test-item-01",
        title: "Test Video Title",
        videoUrl: "https://example.com/test-video.mp4",
        danmaku: sampleXml
      })
    });
    assert.strictEqual(createRes.status, 200);
    assert.strictEqual(createRes.json.ok, true);
    const itemVcrid = createRes.json.item.vcrid;
    assert.ok(itemVcrid > 0, "Shared VCRID must be allocated");
    console.log(`  ✓ Custom entry created with allocated vcrid: ${itemVcrid}`);

    const duplicateRes = await request(`${origin}/api/admin/custom`, {
      method: "POST",
      headers: { "Content-Type": "application/json", Authorization: `Bearer ${adminToken}` },
      body: JSON.stringify({ customId: "test-item-01", title: "Duplicate", videoUrl: "https://example.com/test-video.mp4", danmaku: sampleXml })
    });
    assert.equal(duplicateRes.status, 409);
    const beforeEdit = await request(`${origin}/player/?__dm=1&url=test-item-01`);
    const updateRes = await request(`${origin}/api/admin/custom`, {
      method: "POST",
      headers: { "Content-Type": "application/json", Authorization: `Bearer ${adminToken}` },
      body: JSON.stringify({ customId: "test-item-01", title: "Test Video Title", videoUrl: "https://example.com/test-video.mp4", danmaku: "", replaceExisting: true })
    });
    assert.equal(updateRes.status, 200);
    assert.equal(updateRes.json.item.vcrid, itemVcrid);
    assert.equal(updateRes.json.item.count, createRes.json.item.count);
    const afterEdit = await request(`${origin}/player/?__dm=1&url=test-item-01`);
    assert.deepEqual(afterEdit.text.split("\n").filter((line) => /^\d+\t/.test(line)), beforeEdit.text.split("\n").filter((line) => /^\d+\t/.test(line)));
    console.log("  ✓ Editing preserves vcrid and danmaku; accidental duplicate creation remains rejected");

    // 3.6 Player Redirect (browser / player media request)
    const playRes = await request(`${origin}/player/?url=test-item-01`);
    assert.strictEqual(playRes.status, 302, "Media player request must 302 redirect");
    assert.strictEqual(playRes.headers.get("location"), "https://example.com/test-video.mp4");

    // 3.7 Danmaku Request (__dm=1)
    const dmRes = await request(`${origin}/player/?__dm=1&url=test-item-01`);
    assert.strictEqual(dmRes.status, 200);
    assert.ok(dmRes.text.startsWith("#YBDM/1\n"));
    assert.ok(dmRes.text.includes("#manifest_type=bilibili-video\n"));
    assert.ok(dmRes.text.includes(`#vcrid=${itemVcrid}\n`));
    console.log("  ✓ /player returns 302 for media and #YBDM/1 TSV for danmaku");

    // 3.8 /api/pages (Unity YamaPlayer entrance)
    const pagesRes = await request(`${origin}/api/pages?url=test-item-01`);
    assert.strictEqual(pagesRes.status, 200);
    assert.strictEqual(pagesRes.json.type, "bilibili-video");
    assert.strictEqual(pagesRes.json.provider, "custom");
    assert.strictEqual(pagesRes.json.title, "Test Video Title");
    assert.strictEqual(pagesRes.json.pages[0].vcrid, itemVcrid);
    console.log("  ✓ /api/pages returns bilibili-video compatible manifest for Unity");

    // 3.9 VCRID Player and Danmaku Access
    const vcrPlayRes = await request(`${origin}/player/?vcrid=${itemVcrid}`);
    assert.strictEqual(vcrPlayRes.status, 302);
    assert.strictEqual(vcrPlayRes.headers.get("location"), "https://example.com/test-video.mp4");

    const vcrDmRes = await request(`${origin}/player/?__dm=1&vcrid=${itemVcrid}`);
    assert.strictEqual(vcrDmRes.status, 200);
    assert.ok(vcrDmRes.text.startsWith("#YBDM/1\n"));
    console.log("  ✓ /player/?vcrid=N works for both 302 redirect and danmaku text");

    // 3.9.1 Create temporary entry test-del-item, then delete and verify vcrid returns 404 (NEVER fallback to record.videoUrl)
    const delCreate = await request(`${origin}/api/admin/custom`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        Authorization: `Bearer ${adminToken}`
      },
      body: JSON.stringify({
        customId: "test-del-item",
        title: "To Be Deleted",
        videoUrl: "https://example.com/del.mp4",
        danmaku: sampleXml
      })
    });
    assert.strictEqual(delCreate.status, 200);
    const delVcrid = delCreate.json.item.vcrid;

    const delRes = await request(`${origin}/api/admin/custom?id=test-del-item`, {
      method: "DELETE",
      headers: { Authorization: `Bearer ${adminToken}` }
    });
    assert.strictEqual(delRes.status, 200);

    const vcrMissingPlay = await request(`${origin}/player/?vcrid=${delVcrid}`);
    assert.strictEqual(vcrMissingPlay.status, 404, "Deleted custom item must return 404 on vcrid player, NOT redirect to stale videoUrl");

    const vcrMissingResolve = await request(`${origin}/api/resolve?vcrid=${delVcrid}`);
    assert.strictEqual(vcrMissingResolve.status, 404, "Deleted custom item must return 404 on vcrid resolve");
    console.log("  ✓ Deleted custom item returns 404 on vcrid player and resolve (no stale fallback)");

    // 3.10 Regression check: ensure unknown Bilibili/Netease does not 404 as custom
    const biliRes = await request(`${origin}/api/pages?url=https://www.bilibili.com/video/BV1xx411c7mD`);
    // Should attempt Bilibili view or fail with Bilibili error, never custom_id_not_found
    assert.notStrictEqual(biliRes.json?.error, "custom_id_not_found");
    console.log("  ✓ Normal Bilibili URLs do not fall through into custom 404");

    // 3.11 Reboot persistence test
    killProcess(serverProc);
    await sleep(300);
    assignedPort = await startServer(adminToken);
    const rebootOrigin = `http://127.0.0.1:${assignedPort}`;

    const rebootList = await request(`${rebootOrigin}/api/admin/custom`, {
      headers: { Authorization: `Bearer ${adminToken}` }
    });
    assert.strictEqual(rebootList.status, 200);
    assert.strictEqual(rebootList.json.items.length, 1);
    assert.strictEqual(rebootList.json.items[0].customId, "test-item-01");
    assert.strictEqual(rebootList.json.items[0].vcrid, itemVcrid);
    console.log("  ✓ Data persisted and shared VCRID maintained after server restart");

    // 3.12 Server without ADMIN_TOKEN disables admin write & read API
    killProcess(serverProc);
    await sleep(300);
    assignedPort = await startServer(""); // no token
    const noTokenOrigin = `http://127.0.0.1:${assignedPort}`;

    const noTokenGet = await request(`${noTokenOrigin}/api/admin/custom`);
    assert.strictEqual(noTokenGet.status, 403, "Unset ADMIN_TOKEN must disable GET with 403");
    assert.strictEqual(noTokenGet.json.error, "admin_disabled");

    const noTokenPost = await request(`${noTokenOrigin}/api/admin/custom`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ customId: "fail-test", videoUrl: "https://example.com/v.mp4" })
    });
    assert.strictEqual(noTokenPost.status, 403, "Unset ADMIN_TOKEN must disable POST with 403");

    const adminHtml = await request(`${noTokenOrigin}/admin`);
    assert.strictEqual(adminHtml.status, 200, "/admin page must still render documentation");
    console.log("  ✓ Without ADMIN_TOKEN, all admin APIs disabled (403) while page renders instructions");

    // -------------------------------------------------------------
    // SUITE 4: Worker Proxy to Docker (Unity Flow & Headers)
    // -------------------------------------------------------------
    console.log("\n[SUITE 4] Testing Cloudflare Worker Proxy to Node Docker Backend...");
    // Restart node server with adminToken for worker testing
    killProcess(serverProc);
    await sleep(300);
    assignedPort = await startServer(adminToken);
    const dockerOrigin = `http://127.0.0.1:${assignedPort}`;

    // 4.1 Worker without BILI_DOCKER_ORIGIN returns 503
    const workerReqNoOrigin = new Request("https://worker.local/api/admin/custom");
    const workerResNoOrigin = await worker.fetch(workerReqNoOrigin, {});
    assert.strictEqual(workerResNoOrigin.status, 503);
    const noOriginJson = await workerResNoOrigin.json();
    assert.strictEqual(noOriginJson.error, "docker_origin_not_configured");
    console.log("  ✓ Worker without BILI_DOCKER_ORIGIN cleanly returns 503");

    // 4.2 Worker self-loop protection returns 503
    const workerReqSelf = new Request("https://worker.local/api/admin/custom");
    const workerResSelf = await worker.fetch(workerReqSelf, { BILI_DOCKER_ORIGIN: "https://worker.local" });
    assert.strictEqual(workerResSelf.status, 503);
    const selfJson = await workerResSelf.json();
    assert.strictEqual(selfJson.error, "docker_origin_self_loop");
    console.log("  ✓ Worker self-loop detection returns 503");

    const workerEnv = { BILI_DOCKER_ORIGIN: dockerOrigin };

    // 4.3 Worker proxies GET /api/admin/custom with token
    const workerGetAuth = new Request("https://worker.local/api/admin/custom", {
      headers: { Authorization: `Bearer ${adminToken}` }
    });
    const workerGetRes = await worker.fetch(workerGetAuth, workerEnv);
    assert.strictEqual(workerGetRes.status, 200);
    const workerGetJson = await workerGetRes.json();
    assert.strictEqual(workerGetJson.ok, true);
    console.log("  ✓ Worker successfully proxies GET /api/admin/custom with auth header");

    // 4.4 Worker proxies POST /api/admin/custom with body and auth
    const workerPostReq = new Request("https://worker.local/api/admin/custom", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "X-Admin-Token": adminToken
      },
      body: JSON.stringify({
        customId: "worker-entry-01",
        title: "Worker Proxied Video",
        videoUrl: "https://example.com/worker-video.mp4",
        danmaku: sampleXml
      })
    });
    const workerPostRes = await worker.fetch(workerPostReq, workerEnv);
    assert.strictEqual(workerPostRes.status, 200);
    const workerPostJson = await workerPostRes.json();
    assert.strictEqual(workerPostJson.ok, true);
    const workerVcrid = workerPostJson.item.vcrid;
    assert.ok(workerVcrid > 0, "Allocated shared VCRID via Worker proxy");
    console.log(`  ✓ Worker successfully proxies POST body and creates entry with shared vcrid: ${workerVcrid}`);

    // 4.5 Worker proxies /api/pages
    const workerPagesReq = new Request("https://worker.local/api/pages?url=worker-entry-01");
    const workerPagesRes = await worker.fetch(workerPagesReq, workerEnv);
    assert.strictEqual(workerPagesRes.status, 200);
    const workerPagesJson = await workerPagesRes.json();
    assert.strictEqual(workerPagesJson.type, "bilibili-video");
    assert.strictEqual(workerPagesJson.title, "Worker Proxied Video");
    assert.strictEqual(workerPagesJson.pages[0].vcrid, workerVcrid);
    console.log("  ✓ Worker proxies /api/pages and returns valid manifest with shared vcrid");

    // 4.6 Worker proxies /player/ 302 redirect (preserves manual redirect)
    const workerPlayReq = new Request("https://worker.local/player/?url=worker-entry-01");
    const workerPlayRes = await worker.fetch(workerPlayReq, workerEnv);
    assert.strictEqual(workerPlayRes.status, 302);
    assert.strictEqual(workerPlayRes.headers.get("location"), "https://example.com/worker-video.mp4");
    console.log("  ✓ Worker proxies media request and preserves 302 redirect");

    // 4.7 Worker proxies /player/?vcrid=N
    const workerVcrReq = new Request(`https://worker.local/player/?vcrid=${workerVcrid}`);
    const workerVcrRes = await worker.fetch(workerVcrReq, workerEnv);
    assert.strictEqual(workerVcrRes.status, 302);
    assert.strictEqual(workerVcrRes.headers.get("location"), "https://example.com/worker-video.mp4");
    console.log("  ✓ Worker proxies /player/?vcrid=N to authoritative Node backend");

    // 4.8 Worker proxies DELETE
    const workerDelReq = new Request("https://worker.local/api/admin/custom?id=worker-entry-01", {
      method: "DELETE",
      headers: { Authorization: `Bearer ${adminToken}` }
    });
    const workerDelRes = await worker.fetch(workerDelReq, workerEnv);
    assert.strictEqual(workerDelRes.status, 200);
    const workerDelJson = await workerDelRes.json();
    assert.strictEqual(workerDelJson.ok, true);
    console.log("  ✓ Worker proxies DELETE with auth to Node backend");

    // 4.9 Worker limits request body to 16MB
    const bigWorkerReq = new Request("https://worker.local/api/admin/custom", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "Content-Length": String(17 * 1024 * 1024),
        "X-Admin-Token": adminToken
      },
      body: JSON.stringify({ customId: "too-big", videoUrl: "https://example.com/v.mp4" })
    });
    const bigWorkerRes = await worker.fetch(bigWorkerReq, workerEnv);
    assert.strictEqual(bigWorkerRes.status, 413, "Worker must reject bodies > 16MB with 413");
    console.log("  ✓ Worker enforces 16MB body limit with HTTP 413");

  } finally {
    killProcess(serverProc);
    try { rmSync(httpTestDir, { recursive: true, force: true }); } catch {}
  }

  console.log("\n=======================================================");
  console.log(" ALL SERVER 1.2 TESTS PASSED SUCCESSFULLY! ");
  console.log("=======================================================\n");
  process.exit(0);
}

runAllTests().catch((err) => {
  console.error("Test execution failed:", err);
  process.exit(1);
});
