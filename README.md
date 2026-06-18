# VRChat 哔哩哔哩弹幕组件

[简体中文](README.md) | [English](README_EN.md)

为 VRChat 世界视频播放器提供哔哩哔哩弹幕加载、同步与渲染功能。

当前稳定版为 **v5.0.0**，目前已适配并测试 **YamaPlayer**。项目采用通用名称，后续计划适配更多 VRChat 世界播放器。

> 本项目不是 VRChat、哔哩哔哩或 YamaPlayer 的官方组件。

## 功能

- 根据播放器当前 URL 自动请求对应弹幕
- 支持滚动、顶部和底部弹幕
- 跟随视频播放进度同步，正确处理暂停和继续播放
- 使用 `RectMask2D` 在播放器画面边缘裁切弹幕
- 支持彩色弹幕、黑色描边和轻微加粗
- 可调整字体缩放、透明度、轨道数、速度和时间偏移
- 仅更新正在显示的弹幕，降低每帧遍历开销
- URL 输入框可预填自定义解析服务前缀
- 提供弹幕开启、关闭和切换事件，方便连接世界内自定义 UI

## 环境要求

- Unity 与当前 VRChat Worlds SDK 兼容的版本
- VRChat Worlds SDK（包含 UdonSharp）
- TextMeshPro
- YamaPlayer（当前 v5.0.0 适配器所需）
- 一个能够返回 `#YBDM/1` 文本弹幕的解析服务

默认解析服务前缀：

```text
https://danmaku.paulkoishi.com/player/?url=
```

## 安装

1. 下载 [v5.0.0 Release](https://github.com/sodakitten/vrc-bilibili-danmaku/releases/tag/v5.0.0)。
2. 删除旧版目录：
   - `Assets/YamaBiliDanmaku`
   - `Assets/YamaBiliDanmakuV2`
   - `Packages/yama-bili-danmaku*`
3. 将 `YamaBiliDanmakuV3` 文件夹放入 Unity 项目的 `Assets/`。
4. 执行 `Assets > Reimport All`。
5. 执行 `Yamadev > YamaPlayer > Fix Bili Danmaku U# Program Asset`。
6. 执行 `UdonSharp > Compile All UdonSharp Programs`。
7. 选中目标 YamaPlayer，执行 `Yamadev > YamaPlayer > Create Bili Danmaku Module`。

## 手动绑定 URL 输入框

预输入前缀组件的两个 URL 输入框必须在 Inspector 中**手动拖入**。不要依赖自动查找或组件顺序猜测，不同 YamaPlayer 版本或自定义预制体的层级可能不同。

| Inspector 字段 | YamaPlayer 默认对象路径 |
| --- | --- |
| `Top Url Input Field` | `ScreenUI/Canvas/Control/Main/Top/UrlInput` |
| `Bottom Url Input Field` | `ScreenUI/Canvas/Control/Main/LeftSide/Container/UrlInput` |

两个对象都应拖入其自身的 `VRC URL Input Field` 组件。

前缀设置：

- `Enable Url Prefix On Input`：控制预输入功能，默认开启。
- `Url Prefix`：自定义解析服务地址。
- `Keep Prefix When Empty`：持续补回空输入框；若希望玩家可以手动删除前缀，请保持关闭。

## 播放视频

玩家在 YamaPlayer 输入框中填写：

```text
https://danmaku.paulkoishi.com/player/?url=<哔哩哔哩视频链接>
```

同一 URL 由视频播放器请求时返回视频解析结果，由 `VRCStringDownloader` 请求时返回弹幕文本，因此不需要 `room` 或世界实例标识。

## 常用设置

| 设置 | 默认值 | 说明 |
| --- | ---: | --- |
| `Lane Count` | 12 | 弹幕轨道数量 |
| `Scroll Duration` | 8 | 滚动弹幕通过画面的秒数 |
| `Static Duration` | 4 | 顶部/底部弹幕停留秒数 |
| `Time Offset Ms` | 0 | 弹幕时间校正 |
| `Max Danmaku Lines` | 1600 | 单次加载的最大弹幕条数 |
| `Font Scale` | 1.1 | 字体显示缩放 |
| `Text Alpha` | 0.72 | 字体透明度 |
| `Outline Width` | 0.11 | TMP 黑色描边宽度 |
| `Outline Alpha` | 0.7 | 描边透明度 |

修改现有组件的描边或粗体设置后，选中组件并执行：

```text
Yamadev > YamaPlayer > Apply Selected Bili Danmaku Visual Style
```

## 自定义弹幕开关

组件不生成固定样式的开关。可以让世界中的按钮调用以下公开事件：

```text
ToggleDanmaku
EnableDanmaku
DisableDanmaku
```

## 常见问题

### 找不到有效的 U# Program Asset

依次执行：

```text
Yamadev > YamaPlayer > Fix Bili Danmaku U# Program Asset
UdonSharp > Compile All UdonSharp Programs
```

如果仍然失败，在 `Assets/YamaBiliDanmakuV3/Runtime` 中手动创建 U# Script，保留生成的 `.asset`，并将其 Source C# Script 指向 `YamaBiliDanmakuModule3.cs`。

### 显示 Loaded 但没有弹幕

- 确认播放器当前 URL 使用支持弹幕响应的解析服务。
- 确认 `Controller`、`Lane Root` 和 `Text Pool` 已正确绑定。
- 确认 `Danmaku Enabled` 已开启。
- 不要混用旧版组件和旧版 U# Program Asset。

## 当前版本说明

v5.0.0 是当前稳定基线，包含彩色弹幕 TMP 描边、轻微加粗、URL 前缀辅助、活动弹幕索引优化，以及暂停后继续播放时的计时补偿。
