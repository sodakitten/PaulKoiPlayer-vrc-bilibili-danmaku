using System.IO;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;
using VRC.SDK3.Components;
using VRC.Udon;
using Yamadev.YamaStream;

namespace YamaBiliDanmakuV3.Editor
{
  public static class YamaBiliDanmakuRigBuilder3
  {
    private const int PoolSize = 96;
    private const int PagesButtonCount = 6;
    private const float CanvasWidth = 1750f;
    private const float CanvasHeight = 980f;
    private const string DefaultUrlPrefix = "https://danmaku.paulkoishi.com/player/?url=";
    private const string DefaultPagesApiPrefix = DefaultUrlPrefix;
    private const string DefaultVcridUrlPrefix = "https://danmaku.paulkoishi.com/player/?vcrid=";
    private const int DefaultVcridMax = 1000000;
    private const string DefaultPrefabPath = "Assets/YamaBiliDanmakuV3/Prefabs/Bili Danmaku Module.prefab";
    private const string OutlineMaterialPath = "Assets/YamaBiliDanmakuV3/Materials/Bili Danmaku TMP Outline.mat";
    private const string ButtonMaterialPath = "Assets/YamaBiliDanmakuV3/Materials/Bili Danmaku UI Button.mat";
    private const string LegacyYouYuanFontAssetPath = "Assets/YamaBiliDanmakuV3/Generated/UI/YouYuan UI SDF.asset";
    private const string GeneratedUiFontAssetPath = "Assets/YamaBiliDanmakuV3/Generated/UI/PaulKoi UI SDF.asset";
    private const string BundledUiFontSourcePath = "Assets/YamaBiliDanmakuV3/Fonts/NotoSansSC-PaulKoiUI.otf";
    private const string YamaUiFontSourcePath = "Packages/net.kwxxw.yama-stream/Assets/Fonts/ZenMaruGothic-Regular.ttf";
    private const string MirrorReadableShaderName = "YamaBiliDanmaku/TMP Mirror Readable";
    private const string ButtonShaderName = "YamaBiliDanmaku/UI Button";
    private const string ControlsCanvasName = "Danmaku Controls Canvas";
    private const float ControlsWidth = 420f;
    private const float ControlsHeight = 70f;
    private const float ControlsPadding = 6f;
    private const float ControlButtonWidth = 132f;
    private const float ControlButtonHeight = 58f;
    private const float ControlButtonGap = 6f;
    private const float ControlsLocalZ = -8f;
    private const string PagesPanelName = "Bili Pages Panel";
    private const float PagesPanelWidth = 420f;
    private const float PagesPanelHeight = 258f;
    private const float PagesPanelLocalZ = -8f;
    private const float PagesHeaderActionWidth = 68f;
    private const float PagesHeaderActionHeight = 37f;
    private const float PagesHeaderRightPadding = 14f;
    private const float PagesHeaderGap = 14f;
    private const float PagesHeaderContentWidth = PagesPanelWidth - 24f - PagesHeaderActionWidth - PagesHeaderRightPadding - PagesHeaderGap;
    private const string RoundedSpriteGuid = "cb6af20af8ed3f6438490dcef842bdde";
    private const string DisplayAreaIconGuid = "23aa86979de8cf94db763545b72e0c9b";
    private const string DanmakuIconGuid = "49e3e4d5882a85e4d8416c06d008c51f";
    private const string UrlFillIconGuid = "ab97ea5771096a348a20dec1cac271dd";
    private const string HomeIconGuid = "b2a21f86f3a3fd740befddbb73f80870";
    private const string RepeatIconGuid = "e91c0974ef297f4499ff1d41531c4143";
    private const string PreviousIconGuid = "ef9c13e59c2ab294a9c68c20e6f618bf";
    private const string NextIconGuid = "4742737fe3b86d04d9a66a7fbdf4b7bb";
    private const string DeleteIconGuid = "34a3c976457fe7447aeadabe9d1566dc";
    private const float DefaultOutlineWidth = 0.11f;
    private const float DefaultOutlineAlpha = 0.7f;
    private const float DefaultFaceDilate = 0.012f;
    private const float DefaultWeightNormal = 0f;
    private const float DefaultWeightBold = 0.28f;
    private const float DefaultUnderlayDilate = 0.16f;
    private const float DefaultUnderlaySoftness = 0.03f;
    private const string RequiredUiCharacters = "播放列表队首页顺序单项循环上一下弹幕区域全屏半四分之显示开关链接填充等待正在加载解析入删除保留终止第共视频已返回暂无持当前内容没有条展超时按未找到器控制切换为即将失败识别歌曲信息读取空目网易云后续最多所选效缺少地址录不存限变化请重试从同步清中名命可用的该称并稍候经0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz-/:：·，。！？（）()[]<>｜ ";

    private static readonly Color UiAccentColor = new Color(200f / 255f, 168f / 255f, 128f / 255f, 1f);
    private static readonly Color UiTextColor = new Color(222f / 255f, 207f / 255f, 185f / 255f, 0.96f);
    private static readonly Color UiMutedTextColor = new Color(200f / 255f, 180f / 255f, 154f / 255f, 0.76f);
    private static readonly Color UiButtonColor = new Color(200f / 255f, 168f / 255f, 128f / 255f, 0.07f);
    private static readonly Color UiDividerColor = new Color(200f / 255f, 168f / 255f, 128f / 255f, 0.28f);

    [MenuItem("Yamadev/YamaPlayer/Create Bili Danmaku Module", false, 2000)]
    public static void CreateRig()
    {
      Controller controller = FindTargetController();
      if (controller == null)
      {
        EditorUtility.DisplayDialog("Yama Bili Danmaku", "Select a YamaPlayer object, or an object under YamaPlayer that contains a Controller.", "OK");
        return;
      }

      Transform parent = ResolveRigParent(controller);
      GameObject root = BuildRig(controller, parent);
      if (root == null) return;
      Undo.RegisterCreatedObjectUndo(root, "Create Bili Danmaku Module");

      Selection.activeObject = root;
      EditorGUIUtility.PingObject(root);
    }

    [MenuItem("GameObject/YamaPlayer/Bili Danmaku Module", false, 41)]
    public static void CreateRigFromGameObjectMenu()
    {
      CreateRig();
    }

    [MenuItem("GameObject/YamaPlayer/Bili Danmaku Module", true)]
    public static bool ValidateCreateRigFromGameObjectMenu()
    {
      return FindTargetController() != null;
    }

    [MenuItem("Yamadev/YamaPlayer/Save Selected Bili Danmaku Module As Prefab", false, 2001)]
    public static void SaveSelectedAsPrefab()
    {
      GameObject selected = Selection.activeGameObject;
      if (selected == null || !HasDanmakuModule(selected))
      {
        EditorUtility.DisplayDialog("Yama Bili Danmaku", "Select a Bili Danmaku Module object first.", "OK");
        return;
      }

      string path = EditorUtility.SaveFilePanelInProject(
        "Save Bili Danmaku Prefab",
        "Bili Danmaku Module",
        "prefab",
        "Choose where to save the prefab.",
        "Assets/YamaBiliDanmakuV3/Prefabs");

      if (string.IsNullOrEmpty(path)) return;

      EnsureDirectoryForAsset(path);
      GameObject prefab = PrefabUtility.SaveAsPrefabAsset(selected, path);
      if (prefab == null)
      {
        EditorUtility.DisplayDialog("Yama Bili Danmaku", "Failed to save prefab.", "OK");
        return;
      }

      Selection.activeObject = prefab;
      EditorGUIUtility.PingObject(prefab);
    }

    [MenuItem("Yamadev/YamaPlayer/Create Package Prefab Asset", false, 2002)]
    public static void CreatePrefabAsset()
    {
      string path = DefaultPrefabPath;
      EnsureDirectoryForAsset(path);

      GameObject temp = BuildRig(null, null);
      if (temp == null) return;
      try
      {
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(temp, path);
        if (prefab == null)
        {
          EditorUtility.DisplayDialog("Yama Bili Danmaku", "Failed to create prefab asset.", "OK");
          return;
        }

        Selection.activeObject = prefab;
        EditorGUIUtility.PingObject(prefab);
      }
      finally
      {
        UnityEngine.Object.DestroyImmediate(temp);
      }
    }

