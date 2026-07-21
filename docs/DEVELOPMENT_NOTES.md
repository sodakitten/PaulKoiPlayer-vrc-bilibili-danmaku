# 开发试错与演进记录

本文记录 VRChat 哔哩哔哩弹幕组件从最初原型到 v1.0.0 稳定版之间的重要错误、失败方案、修复方式和设计取舍。它不是完整提交日志，而是为后续维护保留的工程笔记。

## 当前稳定基线

- Unity 组件：`Runtime/YamaBiliDanmakuModule3.cs`
- URL 前缀辅助：`Runtime/YamaBiliUrlPrefixHelper3.cs`
- 编辑器生成器：`Editor/YamaBiliDanmakuRigBuilder3.cs`
- 镜子可读 TMP Shader：`Shaders/YamaBiliDanmakuTMPMirrorReadable.shader`
- Docker 服务：`server/src/server.js`
- YamaPlayer PC / 桌面端当前正式稳定版：`1.11`（由 `alpha1.1` 原样晋升并发布到 GitHub）。1.11 Release 只包含 YamaPlayer PC / 桌面端适配包；iwaSync3 与 VizVid 保持当前公开版本。Android / Quest 普通播放器继续使用 `1.03`；Android / Quest 可拾取平板使用独立 `YamaBiliDanmakuTabletV3` 包
- YamaPlayer 上游兼容参考包固定为 `E:\paulkoiplayer\net.kwxxw.yama-stream-1.5.18.zip`，包内版本为 `1.5.18`，SHA-256 为 `EE0C5DF182180A99839483DDA45CCE180046DA9A12DB9747897108F56096A2BC`。以后判断 Controller、Queue、Track、Listener、Canvas、Screen、材质或 prefab 结构时必须以这个 ZIP 的实际内容为准；不得再用 `E:\paulkoiplayer\YamaPlayer` 中的 `2.0.0-beta.6` 源码推断 1.5.18 行为，也不得仅按网上最新版或旧教程猜 API。

后续 YamaPlayer 普通播放器功能应从本地正式 1.11 这个 PC 主线基线继续；当前正式包已由 alpha1.1 原样晋升，存放于 `E:\paulkoiplayer\1.11.zip` 和 `E:\paulkoiplayer\PaulKoiPlayer-YamaBiliDanmakuV3-1.11.zip`，不应从更早试验包或旧版 Package 目录回拷代码。金黑视觉分支必须从 `E:\paulkoiplayer\beta14.02.zip` 继续，不能直接拿当前黑白主线 Editor/Pages 覆盖，否则会丢失正式 1.10 的金色 UI 和金色列表视觉。Android / Quest 普通播放器不要套用 PC 端外部显示面挂载逻辑，继续保留 1.03；Android / Quest 平板跟随问题不要继续塞回普通 `YamaBiliDanmakuV3`，应在独立 Tablet 包内处理。

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

早期曾在没有固定上游版本和没有核对 prefab 的情况下，通过宽泛层级、序列化字段或组件顺序寻找 Top/Bottom URL 输入框。不同 YamaPlayer 版本和自定义 Prefab 的结构可能不同，按组件顺序或模糊名称猜测容易接反、误连组件自己的 Queue 输入框或完全找不到。

早期 1.10 以及由 beta13.56 首次晋升的旧 1.11 采用手动绑定。`alpha1.0` 针对后来固定并逐项核对过的 YamaPlayer 1.5.18 试验精确自动补齐；alpha1.1 已原样晋升为当前正式 1.11，因此当前正式版也使用以下规则：

- 优先读取 1.5.18 `UIController._urlInputFieldTop` 与 `_urlInputField` 的权威序列化引用；代理不可读时只接受两条已经验证的精确 ScreenUI 路径。
- 只补空字段，已有 `Top Url Input Field` / `Bottom Url Input Field` 手动绑定不被覆盖；无法精确匹配的自定义布局继续由世界作者在 Inspector 拖入。
- 不得把该例外扩展成按组件顺序、宽泛对象名或全场景第一个输入框“自动猜测”；宁可显式配置，也不要静默连错。
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

## 12. v1.02 高密度弹幕与轻量性能优化

v1.02 从 `beta10.1` 晋升。它没有改下载、解析、播放同步和 TMP 视觉主链路，主要解决高密度弹幕视频只显示开头一段的问题，并补了几处低风险运行时优化。

### 高密度弹幕上限

测试视频 `BV1xx411c7Xg` 的服务端 `#YBDM/1` 返回约 3600 条弹幕，但 v1.01 Unity 端默认 `_maxDanmakuLines = 1600`。这个视频前 1600 条弹幕集中在开头十几秒内，因此 Unity 端看起来像“后面没有弹幕”。

v1.02 将默认上限提高到 4096：

- Runtime 默认值：`_maxDanmakuLines = 4096`。
- 编辑器生成器 `WireModule()` 也会把现有模块写回 4096。
- 对象池仍保持原有规模，不增加同时显示的 TMP 文本数量。

后续不要把“加载上限”与“同时显示数量”混为一谈。提高 `_maxDanmakuLines` 主要增加加载后保存的弹幕数组容量，不等于屏幕上同时渲染更多 TextMeshPro 对象。

### 低风险性能小修

v1.02 还保留了 `beta10.0` 的几处小优化：

- 弹幕关闭后，`Update()` 不再每帧重复调用 `HideAllTexts()`。
- `HideAllTexts()` 只处理 `_activePoolIndexes` 中当前 active 的弹幕，不再每次遍历完整对象池。
- 新弹幕发射前，只有文本对象仍 active 时才 `SetActive(false)`，减少无意义 UI active 状态切换。
- 新建模块不再给主弹幕 Canvas 添加多余 `GraphicRaycaster`；重新执行 `Wire Selected Bili Danmaku Module` 会移除旧模块上的主 Canvas raycaster，保留独立控制面板 Canvas 的 raycaster。

这些修改的目标是降低无效 CPU/UI 工作量，不改变 TMP 材质、透明度、描边、Underlay、镜子可读 shader 或按钮交互结构。

## 13. iwaSync3 与 VizVid 专用适配线

v1.02 之后开始拆分第三方播放器专用线。目标不是把 YamaPlayer 适配层改成万能抽象，而是在不动稳定主链路的前提下，为不同播放器提供最小接入层：

- YamaPlayer 线继续使用 `YamaBiliDanmakuV3/`。
- iwaSync3 线使用 `IwaBiliDanmakuV3/`，依赖 `HoshinoLabs.IwaSync3.Udon.VideoCore`。
- VizVid 线使用 `VizVidBiliDanmakuV3/`，依赖 `JLChnToZ.VRC.VVMW.Core`。

这三条线可以复用弹幕解析、对象池、显示区域、弹幕开关、描边和镜子可读 shader 思路，但播放器接口必须各自隔离。不要让 VizVid 线引用 YamaPlayer 或 iwaSync3 类型；也不要为了适配新播放器去改 YamaPlayer 已验证可用的类名、namespace、文件名或 U# Program Asset 关联。

### iwaSync3 线

iwaSync3 的可用接入点是：

```text
HoshinoLabs.IwaSync3.Udon.VideoCore
```

当前 iwa 线从 `VideoCore.url` 读取当前 URL，从 `time / paused / isPlaying / isLive` 读取播放状态，并通过 iwa 的 listener 事件重置或恢复弹幕。默认生成的弹幕模块使用用户实测正常的画面参数：

```text
Canvas Size      2875 x 1600
Canvas Position  -0.005, 1.001, -0.02
Canvas Scale      0.001
Base Font Size   52
Font Scale        1.1
Line Height       92
Line Spacing      1.35
Lane Count        12
```

曾经踩过的坑是直接设置 `TextMeshProUGUI.fontSize` 会在 UdonSharp 中报 `Method is not exposed to Udon: 'text.fontSize'`。因此运行时不要改 TMP 字号；字号、行高和材质样式应由编辑器生成器在创建对象时写入，运行时只通过 `RectTransform.localScale` 和自身轨道计算控制观感。

iwa 的 URL 前缀辅助应绑定 iwaSync3 的 `VRCUrlInputField`，常见路径是 `Canvas (1)/Panel/Address` 或 `Canvas (1)/Address`。如果自动搜索不到，允许世界作者手动拖入，但不要把 Yama 的 Top/Bottom URL Input 自动查找逻辑搬过来。

### VizVid 线

VizVid 1.7.5 的可用接入点是：

```text
JLChnToZ.VRC.VVMW.Core
```

VizVid Core 公开了当前 URL 和播放状态，因此 VizVid 线不需要读私有字段，也不需要依赖 Yama/iwa：

```text
Core.Url
Core.Time
Core.IsPlaying
Core.IsPaused
Core.IsStatic
```

VizVid 的事件机制来自 `UdonSharpEventSender`。它的 Core 会通过 `SendEvent(...)` 广播事件，正确做法是在模块启动时只注册必要的命名事件：

```text
_core._AddListener(this, "_OnVideoBeginLoad")
_core._AddListener(this, "OnVideoPlay")
_core._AddListener(this, "OnVideoPause")
_core._AddListener(this, "_onVideoEnd")
_core._AddListener(this, "_onVideoLoop")
_core._AddListener(this, "_OnVideoError")
```

不要注册全量 listener 后让所有 UI、音量、同步事件都打到弹幕模块；这会增加无意义 Udon 事件调用，也会让后续排查更难。

VizVid 测试包当前命名为：

```text
vizvid1.0.zip
```

