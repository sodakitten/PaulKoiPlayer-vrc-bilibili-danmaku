#if PAGES_BACKEND_SYNC_TESTS
// Test-only SDK adapters. Production methods/fields are inserted by the PowerShell runner.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Text.Json;
namespace PagesBackendSync
{
    public interface IUdonEventReceiver { }
    public interface IVRCStringDownload { VRCUrl Url { get; } string Result { get; } int ErrorCode { get; } }
    public class Download : IVRCStringDownload
    {
        public VRCUrl Url { get; set; }
        private string body;
        public bool ThrowOnResult;
        public string Result { get { if (ThrowOnResult) throw new Exception("SDK Result unavailable on error"); return body; } set { body = value; } }
        public int ErrorCode { get; set; }
        public Download(string url, string body = "", int code = 500) { Url = new VRCUrl(url); Result = body; ErrorCode = code; }
    }
    public class VRCUrl
    {
        private string value;
        public VRCUrl(string s) { value = s; }
        public string Get() { return value; }
        public static VRCUrl Empty = new VRCUrl("");
        public static bool IsNullOrEmpty(VRCUrl u) { return u == null || string.IsNullOrEmpty(u.value); }
    }
    public static class Utilities { public static bool IsValid(object o) { return o != null; } }
    public class VRCPlayerApi { public int playerId; }
    public static class Networking
    {
        public static VRCPlayerApi LocalPlayer = new VRCPlayerApi { playerId = 1 };
        public static bool IsOwner(object o) { return ((Controller)o).OwnerId == 1; }
        public static VRCPlayerApi GetOwner(object o) { return new VRCPlayerApi { playerId = ((Controller)o).OwnerId }; }
    }
    public static class Time { public static float time; }
    public static class Mathf
    {
        public static int Min(int a, int b) { return Math.Min(a, b); }
        public static int Max(int a, int b) { return Math.Max(a, b); }
        public static float Max(float a, float b) { return Math.Max(a, b); }
        public static int RoundToInt(float x) { return (int)Math.Round(x); }
    }
    public class Track
    {
        public VRCUrl Url;
        public string Title = "";
        public bool IsValid() { return Url != null; }
        public VRCUrl GetVRCUrl() { return Url; }
        public bool HasTitle() { return !string.IsNullOrEmpty(Title); }
        public string GetTitle() { return Title; }
        public void SetTitle(string s) { Title = s; }
        public static Track New(int type, string title, VRCUrl url) { return new Track { Url = url, Title = title }; }
    }
    public class Playlist
    {
        public List<Track> Items = new List<Track>();
        public int Added;
        public int Length { get { return Items.Count; } }
        public Track GetTrack(int i) { return Items[i]; }
        public void TakeOwnership() { }
        public void SendEvent() { }
        public void AddTrack(Track t) { Items.Add(t); Added++; }
    }
    public class Controller
    {
        public int OwnerId = 1, VideoPlayerType, Played;
        public bool Stopped, IsLoading;
        public Track Track = Track.New(0, "", new VRCUrl("A"));
        public Playlist Queue = new Playlist();
        public object gameObject { get { return this; } }
        public object GetProgramVariable(string s) { return VRCUrl.Empty; }
        public void TakeOwnership() { OwnerId = 1; }
        public void RequestSerialization() { }
        public void PlayTrack(Track t) { Track = t; Played++; }
    }
    public class YamaBiliDanmakuModule3
    {
        public bool AudioMode;
        public List<VRCUrl> Lyrics = new List<VRCUrl>();
        public void SetExternalAudioMode(bool mode) { AudioMode = mode; }
        public void LoadDanmakuUrl(VRCUrl url) { Lyrics.Add(url); }
    }
    public enum TokenType { None, String, Number, DataDictionary, DataList }
    public struct DataToken
    {
        public object Value;
        public TokenType TokenType;
        public bool IsNumber { get { return TokenType == TokenType.Number; } }
        public double Number { get { return (double)Value; } }
        public string String { get { return (string)Value; } }
        public DataDictionary DataDictionary { get { return (DataDictionary)Value; } }
        public DataList DataList { get { return (DataList)Value; } }
    }
    public class DataDictionary : Dictionary<string, DataToken>
    {
        public bool TryGetValue(string key, TokenType type, out DataToken value) { return TryGetValue(key, out value) && value.TokenType == type; }
    }
    public class DataList : List<DataToken> { }
    public static class VRCJson
    {
        public static bool TryDeserializeFromJson(string s, out DataToken token)
        {
            token = default(DataToken);
            try { using (var doc = JsonDocument.Parse(s)) token = Convert(doc.RootElement); return true; }
            catch (JsonException) { return false; }
        }
        private static DataToken Convert(JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.String) return new DataToken { Value = e.GetString(), TokenType = TokenType.String };
            if (e.ValueKind == JsonValueKind.Number) return new DataToken { Value = e.GetDouble(), TokenType = TokenType.Number };
            if (e.ValueKind == JsonValueKind.Array)
            {
                var list = new DataList(); foreach (var item in e.EnumerateArray()) list.Add(Convert(item));
                return new DataToken { Value = list, TokenType = TokenType.DataList };
            }
            if (e.ValueKind == JsonValueKind.Object)
            {
                var dict = new DataDictionary(); foreach (var item in e.EnumerateObject()) dict[item.Name] = Convert(item.Value);
                return new DataToken { Value = dict, TokenType = TokenType.DataDictionary };
            }
            return default(DataToken);
        }
    }
    public class StubBehaviour : IUdonEventReceiver
    {
        public List<string> Sent = new List<string>();
        public List<string> Events = new List<string>();
        public List<float> Delays = new List<float>();
        public virtual void OnStringLoadSuccess(IVRCStringDownload r) { }
        public virtual void OnStringLoadError(IVRCStringDownload r) { }
        public void SendCustomEventDelayedSeconds(string name, float seconds) { Events.Add(name); Delays.Add(seconds); }
        public void SendCustomEventDelayedFrames(string name, int frames) { Events.Add(name); }
    }
    public static class VRCStringDownloader
    {
        public static void LoadUrl(VRCUrl url, IUdonEventReceiver receiver) { ((StubBehaviour)receiver).Sent.Add(url.Get()); }
    }
    public class PagesHarness : StubBehaviour
    {
        /* PRODUCTION_METHODS */
        public string Status = "", DisplayTitle = "";
        public int ManifestEntries, PlayedManifest = -1, QueuedManifest, AddedParsed, StandaloneEntries, StandaloneSelected = -1;
        private void SetStatus(string s) { Status = s; }
        private void SetTitle(string s) { DisplayTitle = s; }
        private void UpdateLabels() { }
        private void DiscardPreservedStandaloneManifest() { _preserveStandaloneManifestRequest = false; }
        private void PreserveStandaloneManifestForQueueRequest() { _preserveStandaloneManifestRequest = true; }
        private void ExitStandaloneManifestModeForUnifiedQueueRequest() { _standaloneManifestMode = false; }
        private void RestorePreservedStandaloneManifest() { _preserveStandaloneManifestRequest = false; }
        private void ActivateSyncedBiliManifestFromParsed() { ManifestEntries++; }
        private void PublishManifestState(VRCUrl u, int id, bool active) { }
        private void EnterStandaloneManifestMode(int mode, VRCUrl source) { StandaloneEntries++; StandaloneSelected = _selectedIndex; }
        private void ReplaceQueuedTrackWithParsedTracks(VRCUrl u, int i, string s) { AddedParsed++; }
        private string GetParsedQueueTitle(int i) { return _parsedSourceTitle; }
        private bool HasRetainedBiliManifestItems() { return false; }
        private int GetRetainedHistoryDisplayCount() { return 0; }
        private bool HasCurrentUnifiedTrack() { return !_controller.Stopped; }
        private void MergeUnifiedQueueHeader(VRCUrl u, string title, bool other) { }
        private string BuildParsedTrackTitle(int i) { return _parts[i]; }
        private void PrepareCurrentTrackRetention(VRCUrl u) { }
        private void AddParsedTracksToQueue(int first, VRCUrl source, bool start, bool expand) { AddedParsed++; }
        private void ClampUnifiedPageOffset() { }
        private void PublishUnifiedQueueHeader(string s, VRCUrl u, bool a, bool b) { }
        private int GetUnifiedDisplayCount() { return _controller.Queue.Length; }
        private void SetQueuedManifestPlaceholderTitle(int index, string url, string title) { _controller.Queue.GetTrack(index).SetTitle(title); }
        private void AddBiliManifestPlaceholderToQueue(VRCUrl source, int selected) { QueuedManifest++; }
        private void CleanupActiveBiliManifestQueueEntries() { ManifestEntries++; }
        private void SuppressControllerForward() { }
        private int GetBiliManifestVcrid(int i) { return _biliManifestVcrids[i]; }
        private string GetBiliManifestPart(int i) { return _biliManifestParts[i]; }
        private void PlayBiliManifestIndex(int i) { PlayedManifest = i; }
        private void InvalidateUnifiedDisplayCache() { }
        private void HideAllDeleteIcons() { }
        private int GetVisibleManifestItemCount() { return _biliManifestTotalPages; }
        private void RefreshBiliPlaybackSource(string url) { }
        private void ConfigureCurrentTrackDanmaku() { }
    }
}
#endif
