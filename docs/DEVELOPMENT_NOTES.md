# 开发试错与演进记录

本文记录 VRChat 哔哩哔哩弹幕组件从最初原型到 v1.0.0 稳定版之间的重要错误、失败方案、修复方式和设计取舍。它不是完整提交日志，而是为后续维护保留的工程笔记。

## 当前稳定基线

- Unity 组件：`Runtime/YamaBiliDanmakuModule3.cs`
- URL 前缀辅助：`Runtime/YamaBiliUrlPrefixHelper3.cs`
- 编辑器生成器：`Editor/YamaBiliDanmakuRigBuilder3.cs`
- 镜子可读 TMP Shader：`Shaders/YamaBiliDanmakuTMPMirrorReadable.shader`
- Docker 服务：`server/src/server.js`
- 公开版本：`1.01`

后续功能应从这个基线继续，不应从早期试验包或旧版 Package 目录回拷代码。

## 1. 从房间状态转向无状态 URL

早期服务端使用类似以下接口保存“当前视频”：

```text
/api/current?room=main
/api/set?room=main
```

这个方案在单个测试世界中可用，但不同世界或多个实例共用 `main` 时会互相覆盖。仅增加世界名也不能自然区分同一世界的多个实例。

最终改为统一入口：

```text
/player/?url=<原始链接>
```

- 视频播放器请求时返回媒体直链的 `302`。
- `VRCStringDownloader` 请求时返回 `#YBDM/1` 弹幕文本。
- `__dm=1` 可强制进入弹幕模式，用于诊断。
- 请求以 URL、分 P、音质等参数作为缓存键，不再保存全局房间状态。

结果是不同世界、不同实例和不同视频可以并发工作；相同资源的并发请求还可以共享正在进行的上游任务和缓存结果。

## 2. Unity Package 与程序集引用问题

最早尝试使用 Package/asmdef 形式集成，连续出现以下类型的错误：

```text
CS0012: UdonSharpBehaviour is defined in an assembly that is not referenced
CS0012: ISupportsPrefabSerialization ... VRC.Udon.Serialization.OdinSerializer
CS0246: YamaPlayerModule / YamaPlayerModuleDefinition could not be found
```

原因包括：

- VRChat Worlds SDK、UdonSharp、OdinSerializer 与第三方播放器程序集的引用边界不同。
- YamaPlayer 新旧版本公开类型发生变化，旧类型名不能继续假设存在。
- Editor asmdef 和 Runtime asmdef 对 UdonSharp 类型的可见性并不等同于普通 Unity 脚本。

稳定方案是将组件直接放入：

```text
Assets/YamaBiliDanmakuV3/
```

并避免为这一版增加自定义 asmdef。当前适配直接使用实际存在的 `Yamadev.YamaStream.Controller`，而不是猜测 `YamaPlayerModule` 一类旧 API。

## 3. 编辑器 API 与类型错误

开发生成器时出现过：

```text
CS0104: Object is an ambiguous reference between UnityEngine.Object and object
CS0029: Cannot implicitly convert type Yamadev.YamaStream.Track to object[]
```

修复原则：

- Unity 编辑器对象明确写为 `UnityEngine.Object`。
- 不通过猜测把播放器内部类型强制转换为 `object[]`。
- 先检查当前安装版本的真实类型和序列化字段，再写适配代码。
- 对第三方播放器只绑定所需的最小公开接口，减少版本升级影响。

## 4. UdonSharp Program Asset

仅让 C# 脚本通过 Unity 编译还不够。UdonSharpBehaviour 必须关联有效的 U# Program Asset，否则会出现：

```text
Unable to find valid U# program asset associated with script
NullReferenceException in RunBehaviourSetup
```

因此增加了编辑器命令：

```text
Yamadev > YamaPlayer > Fix Bili Danmaku U# Program Asset
```

正确安装顺序是先修复 Program Asset，再执行：

```text
UdonSharp > Compile All UdonSharp Programs
```

然后创建弹幕模块。生成器也保留了手动创建 U# Script 并指定 Source C# Script 的兜底说明。

## 5. “Loaded 但没有弹幕”回归

这是开发过程中最需要警惕的回归：下载和解析已经完成，状态显示 `Loaded`，但屏幕上没有任何文字。

曾经叠加尝试过的高风险改动包括：

