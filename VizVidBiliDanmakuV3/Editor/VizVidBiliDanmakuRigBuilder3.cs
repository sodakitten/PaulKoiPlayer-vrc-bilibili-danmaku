using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;
using VRC.SDK3.Components;
using VRC.Udon;
using JLChnToZ.VRC.VVMW;

namespace VizVidBiliDanmakuV3.Editor
{
  public static class VizVidBiliDanmakuRigBuilder3
  {
    private const int PoolSize = 96;
    private const float CanvasWidth = 2875f;
    private const float CanvasHeight = 1600f;
    private const float CanvasLocalX = -0.005f;
    private const float CanvasLocalY = 1.503f;
    private const float CanvasLocalZ = -0.02f;
    private const string DefaultUrlPrefix = "https://danmaku.paulkoishi.com/player/?url=";
    private const string DefaultPrefabPath = "Assets/VizVidBiliDanmakuV3/Prefabs/Bili Danmaku Module.prefab";
    private const string OutlineMaterialPath = "Assets/VizVidBiliDanmakuV3/Materials/Bili Danmaku TMP Outline.mat";
    private const string ButtonMaterialPath = "Assets/VizVidBiliDanmakuV3/Materials/Bili Danmaku UI Button.mat";
    private const string MirrorReadableShaderName = "VizVidBiliDanmaku/TMP Mirror Readable";
    private const string ButtonShaderName = "VizVidBiliDanmaku/UI Button";
    private const string ControlsCanvasName = "Danmaku Controls Canvas";
    private const float ControlsWidth = 260f;
    private const float ControlsHeight = 160f;
    private const float ControlsPadding = 12f;
    private const float ControlButtonWidth = 236f;
    private const float ControlButtonHeight = 40f;
    private const float ControlsLocalZ = -8f;
    private const float DefaultOutlineWidth = 0.11f;
    private const float DefaultOutlineAlpha = 0.7f;
    private const float DefaultFaceDilate = 0.012f;
    private const float DefaultWeightNormal = 0f;
    private const float DefaultWeightBold = 0.28f;
    private const float DefaultUnderlayDilate = 0.16f;
    private const float DefaultUnderlaySoftness = 0.03f;

    [MenuItem("PaulKoiPlayer/VizVid/Create Bili Danmaku Module", false, 2000)]
    public static void CreateRig()
    {
      Core core = FindTargetCore();
      if (core == null)
      {
        EditorUtility.DisplayDialog("VizVid Bili Danmaku", "Select a VizVid object, or an object under VizVid that contains a Core.", "OK");
        return;
      }

      Transform parent = FindVizVidRoot(core.transform);
      GameObject root = BuildRig(core, parent);
      if (root == null) return;
      Undo.RegisterCreatedObjectUndo(root, "Create Bili Danmaku Module");

      Selection.activeObject = root;
      EditorGUIUtility.PingObject(root);
    }

    [MenuItem("GameObject/VizVid/Bili Danmaku Module", false, 41)]
    public static void CreateRigFromGameObjectMenu()
    {
      CreateRig();
    }

    [MenuItem("GameObject/VizVid/Bili Danmaku Module", true)]
    public static bool ValidateCreateRigFromGameObjectMenu()
    {
      return FindTargetCore() != null;
    }

    [MenuItem("PaulKoiPlayer/VizVid/Save Selected Bili Danmaku Module As Prefab", false, 2001)]
    public static void SaveSelectedAsPrefab()
    {
      GameObject selected = Selection.activeGameObject;
      if (selected == null || !HasDanmakuModule(selected))
      {
        EditorUtility.DisplayDialog("VizVid Bili Danmaku", "Select a Bili Danmaku Module object first.", "OK");
        return;
      }

      string path = EditorUtility.SaveFilePanelInProject(
        "Save Bili Danmaku Prefab",
        "Bili Danmaku Module",
        "prefab",
        "Choose where to save the prefab.",
        "Assets/VizVidBiliDanmakuV3/Prefabs");

      if (string.IsNullOrEmpty(path)) return;

      EnsureDirectoryForAsset(path);
      GameObject prefab = PrefabUtility.SaveAsPrefabAsset(selected, path);
      if (prefab == null)
      {
        EditorUtility.DisplayDialog("VizVid Bili Danmaku", "Failed to save prefab.", "OK");
        return;
      }

      Selection.activeObject = prefab;
      EditorGUIUtility.PingObject(prefab);
    }

