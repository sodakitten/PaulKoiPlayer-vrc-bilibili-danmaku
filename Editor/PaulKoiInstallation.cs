using System;
using System.IO;
using TMPro;
using UdonSharp;
using UdonSharp.Compiler;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace YamaBiliDanmakuV3.Editor
{
  internal static class PaulKoiInstallation
  {
    private const string TmpPackageName = "TMP Essential Resources";
    private static bool importingTmp;

    internal static bool CanInstall(GameObject selected)
    {
      return selected != null && selected.scene.IsValid() && selected.scene.isLoaded &&
        !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling &&
        !EditorApplication.isUpdating && !importingTmp && PrefabStageUtility.GetCurrentPrefabStage() == null;
    }

    internal static T FindPlayer<T>(GameObject selected) where T : Component
    {
      if (selected == null || !selected.scene.IsValid()) return null;
      for (Transform current = selected.transform; current != null; current = current.parent)
      {
        T[] matches = current.GetComponentsInChildren<T>(true);
        if (matches.Length == 1) return matches[0];
        if (matches.Length > 1) return null;
      }

      T player = null;
      foreach (GameObject root in selected.scene.GetRootGameObjects())
      {
        foreach (T match in root.GetComponentsInChildren<T>(true))
        {
          if (player != null) return null;
          player = match;
        }
      }
      return player;
    }

    internal static void Select(GameObject root)
    {
      Selection.activeGameObject = root;
      EditorGUIUtility.PingObject(root);
    }

    internal static void PreparePrograms(params Type[] behaviourTypes)
    {
      foreach (Type type in behaviourTypes)
      {
        var existing = UdonSharpEditorUtility.GetUdonSharpProgramAsset(type);
        if (existing != null)
        {
          if (existing.ScriptVersion < UdonSharpProgramVersion.V1SerializationUpdate)
          {
            existing.ScriptVersion = UdonSharpProgramVersion.V1SerializationUpdate;
          }
          continue;
        }

        MonoScript script = null;
        string scriptPath = null;
        foreach (string guid in AssetDatabase.FindAssets(type.Name + " t:MonoScript", new[] { "Assets" }))
        {
          string path = AssetDatabase.GUIDToAssetPath(guid);
          MonoScript candidate = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
          if (candidate == null || candidate.GetClass() != type) continue;
          script = candidate;
          scriptPath = path;
          break;
        }
        if (script == null) throw new InvalidOperationException("PaulKoiPlayer cannot find the script for " + type.FullName);

        var program = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
        program.sourceCsScript = script;
        program.ScriptVersion = UdonSharpProgramVersion.V1SerializationUpdate;
        string assetPath = AssetDatabase.GenerateUniqueAssetPath(Path.ChangeExtension(scriptPath, ".asset"));
        AssetDatabase.CreateAsset(program, assetPath);
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
      }

      UdonSharpCompilerV1.CompileSync();
      if (UdonSharpProgramAsset.AnyUdonSharpScriptHasError())
      {
        throw new InvalidOperationException("PaulKoiPlayer installation stopped because UdonSharp reported compilation errors. See the Unity Console.");
      }
      foreach (Type type in behaviourTypes)
      {
        var program = UdonSharpEditorUtility.GetUdonSharpProgramAsset(type);
        if (program == null || program.GetRealProgram() == null ||
            program.ScriptVersion < UdonSharpProgramVersion.CurrentVersion ||
            program.CompiledVersion < UdonSharpProgramVersion.CurrentVersion)
        {
          throw new InvalidOperationException("PaulKoiPlayer could not compile the Udon program for " + type.Name);
        }
      }
    }

    internal static void Install(Func<GameObject> create, params Type[] behaviourTypes)
    {
      if (Resources.Load<TMP_Settings>("TMP Settings") == null)
      {
        GameObject selected = Selection.activeGameObject;
        ImportTextMeshProSettings(() =>
        {
          if (CanInstall(selected)) InstallPrepared(create, behaviourTypes);
          else Debug.LogWarning("PaulKoiPlayer installation was cancelled because the selected scene is no longer available.");
        });
        return;
      }

      InstallPrepared(create, behaviourTypes);
    }

    private static void InstallPrepared(Func<GameObject> create, Type[] behaviourTypes)
    {
      PreparePrograms(behaviourTypes);

      const string action = "Install PaulKoiPlayer Danmaku";
      Undo.IncrementCurrentGroup();
      int group = Undo.GetCurrentGroup();
      Undo.SetCurrentGroupName(action);
      try
      {
        GameObject root = create();
        foreach (UdonSharpBehaviour behaviour in root.GetComponentsInChildren<UdonSharpBehaviour>(true))
        {
          UdonSharpEditorUtility.CopyProxyToUdon(behaviour, ProxySerializationPolicy.RootOnly);
        }
        EditorSceneManager.MarkSceneDirty(root.scene);
        Undo.CollapseUndoOperations(group);
        Select(root);
      }
      catch
      {
        Undo.RevertAllDownToGroup(group);
        throw;
      }
    }

    private static void ImportTextMeshProSettings(Action onReady)
    {
      var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.unity.textmeshpro");
      string archive = package == null ? "" : Path.Combine(package.resolvedPath, "Package Resources", TmpPackageName + ".unitypackage");
      if (!File.Exists(archive))
      {
        throw new InvalidOperationException("Import TMP Essential Resources from Window > TextMeshPro before installing PaulKoiPlayer.");
      }
      importingTmp = true;
      double deadline = EditorApplication.timeSinceStartup + 120;
      EditorApplication.update += ContinueAfterImport;
      AssetDatabase.importPackageCancelled += Cancel;
      AssetDatabase.importPackageFailed += Fail;
      try
      {
        AssetDatabase.ImportPackage(archive, false);
      }
      catch
      {
        Unsubscribe();
        throw;
      }

      void ContinueAfterImport()
      {
        if (EditorApplication.timeSinceStartup >= deadline)
        {
          Fail(TmpPackageName, "Import did not finish.");
          return;
        }
        if (EditorApplication.isUpdating || EditorApplication.isCompiling || Resources.Load<TMP_Settings>("TMP Settings") == null) return;
        Unsubscribe();
        onReady();
      }

      void Cancel(string name)
      {
        Fail(name, "Import cancelled.");
      }

      void Fail(string name, string reason)
      {
        if (Path.GetFileNameWithoutExtension(name) != TmpPackageName) return;
        Unsubscribe();
        Debug.LogError("PaulKoiPlayer could not import TMP Essential Resources. " + reason);
      }

      void Unsubscribe()
      {
        importingTmp = false;
        EditorApplication.update -= ContinueAfterImport;
        AssetDatabase.importPackageCancelled -= Cancel;
        AssetDatabase.importPackageFailed -= Fail;
      }
    }
  }
}
