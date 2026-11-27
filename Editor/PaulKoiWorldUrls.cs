using System;
using System.Collections.Generic;
using UdonSharp;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.Core;

namespace YamaBiliDanmakuV3.Editor
{
  internal sealed class PaulKoiWorldUrls : IProcessSceneWithReport
  {
    private const string WorldParameter = "__world";
    public int callbackOrder => -90001;

    public void OnProcessScene(Scene scene, BuildReport report)
    {
      if (!scene.IsValid() || !scene.isLoaded) return;
      string worldId = GetWorldId(scene);
      foreach (GameObject root in scene.GetRootGameObjects())
      {
        foreach (YamaBiliUrlPrefixHelper3 helper in root.GetComponentsInChildren<YamaBiliUrlPrefixHelper3>(true))
        {
          if (helper != null && !IsEditorOnly(helper.transform))
          {
            StampUrl(helper, "_urlPrefix", worldId);
          }
        }
      }
    }

    internal static string GetWorldId(Scene scene)
    {
      foreach (GameObject root in scene.GetRootGameObjects())
      {
        foreach (PipelineManager manager in root.GetComponentsInChildren<PipelineManager>(true))
        {
          if (manager == null || IsEditorOnly(manager.transform)) continue;
          string id = manager.blueprintId;
          if (!string.IsNullOrEmpty(id) && id.StartsWith("wrld_", StringComparison.OrdinalIgnoreCase) &&
              Guid.TryParseExact(id.Substring(5), "D", out Guid worldGuid))
          {
            return "wrld_" + worldGuid.ToString("D");
          }
        }
      }
      return "";
    }

    internal static bool IsEditorOnly(Transform current)
    {
      while (current != null)
      {
        if (current.CompareTag("EditorOnly")) return true;
        current = current.parent;
      }
      return false;
    }

    internal static void StampUrl(UdonSharpBehaviour behaviour, string field, string worldId)
    {
      var serialized = new SerializedObject(behaviour);
      SerializedProperty url = serialized.FindProperty(field)?.FindPropertyRelative("url");
      if (url == null || string.IsNullOrEmpty(url.stringValue)) return;
      string before = url.stringValue;
      string after = WithWorldId(before, "url", worldId);
      if (after == before) return;
      url.stringValue = after;
      serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    internal static string WithWorldId(string prefix, string target, string worldId)
    {
      if (!Uri.TryCreate(prefix, UriKind.Absolute, out Uri uri) ||
          (uri.Scheme != "http" && uri.Scheme != "https") || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 ||
          (uri.AbsolutePath != "/player/" && uri.AbsolutePath != "/player" && uri.AbsolutePath != "/api/pages"))
      {
        return prefix;
      }

      string[] fields = uri.Query.TrimStart('?').Split('&');
      if (fields[fields.Length - 1] != target + "=") return prefix;

      var parts = new List<string>();
      if (!string.IsNullOrEmpty(worldId)) parts.Add(WorldParameter + "=" + worldId);
      foreach (string field in fields)
      {
        if (field.Length == 0) continue;
        int equals = field.IndexOf('=');
        string name = Uri.UnescapeDataString(equals < 0 ? field : field.Substring(0, equals));
        if (name != WorldParameter) parts.Add(field);
      }
      return uri.GetLeftPart(UriPartial.Path) + "?" + string.Join("&", parts);
    }
  }
}
