using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.StringLoading;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;
using Yamadev.YamaStream;

namespace YamaBiliDanmakuV3
{
  [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
  public class YamaBiliDanmakuModule3 : UdonSharpBehaviour
  {
    [Header("YamaPlayer")]
    [SerializeField] private Controller _controller;

    [Header("Loading")]
    [SerializeField] private bool _loadFromCurrentYamaPlayerUrl = true;
    [SerializeField] private VRCUrl _fallbackDanmakuUrl = VRCUrl.Empty;
    [SerializeField] private YamaBiliPagesPlaylist3 _pagesPlaylist;
    [SerializeField] private TextMeshProUGUI _statusText;
    [SerializeField] private TextMeshProUGUI _displayAreaButtonLabel;
    [SerializeField] private TextMeshProUGUI _danmakuToggleButtonLabel;
    [SerializeField] private TextMeshProUGUI _urlPrefixToggleButtonLabel;
    [SerializeField, Range(0f, 10f)] private float _statusVisibleSeconds = 2f;

    [Header("URL Fill")]
    [SerializeField] private YamaBiliUrlPrefixHelper3 _urlPrefixHelper;
    [SerializeField] private bool _urlPrefixFillEnabled = true;

    [Header("Renderer")]
    [SerializeField] private RectTransform _laneRoot;
    [SerializeField] private TextMeshProUGUI[] _textPool = new TextMeshProUGUI[0];
    [SerializeField, Range(1, 32)] private int _laneCount = 12;
    [SerializeField, Range(12f, 80f)] private float _lineHeight = 34f;
    [SerializeField, Range(4f, 16f)] private float _scrollDuration = 8f;
    [SerializeField, Range(1f, 8f)] private float _staticDuration = 4f;
    [SerializeField] private bool _dropDanmakuWhenLanesAreFull = true;
    [SerializeField, Range(0f, 0.5f)] private float _laneSpawnSlackSeconds = 0.05f;
    [SerializeField, Range(-5000, 5000)] private int _timeOffsetMs = 0;

    [Header("Display Area")]
    [Tooltip("0 = full screen, 1 = upper half, 2 = upper quarter anti-blocking area.")]
    [SerializeField, Range(0, 2)] private int _displayAreaMode = 0;

    [Header("Appearance")]
    [SerializeField, Range(0.1f, 3f)] private float _fontScale = 1.1f;
    [Tooltip("Danmaku text opacity. 1 is fully opaque, 0 is invisible.")]
    [SerializeField, Range(0f, 1f)] private float _textAlpha = 0.72f;
    [SerializeField] private bool _danmakuEnabled = true;

    [Header("Editor Visual Style")]
    [Tooltip("Editor-applied style only. Run Yamadev > YamaPlayer > Apply Selected Bili Danmaku Visual Style after changing this on an existing module.")]
    [SerializeField] private bool _editorBoldText = true;
    [SerializeField] private bool _editorHeavyOutlineEnabled = true;
    [SerializeField, Range(0f, 0.5f)] private float _editorOutlineWidth = 0.11f;
    [SerializeField, Range(0f, 1f)] private float _editorOutlineAlpha = 0.7f;

    private int[] _timeMs = new int[0];
    private int[] _mode = new int[0];
    private int[] _color = new int[0];
    private int[] _fontSize = new int[0];
    private string[] _content = new string[0];
    private int _lineCount;
    private int _nextLine;
    private bool _loaded;
    private bool _requestedForPlayback;
    private bool _externalAudioMode;
    private int _lastVideoMs = -1;
    private float _statusHideAt;
    private bool _lastLoadWasCurrentUrl;
    private bool _triedFallbackAfterCurrentUrl;

    // Keep one SDK download in flight. A cancelled request must drain before a
    // new request (including the same URL) can be mistaken for its response.
    private int _requestEpoch;
    private int _downloadEpoch;
    private bool _downloadInFlight;
    private string _downloadUrl = "";
    private VRCUrl _pendingDownloadUrl = VRCUrl.Empty;
    private string _requestPlaybackUrl = "";
    private bool _requestAudioMode;
    private VRCUrl _requestDanmakuUrl = VRCUrl.Empty;
    private int _pendingDownloadEpoch;
    private float _nextDownloadAt;
    private bool _metadataFollowed;
    private bool _requestRequiresV2;
    private int _retryCount;
    private bool _retryPending;
    private int _retryEpoch;
    private string _retryUrl = "";
    private float _retryDueAt;

    // Scan twice: validate the entire response/count real rows, then decode.
    // No partial timeline becomes visible, and neither pass splits the payload.
    private const int ParseCharactersPerFrame = 8192;
    private const int ParseLinesPerFrame = 128;
    private string _parseText = "";
    private string _parseUrl = "";
    private int _parseEpoch;
    private int _parsePhase;
    private int _parseLastFrame = -1;
    private int _parsePosition;
    private int _parseStart;
    private int _parseEnd;
    private int _parseLineStart;
    private int[] _parseTabs = new int[5];
    private int _parseTabCount;
    private int _parseRows;
    private int _parseRenderableRows;
    private bool _parseHtml;
    private bool _parseHtmlClosed;
    private bool _parseComplete;
    private int _parseCompleteFlag = -1;
    private bool _parseMetadata;
    private bool _parseIncomplete;
    private bool _parseHasError;
    private bool _parsePermanent;
    private int _parseExpectedCount = -1;
    private int _parseVcrid;
    private string _parseProvider = "";
    private int _parseRetryAfter = -1;
    private bool _parseDecoding;
    private int _parseContentPosition;
    private int _parseContentEnd;
    private char[] _parseContentBuffer = new char[0];
    private int _parseContentLength;
    private bool _parseEscapePending;

    private bool[] _poolActive = new bool[0];
    private RectTransform[] _poolRects = new RectTransform[0];
    private float[] _activeStartTime = new float[0];
    private float[] _activeDuration = new float[0];
    private float[] _activeStartX = new float[0];
    private float[] _activeEndX = new float[0];
    private float[] _activeY = new float[0];
    private int[] _activeMode = new int[0];
    private int[] _activePoolIndexes = new int[0];
    private int _activePoolCount;
    private float[] _laneReadyAt = new float[0];
    private bool _wasPaused;
    private float _pauseStartedAt;

    private void Start()
    {
      if (!Utilities.IsValid(_controller))
      {
        _controller = FindControllerInParents();
      }

      InitializePool();
      UpdateDisplayAreaButtonLabel();
      UpdateDanmakuToggleButtonLabel();
      UpdateUrlPrefixToggleButtonLabel();
      SetStatus("idle");
    }

    private void Update()
    {
      UpdateStatusVisibility();
      if (_requestedForPlayback && !IsDanmakuRequestCurrent())
        ResetDanmaku();

      RetryDanmakuDownload();
      StartPendingDanmakuDownload();
      TickDanmakuParser();
      if (!Utilities.IsValid(_controller)) return;

      if (_controller.Stopped)
      {
        if (!_controller.IsLoading && (_loaded || _lineCount > 0 || _requestedForPlayback)) ResetDanmaku();
        return;
      }

      if (!_requestedForPlayback && !_externalAudioMode)
      {
        _requestedForPlayback = true;
        _requestPlaybackUrl = GetUrlString(GetCurrentYamaPlayerUrl());
        _requestAudioMode = _externalAudioMode;
        LoadCurrentTrackDanmaku();
      }

      if (_controller.Paused)
      {
        if (!_wasPaused)
        {
          _wasPaused = true;
          _pauseStartedAt = Time.time;
        }
        return;
      }

      if (_wasPaused)
      {
        ResumeActiveTimers(Time.time - _pauseStartedAt);
        _wasPaused = false;
      }

      UpdateActiveTexts();

      if (!_loaded || !Utilities.IsValid(_controller) || _controller.Stopped || _controller.IsLive) return;

      int videoMs = Mathf.RoundToInt(_controller.VideoTime * 1000f) + _timeOffsetMs;
      if (_lastVideoMs >= 0 && Mathf.Abs(videoMs - _lastVideoMs) > 1800)
      {
        SeekLineIndex(videoMs);
        HideAllTexts();
      }
      _lastVideoMs = videoMs;

      while (_nextLine < _lineCount && _timeMs[_nextLine] <= videoMs + 120)
      {
        if (_timeMs[_nextLine] >= videoMs - 500)
        {
          ShowLine(_nextLine);
        }
        _nextLine++;
      }
    }

    public void LoadCurrentTrackDanmaku()
    {
      BeginDanmakuRequest();
      if (!_loadFromCurrentYamaPlayerUrl)
      {
        QueueFallbackDanmaku();
        return;
      }

      VRCUrl currentUrl = GetCurrentYamaPlayerUrl();
      if (VRCUrl.IsNullOrEmpty(currentUrl))
      {
        SetStatus("no current url");
        QueueFallbackDanmaku();
        return;
      }

      _lastLoadWasCurrentUrl = true;
      _triedFallbackAfterCurrentUrl = false;
      SetStatus("loading current url");
      BeginDanmakuDownload(currentUrl);
    }

    public void LoadFallbackDanmaku()
    {
      BeginDanmakuRequest();
      QueueFallbackDanmaku();
    }

    private void QueueFallbackDanmaku()
    {
      if (VRCUrl.IsNullOrEmpty(_fallbackDanmakuUrl))
      {
        SetStatus("no danmaku url");
        return;
      }

      _lastLoadWasCurrentUrl = false;
      SetStatus("loading fallback");
      BeginDanmakuDownload(_fallbackDanmakuUrl);
    }

    public void LoadDanmakuUrl(VRCUrl danmakuUrl)
    {
      if (VRCUrl.IsNullOrEmpty(danmakuUrl) || string.IsNullOrEmpty(danmakuUrl.Get()))
      {
        SetStatus("no selected danmaku url");
        return;
      }

      BeginDanmakuRequest();
      _lastLoadWasCurrentUrl = false;
      _triedFallbackAfterCurrentUrl = false;
      _requestedForPlayback = true;
      SetStatus("loading selected p");
      BeginDanmakuDownload(danmakuUrl);
    }

    public void ClearDanmaku()
    {
      ResetDanmaku();
    }

    public void SetExternalAudioMode(bool enabled)
    {
      if (_externalAudioMode == enabled) return;
      _externalAudioMode = enabled;
      ResetDanmaku();
      if (_externalAudioMode) SetStatus("audio playlist");
    }

    public void HideDanmaku()
    {
      SetDanmakuEnabled(false);
    }

    public void ToggleDanmaku()
    {
      SetDanmakuEnabled(!_danmakuEnabled);
    }

    public void EnableDanmaku()
    {
      SetDanmakuEnabled(true);
    }

    public void DisableDanmaku()
    {
      SetDanmakuEnabled(false);
    }

    public void ToggleUrlPrefixBackfill()
    {
      SetUrlPrefixFillEnabled(!_urlPrefixFillEnabled);
    }

    public void EnableUrlPrefixBackfill()
    {
      SetUrlPrefixFillEnabled(true);
    }

    public void DisableUrlPrefixBackfill()
    {
      SetUrlPrefixFillEnabled(false);
    }

    public void SetDanmakuEnabled(bool enabled)
    {
      if (_danmakuEnabled == enabled) return;

      _danmakuEnabled = enabled;
      ApplyAlphaToActiveTexts();
      SetStatus(_danmakuEnabled ? "danmaku on" : "danmaku off");
      UpdateDanmakuToggleButtonLabel();
    }

    public void SetUrlPrefixFillEnabled(bool enabled)
    {
      if (_urlPrefixFillEnabled == enabled) return;

      _urlPrefixFillEnabled = enabled;
      if (Utilities.IsValid(_urlPrefixHelper))
      {
        _urlPrefixHelper.SetEnableUrlPrefixOnInput(_urlPrefixFillEnabled);
      }

      SetStatus(_urlPrefixFillEnabled ? "url fill on" : "url fill off");
      UpdateUrlPrefixToggleButtonLabel();
    }

    public void SetDanmakuAlpha(float alpha)
    {
      _textAlpha = Mathf.Clamp01(alpha);
      ApplyAlphaToActiveTexts();
    }

    public void SetFullScreenDanmaku()
    {
      SetDisplayAreaMode(0);
    }

    public void SetHalfScreenDanmaku()
    {
      SetDisplayAreaMode(1);
    }

    public void SetQuarterScreenDanmaku()
    {
      SetDisplayAreaMode(2);
    }

    public void CycleDisplayAreaMode()
    {
      SetDisplayAreaMode((_displayAreaMode + 1) % 3);
    }

    public override void OnStringLoadSuccess(IVRCStringDownload result)
    {
      if (!AcceptDanmakuDownload(result.Url)) return;
      ParseDanmaku(result.Result);
    }

    public override void OnStringLoadError(IVRCStringDownload result)
    {
      if (!AcceptDanmakuDownload(result.Url)) return;
      if (TryLoadFallbackAfterCurrentUrl("load error " + result.ErrorCode)) return;
      // Result is not guaranteed on SDK errors. Backend status envelopes arrive
      // through HTTP 200; transport failures use the documented ErrorCode only.
      ScheduleDanmakuRetry("load error " + result.ErrorCode,
        result.ErrorCode != 403 && result.ErrorCode != 404, -1);
    }

    private string GetUrlString(VRCUrl url)
    {
      return VRCUrl.IsNullOrEmpty(url) ? "" : url.Get();
    }

    private void BeginDanmakuRequest()
    {
      ResetDanmaku();
      _requestPlaybackUrl = GetUrlString(GetCurrentYamaPlayerUrl());
      _requestAudioMode = _externalAudioMode;
      _requestedForPlayback = true;
    }

    private void BeginDanmakuDownload(VRCUrl url)
    {
      if (IsMetadataV2Url(url)) _requestRequiresV2 = true;
      _requestDanmakuUrl = url;
      _pendingDownloadUrl = url;
      _pendingDownloadEpoch = _requestEpoch;
      StartPendingDanmakuDownload();
    }

    private bool IsMetadataV2Url(VRCUrl url)
    {
      string value = GetUrlString(url);
      int marker = value.IndexOf("?__pkdm=2");
      if (marker < 0) marker = value.IndexOf("&__pkdm=2");
      if (marker < 0) return false;
      int end = marker + 9;
      return end == value.Length || value[end] == '&' || value[end] == '#';
    }

    private bool IsDanmakuRequestCurrent()
    {
      if (!_requestedForPlayback || _requestAudioMode != _externalAudioMode) return false;
      if (!Utilities.IsValid(_controller)) return true;
      return (!_controller.Stopped || _controller.IsLoading) &&
             _requestPlaybackUrl == GetUrlString(GetCurrentYamaPlayerUrl());
    }

    private void StartPendingDanmakuDownload()
    {
      if (_downloadInFlight || VRCUrl.IsNullOrEmpty(_pendingDownloadUrl)) return;
      if (_pendingDownloadEpoch != _requestEpoch) return;
      if (!IsDanmakuRequestCurrent())
      {
        ResetDanmaku();
        return;
      }
      if (Time.time < _nextDownloadAt) return;
      VRCUrl url = _pendingDownloadUrl;
      _pendingDownloadUrl = VRCUrl.Empty;
      _downloadUrl = GetUrlString(url);
      _downloadEpoch = _requestEpoch;
      _downloadInFlight = true;
      _nextDownloadAt = Time.time + 5f;
      VRCStringDownloader.LoadUrl(url, (IUdonEventReceiver)this);
    }

    private bool AcceptDanmakuDownload(VRCUrl url)
    {
      if (!_downloadInFlight || GetUrlString(url) != _downloadUrl) return false;
      _downloadInFlight = false;
      _downloadUrl = "";
      if (_downloadEpoch != _requestEpoch)
      {
        StartPendingDanmakuDownload();
        return false;
      }
      if (!IsDanmakuRequestCurrent())
      {
        ResetDanmaku();
        return false;
      }
      return true;
    }

    private bool TryLoadFallbackAfterCurrentUrl(string reason)
    {
      if (!_lastLoadWasCurrentUrl || _triedFallbackAfterCurrentUrl || VRCUrl.IsNullOrEmpty(_fallbackDanmakuUrl)) return false;

      _triedFallbackAfterCurrentUrl = true;
      if (_requestRequiresV2 && !IsMetadataV2Url(_fallbackDanmakuUrl))
      {
        SetStatus("danmaku fallback protocol mismatch");
        return true;
      }
      SetStatus(reason + ", fallback");
      QueueFallbackDanmaku();
      return true;
    }

    private void ScheduleDanmakuRetry(string reason, bool retryable, int retryAfter)
    {
      CancelDanmakuParse();
      if (!IsDanmakuRequestCurrent()) return;
      if (!retryable || _retryCount >= 3)
      {
        SetStatus(reason + (retryable ? ", retry limit" : ", permanent"));
        return;
      }
      _retryCount++;
      float backoff = 5f * Mathf.Pow(2f, _retryCount - 1);
      float wait = Mathf.Max(backoff, retryAfter >= 0 ? retryAfter : 180f);
      _retryPending = true;
      _retryEpoch = _requestEpoch;
      _retryUrl = GetUrlString(_requestDanmakuUrl);
      _retryDueAt = Time.time + wait;
      SetStatus(reason + ", retry " + _retryCount + " in " + wait + "s");
      SendCustomEventDelayedSeconds(nameof(RetryDanmakuDownload), wait);
    }

    public void RetryDanmakuDownload()
    {
      // Delayed events are wakeups only: even an old A->B->A event must obey
      // the current epoch, URL and deadline, and can consume a retry only once.
      if (!_retryPending || _retryEpoch != _requestEpoch ||
          _retryUrl != GetUrlString(_requestDanmakuUrl)) return;
      if (!IsDanmakuRequestCurrent())
      {
        ResetDanmaku();
        return;
      }
      if (Time.time < _retryDueAt) return;
      _retryPending = false;
      BeginDanmakuDownload(_requestDanmakuUrl);
    }

    private void InitializePool()
    {
      int count = Utilities.IsValid(_textPool) ? _textPool.Length : 0;
      _poolActive = new bool[count];
      _poolRects = new RectTransform[count];
      _activeStartTime = new float[count];
      _activeDuration = new float[count];
      _activeStartX = new float[count];
      _activeEndX = new float[count];
      _activeY = new float[count];
      _activeMode = new int[count];
      _activePoolIndexes = new int[count];
      _activePoolCount = 0;
      _laneReadyAt = new float[Mathf.Max(1, _laneCount)];

      for (int i = 0; i < count; i++)
      {
        TextMeshProUGUI item = _textPool[i];
        if (!Utilities.IsValid(item)) continue;
        _poolRects[i] = item.GetComponent<RectTransform>();
        item.gameObject.SetActive(false);
      }
    }

    private void ResetDanmaku()
    {
      _requestEpoch++;
      _pendingDownloadUrl = VRCUrl.Empty;
      _requestDanmakuUrl = VRCUrl.Empty;
      _retryPending = false;
      _retryCount = 0;
      _metadataFollowed = false;
      _requestRequiresV2 = false;
      CancelDanmakuParse();
      _timeMs = new int[0];
      _mode = new int[0];
      _color = new int[0];
      _fontSize = new int[0];
      _content = new string[0];
      _requestPlaybackUrl = "";
      _loaded = false;
      _lineCount = 0;
      _nextLine = 0;
      _requestedForPlayback = false;
      _lastVideoMs = -1;
      _lastLoadWasCurrentUrl = false;
      _triedFallbackAfterCurrentUrl = false;
      _wasPaused = false;
      _pauseStartedAt = 0f;
      HideAllTexts();
      ClearLaneTimers();
    }

    private VRCUrl GetCurrentYamaPlayerUrl()
    {
      if (!Utilities.IsValid(_controller)) return VRCUrl.Empty;

      // In 1.5.18 Track is updated before the synchronized _url is serialized.
      Track track = _controller.Track;
      if (track.IsValid())
      {
        VRCUrl trackUrl = track.GetVRCUrl();
        if (!VRCUrl.IsNullOrEmpty(trackUrl)) return trackUrl;
      }

      object currentUrl = _controller.GetProgramVariable("_url");
      if (!Utilities.IsValid(currentUrl)) return VRCUrl.Empty;

      VRCUrl url = (VRCUrl)currentUrl;
      if (!VRCUrl.IsNullOrEmpty(url)) return url;

      return VRCUrl.Empty;
    }

    private void ParseDanmaku(string text)
    {
      CancelDanmakuParse();
      _loaded = false;
      _lineCount = 0;
      _nextLine = 0;
      HideAllTexts();
      _parseText = text == null ? "" : text;
      _parseUrl = GetUrlString(_requestDanmakuUrl);
      _parseEpoch = _requestEpoch;
      _parsePhase = 1;
      _parsePosition = 0;
      _parseEnd = _parseText.Length;
      _parseRows = 0;
      _parseRenderableRows = 0;
      _parseHtml = false;
      _parseHtmlClosed = false;
      _parseComplete = false;
      _parseCompleteFlag = -1;
      _parseMetadata = false;
      _parseIncomplete = false;
      _parseHasError = false;
      _parsePermanent = false;
      _parseExpectedCount = -1;
      _parseVcrid = 0;
      _parseProvider = "";
      _parseRetryAfter = -1;
      SetStatus("reading danmaku");
    }

    private void CancelDanmakuParse()
    {
      _parsePhase = 0;
      _parseText = "";
      _parseDecoding = false;
      _parseContentBuffer = new char[0];
    }

    private void TickDanmakuParser()
    {
      if (_parsePhase == 0 || _parseLastFrame == Time.frameCount) return;
      _parseLastFrame = Time.frameCount;
      if (_parseEpoch != _requestEpoch || _parseUrl != GetUrlString(_requestDanmakuUrl) ||
          !IsDanmakuRequestCurrent())
      {
        ResetDanmaku();
        return;
      }
      if (_parsePhase == 1)
      {
        FindDanmakuBlock();
        return;
      }

      int characters = 0;
      int lines = 0;
      while (_parsePhase >= 2 && characters < ParseCharactersPerFrame && lines < ParseLinesPerFrame)
      {
        if (_parseDecoding)
        {
          if (_parseContentPosition < _parseContentEnd)
          {
            int before = _parseContentPosition;
            char value = ReadDanmakuContentChar();
            characters += _parseContentPosition - before;
            if (_parseEscapePending)
            {
              if (value == 'n') value = '\n';
              else if (value == 'r') value = '\r';
              else if (value == 't') value = '\t';
              else if (value != '\\') _parseContentBuffer[_parseContentLength++] = '\\';
              _parseContentBuffer[_parseContentLength++] = value;
              _parseEscapePending = false;
            }
            else if (value == '\\') _parseEscapePending = true;
            else _parseContentBuffer[_parseContentLength++] = value;
            continue;
          }
          if (_parseEscapePending) _parseContentBuffer[_parseContentLength++] = '\\';
          _content[_lineCount++] = new string(_parseContentBuffer, 0, _parseContentLength);
          _parseContentBuffer = new char[0];
          _parseDecoding = false;
          continue;
        }

        if (_parsePosition >= _parseEnd)
        {
          if (_parseLineStart < _parseEnd)
          {
            ReadDanmakuLine(_parseEnd);
            _parseLineStart = _parseEnd;
            _parseTabCount = 0;
            lines++;
            continue;
          }
          FinishDanmakuPass();
          return;
        }

        char c = _parseText[_parsePosition];
        if (_parseHtml && c == '<' && MatchesDanmakuText(_parsePosition, "</script>", _parseEnd))
        {
          _parseHtmlClosed = true;
          _parseEnd = _parsePosition;
          continue;
        }
        characters++;
        if (c == '\r' || c == '\n')
        {
          ReadDanmakuLine(_parsePosition);
          _parseLineStart = ++_parsePosition;
          _parseTabCount = 0;
          lines++;
        }
        else
        {
          if (c == '\t' && _parseTabCount < 5) _parseTabs[_parseTabCount++] = _parsePosition;
          _parsePosition++;
        }
      }
    }

    private void FindDanmakuBlock()
    {
      string open = "<script type=\"application/yama-bili-danmaku\">";
      int remaining = _parseEnd - _parsePosition;
      int length = Mathf.Min(remaining, ParseCharactersPerFrame + open.Length);
      int marker = _parseText.IndexOf("#YBDM/1", _parsePosition, length);
      int script = _parseText.IndexOf(open, _parsePosition, length);
      if (script >= 0 && (marker < 0 || script < marker))
      {
        _parseHtml = true;
        _parseStart = script + open.Length;
      }
      else if (marker >= 0) _parseStart = marker;
      else
      {
        if (remaining > ParseCharactersPerFrame)
        {
          _parsePosition += ParseCharactersPerFrame;
          return;
        }
        CancelDanmakuParse();
        if (!TryLoadFallbackAfterCurrentUrl("no #YBDM block")) SetStatus("no #YBDM block");
        return;
      }
      _parsePhase = 2;
      _parsePosition = _parseStart;
      _parseLineStart = _parseStart;
      _parseTabCount = 0;
    }

    private bool MatchesDanmakuText(int start, string value, int end)
    {
      if (end - start < value.Length) return false;
      for (int i = 0; i < value.Length; i++)
        if (_parseText[start + i] != value[i]) return false;
      return true;
    }

    private int ReadDanmakuInteger(int start, int end, int fallback)
    {
      // Integers cannot legitimately require an unbounded substring.
      if (end <= start || end - start > 12) return fallback;
      return ParseInt(_parseText.Substring(start, end - start), fallback);
    }

    private void ReadDanmakuLine(int end)
    {
      if (end <= _parseLineStart) return;
      if (_parseText[_parseLineStart] == '#')
      {
        if (_parsePhase == 2) ReadDanmakuMetadata(end);
        return;
      }
      if (_parseTabCount < 5) return;
      int mode = ReadDanmakuInteger(_parseTabs[0] + 1, _parseTabs[1], 1);
      int pool = ReadDanmakuInteger(_parseTabs[3] + 1, _parseTabs[4], 0);
      bool renderable = pool != 2 && mode != 7 && mode != 8 && mode != 9;
      if (_parsePhase == 2)
      {
        _parseRows++;
        if (renderable) _parseRenderableRows++;
        return;
      }
      if (!renderable) return;
      int timeMs = ReadDanmakuInteger(_parseLineStart, _parseTabs[0], 0);
      // Backend timelines are sorted; reject a broken timeline instead of making
      // binary seek silently skip rows. Equal timestamps retain their row order.
      if (_lineCount > 0 && timeMs < _timeMs[_lineCount - 1])
      {
        _lineCount = 0;
        ScheduleDanmakuRetry("unsorted danmaku", true, _parseRetryAfter);
        return;
      }
      _timeMs[_lineCount] = timeMs;
      _mode[_lineCount] = mode;
      _color[_lineCount] = ReadDanmakuInteger(_parseTabs[1] + 1, _parseTabs[2], 16777215);
      _fontSize[_lineCount] = ReadDanmakuInteger(_parseTabs[2] + 1, _parseTabs[3], 25);
      _parseContentPosition = _parseTabs[4] + 1;
      _parseContentEnd = end;
      _parseContentBuffer = new char[end - _parseContentPosition];
      _parseContentLength = 0;
      _parseEscapePending = false;
      _parseDecoding = true;
    }

    private void ReadDanmakuMetadata(int end)
    {
      // Only short protocol fields are inspected; long titles/headers are scanned
      // with the same character budget, never copied or split in their entirety.
      string line = _parseText.Substring(_parseLineStart, Mathf.Min(256, end - _parseLineStart));
      if (line.StartsWith("#danmaku_status="))
      {
        string status = line.Substring(16).Trim();
        if (status == "complete") _parseComplete = true;
        else if (status == "metadata") _parseMetadata = true;
        else if (status == "error") _parseHasError = true;
        else _parseIncomplete = true; // pending, partial, or an unknown state
      }
      else if (line.StartsWith("#error=") && line.Length > 7) _parseHasError = true;
      else if (line.StartsWith("#danmaku_complete="))
      {
        int complete = ParseInt(line.Substring(18).Trim(), -2);
        if ((complete != 0 && complete != 1) || (_parseCompleteFlag >= 0 && complete != _parseCompleteFlag))
          _parseIncomplete = true;
        _parseCompleteFlag = complete;
      }
      else if (line.StartsWith("#retryable="))
      {
        if (line.Substring(11).Trim() == "0") _parsePermanent = true;
      }
      else if (line.StartsWith("#retry_after_seconds="))
        _parseRetryAfter = Mathf.Max(_parseRetryAfter, ParseInt(line.Substring(21).Trim(), -1));
      else if (line.StartsWith("#count="))
      {
        int count = ParseInt(line.Substring(7).Trim(), -2);
        if (count < 0 || (_parseExpectedCount >= 0 && count != _parseExpectedCount)) _parseIncomplete = true;
        _parseExpectedCount = count;
      }
      else if (line.StartsWith("#vcrid=")) _parseVcrid = ParseInt(line.Substring(7).Trim(), 0);
      else if (line.StartsWith("#provider=")) _parseProvider = line.Substring(10).Trim();
      else if (line.StartsWith("#error_code=") || line.StartsWith("#http_status="))
      {
        int code = ParseInt(line.Substring(line.IndexOf('=') + 1).Trim(), 0);
        if (code == 403 || code == 404)
        {
          _parsePermanent = true;
          _parseHasError = true;
        }
      }
    }

    private char ReadDanmakuContentChar()
    {
      char value = _parseText[_parseContentPosition++];
      if (!_parseHtml || value != '&') return value;
      int start = _parseContentPosition - 1;
      if (MatchesDanmakuText(start, "&lt;", _parseContentEnd)) { _parseContentPosition += 3; return '<'; }
      if (MatchesDanmakuText(start, "&gt;", _parseContentEnd)) { _parseContentPosition += 3; return '>'; }
      if (MatchesDanmakuText(start, "&amp;", _parseContentEnd)) { _parseContentPosition += 4; return '&'; }
      if (MatchesDanmakuText(start, "&quot;", _parseContentEnd)) { _parseContentPosition += 5; return '"'; }
      return value;
    }

    private void FinishDanmakuPass()
    {
      if (_parsePhase == 2)
      {
        if ((_parseComplete && (_parseCompleteFlag != 1 || _parseExpectedCount < 0)) ||
            (_parseMetadata && _parseCompleteFlag != 0) ||
            (!_parseMetadata && _parseCompleteFlag == 0) || (_parseHtml && !_parseHtmlClosed))
          _parseIncomplete = true;
        if (_parseHasError || _parseIncomplete || (_parseComplete && _parseMetadata))
        {
          ScheduleDanmakuRetry(_parseHasError ? "danmaku error" : "danmaku incomplete", !_parsePermanent, _parseRetryAfter);
          return;
        }
        if (_parseMetadata)
        {
          FollowDanmakuMetadata();
          return;
        }
        if (_parseExpectedCount >= 0 && _parseExpectedCount != _parseRows)
        {
          ScheduleDanmakuRetry("danmaku count mismatch", !_parsePermanent, _parseRetryAfter);
          return;
        }
        _timeMs = new int[_parseRenderableRows];
        _mode = new int[_parseRenderableRows];
        _color = new int[_parseRenderableRows];
        _fontSize = new int[_parseRenderableRows];
        _content = new string[_parseRenderableRows];
        _parsePhase = 3;
        _parsePosition = _parseStart;
        _parseLineStart = _parseStart;
        _parseTabCount = 0;
        return;
      }
      CancelDanmakuParse();
      _loaded = true; // A complete zero-row response is a valid empty timeline.
      int videoMs = Utilities.IsValid(_controller) ? Mathf.RoundToInt(_controller.VideoTime * 1000f) + _timeOffsetMs : 0;
      SeekLineIndex(videoMs);
      _lastVideoMs = videoMs;
      SetStatus(_lineCount > 0 ? "loaded " + _lineCount : "empty danmaku");
    }

    private void FollowDanmakuMetadata()
    {
      CancelDanmakuParse();
      if (_metadataFollowed || _externalAudioMode ||
          (!string.IsNullOrEmpty(_parseProvider) && _parseProvider != "bili" && _parseProvider != "bilibili"))
      {
        SetStatus("unexpected danmaku metadata");
        return;
      }
      if (!Utilities.IsValid(_pagesPlaylist)) _pagesPlaylist = FindPagesPlaylistInParents();
      VRCUrl url = Utilities.IsValid(_pagesPlaylist) ? _pagesPlaylist.GetVcridUrl(_parseVcrid) : VRCUrl.Empty;
      if (VRCUrl.IsNullOrEmpty(url))
      {
        SetStatus("danmaku vcrid missing from catalog");
        return;
      }
      if (_requestRequiresV2 && !IsMetadataV2Url(url))
      {
        SetStatus("danmaku catalog protocol mismatch");
        return;
      }
      _metadataFollowed = true;
      SetStatus("loading danmaku vcrid");
      BeginDanmakuDownload(url);
    }

    private void ShowLine(int index)
    {
      ShowLineWithAge(index, 0f);
    }

    private void ShowLineWithAge(int index, float ageSeconds)
    {
      int mode = _mode[index];
      bool isFixedLyric = _externalAudioMode && mode == 4;
      if (isFixedLyric && (index <= 0 || _timeMs[index - 1] != _timeMs[index]))
      {
        HideAllTexts();
        ClearLaneTimers();
      }

      int poolIndex = FindFreeText();
      if (poolIndex < 0 || !Utilities.IsValid(_laneRoot)) return;

      TextMeshProUGUI text = _textPool[poolIndex];
      RectTransform rect = _poolRects[poolIndex];
      if (!Utilities.IsValid(text) || !Utilities.IsValid(rect)) return;

      bool isBottom = mode == 4;
      bool isTop = mode == 5;
      bool isStatic = isBottom || isTop;
      if (text.gameObject.activeSelf) text.gameObject.SetActive(false);
      text.text = _content[index];
      text.color = ToColor(_color[index]);

      float visualScale = Mathf.Clamp((_fontSize[index] * _fontScale) / 25f, 0.5f, 2.5f);
      rect.localScale = new Vector3(visualScale, visualScale, 1f);

      float rootWidth = Mathf.Max(1f, _laneRoot.rect.width);
      float rootHeight = Mathf.Max(1f, _laneRoot.rect.height);
      float areaFactor = GetDisplayAreaHeightFactor();
      float areaHeight = Mathf.Max(_lineHeight, rootHeight * areaFactor);
      float areaTop = rootHeight * 0.5f;
      float areaBottom = areaTop - areaHeight;
      int effectiveLaneCount = GetEffectiveLaneCount(areaHeight);
      int lane = isFixedLyric ? GetFixedLyricLane(index, effectiveLaneCount) : SelectLane(effectiveLaneCount);
      if (lane < 0) return;
      int contentLength = string.IsNullOrEmpty(_content[index]) ? 1 : _content[index].Length;
      float textWidth = Mathf.Max(120f, contentLength * 36f * visualScale + 96f);
      float y = isBottom
        ? areaBottom + _lineHeight * (lane + 0.5f)
        : areaTop - _lineHeight * (lane + 0.5f);

      _poolActive[poolIndex] = true;
      AddActivePoolIndex(poolIndex);
      _activeDuration[poolIndex] = isFixedLyric ? GetFixedLyricDuration(index) : isStatic ? _staticDuration : _scrollDuration;
      float clampedAge = Mathf.Clamp(ageSeconds, 0f, Mathf.Max(0.01f, _activeDuration[poolIndex] - 0.01f));
      _activeStartTime[poolIndex] = Time.time - clampedAge;
      _activeStartX[poolIndex] = isStatic ? 0f : rootWidth * 0.5f + textWidth * 0.5f;
      _activeEndX[poolIndex] = isStatic ? 0f : -rootWidth * 0.5f - textWidth * 0.5f;
      _activeY[poolIndex] = y;
      _activeMode[poolIndex] = mode;

      float progress = Mathf.Clamp01(clampedAge / Mathf.Max(0.01f, _activeDuration[poolIndex]));
      float x = Mathf.Lerp(_activeStartX[poolIndex], _activeEndX[poolIndex], progress);
      rect.anchoredPosition = new Vector2(x, y);
      text.gameObject.SetActive(true);
      float laneHoldSeconds = isStatic ? _staticDuration * 0.7f : _scrollDuration * 0.35f;
      _laneReadyAt[lane] = Time.time + Mathf.Max(0f, laneHoldSeconds - clampedAge);
    }

    private int GetFixedLyricLane(int index, int laneCount)
    {
      int groupStart = index;
      while (groupStart > 0 && _timeMs[groupStart - 1] == _timeMs[index]) groupStart--;

      int groupEnd = index;
      while (groupEnd + 1 < _lineCount && _timeMs[groupEnd + 1] == _timeMs[index]) groupEnd++;

      int groupPosition = index - groupStart;
      int groupCount = groupEnd - groupStart + 1;
      int lane = groupCount - groupPosition;
      return Mathf.Clamp(lane, 0, Mathf.Max(0, laneCount - 1));
    }

    private float GetFixedLyricDuration(int index)
    {
      int nextIndex = index + 1;
      while (nextIndex < _lineCount && _timeMs[nextIndex] == _timeMs[index]) nextIndex++;
      if (nextIndex < _lineCount)
      {
        int intervalMs = _timeMs[nextIndex] - _timeMs[index];
        if (intervalMs > 0) return Mathf.Max(0.5f, intervalMs / 1000f);
      }

      if (Utilities.IsValid(_controller) && !_controller.IsLive)
      {
        float remaining = _controller.Duration - _timeMs[index] / 1000f;
        if (remaining > 0f) return Mathf.Max(0.5f, remaining);
      }
      return _staticDuration;
    }

    private void UpdateActiveTexts()
    {
      if (!Utilities.IsValid(_textPool)) return;

      int cursor = 0;
      while (cursor < _activePoolCount)
      {
        int i = _activePoolIndexes[cursor];
        if (!_poolActive[i])
        {
          RemoveActivePoolIndexAt(cursor);
          continue;
        }

        TextMeshProUGUI text = _textPool[i];
        RectTransform rect = _poolRects[i];
        if (!Utilities.IsValid(text) || !Utilities.IsValid(rect))
        {
          _poolActive[i] = false;
          RemoveActivePoolIndexAt(cursor);
          continue;
        }

        float age = Time.time - _activeStartTime[i];
        float t = Mathf.Clamp01(age / Mathf.Max(0.01f, _activeDuration[i]));
        if (t >= 1f)
        {
          _poolActive[i] = false;
          text.gameObject.SetActive(false);
          RemoveActivePoolIndexAt(cursor);
          continue;
        }

        float x = Mathf.Lerp(_activeStartX[i], _activeEndX[i], t);
        rect.anchoredPosition = new Vector2(x, _activeY[i]);
        cursor++;
      }
    }

    private void ResumeActiveTimers(float pauseSeconds)
    {
      if (pauseSeconds <= 0f) return;

      int cursor = 0;
      while (cursor < _activePoolCount)
      {
        int i = _activePoolIndexes[cursor];
        if (_poolActive[i]) _activeStartTime[i] += pauseSeconds;
        cursor++;
      }

      if (!Utilities.IsValid(_laneReadyAt)) return;
      for (int i = 0; i < _laneReadyAt.Length; i++)
      {
        if (_laneReadyAt[i] > 0f) _laneReadyAt[i] += pauseSeconds;
      }
    }

    private void ApplyAlphaToActiveTexts()
    {
      if (!Utilities.IsValid(_textPool)) return;

      int cursor = 0;
      while (cursor < _activePoolCount)
      {
        int i = _activePoolIndexes[cursor];
        if (_poolActive[i] && Utilities.IsValid(_textPool[i]))
        {
          Color color = _textPool[i].color;
          color.a = GetVisibleAlpha();
          _textPool[i].color = color;
        }
        cursor++;
      }
    }

    private void AddActivePoolIndex(int poolIndex)
    {
      if (!Utilities.IsValid(_activePoolIndexes)) return;
      if (_activePoolCount < 0) _activePoolCount = 0;
      if (_activePoolCount >= _activePoolIndexes.Length) return;

      _activePoolIndexes[_activePoolCount] = poolIndex;
      _activePoolCount++;
    }

    private void RemoveActivePoolIndexAt(int activeIndex)
    {
      if (!Utilities.IsValid(_activePoolIndexes)) return;
      if (activeIndex < 0 || activeIndex >= _activePoolCount) return;

      _activePoolCount--;
      if (activeIndex < _activePoolCount)
      {
        _activePoolIndexes[activeIndex] = _activePoolIndexes[_activePoolCount];
      }
    }

    private int FindFreeText()
    {
      for (int i = 0; i < _poolActive.Length; i++)
      {
        if (!_poolActive[i]) return i;
      }
      return -1;
    }

    private int SelectLane(int laneCount)
    {
      int count = Mathf.Clamp(laneCount, 1, Mathf.Max(1, _laneReadyAt.Length));
      int best = 0;
      float bestTime = _laneReadyAt[0];
      for (int i = 1; i < count; i++)
      {
        if (_laneReadyAt[i] < bestTime)
        {
          best = i;
          bestTime = _laneReadyAt[i];
        }
      }
      if (_dropDanmakuWhenLanesAreFull && bestTime > Time.time + Mathf.Max(0f, _laneSpawnSlackSeconds)) return -1;
      return best;
    }

    private int GetEffectiveLaneCount(float areaHeight)
    {
      int byHeight = Mathf.Max(1, Mathf.FloorToInt(areaHeight / Mathf.Max(1f, _lineHeight)));
      return Mathf.Clamp(byHeight, 1, Mathf.Max(1, _laneReadyAt.Length));
    }

    private float GetDisplayAreaHeightFactor()
    {
      if (_displayAreaMode == 1) return 0.5f;
      if (_displayAreaMode == 2) return 0.25f;
      return 1f;
    }

    private void SetDisplayAreaMode(int mode)
    {
      _displayAreaMode = Mathf.Clamp(mode, 0, 2);
      ClearLaneTimers();
      UpdateDisplayAreaButtonLabel();
      SetStatus(GetDisplayAreaStatusText());
    }

    private void UpdateDisplayAreaButtonLabel()
    {
      if (!Utilities.IsValid(_displayAreaButtonLabel)) return;
      _displayAreaButtonLabel.text = GetDisplayAreaButtonText();
    }

    private void UpdateDanmakuToggleButtonLabel()
    {
      if (!Utilities.IsValid(_danmakuToggleButtonLabel)) return;
      _danmakuToggleButtonLabel.text = _danmakuEnabled ? "弹幕显示：开" : "弹幕显示：关";
    }

    private void UpdateUrlPrefixToggleButtonLabel()
    {
      if (!Utilities.IsValid(_urlPrefixToggleButtonLabel)) return;
      _urlPrefixToggleButtonLabel.text = _urlPrefixFillEnabled ? "链接填充：开" : "链接填充：关";
    }

    private string GetDisplayAreaButtonText()
    {
      if (_displayAreaMode == 1) return "弹幕区域：半屏";
      if (_displayAreaMode == 2) return "弹幕区域：四分之一";
      return "弹幕区域：全屏";
    }

    private string GetDisplayAreaStatusText()
    {
      if (_displayAreaMode == 1) return "danmaku half screen";
      if (_displayAreaMode == 2) return "danmaku quarter screen";
      return "danmaku full screen";
    }

    private void SeekLineIndex(int videoMs)
    {
      int target = Mathf.Max(0, videoMs - 300);
      _nextLine = LowerBoundDanmakuTime(target);
      ClearLaneTimers();
    }

    private int LowerBoundDanmakuTime(int target)
    {
      int low = 0;
      int high = _lineCount;
      while (low < high)
      {
        int middle = low + (high - low) / 2;
        if (_timeMs[middle] < target) low = middle + 1;
        else high = middle;
      }
      return low;
    }

    private void RestoreVisibleDanmakuAt(int videoMs)
    {
      HideAllTexts();
      ClearLaneTimers();

      int restoreWindowMs = Mathf.CeilToInt(Mathf.Max(_scrollDuration, _staticDuration) * 1000f);
      int startMs = Mathf.Max(0, videoMs - restoreWindowMs);
      for (int i = LowerBoundDanmakuTime(startMs); i < _lineCount && _timeMs[i] <= videoMs; i++)
      {
        if (_timeMs[i] < startMs) continue;

        int mode = _mode[i];
        bool isStatic = mode == 4 || mode == 5;
        float duration = isStatic ? _staticDuration : _scrollDuration;
        int ageMs = videoMs - _timeMs[i];
        if (ageMs < 0 || ageMs >= Mathf.CeilToInt(duration * 1000f)) continue;

        ShowLineWithAge(i, ageMs / 1000f);
      }

      _nextLine = LowerBoundDanmakuTime(videoMs + 121);
      _lastVideoMs = videoMs;
    }

    private void HideAllTexts()
    {
      if (!Utilities.IsValid(_textPool)) return;
      int cursor = 0;
      while (cursor < _activePoolCount && cursor < _activePoolIndexes.Length)
      {
        int i = _activePoolIndexes[cursor];
        if (i < 0 || i >= _poolActive.Length || i >= _textPool.Length)
        {
          cursor++;
          continue;
        }

        _poolActive[i] = false;
        if (Utilities.IsValid(_textPool[i])) _textPool[i].gameObject.SetActive(false);
        cursor++;
      }

      _activePoolCount = 0;
    }

    private void ClearLaneTimers()
    {
      if (!Utilities.IsValid(_laneReadyAt)) return;
      for (int i = 0; i < _laneReadyAt.Length; i++) _laneReadyAt[i] = 0f;
    }

    private string ExtractBiliKey(string input)
    {
      string text = PercentDecodeBasic(input);
      int bv = text.IndexOf("BV");
      if (bv < 0) bv = text.IndexOf("bv");
      if (bv >= 0 && text.Length >= bv + 12)
      {
        return text.Substring(bv, Mathf.Min(12, text.Length - bv)).Replace("bv", "BV");
      }

      int av = text.IndexOf("av");
      if (av >= 0) return "av";
      return "";
    }

    private string PercentDecodeBasic(string input)
    {
      if (string.IsNullOrEmpty(input)) return "";
      return input
        .Replace("%3A", ":").Replace("%3a", ":")
        .Replace("%2F", "/").Replace("%2f", "/")
        .Replace("%3F", "?").Replace("%3f", "?")
        .Replace("%26", "&")
        .Replace("%3D", "=").Replace("%3d", "=");
    }

    private int ParseInt(string value, int fallback)
    {
      int parsed;
      if (int.TryParse(value, out parsed)) return parsed;
      return fallback;
    }

    private Color ToColor(int rgb)
    {
      float r = ((rgb >> 16) & 255) / 255f;
      float g = ((rgb >> 8) & 255) / 255f;
      float b = (rgb & 255) / 255f;
      return new Color(r, g, b, GetVisibleAlpha());
    }

    private float GetVisibleAlpha()
    {
      return _danmakuEnabled ? Mathf.Clamp01(_textAlpha) : 0f;
    }

    private void SetStatus(string text)
    {
      if (!Utilities.IsValid(_statusText)) return;

      _statusText.text = text;
      _statusText.gameObject.SetActive(true);
      _statusHideAt = Time.time + Mathf.Max(0f, _statusVisibleSeconds);
    }

    private void UpdateStatusVisibility()
    {
      if (!Utilities.IsValid(_statusText)) return;
      if (_statusVisibleSeconds <= 0f)
      {
        _statusText.gameObject.SetActive(false);
        return;
      }
      if (_statusText.gameObject.activeSelf && Time.time >= _statusHideAt)
      {
        _statusText.gameObject.SetActive(false);
      }
    }

    private Controller FindControllerInParents()
    {
      Transform current = transform;
      while (Utilities.IsValid(current))
      {
        Controller controller = current.GetComponentInChildren<Controller>(true);
        if (Utilities.IsValid(controller)) return controller;
        current = current.parent;
      }
      return null;
    }

    private YamaBiliPagesPlaylist3 FindPagesPlaylistInParents()
    {
      Transform current = transform;
      while (Utilities.IsValid(current))
      {
        YamaBiliPagesPlaylist3 pages = current.GetComponentInChildren<YamaBiliPagesPlaylist3>(true);
        if (Utilities.IsValid(pages)) return pages;
        current = current.parent;
      }
      return null;
    }
  }
}