它只包含 `VizVidBiliDanmakuV3/`，不包含 README、docs、Yama 线或 iwa 线。解压后应直接进入 package 文件夹。正式发布前仍需用户在 Unity/VRChat 中确认：编译无错误、URL 前缀可用、弹幕位置和字号合适、按钮可点、暂停/跳转/关闭再打开弹幕行为正常。

### VizVid URL Fill 开关失败复盘

`vizvid1.3` 到 `vizvid1.6` 试图给 VizVid 的 URL 前缀辅助增加 `URL Fill: On/Off` 第三个按钮，但这条路线被验证为不稳定，当前已回退到 `vizvid1.4` 行为作为临时基线。

失败点包括：

- `URL Fill: Off` 为了“当场清空前缀”调用 `VRCUrlInputField.SetUrl(VRCUrl.Empty)`，会触发 VizVid 自己监听的 URL 输入变化，从而弹出播放提示或进入播放器输入流程。
- 改成只写 `textComponent.text = ""` 后，显示文本和 `VRCUrlInputField.GetUrl()` 的真实值不同步，导致 On/Off 状态看似切换，实际输入框和回填逻辑都不可靠。
- 继续尝试在 On 时修补显示文本，只会让“显示值”和“真实 URL 值”之间的状态更复杂，容易出现按钮不生效、输入框不刷新或播放结束不回填。

因此后续不要再把“开关状态”和“清空当前输入框”绑在一起。更稳的设计应是：

- `URL Fill` 开关只控制后续是否自动回填，不在 Off 时修改 VizVid 输入框内容。
- 如果必须清空输入框，应使用 VizVid 官方 UI 流程或明确的独立清空按钮，并验证不会触发播放提示。
- 播放中、加载中、暂停中仍然不应回填；播放结束自动回填可以保留，但必须只在输入框为空时执行。
- 若再次实现这个开关，应先在 Unity/VRChat 中单独验证 `VRCUrlInputField.SetUrl`、`onValueChanged`、`onEndEdit` 与 VizVid `UIHandler` 的交互，再合入测试包。

### VizVid 错误 URL 后 URL Fill 卡死

VizVid 在视频加载失败或 URL 无效后，`Core.Url` 可能仍保留失败的 URL。旧版 `VizVidBiliUrlPrefixHelper3.IsVizVidBusy()` 把“Core 中存在非空 URL”也当成 busy，于是错误后即使播放器已经不在加载/播放，URL Fill 仍会被永久挡住，按钮开关也无法重新补回前缀，必须先播放一个正常视频才能解除。

修复原则：

- busy 只应代表 `IsLoading`、`IsPlaying` 或 `IsPaused`，不能因为 `Core.Url` 残留非空就一直 busy。
- URL Fill 关闭时不能任意清空输入框；只允许在播放器空闲、且输入框内容严格等于自动前缀时调用 `SetUrl(VRCUrl.Empty)`，用于去掉 untouched 的自动前缀。
- URL Fill helper 注册 VizVid `_OnVideoError`，错误后延迟恢复；如果输入框为空，或输入框内容等于 VizVid 失败时残留的 `Core.Url`，则写回解析前缀。
- 不改弹幕下载、解析、播放同步和渲染逻辑。

后续又发现：在普通输入模式下，如果输入框当前只是自动填入的前缀，`URL Fill: Off` 看起来没有效果。修正为：Off 时只在播放器空闲、且输入框内容严格等于自动前缀时清空输入框；如果玩家已经输入了真实 URL，或者 VizVid 正在加载/播放/暂停，则不改输入框。这样保留 Off 的即时可见效果，同时避免再次把“开关状态”和“任意清空输入框”绑死。

### 跨播放器线的维护规则

- 播放器适配层可以不同，弹幕解析和渲染主逻辑应保持同一种行为。
- 显示区域切换仍然只影响后续弹幕，不能清空已发射弹幕。
- 弹幕关闭时已有弹幕瞬间隐藏；重新打开时，如果时间线上该弹幕还未结束，应按当前播放时间恢复到正确位置。
- 轨道满时优先丢弃新弹幕，不要强行压到已占用轨道造成严重重叠。
- 测试包只打包对应播放器线目录；稳定版本和 GitHub release 只有在用户明确确认后再提交、推送和发布。

### 可拾取平板与外部显示面挂载

一次失败修复把弹幕模块的 parent 简单改成 `selected.transform.root`，目的是让挂在可拾取平板上的播放器跟随移动。这个判断不够安全：pickup root 往往不是实际屏幕面，弹幕 Canvas 仍按屏幕局部坐标 `(0, 0, -0.02)`、`0.001` 缩放生成，挂到 root 后会偏离显示面，表现为“连弹幕都不显示”。

随后又尝试过两种 beta 修法：

- 直接挂到当前选中的显示面 Transform。
- 挂到显示面所在 root，再用显示面的世界姿态计算位置。

这两种方案在安卓端都出现弹幕明显发灰/变淡，而切回 1.03 后正常。因此本轮外部挂载尝试判定失败，代码应先回退到 1.03 的生成逻辑：`FindPlayerRoot` / `FindIwaRoot` / `FindVizVidRoot` 决定父级，`BuildRig` 使用播放器线原本的局部坐标。不要在没有新验证方案前再次改生成父级。

1.03 的 `_textAlpha = 0.72` 本身在安卓端验证过没有问题。不要把外部挂载导致的发灰误判成全局透明度问题，也不要为了修挂载问题把默认透明度改成 1.0。

后续平板跟随应做成独立包，而不是塞回正式 `YamaBiliDanmakuV3`。第一版隔离方案使用 `YamaBiliDanmakuTabletV3`：独立 namespace、独立类名、独立 shader/material 路径，菜单放在 `PaulKoiPlayer > YamaPlayer Tablet`。不要再按被选中的显示面 `RectTransform` 推算尺寸；实践中平板 UI 的 Rect 可能极大，会生成夸张的大白板。也不要用 `Controller` 推断平板位置，因为平板可能借用另一个电视上的 YamaPlayer Controller，甚至本地 Controller 已被删除。更稳的方式是：用户选中平板层级下已有的 YamaPlayer/屏幕参考对象，工具在同级生成 Tablet 模块并复制该参考对象的 `localPosition`、`localRotation`，但尺寸保持 1.03 逻辑，也就是模块 `localScale = Vector3.one`、Canvas 自己使用 `1750 x 980` 和 `0.001`。Controller 只作为播放同步数据源绑定到 `_controller`，不能参与位置和大小计算。

### v1.04beta PC 端挂载基准

用户提供的 `D:\Downloads\1.04beta.unitypackage` 已验证为当前 PC 端使用正常的 YamaPlayer 包。解包对比后确认其源码与 `E:\paulkoiplayer\beta11.1.zip` 完全一致，因此 1.04beta PC 端以这版为准。

这版的挂载规则是 `ResolveRigParent(...)`：

- 如果选中对象在播放器自身层级内，仍然挂到播放器根节点，保持普通电视/大屏用法。
- 如果选中对象不在播放器根节点内，并且选中对象自身也不包含对应播放器核心组件，则把弹幕模块挂到当前选中的显示面 Transform。
- YamaPlayer、iwaSync3、VizVid 三条 PC 适配线保持同一规则。

这条 PC 规则解决的是外部显示面 / 非播放器根节点的桌面端生成位置问题。它在 Android / Quest 可拾取平板上曾触发弹幕发灰/变淡，因此不要把 1.04beta PC 包视为安卓修复。安卓端规则应保持明确：普通安卓播放器继续使用 1.03；安卓可拾取平板使用 `YamaBiliDanmakuTabletV3` 独立包，并手动拖入播放用 Controller 作为时间和 URL 数据源。

## 14. 服务端依赖与缓存

服务端使用 Node.js 20 Docker 镜像，宿主机默认映射 `7858 -> 3000`。B 站信息、媒体直链和弹幕分别缓存；相同键的并发请求通过 inflight 合并，避免缓存未建立时重复请求上游。

