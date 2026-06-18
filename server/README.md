# VRChat 弹幕解析服务

[简体中文](README.md) | [English](README_EN.md) | [返回项目主页](../README.md)

这是与 Unity 弹幕组件配套的 Docker 服务。VRChat 世界只需要使用一个入口：

```text
https://你的域名/player/?url=<视频或音乐链接>
```

服务端与 Unity 组件统一使用 **v1.0.0**，可在同一个 [v1.0.0 Release](https://github.com/sodakitten/vrc-bilibili-danmaku/releases/tag/v1.0.0) 下载。

对于普通视频请求，服务返回可播放媒体直链的 `302` 跳转；对于 `VRCStringDownloader` 弹幕请求，同一地址返回 `#YBDM/1` 文本。该设计不依赖 `room`、世界名称或实例 ID，可以同时处理不同世界和不同视频。

## 支持内容

- 哔哩哔哩视频链接、BV 号和 av 号
- 哔哩哔哩 MP4 直链解析
- XML 与 protobuf 分段弹幕，输出 `#YBDM/1`
- 网易云音乐单曲、歌单和 `163cn.tv` 短链接
- 网易云歌单使用 `p` 或 `page` 选择曲目
- 视频信息、直链、弹幕和歌单的内存缓存
- 并发请求合并：相同资源首次加载期间只执行一次上游请求
- 无数据库依赖

> **网易云解析依赖说明：** 网易云音乐的单曲、歌单和短链接解析由第三方服务 [https://music.znnu.com/](https://music.znnu.com/) 提供。本项目仅调用其接口，不是该服务的官方项目。该功能的可用性、限流、地区限制和接口变化由第三方服务决定。

## 快速部署

服务器不需要预装 Node.js，Node.js 20 已包含在 Docker 镜像中。

```bash
docker compose up -d --build
```

默认端口映射：

```text
宿主机 7858 -> 容器 3000
```

检查运行状态：

```bash
docker compose ps
curl http://127.0.0.1:7858/health
```

查看状态面板：

```text
http://127.0.0.1:7858/
```

查看缓存统计：

```text
http://127.0.0.1:7858/api/cache/stats
```

更新服务：

```bash
git pull
docker compose up -d --build
```

## 使用方法

### 哔哩哔哩视频

```text
https://你的域名/player/?url=BV1PV7m6DE51
https://你的域名/player/?url=https://www.bilibili.com/video/BV1PV7m6DE51
```

强制返回弹幕，适合诊断：

```text
https://你的域名/player/?__dm=1&url=BV1PV7m6DE51
https://你的域名/api/danmaku?url=BV1PV7m6DE51
```

正常播放请求返回 `302 Location`。弹幕请求返回：

```text
#YBDM/1
#columns=progressMs	mode	color	fontSize	pool	content
```

服务会通过 `__dm=1`、文本类型请求头以及 VRChat 字符串下载器的 User-Agent 识别弹幕请求。

### 网易云音乐

```text
https://你的域名/player/?url=music.163.com/song?id=346075
https://你的域名/player/?url=music.163.com/playlist?id=487424073
https://你的域名/player/?url=music.163.com/playlist?id=487424073&p=2
https://你的域名/player/?url=163cn.tv/xxxx
```

歌单默认播放 `p=1`。网易云网页常见的 `#/song` 与 `#/playlist` 是浏览器片段，不能直接发送到服务器，请改用无 `#` 的地址，或对内层 URL 完整编码。

## 接口

| 路径 | 作用 |
| --- | --- |
| `GET /player/?url=...` | 统一播放与弹幕入口 |
| `GET /player/?__dm=1&url=...` | 强制返回弹幕 |
| `GET /api/danmaku?url=...` | 直接获取弹幕 |
| `GET /api/resolve?url=...` | 获取解析诊断信息 |
| `GET /api/cache/stats` | 查看缓存与命中统计 |
| `GET /health` | Docker 健康检查 |

旧接口 `/api/current` 与 `/api/set` 已彻底移除，并返回 `410 Gone`。不要再使用 `room=main`。

## 环境变量

| 变量 | 默认值 | 说明 |
| --- | ---: | --- |
| `PORT` | `3000` | 容器内 HTTP 端口 |
| `VIEW_CACHE_TTL_SECONDS` | `1800` | B 站视频信息缓存时间 |
| `VIDEO_URL_CACHE_TTL_SECONDS` | `600` | B 站媒体直链缓存时间 |
| `DANMAKU_CACHE_TTL_SECONDS` | `21600` | B 站弹幕缓存时间 |
| `NETEASE_PLAYLIST_CACHE_TTL_SECONDS` | `1800` | 网易云歌单缓存时间 |
| `NETEASE_URL_CACHE_TTL_SECONDS` | `600` | 网易云媒体直链缓存时间 |
| `NETEASE_LEVEL` | `standard` | 网易云默认音质 |
| `ZNNU_BASE_URL` | `https://music.znnu.com` | 网易云解析后端 |
| `ZNNU_FETCH_TIMEOUT_SECONDS` | `30` | 网易云接口超时秒数 |
| `BILI_COOKIE` / `BILIBILI_COOKIE` | 空 | 可选 B 站 Cookie |
| `BILI_USER_AGENT` | 浏览器 UA | 可选上游 User-Agent |

不要把真实 Cookie 直接写入 `docker-compose.yml` 后提交。建议使用服务器本地 `.env` 文件，并限制文件权限。

## Nginx 反向代理

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

随后为域名启用 HTTPS。使用 Cloudflare 时，可以将 DNS 指向服务器，并确保源站证书与 Cloudflare SSL 模式配置正确。

## 缓存与并发

缓存以视频/分 P/音质等解析参数为键，不使用全局 `room` 状态。不同世界播放不同视频时互不覆盖；相同资源并发请求会共享正在进行的上游任务，完成后从内存缓存读取。

缓存位于内存中，容器重启后会清空，但不影响接口正确性，只会让首次请求重新访问上游服务。

## 本地开发

如需脱离 Docker 运行：

```bash
npm run check
npm start
```

需要 Node.js 20 或更高版本。

## 注意事项

本服务不存储或托管哔哩哔哩、网易云音乐的媒体文件，只进行 URL 解析、元数据请求、弹幕格式转换与重定向。网易云解析依赖 [music.znnu.com](https://music.znnu.com/)；第三方接口、CDN、地区限制和访问策略发生变化时，解析结果也可能受到影响。请遵守相关服务条款和当地法律。