- 使用当前 SDK 中不存在的 `YamaPlayerListener`。
- 调用 `Controller.MirrorFlip`，但当前 Controller 没有这个成员。
- 在 Udon 运行时调用部分未暴露的 TextMeshPro API。
- 为描边复制第二套 TextMeshPro 文字对象。
- 同时改动镜像、描边、粗体、前缀和渲染路径。

这些改动有的直接编译失败，有的在编辑器中看似正常，却导致上传世界后只加载不渲染。

最终处理方式不是继续叠补丁，而是回到用户确认可用的第三版渲染路径，只把功能逐项重新加入并逐项验证。由此形成当前稳定基线。

以后若再次出现 `Loaded` 但无弹幕，应按以下顺序检查：

1. 保持解析和时间轴不变，确认 Text Pool、Lane Root 与 Controller 引用。
2. 禁用最近新增的视觉或镜像逻辑。
3. 检查是否调用了 UdonSharp 未暴露的 TMP/Unity API。
4. 与 v1.0.0 的 `ShowLine`、活动池和位置更新路径做最小差异比较。
5. 不要在未定位原因时同时重写下载、解析和渲染。

## 6. 屏幕边缘裁切与性能

早期滚动弹幕会超出播放器边缘。稳定版在 `Danmaku Lanes` 上使用 `RectMask2D`，让文字在画面边缘消失，行为更接近普通视频弹幕播放器。

性能方面，最初每帧扫描整个文字池。现在维护 `_activePoolIndexes`，每帧只更新当前正在显示的弹幕，回收时通过紧凑数组移除索引。

保留的原则：

- Text Pool 继续复用对象，不在播放过程中频繁创建和销毁 GameObject。
- 不用第二套文字池模拟描边。
- 帧率和时间同步逻辑不因性能优化而改变。

## 7. 描边、彩色弹幕与轻微加粗

最初使用 Unity UI `Outline`，实际视觉效果不稳定，部分情况下几乎看不到描边。随后改为 TextMeshPro 材质路径：

- 启用 `OUTLINE_ON`。
- 设置 `_OutlineWidth` 和 `_OutlineColor`。
- 开启 `extraPadding` 并更新 Mesh Padding。

之后发现白色普通弹幕有描边，但红色等彩色弹幕对比不足。最终在同一个 TMP 材质中启用 `UNDERLAY_ON`，使用零偏移黑色 Underlay 增强彩色文字边缘，而不是复制额外文字对象。

当前默认视觉参数：

```text
Text Alpha       0.72
Outline Width    0.11
Outline Alpha    0.70
Face Dilate      0.012
Weight Bold      0.28
Font Scale       1.10
```

粗体不能只依赖 TMP 默认 Bold，因为默认权重过重。当前使用较低 `_WeightBold` 和轻微 `_FaceDilate`，目标是“略粗”，不是黑体块状效果。

这些值由 Unity 编辑器写入 TMP 材质。修改已有模块后必须执行：

```text
Yamadev > YamaPlayer > Apply Selected Bili Danmaku Visual Style
```

只修改 Inspector 数值不会自动更新已有文字对象使用的材质。

## 8. 暂停后弹幕跳动

早期位置计算使用 `Time.time - startTime`。播放器暂停时 `Time.time` 仍继续，因此恢复播放后，弹幕会瞬间跳到“现实时间已经过去”的位置。

稳定版在暂停时记录 `_pauseStartedAt`，恢复时把暂停时长补到：

- 所有活动弹幕的 `_activeStartTime`
- 各轨道的 `_laneReadyAt`

这样恢复播放后弹幕从暂停位置继续，不会闪到后一秒或更远的位置。

## 9. URL 前缀辅助与输入框绑定

前缀辅助的目标是在玩家打开 URL 输入框时预填：

```text
https://danmaku.paulkoishi.com/player/?url=
```

它后来增加了“输入框由非空变为空时重新补回前缀”的检测，以覆盖播放器播放后清空输入框的行为。

曾尝试通过层级、序列化字段和组件顺序自动寻找 Top/Bottom URL 输入框。但不同 YamaPlayer 版本和自定义 Prefab 的层级可能不同，按组件顺序猜测容易接反或完全找不到。

因此当前维护规则是：

