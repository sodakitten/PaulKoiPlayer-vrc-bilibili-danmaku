using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace YamaBiliDanmakuV3.Editor
{
  public static class PaulKoiBackendProtocol3
  {
    [MenuItem("Tools/PaulKoiPlayer/Backend/Enable Metadata V2 on Selected Player")]
    private static void EnableSelected() { ConfigureSelected(true); }

    [MenuItem("Tools/PaulKoiPlayer/Backend/Use Legacy Protocol on Selected Player")]
    private static void DisableSelected() { ConfigureSelected(false); }

    private static void ConfigureSelected(bool enabled)
    {
      GameObject selected = Selection.activeGameObject;
      if (selected == null) throw new InvalidOperationException("Select the player root in the scene first.");
      YamaBiliPagesPlaylist3[] playlists = selected.GetComponentsInChildren<YamaBiliPagesPlaylist3>(true);
      if (playlists.Length != 1)
        throw new InvalidOperationException("Select a root containing exactly one PaulKoiPlayer playlist.");

      YamaBiliPagesPlaylist3 playlist = playlists[0];
      SerializedObject pages = new SerializedObject(playlist);
      Component helper = pages.FindProperty("_urlPrefixHelper").objectReferenceValue as Component;
      YamaBiliDanmakuModule3 module = pages.FindProperty("_danmakuModule").objectReferenceValue as YamaBiliDanmakuModule3;
      if (helper == null || module == null)
        throw new InvalidOperationException("Repair the playlist's input helper and danmaku references first.");

      SerializedObject input = new SerializedObject(helper);
      SerializedProperty inputPrefix = input.FindProperty("_urlPrefix").FindPropertyRelative("url");
      SerializedProperty catalogPrefix = pages.FindProperty("_vcridUrlPrefix");
      SerializedProperty pagesPrefix = pages.FindProperty("_pagesApiPrefix").FindPropertyRelative("url");
      SerializedObject danmaku = new SerializedObject(module);
      SerializedProperty modulePages = danmaku.FindProperty("_pagesPlaylist");
      if (inputPrefix == null || catalogPrefix == null || pagesPrefix == null || modulePages == null)
        throw new InvalidOperationException("Update all frontend scripts before changing protocol.");

      // Validate everything before modifying a scene; preserve custom domains and query settings.
      string source = WithVersion(inputPrefix.stringValue, "url", enabled);
      string catalog = WithVersion(catalogPrefix.stringValue, "vcrid", enabled);
      if (new Uri(source).GetLeftPart(UriPartial.Path) != new Uri(catalog).GetLeftPart(UriPartial.Path))
        throw new InvalidOperationException("Input and VCRID catalog must use the same player endpoint.");

      if (!EditorUtility.DisplayDialog("PaulKoiPlayer backend protocol",
          "Input: " + source + "\nVCRID: " + catalog +
          "\n\nRecompile UdonSharp and rebuild the world afterwards. This does not change the server switch.",
          "Apply", "Cancel")) return;

      Undo.RecordObjects(new UnityEngine.Object[] { playlist, helper, module }, "Change PaulKoiPlayer protocol");
      inputPrefix.stringValue = source;
      catalogPrefix.stringValue = catalog;
      pagesPrefix.stringValue = source;
      modulePages.objectReferenceValue = playlist;
      input.ApplyModifiedProperties();
      pages.ApplyModifiedProperties();
      danmaku.ApplyModifiedProperties();
      foreach (Component component in new Component[] { playlist, helper, module })
      {
        PrefabUtility.RecordPrefabInstancePropertyModifications(component);
        EditorUtility.SetDirty(component);
      }
      EditorSceneManager.MarkSceneDirty(playlist.gameObject.scene);
    }

    internal static string WithVersion(string prefix, string parameter, bool enabled)
    {
      if (!Uri.TryCreate(prefix, UriKind.Absolute, out Uri uri) ||
          (uri.Scheme != "https" && uri.Scheme != "http") ||
          !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment) ||
          (uri.AbsolutePath != "/player/" && uri.AbsolutePath != "/player"))
        throw new ArgumentException("Expected an absolute HTTP(S) /player/ prefix without credentials or fragment.");
      string[] fields = uri.Query.TrimStart('?').Split('&');
      List<string> kept = new List<string>();
      int targets = 0;
      for (int i = 0; i < fields.Length; i++)
      {
        if (fields[i].Length == 0) continue;
        int equals = fields[i].IndexOf('=');
        string key = Uri.UnescapeDataString(equals < 0 ? fields[i] : fields[i].Substring(0, equals));
        if (key == "__pkdm") continue;
        if (key == parameter)
        {
          if (equals < 0 || equals != fields[i].Length - 1 || i != fields.Length - 1)
            throw new ArgumentException("The prefix must end in an empty " + parameter + "= parameter.");
          targets++;
        }
        else
        {
          if (key == "url" || key == "vcrid" || key == "__dm")
            throw new ArgumentException("Do not force text or mix URL and VCRID identifiers in a playback prefix.");
          kept.Add(fields[i]);
        }
      }
      if (targets != 1) throw new ArgumentException("Expected exactly one " + parameter + "= parameter.");
      if (enabled) kept.Add("__pkdm=2");
      kept.Add(parameter + "=");
      return uri.GetLeftPart(UriPartial.Path) + "?" + string.Join("&", kept.ToArray());
    }
  }
}
