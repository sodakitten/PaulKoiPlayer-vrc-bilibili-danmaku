# PaulKoiPlayer

<p align="center">
  <img src="docs/images/paulkoiplayer-logo.png" alt="PaulKoiPlayer logo" width="220">
</p>

[简体中文](README.md) | [English](README_EN.md)

[vrc-bilibili-danmaku](https://github.com/sodakitten/PaulKoiPlayer-vrc-bilibili-danmaku) is the VRChat Bilibili danmaku component for **PaulKoiPlayer**. It provides Bilibili danmaku loading, synchronization, and rendering for video players in VRChat worlds.

Example world: [https://vrchat.com/home/world/wrld_c57b6e50-c63b-42d2-b30d-b76b0562f604](https://vrchat.com/home/world/wrld_c57b6e50-c63b-42d2-b30d-b76b0562f604)

The current official stable YamaPlayer PC / desktop release is **1.10**, promoted from the verified `beta13.43` build and published on GitHub. This 1.10 Release **contains only the YamaPlayer PC / desktop adapter package**; the iwaSync3 and VizVid lines remain on their current public versions. For Android / Quest fixed screens or non-pickup players, continue using **1.03**; pickup tablets use the separate `YamaBiliDanmakuTabletV3` package, which is not included in the 1.10 Release.

> This is not an official VRChat, Bilibili, or YamaPlayer component.

## Required before use

If your VRChat world was created with VCC, the project usually already includes the VRChat Worlds SDK and UdonSharp. In addition to those standard world dependencies, install the player required by the adapter you import:

- [YamaPlayer](https://github.com/koorimizuw/YamaPlayer): required by `YamaBiliDanmakuV3` and `YamaBiliDanmakuTabletV3`.
- iwaSync3: required by `IwaBiliDanmakuV3`.
- VizVid: required by `VizVidBiliDanmakuV3`.
- TextMeshPro: TMP Essentials must be imported in the Unity project.
- Docker: only required when self-hosting the `server/` backend. It is not required when using an existing public parser service.

If you do not want to self-host the backend, you can use the already deployed public service. Set `Bili URL Prefix Helper > Url Prefix` to:

```text
https://danmaku.paulkoishi.com/player/?url=
```

Public service status page: [https://danmaku.paulkoishi.com/](https://danmaku.paulkoishi.com/). If this page cannot be reached, the current public service is unavailable.

This public endpoint currently supports:

- Bilibili video resolution, with matching Bilibili danmaku returned to this component.
- Bilibili live stream resolution, without live danmaku for now.
- NetEase Cloud Music song resolution.
- NetEase Cloud Music playlist resolution. YamaPlayer 1.10 displays the playlist in-world and plays entries through backend `vcrid` URLs.
- Bilibili multi-part videos, collections, and lists. YamaPlayer 1.10 displays titles, switches entries, and supports continuous playback.

If you self-host this project's `server/` backend, replace the domain with your own domain to get the same resolver features.

## Repository layout

- `Runtime/` and `Editor/`: the YamaPlayer PC / desktop Unity/UdonSharp danmaku component.
- `IwaBiliDanmakuV3/`: dedicated iwaSync3 adapter line.
- `VizVidBiliDanmakuV3/`: dedicated VizVid adapter line.
- `YamaBiliDanmakuTabletV3/`: dedicated YamaPlayer Android / Quest pickup tablet adapter line.
- [`server/`](server/README_EN.md): the Dockerized media resolver and danmaku proxy.
- [`docs/DEVELOPMENT_NOTES.md`](docs/DEVELOPMENT_NOTES.md): development history, failed approaches, and fixes (Chinese).

## Downloads

Current local and public package status:

- `PaulKoiPlayer-YamaBiliDanmakuV3-1.10.zip`: official YamaPlayer PC / desktop source package and the only Unity adapter included in the 1.10 Release.
- `PaulKoiPlayer-IwaBiliDanmakuV3-1.04beta.zip`: iwaSync3 PC / desktop source package.
- `PaulKoiPlayer-VizVidBiliDanmakuV3-1.04beta.zip`: VizVid PC / desktop source package.
- `PaulKoiPlayer-YamaBiliDanmakuTabletV3-android-beta1.5.zip`: dedicated YamaPlayer Android / Quest pickup tablet package.

The server continues to use the v1.0.3 `server/` backend.

### Android / Quest note

YamaPlayer 1.10 retains the verified PC / desktop external-display mounting behavior: selecting an object inside the player mounts the module under the player root, while selecting an external tablet or display surface uses the selected display Transform.

For Android / Quest fixed screens, normal large screens, or non-pickup players, continue using 1.03. Only Android / Quest pickup tablets should import the separate `YamaBiliDanmakuTabletV3` package. It uses its own namespace, class names, shaders, and menu items. After generating the tablet module, manually drag the playback YamaPlayer `Controller` into the Inspector as the data source.

That PC mounting logic caused visibly dimmer danmaku on Android / Quest pickup tablets during testing. Keep the Android rule simple: normal Android players use 1.03; pickup tablets use the Tablet package.

## Features

- Requests danmaku for the URL currently loaded by the player
- Supports scrolling, top, and bottom danmaku
- Synchronizes with playback and correctly compensates for pause/resume time
- Clips danmaku at the video boundary with `RectMask2D`
- Supports colored text, black TMP outlines, and a light semibold style
- Configurable font scale, opacity, lanes, speed, and timing offset
- Updates only active danmaku entries to reduce per-frame work
- Optional custom parser prefix for player URL input fields
- Public enable, disable, and toggle events for custom world UI
- YamaPlayer 1.10 supports Bilibili multi-part videos/collections/lists and NetEase Cloud Music playlists, with up to six entries per page
- Sequential playback with list wraparound, single-entry looping, Home, Previous, and Next controls
- Lightweight manual Udon synchronization for multiplayer selection state and late-join list recovery

## Requirements

- A Unity version supported by the current VRChat Worlds SDK
- VRChat Worlds SDK, usually already included by a VCC World project
- UdonSharp, using the version integrated with the current VRChat Worlds SDK
- TextMeshPro Essentials
- The target player used by the imported adapter: YamaPlayer, iwaSync3, or VizVid
- A parser service that returns danmaku in `#YBDM/1` text format

Default parser prefix:

```text
https://danmaku.paulkoishi.com/player/?url=
```

## Installation

1. Use the local `1.10.zip` package for YamaPlayer on PC / desktop. iwaSync3 and VizVid remain on their current versions.
2. Remove old installations:
   - `Assets/YamaBiliDanmaku`
   - `Assets/YamaBiliDanmakuV2`
   - `Packages/yama-bili-danmaku*`
3. Place the `YamaBiliDanmakuV3` folder under your Unity project's `Assets/` directory.
4. Run `Assets > Reimport All`.
5. Run `Yamadev > YamaPlayer > Fix Bili Danmaku U# Program Asset`.
6. Run `UdonSharp > Compile All UdonSharp Programs`.
7. Select the target YamaPlayer and run `Yamadev > YamaPlayer > Create Bili Danmaku Module`.

![Create the danmaku module from the YamaPlayer context menu](docs/images/install-yamaplayer-menu.png)

## Manually assign the URL inputs

The two URL fields used by the prefix helper must be assigned **manually** in the Inspector. Do not rely on automatic lookup or component-order guessing. The hierarchy may differ between YamaPlayer releases and customized prefabs.

| Inspector field | Default YamaPlayer object path |
| --- | --- |
| `Top Url Input Field` | `ScreenUI/Canvas/Control/Main/Top/UrlInput` |
| `Bottom Url Input Field` | `ScreenUI/Canvas/Control/Main/LeftSide/Container/UrlInput` |

Drag the `VRC URL Input Field` component from each object into the corresponding field.

![Manually assign the two YamaPlayer URL input fields](docs/images/url-prefix-helper-bindings.png)

Prefix settings:

- `Enable Url Prefix On Input`: enables the prefix helper; on by default.
- `Url Prefix`: changes the parser service URL. If you are not self-hosting, use `https://danmaku.paulkoishi.com/player/?url=`.
- `Keep Prefix When Empty`: continuously restores empty inputs. Leave this off when players should be allowed to delete the prefix.

## Playing a video

Enter the following URL in YamaPlayer:

```text
https://danmaku.paulkoishi.com/player/?url=<Bilibili video URL>
```

You can also enter a Bilibili live stream URL, a NetEase song URL, or a NetEase playlist URL. For NetEase playlists, add `&p=<number>` to select the track:

```text
https://danmaku.paulkoishi.com/player/?url=<NetEase playlist URL>&p=1
```

The same endpoint returns video resolution data to the video player and danmaku text to `VRCStringDownloader`. No room name or world instance identifier is required.

## Common settings

> **These settings are configured in the Unity Editor only.** YamaPlayer 1.10 generates lightweight player controls and the playlist panel. Font size, opacity, weight, outline, lane count, scroll speed, and timing offset still need to be configured by the world author in the Unity Inspector and saved before uploading the world.

| Setting | Default | Description |
| --- | ---: | --- |
| `Lane Count` | 12 | Number of danmaku lanes |
| `Scroll Duration` | 8 | Seconds for scrolling text to cross the screen |
| `Static Duration` | 4 | Display time for top and bottom comments |
| `Time Offset Ms` | 0 | Danmaku timing correction |
| `Max Danmaku Lines` | 4096 | Maximum comments loaded per request |
| `Font Scale` | 1.1 | Text display scale |
| `Text Alpha` | 0.72 | Text opacity |
| `Outline Width` | 0.11 | Black TMP outline width |
| `Outline Alpha` | 0.7 | Outline opacity |

### Applying outline and font-weight changes

The bold, outline width, and outline alpha values under `Editor Visual Style` are editor-applied material settings, not live runtime properties. After changing an existing module, apply the values to its TextMeshPro material:

1. Select the generated `Bili Danmaku Module` object in the Hierarchy.
2. In `Yama Bili Danmaku Module 3 > Editor Visual Style`, change:
   - `Editor Bold Text`
   - `Editor Heavy Outline Enabled`
   - `Editor Outline Width`
   - `Editor Outline Alpha`
3. Keep that object selected and run:

```text
Yamadev > YamaPlayer > Apply Selected Bili Danmaku Visual Style
```

![Apply the danmaku outline and font-weight visual style](docs/images/apply-visual-style-menu.png)

4. Confirm the success message in the Console before entering Play Mode or uploading the world again.

Changing the Inspector values without running the Apply command does not update the material used by existing danmaku text objects, so the outline or weight may appear unchanged. Newly generated modules receive the current default style during creation.

## In-world danmaku controls

YamaPlayer 1.10 generates a lightweight Chinese control interface under `Bili Danmaku Module`:

- `弹幕范围：全屏 / 半屏 / 1/4屏`: cycles the danmaku display area.
- `弹幕：开启 / 关闭`: toggles danmaku visibility.
- `URL 回填：开启 / 关闭`: toggles URL-prefix filling.
- `播放列表`: shows Bilibili multi-part/list entries or NetEase playlist tracks and switches between sequential and single-entry looping.

Switching the display area does not clear danmaku already on screen. Only newly emitted danmaku uses the new area.

These Udon public events remain available for custom world UI or scripts:

```text
ToggleDanmaku
EnableDanmaku
DisableDanmaku
SetFullScreenDanmaku
SetHalfScreenDanmaku
SetQuarterScreenDanmaku
CycleDisplayAreaMode
```

## Troubleshooting

### No valid U# Program Asset

Run these commands in order:

```text
Yamadev > YamaPlayer > Fix Bili Danmaku U# Program Asset
UdonSharp > Compile All UdonSharp Programs
```

If it still fails, manually create a U# Script in `Assets/YamaBiliDanmakuV3/Runtime`, keep the generated `.asset`, and set its Source C# Script to `YamaBiliDanmakuModule3.cs`.

### Loaded is shown, but no danmaku appears

- Confirm that the current player URL uses a parser endpoint that serves danmaku responses.
- Verify the `Controller`, `Lane Root`, and `Text Pool` references.
- Confirm that `Danmaku Enabled` is enabled.
- Do not mix components or U# Program Assets from older releases.

## Current release

Compared with 1.04beta, YamaPlayer 1.10 adds:

- A generic `播放列表` panel for Bilibili multi-part videos, collections/lists, and NetEase Cloud Music playlists.
- Up to six entries per page with Home, Previous, Next, sequential-play, and single-entry-loop controls.
- Playback through backend-prebuilt `vcrid` URLs, avoiding runtime `VRCUrl` construction in Udon.
- Sequential playback wraps from the final item to the first; single-entry looping reuses YamaPlayer's own loop state.
- Protection against first-load, track-change, and stop-event races that could collapse a full playlist into one item.
- Lightweight multiplayer synchronization for the manifest source, current item, loop mode, and revision; late joiners can download the complete list.
- Non-owner stop events cannot clear the shared list, and the displayed current item follows the actual playing `vcrid`.
- Fixes truncated multi-digit `vcrid` values so entries such as P52 and P150 match the correct playing item.
- Preserves manual page browsing when initial list loading or queued-title resolution finishes instead of forcing the panel back to the first page.
- Chinese in-world controls while preserving the established danmaku download, parsing, timing, outline, mirror readability, and display-area behavior.

Version 1.10 publishes only the YamaPlayer PC / desktop adapter. The iwaSync3, VizVid, and Android / Quest tablet lines have not received this playlist feature set and are not included in this Release.

Compared with 1.03, 1.04beta updates the Unity component:

- Restores the verified PC / desktop external-display mounting behavior: selecting an object inside the player uses the player root; selecting an external tablet or display surface uses the selected display Transform.
- Keeps the same mounting rule across the YamaPlayer, iwaSync3, and VizVid PC adapter lines.
- Moves Android / Quest pickup tablet handling into the separate `YamaBiliDanmakuTabletV3` package instead of patching the normal YamaPlayer package.

Compared with 1.01, 1.02 updates the Unity component:

- Raises the default `Max Danmaku Lines` from 1600 to 4096, fixing dense danmaku videos that previously displayed only the opening section.
- Stops repeatedly hiding all text every frame while danmaku is disabled.
- Makes `HideAllTexts()` process only currently active danmaku entries instead of the full pool.
- Avoids unnecessary UI active-state changes when emitting a new danmaku entry.
- New modules no longer add an extra `GraphicRaycaster` to the main danmaku Canvas; running `Wire Selected Bili Danmaku Module` removes that extra main Canvas raycaster from older modules.

Compared with v1.0.0, 1.01 updates the Unity component:

- Adds a mirror-readable TextMeshPro danmaku shader. It follows YamaPlayer's `_MirrorFlip` + `_VRChatMirrorMode` mirror inversion pattern, pre-flips danmaku text during VRChat mirror rendering, and preserves the existing TMP outline, underlay, and light semibold styling.
- Adds `Danmaku Controls Canvas` with player-facing danmaku on/off and full/half/quarter display area buttons.
- Display area changes affect only newly emitted danmaku; active danmaku already on screen continues without being cleared.
- UI event binding now targets the backing `UdonBehaviour.SendCustomEvent`, matching YamaPlayer's event binding pattern.
- Fixes a brief edge flash that could happen when very long danmaku is emitted.

The `server/` backend did not change in 1.04beta. Continue using the v1.0.3 server.

v1.0.0 is the first unified release containing both the Unity component and its matching Docker server. The component includes colored TMP outlines, light semibold text, the URL prefix helper, active-entry update optimization, and pause/resume timing compensation.

## Acknowledgements and related links

- [danmaku.paulkoishi.com](https://danmaku.paulkoishi.com/): current public parser service status page. If it cannot be reached, the public service is unavailable.
- [koorimizuw/YamaPlayer](https://github.com/koorimizuw/YamaPlayer): the VRChat video player integrated and tested by YamaPlayer 1.10.
- [music.znnu.com](https://music.znnu.com/): the third-party service used by the server for NetEase Cloud Music resolution.
- [yionchi](https://github.com/yionchi): author of the related `music.znnu.com` service.

## Roadmap

Future releases will continue to add density/quantity controls, more display area options, and a compact VRChat-friendly settings panel. These additions will be built around the current stable loading, synchronization, and rendering path.
