using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Components;
using VRC.SDKBase;
using Yamadev.YamaStream;

namespace YamaBiliDanmakuV3
{
  [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
  public class YamaBiliUrlPrefixHelper3 : UdonSharpBehaviour
  {
    [Header("YamaPlayer")]
    [SerializeField] private Controller _controller;

    [Header("YamaPlayer URL Inputs")]
    [SerializeField] private VRCUrlInputField _topUrlInputField;
    [SerializeField] private VRCUrlInputField _bottomUrlInputField;

    [Header("Integrated Queue Input")]
    [SerializeField] private VRCUrlInputField _queueUrlInputField;
    [SerializeField] private YamaBiliPagesPlaylist3 _queuePlaylist;
    [SerializeField] private Text _queueInputText;
    [SerializeField] private Text _queueInputIdleLabel;

    [Header("Prefix")]
    [SerializeField] private bool _enableUrlPrefixOnInput = true;
    [SerializeField] private TextMeshProUGUI _urlPrefixToggleButtonLabel;
    [Tooltip("Prefix inserted into an empty YamaPlayer URL field. Change this when moving to another domain.")]
    [SerializeField] private VRCUrl _urlPrefix = VRCUrl.Empty;
    [Tooltip("When enabled, empty URL fields are refilled periodically. Leave this off if players should be able to delete the prefix manually.")]
    [SerializeField] private bool _keepPrefixWhenEmpty = false;
    [SerializeField, Range(0.5f, 10f)] private float _refreshSeconds = 3f;
    [SerializeField, Range(0.1f, 2f)] private float _inputWatchSeconds = 0.25f;

    private bool _topInputWasActive;
    private bool _bottomInputWasActive;
    private bool _topInputWasEmpty;
    private bool _bottomInputWasEmpty;
    private bool _queueInputEditing;
    private VRCUrl _lastTopInputUrl = VRCUrl.Empty;
    private VRCUrl _lastBottomInputUrl = VRCUrl.Empty;

    private void Start()
    {
      _topInputWasActive = false;
      _bottomInputWasActive = false;
      _topInputWasEmpty = true;
      _bottomInputWasEmpty = true;
      ResetQueueInput();
      SendCustomEventDelayedFrames(nameof(PrimeQueueInput), 1);
      SendCustomEventDelayedSeconds(nameof(PrimeQueueInput), 0.5f);
      UpdateUrlPrefixToggleButtonLabel();
      if (_enableUrlPrefixOnInput) SendCustomEventDelayedSeconds(nameof(WatchInputFields), _inputWatchSeconds);
      if (_keepPrefixWhenEmpty) SendCustomEventDelayedSeconds(nameof(RefreshLoop), _refreshSeconds);
    }

    public void WatchInputFields()
    {
      if (!_enableUrlPrefixOnInput) return;

      WatchInputField(_topUrlInputField, true);
      WatchInputField(_bottomUrlInputField, false);
      if (!_queueInputEditing) PrimeQueueInput();

      SendCustomEventDelayedSeconds(nameof(WatchInputFields), _inputWatchSeconds);
    }

    public void RefreshLoop()
    {
      if (!_keepPrefixWhenEmpty) return;
      ApplyPrefixToEmptyFields();
      SendCustomEventDelayedSeconds(nameof(RefreshLoop), _refreshSeconds);
    }

    public void ApplyPrefixToEmptyFields()
    {
      if (!_enableUrlPrefixOnInput) return;
      ApplyPrefixToEmptyField(_topUrlInputField);
      ApplyPrefixToEmptyField(_bottomUrlInputField);
    }

    public void ApplyPrefixToTopInput()
    {
      RememberInputUrl(_topUrlInputField, true);
      if (!_enableUrlPrefixOnInput) return;
      ApplyPrefixToEmptyField(_topUrlInputField);
    }

    public void ApplyPrefixToBottomInput()
    {
      RememberInputUrl(_bottomUrlInputField, false);
      if (!_enableUrlPrefixOnInput) return;
      ApplyPrefixToEmptyField(_bottomUrlInputField);
    }

    public void PrimeQueueInput()
    {
      if (_queueInputEditing) return;
      SetQueueInputToIdleValue();
      SetQueueInputEditingVisual(false);
    }

    public void PrepareQueueInput()
    {
      if (!_queueInputEditing) SetQueueInputToIdleValue();
      SetQueueInputEditingVisual(true);
    }

    public void SubmitQueueInput()
    {
      if (!Utilities.IsValid(_queueUrlInputField)) return;
      if (!Utilities.IsValid(_controller))
      {
        ResetQueueInput();
        return;
      }

      VRCUrl inputUrl = _queueUrlInputField.GetUrl();
      if (!IsCompleteInputUrl(inputUrl))
      {
        ResetQueueInput();
        return;
      }

      string value = inputUrl.Get();
      string lower = string.IsNullOrEmpty(value) ? "" : value.ToLower();
      if (!lower.StartsWith("http://") && !lower.StartsWith("https://"))
      {
        ResetQueueInput();
        return;
      }

      if (Utilities.IsValid(_queuePlaylist))
      {
        _queuePlaylist.QueueInputUrl(inputUrl);
        ResetQueueInput();
        return;
      }

      Track track = Track.New(_controller.VideoPlayerType, "", inputUrl);
      _controller.TakeOwnership();
      if (_controller.Stopped && !_controller.IsLoading)
      {
        _controller.PlayTrack(track);
      }
      else if (Utilities.IsValid(_controller.Queue))
      {
        _controller.Queue.TakeOwnership();
        _controller.Queue.AddTrack(track);
      }

      ResetQueueInput();
    }

    public void ScheduleQueueInputReset()
    {
      if (_queueInputEditing) return;
      SendCustomEventDelayedFrames(nameof(ResetQueueInput), 1);
    }

    public void ResetQueueInput()
    {
      SetQueueInputToIdleValue();
      SetQueueInputEditingVisual(false);
      SendCustomEventDelayedFrames(nameof(RestoreQueueInputPrefix), 1);
      SendCustomEventDelayedSeconds(nameof(RestoreQueueInputPrefix), 0.3f);
    }

    public void RestoreQueueInputPrefix()
    {
      if (!_queueInputEditing) PrimeQueueInput();
    }

    private void SetQueueInputEditingVisual(bool editing)
    {
      _queueInputEditing = editing;
      if (Utilities.IsValid(_queueInputText))
      {
        Color color = _queueInputText.color;
        color.a = editing ? 0.96f : 0f;
        _queueInputText.color = color;
      }
      if (Utilities.IsValid(_queueInputIdleLabel)) _queueInputIdleLabel.gameObject.SetActive(!editing);
    }

    public void SetEnableUrlPrefixOnInput(bool enableUrlPrefixOnInput)
    {
      _enableUrlPrefixOnInput = enableUrlPrefixOnInput;
      UpdateUrlPrefixToggleButtonLabel();
      if (_enableUrlPrefixOnInput)
      {
        ApplyPrefixToEmptyFields();
        ResetQueueInput();
        SendCustomEventDelayedSeconds(nameof(WatchInputFields), _inputWatchSeconds);
      }
      else
      {
        ClearPrefixIfOnlyPrefix(_topUrlInputField);
        ClearPrefixIfOnlyPrefix(_bottomUrlInputField);
        ResetQueueInput();
      }
    }

    public void ToggleUrlPrefixBackfill()
    {
      SetEnableUrlPrefixOnInput(!_enableUrlPrefixOnInput);
    }

    public void EnableUrlPrefixBackfill()
    {
      SetEnableUrlPrefixOnInput(true);
    }

    public void DisableUrlPrefixBackfill()
    {
      SetEnableUrlPrefixOnInput(false);
    }

    public void SetUrlPrefix(VRCUrl urlPrefix)
    {
      _urlPrefix = urlPrefix;
      ApplyPrefixToEmptyFields();
      ResetQueueInput();
    }

    public void SetKeepPrefixWhenEmpty(bool keepPrefixWhenEmpty)
    {
      _keepPrefixWhenEmpty = keepPrefixWhenEmpty;
      if (_keepPrefixWhenEmpty) SendCustomEventDelayedSeconds(nameof(RefreshLoop), _refreshSeconds);
    }

    public VRCUrl GetTopInputUrl()
    {
      return GetCurrentOrLastInputUrl(_topUrlInputField, _lastTopInputUrl);
    }

    public VRCUrl GetBottomInputUrl()
    {
      return GetCurrentOrLastInputUrl(_bottomUrlInputField, _lastBottomInputUrl);
    }

    private void ApplyPrefixToEmptyField(VRCUrlInputField inputField)
    {
      if (!_enableUrlPrefixOnInput) return;
      if (!Utilities.IsValid(inputField) || VRCUrl.IsNullOrEmpty(_urlPrefix) || string.IsNullOrEmpty(_urlPrefix.Get())) return;

      VRCUrl currentUrl = inputField.GetUrl();
      if (VRCUrl.IsNullOrEmpty(currentUrl) || string.IsNullOrEmpty(currentUrl.Get()))
      {
        inputField.SetUrl(_urlPrefix);
      }
    }

    private bool IsInputActive(VRCUrlInputField inputField)
    {
      return Utilities.IsValid(inputField) && inputField.gameObject.activeInHierarchy;
    }

    private void WatchInputField(VRCUrlInputField inputField, bool isTop)
    {
      RememberInputUrl(inputField, isTop);

      bool active = IsInputActive(inputField);
      bool empty = IsInputEmpty(inputField);
      bool wasActive = isTop ? _topInputWasActive : _bottomInputWasActive;
      bool wasEmpty = isTop ? _topInputWasEmpty : _bottomInputWasEmpty;

      if (active && (!wasActive || (!wasEmpty && empty)))
      {
        ApplyPrefixToEmptyField(inputField);
        empty = IsInputEmpty(inputField);
      }

      if (isTop)
      {
        _topInputWasActive = active;
        _topInputWasEmpty = empty;
      }
      else
      {
        _bottomInputWasActive = active;
        _bottomInputWasEmpty = empty;
      }
    }

    private bool IsInputEmpty(VRCUrlInputField inputField)
    {
      if (!Utilities.IsValid(inputField)) return true;

      VRCUrl currentUrl = GetInputUrl(inputField);
      return VRCUrl.IsNullOrEmpty(currentUrl) || string.IsNullOrEmpty(currentUrl.Get());
    }

    private VRCUrl GetInputUrl(VRCUrlInputField inputField)
    {
      if (!Utilities.IsValid(inputField)) return VRCUrl.Empty;
      return inputField.GetUrl();
    }

    private VRCUrl GetCurrentOrLastInputUrl(VRCUrlInputField inputField, VRCUrl lastUrl)
    {
      VRCUrl currentUrl = GetInputUrl(inputField);
      if (IsCompleteInputUrl(currentUrl)) return currentUrl;
      return IsCompleteInputUrl(lastUrl) ? lastUrl : currentUrl;
    }

    private void RememberInputUrl(VRCUrlInputField inputField, bool isTop)
    {
      VRCUrl currentUrl = GetInputUrl(inputField);
      if (!IsCompleteInputUrl(currentUrl)) return;

      if (isTop) _lastTopInputUrl = currentUrl;
      else _lastBottomInputUrl = currentUrl;
    }

    private bool IsCompleteInputUrl(VRCUrl url)
    {
      if (VRCUrl.IsNullOrEmpty(url)) return false;

      string value = url.Get();
      if (string.IsNullOrEmpty(value)) return false;
      if (VRCUrl.IsNullOrEmpty(_urlPrefix)) return true;
      return value != _urlPrefix.Get();
    }

    private void ClearPrefixIfOnlyPrefix(VRCUrlInputField inputField)
    {
      if (!Utilities.IsValid(inputField) || VRCUrl.IsNullOrEmpty(_urlPrefix)) return;

      VRCUrl currentUrl = inputField.GetUrl();
      if (VRCUrl.IsNullOrEmpty(currentUrl)) return;

      string current = currentUrl.Get();
      string prefix = _urlPrefix.Get();
      if (string.IsNullOrEmpty(current) || current == prefix)
      {
        inputField.SetUrl(VRCUrl.Empty);
      }
    }

    private void SetQueueInputToIdleValue()
    {
      if (!Utilities.IsValid(_queueUrlInputField)) return;

      bool hasPrefix = _enableUrlPrefixOnInput && !VRCUrl.IsNullOrEmpty(_urlPrefix) && !string.IsNullOrEmpty(_urlPrefix.Get());
      VRCUrl targetUrl = hasPrefix ? _urlPrefix : VRCUrl.Empty;
      VRCUrl currentUrl = _queueUrlInputField.GetUrl();
      string current = VRCUrl.IsNullOrEmpty(currentUrl) ? "" : currentUrl.Get();
      string target = VRCUrl.IsNullOrEmpty(targetUrl) ? "" : targetUrl.Get();
      if (current != target) _queueUrlInputField.SetUrl(targetUrl);
    }

    private void UpdateUrlPrefixToggleButtonLabel()
    {
      if (!Utilities.IsValid(_urlPrefixToggleButtonLabel)) return;
      _urlPrefixToggleButtonLabel.text = _enableUrlPrefixOnInput ? "链接填充：开" : "链接填充：关";
    }
  }
}