    [MenuItem("PaulKoiPlayer/VizVid/Create Package Prefab Asset", false, 2002)]
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
          EditorUtility.DisplayDialog("VizVid Bili Danmaku", "Failed to create prefab asset.", "OK");
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

    private static GameObject BuildRig(Core core, Transform parent)
    {
      GameObject root = new GameObject("Bili Danmaku Module (VizVid)", typeof(RectTransform));
      Undo.RegisterCreatedObjectUndo(root, "Create Bili Danmaku Module");
      if (parent != null) root.transform.SetParent(parent, false);
      root.transform.localPosition = new Vector3(CanvasLocalX, CanvasLocalY, CanvasLocalZ);
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
        rect.sizeDelta = new Vector2(CanvasWidth, 110f);

        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.raycastTarget = false;
        text.alignment = TextAlignmentOptions.Center;
        text.fontSize = 52f;
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

      System.Type moduleType = FindType("VizVidBiliDanmakuV3.VizVidBiliDanmakuModule3");
      if (moduleType == null)
      {
        EditorUtility.DisplayDialog(
          "VizVid Bili Danmaku",
          "VizVidBiliDanmakuModule3 type was not found. Make sure the Runtime script has compiled successfully, then reimport this package or restart Unity.",
          "OK");
        UnityEngine.Object.DestroyImmediate(root);
        return null;
      }

      EnsureUdonSharpProgramAsset(moduleType);
      Component module = AddUdonSharpComponentForType(root, moduleType);
      if (module == null)
      {
        EditorUtility.DisplayDialog(
          "VizVid Bili Danmaku",
          "Failed to add UdonSharp component. Try creating a U# Program Asset manually from Assets/VizVidBiliDanmakuV3/Runtime/VizVidBiliDanmakuModule3.cs, then run this menu again.",
          "OK");
        UnityEngine.Object.DestroyImmediate(root);
        return null;
      }

      WireModule(module, core, laneRoot, status, displayAreaButtonLabel, danmakuToggleButtonLabel, displayAreaButton, danmakuToggleButton, pool);
      CreateUrlPrefixHelper(root, core, urlPrefixToggleButton, urlPrefixToggleButtonLabel);

      return root;
    }

    private static void CreateUrlPrefixHelper(GameObject root, Core core, Button urlPrefixToggleButton, TextMeshProUGUI urlPrefixToggleButtonLabel)
    {
      System.Type helperType = FindType("VizVidBiliDanmakuV3.VizVidBiliUrlPrefixHelper3");
      if (helperType == null)
      {
        Debug.LogWarning("VizVid Bili Danmaku: VizVidBiliUrlPrefixHelper3 type was not found. URL prefix helper was not created.");
        return;
      }

      EnsureUdonSharpProgramAsset(helperType);

      GameObject helperObject = new GameObject("Bili URL Prefix Helper (VizVid)");
      helperObject.transform.SetParent(root.transform, false);

      Component helper = AddUdonSharpComponentForType(helperObject, helperType);
      if (helper == null)
      {
        Debug.LogWarning("VizVid Bili Danmaku: failed to add Bili URL Prefix Helper.");
        UnityEngine.Object.DestroyImmediate(helperObject);
        return;
      }

      WireUrlPrefixHelper(helper, core, urlPrefixToggleButton, urlPrefixToggleButtonLabel);
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

      CreateOrFindControlsBackground(controlsObject);
      CreateOrFindCycleButton(controlsObject, "Display Area Button", new Vector2(ControlsPadding, -ControlsPadding), "Danmaku: Full", out displayAreaButton, out displayAreaButtonLabel);
      CreateOrFindCycleButton(controlsObject, "Danmaku Toggle Button", new Vector2(ControlsPadding, -(ControlsPadding + ControlButtonHeight + 8f)), "Danmaku: On", out danmakuToggleButton, out danmakuToggleButtonLabel);
      CreateOrFindCycleButton(controlsObject, "URL Prefix Toggle Button", new Vector2(ControlsPadding, -(ControlsPadding + (ControlButtonHeight + 8f) * 2f)), "URL Fill: On", out urlPrefixToggleButton, out urlPrefixToggleButtonLabel);
      SetLayerRecursively(controlsObject, 0);
    }