网易云音乐解析依赖第三方服务 [https://music.znnu.com/](https://music.znnu.com/)。该依赖不是本项目控制的服务，接口变化、限流、地区限制或不可用都会影响网易云功能。

真实 `BILI_COOKIE`、Token 和 `.env` 不得提交到仓库。

### v1.0.3new 后端刷新

`vrc-bilibili-danmaku-server-v1.0.3new.zip` 仍然保持公开版本号 `1.0.3`，但内部加入了几项防护和兼容改动：请求限流、上游请求超时、失败缓存、输入 URL 白名单、B 站 / 网易云分享文本中的链接提取、`bili2233.cn` 短链识别，以及更完整的缓存 / 限流 / 上游失败统计。

这次后端改动不改变 Unity 端 URL Prefix、弹幕解析和播放同步接口。发布时应替换 release 中的 `vrc-bilibili-danmaku-server-v1.0.3.zip` 资产，但 release tag 继续使用 `v1.0.3`。

## 15. YamaPlayer v1.10 正式稳定版

`beta13.43` 经单人和多人播放列表逻辑迭代后，转为 YamaPlayer PC / 桌面端正式稳定版 `1.10` 并发布到 GitHub。本轮只发布 YamaPlayer 线；iwaSync3、VizVid 与 Android / Quest 平板专用线尚未移植同一套播放列表功能，不得混入 1.10 Release。

这里的“YamaPlayer v1.10”是本项目适配组件的发布版本，不是 YamaPlayer 上游包版本。该适配线的上游唯一参考是 `net.kwxxw.yama-stream-1.5.18.zip`；修改或审查播放器接口前必须先解包 1.5.18 对照真实源码和 prefab。

### 播放列表与播放方式

- 原 `Bili Pages` 改为通用的“播放列表”，同时支持 B 站多 P、合集/list 与网易云音乐歌单；单 P/单曲也显示标题。
- 面板每页最多显示 6 项，使用“首页”“上一页”“下一页”“顺序播放”“单项循环”等中文按钮。
- 点击非当前项目会直接切换播放；重复点击当前项目不会重新加载播放和弹幕。
- 网易云歌单的顺序播放是整表循环，最后一曲结束后回到第一曲；B 站多 P 到最后一 P 后继续 YamaPlayer 后续队列，没有后续项目时停止。单项循环复用 YamaPlayer `Controller.Loop`，避免维护第二套循环状态。
- “首页”只回到列表第一页，不重新解析、不重启当前媒体，也不改变当前播放项目。

### vcrid 与 Udon 限制

- Unity 先读取后端 manifest，再使用后端预置的 `/player/?vcrid=<id>` 条目播放。`vcrid` 同时关联对应分 P/曲目的弹幕。
- VRChat/Udon 不允许在运行时使用 `new VRCUrl(string)`；因此可播放的 vcrid URL 在构建世界时由 `YamaBiliVcridBuildProcess3` 预生成，运行时只从固定目录取 `VRCUrl`。
- 不重新引入不存在的 `YamaPlayerListener`、`TrackUtils`、`Controller.Handler` 或 `Controller.MirrorFlip`，也不依赖 YamachanWebUnit。

### 列表保持与事件竞争

- B 站与网易云歌单都不会因为正常切换项目、首次播放事件或非最终停止事件而把完整列表缩成当前单项。
- 新输入源在确认后才替换旧 manifest；失败时保留明确状态，不用只有 `idle` 的无信息表现。
- 播放器实际 URL 的 vcrid 是当前项目判断的最高优先级，避免 UI 选中项与真正播放项目分离。
- `beta13.42` 修复三处 `vcrid=` 参数偏移错误：参数值必须从标记后的第 6 个字符开始读取，不能错误跳过首位。该错误会把 `vcrid=150` 解析成 `50`、把 `vcrid=52` 解析成 `2`，从而导致长分 P 列表跳到错误项目。
- `beta13.43` 增加本地页面浏览锁定：用户主动点击首页、上一页或下一页后，首次清单解析完成和队列标题回填只更新数据与当前项目，不再把正在浏览的页码强制重置为首页。该状态只属于本地 UI，不加入多人同步字段。

### 多人同步

- `YamaBiliPagesPlaylist3` 使用 Manual Sync，只同步 manifest 来源、当前 vcrid、播放模式、修订号和列表是否有效等少量状态，不同步整个标题数组或弹幕数据。
- 点击项目的玩家先取得 Pages 组件和 YamaPlayer Controller 的所有权，再更新播放与同步状态。
- 非所有者收到的 `OnVideoStop` 不得清空公共列表；只有所有者的真实终止操作可以同步清空。
- 后加入玩家收到同步状态后，用 manifest 来源重新下载完整列表，并根据同步 vcrid 恢复当前项目高亮。
- 该同步只负责播放列表 UI 与选择状态；媒体播放仍由 YamaPlayer 自身同步，弹幕渲染仍按各客户端播放器时间线运行。

### 保持不变的稳定链路

- 没有改动既有弹幕下载、`#YBDM/1` 解析、对象池、时间同步、暂停恢复和显示区域主链路。
- 保留彩色文字、黑色描边、轻微加粗、镜子可读 shader 与现有透明度基线。
- 安卓规则不变：普通 Android / Quest 播放器使用 1.03；可拾取平板使用独立 `YamaBiliDanmakuTabletV3` 包，不把 Tablet 修复并回 YamaPlayer 1.10。
- Unity 编辑器若找不到包含所需中文字形的持久化 TMP 字体，Inspector/Scene 里可能显示方框；VRChat 客户端的运行时字体回退表现不受此编辑器提示影响。

## 16. v1.10 长周期开发复盘

1.10 的难点并不是把 `/api/pages` 的结果画成六个按钮，而是让播放器 URL、后端 manifest、YamaPlayer 队列、当前播放条目、弹幕/歌词来源、多人同步和本地翻页状态在大量异步事件中保持一致。开发过程中多次出现“单独看每段代码都合理，但组合后状态互相覆盖”的问题。以下内容是这轮开发中新增的工程认识，也是以后修改播放列表时必须遵守的边界。

### 不要再把所有状态都叫“当前 URL”

最终实现至少区分六类状态：

- **输入源 URL**：用户输入的 B 站视频、合集/list、网易云单曲或歌单地址，用于请求 manifest。
- **manifest URL**：可重新下载完整列表的来源，不能被当前某个 `/player/?vcrid=...` 覆盖。
- **当前媒体 URL**：YamaPlayer 真正正在播放的条目，主要用于确认当前 `vcrid` 和加载对应弹幕/歌词。
- **YamaPlayer Queue**：普通单 P、独立视频和排在多 P 后面的项目；其生命周期由 YamaPlayer 负责。
- **轻量 manifest 数组**：B 站多 P 和网易云歌单的标题、页码、`vcrid` 等数据，不应全部转换成 YamaPlayer `Track`。
- **本地 UI 页码**：玩家正在浏览哪一页，只属于本地界面，不等于当前播放项目，也不应作为多人同步状态。

早期大量回归都来自这些状态被复用：播放某个 `vcrid` 后把完整 manifest 当成单项、标题回填完成后重置当前页、停止事件把列表当成已经更换来源、或把队列中的占位条目误认成当前媒体。以后新增字段时应先明确它属于上述哪一类，不能再用一个 `_lastUrl` 同时承担多个角色。

### Udon 与 YamaPlayer API 边界

- UdonSharp 运行时不能调用 `new VRCUrl(string)`。编辑器 C# 可以构造 `VRCUrl`，因此 `YamaBiliVcridBuildProcess3` 在构建世界前预生成 `/player/?vcrid=1..N` 的 `VRCUrl[]`，运行时只按 ID 取已有对象。
- `VRCUrl` 没有可供当前 Udon 环境调用的 `IsValidUrl()`；字符串 URL 校验与 `VRCUrl.IsNullOrEmpty()` 不能混为一谈。
- 不得根据别的 YamaPlayer 版本或旧教程猜 API。实际使用的包里没有 `YamaPlayerListener`、`TrackUtils`、`Controller.Handler` 或 `Controller.MirrorFlip`；`Playlist.AddTrack` 需要真正的 `Track`，不是 `object[]`。
- `TMP_Text.fontSize` 在这套 Udon 暴露表中不可运行时赋值。字体大小、行高、Auto Size、Mask 和字体资产应由 Editor 生成器配置，不能把普通 Unity C# 可写属性直接搬进 UdonSharp。
- U# Program Asset 依赖稳定的类名、namespace、文件名和 Source C# Script 关联。修复功能时不应重命名现有运行时类，也不能用大重构换掉组件身份。
- 判断播放器 API 时必须以用户实际导入的 YamaPlayer 源码为准。此次在拿到 `net.kwxxw.yama-stream.zip` 后才确认可用的 `Controller`、`Playlist` 和 `Track` 接口，避免继续围绕不存在的成员修补。

### 后端 vcrid 机制带来的新知识

- `vcrid` 不是把整个 B 站 URL 库爬下来，而是后端为已经解析到的可播放项目建立稳定映射。B 站多 P 绑定 `bvid + cid`，list/合集里的每个独立 BV 绑定自己的项目，重复来源可以复用同一 ID。
- `/player/?vcrid=<id>` 是双用途端点：视频播放器请求时 302 到媒体直链，`VRCStringDownloader`/文本请求时可以返回 `#YBDM/1`。因此同一份预生成 URL 既能播放，也能下载对应弹幕或网易云歌词。
- Unity 不应自己重新拼接 list 内每一项的播放地址，优先使用 manifest 和稳定 `vcrid`。否则短链、独立 BV、分 P `cid` 和网易云 provider 很容易被错误归一化。
- 后端接口契约曾从 `/api/pages` 404、`/player/?__dm=1&url=...` 文本清单演进到带 `vcrid` 的 manifest。Unity 端不能只根据曾经的接口说明推断线上行为，每次变更都要实际验证状态码、响应类型、`manifest_type`、`provider`、项目数和首尾 `vcrid`。
- `vcrid=` 的值从标记后的第 6 个字符开始。`marker + 7` 会悄悄吃掉第一位数字，使 `150` 变成 `50`、`52` 变成 `2`。这种错误不会导致编译失败，只会表现为“点 P150 却跳到别的视频”，必须通过日志同时打印原 URL 与解析 ID 才容易发现。

### 150 项卡顿不是 Unity 的硬上限

曾经怀疑“Unity 或 VRChat 最多只能加载 150 个队列项目”，实际问题不是固定的 150 上限，而是把 90/150 个分 P 全部实例化为 YamaPlayer `Track` 后产生的连锁开销：批量 Add/Remove、网络所有权、队列序列化、标题回填、重复规范化以及 UI 重建会集中发生在同一小段时间内。

最终采用混合模型：

- B 站多 P 保存在 `_biliManifestParts/_biliManifestVcrids` 等轻量数组中，播放面板每次只渲染当前 6 项。
- 多 P 作为一个占位项目排入 YamaPlayer Queue，轮到它时再进入多 P 模式；播放到最后一 P 后继续后续普通队列，不自动回到 P1。
- 普通单 P/独立视频继续使用 YamaPlayer Queue，播完后按 YamaPlayer 原有行为自然移除。
- 网易云歌单进入独立 manifest 模式并整表顺序循环；它不会把每首歌永久堆成大量 Yama Queue 项目。
- 统一显示列表通过缓存后的“来源类型 + 来源索引”合并 manifest、当前项目和 Queue；缓存失效时重建，普通帧不反复扫描和分配。

代码中的 `MaxUnifiedQueueItems = 200` 是界面和内存保护值，不是 VRChat 平台宣称的媒体队列上限。未来若提高它，仍要先测试所有权、序列化和最坏情况下的列表重建成本。

### 播放事件不是单一的“结束”

YamaPlayer 在自然播放结束、用户手动停止、内部切 P、点击其他项目、加载失败和网络端状态变化时，可能触发相近或连续的回调。尤其常见的是 `OnVideoEnd` 后紧接 `OnVideoStop`。如果每个回调都直接清列表或前进队列，就会出现双重前进、跳过项目、列表缩成单项或音乐仍播放但 UI 已清空。

最终必须显式区分：

- `_naturalEndPending`：自然播放结束，后续 Stop 不能再按手动停止处理。
- `_internalTrackSwitch` / `_autoAdvancePending`：插件正在主动切换项目，旧媒体的 Stop 不能清理新状态。
- `_manualStopAdvancePending`：普通队列手动停止后只安排一次前进。
- `_currentPlaybackIsManifestItem` / `_biliManifestPlaybackLocked`：当前播放是否确实属于 B 站多 P manifest，不能只看标题猜测。
- `_ignorePlaybackUrlWhileStopped`：停止阶段播放器仍可能短暂保留旧 URL，此时不能据此重建列表。
- 请求模式、请求 URL 与待处理索引：异步下载回调必须验证自己仍对应当前请求，旧回调不得覆盖新输入。

延迟一帧或等待 0.35 秒只能用于让 YamaPlayer 完成状态提交，不能充当业务真相。凡是使用延迟回调，都必须在回调执行时再次检查 Owner、Stopped、IsLoading、Queue 长度、请求身份和 pending 标志。

### 列表被缩成单项的根因

开发中最顽固的回归是：完整 B 站多 P 或网易云歌单已经显示，点击某项后却只剩当前一项。出现过的根因包括：

- 把当前 `/player/?vcrid=...` 的文本响应当成新的 manifest 来源重新解析。
- 播放开始或停止时再次把当前媒体 URL 当成用户的新输入。
- 解析队列占位条目标题时临时覆盖 `_parts/_vcrids`，异步完成后没有恢复原 manifest。
- 网易云第一首开始播放时，被单曲 metadata 响应误判为新的单曲列表。
- 非 Owner 客户端收到停止事件后清除了共享列表状态。

解决方式不是再增加一个固定秒数，而是保存和恢复完整的 standalone manifest、区分 manifest source 与 current playback、给解析请求标注用途，并让清空操作只在明确的新来源确认或真正的用户操作中发生。

### B 站与网易云不能共用完全相同的结束策略

- B 站多 P 属于一个保留的轻量组。P1 到 Pn 顺序播放，最后一 P 后继续排在它后面的普通视频；没有后续项目时停止。多 P 条目只有用户点击 `×` 或清空队列时才删除。
- 普通单 P/独立视频是一次性 Queue 条目，播放完成后可以消失。
- 网易云歌单是排他的整表模式，顺序模式下最后一曲回到第一曲；切换“单项循环”时只循环当前歌曲。
- 网易云单曲可以作为普通项目与 B 站视频共存，但完整网易云歌单载入时应清理原普通队列，避免两套循环所有权互相竞争。
- 在网易云歌单播放期间追加 B 站项目时，必须先保留当前歌单数组和当前音频，解析完成后再决定排队或切换；不能先清 UI 再等待新视频成功。

Provider 需要在 manifest 解析和真正播放开始时各确认一次。只在首次输入时判断 provider，会在队列切歌、后来加入玩家恢复或播放 URL 被 YamaPlayer 规范化后把网易云歌词走回 B 站弹幕逻辑。

### 网易云歌词复用弹幕链路

- 后端把网易云歌词转换成 `#YBDM/1`，歌词行使用 `mode=4`。Unity 不需要维护第二套字幕解析器，而是复用现有时间轴、对象池和底部静态弹幕渲染。
- `SetExternalAudioMode(true)` 是自动状态，不是让世界用户手动开关。它只阻止弹幕模块错误请求原始歌单 JSON，不能阻止已经加载歌词的 Update、计时和渲染。
- 选中歌曲、自动下一曲、多人恢复当前曲目时，都必须用预生成的 `_vcridUrls[vcrid]` 调用现有 `LoadDanmakuUrl(VRCUrl)`。
- 原文和翻译可能拥有同一时间戳。底部歌词需要稳定的专用行位和合适的存活时间，不能沿用普通顶部/底部弹幕“每条出现后固定数秒消失并逐行堆叠”的全部行为。
- 字幕位置、第二行显示和持续到下一句等调整只能改歌词 mode 的布局/生命周期，不能顺手改变普通 B 站弹幕透明度、描边、字体材质或轨道算法。

### 多人同步的正确粒度

- Pages 组件使用 Manual Sync。点击条目前先取得 Pages 组件、YamaPlayer Controller 以及需要修改的 Queue/History 所有权，再写同步字段并发送事件。
- 不同步完整标题数组和几百个条目。只同步 manifest 来源、模式、当前 `vcrid`、队列标题/来源、修订号和删除的 manifest ID；后来加入玩家根据来源重新下载清单。
- 当前浏览页 `_pageOffset` 与 `_pageViewPinned` 是本地 UI 偏好，不能同步。否则一个玩家翻页会把全房间玩家的面板一起拉走。
- 非 Owner 的 `OnVideoStop`、下载失败或本地 UI 操作不能清空公共清单。共享状态清空必须由拥有者在明确条件下发布。
- “视频同步正确”和“列表高亮正确”是两个验收项。媒体由 YamaPlayer 同步，Pages 组件仍要用实际播放 `vcrid` 恢复高亮，不能只同步数组下标。
- 黑白 UI 的播放列表不要再用“当前项白字、其他项灰字”制造层级；列表文字应全部保持不透明纯白，当前项仍保留 `> `，并让行背景形成清楚但不突兀的明暗区分。当前测试值为普通行白色 alpha `0.07`、当前行白色 alpha `0.14`；Unified Queue 和 standalone manifest 两条刷新路径都要调用同一个 `SetPageButtonVisual`，不能再次出现某一模式只改文字、不改底色。

### 输入框、字体和 UI 的隐藏成本

- YamaPlayer 原有 Top/Bottom URL Input 与组件自己的 `Queue URL Input` 是不同职责。`alpha1.0` 起、当前正式 1.11 已继承，针对固定参考的 YamaPlayer 1.5.18，生成器会先从 `Yamadev.YamaStream.UI.UIController` 的 `_urlInputFieldTop` / `_urlInputField` 读取播放器自己序列化的权威引用；若代理字段不可读，才回退到已经由 1.5.18 `ScreenUI.prefab` 验证的精确路径 `ScreenUI/Canvas/Control/Main/Top/UrlInput` 与 `ScreenUI/Canvas/Control/Main/LeftSide/Container/UrlInput`。只补齐空字段，已有手动引用优先；必须排除组件自己的 `Queue URL Input`，也不得按组件出现顺序、单纯对象名或全场景第一个输入框猜测。自定义/改名布局无法精确匹配时继续手动拖入。生成器创建的 Queue 输入框仍必须自动绑定到 `YamaBiliUrlPrefixHelper3` 和 `YamaBiliPagesPlaylist3`。
- 输入框第一次打开把 WASD 移动键录进去，是焦点、选中时机和预填动作竞争的结果，不是 URL 内容解析问题。应沿用 YamaPlayer 的输入生命周期，在 Select 前后正确 Prime/Reset，并在提交、取消、错误后恢复空闲显示和解析前缀。
- 错误 URL 不能永久留在输入框，也不能让下一次提交继续使用旧值。错误路径与成功路径都必须走统一 Reset。
- “视频链接”现在是集成队列输入，不再是早期的 URL Fill On/Off 按钮。旧 `_urlPrefixToggleButtonLabel` 只为兼容旧对象保留，生成的新 UI 不应再显示过时开关。
- `Queue URL Input` 的可见图标和空闲文字不能放在 InputField 根节点的 `Mask` / `targetGraphic` 状态链里。全透明根遮罩会把子级全部裁掉；不透明根遮罩即使用 `showMaskGraphic = false` 暂时隐藏，也会让第三格继续走与前两个 Button 不同的 stencil/Selectable 材质路径，SDK 修复或选中状态后可能出现亮度差异。当前结构要求：InputField 根 Image 与两个 Cycle Button 一样保持透明并只承担点击；链条图标和 `Idle Label` 直接挂在根节点且不参与 Mask；只有实际 URL `Text` 和 `Placeholder` 放进独立 `Input Text Mask`，其白色遮罩源用 `showMaskGraphic = false` 隐藏；InputField Transition 固定为 None。第三格图标仍必须复用 `CreateOrFindControlIcon`，避免尺寸、位置、颜色、材质或启用规则再次分叉。
- `VRCUrlInputField` 的实际 URL 输入仍需要旧版 `UnityEngine.UI.Text`，但不能因此让第三格空闲标题也使用旧 Text：它与前两个 `TextMeshProUGUI` 在字宽、字重和抗锯齿上明显不同。`Idle Label` 的旧 Text 只保留为 `YamaBiliUrlPrefixHelper3` 的显隐状态句柄并禁用渲染，用户可见的标题必须由其子级 TMP Label 绘制；输入为空时的 Placeholder 也使用 TMP。三个控制标题统一调用 `ConfigureControlLabelVisual`，新标题字符必须同时加入 `RequiredUiCharacters`，防止动态 UI 字体缺字。
- Unity 场景中中文显示成方框，主要是 TMP 字体资产缺少对应 glyph/atlas，不是 C# 字符串损坏。1.10 的 Noto Sans SC 字体只用于组件 UI；不得替换弹幕 Font Atlas 或修改弹幕材质。
- 标题走马灯应根据 `preferredWidth > viewportWidth` 判断真实溢出，并配合 Mask/RectMask 裁切。按“22 个字符”截断无法正确处理中文、日文、拉丁字母和不同字宽。
- UI 生成器要保持按钮等宽、箭头方向/位置一致、删除区有稳定点击范围。Transform、Canvas、Collider 与子对象位置必须统一使用同一局部坐标系，否则移动父级后会出现碰撞体或文字漂移。

### 平板适配不能再塞回普通 PC 包

可拾取平板曾经尝试通过修改普通 `FindPlayerRoot()` 或直接使用 `selected.transform.root` 解决跟随问题，结果破坏 Controller 数据来源、模块挂载比例或弹幕渲染。PC 端“外部显示面跟随”与 Android/Quest 平板的材质、透明度和缩放问题并不是同一个问题。

最终纪律保持不变：YamaPlayer PC 黑白主线使用由 alpha1.1 原样晋升的本地正式 `YamaBiliDanmakuV3` 1.11，金黑测试分支使用 beta14.02；Android/Quest 普通播放器继续使用 1.03；可拾取平板使用独立 `YamaBiliDanmakuTabletV3`。alpha1.1 仅作为正式版晋升来源留档。这些分支不能因为一个场景修复就互相复制生成器或材质参数。

### 日志优先于现象描述

世界内看到的现象经常只能说明最后结果，不能说明先发生了哪个事件。例如“P3 变成单项”可能是 manifest 被清空、显示缓存漏项、错误 `vcrid`、旧下载回调覆盖，或 UI 跳到了只有一项的尾页。

以后排查播放列表必须同时记录并对照：

- 请求 URL、请求模式、下载成功/失败和响应 manifest 类型。
- 当前 Controller URL、Track 标题、Queue 长度、Stopped/IsLoading/Loop。
- manifest 项目数、selected index、selected vcrid、同步 vcrid 和当前页偏移。
- B 站混合模式、网易云独立模式、internal switch、natural end 和 pending advance 标志。
- 统一显示缓存中 manifest/current/queue 各自数量与最后一个来源类型。

此次 `vcrid=150 -> 50`、末尾重复当前项、点击项目后高亮丢失和“音乐还在播放但列表已空”等问题，都是结合 VRChat Player log 后才从多个相似猜测中定位。用户描述应作为复现入口，最终修复依据必须回到日志时间线和实际代码路径。

### 1.10 播放列表回归矩阵

以后修改 `YamaBiliPagesPlaylist3` 后，至少覆盖以下顺序测试，不能只验证一个两 P 视频：

1. B 站单 P：空闲输入、排在普通视频前后、自然结束、手动停止、无效 URL 后重新输入。
2. B 站多 P：2P、90P、150P；重点点击 P2、P3、P52、P90、P150，并核对画面、弹幕和高亮是同一项。
3. 多 P 首次加载期间翻到后页；加载第一 P 或标题回填完成后，面板不得跳回首页。
4. 单 P 在多 P 前、多 P 在单 P 前、多 P 后追加多个普通视频；最后一 P 应继续后续 Queue，不应重复 P1，也不应制造一个重复的“当前项”。
5. 多 P 播放中点击其他 P、重复点击当前 P、删除非当前 P、删除当前 P、清空队列和播放失败。
6. 网易云单曲：标题、播放、歌词、自然结束以及与 B 站单 P 混排。
7. 网易云歌单：首次进入世界后直接解析、立即点击第一曲和后页曲目、自动下一曲、最后一曲回到第一曲、单项循环切换、歌词原文/翻译与切歌更新。
8. 网易云歌单播放时追加 B 站单 P/多 P；当前音乐和歌单不能提前消失，切到新项目后不能把整个面板清空。
9. 暂停/继续、拖动进度、手动 Stop、自然 End、视频错误和快速连续点击；每种情况下检查是否只前进一次。
10. 两名以上玩家：Owner 点击、非 Owner 点击、Owner 离开、后来加入、不同玩家各自翻页；核对媒体同步、列表内容和当前高亮。
11. 超长中英日标题、缺字字符、走马灯、六项分页、左右箭头、单项删除和清空按钮。
12. PC 镜子可读、普通视角、B 站彩色/顶部/底部弹幕和网易云歌词；不得因播放列表改动改变透明度、描边或弹幕材质。播放时弹幕应覆盖视频，但点击 YamaPlayer 后，Order `0` 的灰色交互 UI 应稳定覆盖 Order `-1` 的弹幕/歌词根 Canvas；不得再次把两者设为同级 `0`。
13. Android/Quest 普通播放器和独立 Tablet 包分别测试，不得用 PC 1.10 的结果替代移动端验收。
14. 最后检查 VRChat log 中没有重复请求风暴、连续所有权争抢、同一 Stop 被处理两次、错误 `vcrid` 或显示缓存数量异常。

## 17. 对话收尾与新对话接手基线（2026-07-20）

这一节记录本轮长周期开发结束时的真实发布状态，用于防止新对话把正式版、本地实验包和移动端适配线混在一起。

### 已发布与未发布内容

- GitHub 当前正式基线是 **YamaPlayer PC / 桌面端 v1.11**，由 `alpha1.1` 原样晋升；Release tag 为 `v1.11`，正式包为 `PaulKoiPlayer-YamaBiliDanmakuV3-1.11.zip`。该包使用黑白 UI、YamaPlayer 1.5.18 URL Input 精确自动补齐和根 Canvas Order `-1`，不包含 iwaSync3、VizVid 或 Tablet 线。
- 上一版 `v1.10` 由 `beta13.43` 晋升，使用已经验收的金黑色 UI。本地 `E:\paulkoiplayer\1.10.zip` 与 v1.10 Release 资产内容一致，SHA-256 为 `D905E3BC0C657F4D926BBD427FB82E0E4B8EE9E728D36A517DC9A951CE8CFBE6`。
- 本地另有未发布的 `E:\paulkoiplayer\1.10alpha.zip`，它只把 v1.10 UI 改成纯黑、透明黑和白色文字/描边组合；没有修改 Runtime、弹幕 shader、材质、透明度、描边、镜子可读、下载、解析、播放或同步逻辑。该包尚未经过最终 Unity/VRChat 视觉验收，不得写成正式 1.10，也不得未经明确确认提交或发布。
- `1.10alpha.zip` 当前 SHA-256 为 `360D4397D696C21A232185DFA414EC044ECBC9BBDA7137AAD7927FE70FA08F31`。Alpha 源码改动只在 `Editor/YamaBiliDanmakuRigBuilder3.cs`；已有场景对象需要执行 `Yamadev > YamaPlayer > Apply Selected Bili UI Skin` 才会应用新皮肤。
- 本地 `E:\paulkoiplayer\beta13.44.zip` 是在 `beta13.43.zip` 上继续制作的未验收 PC UI 测试包，SHA-256 为 `7FA784714A19B92497B22874FE63542253D0B2A714BB89661B371592EA922430`。它保留黑白 Alpha 皮肤，把播放列表运行时刷新后的当前项和普通项文字都固定为不透明纯白，并把控制面板/播放列表共用外框的圆角半径增大；当前项仍由 `> ` 前缀区分。该测试包只修改 `Editor/YamaBiliDanmakuRigBuilder3.cs` 和 `Runtime/YamaBiliPagesPlaylist3.cs` 的 UI 视觉值，没有改播放、Queue、manifest、vcrid、所有权、Stop/End、弹幕/歌词或同步逻辑。已有场景对象仍需执行 `Yamadev > YamaPlayer > Apply Selected Bili UI Skin` 更新外框；未经 Unity/VRChat 验收不得提交、推送或发布。
- 本地 `E:\paulkoiplayer\beta13.45.zip` 在 `beta13.44.zip` 上把控制面板和播放列表共用的外框层、内层都改为截图取样的实心深浅灰 `RGB(34,36,38)`，不再保留“浅色边框 + 黑色内芯”；白色播放列表文字和上一版圆角保持不变。SHA-256 为 `79D7C0475A36118390E5EDB0B79E292695715D2D122BB4E4343CA50AE4DD55D4`。相对 `beta13.44` 只修改 Editor UI 颜色，仍未经过 Unity/VRChat 验收，不得提交、推送或发布。
- 本地 `E:\paulkoiplayer\beta13.46.zip` 在 `beta13.45.zip` 上把共用圆角九宫格的 `pixelsPerUnitMultiplier` 从 `1.0` 降到 `0.5`；依据 Unity 2022.3 UI `Image` 实现，这会扩大边界采样和圆角，使 70 高度控制面板的两侧接近可用的最大圆弧，并达到截图红线要求。实心 `RGB(34,36,38)` 灰底和播放列表纯白文字保持不变。SHA-256 为 `257C6548C6D9BB3A80293E9E4AE33997850142A3E4F4159D08D7AD999C74454A`。相对 `beta13.45` 只修改 Editor 圆角视觉值，仍未经过 Unity/VRChat 验收，不得提交、推送或发布。
- 用户随后确认 `beta13.45` 的“灰色填满”和 `beta13.46` 的“修改共用外框圆角”都误解了截图目标，这两个方向已作废，不应继续作为后续测试基线。正确目标是保留 `beta13.44` 的外层黑色圆角，只让内部淡灰控制按钮使用与外框相同的弧度。
- 本地 `E:\paulkoiplayer\beta13.47.zip` 因此直接以 `beta13.44.zip` 为基线：完整保留 13.44 的背景颜色、外层/内层结构和 `pixelsPerUnitMultiplier = 1.0`，只把 `CreateOrFindCycleButton` 中原本 `sprite = null`、`Image.Type.Simple` 的淡灰按钮背景改为与外框相同的 Rounded Sprite、Sliced 类型和倍率。播放列表纯白文字保持不变。相对 `beta13.44` 只有 `Editor/YamaBiliDanmakuRigBuilder3.cs` 不同，SHA-256 为 `57BE3E1FB83CA72E74D4D3FBE8FAEA484BCE11A6FF624C86F3A2F5EA6A4023F7`。仍未经过 Unity/VRChat 验收，不得提交、推送或发布。
- 本地 `E:\paulkoiplayer\beta13.48.zip` 在 `beta13.47.zip` 上修正第三格“视频链接”与第二格“弹幕显示”视觉不对称：`Queue URL Input` 原本仍使用 `sprite = null` 和普通矩形，现在与 Cycle Button 使用相同的 `ControlButtonWidth/Height`、半透明背景色、Rounded Sprite、Sliced 类型和 `pixelsPerUnitMultiplier`。相对 `beta13.47` 只修改 `Editor/YamaBiliDanmakuRigBuilder3.cs`，播放列表纯白文字及所有 Runtime 行为保持不变。SHA-256 为 `04E18530E57C8B264D2E783FCD3DE041180B4D7AEF2641B1B46569987F6FD3FB`。仍未经过 Unity/VRChat 验收，不得提交、推送或发布。
- 本地 `E:\paulkoiplayer\beta13.49.zip` 根据后续截图取消三个子控件各自可见的小矩形/圆角背景：两个 Cycle Button 的 Image 改为透明但继续承担点击命中，视觉填充只由最外层共用 `BG/Fill` 绘制，因此顶部成为一整条淡黑圆角长方形。此版本同时错误地把带 `Mask` 的 `Queue URL Input` 遮罩源 Image 也设为全透明，虽然设置了 `Mask.showMaskGraphic = false`，实际生成后仍会把第三格链条图标和“视频链接”文字裁掉；这一做法已由 `beta13.51` 修复，禁止再作为透明控件的通用写法。相对 `beta13.48` 只修改 `Editor/YamaBiliDanmakuRigBuilder3.cs`，SHA-256 为 `64022D56482290E9E2026C8C44DEB71F6A17E9A84E5A8DB03688CD35C6D50866`。仍未经过 Unity/VRChat 验收，不得提交、推送或发布。
- `beta13.49` 与正式 `1.10.zip` 做过逐文件静态比较：`Runtime/YamaBiliDanmakuModule3.cs`、`Runtime/YamaBiliUrlPrefixHelper3.cs`、`Editor/YamaBiliVcridBuildProcess3.cs`、镜子可读 TMP Shader、UI Shader 和 UI 字体逐字节一致；`Runtime/YamaBiliPagesPlaylist3.cs` 只有三处播放列表 Label 颜色由金/米色改为纯白，播放、Queue、manifest、vcrid、所有权、Stop/End、弹幕/歌词下载解析和渲染路径均未改变。Editor 生成器差异也只落在控制 UI 颜色、背景、按钮状态与提示文案，没有改 Text Pool、弹幕材质或 `ApplyVisualStyle` 链路。
- 本地 `E:\paulkoiplayer\beta13.50.zip` 在 `beta13.49.zip` 上恢复播放列表的明暗层级，但不恢复金色：由实际播放 vcrid 判定的当前项保持不透明纯白，其他 Queue/manifest 条目统一为 68% 灰白，原有 `> ` 当前项前缀和判定逻辑不变。包内文件清单与 `beta13.49` 一致，逐文件比较只有 `Runtime/YamaBiliPagesPlaylist3.cs` 的三处 Label 颜色值不同；正式版播放、Queue、manifest、vcrid、所有权、Stop/End、弹幕/歌词及同步关键文件仍逐字节一致。SHA-256 为 `E8280533BBABE3C3A71C36B2F6CD9C8351B859E6812C0CA0E42CF62242646EBA`。仍未经过 Unity/VRChat 验收，不得提交、推送或发布。
- 本地 `E:\paulkoiplayer\beta13.51.zip` 在 `beta13.50.zip` 上修复顶部第三格 `Queue URL Input` 的链条图标和“视频链接”文字被遮罩一起裁掉的问题：第三格仍通过 `Mask.showMaskGraphic = false` 隐藏自己的矩形，只把 Mask 使用的 Image 恢复为不透明白色以正常写入模板；链条图标改为直接复用前两格的 `CreateOrFindControlIcon`，因此三格的图标尺寸、位置、颜色、材质和启用规则同源。整条可见填充仍只来自共用 `BG/Fill`，不会退回三个独立小矩形。相对 `beta13.50` 的文件清单不变且只有 `Editor/YamaBiliDanmakuRigBuilder3.cs` 不同，播放列表明暗层级及所有 Runtime、弹幕/歌词和播放同步逻辑保持不变。SHA-256 为 `47129995DBB0D3C2241C918CB75FA2DCF7550F6BC7330008E758FCF631254DDE`。仍未经过 Unity/VRChat 验收，不得提交、推送或发布。
- 本地 `E:\paulkoiplayer\beta13.52.zip` 在 `beta13.51.zip` 上继续消除第三格与前两格的渲染路径差异：从 `Queue URL Input` 根节点移除 Mask，根 Image 改为与 Cycle Button 相同的全透明点击图形，InputField Transition 固定为 None；链条图标和 `Idle Label` 留在根节点并禁用 maskable，只有实际 URL Text/Placeholder 迁入独立的 `Input Text Mask` 继续裁切。这样 SDK Auto Fix、InputField 选中状态和 stencil 材质不会再改变第三格可见图标/空闲文字的亮度，整条可见背景仍只由共用 `BG/Fill` 绘制。相对 `beta13.51` 文件清单不变且只有 `Editor/YamaBiliDanmakuRigBuilder3.cs` 不同；所有 Runtime、播放列表明暗、弹幕/歌词与同步逻辑保持不变。SHA-256 为 `6BBB1BA16623DA5DFE8FEC9E9AD344939C5BA13BF97BE18DDCEA5B6C7EC2903D`。仍未经过 Unity/VRChat 验收，不得提交、推送或发布。
- 本地 `E:\paulkoiplayer\beta13.53.zip` 在 `beta13.52.zip` 上把第三格标题由“视频链接”改为“输入链接”，并解决其与前两格字体/字宽不同的问题：用户可见的空闲标题和输入为空时的 Placeholder 均改用 `TextMeshProUGUI`，与前两个按钮共同调用 `ConfigureControlLabelVisual`，统一字号 12、Bold、Auto Size 9–12、居中、颜色和 TMP 字体；旧 `UnityEngine.UI.Text` 仅作为 Runtime 显隐句柄保留且禁用渲染，实际 URL Text 仍保持 VRC InputField 所需类型。“输”已加入 `RequiredUiCharacters`。相对 `beta13.52` 文件清单不变且只有 `Editor/YamaBiliDanmakuRigBuilder3.cs` 不同；所有 Runtime、播放、弹幕/歌词和同步逻辑保持不变。SHA-256 为 `17562564F7779B9D8D93F9B2B192CE769DF580923D300650F1B3318C7B2574BB`。仍未经过 Unity/VRChat 验收，不得提交、推送或发布。
- 本地 `E:\paulkoiplayer\beta13.54.zip` 在 `beta13.53.zip` 上把播放列表六行文字统一为不透明纯白，不再用 68% 灰字区分普通项；由实际播放 vcrid/既有 selected index 判定的当前项继续保留 `> `，并将行背景白色 alpha 从普通项的 `0.07` 仅提高到 `0.09`。Unified Queue、standalone Queue 和 manifest 三种显示分支统一调用 `SetPageButtonVisual`，只改变 Label/Image 视觉值，不改变 Queue、manifest、vcrid、高亮身份、所有权、Stop/End 或前进逻辑。相对 `beta13.53` 文件清单不变且只有 `Runtime/YamaBiliPagesPlaylist3.cs` 不同；弹幕/歌词、同步及其他关键文件仍与正式 1.10 一致。SHA-256 为 `948E34C7360182969C4984BB3D3EB3CF29348987ECC82F4411D521002EABA260`。仍未经过 Unity/VRChat 验收，不得提交、推送或发布。
- 本地 `E:\paulkoiplayer\beta13.55.zip` 把当前项行背景白色 alpha 从 `0.09` 提高到 `0.14`，普通项仍为 `0.07`，六行文字仍全部为不透明纯白，当前项继续保留 `> `；相对 `beta13.54` 的代码差异只有 `Runtime/YamaBiliPagesPlaylist3.cs` 这一处视觉值。全包静态复核确认 9 个文件可读且逐字节匹配当前源码；相对正式 `1.10.zip` 只有 `Editor/YamaBiliDanmakuRigBuilder3.cs` 和 `Runtime/YamaBiliPagesPlaylist3.cs` 不同，其余 Runtime、vcrid 构建脚本、字体和两套 Shader 均逐字节一致。打包时还清除了从 `beta13.43` 测试包继承的 `Fonts/OFL.txt` 第 21 行末尾单个空格，使许可证重新与正式 1.10/仓库一致；这不是代码变更。播放、Queue、manifest、vcrid、异步身份、所有权、Stop/End、弹幕/歌词和同步主链路未改。SHA-256 为 `F2F600CF8A06A444850ACAFDA1E93A897BABE15C13B684A7AC78FBE0B4350666`。本机没有 Unity，仍未经过 Unity/UdonSharp/VRChat 验收，不得提交、推送或发布。
- 本地 `E:\paulkoiplayer\beta13.56.zip` 把弹幕与网易云歌词共用的根 World Space Canvas `sortingOrder` 从 `20` 降到 `0`。随后已用指定参考包 `net.kwxxw.yama-stream-1.5.18.zip` 复核：其 Screen Renderer、ControlBar 和 PlaylistPanel 均为默认 Sorting Layer、Order `0`，ScreenUI 内部 Canvas 使用 `-1/0`，因此该调整与 1.5.18 的层级基线相符，不依赖 2.0 beta 结论。新生成模块直接使用 `0`；已有模块执行 `Yamadev > YamaPlayer > Apply Selected Bili UI Skin` 时也会修正根 Canvas。控制栏和播放列表自己的子 Canvas 仍保留 `35/36`，只用于保证组件 UI 内部顺序；TMP shader 仍为 Transparent Queue、`ZTest [unity_GUIZTestMode]` 和 `ZWrite Off`，没有改成永远置顶，也没有修改透明度、描边、镜子、材质或运行时渲染算法。相对 `beta13.55` 只有 `Editor/YamaBiliDanmakuRigBuilder3.cs` 不同，其余 8 个包文件逐字节一致。SHA-256 为 `70E24BBF5A01A50201420636D386EDEADD05FDD45A2F84C76D37588AF6C5958A`。本机没有 Unity，仍需 Unity/VRChat 实机确认弹幕与 YamaPlayer 面板、视频画面和世界遮挡物的最终前后关系；不得提交、推送或发布。
- `beta13.56` 曾按用户要求首次原样升格为本地正式版 `1.11`。当时的 `E:\paulkoiplayer\1.11.zip` 与 `E:\paulkoiplayer\PaulKoiPlayer-YamaBiliDanmakuV3-1.11.zip` 都是 `beta13.56.zip` 的逐字节副本：大小均为 `3029338` 字节，SHA-256 均为 `70E24BBF5A01A50201420636D386EDEADD05FDD45A2F84C76D37588AF6C5958A`，包内 9 个文件和路径完全相同。该初始正式包保留 beta13.56 的黑白 UI、纯白列表文字、当前行/普通行 `0.14/0.07` 背景明暗、统一“输入链接”第三格、整条淡黑圆角背景及根 Canvas Order `0`，没有额外改动 Runtime、弹幕/歌词 shader、下载、解析、播放或同步链路；后来已由 alpha1.1 晋升的新正式 1.11 取代，当前两个正式包路径不再对应此旧哈希。当时 GitHub 正式版本仍为 v1.10。
- 本地 `E:\paulkoiplayer\beta14.00.zip` 是保留正式 1.10 金黑 UI 与金色/米色播放列表视觉的独立测试分支，大小为 `3029163` 字节，SHA-256 为 `FA5821BA94296887FDFDF116DD148F4DC86F7F35BFE06C8390A3F60892A93673`。它把 beta13.56 已确认的 UI/层级改动移植到金黑基线：根 World Space Canvas Order 改为 `0` 且 Apply Skin 会修正已有对象；第三格由“视频链接”改为“输入链接”；图标、可见 TMP 标题、字号、粗细、字宽、颜色和布局与前两格共用同一生成路径；三个子控件背景透明，顶部只由共用 `BG/Fill` 绘制一整条淡黑圆角长方形；实际 URL 文本仅由内部 `Input Text Mask` 裁切，不再让根 Mask/SDK Auto Fix 改变第三格图标和标题。相对正式 `1.10.zip`，只有 `Editor/YamaBiliDanmakuRigBuilder3.cs` 不同，其余 8 个文件逐字节一致，`Runtime/YamaBiliPagesPlaylist3.cs` 因而完整保留正式 1.10 的金色当前项和米色普通项视觉，播放、Queue、manifest、vcrid、所有权、Stop/End、弹幕/歌词 shader、下载、解析和同步主链路也保持正式 1.10 内容。该包没有经过 Unity/UdonSharp/VRChat 验收，不得提交、推送或发布；后续维护金黑分支必须以该 ZIP 为起点，不能从当前黑白 1.11 主仓库源码重新打包。
- 本地 `E:\paulkoiplayer\alpha1.0.zip` 是从黑白正式 1.11 新建的 URL Input 自动定位测试线，大小为 `3030428` 字节，SHA-256 为 `E849B4972237FCEEF1C07822F519E1B0A1BF494AA9A8F0AEB07FA3567AE7F20B`。根因是 1.11 的 `WireUrlPrefixHelper` 明确只保留手动 Top/Bottom 引用，完全没有自动查找代码。已用固定参考包 `net.kwxxw.yama-stream-1.5.18.zip` 静态核对：`UIController._urlInputFieldTop` 指向 `ScreenUI/Canvas/Control/Main/Top/UrlInput`，`UIController._urlInputField` 指向 `ScreenUI/Canvas/Control/Main/LeftSide/Container/UrlInput`。alpha1.0 优先读取这两个序列化字段，只在代理字段不可读时使用上述两条精确路径；只填补空引用，保留世界作者已有手动绑定，明确排除组件自己的 `Queue URL Input`，找不到完整 1.5.18 结构时给出警告并继续允许手动绑定。新建模块、Wire Selected Module、Wire Selected URL Prefix Helper 和 Apply Selected Bili UI Skin 都会经过同一绑定函数。相对 1.11 的 9 个包文件只有 `Editor/YamaBiliDanmakuRigBuilder3.cs` 不同，其余 Runtime、Pages、字体和 Shader 逐字节一致；未修改播放、Queue、manifest、弹幕/歌词、同步或渲染。本机没有 Unity，尚未验证 prefab 实例/场景覆盖下的实际序列化写回与事件响应，不得提交、推送或发布。
- 本地 `E:\paulkoiplayer\beta14.01.zip` 在金黑 `beta14.00.zip` 上加入与 alpha1.0 完全相同的 YamaPlayer 1.5.18 Top/Bottom URL Input 精确自动定位，大小为 `3030246` 字节，SHA-256 为 `A20865509EB5CF7C891902F7B784EA9BCB09ADD18373023E3A894DD70C08EC6E`。它同样优先读取 `UIController._urlInputFieldTop` / `_urlInputField`，再回退到两条已验证的精确 ScreenUI 路径，只填空引用、保留手动绑定并排除组件自己的 Queue 输入。相对 beta14.00 只有 `Editor/YamaBiliDanmakuRigBuilder3.cs` 不同；相对正式 1.10 也仍然只有该 Editor 文件不同。金色 `UiAccentColor`、米色 `UiTextColor`、`PanelSpritePixelsPerUnitMultiplier = 2f` 和 Black-gold Apply 文案均保留，`Runtime/YamaBiliPagesPlaylist3.cs` 与正式 1.10 逐字节一致，因此金色当前项/米色普通项列表、播放、Queue、manifest、弹幕/歌词、同步、字体和 Shader 均未改变。beta14.01 取代 beta14.00 成为当前金黑测试基线；本机没有 Unity，未经 Unity/UdonSharp/VRChat 验收不得提交、推送或发布。
- 本地 `E:\paulkoiplayer\alpha1.1.zip` 只修正 alpha1.0 与 YamaPlayer 点击灰色 UI 的渲染层级冲突：固定参考的 1.5.18 `ScreenUI/Canvas` 是 Order `0`，而 alpha1.0 的弹幕/网易云歌词根 Canvas 也为 `0`，两套透明 UI 因而会出现同级排序竞争。alpha1.1 仅把 `DanmakuCanvasSortingOrder` 从 `0` 改为 `-1`，新建模块和 Apply Selected Bili UI Skin 都使用新值；组件控制栏与播放列表子 Canvas 继续保持 `35/36`。相对 alpha1.0 的 9 个包文件只有 `Editor/YamaBiliDanmakuRigBuilder3.cs` 不同，把该行 `-1` 还原为 `0` 后 Editor 与 alpha1.0 逐字节一致；其余 8 个文件完全不变。大小为 `3030429` 字节，SHA-256 为 `CFC5FAC001AA34AA7D0AFF4311932B9F088ED38E2629EDA15C2F88F4A3DE2C33`。URL Input 自动定位、黑白 UI、白色列表、Runtime、Shader、材质、透明度、描边、镜子、下载、解析、播放和同步均未修改。本机没有 Unity，仍需验证视频播放时弹幕可见且点击 YamaPlayer 后灰色 UI 稳定覆盖弹幕；不得提交、推送或发布。
- 本地 `E:\paulkoiplayer\beta14.02.zip` 对金黑 beta14.01 应用完全相同且唯一的根 Canvas Order `0 → -1` 修正。相对 beta14.01 只有 `Editor/YamaBiliDanmakuRigBuilder3.cs` 不同，把该行还原后逐字节一致；其余 8 个包文件完全不变。大小为 `3030248` 字节，SHA-256 为 `047EAC2969F4B39942BDC1FF2FCBFFF99601CBFCCE0FB53A3B598AFDB91D2A7D`。金黑 UI、金色/米色正式 1.10 列表、URL Input 自动定位、控制栏/列表 Order `35/36`、Runtime、字体和两套 Shader 全部保持 beta14.01；beta14.02 取代 beta14.01 成为当前金黑测试基线。本机没有 Unity，未经 Unity/UdonSharp/VRChat 验收不得提交、推送或发布。
- 用户随后明确要求把 alpha1.1 原样升格为新的正式版 1.11，并明确授权提交、推送和创建 GitHub v1.11 Release。`E:\paulkoiplayer\1.11.zip`、`E:\paulkoiplayer\PaulKoiPlayer-YamaBiliDanmakuV3-1.11.zip` 与保留追溯的 `E:\paulkoiplayer\alpha1.1.zip` 是逐字节副本：大小均为 `3030429` 字节，SHA-256 均为 `CFC5FAC001AA34AA7D0AFF4311932B9F088ED38E2629EDA15C2F88F4A3DE2C33`，包内 9 个文件和路径完全相同。当前正式 1.11 因此同时包含针对原样 YamaPlayer 1.5.18 的 Top/Bottom URL Input 精确自动补齐和弹幕/歌词根 Canvas Order `-1` 修正；黑白 UI、白色列表、控制栏/列表 Order `35/36`、Runtime、Shader、材质、下载、解析、播放与同步仍与 alpha1.1 完全一致。GitHub v1.11 Release 只上传 `PaulKoiPlayer-YamaBiliDanmakuV3-1.11.zip`，继续使用现有 v1.0.3 server，不包含 iwaSync3、VizVid 或 Tablet 包。本机没有 Unity，不能宣称已经通过 Unity/UdonSharp/VRChat 验收。
- World Space Canvas 绕 X/Y 轴后从观察相机看起来变窄属于透视投影；生成器没有按旋转修改 RectTransform 宽度或 Scale。若实际世界尺寸也发生非预期变形，应检查所挂父级是否为非等比 Scale，并优先旋转整个 `Bili Danmaku Module`、保持它和父级三轴等比。不要为了抵消视觉透视引入 billboard、按视角放宽或运行时缩放，这会改变屏幕对齐并可能影响弹幕/歌词挂载。
- iwaSync3 和 VizVid 保持各自现有公开版本，尚未获得 YamaPlayer v1.10 的完整统一播放列表能力。Android / Quest 普通播放器继续使用 1.03；可拾取平板继续使用独立 `YamaBiliDanmakuTabletV3`，不能把 Tablet 的生成、缩放、材质或 Controller 绑定逻辑并回普通 PC 包。

### v1.10 / v1.11 继承的最终播放逻辑

- 普通单 P / 独立视频由 YamaPlayer Queue 管理，播放完成后按 YamaPlayer 原生行为消失。
- B 站多 P 使用轻量 manifest 数组，Yama Queue 中只放占位项目；P1 到 Pn 顺序播放，最后一 P 后继续后续普通 Queue，没有后续项目时停止，不默认回到 P1。多 P 清单不会因为播放完成而删除，只能由用户删除或清空。
- 网易云单曲可以作为普通 Queue 项目与 B 站视频共存。完整网易云歌单使用独立 manifest 模式；顺序播放会循环整张歌单，单项循环只循环当前歌曲。
- 面板每页只渲染 6 项。当前播放高亮以实际播放 `vcrid` 为准；用户正在浏览的页码是本地状态，首次解析、标题回填或其他玩家操作不得强制把它拉回首页。
- 正式 1.11 集成的“输入链接”输入框由生成器自动绑定到 Queue 输入和 Pages 组件；YamaPlayer 原有 Top/Bottom URL Input 仍是独立字段。当前正式 1.11 与 beta14.02 均保留针对原样 1.5.18 精确补齐空引用的逻辑，自定义/改名布局仍需世界作者手动拖入，任何版本都不能按组件顺序或宽泛名字猜测。
- 后端 `/player/?vcrid=<id>` 同时承担播放跳转和文本弹幕/歌词下载。运行时只能使用构建阶段预生成的 `_vcridUrls`，不能 `new VRCUrl(string)`。解析 `vcrid=` 时值从标记后第 6 个字符开始，绝不能再用 `+7` 截掉首位。
- 网易云歌词复用 `#YBDM/1` 和 `mode=4` 底部静态弹幕链路。外部音频模式由组件自动管理，只阻止错误请求原始歌单 JSON，不得暂停已经加载歌词的 Update、计时和渲染。
- 多人模式只同步 manifest 来源、模式、当前 vcrid、修订号等小状态；后来加入者重新下载完整 manifest。修改 Pages、Controller、Queue 或 History 前必须取得对应所有权，非 Owner 的 Stop/失败回调不能清空公共列表。

### 接手时必须保护的工作区

本轮结束时工作树不是干净的。`Editor/YamaBiliDanmakuRigBuilder3.cs` 当前保存由 alpha1.1 晋升而来的正式 1.11 Editor：包含 YamaPlayer 1.5.18 URL Input 精确自动定位，并把弹幕/歌词根 Canvas Order 设为 `-1`；`Runtime/YamaBiliPagesPlaylist3.cs` 保存本地正式 1.11 黑白列表源码；`YamaBiliDanmakuTabletV3/Editor/YamaBiliDanmakuTabletRigBuilder3.cs` 与 `YamaBiliDanmakuTabletV3/Runtime/YamaBiliDanmakuTabletModule3.cs` 也有未发布修改；`Textures/` 和测试截图是未跟踪文件。当前正式 1.11 完整内容保存在 `1.11.zip` 和 `PaulKoiPlayer-YamaBiliDanmakuV3-1.11.zip`，金黑 beta14.02 的完整分支内容保存在独立 ZIP，不应把仓库中的黑白 Editor 或黑白 Pages 当成 beta14.02 源码覆盖回包。新对话必须先执行 `git status --short` 和逐文件 diff，不能 `reset --hard`、不能 checkout 覆盖，也不能把这些文件顺手混入其他提交。

接手后的第一步应依次是：确认用户要处理的是本地正式 v1.11、GitHub 已发布 v1.10、金黑 beta14.02、Tablet、iwaSync3 还是 VizVid；读取本文件第 15 至 17 节；正式黑白基线从 `1.11.zip` 继续，当前仓库 Editor 与该正式包一致，alpha1.1 只作为其逐字节晋升来源留档，金黑分支从 `E:\paulkoiplayer\beta14.02.zip` 继续；解包并以 `E:\paulkoiplayer\net.kwxxw.yama-stream-1.5.18.zip` 核对 YamaPlayer API 和 prefab，禁止改用本地 2.0 beta 仓库；需要排查播放状态时先读取最新 VRChat Player log，再对照请求 URL、manifest、Queue、当前 vcrid、Owner、Stop/End 和 pending 标志。没有本机 Unity 环境时只能做源码、包结构和静态检查，不能把“能编译/能在 VRChat 运行”写成已经验证。

## 后续修改检查表

每次发布前至少确认：

1. Unity 与 UdonSharp 编译无错误。
2. U# Program Asset 与两个 Runtime 脚本正确关联。
3. 普通、彩色、顶部、底部和滚动弹幕均可见。
4. `Loaded` 后实际有文字进入 Text Pool 并显示。
5. 暂停、继续、拖动进度和换视频后时间同步正确。
6. 弹幕在画面边缘被裁切。
7. 修改视觉参数后执行 Apply 菜单并检查材质。
8. YamaPlayer 1.5.18 的 Top/Bottom URL 输入框能由生成器精确自动补齐；已有手动绑定不被覆盖，自定义/改名布局无法匹配时手动绑定正确，且两个字段都没有误指向组件自己的 Queue URL Input。
9. Docker `/health`、`/player/` 和强制 `__dm=1` 正常。
10. 如果服务端没有变化，Unity 组件 Release 只发布对应适配线的 Unity ZIP，并在 Release Notes 中说明继续使用当前 v1.0.3 server；不要把未同步功能的 iwaSync3、VizVid 或 Tablet 包混入 YamaPlayer Release。

## 不应重新引入的方案

- 不使用不存在的 `YamaPlayerListener` 或 `Controller.MirrorFlip`。
- 不假设旧版 `YamaPlayerModule` 类型仍存在。
- 不为描边复制整套 TextMeshPro 对象。
- 不在 Udon 运行时调用未经确认可暴露的 TMP API。
- 不把 UnityEvent 直接绑定到 UdonSharp C# proxy；需要绑定 backing `UdonBehaviour`。
- 不用 Toggle 表达 `Full/Half/1/4` 这种三态循环状态。
- 不在切换显示区域时调用 `HideAllTexts()` 清空已有弹幕。
- 不在 VizVid `URL Fill: Off` 时调用 `VRCUrlInputField.SetUrl(VRCUrl.Empty)` 清空输入框；这会触发 VizVid 的 URL 输入/播放提示流程。
- 不在弹幕关闭状态下每帧重复遍历并隐藏全部对象池。
- 不把 `_maxDanmakuLines` 降回 1600，除非重新验证高密度弹幕视频不会被截断。
- 不使用全局 `room=main` 保存当前视频。
- 不通过组件顺序、宽泛名称搜索或全场景第一个组件自动猜测 URL 输入框；仅允许使用当前固定参考 YamaPlayer 1.5.18 的 `UIController` 权威序列化字段和已验证精确路径，并保留手动绑定回退。
- 不让 YamaPlayer、iwaSync3、VizVid 三条适配线互相引用对方的播放器类型。
- 不在一次修改中同时替换下载、解析、同步和渲染四条路径。

这份记录应随架构变化继续更新。新功能不仅要记录“做了什么”，也要记录它保护了哪条稳定路径，以及验证过哪些失败场景。