- `Top Url Input Field` 和 `Bottom Url Input Field` 由世界作者在 Inspector 手动拖入。
- 后续不要增强“自动猜测”；宁可显式配置，也不要静默连错。
- 玩家仍可手动删除前缀；`Keep Prefix When Empty` 默认关闭。

## 10. v1.01 世界内互动控制

v1.01 在 Unity 组件端新增了一个轻量的世界内控制面板：

```text
Bili Danmaku Module
└── Danmaku Controls Canvas
    ├── BG
    ├── Display Area Button
    └── Danmaku Toggle Button
```

该面板使用 World Space Canvas、`GraphicRaycaster`、`VRC_UiShape` 和 `BoxCollider`。两个控件都使用普通 `Button`，不使用勾选框：

- `Display Area Button`：循环切换 `Danmaku: Full`、`Danmaku: Half`、`Danmaku: 1/4`。
- `Danmaku Toggle Button`：切换 `Danmaku: On` 与 `Danmaku: Off`。

切换显示区域时，不再清空已经发射的弹幕。旧弹幕继续按发射时的位置和动画走完，后续新弹幕才按新的显示区域分配轨道。这个行为由 `SetDisplayAreaMode` 只更新 `_displayAreaMode`、按钮文字和轨道计时器实现，不调用 `HideAllTexts()`。

以下内容仍只能由世界作者在 Unity Inspector 中调整：

- 字体缩放、透明度、粗体和描边
- 轨道数、滚动时长、静态停留时长
- 时间偏移和最大加载条数
- URL 前缀配置

组件仍公开这些 Udon 事件，供自定义世界 UI 或其他脚本调用：

```text
ToggleDanmaku
EnableDanmaku
DisableDanmaku
SetFullScreenDanmaku
SetHalfScreenDanmaku
SetQuarterScreenDanmaku
CycleDisplayAreaMode
```

互动功能必须建立在当前稳定渲染路径上，不能为了增加 UI 再次改写核心下载、解析、同步和渲染流程。

## 11. v1.01 试错复盘

本轮从 v1.0.0 到 v1.01 的开发中，多次出现“看起来快好了，但实际在 Unity 或 VRChat 里坏掉”的情况。以下问题需要在后续维护中明确避免。

### 镜子可读文字

最初尝试通过场景对象或播放控制器侧翻转文字，但这容易影响普通视角，且会碰到 YamaPlayer API 差异。当前稳定方案是 TMP shader 内使用 VRChat shader global：

```hlsl
uniform float _VRChatMirrorMode;
```

镜子渲染时预翻转顶点位置，普通视角保持不变。材质上保留 `_MirrorFlip` 开关，并继续设置 TMP outline、underlay、轻微加粗参数。不要调用当前 YamaPlayer 中不存在的 `Controller.MirrorFlip`。

### 粉色材质与 TMP shader

粉色材质说明 shader 缺失或材质属性不兼容。v1.01 的生成器会把弹幕 TMP 材质切到 `YamaBiliDanmaku/TMP Mirror Readable`，并从现有 TMP 材质复制 atlas 与 outline 相关属性。不要为了镜子可读直接替换成非 TMP shader，也不要丢失 font atlas、outline、underlay 或 weight 参数。

### UI 射线点击

一开始我把按钮做成独立小 Canvas 或挂在根 Canvas 下面，导致射线命中链路不稳定。后来又只补 `VRC_UiShape` 类型名，仍然不足。真正需要同时满足：

- 独立 World Space Canvas。
- Canvas 物体上有 `GraphicRaycaster`、`VRC_UiShape`、`BoxCollider`。
- Canvas 不在 Unity 的 `UI` layer。
- BoxCollider 覆盖按钮区域，并留有足够厚度。
- Label 也开启 `raycastTarget`，保证点文字时能命中 UI。

参考过 `VRCPlayersOnlyMirrorCutout` 的 `Menu / MirrorToggle / Label` 结构，最终保留其 World Space Canvas、`VRC_UiShape`、BoxCollider 与 Supersampled UI 材质思路，但没有复刻勾选框视觉，因为本组件两个控件都应该以文字显示当前状态。

### Button 与 Toggle 语义

弹幕开关最初做成 Toggle，显示区域也一度做成 Toggle。这样视觉上会出现“填空/勾选框”，但显示区域是三态循环，不是布尔开关；弹幕开关虽然是布尔值，但用户希望两个控件都只显示当前状态。因此 v1.01 统一为普通 Button：

