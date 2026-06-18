# Yama Bili Danmaku V3

This build avoids the old package asmdef/U# assembly issues and uses a unique namespace:
`YamaBiliDanmakuV3.YamaBiliDanmakuModule3`.

Install:
1. Delete old folders: `Assets/YamaBiliDanmaku`, `Assets/YamaBiliDanmakuV2`, and any `Packages/yama-bili-danmaku*` folders.
2. Put this `YamaBiliDanmakuV3` folder directly under your Unity project's `Assets/` folder.
3. Run `Assets > Reimport All`.
4. Run `Yamadev > YamaPlayer > Fix Bili Danmaku U# Program Asset`.
5. Run `UdonSharp > Compile All UdonSharp Programs`.
6. Run `Yamadev > YamaPlayer > Create Bili Danmaku Module`.

If the auto fix fails, manually create a U# Script in `Assets/YamaBiliDanmakuV3/Runtime`, keep the generated `.asset`, and set its Source C# Script to `YamaBiliDanmakuModule3.cs`.


V3 fontfix: Removed runtime TextMeshProUGUI.fontSize / preferredWidth / ForceMeshUpdate calls because they are not exposed to Udon in some SDK versions. Font size is approximated through RectTransform.localScale and text width estimation.

V4 status/control: Status text now auto-hides after 2 seconds. The module exposes `ToggleDanmaku()`, `EnableDanmaku()`, and `DisableDanmaku()` for custom world UI wiring, but no built-in toggle button is generated.

V7 unified player URL: New rigs enable `Load From Current YamaPlayer Url` by default. The module reads the bound YamaPlayer's current `VRCUrl` and requests that same URL for danmaku. This is intended for server endpoints like `https://danmaku.paulkoishi.com/player/?url=...` that return video redirects for video players and `#YBDM/1` text for `VRCStringDownloader`. `Fallback Danmaku Url` is still supported as a backup.

V7.1 UdonSharp fix: Removed C# pattern matching from current URL detection because some UdonSharp compiler versions crash on `object is VRCUrl` expressions.

V7.2 render polish: `Danmaku Lanes` now uses `RectMask2D` so text is clipped at the screen edge. Default danmaku font size is slightly larger, and runtime text alpha defaults to 0.64.

V7.3 performance: Active danmaku texts are tracked in a compact active-index list, so each frame updates only visible/moving danmaku instead of scanning the whole text pool. Timing and frame rate behavior are unchanged.

V7.4 alpha: Default danmaku text alpha and outline alpha are now 0.72.

V7.5 sizing/alpha: Default danmaku text alpha and outline alpha are now 0.66. Newly generated canvases use 1700x925 instead of 1600x900.

V7.6 alpha control: `Text Alpha` is exposed under Appearance for Inspector tuning, and `SetDanmakuAlpha(float)` can update active danmaku opacity at runtime.

V7.7 defaults: Newly generated canvases use 1750x980 by default, and newly generated modules default `Font Scale` to 1.1.

V7.8 URL prefix helper: Newly generated rigs include `Bili URL Prefix Helper`, which fills an empty YamaPlayer URL field with `https://danmaku.paulkoishi.com/player/?url=` when a player clicks/selects the URL input. Players can manually delete the prefix; `Enable URL Prefix On Input` controls this feature and is on by default. `Keep Prefix When Empty` is off by default.

V7.9 prefix settings: `Bili URL Prefix Helper` exposes `Enable URL Prefix On Input` and editable `Url Prefix` fields in the Inspector, so the click-to-fill behavior can be disabled or pointed at another domain without code changes. `Url Prefix` is stored as a `VRCUrl` for UdonSharp compatibility.

V7.10 Udon URL fix: Removed runtime `new VRCUrl(string)` usage from `Bili URL Prefix Helper`; UdonSharp does not expose that constructor. The prefix is now stored directly as a serialized `VRCUrl`.

V7.11 uploaded-world prefix fix: `Bili URL Prefix Helper` now also watches URL input field activation and fills the prefix once when a YamaPlayer input panel opens. This keeps the click-to-fill behavior working in uploaded VRChat worlds even when UI pointer/select events are not delivered reliably.

V7.12 editor visual style switches: Adds Inspector fields under `Editor Visual Style` for bold text and TMP outline strength. Runtime render logic is unchanged except for these serialized settings fields; the actual TMP style is applied by the Unity editor when creating/wiring the module. Heavy outline is enabled by default, while bold text is off by default to keep the danmaku from becoming too thick. After changing style settings on an existing module, select it and run `Yamadev > YamaPlayer > Apply Selected Bili Danmaku Visual Style`.

Stable 4: Keeps the third-build renderer path, soft default style, and editor-applied heavy outline. The URL prefix helper now refills the prefix when a YamaPlayer URL input changes from non-empty to empty, which covers cases where the player clears the input field after playback starts.

TMP outline fix: The editor visual style now creates/updates `Assets/YamaBiliDanmakuV3/Materials/Bili Danmaku TMP Outline.mat`, enables the `OUTLINE_ON` shader keyword, writes `_OutlineWidth` and `_OutlineColor`, sets `extraPadding`, and calls `UpdateMeshPadding()` on every `Danmaku Text`. This is the actual TextMeshPro outline path; Unity UI `Outline` components are not used.

Bilibili-style semibold outline: Default text alpha is `0.72`, outline width is `0.11`, outline alpha stays `0.7`, material face dilate is `0.012`, and TMP bold is enabled with a low material `_WeightBold = 0.28`. This makes the glyphs slightly heavier without using the overly thick default TMP bold weight.

Colored danmaku outline fix: The TMP material also enables `UNDERLAY_ON` with zero offset, black underlay color, `Underlay Dilate = 0.16`, and `Underlay Softness = 0.03`. This adds a black backing layer inside the same TMP shader so colored danmaku, such as red text, has visible edge contrast without creating duplicate TextMeshPro objects.

Pause resume fix: Active danmaku timers now compensate for the amount of real time spent paused. This prevents visible danmaku from jumping forward when YamaPlayer resumes playback after a pause.

## URL prefix helper setup

Assign YamaPlayer's two `VRC URL Input Field` components manually in the Inspector:

- `Top Url Input Field`: `ScreenUI/Canvas/Control/Main/Top/UrlInput`
- `Bottom Url Input Field`: `ScreenUI/Canvas/Control/Main/LeftSide/Container/UrlInput`

Do not rely on component-order auto-detection. Different YamaPlayer versions and customized prefabs can use a different hierarchy or component order.
