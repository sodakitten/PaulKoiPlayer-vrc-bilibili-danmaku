# PaulKoiPlayer 后端 1.2 正式版

服务器后端正式包：`vrc-bilibili-danmaku-server-v1.2.zip`。
本次不包含新的 Unity 包，不替换已有前端 Release 附件。

## 更新内容

- `/admin` 管理自定义 ID、标题、HTTP(S) 媒体直链及 XML / JSON / YBDM 弹幕。
- 自定义条目复用持久化 vcrid，支持现有播放、弹幕和清单接口。
- 修复编辑条目被误判为重复新增；不上传新文件时保留原弹幕及 vcrid。
- 密钥输入支持睁眼/斜杠眼图标切换，凭证仅保存在当前页面会话中。
- 播放、弹幕、清单和 vcrid 链接增加快捷复制按钮。
- 主页 BV / av 与网易云历史记录优先跳转原网站页面。
- 保留 B 站普通视频、多 P、直播、列表和网易云歌曲/歌单/歌词功能，以及历史分页、统计和缓存。

## Docker 部署

解压 ZIP 后进入包含 `docker-compose.yml` 的目录：

```powershell
docker compose build
docker compose up -d
curl.exe http://127.0.0.1:7858/health
```

宿主机不需要 Node.js。镜像为 `paulkoi-danmaku-server:1.2`，端口保持
`7858:3000`。首页 `/`，管理页面 `/admin`。

管理功能需要在同级本地 `.env` 配置自己的 `ADMIN_TOKEN`，然后重建容器。
未配置时管理 API 关闭。详细步骤见包内 `README.Docker.md`。

## 升级与兼容

先停止服务并备份原 `data/` 与 `.env`，保留两项、替换程序文件后运行
`docker compose up -d --build`。vcrid、历史记录、统计、自定义条目和磁盘缓存
继续使用 `./data:/app/data`。发布包不包含密钥、Cookie 或用户数据。

原有 `/player`、`/api/resolve`、`/api/danmaku`、`/api/pages` 和歌词接口保持兼容。
旧房间接口仍返回 `410 Gone`。已有客户端的自定义条目接入方式不变。

## 验证范围

已运行 Node 语法检查与自动化测试，覆盖弹幕解析、编辑、认证、持久化、
vcrid、302/文本响应、密钥图标、复制及失败回退、历史链接和实验 Worker
后端代理回归，并检查 Docker 构建、健康接口及发布包内容。
本次没有新的 Unity 或 VRChat 实机验收；媒体上游的实时可用性仍取决于第三方。

发布标签：`v1.2`；npm 标准版本号：`1.2.0`。本次不部署 Worker。