    private static void CreateOrFindControlsBackground(GameObject controlsObject)
    {
      GameObject backgroundObject = CreateOrFindChild(controlsObject, "BG", typeof(RectTransform), typeof(Image));
      backgroundObject.transform.SetAsFirstSibling();
      RectTransform backgroundRect = backgroundObject.GetComponent<RectTransform>();
      backgroundRect.anchorMin = Vector2.zero;
      backgroundRect.anchorMax = Vector2.one;
      backgroundRect.offsetMin = Vector2.zero;
      backgroundRect.offsetMax = Vector2.zero;
      Image background = backgroundObject.GetComponent<Image>();
      background.color = new Color(0f, 0f, 0f, 0.38f);
      background.raycastTarget = true;
      Material material = GetOrCreateButtonMaterial();
      if (material != null) background.material = material;
    }

    private static void CreateOrFindCycleButton(GameObject controlsObject, string objectName, Vector2 anchoredPosition, string labelText, out Button button, out TextMeshProUGUI label)
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
      image.color = new Color(0f, 0f, 0f, 0.62f);
      image.raycastTarget = true;
      Material material = GetOrCreateButtonMaterial();
      if (material != null) image.material = material;

      button = buttonObject.GetComponent<Button>();
      if (button == null) button = buttonObject.AddComponent<Button>();
      button.interactable = true;
      button.targetGraphic = image;
      button.transition = Selectable.Transition.ColorTint;
      ColorBlock colors = button.colors;
      colors.normalColor = Color.white;
      colors.highlightedColor = new Color(0.82f, 0.92f, 1f, 1f);
      colors.pressedColor = new Color(0.58f, 0.78f, 1f, 1f);
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
      labelRect.offsetMin = Vector2.zero;
      labelRect.offsetMax = Vector2.zero;
      labelRect.localRotation = Quaternion.identity;
      labelRect.localScale = Vector3.one;

      label = labelObject.GetComponent<TextMeshProUGUI>();
      label.raycastTarget = true;
      label.alignment = TextAlignmentOptions.Center;
      label.fontSize = 18f;
      label.fontStyle = FontStyles.Bold;
      label.enableWordWrapping = false;
      label.overflowMode = TextOverflowModes.Ellipsis;
      label.color = new Color(1f, 1f, 1f, 0.94f);
      label.text = labelText;
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
        Debug.LogWarning("VizVid Bili Danmaku: VRC_UiShape type was not found. Add VRC_UiShape to Danmaku Controls Canvas manually if the buttons are not clickable in VRChat.");
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

    private static Core FindTargetCore()
    {
      GameObject selected = Selection.activeGameObject;
      if (selected != null)
      {
        Transform current = selected.transform;
        while (current != null)
        {
          Core core = current.GetComponentInChildren<Core>(true);
          if (core != null) return core;
          current = current.parent;
        }
      }

      return UnityEngine.Object.FindFirstObjectByType<Core>(FindObjectsInactive.Include);
    }

