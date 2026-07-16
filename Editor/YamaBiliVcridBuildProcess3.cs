using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDKBase;

namespace YamaBiliDanmakuV3.Editor
{
  internal class YamaBiliVcridBuildProcess3 : IProcessSceneWithReport
  {
    public int callbackOrder => -90000;

    public void OnProcessScene(Scene scene, BuildReport report)
    {
      if (!scene.IsValid() || !scene.isLoaded) return;

      List<YamaBiliPagesPlaylist3> playlists = FindPlaylists(scene);
      if (playlists.Count == 0) return;

      Dictionary<string, VRCUrl[]> catalogs = new Dictionary<string, VRCUrl[]>();
      for (int i = 0; i < playlists.Count; i++)
      {
        YamaBiliPagesPlaylist3 playlist = playlists[i];
        SerializedObject serialized = new SerializedObject(playlist);
        SerializedProperty prefixProperty = serialized.FindProperty("_vcridUrlPrefix");
        SerializedProperty maxProperty = serialized.FindProperty("_vcridMax");
        string prefix = prefixProperty == null ? "" : prefixProperty.stringValue;
        int max = maxProperty == null ? 0 : maxProperty.intValue;

        if (string.IsNullOrEmpty(prefix) || max < 1)
        {
          Debug.LogError("Yama Bili Pages: VCRID catalog configuration is invalid.", playlist);
          continue;
        }

        string key = prefix + "\n" + max;
        if (!catalogs.TryGetValue(key, out VRCUrl[] urls))
        {
          urls = BuildCatalog(prefix, max);
          catalogs.Add(key, urls);
        }

        playlist.SetProgramVariable("_vcridUrls", urls);
        Debug.Log("Yama Bili Pages: prepared " + max + " VCRID URLs for world build.", playlist);
      }
    }

    private static List<YamaBiliPagesPlaylist3> FindPlaylists(Scene scene)
    {
      List<YamaBiliPagesPlaylist3> results = new List<YamaBiliPagesPlaylist3>();
      GameObject[] roots = scene.GetRootGameObjects();
      for (int i = 0; i < roots.Length; i++)
      {
        YamaBiliPagesPlaylist3[] found = roots[i].GetComponentsInChildren<YamaBiliPagesPlaylist3>(true);
        for (int j = 0; j < found.Length; j++)
        {
          if (found[j] != null && !IsEditorOnly(found[j].transform)) results.Add(found[j]);
        }
      }
      return results;
    }

    private static bool IsEditorOnly(Transform current)
    {
      while (current != null)
      {
        if (current.CompareTag("EditorOnly")) return true;
        current = current.parent;
      }
      return false;
    }

    private static VRCUrl[] BuildCatalog(string prefix, int max)
    {
      VRCUrl[] urls = new VRCUrl[max + 1];
      urls[0] = VRCUrl.Empty;
      for (int i = 1; i <= max; i++)
      {
        urls[i] = new VRCUrl(prefix + i);
      }
      return urls;
    }
  }
}
