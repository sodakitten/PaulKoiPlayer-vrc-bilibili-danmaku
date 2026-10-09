# PaulKoiPlayer 后端 1.2 Docker 部署

正式版本：`1.2`。npm 版本：`1.2.0`。Docker 镜像：`paulkoi-danmaku-server:1.2`。
宿主机只需安装 Docker 和 Docker Compose，不需要安装 Node.js。
容器包含 Node.js 20，端口映射保持 `7858:3000`。

## 安装与启动

解压 `vrc-bilibili-danmaku-server-v1.2.zip`，进入包含 `docker-compose.yml` 的目录：

```powershell
docker compose build
docker compose up -d
curl.exe http://127.0.0.1:7858/health
```

首页：`http://127.0.0.1:7858/`。管理页面：`http://127.0.0.1:7858/admin`。

## 管理密钥

在 `docker-compose.yml` 同级创建本地 `.env` 文件，填入自己的密钥：

```dotenv
ADMIN_TOKEN=replace-with-your-own-admin-token
```

修改 `.env` 后运行 `docker compose up -d --force-recreate`。
打开 `/admin`，输入密钥并保存至当前会话。小眼睛可以显示或隐藏密钥；
刷新页面后需要重新输入。未配置密钥时，管理 API 会关闭。
`.env` 不属于发布包，也不应提交到 Git。

## 自定义视频与弹幕

填写自定义 ID、标题、可公开访问的 HTTP(S) 视频直链，上传 XML、JSON
或 `#YBDM/1` 弹幕文件并保存。新条目需要弹幕文件；编辑已有条目时不选
新文件即可保留原弹幕和 vcrid。重复新增相同 ID 会被拒绝。

列表提供播放、弹幕、清单和 vcrid 播放链接的复制按钮。复制地址使用当前
页面域名；从 localhost 管理页面复制的是本地地址，需要公网地址时应从
自己的 HTTPS 域名打开管理页面。

服务保存配置与弹幕，并向播放器返回媒体直链的 302 重定向。
本地磁盘路径不是媒体直链，视频文件需部署到播放器能访问的 HTTP(S) 服务。

## 接口

| 地址 | 用途 |
| --- | --- |
| `GET /health` | 健康检查 |
| `GET /player/?url=<编码后的链接或自定义ID>` | 播放重定向、弹幕或歌单清单 |
| `GET /player/?vcrid=<ID>` | 持久化映射的播放与弹幕/歌词 |
| `GET /api/resolve?url=...` | 解析 JSON；也支持已有的 vcrid 请求 |
| `GET /api/danmaku?url=...` | `#YBDM/1` 弹幕；也支持已有的 vcrid 请求 |
| `GET /api/pages?url=...` | 分 P、列表、音乐歌单或自定义条目清单 |
| `GET /api/lyrics?url=...` | 按需查询网易云歌词及封面并缓存 |
| `GET /api/cache/stats` | 缓存及服务统计 |
| `GET /admin` | 管理页面 |
| `GET/POST/DELETE /api/admin/custom` | 需要管理员凭证的条目管理 |

`/player` 文本模式继续识别 `Accept: text/plain`、已有下载器请求头和 `__dm=1`。
原始网易云歌单的文本请求返回 JSON 清单，选中歌曲的 vcrid 文本请求返回
歌词弹幕。普通播放保持原有 302 行为。旧房间接口 `/api/current` 和
`/api/set` 继续返回 `410 Gone`。

```powershell
curl.exe -I "http://127.0.0.1:7858/player/?url=BV1BDk2YCEHF&p=2"
curl.exe -H "Accept: text/plain" "http://127.0.0.1:7858/player/?url=BV1BDk2YCEHF&p=2"
curl.exe "http://127.0.0.1:7858/api/pages?url=BV1BDk2YCEHF"
```

## 升级、数据与备份

Compose 挂载 `./data:/app/data`。其中保存历史记录、vcrid 映射、统计、
自定义条目配置、自定义弹幕文件和 B 站弹幕缓存，容器重建后仍保留。
持久化记录使用 JSON 文件，不需要额外部署数据库。

升级前停止服务并备份整个 `data` 目录和本地 `.env`，保留这两项，
替换程序文件后重新构建：

```powershell
docker compose stop
# 此时备份 data 和 .env，替换程序文件但保留原 data 和 .env。
docker compose up -d --build
curl.exe http://127.0.0.1:7858/health
```

发布包不含用户运行数据、Cookie 或密钥。B 站弹幕新缓存默认保留一天，
命中时续至至少当前时间后一天，不累加。缓存、限流和 vcrid 清理阈值可在
Compose 环境变量中调整。默认 vcrid 池容量为 1,000,000。

## HTTPS 反代与检查

域名反代或隧道指向 `http://127.0.0.1:7858`，传递 `Host`、
`X-Forwarded-Proto` 和 `X-Forwarded-For`；提供 `X-Forwarded-Host` 时应
使用实际公网域名。

```powershell
docker compose logs --tail 100
docker compose ps
```

开发机安装 Node.js 20 或更新版本后，可运行 `npm.cmd run check` 与
`npm.cmd test`。本次正式发布面向 Node/Docker，附带的实验 Worker 文件
不是本次部署目标。媒体上游的可用性仍取决于各自服务。