    private static Transform FindVizVidRoot(Transform coreTransform)
    {
      if (coreTransform == null) return null;

      Core targetCore = coreTransform.GetComponent<Core>();
      GameObject selected = Selection.activeGameObject;
      if (selected != null && targetCore != null)
      {
        Transform current = selected.transform;
        while (current != null)
        {
          if (current.GetComponentInChildren<Core>(true) == targetCore)
          {
            return current;
          }
          current = current.parent;
        }
      }

      return coreTransform.parent != null ? coreTransform.parent : coreTransform;
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
        Debug.LogError("VizVid Bili Danmaku: AddComponent failed: " + exception);
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
                Debug.Log("VizVid Bili Danmaku: Added UdonSharp component via " + type.FullName + "." + method.Name);
                return component;
              }
            }
            catch (System.Exception exception)
            {
              Debug.LogWarning("VizVid Bili Danmaku: UdonSharp add helper failed: " + exception.Message);
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

    [MenuItem("PaulKoiPlayer/VizVid/Wire Selected Bili Danmaku Module", false, 2003)]
    public static void WireSelectedModule()
    {
      GameObject selected = Selection.activeGameObject;
      if (selected == null)
      {
        EditorUtility.DisplayDialog("VizVid Bili Danmaku", "Select the Bili Danmaku Module object first.", "OK");
        return;
      }

      Component module = selected.GetComponent("VizVidBiliDanmakuModule3");
      if (module == null)
      {
        EditorUtility.DisplayDialog("VizVid Bili Danmaku", "Selected object does not have VizVidBiliDanmakuModule3. Add it in Inspector first.", "OK");
        return;
      }

      Core core = FindTargetCore();
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
      WireModule(module, core, laneRoot, status, displayAreaButtonLabel, danmakuToggleButtonLabel, displayAreaButton, danmakuToggleButton, pool);
      System.Type helperType = FindType("VizVidBiliDanmakuV3.VizVidBiliUrlPrefixHelper3");
      Component helper = helperType == null ? null : selected.GetComponentInChildren(helperType, true);
      if (helper != null) WireUrlPrefixHelper(helper, core, urlPrefixToggleButton, urlPrefixToggleButtonLabel);
      ApplyVisualStyleFromModule(module, pool);
      EditorUtility.DisplayDialog("VizVid Bili Danmaku", "References wired.", "OK");
    }

    [MenuItem("PaulKoiPlayer/VizVid/Apply Selected Bili Danmaku Visual Style", false, 2005)]
    public static void ApplySelectedVisualStyle()
    {
      GameObject selected = Selection.activeGameObject;
      if (selected == null)
      {
        EditorUtility.DisplayDialog("VizVid Bili Danmaku", "Select the Bili Danmaku Module object first.", "OK");
        return;
      }

      Component module = selected.GetComponent("VizVidBiliDanmakuModule3");
      if (module == null)
      {
        EditorUtility.DisplayDialog("VizVid Bili Danmaku", "Selected object does not have VizVidBiliDanmakuModule3.", "OK");
        return;
      }

      ApplyVisualStyleFromModule(module, selected.GetComponentsInChildren<TextMeshProUGUI>(true));
      EditorUtility.DisplayDialog("VizVid Bili Danmaku", "Visual style applied.", "OK");
    }

    [MenuItem("PaulKoiPlayer/VizVid/Wire Selected Bili URL Prefix Helper", false, 2004)]
    public static void WireSelectedUrlPrefixHelper()
    {
      GameObject selected = Selection.activeGameObject;
      if (selected == null)
      {
        EditorUtility.DisplayDialog("VizVid Bili Danmaku", "Select the Bili URL Prefix Helper object first.", "OK");
        return;
      }

      Component helper = selected.GetComponent("VizVidBiliUrlPrefixHelper3");
      if (helper == null)
      {
        EditorUtility.DisplayDialog("VizVid Bili Danmaku", "Selected object does not have VizVidBiliUrlPrefixHelper3.", "OK");
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

      WireUrlPrefixHelper(helper, FindTargetCore(), urlPrefixToggleButton, urlPrefixToggleButtonLabel);
      EditorUtility.DisplayDialog("VizVid Bili Danmaku", "URL prefix helper references wired.", "OK");
    }

    private static void WireUrlPrefixHelper(Component helper, Core core, Button urlPrefixToggleButton, TextMeshProUGUI urlPrefixToggleButtonLabel)
    {
      if (helper == null) return;

      VRCUrlInputField addressInput = FindVizVidAddressInput(core);

      SerializedObject serialized = new SerializedObject(helper);
      SerializedProperty coreProperty = serialized.FindProperty("_core");
      if (coreProperty != null && core != null) coreProperty.objectReferenceValue = core;

      SerializedProperty addressProperty = serialized.FindProperty("_addressUrlInputField");
      if (addressProperty != null) addressProperty.objectReferenceValue = addressInput;

      SerializedProperty labelProperty = serialized.FindProperty("_urlPrefixToggleButtonLabel");
      if (labelProperty != null) labelProperty.objectReferenceValue = urlPrefixToggleButtonLabel;

      SerializedProperty prefixProperty = serialized.FindProperty("_urlPrefix");
      SetVRCUrl(prefixProperty, DefaultUrlPrefix);

      SetBool(serialized, "_enableUrlPrefixOnInput", true);
      SetBool(serialized, "_keepPrefixWhenEmpty", false);
      SetFloat(serialized, "_refreshSeconds", 3f);
      SetFloat(serialized, "_inputWatchSeconds", 0.25f);

      serialized.ApplyModifiedPropertiesWithoutUndo();

      AddPrefixInputEventTriggers(addressInput, helper, "ApplyPrefixToEmptyField");
      AddModuleButtonClick(urlPrefixToggleButton, helper, "ToggleUrlPrefixBackfill");
    }

    private static void AddPrefixInputEventTriggers(VRCUrlInputField inputField, Component helper, string eventName)
    {
      if (inputField == null || helper == null) return;

      UdonSharpBehaviour behaviour = helper as UdonSharpBehaviour;
      if (behaviour == null)
      {
        Debug.LogWarning("VizVid Bili Danmaku: URL prefix helper is not an UdonSharpBehaviour, cannot wire input click events.");
        return;
      }

      EventTrigger trigger = inputField.GetComponent<EventTrigger>();
      if (trigger == null) trigger = inputField.gameObject.AddComponent<EventTrigger>();
      if (trigger.triggers == null) trigger.triggers = new System.Collections.Generic.List<EventTrigger.Entry>();

      AddPrefixInputEventTrigger(trigger, EventTriggerType.Select, behaviour, eventName);
      AddPrefixInputEventTrigger(trigger, EventTriggerType.PointerClick, behaviour, eventName);
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

    private static VRCUrlInputField FindVizVidAddressInput(Core core)
    {
      if (core == null) return null;

      Transform root = FindVizVidRoot(core.transform);
      if (root == null) root = core.transform;

      VRCUrlInputField input = FindInputAtPath(root, "Canvas (1)/Panel/Address");
      if (input != null) return input;

      input = FindInputAtPath(root, "Canvas (1)/Address");
      if (input != null) return input;

      VRCUrlInputField[] inputs = root.GetComponentsInChildren<VRCUrlInputField>(true);
      for (int i = 0; i < inputs.Length; i++)
      {
        if (inputs[i] != null && inputs[i].name == "Address") return inputs[i];
      }

      return inputs.Length > 0 ? inputs[0] : null;
    }

    private static VRCUrlInputField FindInputAtPath(Transform root, string path)
    {
      if (root == null || string.IsNullOrEmpty(path)) return null;

      Transform target = root.Find(path);
      return target == null ? null : target.GetComponent<VRCUrlInputField>();
    }

    private static void WireModule(Component module, Core core, RectTransform laneRoot, TextMeshProUGUI status, TextMeshProUGUI displayAreaButtonLabel, TextMeshProUGUI danmakuToggleButtonLabel, Button displayAreaButton, Button danmakuToggleButton, TextMeshProUGUI[] rawPool)
    {
      if (module == null) return;

      SerializedObject serialized = new SerializedObject(module);
      SerializedProperty coreProperty = serialized.FindProperty("_core");
      if (coreProperty != null && core != null) coreProperty.objectReferenceValue = core;

      SerializedProperty laneProperty = serialized.FindProperty("_laneRoot");
      if (laneProperty != null) laneProperty.objectReferenceValue = laneRoot;

      SerializedProperty statusProperty = serialized.FindProperty("_statusText");
      if (statusProperty != null) statusProperty.objectReferenceValue = status;

      SerializedProperty displayAreaLabelProperty = serialized.FindProperty("_displayAreaButtonLabel");
      if (displayAreaLabelProperty != null) displayAreaLabelProperty.objectReferenceValue = displayAreaButtonLabel;

      SerializedProperty danmakuToggleLabelProperty = serialized.FindProperty("_danmakuToggleButtonLabel");
      if (danmakuToggleLabelProperty != null) danmakuToggleLabelProperty.objectReferenceValue = danmakuToggleButtonLabel;

      SetBool(serialized, "_loadFromCurrentVizVidUrl", true);
      SetInt(serialized, "_laneCount", 12);
      SetFloat(serialized, "_lineHeight", 92f);
      SetFloat(serialized, "_scrollDuration", 8f);
      SetFloat(serialized, "_staticDuration", 4f);
      SetBool(serialized, "_dropDanmakuWhenLanesAreFull", true);
      SetFloat(serialized, "_laneSpawnSlackSeconds", 0.05f);
      SetFloat(serialized, "_statusVisibleSeconds", 2f);
      SetFloat(serialized, "_baseFontSize", 52f);
      SetFloat(serialized, "_fontScale", 1.1f);
      SetFloat(serialized, "_lineSpacing", 1.35f);
      SetFloat(serialized, "_characterWidth", 48f);
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

    private static void AddModuleButtonClick(Button button, Component module, string eventName)
    {
      if (button == null || module == null || string.IsNullOrEmpty(eventName)) return;

      UnityAction<string> sendEvent;
      if (!TryGetSendCustomEventAction(module, out sendEvent))
      {
        Debug.LogWarning("VizVid Bili Danmaku: module is not an UdonSharpBehaviour, cannot wire controls button.");
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
        Debug.LogWarning("VizVid Bili Danmaku: failed to resolve backing UdonBehaviour, falling back to UdonSharp proxy. " + exception.Message);
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
      float baseFontSize = GetFloat(serialized, "_baseFontSize", 52f);
      ApplyVisualStyle(rawPool, boldText, heavyOutlineEnabled, outlineWidth, outlineAlpha, baseFontSize);
    }

    private static void ApplyVisualStyle(TextMeshProUGUI[] rawPool, bool boldText, bool heavyOutlineEnabled, float outlineWidth, float outlineAlpha, float baseFontSize)
    {
      float width = heavyOutlineEnabled ? Mathf.Clamp(outlineWidth, 0f, 0.5f) : 0f;
      Color outlineColor = new Color(0f, 0f, 0f, heavyOutlineEnabled ? Mathf.Clamp01(outlineAlpha) : 0f);
      Material outlineMaterial = GetOrCreateOutlineMaterial(rawPool, width, outlineColor);

      for (int i = 0; i < rawPool.Length; i++)
      {
        TextMeshProUGUI text = rawPool[i];
        if (text == null || !text.name.StartsWith("Danmaku Text")) continue;

        Undo.RecordObject(text, "Apply Bili Danmaku Visual Style");
        text.fontSize = Mathf.Clamp(baseFontSize, 12f, 120f);
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
          Debug.LogWarning("VizVid Bili Danmaku: could not find a TMP source material for outline. Component outline values were still applied.");
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
        Debug.LogWarning("VizVid Bili Danmaku: UI button shader was not found. Button images may trigger the VRChat SDK built-in UI shader alert.");
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
      if (material.HasProperty("_VVBDMForceMirrorFlip")) material.SetFloat("_VVBDMForceMirrorFlip", 0f);
    }

    private static void ApplyMirrorReadableShader(Material material)
    {
      Shader shader = Shader.Find(MirrorReadableShaderName);
      if (shader == null)
      {
        Debug.LogWarning("VizVid Bili Danmaku: mirror-readable TMP shader was not found. Keeping the current TMP shader.");
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
        Debug.LogWarning("VizVid Bili Danmaku: could not set VRCUrl serialized value for " + property.propertyPath + ". Set Url Prefix manually in Inspector.");
      }
    }

    private static bool HasDanmakuModule(GameObject gameObject)
    {
      if (gameObject == null) return false;
      System.Type moduleType = FindType("VizVidBiliDanmakuV3.VizVidBiliDanmakuModule3");
      if (moduleType != null) return gameObject.GetComponent(moduleType) != null;
      return gameObject.GetComponent("VizVidBiliDanmakuModule3") != null;
    }

    [MenuItem("PaulKoiPlayer/VizVid/Fix Bili Danmaku U# Program Asset", false, 2006)]
    public static void FixProgramAssetMenu()
    {
      System.Type moduleType = FindType("VizVidBiliDanmakuV3.VizVidBiliDanmakuModule3");
      if (moduleType == null)
      {
        EditorUtility.DisplayDialog("VizVid Bili Danmaku", "Runtime type not found. Run Assets > Reimport All first and check Console for C# compile errors.", "OK");
        return;
      }

      UnityEngine.Object asset = EnsureUdonSharpProgramAsset(moduleType);
      System.Type helperType = FindType("VizVidBiliDanmakuV3.VizVidBiliUrlPrefixHelper3");
      UnityEngine.Object helperAsset = helperType == null ? null : EnsureUdonSharpProgramAsset(helperType);
      if (asset == null || helperAsset == null)
      {
        EditorUtility.DisplayDialog("VizVid Bili Danmaku", "Failed to create U# Program Asset.", "OK");
        return;
      }

      Selection.activeObject = asset;
      EditorGUIUtility.PingObject(asset);
      EditorUtility.DisplayDialog("VizVid Bili Danmaku", "U# Program Assets are ready. Now run UdonSharp > Compile All UdonSharp Programs, then create the module again.", "OK");
    }

    private static UnityEngine.Object EnsureUdonSharpProgramAsset(System.Type moduleType)
    {
      MonoScript script = FindRuntimeScript(moduleType.Name + ".cs");
      if (script == null)
      {
        Debug.LogError("VizVid Bili Danmaku: cannot find " + moduleType.Name + ".cs MonoScript.");
        return null;
      }

      UnityEngine.Object existing = FindExistingProgramAsset(script);
      if (existing != null) return existing;

      System.Type programAssetType = FindType("UdonSharp.UdonSharpProgramAsset");
      if (programAssetType == null)
      {
        Debug.LogError("VizVid Bili Danmaku: cannot find UdonSharp.UdonSharpProgramAsset type.");
        return null;
      }

      string basePath = "Assets/VizVidBiliDanmakuV3/Runtime/" + moduleType.Name + ".asset";
      EnsureDirectoryForAsset(basePath);
      string path = AssetDatabase.GenerateUniqueAssetPath(basePath);

      ScriptableObject asset = ScriptableObject.CreateInstance(programAssetType);
      if (asset == null)
      {
        Debug.LogError("VizVid Bili Danmaku: failed to create UdonSharpProgramAsset instance.");
        return null;
      }

      SerializedObject serialized = new SerializedObject(asset);
      SerializedProperty source = serialized.FindProperty("sourceCsScript");
      if (source == null)
      {
        Debug.LogError("VizVid Bili Danmaku: UdonSharpProgramAsset has no sourceCsScript property in this SDK version.");
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

      Debug.Log("VizVid Bili Danmaku: Created U# Program Asset at " + path);
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
        Debug.LogWarning("VizVid Bili Danmaku: " + methodName + " failed: " + exception.Message);
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

