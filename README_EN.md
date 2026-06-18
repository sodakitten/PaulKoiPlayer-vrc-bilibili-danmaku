# VRChat Bilibili Danmaku

[简体中文](README.md) | [English](README_EN.md)

A Bilibili danmaku loader, synchronizer, and renderer for video players in VRChat worlds.

The current stable release is **v1.0.0**. It is currently integrated and tested with **YamaPlayer**. The project uses a player-neutral name because support for additional VRChat video players is planned.

> This is not an official VRChat, Bilibili, or YamaPlayer component.

## Repository layout

- `Runtime/` and `Editor/`: the Unity/UdonSharp danmaku component.
- [`server/`](server/README_EN.md): the Dockerized media resolver and danmaku proxy.

## Downloads

Unified release page: [v1.0.0 Release](https://github.com/sodakitten/vrc-bilibili-danmaku/releases/tag/v1.0.0)

- `vrc-bilibili-danmaku-unity-v1.0.0.zip`: Unity/UdonSharp component.
- `vrc-bilibili-danmaku-server-v1.0.0.zip`: Docker server.

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

## Requirements

- A Unity version supported by the current VRChat Worlds SDK
- VRChat Worlds SDK, including UdonSharp
- TextMeshPro
- YamaPlayer, for the current v1.0.0 adapter
- A parser service that returns danmaku in `#YBDM/1` text format

Default parser prefix:

```text
https://danmaku.paulkoishi.com/player/?url=
```

## Installation

1. Download the Unity component from the [v1.0.0 release](https://github.com/sodakitten/vrc-bilibili-danmaku/releases/tag/v1.0.0).
2. Remove old installations:
   - `Assets/YamaBiliDanmaku`
   - `Assets/YamaBiliDanmakuV2`
   - `Packages/yama-bili-danmaku*`
3. Place the `YamaBiliDanmakuV3` folder under your Unity project's `Assets/` directory.
4. Run `Assets > Reimport All`.
5. Run `Yamadev > YamaPlayer > Fix Bili Danmaku U# Program Asset`.
6. Run `UdonSharp > Compile All UdonSharp Programs`.
7. Select the target YamaPlayer and run `Yamadev > YamaPlayer > Create Bili Danmaku Module`.

## Manually assign the URL inputs

The two URL fields used by the prefix helper must be assigned **manually** in the Inspector. Do not rely on automatic lookup or component-order guessing. The hierarchy may differ between YamaPlayer releases and customized prefabs.

| Inspector field | Default YamaPlayer object path |
| --- | --- |
| `Top Url Input Field` | `ScreenUI/Canvas/Control/Main/Top/UrlInput` |
| `Bottom Url Input Field` | `ScreenUI/Canvas/Control/Main/LeftSide/Container/UrlInput` |

Drag the `VRC URL Input Field` component from each object into the corresponding field.

Prefix settings:

- `Enable Url Prefix On Input`: enables the prefix helper; on by default.
- `Url Prefix`: changes the parser service URL.
- `Keep Prefix When Empty`: continuously restores empty inputs. Leave this off when players should be allowed to delete the prefix.

## Playing a video

Enter the following URL in YamaPlayer:

```text
https://danmaku.paulkoishi.com/player/?url=<Bilibili video URL>
```

The same endpoint returns video resolution data to the video player and danmaku text to `VRCStringDownloader`. No room name or world instance identifier is required.

## Common settings

> **These settings are configured in the Unity Editor only.** The current release does not generate an interactive settings panel inside the VRChat world. Font size, opacity, weight, outline, lane count, scroll speed, and timing offset must be configured by the world author in the Unity Inspector and saved before uploading the world.

| Setting | Default | Description |
| --- | ---: | --- |
| `Lane Count` | 12 | Number of danmaku lanes |
| `Scroll Duration` | 8 | Seconds for scrolling text to cross the screen |
| `Static Duration` | 4 | Display time for top and bottom comments |
| `Time Offset Ms` | 0 | Danmaku timing correction |
| `Max Danmaku Lines` | 1600 | Maximum comments loaded per request |
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

4. Confirm the success message in the Console before entering Play Mode or uploading the world again.

Changing the Inspector values without running the Apply command does not update the material used by existing danmaku text objects, so the outline or weight may appear unchanged. Newly generated modules receive the current default style during creation.

## Custom danmaku toggle

The current release does not automatically generate a player-facing danmaku toggle or any other interactive control panel. These are Udon public events for world authors:

```text
ToggleDanmaku
EnableDanmaku
DisableDanmaku
```

To let players toggle danmaku in the world, the world author must create a Button or Toggle in Unity and manually bind its event to the danmaku module. Without that setup, the uploaded world has no interactive danmaku controls.

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

v1.0.0 is the first unified release containing both the Unity component and its matching Docker server. The component includes colored TMP outlines, light semibold text, the URL prefix helper, active-entry update optimization, and pause/resume timing compensation.
