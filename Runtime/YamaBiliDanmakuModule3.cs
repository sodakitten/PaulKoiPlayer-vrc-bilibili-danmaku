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
    [SerializeField] private TextMeshProUGUI _statusText;
    [SerializeField] private TextMeshProUGUI _displayAreaButtonLabel;
    [SerializeField] private TextMeshProUGUI _danmakuToggleButtonLabel;
    [SerializeField, Range(0f, 10f)] private float _statusVisibleSeconds = 2f;

    [Header("Renderer")]
    [SerializeField] private RectTransform _laneRoot;
    [SerializeField] private TextMeshProUGUI[] _textPool = new TextMeshProUGUI[0];
    [SerializeField, Range(1, 32)] private int _laneCount = 12;
    [SerializeField, Range(12f, 80f)] private float _lineHeight = 34f;
    [SerializeField, Range(4f, 16f)] private float _scrollDuration = 8f;
    [SerializeField, Range(1f, 8f)] private float _staticDuration = 4f;
    [SerializeField, Range(-5000, 5000)] private int _timeOffsetMs = 0;
    [SerializeField, Range(64, 4096)] private int _maxDanmakuLines = 1600;

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
    private int _lastVideoMs = -1;
    private float _statusHideAt;
    private bool _lastLoadWasCurrentUrl;
    private bool _triedFallbackAfterCurrentUrl;

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
      SetStatus("idle");
    }

    private void Update()
    {
      UpdateStatusVisibility();
      if (!Utilities.IsValid(_controller)) return;

      if (_controller.Stopped)
      {
        if (_loaded || _lineCount > 0 || _requestedForPlayback) ResetDanmaku();
        return;
      }

      if (!_danmakuEnabled)
      {
        HideAllTexts();
        return;
      }

      if (!_requestedForPlayback)
      {
        _requestedForPlayback = true;
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
      if (!_loadFromCurrentYamaPlayerUrl)
      {
        LoadFallbackDanmaku();
        return;
      }

      VRCUrl currentUrl = GetCurrentYamaPlayerUrl();
      if (VRCUrl.IsNullOrEmpty(currentUrl))
      {
        SetStatus("no current url");
        LoadFallbackDanmaku();
        return;
      }

      _lastLoadWasCurrentUrl = true;
      _triedFallbackAfterCurrentUrl = false;
      SetStatus("loading current url");
      VRCStringDownloader.LoadUrl(currentUrl, (IUdonEventReceiver)this);
    }

    public void LoadFallbackDanmaku()
    {
      if (VRCUrl.IsNullOrEmpty(_fallbackDanmakuUrl))
      {
        SetStatus("no danmaku url");
        return;
      }

      _lastLoadWasCurrentUrl = false;
      SetStatus("loading fallback");
      VRCStringDownloader.LoadUrl(_fallbackDanmakuUrl, (IUdonEventReceiver)this);
    }

    public void ClearDanmaku()
    {
      ResetDanmaku();
    }

    public void HideDanmaku()
    {
      HideAllTexts();
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

    public void SetDanmakuEnabled(bool enabled)
    {
      if (_danmakuEnabled == enabled) return;

      _danmakuEnabled = enabled;

      if (!_danmakuEnabled)
      {
        HideAllTexts();
        SetStatus("danmaku off");
        UpdateDanmakuToggleButtonLabel();
        return;
      }

      SetStatus("danmaku on");
      UpdateDanmakuToggleButtonLabel();
      ClearLaneTimers();
      if (Utilities.IsValid(_controller) && !_controller.Stopped)
      {
        int videoMs = Mathf.RoundToInt(_controller.VideoTime * 1000f) + _timeOffsetMs;
        if (_loaded) SeekLineIndex(videoMs);
        else _requestedForPlayback = false;
      }
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
      string body = ExtractDanmakuBlock(result.Result);
      if (string.IsNullOrEmpty(body))
      {
        if (TryLoadFallbackAfterCurrentUrl("no #YBDM block")) return;
        SetStatus("no #YBDM block");
        return;
      }

      ParseDanmaku(body);
    }

    public override void OnStringLoadError(IVRCStringDownload result)
    {
      if (TryLoadFallbackAfterCurrentUrl("load error " + result.ErrorCode)) return;
      SetStatus("load error " + result.ErrorCode);
    }

    private bool TryLoadFallbackAfterCurrentUrl(string reason)
    {
      if (!_lastLoadWasCurrentUrl || _triedFallbackAfterCurrentUrl || VRCUrl.IsNullOrEmpty(_fallbackDanmakuUrl)) return false;

      _triedFallbackAfterCurrentUrl = true;
      SetStatus(reason + ", fallback");
      LoadFallbackDanmaku();
      return true;
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

      object currentUrl = _controller.GetProgramVariable("_url");
      if (!Utilities.IsValid(currentUrl)) return VRCUrl.Empty;

      VRCUrl url = (VRCUrl)currentUrl;
      if (!VRCUrl.IsNullOrEmpty(url)) return url;

      return VRCUrl.Empty;
    }

    private void ParseDanmaku(string text)
    {
      _timeMs = new int[_maxDanmakuLines];
      _mode = new int[_maxDanmakuLines];
      _color = new int[_maxDanmakuLines];
      _fontSize = new int[_maxDanmakuLines];
      _content = new string[_maxDanmakuLines];
      _lineCount = 0;

      string[] lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
      for (int i = 0; i < lines.Length && _lineCount < _maxDanmakuLines; i++)
      {
        string line = lines[i];
        if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;

        string[] parts = line.Split('\t');
        if (parts.Length < 6) continue;

        int mode = ParseInt(parts[1], 1);
        int pool = ParseInt(parts[4], 0);
        if (pool == 2 || mode == 7 || mode == 8 || mode == 9) continue;

        _timeMs[_lineCount] = ParseInt(parts[0], 0);
        _mode[_lineCount] = mode;
        _color[_lineCount] = ParseInt(parts[2], 16777215);
        _fontSize[_lineCount] = ParseInt(parts[3], 25);
        _content[_lineCount] = UnescapeField(parts[5]);
        _lineCount++;
      }

      _nextLine = 0;
      _loaded = _lineCount > 0;
      ClearLaneTimers();
      SetStatus(_loaded ? "loaded " + _lineCount : "empty danmaku");
    }

    private void ShowLine(int index)
    {
      int poolIndex = FindFreeText();
      if (poolIndex < 0 || !Utilities.IsValid(_laneRoot)) return;

      TextMeshProUGUI text = _textPool[poolIndex];
      RectTransform rect = _poolRects[poolIndex];
      if (!Utilities.IsValid(text) || !Utilities.IsValid(rect)) return;

      int mode = _mode[index];
      bool isBottom = mode == 4;
      bool isTop = mode == 5;
      bool isStatic = isBottom || isTop;
      text.gameObject.SetActive(false);
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
      int lane = SelectLane(GetEffectiveLaneCount(areaHeight));
      int contentLength = string.IsNullOrEmpty(_content[index]) ? 1 : _content[index].Length;
      float textWidth = Mathf.Max(120f, contentLength * 36f * visualScale + 96f);
      float y = isBottom
        ? areaBottom + _lineHeight * (lane + 0.5f)
        : areaTop - _lineHeight * (lane + 0.5f);

      _poolActive[poolIndex] = true;
      AddActivePoolIndex(poolIndex);
      _activeStartTime[poolIndex] = Time.time;
      _activeDuration[poolIndex] = isStatic ? _staticDuration : _scrollDuration;
      _activeStartX[poolIndex] = isStatic ? 0f : rootWidth * 0.5f + textWidth * 0.5f;
      _activeEndX[poolIndex] = isStatic ? 0f : -rootWidth * 0.5f - textWidth * 0.5f;
      _activeY[poolIndex] = y;
      _activeMode[poolIndex] = mode;

      rect.anchoredPosition = new Vector2(_activeStartX[poolIndex], y);
      text.gameObject.SetActive(true);
      _laneReadyAt[lane] = Time.time + (isStatic ? _staticDuration * 0.7f : _scrollDuration * 0.35f);
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
          color.a = Mathf.Clamp01(_textAlpha);
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
      _danmakuToggleButtonLabel.text = _danmakuEnabled ? "Danmaku: On" : "Danmaku: Off";
    }

    private string GetDisplayAreaButtonText()
    {
      if (_displayAreaMode == 1) return "Danmaku: Half";
      if (_displayAreaMode == 2) return "Danmaku: 1/4";
      return "Danmaku: Full";
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
      _nextLine = 0;
      while (_nextLine < _lineCount && _timeMs[_nextLine] < target)
      {
        _nextLine++;
      }
      ClearLaneTimers();
    }

    private void HideAllTexts()
    {
      if (!Utilities.IsValid(_textPool)) return;
      for (int i = 0; i < _textPool.Length; i++)
      {
        _poolActive[i] = false;
        if (Utilities.IsValid(_textPool[i])) _textPool[i].gameObject.SetActive(false);
      }
      _activePoolCount = 0;
    }

    private void ClearLaneTimers()
    {
      if (!Utilities.IsValid(_laneReadyAt)) return;
      for (int i = 0; i < _laneReadyAt.Length; i++) _laneReadyAt[i] = 0f;
    }

    private string ExtractDanmakuBlock(string raw)
    {
      if (string.IsNullOrEmpty(raw)) return "";

      int marker = raw.IndexOf("#YBDM/1");
      if (marker >= 0) return raw.Substring(marker);

      string open = "<script type=\"application/yama-bili-danmaku\">";
      int start = raw.IndexOf(open);
      if (start < 0) return "";
      start += open.Length;
      int end = raw.IndexOf("</script>", start);
      if (end < 0) return "";
      return HtmlDecodeBasic(raw.Substring(start, end - start));
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

    private string HtmlDecodeBasic(string input)
    {
      if (string.IsNullOrEmpty(input)) return "";
      return input
        .Replace("&lt;", "<")
        .Replace("&gt;", ">")
        .Replace("&amp;", "&")
        .Replace("&quot;", "\"");
    }

    private int ParseInt(string value, int fallback)
    {
      int parsed;
      if (int.TryParse(value, out parsed)) return parsed;
      return fallback;
    }

    private string UnescapeField(string value)
    {
      if (string.IsNullOrEmpty(value)) return "";
      return value
        .Replace("\\n", "\n")
        .Replace("\\r", "\r")
        .Replace("\\t", "\t")
        .Replace("\\\\", "\\");
    }

    private Color ToColor(int rgb)
    {
      float r = ((rgb >> 16) & 255) / 255f;
      float g = ((rgb >> 8) & 255) / 255f;
      float b = (rgb & 255) / 255f;
      return new Color(r, g, b, Mathf.Clamp01(_textAlpha));
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
  }
}
