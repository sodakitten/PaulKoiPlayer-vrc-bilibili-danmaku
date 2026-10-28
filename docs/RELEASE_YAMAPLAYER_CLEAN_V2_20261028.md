# YamaPlayer 纯净版：默认 V2（2026-10-28）

维护者已确认测试并授权发布。本次上传的是同一份已测试的源码 ZIP，不重新打包，不替换历史 v1.11 / v1.2 的附件或标签。

## 下载与范围

- 资产：`PaulKoiPlayer-YamaPlayer-Clean-V2-Default-20261028.zip`。
- 仅支持 YamaPlayer PC / 桌面端，兼容参考为 YamaPlayer 1.5.18；不是 Unity 编辑器导出的 `.unitypackage`。
- 仅包含插件的 Runtime、Editor、Shader、字体与说明，不包含后端、iwaSync3、VizVid、Tablet 或 ProTV 适配器。
- 不更改后端代码、Docker、数据库、生产配置或服务器 V2 开关；后端 v1.2 Release 保持原样。

## 主要变化

- 新建插件默认使用 `__pkdm=2`，输入、清单和预置 VCRID 目录采用一致协议。
- 取消前 4096 条总量截断，分帧解析完整弹幕时间轴，保留同一时间戳的多条弹幕。
- 支持 metadata / complete / error 状态，元数据与完整弹幕分开处理，通过已有预置 VCRID 获取后续弹幕。
- 失败按服务端冷却提示有界重试；保留可用分 P 清单，不把 metadata 当作成功的零条弹幕。
- 加强换片、清空、停止及异步回调隔离；保留原播放时钟、渲染和网易云歌词路径。

## 安装与协议

备份工程后覆盖原插件目录，保留 `.meta`、GUID 和场景组件，不并排导入同名类；重新编译 UdonSharp 并 Build & Test。既有自定义皮肤需单独合并，不属于本次已测试包。

已有世界的序列化前缀不会被覆盖脚本强行改写。选择包含一个播放列表的播放器根对象，执行 `Tools > PaulKoiPlayer > Backend > Enable Metadata V2 on Selected Player`，再重新构建世界。新建插件无需此步骤。

默认输入前缀是 `https://danmaku.paulkoishi.com/player/?__pkdm=2&url=`，VCRID 前缀是 `https://danmaku.paulkoishi.com/player/?__pkdm=2&vcrid=`。媒体请求仍走 302；不要在媒体播放地址添加 `__dm=1`。

真正的 B 站元数据分离响应需要后端支持并启用 `BILIBILI_METADATA_V2_ENABLED`。前端默认 V2 不会自动修改服务器开关；关闭时继续接受旧完整响应。本次发布没有部署或修改服务器。

详细安装、回退与验收：[前后端同步说明](https://github.com/sodakitten/PaulKoiPlayer-vrc-bilibili-danmaku/blob/yamaplayer-clean-v2-20261028/docs/FRONTEND_BACKEND_SYNC_20261028.md)。

## 验证与完整性

维护者反馈已测试；发布前另行通过源码语法、12 项编辑器前缀检查、默认 V2 与旧配置保护检查，以及 5865 条弹幕和 130 条列表实际方法断言。方法测试使用 SDK 桩，不等于独立执行了完整 Unity/UdonSharp 或 VRChat 多客户端验收。

ZIP 的 10 个插件源码/资源文件与发布源码逐文件哈希一致，连同 README 和验证摘要共 12 个文件。包内“本地测试 / 未发布”的说明是打包时记录，为保持已测试文件原样而保留，以本发布说明为准。

文件大小：`3061040` 字节。

SHA-256：

```text
B323901DE7082C0FFEF41353B4933992389236C5690F5C3467801C23F2E0713E
```