- `Button.onClick -> CycleDisplayAreaMode`
- `Button.onClick -> ToggleDanmaku`

如果以后重新引入 Toggle，需要明确它只表达布尔状态，不能拿来表达三态循环。

### UdonSharp 事件绑定

最关键的交互问题不是按钮外观，而是事件绑定目标。我曾把 UnityEvent 直接绑定到 UdonSharp C# proxy：

```text
UdonSharpBehaviour.SendCustomEvent
```

这在 Inspector 中看起来像绑定成功，但 VRChat 运行时可能不触发。YamaPlayer 自己的构建流程使用的是：

```text
UdonSharpEditorUtility.GetBackingUdonBehaviour(...)
```

因此 v1.01 生成器也改为把 `Button.onClick` 绑定到 backing `UdonBehaviour.SendCustomEvent`。后续新增 UnityEvent 绑定必须沿用这个方式。

### 显示区域切换

早期切换 `Full/Half/1/4` 时调用了 `HideAllTexts()`，导致屏幕上已有弹幕瞬间消失。正确行为是只影响后续弹幕：

- 已发射弹幕继续使用发射时保存的 `_activeY` 和运动时间。
- 新发射弹幕在 `ShowLine()` 中读取最新 `_displayAreaMode`。
- 切换时只 `ClearLaneTimers()`，让新区域轨道可立即重新分配。

### 发布纪律

本轮多次临时 beta 包验证了以下纪律的重要性：

- beta 包只包含 `YamaBiliDanmakuV3/`，不混入 README 或 docs。
- 稳定包才包含 README/docs，并按真实版本号命名。
- 用户确认前不提交、不 push、不创建 GitHub release。
- 可用 beta 基线需要明确记录；本轮 `beta9.3` 是交互可用基线，`beta9.4` 在其上修正显示区域切换不清屏。

## 12. 服务端依赖与缓存

服务端使用 Node.js 20 Docker 镜像，宿主机默认映射 `7858 -> 3000`。B 站信息、媒体直链和弹幕分别缓存；相同键的并发请求通过 inflight 合并，避免缓存未建立时重复请求上游。

网易云音乐解析依赖第三方服务 [https://music.znnu.com/](https://music.znnu.com/)。该依赖不是本项目控制的服务，接口变化、限流、地区限制或不可用都会影响网易云功能。

真实 `BILI_COOKIE`、Token 和 `.env` 不得提交到仓库。

## 后续修改检查表

每次发布前至少确认：

1. Unity 与 UdonSharp 编译无错误。
2. U# Program Asset 与两个 Runtime 脚本正确关联。
3. 普通、彩色、顶部、底部和滚动弹幕均可见。
4. `Loaded` 后实际有文字进入 Text Pool 并显示。
5. 暂停、继续、拖动进度和换视频后时间同步正确。
6. 弹幕在画面边缘被裁切。
7. 修改视觉参数后执行 Apply 菜单并检查材质。
8. Top/Bottom URL 输入框手动绑定正确。
9. Docker `/health`、`/player/` 和强制 `__dm=1` 正常。
10. 如果服务端没有变化，Unity 组件 release 只发布同版本 Unity ZIP，并在 release notes 中说明继续使用 v1.0.0 server。

## 不应重新引入的方案

- 不使用不存在的 `YamaPlayerListener` 或 `Controller.MirrorFlip`。
- 不假设旧版 `YamaPlayerModule` 类型仍存在。
- 不为描边复制整套 TextMeshPro 对象。
- 不在 Udon 运行时调用未经确认可暴露的 TMP API。
- 不把 UnityEvent 直接绑定到 UdonSharp C# proxy；需要绑定 backing `UdonBehaviour`。
- 不用 Toggle 表达 `Full/Half/1/4` 这种三态循环状态。
- 不在切换显示区域时调用 `HideAllTexts()` 清空已有弹幕。
- 不使用全局 `room=main` 保存当前视频。
- 不通过组件顺序自动猜测 URL 输入框。
- 不在一次修改中同时替换下载、解析、同步和渲染四条路径。

这份记录应随架构变化继续更新。新功能不仅要记录“做了什么”，也要记录它保护了哪条稳定路径，以及验证过哪些失败场景。
