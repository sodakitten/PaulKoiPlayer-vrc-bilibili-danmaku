using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Components.Video;
using VRC.SDK3.Data;
using VRC.SDK3.StringLoading;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;
using Yamadev.YamaStream;
using Yamadev.YamaStream.Libraries.GenericDataContainer;

namespace YamaBiliDanmakuV3
{
  [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
  public class YamaBiliPagesPlaylist3 : Listener
  {
    private const int VisibleButtonCount = 6;
    private const int MaxUnifiedQueueItems = 200;
    private const int RequestModeNone = 0;
    private const int RequestModeEnqueue = 1;
    private const int RequestModeExpandCurrent = 2;
    private const int RequestModeNormalizeQueued = 3;
    private const int DisplaySourceHistory = 1;
    private const int DisplaySourceCurrent = 2;
    private const int DisplaySourceQueue = 3;
    private const int DisplaySourceManifest = 4;
    private const int ManifestModeNone = 0;
    private const int ManifestModeBilibiliMixed = 1;
    private const int ManifestModeNeteaseExclusive = 2;

    [Header("YamaPlayer")]
    [SerializeField] private Controller _controller;
    [SerializeField] private YamaBiliDanmakuModule3 _danmakuModule;
    [SerializeField] private YamaBiliUrlPrefixHelper3 _urlPrefixHelper;

    [Header("Pages API")]
    [SerializeField] private VRCUrl _pagesApiPrefix = VRCUrl.Empty;
    [SerializeField] private bool _autoRefreshOnPlayback = true;
    [SerializeField] private bool _autoPlayNext = true;
    [SerializeField, Range(0.25f, 3f)] private float _playbackWatchSeconds = 0.5f;

    [Header("VCRID Catalog")]
    [SerializeField] private string _vcridUrlPrefix = "https://danmaku.paulkoishi.com/player/?vcrid=";
    [SerializeField, Range(1, 1000000)] private int _vcridMax = 1000000;
    [SerializeField, HideInInspector] private VRCUrl[] _vcridUrls = new VRCUrl[0];

    [Header("UI")]
    [SerializeField] private TextMeshProUGUI _titleLabel;
    [SerializeField] private RectTransform _titleRect;
    [SerializeField] private RectTransform _titleViewportRect;
    [SerializeField] private TextMeshProUGUI _statusLabel;
    [SerializeField] private TextMeshProUGUI _playModeButtonLabel;
    [SerializeField] private TextMeshProUGUI[] _pageButtonLabels = new TextMeshProUGUI[VisibleButtonCount];
    [SerializeField] private Image[] _deleteButtonIcons = new Image[VisibleButtonCount];

    [Header("Unified Queue")]
    [SerializeField] private bool _useUnifiedQueue = true;

    [Header("Playlist Title Marquee")]
    [SerializeField, Range(8f, 80f)] private float _marqueePixelsPerSecond = 28f;
    [SerializeField, Range(0.04f, 0.25f)] private float _marqueeTickSeconds = 0.08f;
    [SerializeField, Range(0.2f, 3f)] private float _marqueePauseSeconds = 1.2f;

    [UdonSynced] private VRCUrl _syncedManifestUrl = VRCUrl.Empty;
    [UdonSynced] private int _syncedSelectedVcrid;
    [UdonSynced] private int _syncedRevision;
    [UdonSynced] private bool _syncedManifestActive;
    [UdonSynced] private string _syncedQueueTitle = "";
    [UdonSynced] private VRCUrl _syncedQueueSourceUrl = VRCUrl.Empty;
    [UdonSynced] private bool _syncedQueueMixed;
    [UdonSynced] private bool _syncedQueueManaged;
    [UdonSynced] private int _syncedQueueRevision;
    [UdonSynced] private string _syncedPendingRetainedDeleteUrl = "";
    [UdonSynced] private string _syncedPendingRetainUrl = "";
    [UdonSynced] private string _syncedPendingRetainMarkerUrl = "";
    [UdonSynced] private int _syncedManifestMode;
    [UdonSynced] private int[] _syncedDeletedManifestVcrids = new int[0];

    private string[] _parts = new string[0];
    private string[] _playUrls = new string[0];
    private string[] _danmakuUrls = new string[0];
    private int[] _pageNumbers = new int[0];
    private int[] _vcrids = new int[0];
    private int _totalPages;
    private int _selectedIndex = -1;
    private int _pageOffset;
    private bool _pageViewPinned;
    private bool _loading;
    private string _lastRequestUrl = "";
    private string _lastObservedPlaybackUrl = "";
    private float _requestStartedAt;
    private bool _isNeteasePlaylist;
    private bool _parsedShouldCycle;
    private bool _autoAdvancePending;
    private int _pendingNextIndex = -1;
    private bool _ignorePlaybackUrlWhileStopped;
    private bool _internalTrackSwitch;
    private int _pendingStandaloneVcrid;
    private VRCUrl _lastManifestSourceUrl = VRCUrl.Empty;
    private bool _syncedManifestLoadPending;
    private float _marqueeElapsed;
    private bool _needsNeteaseSongMetadata;
    private string _pendingNeteaseMetadataUrl = "";
    private int _pendingNeteaseMetadataIndex = -1;
    private string _parsedSourceTitle = "";
    private bool _parsedIsBilibiliList;
    private int _unifiedRequestMode;
    private VRCUrl _unifiedRequestSourceUrl = VRCUrl.Empty;
    private bool _unifiedStartWhenReady;
    private bool _completeUnifiedRequestAfterMetadata;
    private string _expandedPlaybackSourceUrl = "";
    private string _lastConfiguredDanmakuUrl = "";
    private bool _lastConfiguredDanmakuWasNetease;
    private bool _cycleAppendPending;
    private string[] _visibleDeleteUrls = new string[VisibleButtonCount];
    private bool _unifiedQueueLimitReached;
    private int _normalizeQueueIndex = -1;
    private string _normalizeQueueTrackUrl = "";
    private bool _normalizeQueueScheduled;
    private bool _naturalEndPending;
    private bool _manualStopAdvancePending;
    private bool _clearingUnifiedQueue;
    private bool _standaloneManifestMode;
    private bool _biliMixedManifestMode;
    private bool _currentPlaybackIsManifestItem;
    private bool _biliManifestPlaybackLocked;
    private bool _advanceQueueAfterManifestPending;
    private bool _controllerForwardSuppressed;
    private float _savedControllerForwardInterval;
    private bool _editingRetainedHistory;
    private int[] _retainedHistoryIndexes = new int[0];
    private string[] _retainedHistoryUrls = new string[0];
    private int _retainedHistoryCacheLength = -1;
    private string _retainedHistoryCacheSuppressedUrl = "";
    private int[] _unifiedDisplaySourceTypes = new int[0];
    private int[] _unifiedDisplaySourceIndexes = new int[0];
    private bool _unifiedDisplayCacheDirty = true;
    private string _lastUnifiedDisplayDebugState = "";
    private string[] _biliManifestParts = new string[0];
    private int[] _biliManifestPageNumbers = new int[0];
    private int[] _biliManifestVcrids = new int[0];
    private int _biliManifestTotalPages;
    private int _biliManifestSelectedIndex = -1;
    private string _biliManifestTitle = "";
    private VRCUrl _biliManifestSourceUrl = VRCUrl.Empty;
    private bool _preserveStandaloneManifestRequest;
    private string[] _preservedStandaloneParts = new string[0];
    private string[] _preservedStandalonePlayUrls = new string[0];
    private string[] _preservedStandaloneDanmakuUrls = new string[0];
    private int[] _preservedStandalonePageNumbers = new int[0];
    private int[] _preservedStandaloneVcrids = new int[0];
    private int _preservedStandaloneTotalPages;
    private int _preservedStandaloneSelectedIndex = -1;
    private int _preservedStandalonePageOffset;
    private string _preservedStandaloneSourceTitle = "";
    private bool _preservedStandaloneIsNetease;
    private bool _preservedStandaloneShouldCycle;
    private bool _preservedStandaloneIsBilibiliList;
    private VRCUrl _preservedStandaloneSourceUrl = VRCUrl.Empty;
    private bool _preservedStandaloneNeedsNeteaseMetadata;
    private string _preservedStandaloneMetadataUrl = "";
    private int _preservedStandaloneMetadataIndex = -1;
    private int _pendingStandaloneQueueRemovalIndex = -1;
    private string _pendingStandaloneQueueRemovalUrl = "";

    private void Start()
    {
      if (!Utilities.IsValid(_controller)) _controller = FindControllerInParents();
      if (!Utilities.IsValid(_danmakuModule)) _danmakuModule = GetComponentInParent<YamaBiliDanmakuModule3>();
      if (Utilities.IsValid(_controller))
      {
        _ignorePlaybackUrlWhileStopped = _controller.Stopped;
        _controller.AddListener(this);
      }
      UpdateLabels();
      UpdatePlayModeButtonLabel();
      SetStatus(_useUnifiedQueue ? "等待播放队列" : "等待播放列表");
      if (_useUnifiedQueue)
      {
        if (_syncedManifestActive)
        {
          ApplySyncedManifestModeFlags();
          ApplySyncedManifestState();
        }
        else
        {
          EnsureHistoryOwnershipForControllerOwner();
          SendCustomEventDelayedFrames(nameof(ApplyPendingCurrentRetention), 1);
          SendCustomEventDelayedFrames(nameof(CleanupPendingRetainedDelete), 1);
          ConfigureCurrentTrackDanmaku();
          ScheduleNormalizeQueue();
        }
      }
      else ApplySyncedManifestState();
      if (_autoRefreshOnPlayback) SendCustomEventDelayedSeconds(nameof(WatchPlaybackUrl), _playbackWatchSeconds);
      SendCustomEventDelayedSeconds(nameof(AdvanceTitleMarquee), GetMarqueeTickSeconds());
    }

    public void AdvanceTitleMarquee()
    {
      if ((_useUnifiedQueue && !_standaloneManifestMode && GetUnifiedDisplayCount() > 0) ||
          ((!_useUnifiedQueue || _standaloneManifestMode) && _totalPages > 0))
      {
        _marqueeElapsed += GetMarqueeTickSeconds();
        if (_marqueeElapsed > 100000f) _marqueeElapsed = 0f;
        UpdateTitleMarqueePosition();
      }
      SendCustomEventDelayedSeconds(nameof(AdvanceTitleMarquee), GetMarqueeTickSeconds());
    }

    private float GetMarqueeTickSeconds()
    {
      return _marqueeTickSeconds < 0.04f ? 0.08f : _marqueeTickSeconds;
    }

    private float GetMarqueeSpeed()
    {
      return _marqueePixelsPerSecond < 8f ? 28f : _marqueePixelsPerSecond;
    }

    private float GetMarqueePauseSeconds()
    {
      return _marqueePauseSeconds < 0.2f ? 1.2f : _marqueePauseSeconds;
    }

    public void RefreshPages()
    {
      _pageOffset = 0;
      _pageViewPinned = true;
      UpdateLabels();
      if (_useUnifiedQueue && !_standaloneManifestMode)
      {
        SetStatus(GetUnifiedDisplayCount() > 0 ? "已返回首页" : "暂无播放队列");
        return;
      }
      SetStatus(_totalPages > 0 ? "已返回首页" : "暂无播放列表");
    }

    public void WatchPlaybackUrl()
    {
      if (!_autoRefreshOnPlayback) return;

      if (_useUnifiedQueue && !_standaloneManifestMode)
      {
        WatchUnifiedQueue();
        SendCustomEventDelayedSeconds(nameof(WatchPlaybackUrl), _playbackWatchSeconds);
        return;
      }

      if (_syncedManifestLoadPending)
      {
        SendCustomEventDelayedSeconds(nameof(WatchPlaybackUrl), _playbackWatchSeconds);
        return;
      }

      bool controllerStopped = Utilities.IsValid(_controller) && _controller.Stopped;
      bool controllerLoading = Utilities.IsValid(_controller) && _controller.IsLoading;
      if (_autoAdvancePending || _internalTrackSwitch)
      {
        SendCustomEventDelayedSeconds(nameof(WatchPlaybackUrl), _playbackWatchSeconds);
        return;
      }

      if (_ignorePlaybackUrlWhileStopped && controllerStopped && !controllerLoading)
      {
        SendCustomEventDelayedSeconds(nameof(WatchPlaybackUrl), _playbackWatchSeconds);
        return;
      }
      if (!controllerStopped || controllerLoading) _ignorePlaybackUrlWhileStopped = false;

      VRCUrl playbackUrl = GetCurrentControllerUrl();
      string request = GetUrlString(playbackUrl);
      if (request != _lastObservedPlaybackUrl)
      {
        if (_totalPages > 0 && SelectManifestItemFromUrl(request))
        {
          _lastObservedPlaybackUrl = request;
          UpdateLabels();
          int selectedVcrid = GetSelectedVcrid();
          if (Utilities.IsValid(_controller) &&
              Networking.IsOwner(_controller.gameObject) &&
              selectedVcrid != _syncedSelectedVcrid)
          {
            PublishManifestState(GetManifestSourceUrl(), selectedVcrid, true);
          }
          SendCustomEventDelayedSeconds(nameof(WatchPlaybackUrl), _playbackWatchSeconds);
          return;
        }

        if (_totalPages > 1 && IsVcridPlaybackUrl(request))
        {
          _lastObservedPlaybackUrl = request;
          SetStatus("保持当前播放列表");
          SendCustomEventDelayedSeconds(nameof(WatchPlaybackUrl), _playbackWatchSeconds);
          return;
        }

        if (_totalPages > 1 &&
            !string.IsNullOrEmpty(request) &&
            !IsManifestSourceRequestUrl(request))
        {
          _lastObservedPlaybackUrl = request;
          SetStatus("保持当前播放列表");
          SendCustomEventDelayedSeconds(nameof(WatchPlaybackUrl), _playbackWatchSeconds);
          return;
        }

        if (_totalPages > 0 && string.IsNullOrEmpty(request) && (!controllerStopped || controllerLoading))
        {
          SendCustomEventDelayedSeconds(nameof(WatchPlaybackUrl), _playbackWatchSeconds);
          return;
        }

        if (!IsVcridPlaybackUrl(request)) _internalTrackSwitch = false;
        _lastObservedPlaybackUrl = request;
        if (!string.IsNullOrEmpty(request) &&
            IsUsableRequestUrl(playbackUrl) &&
            IsPagesRequestUrl(request))
        {
          if (_useUnifiedQueue && _standaloneManifestMode)
          {
            BeginUnifiedSourceRequest(playbackUrl, RequestModeExpandCurrent, false);
          }
          else
          {
            ClearPages("正在加载播放列表");
            BeginPagesRequest(playbackUrl);
          }
        }
        else
        {
          _loading = false;
          _lastRequestUrl = "";
          ClearPages("当前内容没有播放列表");
        }
      }

      SendCustomEventDelayedSeconds(nameof(WatchPlaybackUrl), _playbackWatchSeconds);
    }

    private void WatchUnifiedQueue()
    {
      if (!Utilities.IsValid(_controller)) return;

      ScheduleNormalizeQueue();

      VRCUrl playbackUrl = GetCurrentControllerUrl();
      string request = GetUrlString(playbackUrl);
      if (request != _lastObservedPlaybackUrl)
      {
        _lastObservedPlaybackUrl = request;
        if (_biliMixedManifestMode) RefreshBiliPlaybackSource(request);
        UpdateLabels();
        ConfigureCurrentTrackDanmaku();
      }
      if (string.IsNullOrEmpty(request))
      {
        _expandedPlaybackSourceUrl = "";
        return;
      }

      if (_unifiedRequestMode != RequestModeNone || _loading) return;
      if (!Networking.IsOwner(_controller.gameObject)) return;
      if (!IsVcridPlaybackUrl(request) && IsPagesRequestUrl(request) && request != _expandedPlaybackSourceUrl)
      {
        BeginUnifiedSourceRequest(playbackUrl, RequestModeExpandCurrent, false);
      }
    }

    private void ScheduleNormalizeQueue()
    {
      if (!_useUnifiedQueue || _standaloneManifestMode || _normalizeQueueScheduled) return;
      _normalizeQueueScheduled = true;
      SendCustomEventDelayedFrames(nameof(NormalizeNextQueuedTrack), 1);
    }

    public void NormalizeNextQueuedTrack()
    {
      _normalizeQueueScheduled = false;
      if (!_useUnifiedQueue || _standaloneManifestMode || !Utilities.IsValid(_controller)) return;
      if (!Networking.IsOwner(_controller.gameObject)) return;
      if (_loading || _unifiedRequestMode != RequestModeNone)
      {
        _normalizeQueueScheduled = true;
        SendCustomEventDelayedSeconds(nameof(NormalizeNextQueuedTrack), 0.25f);
        return;
      }

      Playlist queue = _controller.Queue;
      if (!Utilities.IsValid(queue)) return;
      for (int i = 0; i < queue.Length; i++)
      {
        Track track = queue.GetTrack(i);
        if (!IsUsableTrack(track) || track.HasTitle()) continue;

        string request = GetTrackVrcUrl(track);
        if (IsVcridPlaybackUrl(request) || !IsPagesRequestUrl(request)) continue;

        _normalizeQueueIndex = i;
        _normalizeQueueTrackUrl = request;
        BeginUnifiedSourceRequest(track.GetVRCUrl(), RequestModeNormalizeQueued, false);
        return;
      }
    }

    private void BeginPagesRequest(VRCUrl requestUrl)
    {
      string request = GetUrlString(requestUrl);
      _loading = true;
      _lastRequestUrl = request;
      _lastObservedPlaybackUrl = request;
      _requestStartedAt = Time.time;
      SetStatus("正在加载播放列表");
      VRCStringDownloader.LoadUrl(requestUrl, (IUdonEventReceiver)this);
      SendCustomEventDelayedSeconds(nameof(CheckPagesTimeout), 15f);
    }

    public bool QueueInputUrl(VRCUrl inputUrl)
    {
      if (!_useUnifiedQueue || !Utilities.IsValid(_controller) || !IsUsableRequestUrl(inputUrl)) return false;
      if (_loading || _unifiedRequestMode != RequestModeNone || !string.IsNullOrEmpty(_pendingNeteaseMetadataUrl))
      {
        SetStatus("正在解析上一条链接");
        return false;
      }

      bool startWhenReady = _controller.Stopped && !_controller.IsLoading &&
                            (!Utilities.IsValid(_controller.Queue) || _controller.Queue.Length == 0);
      if (_standaloneManifestMode) startWhenReady = false;
      BeginUnifiedSourceRequest(inputUrl, RequestModeEnqueue, startWhenReady);
      return true;
    }

    private void BeginUnifiedSourceRequest(VRCUrl requestUrl, int requestMode, bool startWhenReady)
    {
      if (_standaloneManifestMode && requestMode == RequestModeEnqueue)
        PreserveStandaloneManifestForQueueRequest();
      else if (_standaloneManifestMode)
        ExitStandaloneManifestModeForUnifiedQueueRequest();
      if (requestMode != RequestModeNormalizeQueued)
      {
        _normalizeQueueIndex = -1;
        _normalizeQueueTrackUrl = "";
      }
      ResetParsedSource();
      _unifiedRequestMode = requestMode;
      _unifiedRequestSourceUrl = requestUrl;
      _unifiedStartWhenReady = startWhenReady;
      _completeUnifiedRequestAfterMetadata = false;
      _unifiedQueueLimitReached = false;
      _loading = true;
      _lastRequestUrl = GetUrlString(requestUrl);
      _requestStartedAt = Time.time;
      SetStatus(requestMode == RequestModeEnqueue ? "正在解析队列链接" : "正在展开当前内容");
      VRCStringDownloader.LoadUrl(requestUrl, (IUdonEventReceiver)this);
      SendCustomEventDelayedSeconds(nameof(CheckPagesTimeout), 15f);
    }

    public void CheckPagesTimeout()
    {
      if (!_loading) return;
      float remaining = 15f - (Time.time - _requestStartedAt);
      if (remaining > 0.1f)
      {
        SendCustomEventDelayedSeconds(nameof(CheckPagesTimeout), remaining);
        return;
      }
      _loading = false;
      _syncedManifestLoadPending = false;
      if (_useUnifiedQueue && _unifiedRequestMode != RequestModeNone)
      {
        CompleteUnifiedRequestWithRawFallback("解析超时，已按单项加入");
        return;
      }
      SetStatus("加载超时");
    }

    public void PreviousPage()
    {
      if (_pageOffset <= 0) return;
      _pageOffset = Mathf.Max(0, _pageOffset - VisibleButtonCount);
      _pageViewPinned = true;
      UpdateLabels();
    }

    public void NextPage()
    {
      int itemCount = _useUnifiedQueue
        ? (_standaloneManifestMode ? GetStandaloneDisplayCount() : GetUnifiedDisplayCount())
        : _totalPages;
      if (_pageOffset + VisibleButtonCount >= itemCount) return;
      _pageOffset += VisibleButtonCount;
      _pageViewPinned = true;
      UpdateLabels();
    }

    public void SelectPage0() { SelectVisiblePage(0); }
    public void SelectPage1() { SelectVisiblePage(1); }
    public void SelectPage2() { SelectVisiblePage(2); }
    public void SelectPage3() { SelectVisiblePage(3); }
    public void SelectPage4() { SelectVisiblePage(4); }
    public void SelectPage5() { SelectVisiblePage(5); }

    public void TogglePlaybackMode()
    {
      if (!Utilities.IsValid(_controller))
      {
        SetStatus("未找到播放器控制器");
        return;
      }

      _controller.TakeOwnership();
      _controller.Loop = !_controller.Loop;
      UpdatePlayModeButtonLabel();
      SetStatus(_controller.Loop ? "已切换为单项循环" : "已切换为顺序播放");
    }

    public void ClearUnifiedQueue()
    {
      if (!_useUnifiedQueue || !Utilities.IsValid(_controller))
      {
        SetStatus("未找到播放队列");
        return;
      }

      if (_standaloneManifestMode)
      {
        ClearStandaloneManifestList();
        return;
      }
      if (_biliMixedManifestMode)
      {
        ClearBiliMixedManifestAndQueue();
        return;
      }

      Playlist queue = _controller.Queue;
      int queuedCount = Utilities.IsValid(queue) ? queue.Length : 0;
      int retainedCount = 0;
      bool requestPending = _loading || _unifiedRequestMode != RequestModeNone ||
                            !string.IsNullOrEmpty(_pendingNeteaseMetadataUrl);

      CancelUnifiedQueueRequest();
      string currentUrl = GetUrlString(GetCurrentControllerUrl());
      _expandedPlaybackSourceUrl = currentUrl;
      _lastObservedPlaybackUrl = currentUrl;

      Track currentTrack = _controller.Track;
      string activeCurrentUrl = HasCurrentUnifiedTrack() ? GetTrackVrcUrl(currentTrack) : "";
      bool retainedCurrent = !string.IsNullOrEmpty(activeCurrentUrl) &&
                             (IsRetainedMultiPageTrack(currentTrack) || activeCurrentUrl == _syncedPendingRetainUrl);
      string pendingCurrentUrl = retainedCurrent ? activeCurrentUrl : "";

      if (queuedCount > 0 || retainedCount > 0)
      {
        _clearingUnifiedQueue = true;
        if (Utilities.IsValid(queue))
        {
          queue.TakeOwnership();
          queue.Tracks.Clear();
          queue.SendEvent();
        }
        _clearingUnifiedQueue = false;
      }
      PublishPendingRetainedDeleteUrl(pendingCurrentUrl);
      PublishPendingCurrentRetention("", "");

      _pageOffset = 0;
      _pageViewPinned = false;
      _unifiedQueueLimitReached = false;
      InvalidateUnifiedDisplayCache();
      HideAllDeleteIcons();
      EnsureUnifiedQueueHeaderMatchesTracks();
      UpdateLabels();
      SetStatus(queuedCount > 0 || retainedCount > 0 || requestPending || !string.IsNullOrEmpty(pendingCurrentUrl)
        ? "已清空播放队列"
        : "播放队列已经为空");
    }

    private void CancelUnifiedQueueRequest()
    {
      DiscardPreservedStandaloneManifest();
      _loading = false;
      _lastRequestUrl = "";
      _syncedManifestLoadPending = false;
      _unifiedRequestMode = RequestModeNone;
      _unifiedRequestSourceUrl = VRCUrl.Empty;
      _unifiedStartWhenReady = false;
      _completeUnifiedRequestAfterMetadata = false;
      _normalizeQueueIndex = -1;
      _normalizeQueueTrackUrl = "";
      _normalizeQueueScheduled = false;
      _pendingStandaloneQueueRemovalIndex = -1;
      _pendingStandaloneQueueRemovalUrl = "";
      ResetParsedSource();
    }

    public override void OnLoopChanged()
    {
      UpdatePlayModeButtonLabel();
    }

    public override void OnQueueUpdated()
    {
      if (!_useUnifiedQueue) return;
      if (_clearingUnifiedQueue) return;
      if (_standaloneManifestMode)
      {
        ClampStandalonePageOffset();
        HideAllDeleteIcons();
        UpdateLabels();
        return;
      }
      InvalidateUnifiedDisplayCache();
      EnsureUnifiedQueueHeaderMatchesTracks();
      ClampUnifiedPageOffset();
      HideAllDeleteIcons();
      UpdateLabels();
      ScheduleNormalizeQueue();
    }

    public override void OnHistoryUpdated()
    {
      if (!_useUnifiedQueue) return;
    }

    public override void OnTrackUpdated()
    {
      if (!_useUnifiedQueue) return;
      if (_standaloneManifestMode)
      {
        RefreshStandaloneManifestSelection();
        return;
      }
      TryFinalizeStandaloneQueueSelection();
      if (_biliMixedManifestMode) RefreshBiliPlaybackSource(GetUrlString(GetCurrentControllerUrl()));
      InvalidateUnifiedDisplayCache();
      EnsureUnifiedQueueHeaderMatchesTracks();
      FocusUnifiedCurrentPage();
      HideAllDeleteIcons();
      UpdateLabels();
      ConfigureCurrentTrackDanmaku();
    }

    public override void OnUrlChanged()
    {
      if (!_useUnifiedQueue) return;
      if (_standaloneManifestMode)
      {
        RefreshStandaloneManifestSelection();
        return;
      }
      TryFinalizeStandaloneQueueSelection();
      if (_biliMixedManifestMode) RefreshBiliPlaybackSource(GetUrlString(GetCurrentControllerUrl()));
      InvalidateUnifiedDisplayCache();
      UpdateLabels();
      ConfigureCurrentTrackDanmaku();
    }

    public override void OnOwnerChanged()
    {
      if (!_useUnifiedQueue) return;
      if (_standaloneManifestMode)
      {
        RefreshStandaloneManifestSelection();
        return;
      }
      if (_biliMixedManifestMode)
      {
        RefreshBiliPlaybackSource(GetUrlString(GetCurrentControllerUrl()));
        UpdateLabels();
        ConfigureCurrentTrackDanmaku();
        ScheduleNormalizeQueue();
        return;
      }
      EnsureHistoryOwnershipForControllerOwner();
      ApplyPendingCurrentRetention();
      CleanupPendingRetainedDelete();
      UpdateLabels();
      ConfigureCurrentTrackDanmaku();
      ScheduleNormalizeQueue();
    }

    public override void OnVideoEnd()
    {
      if (_biliMixedManifestMode && _currentPlaybackIsManifestItem)
      {
        _naturalEndPending = true;
        if (!_autoPlayNext || !Utilities.IsValid(_controller) || _controller.Loop) return;
        if (!Networking.IsOwner(_controller.gameObject)) return;
        int nextBiliIndex = FindNextVisibleBiliManifestIndex(_biliManifestSelectedIndex + 1);
        if (nextBiliIndex >= 0)
        {
          _autoAdvancePending = true;
          _pendingNextIndex = nextBiliIndex;
          SetStatus("即将播放第 " + GetBiliManifestPageNumber(nextBiliIndex) + " 项");
          SendCustomEventDelayedFrames(nameof(PlayPendingNext), 1);
          return;
        }

        RestoreControllerForward();
        _advanceQueueAfterManifestPending = Utilities.IsValid(_controller.Queue) && _controller.Queue.Length > 0;
        SetStatus(_advanceQueueAfterManifestPending ? "多P播放完成，即将继续播放队列" : "已播放至最后一项");
        return;
      }
      if (_standaloneManifestMode) _naturalEndPending = true;
      if (_useUnifiedQueue && !_standaloneManifestMode)
      {
        _naturalEndPending = true;
        EnsureHistoryOwnershipForControllerOwner();
        if (!_autoPlayNext || !Utilities.IsValid(_controller) || _controller.Loop || !_syncedQueueManaged) return;
        if (!Networking.IsOwner(_controller.gameObject) || _cycleAppendPending) return;
        Track currentTrack = _controller.Track;
        if (!IsUsableTrack(currentTrack) || !IsNeteasePlaylistCycleTrack(currentTrack) ||
            !Utilities.IsValid(_controller.Queue) || _controller.Queue.Length <= 0) return;

        _cycleAppendPending = true;
        _controller.Queue.TakeOwnership();
        _controller.Queue.AddTrack(currentTrack);
        return;
      }

      if (!_autoPlayNext || !Utilities.IsValid(_controller) || _controller.Loop) return;
      if (!Networking.IsOwner(_controller.gameObject)) return;
      if (_selectedIndex < 0 || _totalPages <= 0) return;
      int nextIndex = _selectedIndex + 1;
      if (nextIndex >= _totalPages)
      {
        if (!_parsedShouldCycle)
        {
          SetStatus("已播放至最后一项");
          return;
        }
        nextIndex = 0;
      }
      _autoAdvancePending = true;
      _pendingNextIndex = nextIndex;
      SetStatus("即将播放第 " + _pageNumbers[nextIndex] + " 项");
      SendCustomEventDelayedFrames(nameof(PlayPendingNext), 1);
    }

    public override void OnVideoStop()
    {
      if (_biliMixedManifestMode && _currentPlaybackIsManifestItem && _naturalEndPending)
      {
        _naturalEndPending = false;
        _ignorePlaybackUrlWhileStopped = true;
        UpdateLabels();
        if (_autoAdvancePending) return;
        _currentPlaybackIsManifestItem = false;
        _biliManifestPlaybackLocked = false;
        InvalidateUnifiedDisplayCache();
        if (_advanceQueueAfterManifestPending)
          SendCustomEventDelayedFrames(nameof(AdvanceQueueAfterBiliManifest), 1);
        else
          SetStatus("已播放至最后一项");
        return;
      }
      if (_biliMixedManifestMode && _currentPlaybackIsManifestItem &&
          (_internalTrackSwitch || _autoAdvancePending))
      {
        UpdateLabels();
        return;
      }
      if (_useUnifiedQueue && !_standaloneManifestMode)
      {
        _cycleAppendPending = false;
        _lastConfiguredDanmakuUrl = "";
        if (_naturalEndPending)
        {
          _naturalEndPending = false;
          UpdateLabels();
          return;
        }
        if (_autoPlayNext && Utilities.IsValid(_controller) && Utilities.IsValid(_controller.Queue) &&
            Networking.IsOwner(_controller.gameObject) && _controller.Queue.Length > 0 && !_manualStopAdvancePending)
        {
          _manualStopAdvancePending = true;
          SendCustomEventDelayedFrames(nameof(AdvanceUnifiedQueueAfterManualStop), 1);
        }
        UpdateLabels();
        return;
      }

      if (_standaloneManifestMode && _naturalEndPending)
      {
        _naturalEndPending = false;
        _ignorePlaybackUrlWhileStopped = true;
        UpdateLabels();
        if (!_autoAdvancePending) SetStatus("已播放至最后一项");
        return;
      }
      if (_standaloneManifestMode && _ignorePlaybackUrlWhileStopped)
      {
        UpdateLabels();
        return;
      }

      if (!Utilities.IsValid(_controller) || !Networking.IsOwner(_controller.gameObject)) return;
      if (_internalTrackSwitch || _autoAdvancePending) return;
      SendCustomEventDelayedSeconds(nameof(CheckStoppedForPages), 0.35f);
    }

    public void CheckStoppedForPages()
    {
      if (!Utilities.IsValid(_controller) || !_controller.Stopped || _controller.IsLoading) return;
      if (!Networking.IsOwner(_controller.gameObject)) return;

      _ignorePlaybackUrlWhileStopped = true;
      _loading = false;
      _lastRequestUrl = "";
      _lastObservedPlaybackUrl = "";
      if (_biliMixedManifestMode)
      {
        _currentPlaybackIsManifestItem = false;
        _biliManifestPlaybackLocked = false;
        _autoAdvancePending = false;
        _advanceQueueAfterManifestPending = false;
        RestoreControllerForward();
        InvalidateUnifiedDisplayCache();
        UpdateLabels();
        SetStatus("播放已终止，多P列表已保留");
        return;
      }
      if (_standaloneManifestMode) ExitStandaloneManifestModeForNewRequest();
      ClearPages("播放已终止");
      PublishManifestState(VRCUrl.Empty, 0, false);
    }

    public void AdvanceUnifiedQueueAfterManualStop()
    {
      _manualStopAdvancePending = false;
      if (!_useUnifiedQueue || _standaloneManifestMode || !Utilities.IsValid(_controller) || !Utilities.IsValid(_controller.Queue)) return;
      if (!Networking.IsOwner(_controller.gameObject)) return;
      if (!_controller.Stopped || _controller.IsLoading || _controller.Queue.Length <= 0) return;
      _controller.RunForward();
    }

    public void AdvanceQueueAfterBiliManifest()
    {
      if (!_advanceQueueAfterManifestPending) return;
      _advanceQueueAfterManifestPending = false;
      RestoreControllerForward();
      if (!Utilities.IsValid(_controller) || !Utilities.IsValid(_controller.Queue)) return;
      if (!Networking.IsOwner(_controller.gameObject)) return;
      if (!_controller.Stopped || _controller.IsLoading || _controller.Queue.Length <= 0) return;
      _controller.RunForward();
    }

    public override void OnVideoStart()
    {
      if (_biliMixedManifestMode)
      {
        if (_internalTrackSwitch && _biliManifestPlaybackLocked)
        {
          _currentPlaybackIsManifestItem = true;
          InvalidateUnifiedDisplayCache();
        }
        else
        {
          RefreshBiliPlaybackSource(GetUrlString(GetCurrentControllerUrl()));
        }
        _internalTrackSwitch = false;
        _ignorePlaybackUrlWhileStopped = false;
      }
      if (_useUnifiedQueue && !_standaloneManifestMode)
      {
        TryFinalizeStandaloneQueueSelection();
        EnsureHistoryOwnershipForControllerOwner();
        _cycleAppendPending = false;
        _naturalEndPending = false;
        _manualStopAdvancePending = false;
        FocusUnifiedCurrentPage();
        UpdateLabels();
        ConfigureCurrentTrackDanmaku();
        return;
      }

      _naturalEndPending = false;
      _ignorePlaybackUrlWhileStopped = false;
      if (_standaloneManifestMode) ConfirmStandaloneManifestPlayback();
      else LoadSelectedNeteaseDanmaku();
      _internalTrackSwitch = false;
    }

    public override void OnVideoError(VideoError videoError)
    {
      if (_biliMixedManifestMode && _currentPlaybackIsManifestItem)
      {
        _currentPlaybackIsManifestItem = false;
        _biliManifestPlaybackLocked = false;
        _internalTrackSwitch = false;
        _autoAdvancePending = false;
        _naturalEndPending = false;
        _advanceQueueAfterManifestPending = false;
        RestoreControllerForward();
        InvalidateUnifiedDisplayCache();
        UpdateLabels();
        SetStatus("播放失败，多P列表已保留");
        return;
      }
      if (_useUnifiedQueue && !_standaloneManifestMode)
      {
        _cycleAppendPending = false;
        SetStatus("播放失败");
        return;
      }

      if (!_standaloneManifestMode || _pendingStandaloneVcrid <= 0) _internalTrackSwitch = false;
      _autoAdvancePending = false;
      _naturalEndPending = false;
      if (_standaloneManifestMode) _ignorePlaybackUrlWhileStopped = true;
      SetStatus("播放失败");
    }

    public void PlayPendingNext()
    {
      if (_biliMixedManifestMode && _currentPlaybackIsManifestItem)
      {
        if (!_autoAdvancePending) return;
        int nextBiliIndex = _pendingNextIndex;
        _pendingNextIndex = -1;
        _autoAdvancePending = false;
        PlayBiliManifestIndex(nextBiliIndex);
        return;
      }
      if (_useUnifiedQueue && !_standaloneManifestMode) return;
      if (!_autoAdvancePending) return;

      int nextIndex = _pendingNextIndex;
      _pendingNextIndex = -1;
      if (!Utilities.IsValid(_controller) || _controller.Loop)
      {
        _autoAdvancePending = false;
        return;
      }

      if (Utilities.IsValid(_danmakuModule)) _danmakuModule.ClearDanmaku();
      PlayPageIndex(nextIndex);
      _autoAdvancePending = false;
    }

    public override void OnStringLoadSuccess(IVRCStringDownload result)
    {
      string resultUrl = GetUrlString(result.Url);
      if (!string.IsNullOrEmpty(_pendingNeteaseMetadataUrl) && resultUrl == _pendingNeteaseMetadataUrl)
      {
        int metadataIndex = _pendingNeteaseMetadataIndex;
        _pendingNeteaseMetadataUrl = "";
        _pendingNeteaseMetadataIndex = -1;
        ApplyNeteaseSongMetadata(result.Result, metadataIndex);
        if (_useUnifiedQueue && _completeUnifiedRequestAfterMetadata)
        {
          _completeUnifiedRequestAfterMetadata = false;
          CompleteUnifiedRequest();
        }
        return;
      }

      if (resultUrl != _lastRequestUrl) return;
      if (_useUnifiedQueue && _unifiedRequestMode != RequestModeNone)
      {
        _loading = false;
        _syncedManifestLoadPending = false;
        _lastManifestSourceUrl = result.Url;
        ParsePagesJson(result.Result);
        if (_totalPages <= 0)
        {
          CompleteUnifiedRequestWithRawFallback("未识别播放列表，已按单项加入");
          return;
        }

        if (_needsNeteaseSongMetadata)
        {
          _completeUnifiedRequestAfterMetadata = true;
          RequestSelectedNeteaseSongMetadata();
          if (!string.IsNullOrEmpty(_pendingNeteaseMetadataUrl)) return;
          _completeUnifiedRequestAfterMetadata = false;
        }
        CompleteUnifiedRequest();
        return;
      }

      if (_totalPages > 1 && IsVcridPlaybackUrl(GetUrlString(result.Url)))
      {
        _loading = false;
        _syncedManifestLoadPending = false;
        SetStatus("保持当前播放列表");
        return;
      }
      _loading = false;
      _syncedManifestLoadPending = false;
      _lastManifestSourceUrl = result.Url;
      ParsePagesJson(result.Result);
      if (_useUnifiedQueue && _syncedManifestActive && _syncedManifestMode == ManifestModeBilibiliMixed)
      {
        ActivateSyncedBiliManifestFromParsed();
        return;
      }
      if (_syncedManifestActive && ApplyCurrentOrSyncedSelection())
      {
        UpdateLabels();
      }
      if (_totalPages > 0 && Utilities.IsValid(_controller) && Networking.IsOwner(_controller.gameObject))
      {
        PublishManifestState(_lastManifestSourceUrl, GetSelectedVcrid(), true);
      }
      LoadSelectedNeteaseDanmaku();
      RequestSelectedNeteaseSongMetadata();
    }

    public override void OnStringLoadError(IVRCStringDownload result)
    {
      string resultUrl = GetUrlString(result.Url);
      if (!string.IsNullOrEmpty(_pendingNeteaseMetadataUrl) && resultUrl == _pendingNeteaseMetadataUrl)
      {
        _pendingNeteaseMetadataUrl = "";
        _pendingNeteaseMetadataIndex = -1;
        if (_useUnifiedQueue && _completeUnifiedRequestAfterMetadata)
        {
          _completeUnifiedRequestAfterMetadata = false;
          CompleteUnifiedRequest();
          return;
        }
        SetStatus("歌曲信息读取失败 " + result.ErrorCode);
        return;
      }

      if (resultUrl != _lastRequestUrl) return;
      _loading = false;
      _syncedManifestLoadPending = false;
      if (_useUnifiedQueue && _unifiedRequestMode != RequestModeNone)
      {
        CompleteUnifiedRequestWithRawFallback("解析失败，已按单项加入");
        return;
      }
      SetStatus("列表加载失败 " + result.ErrorCode);
    }

    private void ParsePagesJson(string json)
    {
      if (string.IsNullOrEmpty(json))
      {
        SetStatus("播放列表为空");
        return;
      }

      if (ParsePagesMetadata(json)) return;

      bool preserveBiliPage = _useUnifiedQueue && _biliMixedManifestMode && _biliManifestTotalPages > 0;
      int preservedPageOffset = _pageOffset;

      if (!VRCJson.TryDeserializeFromJson(json, out DataToken rootToken) || rootToken.TokenType != TokenType.DataDictionary)
      {
        SetStatus("播放列表解析失败");
        return;
      }

      DataDictionary root = rootToken.DataDictionary;
      if (!root.TryGetValue("pages", TokenType.DataList, out DataToken pagesToken))
      {
        SetStatus("没有找到播放项目");
        return;
      }

      DataList pages = pagesToken.DataList;
      bool isNetease = false;
      bool isNeteaseSong = false;
      bool isNeteasePlaylistManifest = false;
      _parsedIsBilibiliList = false;
      if (root.TryGetValue("type", TokenType.String, out DataToken typeToken))
      {
        isNetease = typeToken.String == "netease-playlist" || typeToken.String == "netease-song";
        isNeteaseSong = typeToken.String == "netease-song";
        isNeteasePlaylistManifest = typeToken.String == "netease-playlist";
        _parsedIsBilibiliList = typeToken.String == "bilibili-list";
      }
      if (root.TryGetValue("provider", TokenType.String, out DataToken providerToken) && providerToken.String == "netease") isNetease = true;
      _isNeteasePlaylist = isNetease;
      _parsedShouldCycle = isNeteasePlaylistManifest;
      if (!_useUnifiedQueue && Utilities.IsValid(_danmakuModule)) _danmakuModule.SetExternalAudioMode(isNetease);

      int count = Mathf.Min(Mathf.Max(0, pages.Count), 4096);
      _parts = new string[count];
      _playUrls = new string[count];
      _danmakuUrls = new string[count];
      _pageNumbers = new int[count];
      _vcrids = new int[count];
      _totalPages = count;
      _selectedIndex = -1;
      if (!preserveBiliPage) _pageOffset = 0;

      if (root.TryGetValue("title", TokenType.String, out DataToken titleToken))
      {
        SetParsedSourceTitle(titleToken.String);
      }
      else
      {
        SetParsedSourceTitle(isNetease ? "网易云歌单" : "播放列表");
      }

      int selectedPage = 1;
      if (root.TryGetValue("selectedPage", out DataToken selectedPageToken)) selectedPage = TokenToInt(selectedPageToken, 1);
      string rootTitle = "";
      if (root.TryGetValue("title", TokenType.String, out DataToken rootTitleToken)) rootTitle = rootTitleToken.String;
      string singleSongTitle = "";
      string singleSongArtist = "";

      for (int i = 0; i < count; i++)
      {
        DataToken itemToken = pages[i];
        if (itemToken.TokenType != TokenType.DataDictionary) continue;

        DataDictionary item = itemToken.DataDictionary;
        int page = i + 1;
        string part = "";
        string playUrl = "";
        string danmakuUrl = "";
        string itemTitle = "";
        string artist = "";
        int vcrid = 0;

        if (item.TryGetValue("page", out DataToken pageToken)) page = TokenToInt(pageToken, page);
        if (item.TryGetValue("part", TokenType.String, out DataToken partToken)) part = partToken.String;
        if (item.TryGetValue("name", TokenType.String, out DataToken nameToken)) itemTitle = nameToken.String;
        if (string.IsNullOrEmpty(itemTitle) && item.TryGetValue("title", TokenType.String, out DataToken itemTitleToken)) itemTitle = itemTitleToken.String;
        if (item.TryGetValue("artist", TokenType.String, out DataToken artistToken)) artist = artistToken.String;
        if (item.TryGetValue("playUrl", TokenType.String, out DataToken playUrlToken)) playUrl = playUrlToken.String;
        if (item.TryGetValue("danmakuUrl", TokenType.String, out DataToken danmakuUrlToken)) danmakuUrl = danmakuUrlToken.String;
        if (item.TryGetValue("vcrid", out DataToken vcridToken)) vcrid = TokenToInt(vcridToken, 0);

        _pageNumbers[i] = page;
        if (isNetease)
        {
          string songTitle = string.IsNullOrEmpty(itemTitle) ? "Track " + page : itemTitle;
          _parts[i] = string.IsNullOrEmpty(artist) ? songTitle : songTitle + " - " + artist;
          if (i == 0)
          {
            singleSongTitle = itemTitle;
            singleSongArtist = artist;
          }
        }
        else
        {
          bool useVideoTitle = count == 1 && !_parsedIsBilibiliList && !string.IsNullOrEmpty(rootTitle);
          _parts[i] = useVideoTitle ? rootTitle : string.IsNullOrEmpty(part) ? "P" + page : part;
        }
        _playUrls[i] = playUrl;
        _danmakuUrls[i] = danmakuUrl;
        _vcrids[i] = vcrid;
        if (page == selectedPage) _selectedIndex = i;
      }

      _needsNeteaseSongMetadata = false;
      if (isNeteaseSong && count == 1)
      {
        string displaySongTitle = string.IsNullOrEmpty(singleSongTitle) ? rootTitle : singleSongTitle;
        if (string.IsNullOrEmpty(displaySongTitle))
        {
          _needsNeteaseSongMetadata = true;
          SetParsedSourceTitle("正在读取歌曲信息");
          _parts[0] = "正在读取歌曲信息";
        }
        else
        {
          SetParsedSourceTitle(displaySongTitle);
          _parts[0] = string.IsNullOrEmpty(singleSongArtist)
            ? displaySongTitle
            : displaySongTitle + " - " + singleSongArtist;
        }
      }

      bool directPagesRequest = IsDirectPagesApiUrl(GetUrlString(GetCurrentControllerUrl()));
      if (isNetease && directPagesRequest) _selectedIndex = -1;
      if (_selectedIndex < 0 && count > 0 && (!isNetease || !directPagesRequest)) _selectedIndex = 0;
      _pageOffset = preserveBiliPage
        ? preservedPageOffset
        : Mathf.Max(0, (_selectedIndex / VisibleButtonCount) * VisibleButtonCount);
      SetStatus(count > 0 ? "已加载 " + count + " 项" : "播放列表为空");
      UpdateLabels();
    }

    private void ResetParsedSource()
    {
      _parts = new string[0];
      _playUrls = new string[0];
      _danmakuUrls = new string[0];
      _pageNumbers = new int[0];
      _vcrids = new int[0];
      _totalPages = 0;
      _selectedIndex = -1;
      _parsedSourceTitle = "";
      _parsedIsBilibiliList = false;
      _isNeteasePlaylist = false;
      _parsedShouldCycle = false;
      _needsNeteaseSongMetadata = false;
      _pendingNeteaseMetadataUrl = "";
      _pendingNeteaseMetadataIndex = -1;
    }

    private void SetParsedSourceTitle(string title)
    {
      _parsedSourceTitle = SanitizeUiText(title);
      if (!_useUnifiedQueue || _standaloneManifestMode) SetTitle(title);
    }

    private void CompleteUnifiedRequest()
    {
      int requestMode = _unifiedRequestMode;
      VRCUrl sourceUrl = _unifiedRequestSourceUrl;
      bool startWhenReady = _unifiedStartWhenReady;
      int normalizeQueueIndex = _normalizeQueueIndex;
      string normalizeQueueTrackUrl = _normalizeQueueTrackUrl;
      _unifiedRequestMode = RequestModeNone;
      _unifiedRequestSourceUrl = VRCUrl.Empty;
      _unifiedStartWhenReady = false;
      _completeUnifiedRequestAfterMetadata = false;
      _normalizeQueueIndex = -1;
      _normalizeQueueTrackUrl = "";
      _loading = false;

      if (requestMode == RequestModeNone || _totalPages <= 0) return;
      if (ShouldUseNeteaseExclusiveManifestPlayback())
      {
        DiscardPreservedStandaloneManifest();
        EnterStandaloneManifestMode(requestMode, sourceUrl);
        return;
      }
      if (ShouldUseBiliMixedManifestPlayback())
      {
        CompleteBiliMixedManifestRequest(requestMode, sourceUrl, normalizeQueueIndex, normalizeQueueTrackUrl);
        return;
      }
      if (requestMode == RequestModeNormalizeQueued)
      {
        ReplaceQueuedTrackWithParsedTracks(sourceUrl, normalizeQueueIndex, normalizeQueueTrackUrl);
        return;
      }
      if (requestMode == RequestModeEnqueue)
      {
        startWhenReady = Utilities.IsValid(_controller) && _controller.Stopped && !_controller.IsLoading &&
                         Utilities.IsValid(_controller.Queue) && _controller.Queue.Length == 0;
        if (_preserveStandaloneManifestRequest) startWhenReady = false;
      }
      int selectedIndex = _selectedIndex >= 0 && _selectedIndex < _totalPages ? _selectedIndex : 0;
      string sourceTitle = GetParsedQueueTitle(selectedIndex);

      if (requestMode == RequestModeExpandCurrent)
      {
        DiscardPreservedStandaloneManifest();
        _expandedPlaybackSourceUrl = GetUrlString(sourceUrl);
        bool hasOtherEntries = HasRetainedBiliManifestItems() || GetRetainedHistoryDisplayCount() > 0 ||
                               (Utilities.IsValid(_controller) && Utilities.IsValid(_controller.Queue) && _controller.Queue.Length > 0);
        MergeUnifiedQueueHeader(sourceUrl, sourceTitle, hasOtherEntries);
        SetCurrentTrackTitle(BuildParsedTrackTitle(selectedIndex));
        PrepareCurrentTrackRetention(sourceUrl);
        AddParsedTracksToQueue(selectedIndex + 1, sourceUrl, false, true);
        SetStatus(_totalPages > selectedIndex + 1 ? "已展开后续项目" : "当前内容共 1 项");
        LoadParsedCurrentDanmaku(selectedIndex);
      }
      else
      {
        bool hadExisting = HasRetainedBiliManifestItems() || GetRetainedHistoryDisplayCount() > 0 || HasCurrentUnifiedTrack() ||
                           (Utilities.IsValid(_controller) && Utilities.IsValid(_controller.Queue) && _controller.Queue.Length > 0);
        MergeUnifiedQueueHeader(sourceUrl, sourceTitle, hadExisting);
        AddParsedTracksToQueue(selectedIndex, sourceUrl, startWhenReady, false);
      }

      if (_preserveStandaloneManifestRequest)
      {
        RestorePreservedStandaloneManifest();
        SetStatus("已追加到网易云歌单末尾");
        return;
      }

      ClampUnifiedPageOffset();
      UpdateLabels();
      ScheduleNormalizeQueue();
    }

    private bool ShouldUseNeteaseExclusiveManifestPlayback()
    {
      return _isNeteasePlaylist && _parsedShouldCycle;
    }

    private bool ShouldUseBiliMixedManifestPlayback()
    {
      return !_isNeteasePlaylist && _totalPages > 1;
    }

    private void CompleteBiliMixedManifestRequest(int requestMode, VRCUrl sourceUrl, int normalizeQueueIndex, string normalizeQueueTrackUrl)
    {
      int selectedIndex = _selectedIndex >= 0 && _selectedIndex < _totalPages ? _selectedIndex : 0;
      if (requestMode == RequestModeNormalizeQueued)
      {
        SetQueuedManifestPlaceholderTitle(normalizeQueueIndex, normalizeQueueTrackUrl, GetParsedQueueTitle(selectedIndex));
        ResetParsedSource();
        ScheduleNormalizeQueue();
        return;
      }

      bool playNow = !_preserveStandaloneManifestRequest && (requestMode == RequestModeExpandCurrent ||
                     (requestMode == RequestModeEnqueue && Utilities.IsValid(_controller) &&
                       _controller.Stopped && !_controller.IsLoading &&
                       (!Utilities.IsValid(_controller.Queue) || _controller.Queue.Length == 0)));
      if (playNow)
      {
        EnterBiliMixedManifestMode(requestMode, sourceUrl, selectedIndex);
        return;
      }

      AddBiliManifestPlaceholderToQueue(sourceUrl, selectedIndex);
      if (_preserveStandaloneManifestRequest)
      {
        RestorePreservedStandaloneManifest();
        SetStatus("B站多P已追加到网易云歌单末尾");
      }
    }

    private void SetQueuedManifestPlaceholderTitle(int preferredQueueIndex, string expectedTrackUrl, string title)
    {
      if (!Utilities.IsValid(_controller) || !Utilities.IsValid(_controller.Queue)) return;
      int queueIndex = FindQueuedTrackIndex(preferredQueueIndex, expectedTrackUrl);
      if (queueIndex < 0) return;

      Track track = _controller.Queue.GetTrack(queueIndex);
      if (!IsUsableTrack(track)) return;
      _controller.Queue.TakeOwnership();
      track.SetTitle(string.IsNullOrEmpty(title) ? "B站多P视频" : title);
      _controller.Queue.SendEvent();
      SetStatus("已识别多P视频，播放时展开");
      UpdateLabels();
    }

    private void AddBiliManifestPlaceholderToQueue(VRCUrl sourceUrl, int selectedIndex)
    {
      if (!Utilities.IsValid(_controller) || !Utilities.IsValid(_controller.Queue)) return;
      string title = GetParsedQueueTitle(selectedIndex);
      Track placeholder = Track.New(_controller.VideoPlayerType,
        string.IsNullOrEmpty(title) ? "B站多P视频" : title, sourceUrl);
      bool hadExisting = HasCurrentUnifiedTrack() || _controller.Queue.Length > 0 || _biliMixedManifestMode;
      MergeUnifiedQueueHeader(sourceUrl, title, hadExisting);
      _controller.Queue.TakeOwnership();
      _controller.Queue.AddTrack(placeholder);
      ResetParsedSource();
      SetStatus("多P视频已加入播放队列，播放时展开");
      InvalidateUnifiedDisplayCache();
      UpdateLabels();
      ScheduleNormalizeQueue();
    }

    private void EnterBiliMixedManifestMode(int requestMode, VRCUrl sourceUrl, int selectedIndex)
    {
      if (!Utilities.IsValid(_controller) || _totalPages <= 0) return;

      bool newManifest = GetUrlString(_biliManifestSourceUrl) != GetUrlString(sourceUrl);
      _biliManifestParts = _parts;
      _biliManifestPageNumbers = _pageNumbers;
      _biliManifestVcrids = _vcrids;
      _biliManifestTotalPages = _totalPages;
      _biliManifestSelectedIndex = selectedIndex;
      _biliManifestTitle = GetParsedQueueTitle(selectedIndex);
      _biliManifestSourceUrl = sourceUrl;
      _standaloneManifestMode = false;
      _biliMixedManifestMode = true;
      _pageViewPinned = false;
      _currentPlaybackIsManifestItem = true;
      _biliManifestPlaybackLocked = true;
      _advanceQueueAfterManifestPending = false;
      _expandedPlaybackSourceUrl = GetUrlString(sourceUrl);
      _lastManifestSourceUrl = sourceUrl;
      _selectedIndex = selectedIndex;
      _pageOffset = 0;
      _autoAdvancePending = false;
      _pendingNextIndex = -1;
      _naturalEndPending = false;
      _manualStopAdvancePending = false;
      _cycleAppendPending = false;
      _pendingStandaloneVcrid = 0;
      _lastConfiguredDanmakuUrl = "";
      _lastConfiguredDanmakuWasNetease = false;
      if (newManifest) _syncedDeletedManifestVcrids = new int[0];

      if (Utilities.IsValid(Networking.LocalPlayer)) _controller.TakeOwnership();
      CleanupActiveBiliManifestQueueEntries();
      SuppressControllerForward();
      string sourceTitle = _biliManifestTitle;
      bool hasIndependentItems = Utilities.IsValid(_controller.Queue) && _controller.Queue.Length > 0;
      PublishManifestState(sourceUrl, GetBiliManifestVcrid(selectedIndex), true);
      MergeUnifiedQueueHeader(sourceUrl, sourceTitle, hasIndependentItems);

      if (requestMode == RequestModeExpandCurrent)
      {
        SetCurrentTrackTitle(GetBiliManifestPart(selectedIndex));
      }
      else
      {
        _biliManifestSelectedIndex = -1;
        PlayBiliManifestIndex(selectedIndex);
      }

      ResetParsedSource();
      InvalidateUnifiedDisplayCache();
      HideAllDeleteIcons();
      UpdateLabels();
      SetStatus("已加载多P视频，共 " + GetVisibleManifestItemCount() + " 项");
    }

    private void EnterStandaloneManifestMode(int requestMode, VRCUrl sourceUrl)
    {
      if (!Utilities.IsValid(_controller) || _totalPages <= 0) return;

      int selectedIndex = _selectedIndex >= 0 && _selectedIndex < _totalPages ? _selectedIndex : 0;
      _standaloneManifestMode = true;
      _biliMixedManifestMode = false;
      _pageViewPinned = false;
      _currentPlaybackIsManifestItem = true;
      _advanceQueueAfterManifestPending = false;
      _pendingStandaloneQueueRemovalIndex = -1;
      _pendingStandaloneQueueRemovalUrl = "";
      RestoreControllerForward();
      ResetBiliManifestState();
      _expandedPlaybackSourceUrl = GetUrlString(sourceUrl);
      _lastManifestSourceUrl = sourceUrl;
      _autoAdvancePending = false;
      _pendingNextIndex = -1;
      _naturalEndPending = false;
      _manualStopAdvancePending = false;
      _cycleAppendPending = false;
      _pendingStandaloneVcrid = 0;
      _lastConfiguredDanmakuUrl = "";
      _pageOffset = Mathf.Max(0, (selectedIndex / VisibleButtonCount) * VisibleButtonCount);
      _unifiedQueueLimitReached = false;
      _syncedQueueManaged = false;
      _syncedQueueMixed = false;
      _syncedQueueTitle = "";
      _syncedQueueSourceUrl = VRCUrl.Empty;
      _syncedPendingRetainedDeleteUrl = "";
      _syncedPendingRetainUrl = "";
      _syncedPendingRetainMarkerUrl = "";
      _syncedDeletedManifestVcrids = new int[0];

      if (Utilities.IsValid(Networking.LocalPlayer)) _controller.TakeOwnership();
      ClearUnifiedItemsForStandaloneManifest();
      HideAllDeleteIcons();
      SetTitle(string.IsNullOrEmpty(_parsedSourceTitle) ? "播放列表" : _parsedSourceTitle);
      if (Utilities.IsValid(_danmakuModule)) _danmakuModule.SetExternalAudioMode(_isNeteasePlaylist);

      if (requestMode == RequestModeExpandCurrent)
      {
        _selectedIndex = selectedIndex;
        SetCurrentTrackTitle(BuildParsedTrackTitle(selectedIndex));
        PublishManifestState(sourceUrl, GetSelectedVcrid(), true);
        LoadSelectedNeteaseDanmaku();
      }
      else
      {
        _selectedIndex = -1;
        PlayPageIndex(selectedIndex);
      }

      SetStatus("已加载独立播放列表，共 " + _totalPages + " 项");
      UpdateLabels();
    }

    private void ClearUnifiedItemsForStandaloneManifest()
    {
      if (!Utilities.IsValid(_controller) || !Networking.IsOwner(_controller.gameObject)) return;

      Playlist queue = _controller.Queue;
      _clearingUnifiedQueue = true;
      if (Utilities.IsValid(queue))
      {
        queue.TakeOwnership();
        queue.Tracks.Clear();
        queue.SendEvent();
      }
      _clearingUnifiedQueue = false;
      InvalidateUnifiedDisplayCache();
    }

    private void ExitStandaloneManifestModeForNewRequest()
    {
      if (!_standaloneManifestMode) return;

      _standaloneManifestMode = false;
      _currentPlaybackIsManifestItem = false;
      _biliManifestPlaybackLocked = false;
      _advanceQueueAfterManifestPending = false;
      _autoAdvancePending = false;
      _pendingNextIndex = -1;
      _internalTrackSwitch = false;
      _naturalEndPending = false;
      _pendingStandaloneVcrid = 0;
      _lastConfiguredDanmakuUrl = "";
      _lastConfiguredDanmakuWasNetease = false;
      if (Utilities.IsValid(_danmakuModule)) _danmakuModule.SetExternalAudioMode(false);
      if (Utilities.IsValid(_controller) && Networking.IsOwner(_controller.gameObject))
        PublishManifestState(VRCUrl.Empty, 0, false);
      InvalidateUnifiedDisplayCache();
      HideAllDeleteIcons();
    }

    private void PreserveStandaloneManifestForQueueRequest()
    {
      if (_preserveStandaloneManifestRequest || !_standaloneManifestMode) return;

      _preserveStandaloneManifestRequest = true;
      _preservedStandaloneParts = _parts;
      _preservedStandalonePlayUrls = _playUrls;
      _preservedStandaloneDanmakuUrls = _danmakuUrls;
      _preservedStandalonePageNumbers = _pageNumbers;
      _preservedStandaloneVcrids = _vcrids;
      _preservedStandaloneTotalPages = _totalPages;
      _preservedStandaloneSelectedIndex = _selectedIndex;
      _preservedStandalonePageOffset = _pageOffset;
      _preservedStandaloneSourceTitle = _parsedSourceTitle;
      _preservedStandaloneIsNetease = _isNeteasePlaylist;
      _preservedStandaloneShouldCycle = _parsedShouldCycle;
      _preservedStandaloneIsBilibiliList = _parsedIsBilibiliList;
      _preservedStandaloneSourceUrl = _lastManifestSourceUrl;
      _preservedStandaloneNeedsNeteaseMetadata = _needsNeteaseSongMetadata;
      _preservedStandaloneMetadataUrl = _pendingNeteaseMetadataUrl;
      _preservedStandaloneMetadataIndex = _pendingNeteaseMetadataIndex;
      Debug.Log("[PaulKoiPages] provider=netease stage=preserve-for-enqueue pages=" + _totalPages +
                " queue=" + GetStandaloneQueueCount());
    }

    private void RestorePreservedStandaloneManifest()
    {
      if (!_preserveStandaloneManifestRequest) return;

      _parts = _preservedStandaloneParts;
      _playUrls = _preservedStandalonePlayUrls;
      _danmakuUrls = _preservedStandaloneDanmakuUrls;
      _pageNumbers = _preservedStandalonePageNumbers;
      _vcrids = _preservedStandaloneVcrids;
      _totalPages = _preservedStandaloneTotalPages;
      _selectedIndex = _preservedStandaloneSelectedIndex;
      _pageOffset = _preservedStandalonePageOffset;
      _parsedSourceTitle = _preservedStandaloneSourceTitle;
      _isNeteasePlaylist = _preservedStandaloneIsNetease;
      _parsedShouldCycle = _preservedStandaloneShouldCycle;
      _parsedIsBilibiliList = _preservedStandaloneIsBilibiliList;
      _lastManifestSourceUrl = _preservedStandaloneSourceUrl;
      _needsNeteaseSongMetadata = _preservedStandaloneNeedsNeteaseMetadata;
      _pendingNeteaseMetadataUrl = _preservedStandaloneMetadataUrl;
      _pendingNeteaseMetadataIndex = _preservedStandaloneMetadataIndex;
      _standaloneManifestMode = true;
      DiscardPreservedStandaloneManifest();
      int queueCount = GetStandaloneQueueCount();
      if (queueCount > 0)
        _pageOffset = ((_totalPages + queueCount - 1) / VisibleButtonCount) * VisibleButtonCount;
      InvalidateUnifiedDisplayCache();
      HideAllDeleteIcons();
      UpdateLabels();
      Debug.Log("[PaulKoiPages] provider=netease stage=restore-after-enqueue pages=" + _totalPages +
                " queue=" + GetStandaloneQueueCount());
    }

    private void DiscardPreservedStandaloneManifest()
    {
      _preserveStandaloneManifestRequest = false;
      _preservedStandaloneParts = new string[0];
      _preservedStandalonePlayUrls = new string[0];
      _preservedStandaloneDanmakuUrls = new string[0];
      _preservedStandalonePageNumbers = new int[0];
      _preservedStandaloneVcrids = new int[0];
      _preservedStandaloneTotalPages = 0;
      _preservedStandaloneSelectedIndex = -1;
      _preservedStandalonePageOffset = 0;
      _preservedStandaloneSourceTitle = "";
      _preservedStandaloneIsNetease = false;
      _preservedStandaloneShouldCycle = false;
      _preservedStandaloneIsBilibiliList = false;
      _preservedStandaloneSourceUrl = VRCUrl.Empty;
      _preservedStandaloneNeedsNeteaseMetadata = false;
      _preservedStandaloneMetadataUrl = "";
      _preservedStandaloneMetadataIndex = -1;
    }

    private void ExitStandaloneManifestModeForUnifiedQueueRequest()
    {
      if (!_standaloneManifestMode) return;

      string currentUrl = GetUrlString(GetCurrentControllerUrl());
      Track currentTrack = Utilities.IsValid(_controller) ? _controller.Track : Track.Empty();
      bool keepCurrentNeteasePlayback = IsUsableTrack(currentTrack) &&
                                         IsCurrentPlaybackNetease(currentTrack, currentUrl);

      _standaloneManifestMode = false;
      _currentPlaybackIsManifestItem = false;
      _biliManifestPlaybackLocked = false;
      _advanceQueueAfterManifestPending = false;
      _autoAdvancePending = false;
      _pendingNextIndex = -1;
      _internalTrackSwitch = false;
      _naturalEndPending = false;
      _pendingStandaloneVcrid = 0;
      _lastConfiguredDanmakuUrl = keepCurrentNeteasePlayback ? currentUrl : "";
      _lastConfiguredDanmakuWasNetease = keepCurrentNeteasePlayback;
      if (Utilities.IsValid(_danmakuModule))
        _danmakuModule.SetExternalAudioMode(keepCurrentNeteasePlayback);
      if (Utilities.IsValid(_controller) && Networking.IsOwner(_controller.gameObject))
        PublishManifestState(VRCUrl.Empty, 0, false);

      _expandedPlaybackSourceUrl = currentUrl;
      _lastObservedPlaybackUrl = currentUrl;
      PublishUnifiedQueueHeader("播放队列", VRCUrl.Empty, true, true);
      InvalidateUnifiedDisplayCache();
      HideAllDeleteIcons();
      UpdateLabels();
      Debug.Log("[PaulKoiPages] provider=netease stage=exit-to-unified keepCurrent=" +
                keepCurrentNeteasePlayback + " current=" + currentUrl);
    }

    private void ClearStandaloneManifestList()
    {
      if (!_standaloneManifestMode) return;

      string currentUrl = GetUrlString(GetCurrentControllerUrl());
      if (Utilities.IsValid(_controller) && Utilities.IsValid(_controller.Queue) &&
          Networking.IsOwner(_controller.gameObject) && _controller.Queue.Length > 0)
      {
        _clearingUnifiedQueue = true;
        _controller.Queue.TakeOwnership();
        _controller.Queue.Tracks.Clear();
        _controller.Queue.SendEvent();
        _clearingUnifiedQueue = false;
      }
      ExitStandaloneManifestModeForNewRequest();
      CancelUnifiedQueueRequest();
      _expandedPlaybackSourceUrl = currentUrl;
      _lastObservedPlaybackUrl = currentUrl;
      _pageOffset = 0;
      _pageViewPinned = false;
      EnsureUnifiedQueueHeaderMatchesTracks();
      UpdateLabels();
      SetStatus("已清空播放列表");
    }

    private void ClearBiliMixedManifestAndQueue()
    {
      if (!_biliMixedManifestMode || !Utilities.IsValid(_controller)) return;
      string currentUrl = GetUrlString(GetCurrentControllerUrl());
      Playlist queue = _controller.Queue;
      _clearingUnifiedQueue = true;
      if (Utilities.IsValid(queue))
      {
        queue.TakeOwnership();
        queue.Tracks.Clear();
        queue.SendEvent();
      }
      _clearingUnifiedQueue = false;

      RestoreControllerForward();
      _biliMixedManifestMode = false;
      _currentPlaybackIsManifestItem = false;
      _biliManifestPlaybackLocked = false;
      _advanceQueueAfterManifestPending = false;
      _syncedDeletedManifestVcrids = new int[0];
      ResetBiliManifestState();
      CancelUnifiedQueueRequest();
      _expandedPlaybackSourceUrl = currentUrl;
      _lastObservedPlaybackUrl = currentUrl;
      PublishManifestState(VRCUrl.Empty, 0, false);
      _pageOffset = 0;
      _pageViewPinned = false;
      InvalidateUnifiedDisplayCache();
      HideAllDeleteIcons();
      EnsureUnifiedQueueHeaderMatchesTracks();
      UpdateLabels();
      SetStatus("已清空播放队列");
    }

    private void ResetBiliManifestState()
    {
      _biliMixedManifestMode = false;
      _biliManifestPlaybackLocked = false;
      _biliManifestParts = new string[0];
      _biliManifestPageNumbers = new int[0];
      _biliManifestVcrids = new int[0];
      _biliManifestTotalPages = 0;
      _biliManifestSelectedIndex = -1;
      _biliManifestTitle = "";
      _biliManifestSourceUrl = VRCUrl.Empty;
      InvalidateUnifiedDisplayCache();
    }

    private void RefreshStandaloneManifestSelection()
    {
      if (!_standaloneManifestMode || _totalPages <= 0) return;
      if (_internalTrackSwitch || _pendingStandaloneVcrid > 0)
      {
        UpdateLabels();
        return;
      }
      ApplyCurrentOrSyncedSelection();
      UpdateLabels();
      LoadSelectedNeteaseDanmaku();
    }

    private void ConfirmStandaloneManifestPlayback()
    {
      if (!_standaloneManifestMode || _totalPages <= 0) return;

      int pendingVcrid = _pendingStandaloneVcrid;
      int currentIndex = FindManifestItemIndexFromUrl(GetUrlString(GetCurrentControllerUrl()));
      if (currentIndex >= 0)
      {
        _selectedIndex = currentIndex;
        if (!_pageViewPinned)
          _pageOffset = Mathf.Max(0, (_selectedIndex / VisibleButtonCount) * VisibleButtonCount);
      }
      else if (pendingVcrid > 0)
      {
        SelectManifestItemByVcrid(pendingVcrid);
      }
      else
      {
        ApplyCurrentOrSyncedSelection();
      }

      _pendingStandaloneVcrid = 0;
      UpdateLabels();
      LoadSelectedNeteaseDanmaku();
      Debug.Log("[PaulKoiPages] provider=netease stage=video-start currentIndex=" + currentIndex +
                " selected=" + _selectedIndex + " vcrid=" + GetSelectedVcrid() + " pending=" + pendingVcrid);
    }

    private void CompleteUnifiedRequestWithRawFallback(string status)
    {
      int requestMode = _unifiedRequestMode;
      VRCUrl sourceUrl = _unifiedRequestSourceUrl;
      bool startWhenReady = _unifiedStartWhenReady;
      int normalizeQueueIndex = _normalizeQueueIndex;
      string normalizeQueueTrackUrl = _normalizeQueueTrackUrl;
      _unifiedRequestMode = RequestModeNone;
      _unifiedRequestSourceUrl = VRCUrl.Empty;
      _unifiedStartWhenReady = false;
      _completeUnifiedRequestAfterMetadata = false;
      _normalizeQueueIndex = -1;
      _normalizeQueueTrackUrl = "";
      _loading = false;

      if (requestMode == RequestModeNormalizeQueued)
      {
        MarkQueuedTrackAsUnnamed(normalizeQueueIndex, normalizeQueueTrackUrl, status);
        return;
      }

      if (requestMode == RequestModeEnqueue)
      {
        startWhenReady = Utilities.IsValid(_controller) && _controller.Stopped && !_controller.IsLoading &&
                         Utilities.IsValid(_controller.Queue) && _controller.Queue.Length == 0;
        if (_preserveStandaloneManifestRequest) startWhenReady = false;
      }

      if (requestMode == RequestModeExpandCurrent)
      {
        _expandedPlaybackSourceUrl = GetUrlString(sourceUrl);
        if (!_syncedQueueManaged) PublishUnifiedQueueHeader("播放队列", VRCUrl.Empty, true, true);
        SetStatus(status);
        UpdateLabels();
        ScheduleNormalizeQueue();
        return;
      }

      if (requestMode != RequestModeEnqueue || !Utilities.IsValid(_controller))
      {
        if (_preserveStandaloneManifestRequest) RestorePreservedStandaloneManifest();
        return;
      }
      Track track = Track.New(_controller.VideoPlayerType, GetUrlString(sourceUrl), sourceUrl);
      bool hadExisting = HasRetainedBiliManifestItems() || GetRetainedHistoryDisplayCount() > 0 || HasCurrentUnifiedTrack() ||
                         (Utilities.IsValid(_controller.Queue) && _controller.Queue.Length > 0);
      MergeUnifiedQueueHeader(sourceUrl, "播放队列", hadExisting);
      _controller.TakeOwnership();
      if (startWhenReady)
      {
        _controller.PlayTrack(track);
      }
      else if (Utilities.IsValid(_controller.Queue) && GetUnifiedDisplayCount() < MaxUnifiedQueueItems)
      {
        _controller.Queue.TakeOwnership();
        _controller.Queue.AddTrack(track);
      }
      if (_preserveStandaloneManifestRequest) RestorePreservedStandaloneManifest();
      SetStatus(status);
      UpdateLabels();
      ScheduleNormalizeQueue();
    }

    private void ReplaceQueuedTrackWithParsedTracks(VRCUrl sourceUrl, int preferredQueueIndex, string expectedTrackUrl)
    {
      if (!Utilities.IsValid(_controller) || !Utilities.IsValid(_controller.Queue)) return;
      Playlist queue = _controller.Queue;
      int queueIndex = FindQueuedTrackIndex(preferredQueueIndex, expectedTrackUrl);
      if (queueIndex < 0)
      {
        ScheduleNormalizeQueue();
        return;
      }

      int firstIndex = _selectedIndex >= 0 && _selectedIndex < _totalPages ? _selectedIndex : 0;
      int validCount = 0;
      string originalUrl = GetUrlString(sourceUrl);
      for (int i = firstIndex; i < _totalPages; i++)
      {
        if (IsUsableTrack(CreateParsedTrack(i, originalUrl))) validCount++;
      }
      if (validCount <= 0)
      {
        MarkQueuedTrackAsUnnamed(queueIndex, expectedTrackUrl, "没有可用的播放地址，已保留该链接");
        return;
      }

      bool hadOtherEntries = HasRetainedBiliManifestItems() || GetRetainedHistoryDisplayCount() > 0 ||
                             HasCurrentUnifiedTrack() || queue.Length > 1;
      string sourceTitle = GetParsedQueueTitle(firstIndex);
      MergeUnifiedQueueHeader(sourceUrl, sourceTitle, hadOtherEntries);

      queue.TakeOwnership();
      _controller.TakeOwnership();
      _clearingUnifiedQueue = true;
      queue.RemoveTrack(queueIndex);
      InvalidateUnifiedDisplayCache();

      int occupied = GetUnifiedDisplayCount();
      int available = Mathf.Max(0, MaxUnifiedQueueItems - occupied);
      int added = 0;
      bool truncated = false;
      for (int i = firstIndex; i < _totalPages; i++)
      {
        Track track = CreateParsedTrack(i, originalUrl);
        if (!IsUsableTrack(track)) continue;
        if (available <= 0)
        {
          truncated = true;
          break;
        }

        queue.AddTrack(track);
        int targetIndex = Mathf.Min(queueIndex + added, queue.Length - 1);
        int addedIndex = queue.Length - 1;
        while (addedIndex > targetIndex)
        {
          queue.MoveUp(addedIndex);
          addedIndex--;
        }
        available--;
        added++;
      }
      _clearingUnifiedQueue = false;

      _syncedQueueManaged = true;
      _unifiedQueueLimitReached = truncated;
      SetStatus(truncated
        ? "队列最多保留 " + MaxUnifiedQueueItems + " 项"
        : "已读取名称并加入播放队列");
      InvalidateUnifiedDisplayCache();
      if (added > 0) FocusUnifiedQueueIndex(queueIndex);
      ClampUnifiedPageOffset();
      UpdateLabels();
      ScheduleNormalizeQueue();
    }

    private int FindQueuedTrackIndex(int preferredQueueIndex, string expectedTrackUrl)
    {
      if (!Utilities.IsValid(_controller) || !Utilities.IsValid(_controller.Queue) || string.IsNullOrEmpty(expectedTrackUrl)) return -1;
      Playlist queue = _controller.Queue;
      if (preferredQueueIndex >= 0 && preferredQueueIndex < queue.Length &&
          GetTrackVrcUrl(queue.GetTrack(preferredQueueIndex)) == expectedTrackUrl)
      {
        return preferredQueueIndex;
      }

      for (int i = 0; i < queue.Length; i++)
      {
        if (GetTrackVrcUrl(queue.GetTrack(i)) == expectedTrackUrl) return i;
      }
      return -1;
    }

    private void MarkQueuedTrackAsUnnamed(int preferredQueueIndex, string expectedTrackUrl, string status)
    {
      if (!Utilities.IsValid(_controller) || !Utilities.IsValid(_controller.Queue)) return;
      Playlist queue = _controller.Queue;
      int queueIndex = FindQueuedTrackIndex(preferredQueueIndex, expectedTrackUrl);
      if (queueIndex >= 0)
      {
        Track track = queue.GetTrack(queueIndex);
        if (IsUsableTrack(track) && !track.HasTitle())
        {
          queue.TakeOwnership();
          string fallbackTitle = string.IsNullOrEmpty(expectedTrackUrl) ? GetTrackVrcUrl(track) : expectedTrackUrl;
          track.SetTitle(fallbackTitle);
          queue.SendEvent();
        }
      }
      SetStatus(status);
      UpdateLabels();
      ScheduleNormalizeQueue();
    }

    private void SetCurrentTrackTitle(string title)
    {
      if (!Utilities.IsValid(_controller) || string.IsNullOrEmpty(title)) return;
      Track track = _controller.Track;
      if (!IsUsableTrack(track) || (track.HasTitle() && track.GetTitle() == title)) return;

      track.SetTitle(title);
      if (Networking.IsOwner(_controller.gameObject)) _controller.RequestSerialization();
    }

    private void AddParsedTracksToQueue(int firstIndex, VRCUrl sourceUrl, bool playFirst, bool insertAtFront)
    {
      if (!Utilities.IsValid(_controller) || firstIndex < 0 || firstIndex >= _totalPages) return;
      Playlist queue = _controller.Queue;
      if (!Utilities.IsValid(queue)) return;

      string originalUrl = GetUrlString(sourceUrl);
      int occupied = GetUnifiedDisplayCount();
      int available = Mathf.Max(0, MaxUnifiedQueueItems - occupied);
      if (playFirst) available = Mathf.Max(0, available - 1);
      int added = 0;
      int firstAddedQueueIndex = -1;
      bool truncated = false;
      queue.TakeOwnership();
      _controller.TakeOwnership();
      _clearingUnifiedQueue = true;

      for (int i = firstIndex; i < _totalPages; i++)
      {
        Track track = CreateParsedTrack(i, originalUrl);
        if (!IsUsableTrack(track)) continue;

        if (playFirst)
        {
          _controller.PlayTrack(track);
          playFirst = false;
          continue;
        }

        if (available <= 0)
        {
          truncated = true;
          break;
        }
        int insertionIndex = insertAtFront ? added : queue.Length;
        queue.AddTrack(track);
        if (firstAddedQueueIndex < 0) firstAddedQueueIndex = insertionIndex;
        if (insertAtFront)
        {
          int queueIndex = queue.Length - 1;
          while (queueIndex > insertionIndex)
          {
            queue.MoveUp(queueIndex);
            queueIndex--;
          }
        }
        available--;
        added++;
      }
      _clearingUnifiedQueue = false;
      InvalidateUnifiedDisplayCache();
      if (firstAddedQueueIndex >= 0 && !insertAtFront) FocusUnifiedQueueIndex(firstAddedQueueIndex);

      _syncedQueueManaged = true;
      if (truncated)
      {
        _unifiedQueueLimitReached = true;
        SetStatus("队列最多保留 " + MaxUnifiedQueueItems + " 项");
      }
      else
      {
        SetStatus("已加入播放队列");
      }
    }

    private Track CreateParsedTrack(int index, string originalUrl)
    {
      if (index < 0 || index >= _vcrids.Length || !Utilities.IsValid(_controller)) return Track.Empty();
      int vcrid = _vcrids[index];
      if (vcrid <= 0 || vcrid > _vcridMax || _vcridUrls == null || vcrid >= _vcridUrls.Length) return Track.Empty();

      VRCUrl playUrl = _vcridUrls[vcrid];
      if (!IsPlayableUrl(GetUrlString(playUrl))) return Track.Empty();
      string title = BuildParsedTrackTitle(index);
      string trackOriginalUrl = BuildParsedTrackOriginalUrl(GetUrlString(playUrl), originalUrl);
      return string.IsNullOrEmpty(trackOriginalUrl)
        ? Track.New(_controller.VideoPlayerType, title, playUrl)
        : Track.New(_controller.VideoPlayerType, title, playUrl, trackOriginalUrl);
    }

    private string BuildParsedTrackOriginalUrl(string playUrl, string originalUrl)
    {
      if (string.IsNullOrEmpty(playUrl) || string.IsNullOrEmpty(originalUrl)) return "";
      string trackOriginalUrl = playUrl;
      trackOriginalUrl += (trackOriginalUrl.IndexOf("?") >= 0 ? "&" : "?") +
                          "pk_group=" + GetStableSourceId(originalUrl) +
                          "&pk_provider=" + (_isNeteasePlaylist ? "netease" : "bilibili");
      if (ShouldRetainParsedBiliPages()) trackOriginalUrl += "&pk_keep=bilibili_pages";
      if (_parsedShouldCycle) trackOriginalUrl += "&pk_cycle=netease_playlist";
      return trackOriginalUrl;
    }

    private bool ShouldRetainParsedBiliPages()
    {
      return _totalPages > 1 && !_parsedIsBilibiliList && !_isNeteasePlaylist;
    }

    private void PrepareCurrentTrackRetention(VRCUrl sourceUrl)
    {
      if (!ShouldRetainParsedBiliPages() || !Utilities.IsValid(_controller))
      {
        PublishPendingCurrentRetention("", "");
        return;
      }
      Track track = _controller.Track;
      if (!IsUsableTrack(track)) return;

      string markerUrl = BuildParsedTrackOriginalUrl(GetTrackVrcUrl(track), GetUrlString(sourceUrl));
      if (string.IsNullOrEmpty(markerUrl)) return;
      PublishPendingCurrentRetention(GetTrackVrcUrl(track), markerUrl);
    }

    private string BuildParsedTrackTitle(int index)
    {
      string part = index >= 0 && index < _parts.Length ? SanitizeUiText(_parts[index]) : "";
      string sourceTitle = SanitizeUiText(_parsedSourceTitle);
      return string.IsNullOrEmpty(part) ? sourceTitle : part;
    }

    private string GetParsedQueueTitle(int selectedIndex)
    {
      string title = SanitizeUiText(_parsedSourceTitle);
      if (!string.IsNullOrEmpty(title) && title != "正在读取歌曲信息") return title;
      string itemTitle = BuildParsedTrackTitle(selectedIndex);
      return string.IsNullOrEmpty(itemTitle) ? "播放队列" : itemTitle;
    }

    private void MergeUnifiedQueueHeader(VRCUrl sourceUrl, string sourceTitle, bool hadExisting)
    {
      string source = GetUrlString(sourceUrl);
      string currentSource = GetUrlString(_syncedQueueSourceUrl);
      bool sameSource = !string.IsNullOrEmpty(source) && source == currentSource;
      string title = string.IsNullOrEmpty(sourceTitle) ? "播放队列" : sourceTitle;

      if (!hadExisting)
      {
        PublishUnifiedQueueHeader(title, sourceUrl, false, true);
      }
      else if (_syncedQueueManaged && !_syncedQueueMixed && sameSource)
      {
        PublishUnifiedQueueHeader(_syncedQueueTitle, _syncedQueueSourceUrl, false, true);
      }
      else
      {
        PublishUnifiedQueueHeader("播放队列", VRCUrl.Empty, true, true);
      }
    }

    private void PublishUnifiedQueueHeader(string title, VRCUrl sourceUrl, bool mixed, bool managed)
    {
      if (!Utilities.IsValid(Networking.LocalPlayer)) return;
      TakeOwnership();
      _syncedQueueTitle = string.IsNullOrEmpty(title) ? "播放队列" : SanitizeUiText(title);
      _syncedQueueSourceUrl = sourceUrl;
      _syncedQueueMixed = mixed;
      _syncedQueueManaged = managed;
      _syncedQueueRevision++;
      if (_syncedQueueRevision < 0) _syncedQueueRevision = 1;
      RequestSerialization();
    }

    private void EnsureUnifiedQueueHeaderMatchesTracks()
    {
      if (!Utilities.IsValid(_controller)) return;
      if (!Networking.IsOwner(_controller.gameObject)) return;

      Playlist queue = _controller.Queue;
      bool hasCurrent = HasCurrentUnifiedTrack();
      bool hasQueued = Utilities.IsValid(queue) && queue.Length > 0;
      bool hasRetained = GetRetainedHistoryDisplayCount() > 0 || HasRetainedBiliManifestItems();
      if (!hasCurrent && !hasQueued && !hasRetained)
      {
        if (_syncedQueueManaged || _syncedQueueMixed || _syncedQueueTitle != "播放队列" ||
            !VRCUrl.IsNullOrEmpty(_syncedQueueSourceUrl))
        {
          PublishUnifiedQueueHeader("播放队列", VRCUrl.Empty, false, false);
        }
        return;
      }

      if (!_syncedQueueManaged || _syncedQueueMixed) return;

      string expected = GetUrlString(_syncedQueueSourceUrl);
      if (string.IsNullOrEmpty(expected)) return;
      Track currentTrack = _controller.Track;
      if (IsUsableTrack(currentTrack) && !IsCurrentBiliManifestDisplayItem() &&
          !TrackMatchesUnifiedSource(currentTrack, expected))
      {
        PublishUnifiedQueueHeader("播放队列", VRCUrl.Empty, true, true);
        return;
      }

      if (!Utilities.IsValid(queue)) return;
      for (int i = 0; i < queue.Length; i++)
      {
        Track track = queue.GetTrack(i);
        if (!IsUsableTrack(track)) continue;
        if (TrackMatchesUnifiedSource(track, expected)) continue;
        PublishUnifiedQueueHeader("播放队列", VRCUrl.Empty, true, true);
        return;
      }

      Playlist history = _controller.History;
      if (!Utilities.IsValid(history)) return;
      int retainedCount = GetRetainedHistoryDisplayCount();
      for (int i = 0; i < retainedCount; i++)
      {
        int historyIndex = GetRetainedHistoryIndex(i);
        if (historyIndex < 0 || TrackMatchesUnifiedSource(history.GetTrack(historyIndex), expected)) continue;
        PublishUnifiedQueueHeader("播放队列", VRCUrl.Empty, true, true);
        return;
      }
    }

    private bool TrackMatchesUnifiedSource(Track track, string expectedSource)
    {
      if (!IsUsableTrack(track) || string.IsNullOrEmpty(expectedSource)) return false;
      string source = track.GetOriginalUrl();
      if (string.IsNullOrEmpty(source)) return GetUrlString(track.GetVRCUrl()) == expectedSource;

      int marker = source.IndexOf("pk_group=");
      if (marker < 0) return source == expectedSource;
      int actualGroup = ParseIntString(source.Substring(marker + 9), 0);
      return actualGroup > 0 && actualGroup == GetStableSourceId(expectedSource);
    }

    private int GetStableSourceId(string source)
    {
      if (string.IsNullOrEmpty(source)) return 0;
      int hash = 17;
      for (int i = 0; i < source.Length; i++)
      {
        hash = hash * 31 + source[i];
      }
      hash &= 0x7fffffff;
      return hash == 0 ? 1 : hash;
    }

    private void SelectVisiblePage(int visibleIndex)
    {
      _pageViewPinned = false;
      if (_useUnifiedQueue && _standaloneManifestMode)
      {
        int standaloneIndex = _pageOffset + visibleIndex;
        if (standaloneIndex < _totalPages)
          PlayPageIndex(standaloneIndex);
        else
          PlayStandaloneQueueTrack(standaloneIndex - _totalPages);
        return;
      }
      if (_useUnifiedQueue)
      {
        PlayUnifiedDisplayIndex(_pageOffset + visibleIndex);
        return;
      }
      int index = _pageOffset + visibleIndex;
      PlayPageIndex(index);
    }

    private void PlayStandaloneQueueTrack(int queueIndex)
    {
      if (!Utilities.IsValid(_controller) || !Utilities.IsValid(_controller.Queue) ||
          queueIndex < 0 || queueIndex >= _controller.Queue.Length) return;

      Track selectedTrack = _controller.Queue.GetTrack(queueIndex);
      if (!IsUsableTrack(selectedTrack)) return;
      string selectedUrl = GetTrackVrcUrl(selectedTrack);
      _pendingStandaloneQueueRemovalIndex = queueIndex;
      _pendingStandaloneQueueRemovalUrl = selectedUrl;
      Debug.Log("[PaulKoiPages] provider=netease stage=play-queued-external queueIndex=" + queueIndex +
                " url=" + selectedUrl);

      ExitStandaloneManifestModeForUnifiedQueueRequest();
      EnsureHistoryOwnershipForControllerOwner();
      _controller.TakeOwnership();
      _cycleAppendPending = false;
      _controller.PlayTrack(selectedTrack);
      InvalidateUnifiedDisplayCache();
      FocusUnifiedCurrentPage();
      UpdateLabels();
      SetStatus("正在播放所选项目");
    }

    private void TryFinalizeStandaloneQueueSelection()
    {
      if (string.IsNullOrEmpty(_pendingStandaloneQueueRemovalUrl) || !Utilities.IsValid(_controller) ||
          !Utilities.IsValid(_controller.Queue)) return;
      if (!IsUsableTrack(_controller.Track) ||
          GetTrackVrcUrl(_controller.Track) != _pendingStandaloneQueueRemovalUrl) return;

      int removeIndex = FindQueuedTrackIndex(_pendingStandaloneQueueRemovalIndex,
        _pendingStandaloneQueueRemovalUrl);
      string confirmedUrl = _pendingStandaloneQueueRemovalUrl;
      _pendingStandaloneQueueRemovalIndex = -1;
      _pendingStandaloneQueueRemovalUrl = "";
      if (removeIndex >= 0)
      {
        _controller.Queue.TakeOwnership();
        _controller.Queue.RemoveTrack(removeIndex);
      }
      InvalidateUnifiedDisplayCache();
      FocusUnifiedCurrentPage();
      UpdateLabels();
      Debug.Log("[PaulKoiPages] stage=confirmed-external-current url=" + confirmedUrl);
    }

    private void PlayUnifiedDisplayIndex(int displayIndex)
    {
      if (!Utilities.IsValid(_controller) || displayIndex < 0 || displayIndex >= GetUnifiedDisplayCount()) return;
      if (GetUnifiedDisplaySourceType(displayIndex) == DisplaySourceManifest)
      {
        PlayBiliManifestIndex(GetUnifiedDisplaySourceIndex(displayIndex));
        return;
      }
      Track selectedTrack = GetUnifiedDisplayTrack(displayIndex);
      if (!IsUsableTrack(selectedTrack)) return;
      if (IsCurrentUnifiedTrack(selectedTrack))
      {
        SetStatus("当前项目正在播放");
        return;
      }

      Playlist queue = _controller.Queue;
      int queueIndex = GetUnifiedDisplayQueueIndex(displayIndex);
      string selectedUrl = GetTrackVrcUrl(selectedTrack);
      if (!selectedTrack.HasTitle() && !IsVcridPlaybackUrl(selectedUrl) && IsPagesRequestUrl(selectedUrl))
      {
        SetStatus("正在读取所选项目，请稍候");
        ScheduleNormalizeQueue();
        return;
      }
      if (_biliMixedManifestMode)
      {
        _currentPlaybackIsManifestItem = false;
        _biliManifestPlaybackLocked = false;
        _autoAdvancePending = false;
        _advanceQueueAfterManifestPending = false;
        RestoreControllerForward();
      }
      EnsureHistoryOwnershipForControllerOwner();
      _controller.TakeOwnership();
      EnsureHistoryOwnershipForControllerOwner();
      _cycleAppendPending = false;
      _controller.PlayTrack(selectedTrack);
      if (Utilities.IsValid(queue))
      {
        int removeIndex = FindQueuedTrackIndex(queueIndex, selectedUrl);
        if (removeIndex >= 0)
        {
          queue.TakeOwnership();
          queue.RemoveTrack(removeIndex);
        }
      }
      FocusUnifiedCurrentPage();
      SetStatus("正在播放所选项目");
      UpdateLabels();
    }

    private void PlayBiliManifestIndex(int index)
    {
      if (!_biliMixedManifestMode || index < 0 || index >= _biliManifestTotalPages || IsBiliManifestItemDeleted(index)) return;
      if (!Utilities.IsValid(_controller)) return;

      int vcrid = GetBiliManifestVcrid(index);
      if (vcrid <= 0 || vcrid > _vcridMax || _vcridUrls == null || vcrid >= _vcridUrls.Length)
      {
        SetStatus("播放项目无效");
        return;
      }

      VRCUrl pageUrl = _vcridUrls[vcrid];
      string playUrl = GetUrlString(pageUrl);
      if (!IsPlayableUrl(playUrl))
      {
        SetStatus("播放地址不存在");
        return;
      }

      if (_biliManifestSelectedIndex == index && _currentPlaybackIsManifestItem &&
          !_controller.Stopped && !_controller.IsLoading)
      {
        SetStatus("当前正在播放第 " + GetBiliManifestPageNumber(index) + " 项");
        return;
      }

      _controller.TakeOwnership();
      _biliManifestSelectedIndex = index;
      _currentPlaybackIsManifestItem = true;
      _biliManifestPlaybackLocked = true;
      _advanceQueueAfterManifestPending = false;
      _autoAdvancePending = false;
      _pendingNextIndex = -1;
      _lastObservedPlaybackUrl = playUrl;
      _internalTrackSwitch = true;
      SuppressControllerForward();
      InvalidateUnifiedDisplayCache();
      UpdateLabels();
      _controller.PlayTrack(Track.New(_controller.VideoPlayerType, GetBiliManifestPart(index), pageUrl));
      PublishManifestState(_biliManifestSourceUrl, vcrid, true);
      SendCustomEventDelayedFrames(nameof(CleanupActiveBiliManifestQueueEntries), 1);
      SendCustomEventDelayedFrames(nameof(RestoreBiliManifestViewAfterTrackSwitch), 1);
      SetStatus("正在播放第 " + GetBiliManifestPageNumber(index) + " 项");
    }

    public void RestoreBiliManifestViewAfterTrackSwitch()
    {
      if (_biliManifestTotalPages <= 1 || _biliManifestVcrids == null || _biliManifestVcrids.Length <= 1) return;

      string currentUrl = GetUrlString(GetCurrentControllerUrl());
      if (string.IsNullOrEmpty(currentUrl)) return;

      _standaloneManifestMode = false;
      _biliMixedManifestMode = true;
      bool selected = SelectBiliManifestItemFromUrl(currentUrl);
      if (selected)
      {
        _currentPlaybackIsManifestItem = true;
        _biliManifestPlaybackLocked = true;
        SuppressControllerForward();
      }
      else if (HasCurrentUnifiedTrack())
      {
        _currentPlaybackIsManifestItem = false;
        _biliManifestPlaybackLocked = false;
        RestoreControllerForward();
      }
      InvalidateUnifiedDisplayCache();
      FocusUnifiedCurrentPage();
      UpdateLabels();
    }

    public void CleanupActiveBiliManifestQueueEntries()
    {
      if (!_biliMixedManifestMode || _biliManifestTotalPages <= 1 || !Utilities.IsValid(_controller) ||
          !Utilities.IsValid(_controller.Queue) || !Networking.IsOwner(_controller.gameObject)) return;

      Playlist queue = _controller.Queue;
      bool removed = false;
      queue.TakeOwnership();
      _clearingUnifiedQueue = true;
      for (int i = queue.Length - 1; i >= 0; i--)
      {
        Track track = queue.GetTrack(i);
        if (!IsActiveBiliManifestQueueTrack(track)) continue;
        queue.RemoveTrack(i);
        removed = true;
      }
      _clearingUnifiedQueue = false;
      if (!removed) return;

      InvalidateUnifiedDisplayCache();
      ClampUnifiedPageOffset();
      UpdateLabels();
    }

    private int GetBiliManifestVcrid(int index)
    {
      return index >= 0 && index < _biliManifestVcrids.Length ? _biliManifestVcrids[index] : 0;
    }

    private int GetBiliManifestPageNumber(int index)
    {
      return index >= 0 && index < _biliManifestPageNumbers.Length ? _biliManifestPageNumbers[index] : index + 1;
    }

    private string GetBiliManifestPart(int index)
    {
      if (index < 0 || index >= _biliManifestParts.Length) return "P" + (index + 1);
      string part = SanitizeUiText(_biliManifestParts[index]);
      return string.IsNullOrEmpty(part) ? "P" + GetBiliManifestPageNumber(index) : part;
    }

    private string FormatBiliManifestLabel(int index)
    {
      int page = GetBiliManifestPageNumber(index);
      string pageText = page < 10 ? "0" + page : page.ToString();
      return pageText + "  " + Shorten(GetBiliManifestPart(index), 22);
    }

    private bool IsBiliManifestItemDeleted(int index)
    {
      int vcrid = GetBiliManifestVcrid(index);
      if (vcrid <= 0 || _syncedDeletedManifestVcrids == null) return false;
      for (int i = 0; i < _syncedDeletedManifestVcrids.Length; i++)
      {
        if (_syncedDeletedManifestVcrids[i] == vcrid) return true;
      }
      return false;
    }

    private int GetVisibleManifestItemCount()
    {
      if (!_biliMixedManifestMode) return 0;
      int count = 0;
      for (int i = 0; i < _biliManifestTotalPages; i++)
      {
        if (!IsBiliManifestItemDeleted(i)) count++;
      }
      return count;
    }

    private bool HasRetainedBiliManifestItems()
    {
      return _biliMixedManifestMode && GetVisibleManifestItemCount() > 0;
    }

    private int FindNextVisibleBiliManifestIndex(int startIndex)
    {
      for (int i = Mathf.Max(0, startIndex); i < _biliManifestTotalPages; i++)
      {
        if (!IsBiliManifestItemDeleted(i)) return i;
      }
      return -1;
    }

    private bool SelectBiliManifestItemFromUrl(string url)
    {
      int index = FindBiliManifestIndexFromUrl(url);
      if (index < 0) return false;
      _biliManifestSelectedIndex = index;
      return true;
    }

    private int FindBiliManifestIndexFromUrl(string url)
    {
      if (!_biliMixedManifestMode || string.IsNullOrEmpty(url)) return -1;
      string lower = url.ToLower();
      int marker = lower.IndexOf("vcrid=");
      if (marker < 0) return -1;
      int vcrid = ParseIntString(url.Substring(marker + 6), 0);
      return FindBiliManifestIndexByVcrid(vcrid);
    }

    private int FindBiliManifestIndexByVcrid(int vcrid)
    {
      if (vcrid <= 0) return -1;
      for (int i = 0; i < _biliManifestVcrids.Length; i++)
      {
        if (_biliManifestVcrids[i] == vcrid) return i;
      }
      return -1;
    }

    private bool SelectBiliManifestItemByVcrid(int vcrid)
    {
      int index = FindBiliManifestIndexByVcrid(vcrid);
      if (index < 0) return false;
      _biliManifestSelectedIndex = index;
      return true;
    }

    private int GetActiveBiliManifestIndex()
    {
      if (!_biliMixedManifestMode || _biliManifestTotalPages <= 1) return -1;

      if (Utilities.IsValid(_controller) && (!_controller.Stopped || _controller.IsLoading))
      {
        int currentIndex = FindBiliManifestIndexFromUrl(GetUrlString(GetCurrentControllerUrl()));
        if (currentIndex >= 0) return currentIndex;
        if (CurrentTrackTitleMatchesSelectedBiliManifestPart()) return _biliManifestSelectedIndex;

        if (_biliManifestPlaybackLocked || _currentPlaybackIsManifestItem)
        {
          int syncedIndex = FindBiliManifestIndexByVcrid(_syncedSelectedVcrid);
          if (syncedIndex >= 0) return syncedIndex;
        }
        return -1;
      }

      return (_biliManifestPlaybackLocked || _currentPlaybackIsManifestItem) &&
             _biliManifestSelectedIndex >= 0 && _biliManifestSelectedIndex < _biliManifestTotalPages
        ? _biliManifestSelectedIndex
        : -1;
    }

    private void ActivateSyncedBiliManifestFromParsed()
    {
      if (_totalPages <= 0) return;
      _standaloneManifestMode = false;
      _biliMixedManifestMode = true;
      _biliManifestParts = _parts;
      _biliManifestPageNumbers = _pageNumbers;
      _biliManifestVcrids = _vcrids;
      _biliManifestTotalPages = _totalPages;
      _biliManifestTitle = GetParsedQueueTitle(_selectedIndex >= 0 ? _selectedIndex : 0);
      _biliManifestSourceUrl = _syncedManifestUrl;
      _biliManifestSelectedIndex = _selectedIndex >= 0 ? _selectedIndex : 0;
      string currentUrl = GetUrlString(GetCurrentControllerUrl());
      bool currentTrackIsManifestItem = SelectBiliManifestItemFromUrl(currentUrl);
      if (!currentTrackIsManifestItem) SelectBiliManifestItemByVcrid(_syncedSelectedVcrid);
      bool sourceRequestIsStillManifest = currentUrl == GetUrlString(_biliManifestSourceUrl) &&
                                          (_biliManifestPlaybackLocked || _currentPlaybackIsManifestItem ||
                                           CurrentTrackTitleMatchesSelectedBiliManifestPart());
      _currentPlaybackIsManifestItem = currentTrackIsManifestItem || sourceRequestIsStillManifest;
      _biliManifestPlaybackLocked = _currentPlaybackIsManifestItem;
      if (_currentPlaybackIsManifestItem) SuppressControllerForward();
      ResetParsedSource();
      InvalidateUnifiedDisplayCache();
      UpdateLabels();
    }

    private void ApplySyncedManifestModeFlags()
    {
      if (_syncedManifestMode == ManifestModeNeteaseExclusive)
      {
        RestoreControllerForward();
        ResetBiliManifestState();
        _standaloneManifestMode = true;
        _currentPlaybackIsManifestItem = true;
        return;
      }
      if (_syncedManifestMode == ManifestModeBilibiliMixed)
      {
        _standaloneManifestMode = false;
        _biliMixedManifestMode = true;
        return;
      }

      RestoreControllerForward();
      _standaloneManifestMode = false;
      ResetBiliManifestState();
      _currentPlaybackIsManifestItem = false;
      _biliManifestPlaybackLocked = false;
    }

    private void RefreshBiliPlaybackSource(string request)
    {
      if (!_biliMixedManifestMode) return;
      if (string.IsNullOrEmpty(request) &&
          (_naturalEndPending || _autoAdvancePending || _internalTrackSwitch || _advanceQueueAfterManifestPending)) return;
      bool sourceRequestIsStillManifest = !string.IsNullOrEmpty(request) &&
                                          request == GetUrlString(_biliManifestSourceUrl) &&
                                          (_biliManifestPlaybackLocked || _currentPlaybackIsManifestItem ||
                                           CurrentTrackTitleMatchesSelectedBiliManifestPart());
      bool manifestItem = SelectBiliManifestItemFromUrl(request) ||
                          sourceRequestIsStillManifest;
      if (manifestItem)
      {
        _currentPlaybackIsManifestItem = true;
        _biliManifestPlaybackLocked = true;
        SuppressControllerForward();
        int vcrid = GetBiliManifestVcrid(_biliManifestSelectedIndex);
        if (vcrid > 0 && Utilities.IsValid(_controller) && Networking.IsOwner(_controller.gameObject) &&
            vcrid != _syncedSelectedVcrid)
          PublishManifestState(_biliManifestSourceUrl, vcrid, true);
      }
      else if (_biliManifestPlaybackLocked && string.IsNullOrEmpty(request) &&
               Utilities.IsValid(_controller) && (!_controller.Stopped || _controller.IsLoading))
      {
        _currentPlaybackIsManifestItem = true;
      }
      else if (_biliManifestPlaybackLocked && Utilities.IsValid(_controller) &&
               (!_controller.Stopped || _controller.IsLoading) &&
               CurrentTrackTitleMatchesSelectedBiliManifestPart())
      {
        _currentPlaybackIsManifestItem = true;
      }
      else
      {
        _currentPlaybackIsManifestItem = false;
        _biliManifestPlaybackLocked = false;
        RestoreControllerForward();
      }
      InvalidateUnifiedDisplayCache();
    }

    private void SuppressControllerForward()
    {
      if (_controllerForwardSuppressed || !Utilities.IsValid(_controller) ||
          !Networking.IsOwner(_controller.gameObject)) return;
      object value = _controller.GetProgramVariable("_forwardInterval");
      _savedControllerForwardInterval = Utilities.IsValid(value) ? (float)value : 0f;
      _controller.SetProgramVariable("_forwardInterval", -1f);
      _controllerForwardSuppressed = true;
    }

    private void RestoreControllerForward()
    {
      if (!_controllerForwardSuppressed || !Utilities.IsValid(_controller)) return;
      _controller.SetProgramVariable("_forwardInterval", _savedControllerForwardInterval);
      _controllerForwardSuppressed = false;
    }

    private void PlayPageIndex(int index)
    {
      if (index < 0 || index >= _totalPages) return;

      if (!Utilities.IsValid(_controller))
      {
        SetStatus("未找到播放器控制器");
        return;
      }

      int vcrid = _vcrids[index];
      if (vcrid <= 0 || vcrid > _vcridMax)
      {
        SetStatus("播放项目无效");
        return;
      }

      if (_vcridUrls == null || vcrid >= _vcridUrls.Length)
      {
        SetStatus("缺少播放地址目录");
        return;
      }

      VRCUrl pageUrl = _vcridUrls[vcrid];
      string playUrl = GetUrlString(pageUrl);
      if (!IsPlayableUrl(playUrl))
      {
        SetStatus("播放地址不存在");
        return;
      }

      if (_selectedIndex == index && !_controller.Stopped && !_controller.IsLoading)
      {
        SetStatus("当前正在播放第 " + _pageNumbers[index] + " 项");
        return;
      }

      _selectedIndex = index;
      if (!_pageViewPinned)
        _pageOffset = Mathf.Max(0, (_selectedIndex / VisibleButtonCount) * VisibleButtonCount);
      _lastObservedPlaybackUrl = playUrl;
      _internalTrackSwitch = true;
      _pendingStandaloneVcrid = vcrid;
      _controller.TakeOwnership();
      UpdateLabels();
      if (_isNeteasePlaylist && Utilities.IsValid(_danmakuModule)) _danmakuModule.ClearDanmaku();
      Track pageTrack = _isNeteasePlaylist
        ? Track.New(_controller.VideoPlayerType, _parts[index], pageUrl,
          playUrl + (playUrl.IndexOf("?") >= 0 ? "&" : "?") + "pk_provider=netease")
        : Track.New(_controller.VideoPlayerType, _parts[index], pageUrl);
      _controller.PlayTrack(pageTrack);
      PublishManifestState(GetManifestSourceUrl(), vcrid, true);
      SetStatus("正在播放第 " + _pageNumbers[index] + " 项");
    }

    private bool IsPlayableUrl(string url)
    {
      if (string.IsNullOrEmpty(url)) return false;
      string lower = url.ToLower();
      return lower.StartsWith("http://") || lower.StartsWith("https://");
    }

    private VRCUrl GetCurrentControllerUrl()
    {
      if (!Utilities.IsValid(_controller)) return VRCUrl.Empty;

      Track currentTrack = _controller.Track;
      if (currentTrack.IsValid())
      {
        VRCUrl trackUrl = currentTrack.GetVRCUrl();
        if (!VRCUrl.IsNullOrEmpty(trackUrl)) return trackUrl;
      }

      object currentUrl = _controller.GetProgramVariable("_url");
      if (!Utilities.IsValid(currentUrl)) return VRCUrl.Empty;

      VRCUrl url = (VRCUrl)currentUrl;
      return VRCUrl.IsNullOrEmpty(url) ? VRCUrl.Empty : url;
    }

    private void ConfigureCurrentTrackDanmaku()
    {
      if (!Utilities.IsValid(_controller) || !Utilities.IsValid(_danmakuModule)) return;
      if (_controller.Stopped) return;
      Track track = _controller.Track;
      if (!IsUsableTrack(track)) return;

      VRCUrl currentUrl = track.GetVRCUrl();
      string current = GetUrlString(currentUrl);
      bool netease = IsCurrentPlaybackNetease(track, current);
      bool providerChanged = netease != _lastConfiguredDanmakuWasNetease;
      _danmakuModule.SetExternalAudioMode(netease);
      if (!IsVcridPlaybackUrl(current))
      {
        _lastConfiguredDanmakuWasNetease = netease;
        return;
      }
      if (current == _lastConfiguredDanmakuUrl && !providerChanged) return;

      _danmakuModule.LoadDanmakuUrl(currentUrl);
      _lastConfiguredDanmakuUrl = current;
      _lastConfiguredDanmakuWasNetease = netease;
    }

    private void LoadParsedCurrentDanmaku(int parsedIndex)
    {
      if (!_isNeteasePlaylist || !Utilities.IsValid(_danmakuModule)) return;
      if (parsedIndex < 0 || parsedIndex >= _vcrids.Length) return;
      int vcrid = _vcrids[parsedIndex];
      if (vcrid <= 0 || vcrid > _vcridMax || _vcridUrls == null || vcrid >= _vcridUrls.Length) return;

      VRCUrl lyricsUrl = _vcridUrls[vcrid];
      if (VRCUrl.IsNullOrEmpty(lyricsUrl)) return;
      _danmakuModule.SetExternalAudioMode(true);
      _danmakuModule.LoadDanmakuUrl(lyricsUrl);
      _lastConfiguredDanmakuUrl = GetUrlString(GetCurrentControllerUrl());
      _lastConfiguredDanmakuWasNetease = true;
    }

    private bool IsCurrentPlaybackNetease(Track track, string currentUrl)
    {
      if (IsNeteaseSourceUrl(track.GetOriginalUrl())) return true;
      if (!_standaloneManifestMode || !_isNeteasePlaylist) return false;
      if (FindManifestItemIndexFromUrl(currentUrl) >= 0) return true;

      int selectedVcrid = GetSelectedVcrid();
      return selectedVcrid > 0 && selectedVcrid == _syncedSelectedVcrid;
    }

    private bool IsNeteaseSourceUrl(string sourceUrl)
    {
      if (string.IsNullOrEmpty(sourceUrl)) return false;
      string lower = sourceUrl.ToLower();
      return lower.IndexOf("music.163.com") >= 0 || lower.IndexOf("163cn.tv") >= 0 || lower.IndexOf("netease") >= 0;
    }

    private bool IsNeteasePlaylistCycleTrack(Track track)
    {
      if (!IsUsableTrack(track)) return false;
      string sourceUrl = track.GetOriginalUrl();
      return !string.IsNullOrEmpty(sourceUrl) && sourceUrl.ToLower().IndexOf("pk_cycle=netease_playlist") >= 0;
    }

    private VRCUrl GetPagesRequestUrl()
    {
      VRCUrl url = GetCurrentControllerUrl();
      if (IsUsableRequestUrl(url) && IsPagesRequestUrl(GetUrlString(url))) return url;

      if (Utilities.IsValid(_urlPrefixHelper))
      {
        url = _urlPrefixHelper.GetBottomInputUrl();
        if (IsUsableRequestUrl(url)) return url;

        url = _urlPrefixHelper.GetTopInputUrl();
        if (IsUsableRequestUrl(url)) return url;
      }

      if (IsPagesRequestUrl(GetUrlString(_pagesApiPrefix))) return _pagesApiPrefix;
      return VRCUrl.Empty;
    }

    private bool IsUsableRequestUrl(VRCUrl url)
    {
      string value = GetUrlString(url);
      return !string.IsNullOrEmpty(value) && !IsEmptyPlayerPrefix(value);
    }

    private bool IsEmptyPlayerPrefix(string url)
    {
      if (string.IsNullOrEmpty(url)) return true;
      string lower = url.ToLower();
      return lower.EndsWith("/player/?url=") || lower.EndsWith("/player/?__dm=1&url=") || lower.EndsWith("/api/pages?url=");
    }

    private string GetUrlString(VRCUrl url)
    {
      return VRCUrl.IsNullOrEmpty(url) ? "" : url.Get();
    }

    private void UpdateLabels()
    {
      if (_useUnifiedQueue && !_standaloneManifestMode)
      {
        UpdateUnifiedQueueLabels();
        return;
      }

      if (_standaloneManifestMode)
        SetTitle(string.IsNullOrEmpty(_parsedSourceTitle) ? "播放列表" : _parsedSourceTitle);
      UpdatePageButtonLabels();

      int standaloneItemCount = _useUnifiedQueue && _standaloneManifestMode
        ? GetStandaloneDisplayCount()
        : _totalPages;
      if (standaloneItemCount > 0)
      {
        int start = _pageOffset + 1;
        int end = Mathf.Min(_pageOffset + VisibleButtonCount, standaloneItemCount);
        SetStatus("第 " + start + "-" + end + " 项 / 共 " + standaloneItemCount + " 项");
      }
    }

    private void UpdateUnifiedQueueLabels()
    {
      int itemCount = GetUnifiedDisplayCount();
      int activeManifestIndex = GetActiveBiliManifestIndex();
      ClampUnifiedPageOffset(itemCount);
      bool hasIndependentItems = _biliMixedManifestMode &&
        ((HasCurrentUnifiedTrack() && !IsCurrentBiliManifestDisplayItem()) ||
         (Utilities.IsValid(_controller) && Utilities.IsValid(_controller.Queue) && _controller.Queue.Length > 0));
      string title = _biliMixedManifestMode && !hasIndependentItems
        ? _biliManifestTitle
        : _syncedQueueTitle;
      SetTitle(itemCount <= 0 || string.IsNullOrEmpty(title) ? "播放队列" : title);

      for (int i = 0; i < _pageButtonLabels.Length; i++)
      {
        TextMeshProUGUI label = _pageButtonLabels[i];
        if (!Utilities.IsValid(label)) continue;

        int displayIndex = _pageOffset + i;
        bool visible = displayIndex >= 0 && displayIndex < itemCount;
        label.transform.parent.gameObject.SetActive(visible);
        bool deletable = visible;
        SetDeleteControlVisible(i, deletable);
        int sourceType = visible ? GetUnifiedDisplaySourceType(displayIndex) : 0;
        int sourceIndex = visible ? GetUnifiedDisplaySourceIndex(displayIndex) : -1;
        Track displayTrack = visible && sourceType != DisplaySourceManifest ? GetUnifiedDisplayTrack(displayIndex) : Track.Empty();
        _visibleDeleteUrls[i] = !deletable ? "" : sourceType == DisplaySourceManifest
          ? "bili:" + GetBiliManifestVcrid(sourceIndex)
          : GetTrackVrcUrl(displayTrack);
        if (!visible) continue;

        bool current = sourceType == DisplaySourceManifest
          ? sourceIndex == activeManifestIndex
          : IsCurrentUnifiedTrack(displayTrack);
        label.text = (current ? "> " : "") + (sourceType == DisplaySourceManifest
          ? FormatBiliManifestLabel(sourceIndex)
          : FormatUnifiedTrackLabel(displayIndex, displayTrack));
        SetPageButtonVisual(label, current);
      }

      if (itemCount <= 0)
      {
        SetStatus("暂无播放队列");
        return;
      }

      int start = _pageOffset + 1;
      int end = Mathf.Min(_pageOffset + VisibleButtonCount, itemCount);
      string status = "第 " + start + "-" + end + " 项 / 共 " + itemCount + " 项";
      if (_unifiedQueueLimitReached || itemCount >= MaxUnifiedQueueItems) status += " · 上限 " + MaxUnifiedQueueItems + " 项";
      SetStatus(status);
    }

    private int GetUnifiedDisplayCount()
    {
      EnsureUnifiedDisplayCache();
      return _unifiedDisplaySourceTypes.Length;
    }

    private bool HasCurrentUnifiedTrack()
    {
      return Utilities.IsValid(_controller) && (!_controller.Stopped || _controller.IsLoading) && IsUsableTrack(_controller.Track);
    }

    private bool ShouldIncludeCurrentUnifiedTrack()
    {
      return HasCurrentUnifiedTrack() && !IsCurrentBiliManifestDisplayItem();
    }

    private bool IsCurrentBiliManifestDisplayItem()
    {
      if (!_biliMixedManifestMode || _biliManifestTotalPages <= 1 || !HasCurrentUnifiedTrack()) return false;
      if (_biliManifestPlaybackLocked || _currentPlaybackIsManifestItem || IsActiveBiliManifestTrack(_controller.Track))
        return true;
      return CurrentTrackTitleMatchesSelectedBiliManifestPart();
    }

    private bool CurrentTrackTitleMatchesSelectedBiliManifestPart()
    {
      if (!Utilities.IsValid(_controller) || !HasCurrentUnifiedTrack()) return false;
      Track currentTrack = _controller.Track;
      if (!currentTrack.HasTitle() || _biliManifestSelectedIndex < 0 ||
          _biliManifestSelectedIndex >= _biliManifestTotalPages) return false;
      return SanitizeUiText(currentTrack.GetTitle()) == GetBiliManifestPart(_biliManifestSelectedIndex);
    }

    private bool IsUsableTrack(Track track)
    {
      if (!track.IsValid()) return false;
      VRCUrl url = track.GetVRCUrl();
      return !VRCUrl.IsNullOrEmpty(url) && !string.IsNullOrEmpty(url.Get());
    }

    private Track GetUnifiedDisplayTrack(int displayIndex)
    {
      if (!Utilities.IsValid(_controller) || displayIndex < 0) return Track.Empty();
      EnsureUnifiedDisplayCache();
      if (displayIndex >= _unifiedDisplaySourceTypes.Length) return Track.Empty();
      int sourceType = _unifiedDisplaySourceTypes[displayIndex];
      int sourceIndex = _unifiedDisplaySourceIndexes[displayIndex];
      if (sourceType == DisplaySourceHistory && Utilities.IsValid(_controller.History))
        return _controller.History.GetTrack(sourceIndex);
      if (sourceType == DisplaySourceCurrent) return _controller.Track;
      if (sourceType == DisplaySourceQueue && Utilities.IsValid(_controller.Queue))
        return _controller.Queue.GetTrack(sourceIndex);
      return Track.Empty();
    }

    private string FormatUnifiedTrackLabel(int displayIndex)
    {
      return FormatUnifiedTrackLabel(displayIndex, GetUnifiedDisplayTrack(displayIndex));
    }

    private string FormatUnifiedTrackLabel(int displayIndex, Track track)
    {
      string title = IsUsableTrack(track) && track.HasTitle() ? track.GetTitle() : IsUsableTrack(track) ? "正在读取名称" : "无效项目";
      int number = displayIndex + 1;
      string numberText = number < 10 ? "0" + number : number.ToString();
      return numberText + "  " + Shorten(SanitizeUiText(title), 22);
    }

    private void ClampUnifiedPageOffset()
    {
      ClampUnifiedPageOffset(GetUnifiedDisplayCount());
    }

    private void ClampStandalonePageOffset()
    {
      int itemCount = GetStandaloneDisplayCount();
      if (itemCount <= 0)
      {
        _pageOffset = 0;
        return;
      }
      int maxOffset = ((itemCount - 1) / VisibleButtonCount) * VisibleButtonCount;
      _pageOffset = Mathf.Clamp(_pageOffset, 0, maxOffset);
    }

    private void ClampUnifiedPageOffset(int itemCount)
    {
      if (itemCount <= 0)
      {
        _pageOffset = 0;
        return;
      }

      int maxOffset = ((itemCount - 1) / VisibleButtonCount) * VisibleButtonCount;
      _pageOffset = Mathf.Clamp(_pageOffset, 0, maxOffset);
    }

    private void FocusUnifiedCurrentPage()
    {
      if (_pageViewPinned)
      {
        ClampUnifiedPageOffset();
        return;
      }
      int currentIndex = GetUnifiedCurrentDisplayIndex();
      _pageOffset = currentIndex < 0 ? 0 : (currentIndex / VisibleButtonCount) * VisibleButtonCount;
      ClampUnifiedPageOffset();
    }

    private int GetUnifiedCurrentDisplayIndex()
    {
      int activeManifestIndex = GetActiveBiliManifestIndex();
      if (activeManifestIndex >= 0)
      {
        int manifestCount = GetUnifiedDisplayCount();
        for (int i = 0; i < manifestCount; i++)
        {
          if (GetUnifiedDisplaySourceType(i) == DisplaySourceManifest &&
              GetUnifiedDisplaySourceIndex(i) == activeManifestIndex) return i;
        }
        return -1;
      }
      if (!HasCurrentUnifiedTrack()) return -1;
      string currentUrl = GetTrackVrcUrl(_controller.Track);
      int count = GetUnifiedDisplayCount();
      for (int i = 0; i < count; i++)
      {
        if (GetTrackVrcUrl(GetUnifiedDisplayTrack(i)) == currentUrl) return i;
      }
      return -1;
    }

    private bool IsCurrentUnifiedTrack(Track track)
    {
      return HasCurrentUnifiedTrack() && IsUsableTrack(track) &&
             GetTrackVrcUrl(track) == GetTrackVrcUrl(_controller.Track);
    }

    private bool IsRetainedMultiPageTrack(Track track)
    {
      if (!IsUsableTrack(track)) return false;
      string originalUrl = track.GetOriginalUrl();
      return !string.IsNullOrEmpty(originalUrl) && originalUrl.ToLower().IndexOf("pk_keep=bilibili_pages") >= 0;
    }

    private int GetRetainedHistoryDisplayCount()
    {
      return 0;
    }

    private void EnsureRetainedHistoryCache()
    {
      int historyLength = Utilities.IsValid(_controller) && Utilities.IsValid(_controller.History)
        ? _controller.History.Length
        : 0;
      if (_retainedHistoryCacheLength == historyLength &&
          _retainedHistoryCacheSuppressedUrl == _syncedPendingRetainedDeleteUrl) return;

      Playlist history = Utilities.IsValid(_controller) ? _controller.History : null;
      int capacity = Mathf.Min(historyLength, MaxUnifiedQueueItems);
      int[] indexes = new int[capacity];
      string[] urls = new string[capacity];
      int count = 0;
      if (Utilities.IsValid(history))
      {
        for (int i = 0; i < history.Length && count < capacity; i++)
        {
          Track track = history.GetTrack(i);
          if (!IsRetainedMultiPageTrack(track)) continue;
          string url = GetTrackVrcUrl(track);
          if (string.IsNullOrEmpty(url) || url == _syncedPendingRetainedDeleteUrl) continue;

          bool duplicate = false;
          for (int j = 0; j < count; j++)
          {
            if (urls[j] == url)
            {
              duplicate = true;
              break;
            }
          }
          if (duplicate) continue;
          indexes[count] = i;
          urls[count] = url;
          count++;
        }
      }

      _retainedHistoryIndexes = new int[count];
      _retainedHistoryUrls = new string[count];
      for (int i = 0; i < count; i++)
      {
        _retainedHistoryIndexes[i] = indexes[i];
        _retainedHistoryUrls[i] = urls[i];
      }
      _retainedHistoryCacheLength = historyLength;
      _retainedHistoryCacheSuppressedUrl = _syncedPendingRetainedDeleteUrl;
    }

    private void InvalidateRetainedHistoryCache()
    {
      _retainedHistoryCacheLength = -1;
      _retainedHistoryCacheSuppressedUrl = "";
      InvalidateUnifiedDisplayCache();
    }

    private int GetRetainedHistoryIndex(int displayIndex)
    {
      EnsureRetainedHistoryCache();
      if (displayIndex < 0 || displayIndex >= _retainedHistoryIndexes.Length) return -1;
      return _retainedHistoryIndexes[displayIndex];
    }

    private bool HasVisibleRetainedHistoryUrl(string url)
    {
      if (string.IsNullOrEmpty(url)) return false;
      EnsureRetainedHistoryCache();
      for (int i = 0; i < _retainedHistoryUrls.Length; i++)
      {
        if (_retainedHistoryUrls[i] == url) return true;
      }
      return false;
    }

    private bool ShouldIncludeUnifiedQueueTrack(int queueIndex)
    {
      if (!Utilities.IsValid(_controller) || !Utilities.IsValid(_controller.Queue)) return false;
      Playlist queue = _controller.Queue;
      if (queueIndex < 0 || queueIndex >= queue.Length) return false;
      return !IsActiveBiliManifestQueueTrack(queue.GetTrack(queueIndex));
    }

    private bool IsActiveBiliManifestQueueTrack(Track track)
    {
      if (IsActiveBiliManifestTrack(track)) return true;
      if (!_biliMixedManifestMode || _biliManifestTotalPages <= 1 || !IsUsableTrack(track)) return false;

      if (HasCurrentUnifiedTrack() && IsCurrentBiliManifestDisplayItem() &&
          GetTrackVrcUrl(track) == GetTrackVrcUrl(_controller.Track)) return true;

      if (!track.HasTitle()) return false;
      string trackTitle = SanitizeUiText(track.GetTitle());
      string trackUrl = GetTrackVrcUrl(track);
      if (_biliManifestSelectedIndex >= 0 && _biliManifestSelectedIndex < _biliManifestTotalPages &&
          trackTitle == GetBiliManifestPart(_biliManifestSelectedIndex) &&
          (IsVcridPlaybackUrl(trackUrl) || IsPagesRequestUrl(trackUrl))) return true;
      return trackTitle == SanitizeUiText(_biliManifestTitle) && IsPagesRequestUrl(trackUrl);
    }

    private bool IsActiveBiliManifestTrack(Track track)
    {
      if (!_biliMixedManifestMode || _biliManifestTotalPages <= 1 || !IsUsableTrack(track)) return false;

      if (IsActiveBiliManifestUrl(GetTrackVrcUrl(track))) return true;
      return IsActiveBiliManifestUrl(track.GetOriginalUrl());
    }

    private bool IsActiveBiliManifestUrl(string url)
    {
      if (string.IsNullOrEmpty(url)) return false;

      string sourceUrl = GetUrlString(_biliManifestSourceUrl);
      if (url == sourceUrl) return true;

      int groupMarker = url.IndexOf("pk_group=");
      if (groupMarker >= 0 && ParseIntString(url.Substring(groupMarker + 9), 0) == GetStableSourceId(sourceUrl))
        return true;

      string lower = url.ToLower();
      int marker = lower.IndexOf("vcrid=");
      if (marker < 0) return false;
      int vcrid = ParseIntString(url.Substring(marker + 6), 0);
      if (vcrid <= 0) return false;
      for (int i = 0; i < _biliManifestVcrids.Length; i++)
      {
        if (_biliManifestVcrids[i] == vcrid) return true;
      }
      return false;
    }

    private void EnsureUnifiedDisplayCache()
    {
      if (!_unifiedDisplayCacheDirty) return;
      _unifiedDisplayCacheDirty = false;
      if (!Utilities.IsValid(_controller))
      {
        _unifiedDisplaySourceTypes = new int[0];
        _unifiedDisplaySourceIndexes = new int[0];
        return;
      }

      int[] sourceTypes = new int[MaxUnifiedQueueItems];
      int[] sourceIndexes = new int[MaxUnifiedQueueItems];
      int count = 0;
      int manifestAdded = 0;
      int currentAdded = 0;
      int queueAdded = 0;

      if (_biliMixedManifestMode)
      {
        for (int i = 0; i < _biliManifestTotalPages && count < MaxUnifiedQueueItems; i++)
        {
          if (IsBiliManifestItemDeleted(i)) continue;
          sourceTypes[count] = DisplaySourceManifest;
          sourceIndexes[count] = i;
          count++;
          manifestAdded++;
        }
      }

      if (count < MaxUnifiedQueueItems && ShouldIncludeCurrentUnifiedTrack() &&
          !_biliManifestPlaybackLocked && !_currentPlaybackIsManifestItem)
      {
        sourceTypes[count] = DisplaySourceCurrent;
        sourceIndexes[count] = 0;
        count++;
        currentAdded = 1;
      }

      Playlist queue = _controller.Queue;
      if (Utilities.IsValid(queue))
      {
        for (int i = 0; i < queue.Length && count < MaxUnifiedQueueItems; i++)
        {
          if (!ShouldIncludeUnifiedQueueTrack(i)) continue;
          sourceTypes[count] = DisplaySourceQueue;
          sourceIndexes[count] = i;
          count++;
          queueAdded++;
        }
      }

      _unifiedDisplaySourceTypes = new int[count];
      _unifiedDisplaySourceIndexes = new int[count];
      for (int i = 0; i < count; i++)
      {
        _unifiedDisplaySourceTypes[i] = sourceTypes[i];
        _unifiedDisplaySourceIndexes[i] = sourceIndexes[i];
      }

      if (_biliMixedManifestMode)
      {
        int tailType = count > 0 ? sourceTypes[count - 1] : 0;
        string debugState = "manifest=" + manifestAdded + " current=" + currentAdded +
                            " queue=" + queueAdded + " total=" + count + " tailType=" + tailType +
                            " active=" + GetActiveBiliManifestIndex() +
                            " selected=" + _biliManifestSelectedIndex + " synced=" + _syncedSelectedVcrid;
        if (debugState != _lastUnifiedDisplayDebugState)
        {
          _lastUnifiedDisplayDebugState = debugState;
          Debug.Log("[PaulKoiPages] " + debugState);
        }
      }
      else
      {
        _lastUnifiedDisplayDebugState = "";
      }
    }

    private void InvalidateUnifiedDisplayCache()
    {
      _unifiedDisplayCacheDirty = true;
    }

    private int GetUnifiedDisplayQueueIndex(int displayIndex)
    {
      EnsureUnifiedDisplayCache();
      if (displayIndex < 0 || displayIndex >= _unifiedDisplaySourceTypes.Length) return -1;
      return _unifiedDisplaySourceTypes[displayIndex] == DisplaySourceQueue
        ? _unifiedDisplaySourceIndexes[displayIndex]
        : -1;
    }

    private void FocusUnifiedQueueIndex(int queueIndex)
    {
      if (queueIndex < 0) return;
      int count = GetUnifiedDisplayCount();
      for (int i = 0; i < count; i++)
      {
        if (GetUnifiedDisplayQueueIndex(i) != queueIndex) continue;
        _pageOffset = (i / VisibleButtonCount) * VisibleButtonCount;
        _pageViewPinned = true;
        return;
      }
    }

    private int GetUnifiedDisplaySourceType(int displayIndex)
    {
      EnsureUnifiedDisplayCache();
      if (displayIndex < 0 || displayIndex >= _unifiedDisplaySourceTypes.Length) return 0;
      return _unifiedDisplaySourceTypes[displayIndex];
    }

    private int GetUnifiedDisplaySourceIndex(int displayIndex)
    {
      EnsureUnifiedDisplayCache();
      if (displayIndex < 0 || displayIndex >= _unifiedDisplaySourceIndexes.Length) return -1;
      return _unifiedDisplaySourceIndexes[displayIndex];
    }

    private bool IsUnifiedDisplayDeletable(int displayIndex)
    {
      return displayIndex >= 0 && displayIndex < GetUnifiedDisplayCount();
    }

    private bool IsVisibleDisplayDeletable(int displayIndex)
    {
      if (_standaloneManifestMode)
        return displayIndex >= _totalPages && displayIndex < GetStandaloneDisplayCount();
      return IsUnifiedDisplayDeletable(displayIndex);
    }

    private void SetDeleteControlVisible(int visibleIndex, bool visible)
    {
      if (_deleteButtonIcons == null || visibleIndex < 0 || visibleIndex >= _deleteButtonIcons.Length) return;
      Image icon = _deleteButtonIcons[visibleIndex];
      if (!Utilities.IsValid(icon)) return;
      Transform hotspot = icon.transform.parent;
      if (Utilities.IsValid(hotspot)) hotspot.gameObject.SetActive(visible);
      SetDeleteIconAlpha(visibleIndex, visible ? 1f : 0f);
    }

    private void SetDeleteIconAlpha(int visibleIndex, float alpha)
    {
      if (_deleteButtonIcons == null || visibleIndex < 0 || visibleIndex >= _deleteButtonIcons.Length) return;
      Image icon = _deleteButtonIcons[visibleIndex];
      if (!Utilities.IsValid(icon)) return;
      Color color = icon.color;
      color.a = Mathf.Clamp01(alpha);
      icon.color = color;
    }

    private void ShowDeleteIcon(int visibleIndex)
    {
      int displayIndex = _pageOffset + visibleIndex;
      if (!IsVisibleDisplayDeletable(displayIndex)) return;
      SetDeleteIconAlpha(visibleIndex, 1f);
    }

    private void HideDeleteIcon(int visibleIndex)
    {
      int displayIndex = _pageOffset + visibleIndex;
      SetDeleteIconAlpha(visibleIndex, IsVisibleDisplayDeletable(displayIndex) ? 1f : 0f);
    }

    private void DeleteVisibleQueueItem(int visibleIndex)
    {
      if (!Utilities.IsValid(_controller) || !Utilities.IsValid(_controller.Queue)) return;
      int displayIndex = _pageOffset + visibleIndex;
      if (_standaloneManifestMode)
      {
        int queueIndex = displayIndex - _totalPages;
        if (queueIndex < 0 || queueIndex >= _controller.Queue.Length) return;
        Track queuedTrack = _controller.Queue.GetTrack(queueIndex);
        string standaloneExpectedUrl = visibleIndex >= 0 && visibleIndex < _visibleDeleteUrls.Length
          ? _visibleDeleteUrls[visibleIndex]
          : "";
        if (!IsUsableTrack(queuedTrack) || string.IsNullOrEmpty(standaloneExpectedUrl) ||
            standaloneExpectedUrl != GetTrackVrcUrl(queuedTrack))
        {
          SetStatus("队列已变化，请重试");
          UpdateLabels();
          return;
        }

        _controller.Queue.TakeOwnership();
        _controller.Queue.RemoveTrack(queueIndex);
        ClampStandalonePageOffset();
        UpdateLabels();
        SetStatus("已删除队列项目");
        return;
      }
      if (!IsUnifiedDisplayDeletable(displayIndex)) return;
      if (GetUnifiedDisplaySourceType(displayIndex) == DisplaySourceManifest)
      {
        DeleteBiliManifestItem(GetUnifiedDisplaySourceIndex(displayIndex), visibleIndex);
        return;
      }
      Track track = GetUnifiedDisplayTrack(displayIndex);
      if (!IsUsableTrack(track)) return;
      bool deletingCurrent = IsCurrentUnifiedTrack(track);

      string expectedUrl = visibleIndex >= 0 && visibleIndex < _visibleDeleteUrls.Length ? _visibleDeleteUrls[visibleIndex] : "";
      if (string.IsNullOrEmpty(expectedUrl) || expectedUrl != GetTrackVrcUrl(track))
      {
        HideDeleteIcon(visibleIndex);
        SetStatus("队列已变化，请重试");
        UpdateLabels();
        return;
      }

      bool retainedMultiPage = IsRetainedMultiPageTrack(track) ||
                               (deletingCurrent && expectedUrl == _syncedPendingRetainUrl);

      if (retainedMultiPage)
      {
        if (deletingCurrent && expectedUrl == _syncedPendingRetainUrl) PublishPendingCurrentRetention("", "");
        if (deletingCurrent) PublishPendingRetainedDeleteUrl(expectedUrl);
        RemoveRetainedTrackCopiesFromQueue(expectedUrl);
        RemoveRetainedHistoryTrackCopies(expectedUrl);
      }

      if (deletingCurrent)
      {
        _controller.TakeOwnership();
        EnsureHistoryOwnershipForControllerOwner();
        _controller.Stopped = true;
        SetStatus("已删除当前项目");
        return;
      }

      if (!retainedMultiPage)
      {
        int queueIndex = GetUnifiedDisplayQueueIndex(displayIndex);
        if (queueIndex < 0 || queueIndex >= _controller.Queue.Length) return;
        _controller.Queue.TakeOwnership();
        _controller.Queue.RemoveTrack(queueIndex);
      }
      HideDeleteIcon(visibleIndex);
      ClampUnifiedPageOffset();
      EnsureUnifiedQueueHeaderMatchesTracks();
      SetStatus("已从播放队列删除");
      UpdateLabels();
    }

    private void DeleteBiliManifestItem(int manifestIndex, int visibleIndex)
    {
      if (!_biliMixedManifestMode || manifestIndex < 0 || manifestIndex >= _biliManifestTotalPages) return;
      int vcrid = GetBiliManifestVcrid(manifestIndex);
      string expected = visibleIndex >= 0 && visibleIndex < _visibleDeleteUrls.Length ? _visibleDeleteUrls[visibleIndex] : "";
      if (vcrid <= 0 || expected != "bili:" + vcrid)
      {
        HideDeleteIcon(visibleIndex);
        SetStatus("队列已变化，请重试");
        UpdateLabels();
        return;
      }
      if (IsBiliManifestItemDeleted(manifestIndex)) return;

      TakeOwnership();
      int oldLength = _syncedDeletedManifestVcrids == null ? 0 : _syncedDeletedManifestVcrids.Length;
      int[] deleted = new int[oldLength + 1];
      for (int i = 0; i < oldLength; i++) deleted[i] = _syncedDeletedManifestVcrids[i];
      deleted[oldLength] = vcrid;
      _syncedDeletedManifestVcrids = deleted;
      _syncedRevision++;
      if (_syncedRevision < 0) _syncedRevision = 1;
      RequestSerialization();

      bool deletingCurrent = _currentPlaybackIsManifestItem && manifestIndex == _biliManifestSelectedIndex;
      InvalidateUnifiedDisplayCache();
      HideDeleteIcon(visibleIndex);
      if (deletingCurrent)
      {
        int nextIndex = FindNextVisibleBiliManifestIndex(manifestIndex + 1);
        if (nextIndex >= 0)
        {
          PlayBiliManifestIndex(nextIndex);
          return;
        }

        _naturalEndPending = true;
        _advanceQueueAfterManifestPending = Utilities.IsValid(_controller.Queue) && _controller.Queue.Length > 0;
        RestoreControllerForward();
        _controller.TakeOwnership();
        _controller.Stopped = true;
        return;
      }

      ClampUnifiedPageOffset();
      UpdateLabels();
      SetStatus("已删除该分P项目");
    }

    private void HideAllDeleteIcons()
    {
      for (int i = 0; i < VisibleButtonCount; i++) HideDeleteIcon(i);
    }

    private void EnsureHistoryOwnershipForControllerOwner()
    {
      if (!Utilities.IsValid(_controller) || !Networking.IsOwner(_controller.gameObject)) return;
      Playlist history = _controller.History;
      if (Utilities.IsValid(history)) history.TakeOwnership();
    }

    private void RemoveRetainedTrackCopiesFromQueue(string url)
    {
      if (string.IsNullOrEmpty(url) || !Utilities.IsValid(_controller) || !Utilities.IsValid(_controller.Queue)) return;
      Playlist queue = _controller.Queue;
      queue.TakeOwnership();
      _clearingUnifiedQueue = true;
      for (int i = queue.Length - 1; i >= 0; i--)
      {
        Track queuedTrack = queue.GetTrack(i);
        if (IsRetainedMultiPageTrack(queuedTrack) && GetTrackVrcUrl(queuedTrack) == url) queue.RemoveTrack(i);
      }
      _clearingUnifiedQueue = false;
      InvalidateUnifiedDisplayCache();
    }

    private void RemoveRetainedHistoryTrackCopies(string url)
    {
      if (string.IsNullOrEmpty(url) || !Utilities.IsValid(_controller) || !Utilities.IsValid(_controller.History)) return;
      Playlist history = _controller.History;
      history.TakeOwnership();
      _editingRetainedHistory = true;
      for (int i = history.Length - 1; i >= 0; i--)
      {
        Track historyTrack = history.GetTrack(i);
        if (IsRetainedMultiPageTrack(historyTrack) && GetTrackVrcUrl(historyTrack) == url) history.RemoveTrack(i);
      }
      _editingRetainedHistory = false;
      InvalidateRetainedHistoryCache();
    }

    private void RemoveAllRetainedHistoryTracks()
    {
      if (!Utilities.IsValid(_controller) || !Utilities.IsValid(_controller.History)) return;
      Playlist history = _controller.History;
      history.TakeOwnership();
      _editingRetainedHistory = true;
      for (int i = history.Length - 1; i >= 0; i--)
      {
        if (IsRetainedMultiPageTrack(history.GetTrack(i))) history.RemoveTrack(i);
      }
      _editingRetainedHistory = false;
      InvalidateRetainedHistoryCache();
    }

    private void PublishPendingRetainedDeleteUrl(string url)
    {
      if (_syncedPendingRetainedDeleteUrl == url) return;
      InvalidateRetainedHistoryCache();
      if (!Utilities.IsValid(Networking.LocalPlayer))
      {
        _syncedPendingRetainedDeleteUrl = url;
        return;
      }
      TakeOwnership();
      _syncedPendingRetainedDeleteUrl = url;
      _syncedQueueRevision++;
      if (_syncedQueueRevision < 0) _syncedQueueRevision = 1;
      RequestSerialization();
    }

    private void PublishPendingCurrentRetention(string url, string markerUrl)
    {
      if (_syncedPendingRetainUrl == url && _syncedPendingRetainMarkerUrl == markerUrl) return;
      if (!Utilities.IsValid(Networking.LocalPlayer))
      {
        _syncedPendingRetainUrl = url;
        _syncedPendingRetainMarkerUrl = markerUrl;
        return;
      }
      TakeOwnership();
      _syncedPendingRetainUrl = url;
      _syncedPendingRetainMarkerUrl = markerUrl;
      _syncedQueueRevision++;
      if (_syncedQueueRevision < 0) _syncedQueueRevision = 1;
      RequestSerialization();
    }

    public void ApplyPendingCurrentRetention()
    {
      string url = _syncedPendingRetainUrl;
      string markerUrl = _syncedPendingRetainMarkerUrl;
      if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(markerUrl) || !Utilities.IsValid(_controller)) return;
      if (!Networking.IsOwner(_controller.gameObject) || !Utilities.IsValid(_controller.History)) return;

      Playlist history = _controller.History;
      history.TakeOwnership();
      for (int i = history.Length - 1; i >= 0; i--)
      {
        Track track = history.GetTrack(i);
        if (!IsUsableTrack(track) || GetTrackVrcUrl(track) != url) continue;
        if (!IsRetainedMultiPageTrack(track))
        {
          object[] trackData = (object[])(object)track;
          if (trackData == null || trackData.Length < 4) return;
          _editingRetainedHistory = true;
          trackData[3] = markerUrl;
          history.SendEvent();
          _editingRetainedHistory = false;
        }
        InvalidateRetainedHistoryCache();
        PublishPendingCurrentRetention("", "");
        return;
      }
    }

    public void CleanupPendingRetainedDelete()
    {
      string url = _syncedPendingRetainedDeleteUrl;
      if (string.IsNullOrEmpty(url) || !Utilities.IsValid(_controller)) return;
      if (!Networking.IsOwner(_controller.gameObject)) return;

      bool stillCurrent = HasCurrentUnifiedTrack() && GetTrackVrcUrl(_controller.Track) == url;
      RemoveRetainedHistoryTrackCopies(url);
      if (!stillCurrent) PublishPendingRetainedDeleteUrl("");
    }

    private string GetTrackVrcUrl(Track track)
    {
      if (!IsUsableTrack(track)) return "";
      return GetUrlString(track.GetVRCUrl());
    }

    public void ShowDelete0() { ShowDeleteIcon(0); }
    public void ShowDelete1() { ShowDeleteIcon(1); }
    public void ShowDelete2() { ShowDeleteIcon(2); }
    public void ShowDelete3() { ShowDeleteIcon(3); }
    public void ShowDelete4() { ShowDeleteIcon(4); }
    public void ShowDelete5() { ShowDeleteIcon(5); }
    public void HideDelete0() { HideDeleteIcon(0); }
    public void HideDelete1() { HideDeleteIcon(1); }
    public void HideDelete2() { HideDeleteIcon(2); }
    public void HideDelete3() { HideDeleteIcon(3); }
    public void HideDelete4() { HideDeleteIcon(4); }
    public void HideDelete5() { HideDeleteIcon(5); }
    public void DeleteVisible0() { DeleteVisibleQueueItem(0); }
    public void DeleteVisible1() { DeleteVisibleQueueItem(1); }
    public void DeleteVisible2() { DeleteVisibleQueueItem(2); }
    public void DeleteVisible3() { DeleteVisibleQueueItem(3); }
    public void DeleteVisible4() { DeleteVisibleQueueItem(4); }
    public void DeleteVisible5() { DeleteVisibleQueueItem(5); }

    private void UpdatePageButtonLabels()
    {
      int itemCount = _useUnifiedQueue && _standaloneManifestMode
        ? GetStandaloneDisplayCount()
        : _totalPages;
      for (int i = 0; i < _pageButtonLabels.Length; i++)
      {
        TextMeshProUGUI label = _pageButtonLabels[i];
        if (!Utilities.IsValid(label)) continue;

        int index = _pageOffset + i;
        bool visible = index >= 0 && index < itemCount;
        label.transform.parent.gameObject.SetActive(visible);
        bool queuedItem = visible && index >= _totalPages;
        SetDeleteControlVisible(i, queuedItem);
        _visibleDeleteUrls[i] = "";
        if (!visible) continue;

        if (queuedItem)
        {
          int queueIndex = index - _totalPages;
          Track queueTrack = GetStandaloneQueueTrack(queueIndex);
          _visibleDeleteUrls[i] = GetTrackVrcUrl(queueTrack);
          label.text = FormatStandaloneQueueTrackLabel(index, queueTrack);
          SetPageButtonVisual(label, false);
          continue;
        }

        bool current = _selectedIndex == index;
        label.text = (current ? "> " : "") + FormatPageLabel(index);
        SetPageButtonVisual(label, current);
      }
    }

    private void SetPageButtonVisual(TextMeshProUGUI label, bool current)
    {
      if (!Utilities.IsValid(label)) return;
      label.color = new Color(1f, 1f, 1f, 1f);

      Transform buttonTransform = label.transform.parent;
      if (!Utilities.IsValid(buttonTransform)) return;
      Image background = buttonTransform.GetComponent<Image>();
      if (!Utilities.IsValid(background)) return;
      background.color = current
        ? new Color(1f, 1f, 1f, 0.14f)
        : new Color(1f, 1f, 1f, 0.07f);
    }

    private int GetStandaloneQueueCount()
    {
      if (!Utilities.IsValid(_controller) || !Utilities.IsValid(_controller.Queue)) return 0;
      return _controller.Queue.Length;
    }

    private int GetStandaloneDisplayCount()
    {
      return _totalPages + GetStandaloneQueueCount();
    }

    private Track GetStandaloneQueueTrack(int queueIndex)
    {
      if (!Utilities.IsValid(_controller) || !Utilities.IsValid(_controller.Queue) ||
          queueIndex < 0 || queueIndex >= _controller.Queue.Length) return Track.Empty();
      return _controller.Queue.GetTrack(queueIndex);
    }

    private string FormatStandaloneQueueTrackLabel(int displayIndex, Track track)
    {
      string title = IsUsableTrack(track) && track.HasTitle()
        ? SanitizeUiText(track.GetTitle())
        : IsUsableTrack(track) ? GetTrackVrcUrl(track) : "无效项目";
      int number = displayIndex + 1;
      string numberText = number < 10 ? "0" + number : number.ToString();
      return numberText + "  " + Shorten(title, 22);
    }

    private void UpdateTitleMarqueePosition()
    {
      if (!Utilities.IsValid(_titleRect) || !Utilities.IsValid(_titleViewportRect)) return;
      float speed = GetMarqueeSpeed();
      float pause = GetMarqueePauseSeconds();
      float overflow = Mathf.Max(0f, _titleRect.rect.width - _titleViewportRect.rect.width);
      float offset = 0f;
      if (overflow > 0.5f)
      {
        float travelSeconds = overflow / speed;
        float cycleSeconds = pause + travelSeconds + pause;
        float phase = _marqueeElapsed % cycleSeconds;
        if (phase > pause)
        {
          offset = phase < pause + travelSeconds ? (phase - pause) * speed : overflow;
        }
      }

      Vector2 position = _titleRect.anchoredPosition;
      float targetX = -Mathf.Clamp(offset, 0f, overflow);
      if (Mathf.Abs(position.x - targetX) > 0.01f) _titleRect.anchoredPosition = new Vector2(targetX, position.y);
    }

    private void ResetTitleMarqueePosition()
    {
      _marqueeElapsed = 0f;
      if (!Utilities.IsValid(_titleRect)) return;
      Vector2 position = _titleRect.anchoredPosition;
      _titleRect.anchoredPosition = new Vector2(0f, position.y);
    }

    private void ClearPages(string status)
    {
      _parts = new string[0];
      _playUrls = new string[0];
      _danmakuUrls = new string[0];
      _pageNumbers = new int[0];
      _vcrids = new int[0];
      _totalPages = 0;
      _selectedIndex = -1;
      _pageOffset = 0;
      _pageViewPinned = false;
      _autoAdvancePending = false;
      _pendingNextIndex = -1;
      _internalTrackSwitch = false;
      _pendingStandaloneVcrid = 0;
      _isNeteasePlaylist = false;
      _parsedShouldCycle = false;
      _lastManifestSourceUrl = VRCUrl.Empty;
      _syncedManifestLoadPending = false;
      _needsNeteaseSongMetadata = false;
      _pendingNeteaseMetadataUrl = "";
      _pendingNeteaseMetadataIndex = -1;
      _lastConfiguredDanmakuUrl = "";
      _lastConfiguredDanmakuWasNetease = false;
      if (Utilities.IsValid(_danmakuModule)) _danmakuModule.SetExternalAudioMode(false);
      SetTitle("播放列表");
      UpdateLabels();
      SetStatus(status);
    }

    private string FormatPageLabel(int index)
    {
      if (index < 0 || index >= _totalPages) return "";
      string pageText = _pageNumbers[index] < 10 ? "0" + _pageNumbers[index] : _pageNumbers[index].ToString();
      return pageText + "  " + Shorten(SanitizeUiText(_parts[index]), 22);
    }

    private void SetTitle(string text)
    {
      if (!Utilities.IsValid(_titleLabel)) return;
      string sanitized = SanitizeUiText(text);
      if (_titleLabel.text == sanitized) return;
      _titleLabel.text = sanitized;
      ResetTitleMarqueePosition();
    }

    private string SanitizeUiText(string text)
    {
      if (string.IsNullOrEmpty(text)) return "";
      return text.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
    }

    private void SetStatus(string text)
    {
      if (!Utilities.IsValid(_statusLabel)) return;
      _statusLabel.text = text;
    }

    private void UpdatePlayModeButtonLabel()
    {
      if (!Utilities.IsValid(_playModeButtonLabel)) return;
      _playModeButtonLabel.text = Utilities.IsValid(_controller) && _controller.Loop ? "单项循环" : "顺序播放";
    }

    private string Shorten(string text, int maxChars)
    {
      if (string.IsNullOrEmpty(text) || text.Length <= maxChars) return text;
      if (maxChars <= 1) return text.Substring(0, maxChars);
      return text.Substring(0, maxChars - 1) + ".";
    }

    private int TokenToInt(DataToken token, int fallback)
    {
      if (!token.IsNumber) return fallback;
      return Mathf.RoundToInt((float)token.Number);
    }

    private bool ParsePagesMetadata(string raw)
    {
      if (string.IsNullOrEmpty(raw)) return false;

      int marker = raw.IndexOf("#YBDM/1");
      if (marker < 0) return false;

      bool preserveBiliPage = _useUnifiedQueue && _biliMixedManifestMode && _biliManifestTotalPages > 0;
      int preservedPageOffset = _pageOffset;

      _isNeteasePlaylist = false;
      _parsedShouldCycle = false;
      if (!_useUnifiedQueue && Utilities.IsValid(_danmakuModule)) _danmakuModule.SetExternalAudioMode(false);
      string[] lines = raw.Substring(marker).Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
      int itemCount = 0;
      int selectedPage = 1;
      int fallbackVcrid = 0;
      string title = "";
      string fallbackPart = "";
      _parsedIsBilibiliList = false;

      for (int i = 0; i < lines.Length; i++)
      {
        string line = lines[i];
        if (line.StartsWith("#page_item=") || line.StartsWith("#list_item=")) itemCount++;
        else if (line.StartsWith("#page=")) selectedPage = ParseIntString(line.Substring(6), selectedPage);
        else if (line.StartsWith("#vcrid=")) fallbackVcrid = ParseIntString(line.Substring(8), 0);
        else if (line.StartsWith("#title=")) title = UnescapeField(line.Substring(7));
        else if (line.StartsWith("#part=")) fallbackPart = UnescapeField(line.Substring(6));
        else if (line == "#manifest_type=bilibili-list") _parsedIsBilibiliList = true;
        else if (line == "#manifest_type=netease-playlist")
        {
          _isNeteasePlaylist = true;
          _parsedShouldCycle = true;
        }
        else if (line == "#provider=netease" || line.StartsWith("#manifest_type=netease")) _isNeteasePlaylist = true;
      }

      int count = Mathf.Min(Mathf.Max(itemCount, 1), 4096);
      _parts = new string[count];
      _playUrls = new string[count];
      _danmakuUrls = new string[count];
      _pageNumbers = new int[count];
      _vcrids = new int[count];
      _totalPages = count;
      _selectedIndex = -1;
      if (!preserveBiliPage) _pageOffset = 0;

      string displayTitle = title;
      if (string.IsNullOrEmpty(displayTitle) && count == 1) displayTitle = fallbackPart;
      SetParsedSourceTitle(string.IsNullOrEmpty(displayTitle) ? "播放列表" : displayTitle);

      int cursor = 0;
      for (int i = 0; i < lines.Length && cursor < count; i++)
      {
        string line = lines[i];
        bool pageItem = line.StartsWith("#page_item=");
        bool listItem = line.StartsWith("#list_item=");
        if (!pageItem && !listItem) continue;

        string[] fields = line.Substring(11).Split('\t');
        int page = fields.Length > 0 ? ParseIntString(fields[0], cursor + 1) : cursor + 1;
        string part = "";
        int vcrid = 0;
        if (pageItem)
        {
          part = fields.Length > 3 ? UnescapeField(fields[3]) : "";
          vcrid = fields.Length > 4 ? ParseIntString(fields[4], 0) : 0;
        }
        else
        {
          part = fields.Length > 5 ? UnescapeField(fields[5]) : "";
          vcrid = fields.Length > 6 ? ParseIntString(fields[6], 0) : 0;
        }

        _pageNumbers[cursor] = page;
        bool useVideoTitle = count == 1 && !_parsedIsBilibiliList && !_isNeteasePlaylist && !string.IsNullOrEmpty(displayTitle);
        _parts[cursor] = useVideoTitle ? displayTitle : string.IsNullOrEmpty(part) ? "P" + page : part;
        _playUrls[cursor] = "";
        _danmakuUrls[cursor] = "";
        _vcrids[cursor] = vcrid;
        if (page == selectedPage) _selectedIndex = cursor;
        cursor++;
      }

      if (cursor == 0)
      {
        _pageNumbers[0] = selectedPage;
        bool useVideoTitle = !_parsedIsBilibiliList && !_isNeteasePlaylist && !string.IsNullOrEmpty(displayTitle);
        _parts[0] = useVideoTitle ? displayTitle : string.IsNullOrEmpty(fallbackPart) ? "P" + selectedPage : fallbackPart;
        _playUrls[0] = "";
        _danmakuUrls[0] = "";
        _vcrids[0] = fallbackVcrid;
        _selectedIndex = 0;
      }

      if (_selectedIndex < 0 && count > 0) _selectedIndex = 0;
      _pageOffset = preserveBiliPage
        ? preservedPageOffset
        : Mathf.Max(0, (_selectedIndex / VisibleButtonCount) * VisibleButtonCount);
      SetStatus(count > 1 ? "已加载 " + count + " 项" : "当前内容共 1 项");
      UpdateLabels();
      return true;
    }

    private int ParseIntString(string value, int fallback)
    {
      if (string.IsNullOrEmpty(value)) return fallback;

      int result = 0;
      bool hasDigit = false;
      for (int i = 0; i < value.Length; i++)
      {
        char c = value[i];
        if (c < '0' || c > '9') break;
        result = result * 10 + (c - '0');
        hasDigit = true;
      }
      return hasDigit ? result : fallback;
    }

    private string UnescapeField(string value)
    {
      if (string.IsNullOrEmpty(value)) return "";

      string result = "";
      bool escaping = false;
      for (int i = 0; i < value.Length; i++)
      {
        char c = value[i];
        if (escaping)
        {
          if (c == 't') result += "\t";
          else if (c == 'n') result += "\n";
          else if (c == 'r') result += "\r";
          else result += c;
          escaping = false;
        }
        else if (c == '\\')
        {
          escaping = true;
        }
        else
        {
          result += c;
        }
      }
      if (escaping) result += "\\";
      return result;
    }

    private bool IsPagesRequestUrl(string url)
    {
      if (string.IsNullOrEmpty(url)) return false;

      string prefix = GetUrlString(_pagesApiPrefix);
      if (!string.IsNullOrEmpty(prefix) && url.StartsWith(prefix) && url.Length > prefix.Length) return true;

      string lower = url.ToLower();
      int pageApi = lower.IndexOf("/api/pages");
      int urlParameter = lower.IndexOf("url=");
      if (pageApi >= 0 && urlParameter >= 0 && urlParameter + 4 < url.Length) return true;

      int vcridParameter = lower.IndexOf("/player/?vcrid=");
      return vcridParameter >= 0 && vcridParameter + 16 < url.Length;
    }

    private bool IsDirectPagesApiUrl(string url)
    {
      return !string.IsNullOrEmpty(url) && url.ToLower().IndexOf("/api/pages") >= 0;
    }

    private bool IsVcridPlaybackUrl(string url)
    {
      return !string.IsNullOrEmpty(url) && url.ToLower().IndexOf("/player/?vcrid=") >= 0;
    }

    private bool IsManifestSourceRequestUrl(string url)
    {
      if (string.IsNullOrEmpty(url)) return false;

      string lower = url.ToLower();
      int pagesApi = lower.IndexOf("/api/pages");
      if (pagesApi >= 0) return true;

      int playerUrl = lower.IndexOf("/player/?url=");
      return playerUrl >= 0 && playerUrl + 13 < url.Length;
    }

    private bool SelectManifestItemFromUrl(string url)
    {
      int index = FindManifestItemIndexFromUrl(url);
      if (index < 0) return false;
      _selectedIndex = index;
      if (!_pageViewPinned)
        _pageOffset = Mathf.Max(0, (_selectedIndex / VisibleButtonCount) * VisibleButtonCount);
      return true;
    }

    private int FindManifestItemIndexFromUrl(string url)
    {
      if (string.IsNullOrEmpty(url)) return -1;

      string lower = url.ToLower();
      int marker = lower.IndexOf("vcrid=");
      if (marker < 0) return -1;

      int vcrid = ParseIntString(url.Substring(marker + 6), 0);
      if (vcrid <= 0) return -1;

      for (int i = 0; i < _vcrids.Length; i++)
      {
        if (_vcrids[i] == vcrid) return i;
      }
      return -1;
    }

    private bool SelectManifestItemByVcrid(int vcrid)
    {
      if (vcrid <= 0) return false;

      for (int i = 0; i < _vcrids.Length; i++)
      {
        if (_vcrids[i] != vcrid) continue;
        _selectedIndex = i;
        if (!_pageViewPinned)
          _pageOffset = Mathf.Max(0, (_selectedIndex / VisibleButtonCount) * VisibleButtonCount);
        return true;
      }
      return false;
    }

    private int GetSelectedVcrid()
    {
      if (_selectedIndex < 0 || _selectedIndex >= _vcrids.Length) return 0;
      return _vcrids[_selectedIndex];
    }

    private VRCUrl GetManifestSourceUrl()
    {
      if (!VRCUrl.IsNullOrEmpty(_lastManifestSourceUrl)) return _lastManifestSourceUrl;
      return VRCUrl.IsNullOrEmpty(_syncedManifestUrl) ? VRCUrl.Empty : _syncedManifestUrl;
    }

    private void PublishManifestState(VRCUrl manifestUrl, int selectedVcrid, bool active)
    {
      if (!Utilities.IsValid(Networking.LocalPlayer)) return;

      TakeOwnership();
      if (active)
      {
        _syncedManifestUrl = manifestUrl;
        _syncedSelectedVcrid = selectedVcrid;
        _syncedManifestMode = _standaloneManifestMode
          ? ManifestModeNeteaseExclusive
          : _biliMixedManifestMode ? ManifestModeBilibiliMixed : ManifestModeNone;
      }
      else
      {
        _syncedManifestUrl = VRCUrl.Empty;
        _syncedSelectedVcrid = 0;
        _syncedManifestMode = ManifestModeNone;
      }
      _syncedManifestActive = active;
      _syncedRevision++;
      if (_syncedRevision < 0) _syncedRevision = 1;
      RequestSerialization();
    }

    public override void OnDeserialization()
    {
      if (_useUnifiedQueue && (_syncedManifestActive || _standaloneManifestMode || _biliMixedManifestMode))
      {
        if (_syncedManifestActive)
        {
          ApplySyncedManifestModeFlags();
          ApplySyncedManifestState();
        }
        else
        {
          ApplySyncedManifestModeFlags();
          if (Utilities.IsValid(_danmakuModule)) _danmakuModule.SetExternalAudioMode(false);
          if (_totalPages > 0) ClearPages("播放列表已同步清空");
          InvalidateUnifiedDisplayCache();
          UpdateLabels();
          ConfigureCurrentTrackDanmaku();
        }
        return;
      }

      if (_useUnifiedQueue)
      {
        InvalidateRetainedHistoryCache();
        InvalidateUnifiedDisplayCache();
        ClampUnifiedPageOffset();
        HideAllDeleteIcons();
        UpdateLabels();
        ConfigureCurrentTrackDanmaku();
        SendCustomEventDelayedFrames(nameof(ApplyPendingCurrentRetention), 1);
        SendCustomEventDelayedFrames(nameof(CleanupPendingRetainedDelete), 1);
        return;
      }
      ApplySyncedManifestState();
    }

    public void ApplySyncedManifestState()
    {
      if (!_syncedManifestActive)
      {
        if (_useUnifiedQueue) ApplySyncedManifestModeFlags();
        _syncedManifestLoadPending = false;
        if (_totalPages > 0) ClearPages("播放列表已同步清空");
        return;
      }

      if (_useUnifiedQueue) ApplySyncedManifestModeFlags();

      if (_useUnifiedQueue && _syncedManifestMode == ManifestModeBilibiliMixed)
      {
        bool sameManifest = _biliManifestTotalPages > 0 &&
                            GetUrlString(_biliManifestSourceUrl) == GetUrlString(_syncedManifestUrl);
        if (sameManifest)
        {
          if (!SelectBiliManifestItemFromUrl(GetUrlString(GetCurrentControllerUrl())))
            SelectBiliManifestItemByVcrid(_syncedSelectedVcrid);
          RefreshBiliPlaybackSource(GetUrlString(GetCurrentControllerUrl()));
          InvalidateUnifiedDisplayCache();
          UpdateLabels();
          return;
        }

        ResetBiliManifestState();
        _biliMixedManifestMode = true;
        ResetParsedSource();
        _syncedManifestLoadPending = true;
        BeginPagesRequest(_syncedManifestUrl);
        return;
      }

      if (_totalPages > 0 && ApplyCurrentOrSyncedSelection())
      {
        UpdateLabels();
        LoadSelectedNeteaseDanmaku();
        return;
      }

      if (VRCUrl.IsNullOrEmpty(_syncedManifestUrl)) return;
      string syncedUrl = GetUrlString(_syncedManifestUrl);
      if (_loading && syncedUrl == _lastRequestUrl) return;

      ClearPages("正在同步播放列表");
      _syncedManifestLoadPending = true;
      BeginPagesRequest(_syncedManifestUrl);
    }

    private bool ApplyCurrentOrSyncedSelection()
    {
      VRCUrl currentUrl = GetCurrentControllerUrl();
      if (SelectManifestItemFromUrl(GetUrlString(currentUrl))) return true;
      return SelectManifestItemByVcrid(_syncedSelectedVcrid);
    }

    private void LoadSelectedNeteaseDanmaku()
    {
      if (!_isNeteasePlaylist || !Utilities.IsValid(_danmakuModule)) return;
      if (_selectedIndex < 0 || _selectedIndex >= _vcrids.Length) return;

      int vcrid = _vcrids[_selectedIndex];
      if (vcrid <= 0 || vcrid > _vcridMax) return;
      if (_vcridUrls == null || vcrid >= _vcridUrls.Length) return;

      VRCUrl danmakuUrl = _vcridUrls[vcrid];
      if (VRCUrl.IsNullOrEmpty(danmakuUrl) || string.IsNullOrEmpty(danmakuUrl.Get())) return;
      string requestUrl = GetUrlString(danmakuUrl);
      bool providerChanged = !_lastConfiguredDanmakuWasNetease;
      _danmakuModule.SetExternalAudioMode(true);
      if (requestUrl == _lastConfiguredDanmakuUrl && !providerChanged) return;
      _danmakuModule.LoadDanmakuUrl(danmakuUrl);
      _lastConfiguredDanmakuUrl = requestUrl;
      _lastConfiguredDanmakuWasNetease = true;
    }

    private void RequestSelectedNeteaseSongMetadata()
    {
      if (!_needsNeteaseSongMetadata) return;
      int metadataIndex = _selectedIndex;
      if (_useUnifiedQueue && metadataIndex < 0 && _vcrids.Length == 1) metadataIndex = 0;
      if (metadataIndex < 0 || metadataIndex >= _vcrids.Length) return;

      int vcrid = _vcrids[metadataIndex];
      if (vcrid <= 0 || vcrid > _vcridMax) return;
      if (_vcridUrls == null || vcrid >= _vcridUrls.Length) return;

      VRCUrl metadataUrl = _vcridUrls[vcrid];
      string requestUrl = GetUrlString(metadataUrl);
      if (string.IsNullOrEmpty(requestUrl) || requestUrl == _pendingNeteaseMetadataUrl) return;

      _pendingNeteaseMetadataUrl = requestUrl;
      _pendingNeteaseMetadataIndex = metadataIndex;
      VRCStringDownloader.LoadUrl(metadataUrl, (IUdonEventReceiver)this);
      if (_useUnifiedQueue && _completeUnifiedRequestAfterMetadata)
      {
        SendCustomEventDelayedSeconds(nameof(CheckUnifiedMetadataTimeout), 15f);
      }
    }

    public void CheckUnifiedMetadataTimeout()
    {
      if (!_useUnifiedQueue || !_completeUnifiedRequestAfterMetadata || string.IsNullOrEmpty(_pendingNeteaseMetadataUrl)) return;
      _pendingNeteaseMetadataUrl = "";
      _pendingNeteaseMetadataIndex = -1;
      _completeUnifiedRequestAfterMetadata = false;
      CompleteUnifiedRequest();
    }

    private void ApplyNeteaseSongMetadata(string raw, int index)
    {
      if (index < 0 || index >= _parts.Length || string.IsNullOrEmpty(raw)) return;

      int marker = raw.IndexOf("#YBDM/1");
      if (marker < 0) return;

      string title = "";
      string artist = "";
      string[] lines = raw.Substring(marker).Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
      for (int i = 0; i < lines.Length; i++)
      {
        string line = lines[i];
        if (line.StartsWith("#title=")) title = UnescapeField(line.Substring(7));
        else if (line.StartsWith("#artist=")) artist = UnescapeField(line.Substring(8));
      }

      if (string.IsNullOrEmpty(title))
      {
        SetStatus("歌曲信息中没有歌名");
        return;
      }

      _needsNeteaseSongMetadata = false;
      SetParsedSourceTitle(title);
      _parts[index] = string.IsNullOrEmpty(artist) ? title : title + " - " + artist;
      UpdateLabels();
    }

    private Controller FindControllerInParents()
    {
      Transform current = transform;
      while (current != null)
      {
        Controller controller = current.GetComponent<Controller>();
        if (Utilities.IsValid(controller)) return controller;
        current = current.parent;
      }
      return null;
    }
  }
}