    private static GameObject BuildRig(Controller controller, Transform parent)
    {
      GameObject root = new GameObject("Bili Danmaku Module", typeof(RectTransform));
      Undo.RegisterCreatedObjectUndo(root, "Create Bili Danmaku Module");
      if (parent != null) root.transform.SetParent(parent, false);
      root.transform.localPosition = new Vector3(0f, 0f, -0.02f);
      root.transform.localRotation = Quaternion.identity;
      root.transform.localScale = Vector3.one;

      Canvas canvas = root.AddComponent<Canvas>();
      canvas.renderMode = RenderMode.WorldSpace;
      canvas.sortingOrder = 20;
      CanvasScaler scaler = root.AddComponent<CanvasScaler>();
      scaler.dynamicPixelsPerUnit = 10f;

      RectTransform canvasRect = root.GetComponent<RectTransform>();
      canvasRect.sizeDelta = new Vector2(CanvasWidth, CanvasHeight);
      canvasRect.localScale = Vector3.one * 0.001f;

      GameObject laneObject = new GameObject("Danmaku Lanes", typeof(RectTransform), typeof(RectMask2D));
      laneObject.transform.SetParent(root.transform, false);
      RectTransform laneRoot = laneObject.GetComponent<RectTransform>();
      laneRoot.anchorMin = Vector2.zero;
      laneRoot.anchorMax = Vector2.one;
      laneRoot.offsetMin = Vector2.zero;
      laneRoot.offsetMax = Vector2.zero;

      TextMeshProUGUI[] pool = new TextMeshProUGUI[PoolSize];
      for (int i = 0; i < PoolSize; i++)
      {
        GameObject textObject = new GameObject("Danmaku Text " + i, typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(laneObject.transform, false);
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(1400f, 60f);

        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.raycastTarget = false;
        text.alignment = TextAlignmentOptions.Center;
        text.fontSize = 32f;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Overflow;
        text.extraPadding = true;
        text.outlineWidth = DefaultOutlineWidth;
        text.outlineColor = new Color(0f, 0f, 0f, DefaultOutlineAlpha);
        text.gameObject.SetActive(false);
        pool[i] = text;
      }

      GameObject statusObject = new GameObject("Status", typeof(RectTransform), typeof(TextMeshProUGUI));
      statusObject.transform.SetParent(root.transform, false);
      RectTransform statusRect = statusObject.GetComponent<RectTransform>();
      statusRect.anchorMin = new Vector2(0f, 1f);
      statusRect.anchorMax = new Vector2(0f, 1f);
      statusRect.pivot = new Vector2(0f, 1f);
      statusRect.anchoredPosition = new Vector2(16f, -16f);
      statusRect.sizeDelta = new Vector2(700f, 44f);
      TextMeshProUGUI status = statusObject.GetComponent<TextMeshProUGUI>();
      status.raycastTarget = false;
      status.alignment = TextAlignmentOptions.Left;
      status.fontSize = 22f;
      status.color = new Color(1f, 1f, 1f, 0.75f);
      status.text = "idle";
      statusObject.SetActive(false);

      Button displayAreaButton;
      TextMeshProUGUI displayAreaButtonLabel;
      Button danmakuToggleButton;
      TextMeshProUGUI danmakuToggleButtonLabel;
      Button urlPrefixToggleButton;
      TextMeshProUGUI urlPrefixToggleButtonLabel;
      CreateOrFindControlsCanvas(root, out displayAreaButton, out displayAreaButtonLabel, out danmakuToggleButton, out danmakuToggleButtonLabel, out urlPrefixToggleButton, out urlPrefixToggleButtonLabel);

      System.Type moduleType = FindType("YamaBiliDanmakuV3.YamaBiliDanmakuModule3");
      if (moduleType == null)
      {
        EditorUtility.DisplayDialog(
          "Yama Bili Danmaku",
          "YamaBiliDanmakuModule3 type was not found. Make sure the Runtime script has compiled successfully, then reimport this package or restart Unity.",
          "OK");
        UnityEngine.Object.DestroyImmediate(root);
        return null;
      }

      EnsureUdonSharpProgramAsset(moduleType);
      Component module = AddUdonSharpComponentForType(root, moduleType);
      if (module == null)
      {
        EditorUtility.DisplayDialog(
          "Yama Bili Danmaku",
          "Failed to add UdonSharp component. Try creating a U# Program Asset manually from Assets/YamaBiliDanmakuV3/Runtime/YamaBiliDanmakuModule3.cs, then run this menu again.",
          "OK");
        UnityEngine.Object.DestroyImmediate(root);
        return null;
      }

      Component urlPrefixHelper = CreateOrFindUrlPrefixHelper(root, controller, urlPrefixToggleButtonLabel);
      WireModule(module, controller, laneRoot, status, displayAreaButtonLabel, danmakuToggleButtonLabel, displayAreaButton, danmakuToggleButton, pool);
      WireModuleUrlPrefixControls(module, urlPrefixHelper, urlPrefixToggleButtonLabel, urlPrefixToggleButton);
      Component pagesPlaylist = CreateOrFindPagesPlaylist(root, controller, module);
      WirePagesPlaylist(pagesPlaylist, controller, module);
      ApplyChineseUiFont(root);

      return root;
    }

    private static Component CreateOrFindUrlPrefixHelper(GameObject root, Controller controller, TextMeshProUGUI urlPrefixToggleButtonLabel)
    {
      System.Type helperType = FindType("YamaBiliDanmakuV3.YamaBiliUrlPrefixHelper3");
      if (helperType == null)
      {
        Debug.LogWarning("Yama Bili Danmaku: YamaBiliUrlPrefixHelper3 type was not found. URL prefix helper was not created.");
        return null;
      }

      EnsureUdonSharpProgramAsset(helperType);

      Component helper = root == null ? null : root.GetComponentInChildren(helperType, true);
      if (helper == null)
      {
        GameObject helperObject = new GameObject("Bili URL Prefix Helper");
        helperObject.transform.SetParent(root.transform, false);

        helper = AddUdonSharpComponentForType(helperObject, helperType);
        if (helper == null)
        {
          Debug.LogWarning("Yama Bili Danmaku: failed to add Bili URL Prefix Helper.");
          UnityEngine.Object.DestroyImmediate(helperObject);
          return null;
        }
      }

      WireUrlPrefixHelper(helper, controller, urlPrefixToggleButtonLabel);
      return helper;
    }

    private static Component CreateOrFindPagesPlaylist(GameObject root, Controller controller, Component module)
    {
      if (root == null) return null;

      System.Type playlistType = FindType("YamaBiliDanmakuV3.YamaBiliPagesPlaylist3");
      if (playlistType == null)
      {
        Debug.LogWarning("Yama Bili Danmaku: YamaBiliPagesPlaylist3 type was not found. Pages panel was not created.");
        return null;
      }

      EnsureUdonSharpProgramAsset(playlistType);

      Transform panelTransform = root.transform.Find(PagesPanelName);
      GameObject panelObject = panelTransform == null ? null : panelTransform.gameObject;
      if (panelObject == null)
      {
        panelObject = new GameObject(PagesPanelName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        panelObject.transform.SetParent(root.transform, false);
      }

      panelObject.layer = 0;
      RectTransform panelRect = panelObject.GetComponent<RectTransform>();
      panelRect.anchorMin = new Vector2(1f, 1f);
      panelRect.anchorMax = new Vector2(1f, 1f);
      panelRect.pivot = new Vector2(0f, 1f);
      panelRect.anchoredPosition = new Vector2(24f, -100f);
      panelRect.sizeDelta = new Vector2(PagesPanelWidth, PagesPanelHeight);
      Vector3 localPosition = panelRect.localPosition;
      localPosition.z = PagesPanelLocalZ;
      panelRect.localPosition = localPosition;
      panelRect.localRotation = Quaternion.identity;
      panelRect.localScale = Vector3.one;

      Canvas canvas = panelObject.GetComponent<Canvas>();
      if (canvas == null) canvas = panelObject.AddComponent<Canvas>();
      canvas.renderMode = RenderMode.WorldSpace;
      canvas.overrideSorting = true;
      canvas.sortingOrder = 36;

      CanvasScaler scaler = panelObject.GetComponent<CanvasScaler>();
      if (scaler == null) scaler = panelObject.AddComponent<CanvasScaler>();
      scaler.dynamicPixelsPerUnit = 10f;

      GraphicRaycaster raycaster = panelObject.GetComponent<GraphicRaycaster>();
      if (raycaster == null) raycaster = panelObject.AddComponent<GraphicRaycaster>();
      raycaster.ignoreReversedGraphics = true;

      EnsureVrcUiShape(panelObject);
      BoxCollider collider = panelObject.GetComponent<BoxCollider>();
      if (collider == null) collider = panelObject.AddComponent<BoxCollider>();
      collider.isTrigger = true;
      collider.center = new Vector3(PagesPanelWidth * 0.5f, -PagesPanelHeight * 0.5f, 0f);
      collider.size = new Vector3(PagesPanelWidth + 24f, PagesPanelHeight + 24f, 2f);

      CreateOrFindStyledBackground(panelObject);
      RectTransform titleRect;
      RectTransform titleViewportRect;
      TextMeshProUGUI titleLabel = CreateOrFindMarqueeTitle(panelObject, out titleRect, out titleViewportRect);
      TextMeshProUGUI statusLabel = CreateOrFindPanelLabel(panelObject, "Status", new Vector2(18f, -37f), new Vector2(PagesHeaderContentWidth + 6f, 17f), "等待播放队列", 12f, TextAlignmentOptions.Left);
      Button clearQueueButton;
      TextMeshProUGUI clearQueueLabel;
      CreateOrFindPanelButton(
        panelObject,
        "Clear Queue Button",
        new Vector2(PagesPanelWidth - PagesHeaderRightPadding - PagesHeaderActionWidth, -12f),
        PagesHeaderActionWidth,
        PagesHeaderActionHeight,
        "清空",
        11f,
        out clearQueueButton,
        out clearQueueLabel,
        DeleteIconGuid);

      TextMeshProUGUI[] pageLabels = new TextMeshProUGUI[PagesButtonCount];
      Button[] pageButtons = new Button[PagesButtonCount];
      Button[] deleteButtons = new Button[PagesButtonCount];
      Image[] deleteIcons = new Image[PagesButtonCount];
      for (int i = 0; i < PagesButtonCount; i++)
      {
        Button button;
        TextMeshProUGUI label;
        string buttonName = "Page Button " + i;
        CleanupPageItemMarquee(panelObject, buttonName);
        CreateOrFindPanelButton(panelObject, buttonName, new Vector2(14f, -58f - i * 25f), PagesPanelWidth - 28f, 22f, "--", 13f, out button, out label);
        RectTransform labelRect = label.GetComponent<RectTransform>();
        labelRect.offsetMax = new Vector2(-31f, labelRect.offsetMax.y);
        CreateOrFindDeleteControl(button.gameObject, out deleteButtons[i], out deleteIcons[i]);
        pageButtons[i] = button;
        pageLabels[i] = label;
      }

      Button previousButton;
      TextMeshProUGUI previousLabel;
      Button nextButton;
      TextMeshProUGUI nextLabel;
      Button refreshButton;
      TextMeshProUGUI refreshLabel;
      Button playModeButton;
      TextMeshProUGUI playModeLabel;
      CreateOrFindNavigationShell(panelObject);
      CreateOrFindPanelButton(panelObject, "Refresh Button", new Vector2(14f, -218f), 98f, 28f, "首页", 13f, out refreshButton, out refreshLabel, HomeIconGuid, true);
      CreateOrFindPanelButton(panelObject, "Play Mode Button", new Vector2(112f, -218f), 98f, 28f, "顺序播放", 13f, out playModeButton, out playModeLabel, RepeatIconGuid, true);
      CreateOrFindPanelButton(panelObject, "Previous Button", new Vector2(210f, -218f), 98f, 28f, "上一页", 13f, out previousButton, out previousLabel, PreviousIconGuid, true);
      CreateOrFindPanelButton(panelObject, "Next Button", new Vector2(308f, -218f), 98f, 28f, "下一页", 13f, out nextButton, out nextLabel, NextIconGuid, true, true);
      CreateOrFindSeparator(panelObject, "Navigation Divider 1", new Vector2(112f, -232f), new Vector2(1f, 16f));
      CreateOrFindSeparator(panelObject, "Navigation Divider 2", new Vector2(210f, -232f), new Vector2(1f, 16f));
      CreateOrFindSeparator(panelObject, "Navigation Divider 3", new Vector2(308f, -232f), new Vector2(1f, 16f));

      Component playlist = panelObject.GetComponent(playlistType);
      if (playlist == null)
      {
        playlist = AddUdonSharpComponentForType(panelObject, playlistType);
      }

      if (playlist == null)
      {
        Debug.LogWarning("Yama Bili Danmaku: failed to add Pages playlist.");
        return null;
      }

      WirePagesPlaylistReferences(playlist, controller, module, titleLabel, titleRect, titleViewportRect, statusLabel, playModeLabel, pageLabels, deleteIcons);
      AddModuleButtonClick(previousButton, playlist, "PreviousPage");
      AddModuleButtonClick(nextButton, playlist, "NextPage");
      AddModuleButtonClick(refreshButton, playlist, "RefreshPages");
      AddModuleButtonClick(playModeButton, playlist, "TogglePlaybackMode");
      AddModuleButtonClick(clearQueueButton, playlist, "ClearUnifiedQueue");
      for (int i = 0; i < PagesButtonCount; i++)
      {
        AddModuleButtonClick(pageButtons[i], playlist, "SelectPage" + i);
        AddModuleButtonClick(deleteButtons[i], playlist, "DeleteVisible" + i);
      }

      SetLayerRecursively(panelObject, 0);
      ApplyChineseUiFontToObject(panelObject, FindChineseUiFont());
      return playlist;
    }

    private static TextMeshProUGUI CreateOrFindPanelLabel(GameObject panelObject, string objectName, Vector2 anchoredPosition, Vector2 size, string text, float fontSize, TextAlignmentOptions alignment)
    {
      GameObject labelObject = CreateOrFindChild(panelObject, objectName, typeof(RectTransform), typeof(TextMeshProUGUI));
      RectTransform rect = labelObject.GetComponent<RectTransform>();
      rect.anchorMin = new Vector2(0f, 1f);
      rect.anchorMax = new Vector2(0f, 1f);
      rect.pivot = new Vector2(0f, 1f);
      rect.anchoredPosition = anchoredPosition;
      rect.sizeDelta = size;
      rect.localRotation = Quaternion.identity;
      rect.localScale = Vector3.one;

      TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
      label.raycastTarget = false;
      label.alignment = alignment;
      label.fontSize = fontSize;
      label.fontStyle = FontStyles.Bold;
      label.enableWordWrapping = false;
      label.overflowMode = TextOverflowModes.Ellipsis;
      label.richText = false;
      label.parseCtrlCharacters = false;
      label.color = UiMutedTextColor;
      label.text = text;
      return label;
    }

    private static void CreateOrFindPanelButton(GameObject panelObject, string objectName, Vector2 anchoredPosition, float width, float height, string labelText, float fontSize, out Button button, out TextMeshProUGUI label, string iconGuid = null, bool navigationStyle = false, bool iconOnRight = false)
    {
      button = null;
      label = null;
      if (panelObject == null) return;

      Transform buttonTransform = panelObject.transform.Find(objectName);
      GameObject buttonObject = buttonTransform == null ? null : buttonTransform.gameObject;
      if (buttonObject == null)
      {
        buttonObject = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(panelObject.transform, false);
      }

      RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
      buttonRect.anchorMin = new Vector2(0f, 1f);
      buttonRect.anchorMax = new Vector2(0f, 1f);
      buttonRect.pivot = new Vector2(0f, 1f);
      buttonRect.anchoredPosition = anchoredPosition;
      buttonRect.sizeDelta = new Vector2(width, height);
      buttonRect.localRotation = Quaternion.identity;
      buttonRect.localScale = Vector3.one;

      Image image = buttonObject.GetComponent<Image>();
      if (image == null) image = buttonObject.AddComponent<Image>();
      image.color = navigationStyle ? new Color(UiAccentColor.r, UiAccentColor.g, UiAccentColor.b, 0.025f) : UiButtonColor;
      image.raycastTarget = true;
      image.sprite = navigationStyle ? null : GetRoundedUiSprite();
      image.type = image.sprite == null ? Image.Type.Simple : Image.Type.Sliced;
      image.pixelsPerUnitMultiplier = navigationStyle ? 1f : 2f;
      Material material = GetOrCreateButtonMaterial();
      if (material != null) image.material = material;

      button = buttonObject.GetComponent<Button>();
      if (button == null) button = buttonObject.AddComponent<Button>();
      button.interactable = true;
      button.targetGraphic = image;
      button.transition = Selectable.Transition.ColorTint;
      ColorBlock colors = button.colors;
      colors.normalColor = Color.white;
      colors.highlightedColor = new Color(1f, 0.9f, 0.74f, 1f);
      colors.pressedColor = new Color(0.82f, 0.68f, 0.52f, 1f);
      colors.selectedColor = colors.highlightedColor;
      colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
      button.colors = colors;
      Navigation navigation = button.navigation;
      navigation.mode = Navigation.Mode.None;
      button.navigation = navigation;

      Transform labelTransform = buttonObject.transform.Find("Label");
      GameObject labelObject = labelTransform == null ? null : labelTransform.gameObject;
      if (labelObject == null)
      {
        labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(buttonObject.transform, false);
      }

      RectTransform labelRect = labelObject.GetComponent<RectTransform>();
      labelRect.anchorMin = Vector2.zero;
      labelRect.anchorMax = Vector2.one;
      labelRect.offsetMin = string.IsNullOrEmpty(iconGuid) || iconOnRight ? new Vector2(5f, 0f) : new Vector2(25f, 0f);
      labelRect.offsetMax = iconOnRight ? new Vector2(-25f, 0f) : new Vector2(-5f, 0f);
      labelRect.localRotation = Quaternion.identity;
      labelRect.localScale = Vector3.one;

      label = labelObject.GetComponent<TextMeshProUGUI>();
      label.raycastTarget = false;
      label.alignment = TextAlignmentOptions.Center;
      label.fontSize = fontSize;
      label.fontStyle = FontStyles.Bold;
      label.enableWordWrapping = false;
      label.overflowMode = TextOverflowModes.Ellipsis;
      label.richText = false;
      label.parseCtrlCharacters = false;
      label.enableAutoSizing = navigationStyle;
      label.fontSizeMin = 9f;
      label.fontSizeMax = fontSize;
      label.color = UiTextColor;
      label.text = labelText;
      CreateOrFindButtonIcon(buttonObject, iconGuid, navigationStyle ? 14f : 18f, navigationStyle ? 7f : 8f, iconOnRight);
    }

    private static void CreateOrFindDeleteControl(GameObject rowObject, out Button deleteButton, out Image deleteIcon)
    {
      deleteButton = null;
      deleteIcon = null;
      if (rowObject == null) return;

      GameObject hotspotObject = CreateOrFindChild(rowObject, "Delete Hotspot", typeof(RectTransform), typeof(Image), typeof(Button));
      hotspotObject.transform.SetAsLastSibling();
      RectTransform hotspotRect = hotspotObject.GetComponent<RectTransform>();
      hotspotRect.anchorMin = new Vector2(1f, 0f);
      hotspotRect.anchorMax = new Vector2(1f, 1f);
      hotspotRect.pivot = new Vector2(1f, 0.5f);
      hotspotRect.anchoredPosition = Vector2.zero;
      hotspotRect.sizeDelta = new Vector2(30f, 0f);
      hotspotRect.localRotation = Quaternion.identity;
      hotspotRect.localScale = Vector3.one;

      Image hotspotImage = hotspotObject.GetComponent<Image>();
      hotspotImage.sprite = null;
      hotspotImage.type = Image.Type.Simple;
      hotspotImage.color = new Color(0f, 0f, 0f, 0f);
      hotspotImage.raycastTarget = true;

      deleteButton = hotspotObject.GetComponent<Button>();
      deleteButton.targetGraphic = hotspotImage;
      deleteButton.transition = Selectable.Transition.None;
      deleteButton.interactable = true;
      Navigation navigation = deleteButton.navigation;
      navigation.mode = Navigation.Mode.None;
      deleteButton.navigation = navigation;

      GameObject iconObject = CreateOrFindChild(hotspotObject, "Icon", typeof(RectTransform), typeof(Image));
      RectTransform iconRect = iconObject.GetComponent<RectTransform>();
      iconRect.anchorMin = new Vector2(0.5f, 0.5f);
      iconRect.anchorMax = new Vector2(0.5f, 0.5f);
      iconRect.pivot = new Vector2(0.5f, 0.5f);
      iconRect.anchoredPosition = Vector2.zero;
      iconRect.sizeDelta = new Vector2(13f, 13f);
      iconRect.localRotation = Quaternion.identity;
      iconRect.localScale = Vector3.one;

      deleteIcon = iconObject.GetComponent<Image>();
      deleteIcon.sprite = LoadSpriteByGuid(DeleteIconGuid);
      deleteIcon.preserveAspect = true;
      deleteIcon.raycastTarget = false;
      deleteIcon.color = UiAccentColor;
      Material material = GetOrCreateButtonMaterial();
      if (material != null) deleteIcon.material = material;
      iconObject.SetActive(deleteIcon.sprite != null);
    }

    private static void AddHoverCustomEvents(GameObject target, Component receiver, string enterEvent, string exitEvent)
    {
      if (target == null || receiver == null) return;
      UdonSharpBehaviour behaviour = receiver as UdonSharpBehaviour;
      if (behaviour == null) return;

      EventTrigger trigger = target.GetComponent<EventTrigger>();
      if (trigger == null) trigger = target.AddComponent<EventTrigger>();
      if (trigger.triggers == null) trigger.triggers = new System.Collections.Generic.List<EventTrigger.Entry>();
      AddPrefixInputEventTrigger(trigger, EventTriggerType.PointerEnter, behaviour, enterEvent);
      AddPrefixInputEventTrigger(trigger, EventTriggerType.PointerExit, behaviour, exitEvent);
      EditorUtility.SetDirty(trigger);
    }

    private static TextMeshProUGUI CreateOrFindMarqueeTitle(GameObject panelObject, out RectTransform titleRect, out RectTransform viewportRect)
    {
      titleRect = null;
      viewportRect = null;
      if (panelObject == null) return null;

      Transform legacyTitle = panelObject.transform.Find("Title");
      if (legacyTitle != null) UnityEngine.Object.DestroyImmediate(legacyTitle.gameObject);

      GameObject viewportObject = CreateOrFindChild(panelObject, "Title Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
      DestroyComponent<RectMask2D>(viewportObject);
      viewportRect = viewportObject.GetComponent<RectTransform>();
      viewportRect.anchorMin = new Vector2(0f, 1f);
      viewportRect.anchorMax = new Vector2(0f, 1f);
      viewportRect.pivot = new Vector2(0f, 1f);
      viewportRect.anchoredPosition = new Vector2(24f, -12f);
      viewportRect.sizeDelta = new Vector2(PagesHeaderContentWidth, 24f);
      viewportRect.localRotation = Quaternion.identity;
      viewportRect.localScale = Vector3.one;

      Image viewportImage = viewportObject.GetComponent<Image>();
      viewportImage.sprite = null;
      viewportImage.material = GetOrCreateButtonMaterial();
      viewportImage.color = Color.white;
      viewportImage.raycastTarget = false;
      Mask viewportMask = viewportObject.GetComponent<Mask>();
      viewportMask.showMaskGraphic = false;

      GameObject titleObject = CreateOrFindChild(viewportObject, "Title", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(ContentSizeFitter));
      titleRect = titleObject.GetComponent<RectTransform>();
      titleRect.anchorMin = new Vector2(0f, 0f);
      titleRect.anchorMax = new Vector2(0f, 1f);
      titleRect.pivot = new Vector2(0f, 1f);
      titleRect.anchoredPosition = Vector2.zero;
      titleRect.sizeDelta = new Vector2(0f, 0f);
      titleRect.localRotation = Quaternion.identity;
      titleRect.localScale = Vector3.one;

      ContentSizeFitter fitter = titleObject.GetComponent<ContentSizeFitter>();
      fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
      fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

      TextMeshProUGUI titleLabel = titleObject.GetComponent<TextMeshProUGUI>();
      titleLabel.raycastTarget = false;
      titleLabel.alignment = TextAlignmentOptions.Left;
      titleLabel.fontSize = 17f;
      titleLabel.fontStyle = FontStyles.Bold;
      titleLabel.enableWordWrapping = false;
      titleLabel.overflowMode = TextOverflowModes.Overflow;
      titleLabel.richText = false;
      titleLabel.parseCtrlCharacters = false;
      titleLabel.maskable = true;
      titleLabel.color = UiAccentColor;
      titleLabel.text = "播放队列";
      return titleLabel;
    }

    private static void CleanupPageItemMarquee(GameObject panelObject, string buttonName)
    {
      if (panelObject == null) return;
      Transform button = panelObject.transform.Find(buttonName);
      if (button == null) return;

      Transform prefix = button.Find("Prefix");
      if (prefix != null) UnityEngine.Object.DestroyImmediate(prefix.gameObject);
      Transform viewport = button.Find("Title Viewport");
      if (viewport != null) UnityEngine.Object.DestroyImmediate(viewport.gameObject);
    }

    private static void CreateOrFindControlsCanvas(GameObject root, out Button displayAreaButton, out TextMeshProUGUI displayAreaButtonLabel, out Button danmakuToggleButton, out TextMeshProUGUI danmakuToggleButtonLabel, out Button urlPrefixToggleButton, out TextMeshProUGUI urlPrefixToggleButtonLabel)
    {
      displayAreaButton = null;
      displayAreaButtonLabel = null;
      danmakuToggleButton = null;
      danmakuToggleButtonLabel = null;
      urlPrefixToggleButton = null;
      urlPrefixToggleButtonLabel = null;
      if (root == null) return;

      CleanupLegacyRootButton(root, "Display Area Button");
      CleanupLegacyRootButton(root, "Danmaku Toggle Button");
      CleanupLegacyRootButton(root, "URL Prefix Toggle Button");

      Transform controlsTransform = root.transform.Find(ControlsCanvasName);
      GameObject controlsObject = controlsTransform == null ? null : controlsTransform.gameObject;
      if (controlsObject == null)
      {
        controlsObject = new GameObject(ControlsCanvasName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        controlsObject.transform.SetParent(root.transform, false);
      }

      controlsObject.layer = 0;
      RectTransform controlsRect = controlsObject.GetComponent<RectTransform>();
      controlsRect.anchorMin = new Vector2(1f, 1f);
      controlsRect.anchorMax = new Vector2(1f, 1f);
      controlsRect.pivot = new Vector2(0f, 1f);
      controlsRect.anchoredPosition = new Vector2(24f, -18f);
      controlsRect.sizeDelta = new Vector2(ControlsWidth, ControlsHeight);
      Vector3 localPosition = controlsRect.localPosition;
      localPosition.z = ControlsLocalZ;
      controlsRect.localPosition = localPosition;
      controlsRect.localRotation = Quaternion.identity;
      controlsRect.localScale = Vector3.one;

      Canvas canvas = controlsObject.GetComponent<Canvas>();
      if (canvas == null) canvas = controlsObject.AddComponent<Canvas>();
      canvas.renderMode = RenderMode.WorldSpace;
      canvas.overrideSorting = true;
      canvas.sortingOrder = 35;

      CanvasScaler scaler = controlsObject.GetComponent<CanvasScaler>();
      if (scaler == null) scaler = controlsObject.AddComponent<CanvasScaler>();
      scaler.dynamicPixelsPerUnit = 10f;

      GraphicRaycaster raycaster = controlsObject.GetComponent<GraphicRaycaster>();
      if (raycaster == null) raycaster = controlsObject.AddComponent<GraphicRaycaster>();
      raycaster.ignoreReversedGraphics = true;

      EnsureVrcUiShape(controlsObject);
      BoxCollider collider = controlsObject.GetComponent<BoxCollider>();
      if (collider == null) collider = controlsObject.AddComponent<BoxCollider>();
      collider.isTrigger = true;
      collider.center = new Vector3(ControlsWidth * 0.5f, -ControlsHeight * 0.5f, 0f);
      collider.size = new Vector3(ControlsWidth + 24f, ControlsHeight + 24f, 2f);

      CreateOrFindStyledBackground(controlsObject);
      CreateOrFindCycleButton(controlsObject, "Display Area Button", new Vector2(ControlsPadding, -ControlsPadding), "弹幕区域：全屏", DisplayAreaIconGuid, out displayAreaButton, out displayAreaButtonLabel);
      CreateOrFindCycleButton(controlsObject, "Danmaku Toggle Button", new Vector2(ControlsPadding + ControlButtonWidth + ControlButtonGap, -ControlsPadding), "弹幕显示：开", DanmakuIconGuid, out danmakuToggleButton, out danmakuToggleButtonLabel);
      Transform legacyUrlToggle = controlsObject.transform.Find("URL Prefix Toggle Button");
      if (legacyUrlToggle != null) UnityEngine.Object.DestroyImmediate(legacyUrlToggle.gameObject);
      CreateOrFindQueueUrlInput(controlsObject);
      CreateOrFindSeparator(controlsObject, "Control Divider 1", new Vector2(141f, -35f), new Vector2(1f, 34f));
      CreateOrFindSeparator(controlsObject, "Control Divider 2", new Vector2(279f, -35f), new Vector2(1f, 34f));
      SetLayerRecursively(controlsObject, 0);
      ApplyChineseUiFontToObject(controlsObject, FindChineseUiFont());
    }

    private static VRCUrlInputField CreateOrFindQueueUrlInput(GameObject controlsObject)
    {
      if (controlsObject == null) return null;

      GameObject inputObject = CreateOrFindChild(controlsObject, "Queue URL Input", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(VRCUrlInputField));
      DestroyComponent<RectMask2D>(inputObject);
      RectTransform inputRect = inputObject.GetComponent<RectTransform>();
      inputRect.anchorMin = new Vector2(0f, 1f);
      inputRect.anchorMax = new Vector2(0f, 1f);
      inputRect.pivot = new Vector2(0f, 1f);
      inputRect.anchoredPosition = new Vector2(ControlsPadding + (ControlButtonWidth + ControlButtonGap) * 2f, -ControlsPadding);
      inputRect.sizeDelta = new Vector2(ControlButtonWidth, ControlButtonHeight);
      inputRect.localRotation = Quaternion.identity;
      inputRect.localScale = Vector3.one;

      Image background = inputObject.GetComponent<Image>();
      background.sprite = null;
      background.type = Image.Type.Simple;
      background.color = new Color(UiAccentColor.r, UiAccentColor.g, UiAccentColor.b, 0.025f);
      background.raycastTarget = true;
      Material material = GetOrCreateButtonMaterial();
      if (material != null) background.material = material;

      Mask inputMask = inputObject.GetComponent<Mask>();
      inputMask.showMaskGraphic = true;

      GameObject textObject = CreateOrFindChild(inputObject, "Text", typeof(RectTransform), typeof(Text));
      RectTransform textRect = textObject.GetComponent<RectTransform>();
      textRect.anchorMin = new Vector2(0f, 0f);
      textRect.anchorMax = new Vector2(1f, 0f);
      textRect.pivot = new Vector2(0.5f, 0f);
      textRect.anchoredPosition = new Vector2(0f, 4f);
      textRect.sizeDelta = new Vector2(-8f, 20f);
      textRect.localRotation = Quaternion.identity;
      textRect.localScale = Vector3.one;

      Font legacyFont = FindLegacyUiFont();
      Text inputText = textObject.GetComponent<Text>();
      inputText.font = legacyFont;
      inputText.fontSize = 12;
      inputText.fontStyle = FontStyle.Bold;
      inputText.alignment = TextAnchor.MiddleCenter;
      inputText.horizontalOverflow = HorizontalWrapMode.Overflow;
      inputText.verticalOverflow = VerticalWrapMode.Truncate;
      inputText.supportRichText = false;
      inputText.color = UiTextColor;
      inputText.raycastTarget = false;
      inputText.text = "";

      GameObject placeholderObject = CreateOrFindChild(inputObject, "Placeholder", typeof(RectTransform), typeof(Text));
      RectTransform placeholderRect = placeholderObject.GetComponent<RectTransform>();
      placeholderRect.anchorMin = new Vector2(0f, 0f);
      placeholderRect.anchorMax = new Vector2(1f, 0f);
      placeholderRect.pivot = new Vector2(0.5f, 0f);
      placeholderRect.anchoredPosition = new Vector2(0f, 4f);
      placeholderRect.sizeDelta = new Vector2(-8f, 20f);
      placeholderRect.localRotation = Quaternion.identity;
      placeholderRect.localScale = Vector3.one;

      Text placeholder = placeholderObject.GetComponent<Text>();
      placeholder.font = legacyFont;
      placeholder.fontSize = 12;
      placeholder.fontStyle = FontStyle.Bold;
      placeholder.alignment = TextAnchor.MiddleCenter;
      placeholder.horizontalOverflow = HorizontalWrapMode.Overflow;
      placeholder.verticalOverflow = VerticalWrapMode.Truncate;
      placeholder.supportRichText = false;
      placeholder.color = UiAccentColor;
      placeholder.raycastTarget = false;
      placeholder.text = "视频链接";

      GameObject idleLabelObject = CreateOrFindChild(inputObject, "Idle Label", typeof(RectTransform), typeof(Text));
      RectTransform idleLabelRect = idleLabelObject.GetComponent<RectTransform>();
      idleLabelRect.anchorMin = new Vector2(0f, 0f);
      idleLabelRect.anchorMax = new Vector2(1f, 0f);
      idleLabelRect.pivot = new Vector2(0.5f, 0f);
      idleLabelRect.anchoredPosition = new Vector2(0f, 4f);
      idleLabelRect.sizeDelta = new Vector2(-8f, 20f);
      idleLabelRect.localRotation = Quaternion.identity;
      idleLabelRect.localScale = Vector3.one;

      Text idleLabel = idleLabelObject.GetComponent<Text>();
      idleLabel.font = legacyFont;
      idleLabel.fontSize = 12;
      idleLabel.fontStyle = FontStyle.Bold;
      idleLabel.alignment = TextAnchor.MiddleCenter;
      idleLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
      idleLabel.verticalOverflow = VerticalWrapMode.Truncate;
      idleLabel.supportRichText = false;
      idleLabel.color = UiAccentColor;
      idleLabel.raycastTarget = false;
      idleLabel.text = "视频链接";

      GameObject iconObject = CreateOrFindChild(inputObject, "Icon", typeof(RectTransform), typeof(Image));
      RectTransform iconRect = iconObject.GetComponent<RectTransform>();
      iconRect.anchorMin = new Vector2(0.5f, 1f);
      iconRect.anchorMax = new Vector2(0.5f, 1f);
      iconRect.pivot = new Vector2(0.5f, 1f);
      iconRect.anchoredPosition = new Vector2(0f, -5f);
      iconRect.sizeDelta = new Vector2(26f, 26f);
      iconRect.localRotation = Quaternion.identity;
      iconRect.localScale = Vector3.one;

      Image icon = iconObject.GetComponent<Image>();
      icon.sprite = LoadSpriteByGuid(UrlFillIconGuid);
      icon.color = UiAccentColor;
      icon.raycastTarget = false;
      icon.preserveAspect = true;
      if (material != null) icon.material = material;
      iconObject.SetActive(icon.sprite != null);

      VRCUrlInputField inputField = inputObject.GetComponent<VRCUrlInputField>();
      SerializedObject serialized = new SerializedObject(inputField);
      SerializedProperty targetGraphic = serialized.FindProperty("m_TargetGraphic");
      if (targetGraphic != null) targetGraphic.objectReferenceValue = background;
      SerializedProperty textComponent = serialized.FindProperty("m_TextComponent");
      if (textComponent != null) textComponent.objectReferenceValue = inputText;
      SerializedProperty placeholderProperty = serialized.FindProperty("m_Placeholder");
      if (placeholderProperty != null) placeholderProperty.objectReferenceValue = placeholder;
      SetBool(serialized, "m_Interactable", true);
      SetBool(serialized, "m_ReadOnly", false);
      SetBool(serialized, "m_ShouldActivateOnSelect", true);
      SetBool(serialized, "AllowSendingOnEndEdit", true);
      SetInt(serialized, "m_LineType", 0);
      serialized.ApplyModifiedPropertiesWithoutUndo();

      SetLayerRecursively(inputObject, 0);
      return inputField;
    }

    private static Font FindLegacyUiFont()
    {
      Font bundledFont = FindUiSourceFont();
      if (bundledFont != null) return bundledFont;
      return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    private static Font FindUiSourceFont()
    {
      Font bundledFont = AssetDatabase.LoadAssetAtPath<Font>(BundledUiFontSourcePath);
      if (bundledFont != null) return bundledFont;

      Font yamaFont = AssetDatabase.LoadAssetAtPath<Font>(YamaUiFontSourcePath);
      if (yamaFont != null) return yamaFont;

      string[] guids = AssetDatabase.FindAssets("ZenMaruGothic-Regular t:Font");
      for (int i = 0; i < guids.Length; i++)
      {
        string path = AssetDatabase.GUIDToAssetPath(guids[i]);
        Font font = AssetDatabase.LoadAssetAtPath<Font>(path);
        if (font != null) return font;
      }
      return null;
    }

    private static void CreateOrFindStyledBackground(GameObject controlsObject)
    {
      GameObject backgroundObject = CreateOrFindChild(controlsObject, "BG", typeof(RectTransform));
      backgroundObject.transform.SetAsFirstSibling();
      RectTransform backgroundRect = backgroundObject.GetComponent<RectTransform>();
      backgroundRect.anchorMin = Vector2.zero;
      backgroundRect.anchorMax = Vector2.one;
      backgroundRect.offsetMin = Vector2.zero;
      backgroundRect.offsetMax = Vector2.zero;
      DestroyComponent<RawImage>(backgroundObject);

      Sprite rounded = GetRoundedUiSprite();
      Material material = GetOrCreateButtonMaterial();
      Image frame = backgroundObject.GetComponent<Image>();
      if (frame == null) frame = backgroundObject.AddComponent<Image>();
      frame.sprite = rounded;
      frame.type = rounded == null ? Image.Type.Simple : Image.Type.Sliced;
      frame.pixelsPerUnitMultiplier = 2f;
      frame.color = UiAccentColor;
      frame.raycastTarget = true;
      if (material != null) frame.material = material;

      GameObject fillObject = CreateOrFindChild(backgroundObject, "Fill", typeof(RectTransform), typeof(Image));
      RectTransform fillRect = fillObject.GetComponent<RectTransform>();
      fillRect.anchorMin = Vector2.zero;
      fillRect.anchorMax = Vector2.one;
      fillRect.offsetMin = new Vector2(2f, 2f);
      fillRect.offsetMax = new Vector2(-2f, -2f);
      fillRect.localRotation = Quaternion.identity;
      fillRect.localScale = Vector3.one;

      Image fill = fillObject.GetComponent<Image>();
      fill.sprite = rounded;
      fill.type = rounded == null ? Image.Type.Simple : Image.Type.Sliced;
      fill.pixelsPerUnitMultiplier = 2f;
      fill.color = new Color(0.012f, 0.011f, 0.016f, 0.985f);
      fill.raycastTarget = false;
      if (material != null) fill.material = material;
    }

    private static void CreateOrFindCycleButton(GameObject controlsObject, string objectName, Vector2 anchoredPosition, string labelText, string iconGuid, out Button button, out TextMeshProUGUI label)
    {
      button = null;
      label = null;
      if (controlsObject == null) return;

      Transform buttonTransform = controlsObject.transform.Find(objectName);
      GameObject buttonObject = buttonTransform == null ? null : buttonTransform.gameObject;
      if (buttonObject == null)
      {
        buttonObject = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(controlsObject.transform, false);
      }

      DestroyComponent<Toggle>(buttonObject);
      Transform oldBackground = buttonObject.transform.Find("Background");
      if (oldBackground != null) UnityEngine.Object.DestroyImmediate(oldBackground.gameObject);

      RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
      buttonRect.anchorMin = new Vector2(0f, 1f);
      buttonRect.anchorMax = new Vector2(0f, 1f);
      buttonRect.pivot = new Vector2(0f, 1f);
      buttonRect.anchoredPosition = anchoredPosition;
      buttonRect.sizeDelta = new Vector2(ControlButtonWidth, ControlButtonHeight);
      buttonRect.localRotation = Quaternion.identity;
      buttonRect.localScale = Vector3.one;

      Image image = buttonObject.GetComponent<Image>();
      if (image == null) image = buttonObject.AddComponent<Image>();
      image.color = new Color(UiAccentColor.r, UiAccentColor.g, UiAccentColor.b, 0.025f);
      image.raycastTarget = true;
      image.sprite = null;
      image.type = Image.Type.Simple;
      Material material = GetOrCreateButtonMaterial();
      if (material != null) image.material = material;

      button = buttonObject.GetComponent<Button>();
      if (button == null) button = buttonObject.AddComponent<Button>();
      button.interactable = true;
      button.targetGraphic = image;
      button.transition = Selectable.Transition.ColorTint;
      ColorBlock colors = button.colors;
      colors.normalColor = Color.white;
      colors.highlightedColor = new Color(1f, 0.9f, 0.74f, 1f);
      colors.pressedColor = new Color(0.82f, 0.68f, 0.52f, 1f);
      colors.selectedColor = colors.highlightedColor;
      colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
      button.colors = colors;
      Navigation navigation = button.navigation;
      navigation.mode = Navigation.Mode.None;
      button.navigation = navigation;

      Transform labelTransform = buttonObject.transform.Find("Label");
      GameObject labelObject = labelTransform == null ? null : labelTransform.gameObject;
      if (labelObject == null)
      {
        labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(buttonObject.transform, false);
      }

      RectTransform labelRect = labelObject.GetComponent<RectTransform>();
      labelRect.anchorMin = new Vector2(0f, 0f);
      labelRect.anchorMax = new Vector2(1f, 0f);
      labelRect.pivot = new Vector2(0.5f, 0f);
      labelRect.anchoredPosition = new Vector2(0f, 4f);
      labelRect.sizeDelta = new Vector2(-8f, 20f);
      labelRect.localRotation = Quaternion.identity;
      labelRect.localScale = Vector3.one;

      label = labelObject.GetComponent<TextMeshProUGUI>();
      label.raycastTarget = false;
      label.alignment = TextAlignmentOptions.Center;
      label.fontSize = 12f;
      label.fontStyle = FontStyles.Bold;
      label.enableWordWrapping = false;
      label.overflowMode = TextOverflowModes.Ellipsis;
      label.richText = false;
      label.parseCtrlCharacters = false;
      label.enableAutoSizing = true;
      label.fontSizeMin = 9f;
      label.fontSizeMax = 12f;
      label.color = UiAccentColor;
      label.text = labelText;
      CreateOrFindControlIcon(buttonObject, iconGuid);
    }

    private static void CreateOrFindControlIcon(GameObject buttonObject, string iconGuid)
    {
      GameObject iconObject = CreateOrFindChild(buttonObject, "Icon", typeof(RectTransform), typeof(Image));
      RectTransform iconRect = iconObject.GetComponent<RectTransform>();
      iconRect.anchorMin = new Vector2(0.5f, 1f);
      iconRect.anchorMax = new Vector2(0.5f, 1f);
      iconRect.pivot = new Vector2(0.5f, 1f);
      iconRect.anchoredPosition = new Vector2(0f, -5f);
      iconRect.sizeDelta = new Vector2(26f, 26f);
      iconRect.localRotation = Quaternion.identity;
      iconRect.localScale = Vector3.one;

      Image icon = iconObject.GetComponent<Image>();
      icon.sprite = LoadSpriteByGuid(iconGuid);
      icon.color = UiAccentColor;
      icon.raycastTarget = false;
      icon.preserveAspect = true;
      Material material = GetOrCreateButtonMaterial();
      if (material != null) icon.material = material;
      iconObject.SetActive(icon.sprite != null);
    }

    private static void CreateOrFindButtonIcon(GameObject buttonObject, string iconGuid, float size, float inset, bool iconOnRight)
    {
      Transform existing = buttonObject.transform.Find("Icon");
      if (string.IsNullOrEmpty(iconGuid))
      {
        if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);
        return;
      }

      GameObject iconObject = existing == null ? CreateOrFindChild(buttonObject, "Icon", typeof(RectTransform), typeof(Image)) : existing.gameObject;
      if (iconObject.GetComponent<Image>() == null) iconObject.AddComponent<Image>();
      RectTransform iconRect = iconObject.GetComponent<RectTransform>();
      iconRect.anchorMin = new Vector2(iconOnRight ? 1f : 0f, 0.5f);
      iconRect.anchorMax = new Vector2(iconOnRight ? 1f : 0f, 0.5f);
      iconRect.pivot = new Vector2(iconOnRight ? 1f : 0f, 0.5f);
      iconRect.anchoredPosition = new Vector2(iconOnRight ? -inset : inset, 0f);
      iconRect.sizeDelta = new Vector2(size, size);
      iconRect.localRotation = Quaternion.identity;
      iconRect.localScale = Vector3.one;

      Image icon = iconObject.GetComponent<Image>();
      icon.sprite = LoadSpriteByGuid(iconGuid);
      bool mirrorNextIcon = icon.sprite == null && iconGuid == PreviousIconGuid;
      if (mirrorNextIcon) icon.sprite = LoadSpriteByGuid(NextIconGuid);
      iconRect.localScale = mirrorNextIcon ? new Vector3(-1f, 1f, 1f) : Vector3.one;
      icon.color = UiAccentColor;
      icon.raycastTarget = false;
      icon.preserveAspect = true;
      Material material = GetOrCreateButtonMaterial();
      if (material != null) icon.material = material;
      iconObject.SetActive(icon.sprite != null);
    }

    private static void CreateOrFindNavigationShell(GameObject panelObject)
    {
      Sprite rounded = GetRoundedUiSprite();
      Material material = GetOrCreateButtonMaterial();
      GameObject frameObject = CreateOrFindChild(panelObject, "Navigation Frame", typeof(RectTransform), typeof(Image));
      GameObject fillObject = CreateOrFindChild(panelObject, "Navigation BG", typeof(RectTransform), typeof(Image));
      frameObject.transform.SetSiblingIndex(Mathf.Min(1, panelObject.transform.childCount - 1));
      fillObject.transform.SetSiblingIndex(Mathf.Min(2, panelObject.transform.childCount - 1));

      ConfigureNavigationShellImage(frameObject, new Vector2(12f, -216f), new Vector2(PagesPanelWidth - 24f, 32f), rounded, UiAccentColor, material);
      ConfigureNavigationShellImage(fillObject, new Vector2(14f, -218f), new Vector2(PagesPanelWidth - 28f, 28f), rounded, new Color(0.015f, 0.014f, 0.02f, 0.94f), material);
    }

    private static void ConfigureNavigationShellImage(GameObject target, Vector2 anchoredPosition, Vector2 size, Sprite rounded, Color color, Material material)
    {
      RectTransform rect = target.GetComponent<RectTransform>();
      rect.anchorMin = new Vector2(0f, 1f);
      rect.anchorMax = new Vector2(0f, 1f);
      rect.pivot = new Vector2(0f, 1f);
      rect.anchoredPosition = anchoredPosition;
      rect.sizeDelta = size;
      rect.localRotation = Quaternion.identity;
      rect.localScale = Vector3.one;

      Image image = target.GetComponent<Image>();
      image.sprite = rounded;
      image.type = rounded == null ? Image.Type.Simple : Image.Type.Sliced;
      image.pixelsPerUnitMultiplier = 2f;
      image.color = color;
      image.raycastTarget = false;
      if (material != null) image.material = material;
    }

    private static void CreateOrFindSeparator(GameObject parent, string objectName, Vector2 anchoredPosition, Vector2 size)
    {
      GameObject separatorObject = CreateOrFindChild(parent, objectName, typeof(RectTransform), typeof(Image));
      separatorObject.transform.SetAsLastSibling();
      RectTransform rect = separatorObject.GetComponent<RectTransform>();
      rect.anchorMin = new Vector2(0f, 1f);
      rect.anchorMax = new Vector2(0f, 1f);
      rect.pivot = new Vector2(0.5f, 0.5f);
      rect.anchoredPosition = anchoredPosition;
      rect.sizeDelta = size;
      rect.localRotation = Quaternion.identity;
      rect.localScale = Vector3.one;

      Image image = separatorObject.GetComponent<Image>();
      image.sprite = null;
      image.type = Image.Type.Simple;
      image.color = UiDividerColor;
      image.raycastTarget = false;
      Material material = GetOrCreateButtonMaterial();
      if (material != null) image.material = material;
    }

    private static Sprite GetRoundedUiSprite()
    {
      Sprite sprite = LoadSpriteByGuid(RoundedSpriteGuid);
      if (sprite != null) return sprite;
      return AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
    }

    private static Sprite LoadSpriteByGuid(string guid)
    {
      if (string.IsNullOrEmpty(guid)) return null;
      string path = AssetDatabase.GUIDToAssetPath(guid);
      return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static GameObject CreateOrFindChild(GameObject parent, string objectName, params System.Type[] components)
    {
      Transform child = parent.transform.Find(objectName);
      GameObject childObject = child == null ? null : child.gameObject;
      if (childObject == null)
      {
        childObject = new GameObject(objectName, components);
        childObject.transform.SetParent(parent.transform, false);
      }

      for (int i = 0; i < components.Length; i++)
      {
        if (childObject.GetComponent(components[i]) == null) childObject.AddComponent(components[i]);
      }

      return childObject;
    }

    private static void DestroyComponent<T>(GameObject gameObject) where T : Component
    {
      T component = gameObject == null ? null : gameObject.GetComponent<T>();
      if (component != null) UnityEngine.Object.DestroyImmediate(component);
    }

    private static void CleanupLegacyRootButton(GameObject root, string objectName)
    {
      if (root == null || string.IsNullOrEmpty(objectName)) return;
      Transform legacy = root.transform.Find(objectName);
      if (legacy != null) UnityEngine.Object.DestroyImmediate(legacy.gameObject);
    }

    private static void EnsureVrcUiShape(GameObject canvasObject)
    {
      if (canvasObject == null) return;

      System.Type uiShapeType = FindVrcUiShapeType();
      if (uiShapeType == null)
      {
        Debug.LogWarning("Yama Bili Danmaku: VRC_UiShape type was not found. Add VRC_UiShape to Danmaku Controls Canvas manually if the buttons are not clickable in VRChat.");
        return;
      }

      if (canvasObject.GetComponent(uiShapeType) == null)
      {
        canvasObject.AddComponent(uiShapeType);
      }
    }

    private static System.Type FindVrcUiShapeType()
    {
      System.Type uiShapeType = FindType("VRC.SDK3.Components.VRCUiShape");
      if (uiShapeType == null) uiShapeType = FindType("VRC.SDK3.Components.VRC_UIShape");
      if (uiShapeType == null) uiShapeType = FindType("VRC.SDK3.Components.VRC_UiShape");
      if (uiShapeType == null) uiShapeType = FindType("VRC.SDKBase.VRCUiShape");
      if (uiShapeType == null) uiShapeType = FindType("VRC.SDKBase.VRC_UIShape");
      if (uiShapeType == null) uiShapeType = FindType("VRC.SDKBase.VRC_UiShape");
      if (uiShapeType == null) uiShapeType = FindType("VRCUiShape");
      if (uiShapeType == null) uiShapeType = FindType("VRC_UIShape");
      if (uiShapeType == null) uiShapeType = FindType("VRC_UiShape");
      return uiShapeType;
    }

    private static void SetLayerRecursively(GameObject gameObject, int layer)
    {
      if (gameObject == null) return;
      gameObject.layer = layer;
      for (int i = 0; i < gameObject.transform.childCount; i++)
      {
        SetLayerRecursively(gameObject.transform.GetChild(i).gameObject, layer);
      }
    }

    private static Controller FindTargetController()
    {
      GameObject selected = Selection.activeGameObject;
      if (selected != null)
      {
        Transform current = selected.transform;
        while (current != null)
        {
          Controller controller = current.GetComponentInChildren<Controller>(true);
          if (controller != null) return controller;
          current = current.parent;
        }
      }

      return UnityEngine.Object.FindFirstObjectByType<Controller>(FindObjectsInactive.Include);
    }

    private static Transform ResolveRigParent(Controller controller)
    {
      if (controller == null) return null;

      Transform playerRoot = FindPlayerRoot(controller.transform);
      GameObject selected = Selection.activeGameObject;
      if (selected == null) return playerRoot;

      Transform selectedTransform = selected.transform;
      if (playerRoot != null && selectedTransform.IsChildOf(playerRoot)) return playerRoot;
      if (selectedTransform.GetComponentInChildren<Controller>(true) != null) return playerRoot;

      return selectedTransform;
    }

    private static Transform FindPlayerRoot(Transform controllerTransform)
    {
      Transform current = controllerTransform;
      while (current.parent != null)
      {
        current = current.parent;
        if (current.GetComponentInChildren<Controller>(true) != null)
        {
          return current;
        }
      }

      return controllerTransform.parent;
    }


    private static Component AddUdonSharpComponentForType(GameObject gameObject, System.Type moduleType)
    {
      if (gameObject == null || moduleType == null) return null;

      // UdonSharp 1.x expects U# behaviours to be added through its editor helper so the
      // generated Udon C# Program Asset is associated with the C# proxy script. Calling
      // GameObject.AddComponent(type) can leave the behaviour without a valid program asset.
      Component added = TryInvokeUdonSharpAddComponent(gameObject, moduleType);
      if (added != null) return added;

      // Fallback for environments where the helper method name changes. This may still need
      // manual U# Program Asset repair, but keeps the object creation path usable.
      try
      {
        return gameObject.AddComponent(moduleType);
      }
      catch (System.Exception exception)
      {
        Debug.LogError("Yama Bili Danmaku: AddComponent failed: " + exception);
        return null;
      }
    }

    private static Component TryInvokeUdonSharpAddComponent(GameObject gameObject, System.Type moduleType)
    {
      System.Reflection.Assembly[] assemblies = System.AppDomain.CurrentDomain.GetAssemblies();
      for (int a = 0; a < assemblies.Length; a++)
      {
        System.Type[] types;
        try { types = assemblies[a].GetTypes(); }
        catch { continue; }

        for (int t = 0; t < types.Length; t++)
        {
          System.Type type = types[t];
          if (type == null || (type.Namespace != "UdonSharpEditor" && (type.FullName == null || !type.FullName.StartsWith("UdonSharpEditor.")))) continue;

          System.Reflection.MethodInfo[] methods = type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
          for (int m = 0; m < methods.Length; m++)
          {
            System.Reflection.MethodInfo method = methods[m];
            if (method.Name != "AddUdonSharpComponent" && method.Name != "AddComponent") continue;

            System.Reflection.ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != 2) continue;
            if (!typeof(GameObject).IsAssignableFrom(parameters[0].ParameterType)) continue;
            if (!typeof(System.Type).IsAssignableFrom(parameters[1].ParameterType)) continue;

            try
            {
              object result = method.Invoke(null, new object[] { gameObject, moduleType });
              Component component = result as Component;
              if (component != null)
              {
                Debug.Log("Yama Bili Danmaku: Added UdonSharp component via " + type.FullName + "." + method.Name);
                return component;
              }
            }
            catch (System.Exception exception)
            {
              Debug.LogWarning("Yama Bili Danmaku: UdonSharp add helper failed: " + exception.Message);
            }
          }
        }
      }

      return null;
    }

    private static void EnsureDirectoryForAsset(string assetPath)
    {
      string directory = System.IO.Path.GetDirectoryName(assetPath);
      if (string.IsNullOrEmpty(directory)) return;

      string[] parts = directory.Replace("\\", "/").Split('/');
      string current = parts[0];
      for (int i = 1; i < parts.Length; i++)
      {
        string next = current + "/" + parts[i];
        if (!AssetDatabase.IsValidFolder(next))
        {
          AssetDatabase.CreateFolder(current, parts[i]);
        }
        current = next;
      }
    }

    [MenuItem("Yamadev/YamaPlayer/Wire Selected Bili Danmaku Module", false, 2003)]
    public static void WireSelectedModule()
    {
      GameObject selected = Selection.activeGameObject;
      if (selected == null)
      {
        EditorUtility.DisplayDialog("Yama Bili Danmaku", "Select the Bili Danmaku Module object first.", "OK");
        return;
      }

      Component module = selected.GetComponent("YamaBiliDanmakuModule3");
      if (module == null)
      {
        EditorUtility.DisplayDialog("Yama Bili Danmaku", "Selected object does not have YamaBiliDanmakuModule3. Add it in Inspector first.", "OK");
        return;
      }

      Controller controller = FindTargetController();
      RectTransform laneRoot = selected.transform.Find("Danmaku Lanes") as RectTransform;
      TextMeshProUGUI status = null;
      Transform statusTransform = selected.transform.Find("Status");
      if (statusTransform != null) status = statusTransform.GetComponent<TextMeshProUGUI>();
      Button displayAreaButton;
      TextMeshProUGUI displayAreaButtonLabel;
      Button danmakuToggleButton;
      TextMeshProUGUI danmakuToggleButtonLabel;
      Button urlPrefixToggleButton;
      TextMeshProUGUI urlPrefixToggleButtonLabel;
      RemoveRootGraphicRaycaster(selected);
      CreateOrFindControlsCanvas(selected, out displayAreaButton, out displayAreaButtonLabel, out danmakuToggleButton, out danmakuToggleButtonLabel, out urlPrefixToggleButton, out urlPrefixToggleButtonLabel);
      TextMeshProUGUI[] pool = selected.GetComponentsInChildren<TextMeshProUGUI>(true);
      WireModule(module, controller, laneRoot, status, displayAreaButtonLabel, danmakuToggleButtonLabel, displayAreaButton, danmakuToggleButton, pool);
      Component urlPrefixHelper = CreateOrFindUrlPrefixHelper(selected, controller, urlPrefixToggleButtonLabel);
      WireModuleUrlPrefixControls(module, urlPrefixHelper, urlPrefixToggleButtonLabel, urlPrefixToggleButton);
      Component pagesPlaylist = CreateOrFindPagesPlaylist(selected, controller, module);
      WirePagesPlaylist(pagesPlaylist, controller, module);
      ApplyVisualStyleFromModule(module, pool);
      ApplyChineseUiFont(selected);
      EditorUtility.DisplayDialog("Yama Bili Danmaku", "References wired.", "OK");
    }

    [MenuItem("Yamadev/YamaPlayer/Apply Selected Bili Danmaku Visual Style", false, 2005)]
    public static void ApplySelectedVisualStyle()
    {
      GameObject selected = Selection.activeGameObject;
      if (selected == null)
      {
        EditorUtility.DisplayDialog("Yama Bili Danmaku", "Select the Bili Danmaku Module object first.", "OK");
        return;
      }

      Component module = selected.GetComponent("YamaBiliDanmakuModule3");
      if (module == null)
      {
        EditorUtility.DisplayDialog("Yama Bili Danmaku", "Selected object does not have YamaBiliDanmakuModule3.", "OK");
        return;
      }

      ApplyVisualStyleFromModule(module, selected.GetComponentsInChildren<TextMeshProUGUI>(true));
      EditorUtility.DisplayDialog("Yama Bili Danmaku", "Visual style applied.", "OK");
    }

    [MenuItem("Yamadev/YamaPlayer/Create or Wire Selected Pages Panel", false, 2007)]
    public static void CreateOrWireSelectedPagesPanel()
    {
      GameObject selected = Selection.activeGameObject;
      if (selected == null)
      {
        EditorUtility.DisplayDialog("Yama Bili Danmaku", "Select the Bili Danmaku Module object first.", "OK");
        return;
      }

      Component module = selected.GetComponent("YamaBiliDanmakuModule3");
      if (module == null)
      {
        EditorUtility.DisplayDialog("Yama Bili Danmaku", "Selected object does not have YamaBiliDanmakuModule3.", "OK");
        return;
      }

      Controller controller = FindTargetController();
      Component pagesPlaylist = CreateOrFindPagesPlaylist(selected, controller, module);
      WirePagesPlaylist(pagesPlaylist, controller, module);
      EditorUtility.DisplayDialog("Yama Bili Danmaku", "Pages panel wired.", "OK");
    }

    [MenuItem("Yamadev/YamaPlayer/Apply Selected Bili UI Skin", false, 2008)]
    public static void ApplySelectedUiSkin()
    {
      GameObject selected = Selection.activeGameObject;
      if (selected == null)
      {
        EditorUtility.DisplayDialog("Yama Bili Danmaku", "Select the Bili Danmaku Module object first.", "OK");
        return;
      }

      Component module = selected.GetComponent("YamaBiliDanmakuModule3");
      if (module == null)
      {
        EditorUtility.DisplayDialog("Yama Bili Danmaku", "Selected object does not have YamaBiliDanmakuModule3.", "OK");
        return;
      }

      Button displayAreaButton;
      TextMeshProUGUI displayAreaButtonLabel;
      Button danmakuToggleButton;
      TextMeshProUGUI danmakuToggleButtonLabel;
      Button urlPrefixToggleButton;
      TextMeshProUGUI urlPrefixToggleButtonLabel;
      CreateOrFindControlsCanvas(selected, out displayAreaButton, out displayAreaButtonLabel, out danmakuToggleButton, out danmakuToggleButtonLabel, out urlPrefixToggleButton, out urlPrefixToggleButtonLabel);

      Controller controller = FindTargetController();
      Component urlPrefixHelper = CreateOrFindUrlPrefixHelper(selected, controller, null);
      WireModuleUrlPrefixControls(module, urlPrefixHelper, null, null);
      Component pagesPlaylist = CreateOrFindPagesPlaylist(selected, controller, module);
      WirePagesPlaylist(pagesPlaylist, controller, module);
      ApplyChineseUiFont(selected);
      EditorUtility.SetDirty(selected);
      SceneView.RepaintAll();
      EditorUtility.DisplayDialog("Yama Bili Danmaku", "Black-gold UI skin applied. URL Input references were not changed.", "OK");
    }

    [MenuItem("Yamadev/YamaPlayer/Wire Selected Bili URL Prefix Helper", false, 2004)]
    public static void WireSelectedUrlPrefixHelper()
    {
      GameObject selected = Selection.activeGameObject;
      if (selected == null)
      {
        EditorUtility.DisplayDialog("Yama Bili Danmaku", "Select the Bili URL Prefix Helper object first.", "OK");
        return;
      }

      Component helper = selected.GetComponent("YamaBiliUrlPrefixHelper3");
      if (helper == null)
      {
        EditorUtility.DisplayDialog("Yama Bili Danmaku", "Selected object does not have YamaBiliUrlPrefixHelper3.", "OK");
        return;
      }

      Button urlPrefixToggleButton = null;
      TextMeshProUGUI urlPrefixToggleButtonLabel = null;
      Transform moduleRoot = selected.transform.parent;
      if (moduleRoot != null)
      {
        Button displayAreaButton;
        TextMeshProUGUI displayAreaButtonLabel;
        Button danmakuToggleButton;
        TextMeshProUGUI danmakuToggleButtonLabel;
        CreateOrFindControlsCanvas(moduleRoot.gameObject, out displayAreaButton, out displayAreaButtonLabel, out danmakuToggleButton, out danmakuToggleButtonLabel, out urlPrefixToggleButton, out urlPrefixToggleButtonLabel);
      }

      WireUrlPrefixHelper(helper, FindTargetController(), urlPrefixToggleButtonLabel);
      EditorUtility.DisplayDialog("Yama Bili Danmaku", "URL prefix helper references wired.", "OK");
    }

    private static void WireUrlPrefixHelper(Component helper, Controller controller, TextMeshProUGUI urlPrefixToggleButtonLabel)
    {
      if (helper == null) return;

      VRCUrlInputField queueInput = null;
      Text queueInputText = null;
      Text queueInputIdleLabel = null;
      Component queuePlaylist = null;
      Transform moduleRoot = helper.transform.parent;
      if (moduleRoot != null)
      {
        Transform queueInputTransform = moduleRoot.Find(ControlsCanvasName + "/Queue URL Input");
        if (queueInputTransform != null)
        {
          queueInput = queueInputTransform.GetComponent<VRCUrlInputField>();
          Transform textTransform = queueInputTransform.Find("Text");
          if (textTransform != null) queueInputText = textTransform.GetComponent<Text>();
          Transform idleLabelTransform = queueInputTransform.Find("Idle Label");
          if (idleLabelTransform != null) queueInputIdleLabel = idleLabelTransform.GetComponent<Text>();
        }
        Transform queuePanelTransform = moduleRoot.Find(PagesPanelName);
        if (queuePanelTransform != null) queuePlaylist = queuePanelTransform.GetComponent("YamaBiliPagesPlaylist3");
      }

      SerializedObject serialized = new SerializedObject(helper);
      SerializedProperty controllerProperty = serialized.FindProperty("_controller");
      if (controllerProperty != null && controller != null) controllerProperty.objectReferenceValue = controller;

      SerializedProperty topProperty = serialized.FindProperty("_topUrlInputField");
      SerializedProperty bottomProperty = serialized.FindProperty("_bottomUrlInputField");
      VRCUrlInputField topInput = topProperty == null ? null : topProperty.objectReferenceValue as VRCUrlInputField;
      VRCUrlInputField bottomInput = bottomProperty == null ? null : bottomProperty.objectReferenceValue as VRCUrlInputField;

      // YamaPlayer URL inputs are intentionally assigned by the user. The generated queue input
      // has the same component type, so never allow it to occupy either manual reference slot.
      if (queueInput != null && topInput == queueInput)
      {
        topProperty.objectReferenceValue = null;
        topInput = null;
      }
      if (queueInput != null && bottomInput == queueInput)
      {
        bottomProperty.objectReferenceValue = null;
        bottomInput = null;
      }

      SerializedProperty labelProperty = serialized.FindProperty("_urlPrefixToggleButtonLabel");
      if (labelProperty != null) labelProperty.objectReferenceValue = urlPrefixToggleButtonLabel;

      SerializedProperty queueInputProperty = serialized.FindProperty("_queueUrlInputField");
      if (queueInputProperty != null) queueInputProperty.objectReferenceValue = queueInput;

      SerializedProperty queuePlaylistProperty = serialized.FindProperty("_queuePlaylist");
      if (queuePlaylistProperty != null) queuePlaylistProperty.objectReferenceValue = queuePlaylist;

      SerializedProperty queueInputTextProperty = serialized.FindProperty("_queueInputText");
      if (queueInputTextProperty != null) queueInputTextProperty.objectReferenceValue = queueInputText;

      SerializedProperty queueInputIdleLabelProperty = serialized.FindProperty("_queueInputIdleLabel");
      if (queueInputIdleLabelProperty != null) queueInputIdleLabelProperty.objectReferenceValue = queueInputIdleLabel;

      SerializedProperty prefixProperty = serialized.FindProperty("_urlPrefix");
      SetVRCUrl(prefixProperty, DefaultUrlPrefix);

      SetBool(serialized, "_enableUrlPrefixOnInput", true);
      SetBool(serialized, "_keepPrefixWhenEmpty", false);
      SetFloat(serialized, "_refreshSeconds", 3f);
      SetFloat(serialized, "_inputWatchSeconds", 0.25f);

      serialized.ApplyModifiedPropertiesWithoutUndo();

      AddPrefixInputEventTriggers(topInput, helper, "ApplyPrefixToTopInput");
      AddPrefixInputEventTriggers(bottomInput, helper, "ApplyPrefixToBottomInput");
      AddQueueInputEventTriggers(queueInput, helper);
    }

    private static void AddQueueInputEventTriggers(VRCUrlInputField inputField, Component helper)
    {
      if (inputField == null || helper == null) return;

      UdonSharpBehaviour behaviour = helper as UdonSharpBehaviour;
      if (behaviour == null) return;

      EventTrigger trigger = inputField.GetComponent<EventTrigger>();
      if (trigger == null) trigger = inputField.gameObject.AddComponent<EventTrigger>();
      if (trigger.triggers == null) trigger.triggers = new System.Collections.Generic.List<EventTrigger.Entry>();

      RemoveEventTriggerEntries(trigger, EventTriggerType.Select);
      RemoveEventTriggerEntries(trigger, EventTriggerType.PointerClick);
      RemoveEventTriggerEntries(trigger, EventTriggerType.PointerEnter);
      RemoveEventTriggerEntries(trigger, EventTriggerType.Submit);
      RemoveEventTriggerEntries(trigger, EventTriggerType.Deselect);
      AddPrefixInputEventTrigger(trigger, EventTriggerType.PointerEnter, behaviour, "PrimeQueueInput");
      AddPrefixInputEventTrigger(trigger, EventTriggerType.Select, behaviour, "PrepareQueueInput");
      AddInputEndEditCustomEvent(inputField, helper, "SubmitQueueInput");
      EditorUtility.SetDirty(trigger);
    }

    private static void RemoveEventTriggerEntries(EventTrigger trigger, EventTriggerType eventType)
    {
      if (trigger == null || trigger.triggers == null) return;
      for (int i = trigger.triggers.Count - 1; i >= 0; i--)
      {
        EventTrigger.Entry entry = trigger.triggers[i];
        if (entry != null && entry.eventID == eventType) trigger.triggers.RemoveAt(i);
      }
    }

    private static void AddInputEndEditCustomEvent(VRCUrlInputField inputField, Component receiver, string eventName)
    {
      if (inputField == null || receiver == null || string.IsNullOrEmpty(eventName)) return;

      UnityAction<string> sendEvent;
      if (!TryGetSendCustomEventAction(receiver, out sendEvent)) return;

      // This input belongs to the generated queue UI. Remove listeners left by older generated
      // versions so the URL cannot be submitted to YamaPlayer and the queue helper at once.
      for (int i = inputField.onEndEdit.GetPersistentEventCount() - 1; i >= 0; i--)
        UnityEventTools.RemovePersistentListener(inputField.onEndEdit, i);

      UnityEventTools.AddStringPersistentListener(inputField.onEndEdit, sendEvent, eventName);
      inputField.AllowSendingOnEndEdit = true;
      EditorUtility.SetDirty(inputField);
    }

    private static void AddPrefixInputEventTriggers(VRCUrlInputField inputField, Component helper, string eventName)
    {
      if (inputField == null || helper == null) return;

      UdonSharpBehaviour behaviour = helper as UdonSharpBehaviour;
      if (behaviour == null)
      {
        Debug.LogWarning("Yama Bili Danmaku: URL prefix helper is not an UdonSharpBehaviour, cannot wire input click events.");
        return;
      }

      EventTrigger trigger = inputField.GetComponent<EventTrigger>();
      if (trigger == null) trigger = inputField.gameObject.AddComponent<EventTrigger>();
      if (trigger.triggers == null) trigger.triggers = new System.Collections.Generic.List<EventTrigger.Entry>();

      AddPrefixInputEventTrigger(trigger, EventTriggerType.Select, behaviour, eventName);
      AddPrefixInputEventTrigger(trigger, EventTriggerType.PointerClick, behaviour, eventName);
      AddPrefixInputEventTrigger(trigger, EventTriggerType.Submit, behaviour, eventName);
      AddPrefixInputEventTrigger(trigger, EventTriggerType.Deselect, behaviour, eventName);
      EditorUtility.SetDirty(trigger);
    }

    private static void AddPrefixInputEventTrigger(EventTrigger trigger, EventTriggerType eventType, UdonSharpBehaviour behaviour, string eventName)
    {
      for (int i = 0; i < trigger.triggers.Count; i++)
      {
        EventTrigger.Entry existingEntry = trigger.triggers[i];
        if (existingEntry == null || existingEntry.eventID != eventType) continue;

        int persistentCount = existingEntry.callback.GetPersistentEventCount();
        for (int p = 0; p < persistentCount; p++)
        {
          if (existingEntry.callback.GetPersistentTarget(p) == behaviour &&
              existingEntry.callback.GetPersistentMethodName(p) == nameof(UdonSharpBehaviour.SendCustomEvent))
          {
            return;
          }
        }
      }

      EventTrigger.Entry entry = new EventTrigger.Entry { eventID = eventType };
      UnityEventTools.AddStringPersistentListener(entry.callback, behaviour.SendCustomEvent, eventName);
      trigger.triggers.Add(entry);
    }

    private static void WireModule(Component module, Controller controller, RectTransform laneRoot, TextMeshProUGUI status, TextMeshProUGUI displayAreaButtonLabel, TextMeshProUGUI danmakuToggleButtonLabel, Button displayAreaButton, Button danmakuToggleButton, TextMeshProUGUI[] rawPool)
    {
      if (module == null) return;

      SerializedObject serialized = new SerializedObject(module);
      SerializedProperty controllerProperty = serialized.FindProperty("_controller");
      if (controllerProperty != null && controller != null) controllerProperty.objectReferenceValue = controller;

      SerializedProperty laneProperty = serialized.FindProperty("_laneRoot");
      if (laneProperty != null) laneProperty.objectReferenceValue = laneRoot;

      SerializedProperty statusProperty = serialized.FindProperty("_statusText");
      if (statusProperty != null) statusProperty.objectReferenceValue = status;

      SerializedProperty displayAreaLabelProperty = serialized.FindProperty("_displayAreaButtonLabel");
      if (displayAreaLabelProperty != null) displayAreaLabelProperty.objectReferenceValue = displayAreaButtonLabel;

      SerializedProperty danmakuToggleLabelProperty = serialized.FindProperty("_danmakuToggleButtonLabel");
      if (danmakuToggleLabelProperty != null) danmakuToggleLabelProperty.objectReferenceValue = danmakuToggleButtonLabel;

      SetBool(serialized, "_loadFromCurrentYamaPlayerUrl", true);
      SetInt(serialized, "_laneCount", 12);
      SetFloat(serialized, "_lineHeight", 56f);
      SetFloat(serialized, "_scrollDuration", 8f);
      SetFloat(serialized, "_staticDuration", 4f);
      SetBool(serialized, "_dropDanmakuWhenLanesAreFull", true);
      SetFloat(serialized, "_laneSpawnSlackSeconds", 0.05f);
      SetFloat(serialized, "_statusVisibleSeconds", 2f);
      SetFloat(serialized, "_fontScale", 1.1f);
      SetFloat(serialized, "_textAlpha", 0.72f);
      SetInt(serialized, "_displayAreaMode", 0);
      SetBool(serialized, "_editorBoldText", true);
      SetBool(serialized, "_editorHeavyOutlineEnabled", true);
      SetFloat(serialized, "_editorOutlineWidth", DefaultOutlineWidth);
      SetFloat(serialized, "_editorOutlineAlpha", DefaultOutlineAlpha);
      SetInt(serialized, "_maxDanmakuLines", 4096);

      SerializedProperty poolProperty = serialized.FindProperty("_textPool");
      if (poolProperty != null)
      {
        int count = 0;
        for (int i = 0; i < rawPool.Length; i++)
        {
          if (rawPool[i] != null && rawPool[i].name.StartsWith("Danmaku Text")) count++;
        }

        poolProperty.arraySize = count;
        int cursor = 0;
        for (int i = 0; i < rawPool.Length; i++)
        {
          if (rawPool[i] == null || !rawPool[i].name.StartsWith("Danmaku Text")) continue;
          poolProperty.GetArrayElementAtIndex(cursor).objectReferenceValue = rawPool[i];
          cursor++;
        }
      }

      serialized.ApplyModifiedPropertiesWithoutUndo();
      AddModuleButtonClick(displayAreaButton, module, "CycleDisplayAreaMode");
      AddModuleButtonClick(danmakuToggleButton, module, "ToggleDanmaku");
      ApplyVisualStyleFromModule(module, rawPool);
    }

    private static void WireModuleUrlPrefixControls(Component module, Component urlPrefixHelper, TextMeshProUGUI urlPrefixToggleButtonLabel, Button urlPrefixToggleButton)
    {
      if (module == null) return;

      SerializedObject serialized = new SerializedObject(module);

      SerializedProperty helperProperty = serialized.FindProperty("_urlPrefixHelper");
      if (helperProperty != null) helperProperty.objectReferenceValue = urlPrefixHelper;

      SerializedProperty labelProperty = serialized.FindProperty("_urlPrefixToggleButtonLabel");
      if (labelProperty != null) labelProperty.objectReferenceValue = urlPrefixToggleButtonLabel;

      SetBool(serialized, "_urlPrefixFillEnabled", true);

      serialized.ApplyModifiedPropertiesWithoutUndo();
      AddModuleButtonClick(urlPrefixToggleButton, module, "ToggleUrlPrefixBackfill");
    }

    private static void WirePagesPlaylist(Component playlist, Controller controller, Component module)
    {
      if (playlist == null) return;

      Transform panel = playlist.transform;
      TextMeshProUGUI titleLabel = null;
      RectTransform titleRect = null;
      RectTransform titleViewportRect = null;
      TextMeshProUGUI statusLabel = null;
      TextMeshProUGUI playModeLabel = null;
      TextMeshProUGUI[] pageLabels = new TextMeshProUGUI[PagesButtonCount];
      Image[] deleteIcons = new Image[PagesButtonCount];

      Transform titleViewportTransform = panel.Find("Title Viewport");
      if (titleViewportTransform != null)
      {
        titleViewportRect = titleViewportTransform.GetComponent<RectTransform>();
        Transform titleTransform = titleViewportTransform.Find("Title");
        if (titleTransform != null)
        {
          titleLabel = titleTransform.GetComponent<TextMeshProUGUI>();
          titleRect = titleTransform.GetComponent<RectTransform>();
        }
      }
      Transform statusTransform = panel.Find("Status");
      if (statusTransform != null) statusLabel = statusTransform.GetComponent<TextMeshProUGUI>();
      Transform playModeTransform = panel.Find("Play Mode Button/Label");
      if (playModeTransform != null) playModeLabel = playModeTransform.GetComponent<TextMeshProUGUI>();

      for (int i = 0; i < PagesButtonCount; i++)
      {
        Transform labelTransform = panel.Find("Page Button " + i + "/Label");
        if (labelTransform != null) pageLabels[i] = labelTransform.GetComponent<TextMeshProUGUI>();
        Transform deleteIconTransform = panel.Find("Page Button " + i + "/Delete Hotspot/Icon");
        if (deleteIconTransform != null) deleteIcons[i] = deleteIconTransform.GetComponent<Image>();
      }

      WirePagesPlaylistReferences(playlist, controller, module, titleLabel, titleRect, titleViewportRect, statusLabel, playModeLabel, pageLabels, deleteIcons);
    }

    private static void WirePagesPlaylistReferences(Component playlist, Controller controller, Component module, TextMeshProUGUI titleLabel, RectTransform titleRect, RectTransform titleViewportRect, TextMeshProUGUI statusLabel, TextMeshProUGUI playModeLabel, TextMeshProUGUI[] pageLabels, Image[] deleteIcons)
    {
      if (playlist == null) return;

      SerializedObject serialized = new SerializedObject(playlist);
      SerializedProperty controllerProperty = serialized.FindProperty("_controller");
      if (controllerProperty != null && controller != null) controllerProperty.objectReferenceValue = controller;

      SerializedProperty moduleProperty = serialized.FindProperty("_danmakuModule");
      if (moduleProperty != null && module != null) moduleProperty.objectReferenceValue = module;

      Component helper = FindUrlPrefixHelperNear(playlist, module);
      SerializedProperty helperProperty = serialized.FindProperty("_urlPrefixHelper");
      if (helperProperty != null)
      {
        if (helper != null) helperProperty.objectReferenceValue = helper;
      }

      SerializedProperty prefixProperty = serialized.FindProperty("_pagesApiPrefix");
      SetVRCUrl(prefixProperty, DefaultPagesApiPrefix);
      SetBool(serialized, "_autoRefreshOnPlayback", true);
      SetBool(serialized, "_autoPlayNext", true);
      SetBool(serialized, "_useUnifiedQueue", true);
      SetFloat(serialized, "_playbackWatchSeconds", 0.5f);
      SetFloat(serialized, "_marqueePixelsPerSecond", 28f);
      SetFloat(serialized, "_marqueeTickSeconds", 0.08f);
      SetFloat(serialized, "_marqueePauseSeconds", 1.2f);
      SerializedProperty vcridPrefixProperty = serialized.FindProperty("_vcridUrlPrefix");
      if (vcridPrefixProperty != null) vcridPrefixProperty.stringValue = DefaultVcridUrlPrefix;
      SetInt(serialized, "_vcridMax", DefaultVcridMax);

      SerializedProperty titleProperty = serialized.FindProperty("_titleLabel");
      if (titleProperty != null) titleProperty.objectReferenceValue = titleLabel;
      SerializedProperty titleRectProperty = serialized.FindProperty("_titleRect");
      if (titleRectProperty != null) titleRectProperty.objectReferenceValue = titleRect;
      SerializedProperty titleViewportProperty = serialized.FindProperty("_titleViewportRect");
      if (titleViewportProperty != null) titleViewportProperty.objectReferenceValue = titleViewportRect;

      SerializedProperty statusProperty = serialized.FindProperty("_statusLabel");
      if (statusProperty != null) statusProperty.objectReferenceValue = statusLabel;

      SerializedProperty playModeProperty = serialized.FindProperty("_playModeButtonLabel");
      if (playModeProperty != null) playModeProperty.objectReferenceValue = playModeLabel;

      SerializedProperty labelsProperty = serialized.FindProperty("_pageButtonLabels");
      if (labelsProperty != null)
      {
        labelsProperty.arraySize = PagesButtonCount;
        for (int i = 0; i < PagesButtonCount; i++)
        {
          labelsProperty.GetArrayElementAtIndex(i).objectReferenceValue = pageLabels != null && i < pageLabels.Length ? pageLabels[i] : null;
        }
      }

      SerializedProperty deleteIconsProperty = serialized.FindProperty("_deleteButtonIcons");
      if (deleteIconsProperty != null)
      {
        deleteIconsProperty.arraySize = PagesButtonCount;
        for (int i = 0; i < PagesButtonCount; i++)
        {
          deleteIconsProperty.GetArrayElementAtIndex(i).objectReferenceValue = deleteIcons != null && i < deleteIcons.Length ? deleteIcons[i] : null;
        }
      }

      serialized.ApplyModifiedPropertiesWithoutUndo();

      if (helper != null)
      {
        SerializedObject helperSerialized = new SerializedObject(helper);
        SerializedProperty queuePlaylistProperty = helperSerialized.FindProperty("_queuePlaylist");
        if (queuePlaylistProperty != null) queuePlaylistProperty.objectReferenceValue = playlist;
        helperSerialized.ApplyModifiedPropertiesWithoutUndo();
      }
    }

    private static Component FindUrlPrefixHelperNear(Component playlist, Component module)
    {
      System.Type helperType = FindType("YamaBiliDanmakuV3.YamaBiliUrlPrefixHelper3");
      if (helperType == null) return null;

      if (module != null)
      {
        Component helper = module.GetComponentInChildren(helperType, true);
        if (helper != null) return helper;
      }

      if (playlist != null && playlist.transform.parent != null)
      {
        Component helper = playlist.transform.parent.GetComponentInChildren(helperType, true);
        if (helper != null) return helper;
      }

      return null;
    }

    private static void AddModuleButtonClick(Button button, Component module, string eventName)
    {
      if (button == null || module == null || string.IsNullOrEmpty(eventName)) return;

      UnityAction<string> sendEvent;
      if (!TryGetSendCustomEventAction(module, out sendEvent))
      {
        Debug.LogWarning("Yama Bili Danmaku: module is not an UdonSharpBehaviour, cannot wire controls button.");
        return;
      }

      button.onClick = new Button.ButtonClickedEvent();
      UnityEventTools.AddStringPersistentListener(button.onClick, sendEvent, eventName);
      EditorUtility.SetDirty(button);
    }

    private static void RemoveRootGraphicRaycaster(GameObject root)
    {
      if (root == null) return;

      GraphicRaycaster raycaster = root.GetComponent<GraphicRaycaster>();
      if (raycaster == null) return;

      Undo.DestroyObjectImmediate(raycaster);
      EditorUtility.SetDirty(root);
    }

    private static void ApplyChineseUiFont(GameObject root)
    {
      if (root == null) return;

      TMP_FontAsset font = FindChineseUiFont();
      if (font == null) return;

      Transform controls = root.transform.Find(ControlsCanvasName);
      if (controls != null) ApplyChineseUiFontToObject(controls.gameObject, font);

      Transform pages = root.transform.Find(PagesPanelName);
      if (pages != null) ApplyChineseUiFontToObject(pages.gameObject, font);
    }

    private static void ApplyChineseUiFontToObject(GameObject target, TMP_FontAsset font)
    {
      if (target == null || font == null) return;

      TextMeshProUGUI[] labels = target.GetComponentsInChildren<TextMeshProUGUI>(true);
      for (int i = 0; i < labels.Length; i++)
      {
        if (labels[i] == null) continue;
        labels[i].font = font;
        EditorUtility.SetDirty(labels[i]);
      }
    }

    private static TMP_FontAsset FindChineseUiFont()
    {
      TMP_FontAsset generated = GetOrCreateGeneratedUiFont();
      if (generated != null) return generated;

      TMP_FontAsset best = null;
      int bestScore = -1;

      ConsiderUiFont(TMP_Settings.defaultFontAsset, ref best, ref bestScore);

      TMP_FontAsset[] loadedFonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
      for (int i = 0; i < loadedFonts.Length; i++)
      {
        if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(loadedFonts[i]))) continue;
        ConsiderUiFont(loadedFonts[i], ref best, ref bestScore);
      }

      string[] guids = AssetDatabase.FindAssets("t:TMP_FontAsset");
      for (int i = 0; i < guids.Length; i++)
      {
        string path = AssetDatabase.GUIDToAssetPath(guids[i]);
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        ConsiderUiFont(font, ref best, ref bestScore);
      }
      return best;
    }

    private static void ConsiderUiFont(TMP_FontAsset font, ref TMP_FontAsset best, ref int bestScore)
    {
      if (font == null) return;
      string path = AssetDatabase.GetAssetPath(font).Replace('\\', '/');
      if (path == LegacyYouYuanFontAssetPath) return;
      if (!SupportsCharacters(font, RequiredUiCharacters)) return;

      int characterCount = font.characterTable == null ? 0 : font.characterTable.Count;
      int score = Mathf.Min(characterCount, 1000000);
      if (font == TMP_Settings.defaultFontAsset) score += 1;
      if (score <= bestScore) return;

      best = font;
      bestScore = score;
    }

    private static bool SupportsCharacters(TMP_FontAsset font, string characters)
    {
      if (font == null || string.IsNullOrEmpty(characters)) return false;
      for (int i = 0; i < characters.Length; i++)
      {
        if (!font.HasCharacter(characters[i], false, false)) return false;
      }
      return true;
    }

    private static TMP_FontAsset GetOrCreateGeneratedUiFont()
    {
      Font sourceFont = FindUiSourceFont();
      if (sourceFont == null) return null;

      TMP_FontAsset fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(GeneratedUiFontAssetPath);
      if (fontAsset != null && fontAsset.sourceFontFile != sourceFont)
      {
        AssetDatabase.DeleteAsset(GeneratedUiFontAssetPath);
        fontAsset = null;
      }

      if (fontAsset == null)
      {
        string directory = Path.GetDirectoryName(GeneratedUiFontAssetPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        fontAsset = TMP_FontAsset.CreateFontAsset(
          sourceFont,
          64,
          6,
          GlyphRenderMode.SDFAA,
          1024,
          1024,
          AtlasPopulationMode.Dynamic,
          true);
        if (fontAsset == null) return null;

        fontAsset.name = "PaulKoi UI SDF";
        Texture2D atlasTexture = fontAsset.atlasTexture;
        Material atlasMaterial = fontAsset.material;
        if (atlasTexture != null) atlasTexture.name = "PaulKoi UI Atlas";
        if (atlasMaterial != null) atlasMaterial.name = "PaulKoi UI Atlas Material";

        AssetDatabase.CreateAsset(fontAsset, GeneratedUiFontAssetPath);
        if (atlasTexture != null) AssetDatabase.AddObjectToAsset(atlasTexture, fontAsset);
        if (atlasMaterial != null) AssetDatabase.AddObjectToAsset(atlasMaterial, fontAsset);
      }

      fontAsset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
      fontAsset.isMultiAtlasTexturesEnabled = true;

      SerializedObject serialized = new SerializedObject(fontAsset);
      SerializedProperty clearDynamicData = serialized.FindProperty("m_ClearDynamicDataOnBuild");
      if (clearDynamicData != null) clearDynamicData.boolValue = false;
      serialized.ApplyModifiedPropertiesWithoutUndo();

      string missingCharacters;
      fontAsset.TryAddCharacters(RequiredUiCharacters, out missingCharacters, true);
      if (!string.IsNullOrEmpty(missingCharacters))
      {
        Debug.LogWarning("Yama Bili Danmaku: the UI font is missing characters: " + missingCharacters);
      }

      EditorUtility.SetDirty(fontAsset);
      AssetDatabase.SaveAssets();
      return fontAsset;
    }

    private static bool TryGetSendCustomEventAction(Component component, out UnityAction<string> sendEvent)
    {
      sendEvent = null;

      UdonSharpBehaviour behaviour = component as UdonSharpBehaviour;
      if (behaviour == null) return false;

      try
      {
        UdonBehaviour backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(behaviour);
        if (backing != null)
        {
          sendEvent = backing.SendCustomEvent;
          return true;
        }
      }
      catch (System.Exception exception)
      {
        Debug.LogWarning("Yama Bili Danmaku: failed to resolve backing UdonBehaviour, falling back to UdonSharp proxy. " + exception.Message);
      }

      sendEvent = behaviour.SendCustomEvent;
      return true;
    }

    private static void ApplyVisualStyleFromModule(Component module, TextMeshProUGUI[] rawPool)
    {
      if (module == null || rawPool == null) return;

      SerializedObject serialized = new SerializedObject(module);
      bool boldText = GetBool(serialized, "_editorBoldText", true);
      bool heavyOutlineEnabled = GetBool(serialized, "_editorHeavyOutlineEnabled", true);
      float outlineWidth = GetFloat(serialized, "_editorOutlineWidth", DefaultOutlineWidth);
      float outlineAlpha = GetFloat(serialized, "_editorOutlineAlpha", DefaultOutlineAlpha);
      ApplyVisualStyle(rawPool, boldText, heavyOutlineEnabled, outlineWidth, outlineAlpha);
    }

    private static void ApplyVisualStyle(TextMeshProUGUI[] rawPool, bool boldText, bool heavyOutlineEnabled, float outlineWidth, float outlineAlpha)
    {
      float width = heavyOutlineEnabled ? Mathf.Clamp(outlineWidth, 0f, 0.5f) : 0f;
      Color outlineColor = new Color(0f, 0f, 0f, heavyOutlineEnabled ? Mathf.Clamp01(outlineAlpha) : 0f);
      Material outlineMaterial = GetOrCreateOutlineMaterial(rawPool, width, outlineColor);

      for (int i = 0; i < rawPool.Length; i++)
      {
        TextMeshProUGUI text = rawPool[i];
        if (text == null || !text.name.StartsWith("Danmaku Text")) continue;

        Undo.RecordObject(text, "Apply Bili Danmaku Visual Style");
        text.fontStyle = boldText ? FontStyles.Bold : FontStyles.Normal;
        text.extraPadding = true;
        text.outlineWidth = width;
        text.outlineColor = outlineColor;
        if (outlineMaterial != null)
        {
          text.fontSharedMaterial = outlineMaterial;
          text.fontMaterial = outlineMaterial;
        }
        text.UpdateMeshPadding();
        EditorUtility.SetDirty(text);
      }
    }

    private static Material GetOrCreateOutlineMaterial(TextMeshProUGUI[] rawPool, float outlineWidth, Color outlineColor)
    {
      Material material = AssetDatabase.LoadAssetAtPath<Material>(OutlineMaterialPath);
      if (material == null)
      {
        Material source = FindSourceFontMaterial(rawPool);
        if (source == null)
        {
          Debug.LogWarning("Yama Bili Danmaku: could not find a TMP source material for outline. Component outline values were still applied.");
          return null;
        }

        EnsureDirectoryForAsset(OutlineMaterialPath);
        material = new Material(source);
        material.name = "Bili Danmaku TMP Outline";
        AssetDatabase.CreateAsset(material, OutlineMaterialPath);
      }

      ApplyTmpOutlineMaterial(material, outlineWidth, outlineColor);
      EditorUtility.SetDirty(material);
      AssetDatabase.SaveAssets();
      return material;
    }

    private static Material GetOrCreateButtonMaterial()
    {
      Material material = AssetDatabase.LoadAssetAtPath<Material>(ButtonMaterialPath);
      Shader shader = Shader.Find("VRChat/Mobile/Worlds/Supersampled UI");
      if (shader == null) shader = Shader.Find(ButtonShaderName);
      if (shader == null)
      {
        Debug.LogWarning("Yama Bili Danmaku: UI button shader was not found. Button images may trigger the VRChat SDK built-in UI shader alert.");
        return material;
      }

      if (material == null)
      {
        EnsureDirectoryForAsset(ButtonMaterialPath);
        material = new Material(shader);
        material.name = "Bili Danmaku UI Button";
        AssetDatabase.CreateAsset(material, ButtonMaterialPath);
      }

      if (material.shader != shader) material.shader = shader;
      if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
      EditorUtility.SetDirty(material);
      AssetDatabase.SaveAssets();
      return material;
    }

    private static Material FindSourceFontMaterial(TextMeshProUGUI[] rawPool)
    {
      if (rawPool == null) return null;

      for (int i = 0; i < rawPool.Length; i++)
      {
        TextMeshProUGUI text = rawPool[i];
        if (text == null || !text.name.StartsWith("Danmaku Text")) continue;

        Material material = text.fontSharedMaterial;
        if (material != null && AssetDatabase.GetAssetPath(material) != OutlineMaterialPath) return material;

        material = text.fontMaterial;
        if (material != null && AssetDatabase.GetAssetPath(material) != OutlineMaterialPath) return material;
      }

      return null;
    }

    private static void ApplyTmpOutlineMaterial(Material material, float outlineWidth, Color outlineColor)
    {
      if (material == null) return;

      ApplyMirrorReadableShader(material);
      material.EnableKeyword("OUTLINE_ON");
      material.EnableKeyword("UNDERLAY_ON");
      if (material.HasProperty("_OutlineWidth")) material.SetFloat("_OutlineWidth", outlineWidth);
      if (material.HasProperty("_OutlineColor")) material.SetColor("_OutlineColor", outlineColor);
      if (material.HasProperty("_OutlineSoftness")) material.SetFloat("_OutlineSoftness", 0f);
      if (material.HasProperty("_FaceDilate")) material.SetFloat("_FaceDilate", DefaultFaceDilate);
      if (material.HasProperty("_WeightNormal")) material.SetFloat("_WeightNormal", DefaultWeightNormal);
      if (material.HasProperty("_WeightBold")) material.SetFloat("_WeightBold", DefaultWeightBold);
      if (material.HasProperty("_ScaleRatioA")) material.SetFloat("_ScaleRatioA", 1f);
      if (material.HasProperty("_UnderlayColor")) material.SetColor("_UnderlayColor", outlineColor);
      if (material.HasProperty("_UnderlayOffsetX")) material.SetFloat("_UnderlayOffsetX", 0f);
      if (material.HasProperty("_UnderlayOffsetY")) material.SetFloat("_UnderlayOffsetY", 0f);
      if (material.HasProperty("_UnderlayDilate")) material.SetFloat("_UnderlayDilate", DefaultUnderlayDilate);
      if (material.HasProperty("_UnderlaySoftness")) material.SetFloat("_UnderlaySoftness", DefaultUnderlaySoftness);
      if (material.HasProperty("_MirrorFlip")) material.SetInt("_MirrorFlip", 1);
      if (material.HasProperty("_YBDMForceMirrorFlip")) material.SetFloat("_YBDMForceMirrorFlip", 0f);
    }

    private static void ApplyMirrorReadableShader(Material material)
    {
      Shader shader = Shader.Find(MirrorReadableShaderName);
      if (shader == null)
      {
        Debug.LogWarning("Yama Bili Danmaku: mirror-readable TMP shader was not found. Keeping the current TMP shader.");
        return;
      }

      if (material.shader != shader)
      {
        material.shader = shader;
      }
    }

    private static void SetInt(SerializedObject serialized, string name, int value)
    {
      SerializedProperty property = serialized.FindProperty(name);
      if (property != null) property.intValue = value;
    }

    private static void SetFloat(SerializedObject serialized, string name, float value)
    {
      SerializedProperty property = serialized.FindProperty(name);
      if (property != null) property.floatValue = value;
    }

    private static void SetBool(SerializedObject serialized, string name, bool value)
    {
      SerializedProperty property = serialized.FindProperty(name);
      if (property != null) property.boolValue = value;
    }

    private static float GetFloat(SerializedObject serialized, string name, float fallback)
    {
      SerializedProperty property = serialized.FindProperty(name);
      return property != null ? property.floatValue : fallback;
    }

    private static bool GetBool(SerializedObject serialized, string name, bool fallback)
    {
      SerializedProperty property = serialized.FindProperty(name);
      return property != null ? property.boolValue : fallback;
    }

    private static void SetVRCUrl(SerializedProperty property, string url)
    {
      if (property == null) return;

      SerializedProperty stringProperty = property.FindPropertyRelative("url");
      if (stringProperty == null) stringProperty = property.FindPropertyRelative("m_url");
      if (stringProperty == null) stringProperty = property.FindPropertyRelative("_url");
      if (stringProperty == null) stringProperty = property.FindPropertyRelative("Url");

      if (stringProperty != null && stringProperty.propertyType == SerializedPropertyType.String)
      {
        stringProperty.stringValue = url;
      }
      else
      {
        Debug.LogWarning("Yama Bili Danmaku: could not set VRCUrl serialized value for " + property.propertyPath + ". Set Url Prefix manually in Inspector.");
      }
    }

    private static bool HasDanmakuModule(GameObject gameObject)
    {
      if (gameObject == null) return false;
      System.Type moduleType = FindType("YamaBiliDanmakuV3.YamaBiliDanmakuModule3");
      if (moduleType != null) return gameObject.GetComponent(moduleType) != null;
      return gameObject.GetComponent("YamaBiliDanmakuModule3") != null;
    }

    [MenuItem("Yamadev/YamaPlayer/Fix Bili Danmaku U# Program Asset", false, 2005)]
    public static void FixProgramAssetMenu()
    {
      System.Type moduleType = FindType("YamaBiliDanmakuV3.YamaBiliDanmakuModule3");
      if (moduleType == null)
      {
        EditorUtility.DisplayDialog("Yama Bili Danmaku", "Runtime type not found. Run Assets > Reimport All first and check Console for C# compile errors.", "OK");
        return;
      }

      UnityEngine.Object asset = EnsureUdonSharpProgramAsset(moduleType);
      System.Type helperType = FindType("YamaBiliDanmakuV3.YamaBiliUrlPrefixHelper3");
      UnityEngine.Object helperAsset = helperType == null ? null : EnsureUdonSharpProgramAsset(helperType);
      if (asset == null || helperAsset == null)
      {
        EditorUtility.DisplayDialog("Yama Bili Danmaku", "Failed to create U# Program Asset. Use Create > U# Script manually as described in the README.", "OK");
        return;
      }

      Selection.activeObject = asset;
      EditorGUIUtility.PingObject(asset);
      EditorUtility.DisplayDialog("Yama Bili Danmaku", "U# Program Assets are ready. Now run UdonSharp > Compile All UdonSharp Programs, then create the module again.", "OK");
    }

    private static UnityEngine.Object EnsureUdonSharpProgramAsset(System.Type moduleType)
    {
      MonoScript script = FindRuntimeScript(moduleType.Name + ".cs");
      if (script == null)
      {
        Debug.LogError("Yama Bili Danmaku: cannot find " + moduleType.Name + ".cs MonoScript.");
        return null;
      }

      UnityEngine.Object existing = FindExistingProgramAsset(script);
      if (existing != null) return existing;

      System.Type programAssetType = FindType("UdonSharp.UdonSharpProgramAsset");
      if (programAssetType == null)
      {
        Debug.LogError("Yama Bili Danmaku: cannot find UdonSharp.UdonSharpProgramAsset type.");
        return null;
      }

      string basePath = "Assets/YamaBiliDanmakuV3/Runtime/" + moduleType.Name + ".asset";
      EnsureDirectoryForAsset(basePath);
      string path = AssetDatabase.GenerateUniqueAssetPath(basePath);

      ScriptableObject asset = ScriptableObject.CreateInstance(programAssetType);
      if (asset == null)
      {
        Debug.LogError("Yama Bili Danmaku: failed to create UdonSharpProgramAsset instance.");
        return null;
      }

      SerializedObject serialized = new SerializedObject(asset);
      SerializedProperty source = serialized.FindProperty("sourceCsScript");
      if (source == null)
      {
        Debug.LogError("Yama Bili Danmaku: UdonSharpProgramAsset has no sourceCsScript property in this SDK version.");
        UnityEngine.Object.DestroyImmediate(asset);
        return null;
      }

      source.objectReferenceValue = script;
      serialized.ApplyModifiedPropertiesWithoutUndo();

      AssetDatabase.CreateAsset(asset, path);
      EditorUtility.SetDirty(asset);
      AssetDatabase.SaveAssets();
      AssetDatabase.Refresh();

      TryInvokeAssetMethod(asset, "RefreshProgram");
      TryInvokeAssetMethod(asset, "UpdateProgram");

      Debug.Log("Yama Bili Danmaku: Created U# Program Asset at " + path);
      return asset;
    }

    private static void TryInvokeAssetMethod(UnityEngine.Object asset, string methodName)
    {
      if (asset == null) return;
      try
      {
        System.Reflection.MethodInfo method = asset.GetType().GetMethod(methodName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        if (method != null && method.GetParameters().Length == 0) method.Invoke(asset, null);
      }
      catch (System.Exception exception)
      {
        Debug.LogWarning("Yama Bili Danmaku: " + methodName + " failed: " + exception.Message);
      }
    }

    private static UnityEngine.Object FindExistingProgramAsset(MonoScript script)
    {
      string[] guids = AssetDatabase.FindAssets("t:UdonSharpProgramAsset");
      for (int i = 0; i < guids.Length; i++)
      {
        string path = AssetDatabase.GUIDToAssetPath(guids[i]);
        UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(path);
        if (asset == null) continue;

        SerializedObject serialized = new SerializedObject(asset);
        SerializedProperty sourceScript = serialized.FindProperty("sourceCsScript");
        if (sourceScript != null && sourceScript.objectReferenceValue == script)
        {
          return asset;
        }
      }

      return null;
    }

    private static MonoScript FindRuntimeScript(string fileName)
    {
      string[] guids = AssetDatabase.FindAssets(System.IO.Path.GetFileNameWithoutExtension(fileName) + " t:MonoScript");
      for (int i = 0; i < guids.Length; i++)
      {
        string path = AssetDatabase.GUIDToAssetPath(guids[i]);
        if (!path.EndsWith(fileName)) continue;

        MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
        if (script != null) return script;
      }

      return null;
    }

    private static System.Type FindType(string fullName)
    {
      System.Reflection.Assembly[] assemblies = System.AppDomain.CurrentDomain.GetAssemblies();
      for (int i = 0; i < assemblies.Length; i++)
      {
        System.Type type = assemblies[i].GetType(fullName);
        if (type != null) return type;
      }
      return null;
    }
  }
}
