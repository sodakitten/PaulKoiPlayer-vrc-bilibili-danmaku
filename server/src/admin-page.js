/**
 * Admin management page UI for PaulKoiPlayer Custom Danmaku mappings.
 * Shared between Node.js server and Cloudflare Worker.
 */

export function renderAdminPage({ logoPath = "/assets/paulkoi_logo_transparent.png" } = {}) {
  return `<!doctype html>
<html lang="zh-CN">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <link rel="icon" type="image/png" href="${logoPath}">
  <title>PaulKoiPlayer 自定义弹幕管理</title>
  <style>
    :root {
      --bg: #f6f7f9;
      --panel: #ffffff;
      --soft: #eef6f4;
      --soft-warn: #fffbeb;
      --soft-error: #fef2f2;
      --text: #172026;
      --muted: #65717b;
      --line: #dce3e8;
      --green: #0f8f72;
      --green-dark: #0a604e;
      --amber: #b45309;
      --red: #b91c1c;
      --shadow: 0 16px 40px rgba(25, 38, 49, 0.10);
    }
    * { box-sizing: border-box; }
    body {
      margin: 0;
      min-height: 100vh;
      font-family: ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", "Microsoft YaHei", sans-serif;
      background: var(--bg);
      color: var(--text);
    }
    .shell {
      max-width: 1180px;
      margin: 0 auto;
      padding: 32px 20px 48px;
    }
    header {
      display: flex;
      justify-content: space-between;
      align-items: flex-end;
      gap: 24px;
      padding-bottom: 24px;
      border-bottom: 1px solid var(--line);
    }
    .brand {
      display: flex;
      align-items: center;
      gap: 16px;
      min-width: 0;
    }
    .brand-logo {
      width: 72px;
      height: 72px;
      object-fit: contain;
      flex: 0 0 auto;
      filter: drop-shadow(0 10px 18px rgba(0, 138, 210, 0.18));
    }
    h1 {
      margin: 0;
      font-size: 32px;
      line-height: 1.15;
    }
    .eyebrow {
      margin: 0 0 6px;
      color: var(--green);
      font-size: 13px;
      font-weight: 700;
      text-transform: uppercase;
      letter-spacing: 0.5px;
    }
    .nav-links {
      display: flex;
      gap: 10px;
      align-items: center;
    }
    .badge-link {
      display: inline-flex;
      align-items: center;
      gap: 8px;
      min-height: 36px;
      padding: 0 14px;
      border: 1px solid #b9ddd5;
      border-radius: 8px;
      background: var(--soft);
      color: var(--green-dark);
      font-weight: 700;
      font-size: 14px;
      text-decoration: none;
      transition: all .15s ease;
    }
    .badge-link:hover {
      background: #e2f1ed;
      border-color: var(--green);
    }
    .panel {
      margin-top: 20px;
      padding: 24px;
      border: 1px solid var(--line);
      border-radius: 10px;
      background: var(--panel);
      box-shadow: var(--shadow);
    }
    h2 {
      margin: 0 0 16px;
      font-size: 20px;
      display: flex;
      align-items: center;
      gap: 10px;
    }
    .alert-box {
      padding: 14px 18px;
      border-radius: 8px;
      margin-bottom: 18px;
      font-size: 14px;
      line-height: 1.5;
    }
    .alert-info {
      background: var(--soft);
      border: 1px solid #b9ddd5;
      color: #0b5344;
    }
    .alert-warn {
      background: var(--soft-warn);
      border: 1px solid #fed7aa;
      color: var(--amber);
    }
    .alert-error {
      background: var(--soft-error);
      border: 1px solid #fecaca;
      color: var(--red);
    }
    .form-grid {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 16px;
    }
    .form-full {
      grid-column: 1 / -1;
    }
    .form-group {
      display: flex;
      flex-direction: column;
      gap: 6px;
    }
    label {
      font-size: 14px;
      font-weight: 700;
      color: var(--text);
    }
    .help-text {
      font-size: 12px;
      color: var(--muted);
      line-height: 1.4;
    }
    input[type="text"], input[type="password"], input[type="file"] {
      width: 100%;
      min-height: 40px;
      padding: 8px 12px;
      border: 1px solid var(--line);
      border-radius: 8px;
      background: #fff;
      color: var(--text);
      font-family: inherit;
      font-size: 14px;
      outline: none;
      transition: border-color .15s ease;
    }
    input[type="text"]:focus, input[type="password"]:focus {
      border-color: var(--green);
      box-shadow: 0 0 0 3px rgba(15, 143, 114, 0.12);
    }
    input[type="file"] {
      padding: 6px 10px;
      cursor: pointer;
    }
    .btn-row {
      display: flex;
      gap: 12px;
      align-items: center;
      margin-top: 20px;
    }
    button {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      min-height: 40px;
      padding: 0 20px;
      border: 0;
      border-radius: 8px;
      font-family: inherit;
      font-size: 14px;
      font-weight: 700;
      cursor: pointer;
      transition: all .15s ease;
    }
    .btn-primary {
      background: var(--green);
      color: #fff;
    }
    .btn-primary:hover {
      background: var(--green-dark);
    }
    .btn-secondary {
      background: #e2e8f0;
      color: #334155;
    }
    .btn-secondary:hover {
      background: #cbd5e1;
    }
    .btn-danger {
      background: #fee2e2;
      color: var(--red);
      border: 1px solid #fecaca;
      padding: 4px 10px;
      min-height: 32px;
      font-size: 13px;
    }
    .btn-danger:hover {
      background: #fca5a5;
      color: #fff;
    }
    .btn-edit {
      background: var(--soft);
      color: var(--green-dark);
      border: 1px solid #b9ddd5;
      padding: 4px 10px;
      min-height: 32px;
      font-size: 13px;
    }
    .btn-edit:hover {
      background: #d1fae5;
    }
    .token-bar {
      display: flex;
      flex-wrap: wrap;
      gap: 12px;
      align-items: center;
    }
    .token-input {
      position: relative;
      flex: 1;
      min-width: min(260px, 100%);
    }
    .token-input input {
      padding-right: 48px;
    }
    .token-visibility {
      position: absolute;
      right: 4px;
      top: 50%;
      transform: translateY(-50%);
      width: 36px;
      height: 32px;
      min-height: 32px;
      padding: 0;
      justify-content: center;
      background: transparent;
      color: var(--muted);
    }
    .token-visibility:hover {
      background: var(--soft);
      color: var(--green-dark);
    }
    .token-visibility svg {
      width: 20px;
      height: 20px;
    }
    .token-status {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      font-size: 13px;
      font-weight: 700;
      padding: 4px 10px;
      border-radius: 6px;
    }
    .token-status-active {
      background: var(--soft);
      color: var(--green-dark);
      border: 1px solid #b9ddd5;
    }
    .token-status-missing {
      background: var(--soft-warn);
      color: var(--amber);
      border: 1px solid #fed7aa;
    }
    .token-status:before {
      content: "";
      width: 7px;
      height: 7px;
      border-radius: 999px;
      background: currentColor;
    }
    table {
      width: 100%;
      border-collapse: collapse;
      margin-top: 14px;
      font-size: 14px;
    }
    th, td {
      padding: 12px 14px;
      text-align: left;
      border-bottom: 1px solid var(--line);
      vertical-align: middle;
    }
    th {
      color: var(--muted);
      font-size: 13px;
      font-weight: 700;
      background: #fafbfc;
    }
    tr:hover td {
      background: #fbfcfd;
    }
    .link-group {
      display: flex;
      flex-wrap: wrap;
      gap: 6px;
      font-size: 12px;
    }
    .link-btn {
      display: inline-block;
      padding: 3px 8px;
      border-radius: 4px;
      background: #f1f5f9;
      color: #334155;
      text-decoration: none;
      font-weight: 600;
    }
    .link-btn:hover {
      background: #e2e8f0;
      color: var(--green);
    }
    .link-action {
      display: inline-flex;
      align-items: center;
      gap: 2px;
      white-space: nowrap;
    }
    .copy-link {
      width: 28px;
      height: 28px;
      min-height: 28px;
      padding: 0;
      justify-content: center;
      background: #f1f5f9;
      color: #334155;
      border-radius: 4px;
    }
    .copy-link:hover {
      background: #e2e8f0;
      color: var(--green);
    }
    .copy-link svg {
      width: 16px;
      height: 16px;
    }
    .copy-status {
      margin-top: 10px;
      color: var(--green-dark);
      font-size: 13px;
    }
    .code-tag {
      font-family: "Cascadia Mono", Consolas, monospace;
      font-size: 13px;
      padding: 2px 6px;
      border-radius: 4px;
      background: #f1f5f9;
      color: #0f172a;
    }
    .video-url-preview {
      max-width: 220px;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
      display: inline-block;
      color: var(--muted);
      font-size: 13px;
    }
    .empty-state {
      padding: 40px;
      text-align: center;
      color: var(--muted);
    }
    @media (max-width: 880px) {
      header { flex-direction: column; align-items: flex-start; }
      .form-grid { grid-template-columns: 1fr; }
      th, td { padding: 10px 8px; }
    }
  </style>
</head>
<body>
  <main class="shell">
    <header>
      <div class="brand">
        <img class="brand-logo" src="${logoPath}" alt="PaulKoiPlayer">
        <div>
          <p class="eyebrow">PaulKoiPlayer Danmaku Server</p>
          <h1>自定义弹幕映射管理</h1>
        </div>
      </div>
      <div class="nav-links">
        <a href="/" class="badge-link">&larr; 返回仪表盘概览</a>
      </div>
    </header>

    <!-- Token Session Bar -->
    <section class="panel">
      <h2>&#128272; 管理员凭证 (Admin Token)</h2>
      <div class="token-bar">
        <div class="token-input">
          <input type="password" id="adminTokenInput" placeholder="请输入服务器 ADMIN_TOKEN 凭证" autocomplete="off">
          <button type="button" class="token-visibility" id="toggleTokenBtn" title="显示密钥" aria-label="显示密钥" aria-controls="adminTokenInput" aria-pressed="false">
            <svg id="tokenEye" style="display:none" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M2.062 12.348a1 1 0 0 1 0-.696 10.75 10.75 0 0 1 19.876 0 1 1 0 0 1 0 .696 10.75 10.75 0 0 1-19.876 0"/><circle cx="12" cy="12" r="3"/></svg>
            <svg id="tokenEyeOff" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m2 2 20 20"/><path d="M10.58 10.587a2 2 0 0 0 2.83 2.83"/><path d="M9.88 5.09A10.94 10.94 0 0 1 12 4.75c4.76 0 8.87 2.91 10 7.25a10.94 10.94 0 0 1-2.51 4.36"/><path d="M6.61 6.61A10.94 10.94 0 0 0 2 12c1.13 4.34 5.24 7.25 10 7.25a10.94 10.94 0 0 0 5.39-1.86"/></svg>
          </button>
        </div>
        <button type="button" class="btn-primary" id="saveTokenBtn">保存至当前会话</button>
        <button type="button" class="btn-secondary" id="clearTokenBtn">清除凭证</button>
        <div class="token-status token-status-missing" id="tokenStatus">未配置当前会话凭证</div>
      </div>
      <p class="help-text" style="margin: 10px 0 0;">
        安全提示：管理员令牌仅保存在当前网页的 JavaScript 闭包变量内存中（刷新页面即清空），<strong>绝不写入 URL、localStorage、sessionStorage、日志或打包交付包</strong>。若服务端环境变量未配置 <code>ADMIN_TOKEN</code>，写操作将被强制关闭以防公共服务被恶意篡改。
      </p>
    </section>

    <!-- Local Video Notice Banner -->
    <section class="panel" style="background: var(--soft-warn); border-color: #fed7aa;">
      <h2 style="color: var(--amber); margin-bottom: 8px;">&#128161; 本地视频直链配置说明 (必读)</h2>
      <p style="margin: 0; font-size: 14px; line-height: 1.6; color: #78350f;">
        VRChat 游戏内播放器<strong>无法直接读取您本地电脑上的路径（不支持 <code>file://</code>、<code>D:\\...</code> 等本地驱动器路径）</strong>。
        若要播放本地视频，请先将本地视频文件部署为所有玩家均可通过网络访问的 <strong>HTTP 或 HTTPS 直链</strong>（例如：局域网 Web 服务器、NAS 共享直链、对象存储 S3/OSS 或网盘直链）。<br>
        <strong>本后端仅负责保存该媒体直链并向 VRChat 播放器提供 302 重定向跳转与配套弹幕，绝不上传亦不中转海量视频文件本身。</strong>
      </p>
    </section>

    <!-- Mapping Form Panel -->
    <section class="panel">
      <h2 id="formTitleHeader">&#10133; 新增自定义弹幕条目</h2>
      <div id="formAlert" class="alert-box" style="display: none;"></div>

      <form id="customMappingForm" autocomplete="off">
        <div class="form-grid">
          <div class="form-group">
            <label for="inputCustomId">自定义 ID (Custom ID) <span style="color: var(--red);">*</span></label>
            <input type="text" id="inputCustomId" placeholder="例如: 12345 或 episode-01" required pattern="^[a-zA-Z0-9_-]+$" maxlength="64">
            <span class="help-text">用于 <code>/player/?url=&lt;ID&gt;</code> 访问标识。支持字母、数字、下划线及连字符，不可与 B 站 BV/av 冲突。</span>
          </div>

          <div class="form-group">
            <label for="inputTitle">视频标题 (Title) <span style="color: var(--red);">*</span></label>
            <input type="text" id="inputTitle" placeholder="例如: 星球大战：曼达洛人与古古原版" required maxlength="200">
            <span class="help-text">在 VRChat 播放列表和播放器 UI 中展示的标题（若上传的 JSON 包含 meta.title 可自动提取）。</span>
          </div>

          <div class="form-group form-full">
            <label for="inputVideoUrl">可访问的 HTTP(S) 视频媒体直链 <span style="color: var(--red);">*</span></label>
            <input type="text" id="inputVideoUrl" placeholder="例如: https://media.example.com/videos/ep01.mp4" required maxlength="2048">
            <span class="help-text">仅支持 <code>http://</code> 或 <code>https://</code> 协议的媒体文件直链。播放器访问时将获得 302 重定向。</span>
          </div>

          <div class="form-group form-full">
            <label for="inputDanmakuFile" id="danmakuFileLabel">本地弹幕文件 (XML / JSON / YBDM) <span id="danmakuRequiredMark" style="color: var(--red);">*</span></label>
            <input type="file" id="inputDanmakuFile" accept=".xml,.json,.ybdm,text/xml,application/json,text/plain">
            <span class="help-text" id="danmakuFileHelp">
              支持 B 站原生 XML 弹幕文件、导出的结构化 JSON 弹幕文件或已有的 #YBDM/1 格式文件。时间单位将严格校验，弹幕上限保留 4096 条。编辑已有条目时若不更换弹幕可留空。
            </span>
          </div>
        </div>

        <div class="btn-row">
          <button type="submit" class="btn-primary" id="submitBtn">保存条目配置</button>
          <button type="button" class="btn-secondary" id="resetBtn">重置表单</button>
        </div>
      </form>
    </section>

    <!-- Mappings List Panel -->
    <section class="panel">
      <div style="display: flex; justify-content: space-between; align-items: center;">
        <h2 style="margin: 0;">&#128203; 已配置自定义弹幕映射列表</h2>
        <button type="button" class="btn-secondary" id="refreshListBtn">&#8635; 刷新列表</button>
      </div>

      <div style="overflow-x: auto;">
        <table>
          <thead>
            <tr>
              <th>自定义 ID</th>
              <th>标题</th>
              <th>分配 vcrid</th>
              <th>弹幕条数</th>
              <th>视频直链</th>
              <th>测试与调用入口</th>
              <th style="text-align: right;">操作</th>
            </tr>
          </thead>
          <tbody id="customItemsTableBody">
            <tr>
              <td colspan="7" class="empty-state">正在加载列表...</td>
            </tr>
          </tbody>
        </table>
      </div>
      <div id="copyStatus" class="copy-status" role="status" aria-live="polite"></div>
      <input id="manualCopyInput" type="text" readonly hidden aria-label="待复制的完整链接">
    </section>

    <!-- VRChat & Unity Integration Notes -->
    <section class="panel" style="background: #f8fafc;">
      <h2>&#127918; VRChat & Unity 快速接入说明</h2>
      <ol style="margin: 0; padding-left: 20px; font-size: 14px; line-height: 1.8; color: #334155;">
        <li>在 Unity / VRChat YamaPlayer 播放器中，直接输入播放地址：<code class="code-tag">https://&lt;你的域名&gt;/player/?url=12345</code>（或者若开启前缀补全，输入框自动预填前缀后只需输入 <code class="code-tag">12345</code>）。</li>
        <li>YamaPlayer 的播放列表组件会自动请求 <code class="code-tag">/api/pages?url=12345</code>，识别该单项并读取配置的标题，分配稳定的 <code>vcrid</code> 加入播放列表。</li>
        <li>视频播放器开始请求时，后端自动 <strong>302 重定向</strong> 至您填写的媒体直链；同时 VRChat 弹幕组件（VRCStringDownloader）自动请求并获取 <strong>#YBDM/1</strong> 规范弹幕并同步渲染。</li>
        <li><strong>完全无需修改 Unity 客户端或 Shader</strong>，与原 B 站及网易云功能完全并存！</li>
      </ol>
    </section>
  </main>

  <script>
    (function() {
      const tokenInput = document.getElementById('adminTokenInput');
      const toggleTokenBtn = document.getElementById('toggleTokenBtn');
      const tokenEye = document.getElementById('tokenEye');
      const tokenEyeOff = document.getElementById('tokenEyeOff');
      const saveTokenBtn = document.getElementById('saveTokenBtn');
      const clearTokenBtn = document.getElementById('clearTokenBtn');
      const tokenStatus = document.getElementById('tokenStatus');

      const form = document.getElementById('customMappingForm');
      const formTitleHeader = document.getElementById('formTitleHeader');
      const formAlert = document.getElementById('formAlert');
      const inputCustomId = document.getElementById('inputCustomId');
      const inputTitle = document.getElementById('inputTitle');
      const inputVideoUrl = document.getElementById('inputVideoUrl');
      const inputDanmakuFile = document.getElementById('inputDanmakuFile');
      const danmakuRequiredMark = document.getElementById('danmakuRequiredMark');
      const danmakuFileHelp = document.getElementById('danmakuFileHelp');
      const submitBtn = document.getElementById('submitBtn');
      const resetBtn = document.getElementById('resetBtn');

      const tableBody = document.getElementById('customItemsTableBody');
      const refreshListBtn = document.getElementById('refreshListBtn');
      const copyStatus = document.getElementById('copyStatus');
      const manualCopyInput = document.getElementById('manualCopyInput');

      let isEditing = false;
      let currentAdminToken = '';

      function getSessionToken() {
        return currentAdminToken;
      }

      function setSessionToken(token) {
        currentAdminToken = token ? String(token).trim() : '';
        setTokenVisibility(false);
        updateTokenUi();
      }

      function setTokenVisibility(visible) {
        tokenInput.type = visible ? 'text' : 'password';
        const label = visible ? '隐藏密钥' : '显示密钥';
        toggleTokenBtn.title = label;
        toggleTokenBtn.setAttribute('aria-label', label);
        toggleTokenBtn.setAttribute('aria-pressed', String(visible));
        tokenEye.style.display = visible ? '' : 'none';
        tokenEyeOff.style.display = visible ? 'none' : '';
      }

      toggleTokenBtn.addEventListener('click', () => {
        setTokenVisibility(tokenInput.type === 'password');
      });

      function updateTokenUi() {
        const token = getSessionToken();
        if (token) {
          tokenStatus.className = 'token-status token-status-active';
          tokenStatus.textContent = '当前会话凭证已配置';
          tokenInput.value = token;
        } else {
          tokenStatus.className = 'token-status token-status-missing';
          tokenStatus.textContent = '未配置当前会话凭证';
          tokenInput.value = '';
        }
      }

      saveTokenBtn.addEventListener('click', () => {
        const token = tokenInput.value.trim();
        if (!token) {
          showAlert('请输入管理员凭证后再保存。', 'alert-warn');
          return;
        }
        setSessionToken(token);
        showAlert('管理员凭证已保存至当前会话内存。', 'alert-info');
        loadItems();
      });

      clearTokenBtn.addEventListener('click', () => {
        setSessionToken('');
        showAlert('已清除当前会话凭证。', 'alert-warn');
      });

      function showAlert(msg, type = 'alert-info') {
        formAlert.className = 'alert-box ' + type;
        formAlert.textContent = msg;
        formAlert.style.display = 'block';
      }

      function hideAlert() {
        formAlert.style.display = 'none';
      }

      function escapeHtml(str) {
        return String(str || '')
          .replace(/&/g, '&amp;')
          .replace(/</g, '&lt;')
          .replace(/>/g, '&gt;')
          .replace(/"/g, '&quot;')
          .replace(/'/g, '&#39;');
      }

      function resetForm() {
        form.reset();
        isEditing = false;
        inputCustomId.disabled = false;
        formTitleHeader.innerHTML = '&#10133; 新增自定义弹幕条目';
        submitBtn.textContent = '保存条目配置';
        danmakuRequiredMark.style.display = 'inline';
        danmakuFileHelp.textContent = '支持 B 站原生 XML 弹幕文件、导出的结构化 JSON 弹幕文件或已有的 #YBDM/1 格式文件。时间单位将严格校验，弹幕上限保留 4096 条。编辑已有条目时若不更换弹幕可留空。';
        hideAlert();
      }

      resetBtn.addEventListener('click', resetForm);

      // Extract title from JSON file automatically if user hasn't typed one
      inputDanmakuFile.addEventListener('change', async () => {
        const file = inputDanmakuFile.files[0];
        if (!file) return;
        if (!inputTitle.value.trim() && (file.name.endsWith('.json') || file.type.includes('json'))) {
          try {
            const text = await file.text();
            const data = JSON.parse(text);
            const foundTitle = data?.meta?.title || data?.title;
            if (foundTitle && !inputTitle.value.trim()) {
              inputTitle.value = String(foundTitle).trim();
            }
          } catch {}
        }
      });

      form.addEventListener('submit', async (e) => {
        e.preventDefault();
        hideAlert();

        const token = getSessionToken();
        if (!token) {
          showAlert('请先在上方输入管理员凭证并保存至当前会话。', 'alert-warn');
          return;
        }

        const customId = inputCustomId.value.trim();
        const title = inputTitle.value.trim();
        const videoUrl = inputVideoUrl.value.trim();
        const file = inputDanmakuFile.files[0];

        if (!customId) {
          showAlert('请填写自定义 ID。', 'alert-warn');
          return;
        }
        if (!title) {
          showAlert('请填写视频标题。', 'alert-warn');
          return;
        }
        if (!videoUrl) {
          showAlert('请填写视频媒体直链。', 'alert-warn');
          return;
        }
        if (!isEditing && !file) {
          showAlert('新建条目时必须选择本地弹幕文件 (XML、JSON 或 YBDM)。', 'alert-warn');
          return;
        }

        let danmakuText = '';
        if (file) {
          if (file.size > 16 * 1024 * 1024) {
            showAlert('上传文件大小不可超过 16MB。', 'alert-error');
            return;
          }
          submitBtn.disabled = true;
          submitBtn.textContent = '正在读取与解析文件...';
          try {
            danmakuText = await file.text();
          } catch (err) {
            submitBtn.disabled = false;
            submitBtn.textContent = isEditing ? '更新条目配置' : '保存条目配置';
            showAlert('读取本地文件失败: ' + err.message, 'alert-error');
            return;
          }
        }

        submitBtn.disabled = true;
        submitBtn.textContent = '正在提交保存...';

        try {
          const res = await fetch('/api/admin/custom', {
            method: 'POST',
            headers: {
              'Content-Type': 'application/json',
              'Authorization': 'Bearer ' + token
            },
            body: JSON.stringify({
              customId,
              title,
              videoUrl,
              danmaku: danmakuText,
              replaceExisting: isEditing
            })
          });

          const data = await res.json();
          if (!res.ok) {
            let errMsg = data?.message || data?.error || '保存失败 (' + res.status + ')';
            if (res.status === 403) {
              errMsg = '服务端管理写入已被禁用 (未配置 ADMIN_TOKEN 环境变量)。';
            } else if (res.status === 401) {
              errMsg = '管理员凭证无效，请检查会话凭证。';
            }
            showAlert(errMsg, 'alert-error');
          } else {
            let successMsg = '成功保存自定义条目: ' + customId + ' (弹幕数: ' + (data?.item?.count ?? 0) + ' 条，分配 vcrid: ' + (data?.item?.vcrid ?? '-') + ')';
            if (data?.truncatedCount > 0) {
              successMsg += ' [注意：弹幕超过4096条上限，已按时间排序截取前4096条，超出 ' + data.truncatedCount + ' 条]';
            }
            showAlert(successMsg, 'alert-info');
            resetForm();
            loadItems();
          }
        } catch (err) {
          showAlert('网络请求异常: ' + err.message, 'alert-error');
        } finally {
          submitBtn.disabled = false;
          submitBtn.textContent = isEditing ? '更新条目配置' : '保存条目配置';
        }
      });

      function renderLinkAction(path, label, title) {
        const escapedPath = escapeHtml(path);
        const copyLabel = '复制' + label + '链接';
        return '<span class="link-action">' +
          '<a class="link-btn" href="' + escapedPath + '" target="_blank" title="' + escapeHtml(title) + '">' + escapeHtml(label) + '</a>' +
          '<button type="button" class="copy-link" data-copy="' + escapedPath + '" data-label="' + escapeHtml(label) + '" title="' + escapeHtml(copyLabel) + '" aria-label="' + escapeHtml(copyLabel) + '">' +
          '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect width="14" height="14" x="8" y="8" rx="2" ry="2"/><path d="M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2"/></svg>' +
          '</button></span>';
      }

      async function copyLink(button) {
        const url = new URL(button.getAttribute('data-copy'), window.location.origin).href;
        const label = button.getAttribute('data-label');
        manualCopyInput.hidden = true;
        manualCopyInput.value = '';
        button.disabled = true;
        let copied = false;
        try {
          if (navigator.clipboard?.writeText) {
            await navigator.clipboard.writeText(url);
            copied = true;
          }
        } catch {}
        if (!copied) {
          // Older HTTP browsers may only support selection-based copying.
          const selection = document.createElement('textarea');
          selection.value = url;
          selection.style.position = 'fixed';
          selection.style.opacity = '0';
          document.body.appendChild(selection);
          try {
            selection.select();
            copied = document.execCommand('copy');
          } catch {} finally {
            selection.remove();
          }
        }
        button.disabled = false;
        copyStatus.textContent = copied ? '已复制' + label + '链接。' : '浏览器未允许自动复制，请复制下方链接。';
        if (!copied) {
          manualCopyInput.value = url;
          manualCopyInput.hidden = false;
          manualCopyInput.focus();
          manualCopyInput.select();
        }
      }

      async function loadItems() {
        const token = getSessionToken();
        tableBody.innerHTML = '<tr><td colspan="7" class="empty-state">正在加载列表...</td></tr>';
        try {
          const res = await fetch('/api/admin/custom', {
            headers: token ? { 'Authorization': 'Bearer ' + token } : {}
          });
          const data = await res.json();
          if (!res.ok) {
            if (res.status === 403) {
              tableBody.innerHTML = '<tr><td colspan="7" class="empty-state" style="color:var(--amber);">服务端管理功能已关闭（服务端未配置 ADMIN_TOKEN 环境变量）。请在服务器配置环境变量后使用。</td></tr>';
            } else if (res.status === 401) {
              tableBody.innerHTML = '<tr><td colspan="7" class="empty-state">需要管理员凭证才能查看列表，请在上方输入凭证并保存。</td></tr>';
            } else {
              tableBody.innerHTML = '<tr><td colspan="7" class="empty-state" style="color:var(--red);">加载失败: ' + escapeHtml(data?.message || data?.error || res.status) + '</td></tr>';
            }
            return;
          }

          const items = Array.isArray(data?.items) ? data.items : [];
          if (items.length === 0) {
            tableBody.innerHTML = '<tr><td colspan="7" class="empty-state">当前暂无已配置的自定义弹幕条目。请在上方表单添加。</td></tr>';
            return;
          }

          let html = '';
          for (const item of items) {
            const cid = escapeHtml(item.customId);
            const title = escapeHtml(item.title);
            const vcrid = escapeHtml(item.vcrid || '-');
            const count = escapeHtml(item.count || 0);
            const videoUrl = escapeHtml(item.videoUrl || '');

            html += '<tr>' +
              '<td><code class="code-tag">' + cid + '</code></td>' +
              '<td><strong>' + title + '</strong></td>' +
              '<td><code class="code-tag">' + vcrid + '</code></td>' +
              '<td>' + count + ' 条</td>' +
              '<td><a class="video-url-preview" href="' + videoUrl + '" target="_blank" rel="noopener" title="' + videoUrl + '">' + videoUrl + '</a></td>' +
              '<td>' +
                '<div class="link-group">' +
                  renderLinkAction('/player/?url=' + encodeURIComponent(item.customId), '播放', '测试播放器重定向') +
                  renderLinkAction('/player/?__dm=1&url=' + encodeURIComponent(item.customId), '弹幕', '测试弹幕输出') +
                  renderLinkAction('/api/pages?url=' + encodeURIComponent(item.customId), '清单', 'Unity 清单接口') +
                  (item.vcrid ? renderLinkAction('/player/?vcrid=' + item.vcrid, 'vcrid 播放', 'vcrid 播放') : '') +
                '</div>' +
              '</td>' +
              '<td style="text-align: right; white-space: nowrap;">' +
                '<button type="button" class="btn-edit" data-id="' + cid + '" data-title="' + title + '" data-url="' + videoUrl + '">编辑</button> ' +
                '<button type="button" class="btn-danger" data-del="' + cid + '">删除</button>' +
              '</td>' +
            '</tr>';
          }
          tableBody.innerHTML = html;

          // Attach actions
          tableBody.querySelectorAll('.copy-link').forEach(button => {
            button.addEventListener('click', () => copyLink(button));
          });
          tableBody.querySelectorAll('.btn-edit').forEach(btn => {
            btn.addEventListener('click', () => {
              isEditing = true;
              inputCustomId.value = btn.getAttribute('data-id');
              inputCustomId.disabled = true;
              inputTitle.value = btn.getAttribute('data-title');
              inputVideoUrl.value = btn.getAttribute('data-url');
              formTitleHeader.innerHTML = '&#9998; 编辑条目配置: <code class="code-tag">' + btn.getAttribute('data-id') + '</code>';
              submitBtn.textContent = '更新条目配置';
              danmakuRequiredMark.style.display = 'none';
              danmakuFileHelp.textContent = '（编辑模式）：若不需要更换弹幕，此处留空即可保留原有弹幕文件；若需更换可重新选择。';
              form.scrollIntoView({ behavior: 'smooth' });
            });
          });

          tableBody.querySelectorAll('.btn-danger').forEach(btn => {
            btn.addEventListener('click', async () => {
              const id = btn.getAttribute('data-del');
              if (!confirm('确定要删除自定义条目 [' + id + '] 吗？此操作将移除映射及关联弹幕。')) return;
              const token = getSessionToken();
              if (!token) {
                showAlert('请先配置管理员凭证后再执行删除。', 'alert-warn');
                return;
              }
              try {
                const res = await fetch('/api/admin/custom?id=' + encodeURIComponent(id), {
                  method: 'DELETE',
                  headers: { 'Authorization': 'Bearer ' + token }
                });
                const resData = await res.json();
                if (!res.ok) {
                  showAlert('删除失败: ' + (resData?.message || resData?.error || res.status), 'alert-error');
                } else {
                  showAlert('已成功删除条目: ' + id, 'alert-info');
                  loadItems();
                }
              } catch (err) {
                showAlert('删除请求失败: ' + err.message, 'alert-error');
              }
            });
          });
        } catch (err) {
          tableBody.innerHTML = '<tr><td colspan="7" class="empty-state" style="color:var(--red);">请求异常: ' + escapeHtml(err.message) + '</td></tr>';
        }
      }

      refreshListBtn.addEventListener('click', loadItems);

      // Initialize UI on page load
      updateTokenUi();
      loadItems();
    })();
  </script>
</body>
</html>`;
}
