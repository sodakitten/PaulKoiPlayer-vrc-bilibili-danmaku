# 1.11-frontend-fix-alpha1.0：前端测试版

这是附加在 [v1.11 Release](https://github.com/sodakitten/PaulKoiPlayer-vrc-bilibili-danmaku/releases/tag/v1.11) 上的**测试包**，没有替代原正式 1.11。尚未完成完整 Unity/C# 工程编译、UdonSharp 编译或 VRChat 实机验收。兼容参考固定为 YamaPlayer 1.5.18，仅用于 PC 适配；后端继续使用正式 1.11，无需重新部署。

## 下载与校验

- [普通黑白测试包](https://github.com/sodakitten/PaulKoiPlayer-vrc-bilibili-danmaku/releases/download/v1.11/1.11-frontend-fix-alpha1.0.zip)：3,052,026 字节；SHA-256 `39A9A2F4E2606DC8D337B17C54393430F296E43D9345969C2C8BC77591F42D4E`。
- [黑金测试包](https://github.com/sodakitten/PaulKoiPlayer-vrc-bilibili-danmaku/releases/download/v1.11/1.11-frontend-fix-alpha1.0-black-gold.zip)：3,052,059 字节；SHA-256 `0CA01ACEEDCBDFC54DE6076F7E6AB2E03CD6529068F6708ADB6E35EC9401AFF8`。

这两个 ZIP 是已制作测试包的原样上传，根目录 README 中“未提交、推送或发布”描述的是打包时状态；本页说明其后获授权公开为测试版。原正式 ZIP、普通/黑金 UnityPackage、后端 ZIP 和 v1.11 标签不改动，不把整个原正式 Release 改成预发布。

## 改动范围

相对各自正式基线仅修改：

1. `Runtime/YamaBiliPagesPlaylist3.cs`：manifest / 标题回调验证当前请求、用途、相关播放与所有权；完成、取消、超时后丢弃旧回调，避免重新展开或重复入队。主动排队不因正常换片而取消，同一同步 manifest 的选中项变化不反复重启下载。
2. `Runtime/YamaBiliDanmakuModule3.cs`：弹幕/歌词的成功和失败回调验证请求代次、播放 URL 和音频模式。识别当前播放优先使用 1.5.18 的 Track URL，保留 Stopped + IsLoading 的加载过渡。
3. `Editor/YamaBiliDanmakuRigBuilder3.cs`：应用皮肤/重连引用不重置已有后端、URL 前缀、vcrid 目录、播放和轮询设置，默认值只用于新建组件。

原有皮肤、播放列表视觉、Shader、材质、镜子、弹幕解析和歌词时间轴保留。没有混入字幕 alpha、beta14.03 多人同步恢复、Tablet、iwaSync3 或 VizVid 的未发布工作；这不是已验证的 Udon halt 或远端完全无反应问题修复。

每个组件最多一个 SDK 文本下载在途。逻辑取消/业务超时后仍需排空旧 SDK 回调，再发最新待处理请求，以防 A → B → A 同 URL 竞争；新请求可能等待旧下载结束。SDK 完全不回调或 Behaviour 已 Halt 时不能承诺恢复。

## 安装与回退

1. 备份 Unity 工程，只选择一种皮肤包。必须已安装对应的正式 1.11 和 YamaPlayer 1.5.18。
2. 在现有目录只覆盖上述三个同名 `.cs`，保留 `.meta`、GUID、组件引用、UdonSharp Program Asset。不要删除重建组件，不要把两个皮肤的同名类并排导入。
3. 等待导入并全量重新编译 UdonSharp，确认 Console 无 C# / UdonSharp 错误后再 Build & Test。无需重新生成 UI，后端不改。
4. 回退时还原备份的三个源码文件，保留原 `.meta`，再次编译；不能上传旧的编译产物。

ZIP 是源码包，不是本机 Unity 编译或导出的 UnityPackage。已有正式 UnityPackage 附件不会因为源码更新自动获得本次修复。

## 源码对应与复核

主分支的三个前端文件对应普通黑白测试包。黑金差异仅为正式黑金视觉；在仓库的独立副本根目录运行 `git apply docs/variants/black-gold-ui.patch` 可得到黑金测试源码。该补丁不增加第二套同名 `.cs`；不要在含其他实验改动的工作树直接套用。稳定源码请取原 v1.11 正式附件，而非 main 的自动源码下载。

测试脚本：[tests/Verify-FrontendFix.ps1](../tests/Verify-FrontendFix.ps1)。需要 PowerShell 7 及其 Roslyn 程序集，传入一个已准备好的工作目录：

```text
<workspace>/normal/YamaBiliDanmakuV3/           普通测试 ZIP 内的目录
<workspace>/gold/YamaBiliDanmakuV3/             黑金测试 ZIP 内的目录
<workspace>/baseline/normal/YamaBiliDanmakuV3/  原正式 1.11 ZIP 内的目录
<workspace>/baseline/gold/YamaBiliDanmakuV3/    原普通基线共同文件 + 原正式黑金 UnityPackage 的 Editor/Pages
```

```powershell
./tests/Verify-FrontendFix.ps1 -WorkspaceRoot '<workspace>' -Variant normal
./tests/Verify-FrontendFix.ps1 -WorkspaceRoot '<workspace>' -Variant gold
```

已完成普通版 730 项、黑金版 728 项源码/不变项检查及两版各 43 项实际请求方法断言；ZIP 文件列表、每个条目哈希和两版视觉差异均核对。方法测试中 Unity/SDK I/O、解析和 UI 使用桩；**这不是完整工程或 Udon 编译，更不是多人/VR 实测**。

待实机测试：解析期间换片、清空、换 Owner，重复同链接，慢请求超时后晚到，正常排队，B 站多 P 后续队列，网易云单曲/整歌单与歌词，Stop/End 防重，晚加入，PC/VR 与镜子，以及应用皮肤后自定义设置保留。
