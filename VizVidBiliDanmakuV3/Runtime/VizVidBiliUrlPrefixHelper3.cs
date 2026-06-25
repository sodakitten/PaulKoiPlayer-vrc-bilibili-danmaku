using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDKBase;
using JLChnToZ.VRC.VVMW;

namespace VizVidBiliDanmakuV3
{
  [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
  public class VizVidBiliUrlPrefixHelper3 : UdonSharpBehaviour
  {
    [Header("VizVid URL Input")]
    [SerializeField] private Core _core;
    [SerializeField] private VRCUrlInputField _addressUrlInputField;

    [Header("Prefix")]
    [SerializeField] private bool _enableUrlPrefixOnInput = true;
    [SerializeField] private TextMeshProUGUI _urlPrefixToggleButtonLabel;
    [Tooltip("Prefix inserted into an empty VizVid URL field. Change this when moving to another domain.")]
    [SerializeField] private VRCUrl _urlPrefix = VRCUrl.Empty;
    [Tooltip("When enabled, empty URL fields are refilled periodically. Leave this off if players should be able to delete the prefix manually.")]
    [SerializeField] private bool _keepPrefixWhenEmpty = false;
    [SerializeField, Range(0.5f, 10f)] private float _refreshSeconds = 3f;
    [SerializeField, Range(0.1f, 2f)] private float _inputWatchSeconds = 0.25f;

    private bool _inputWasActive;
    private bool _inputWasEmpty;

    private void Start()
    {
      _inputWasActive = false;
      _inputWasEmpty = true;
      UpdateUrlPrefixToggleButtonLabel();
      if (Utilities.IsValid(_core))
      {
        _core._AddListener(this, "_onVideoEnd");
      }
      if (_enableUrlPrefixOnInput) SendCustomEventDelayedSeconds(nameof(WatchInputField), _inputWatchSeconds);
      if (_keepPrefixWhenEmpty) SendCustomEventDelayedSeconds(nameof(RefreshLoop), _refreshSeconds);
    }

    public void WatchInputField()
    {
      if (!_enableUrlPrefixOnInput) return;

      bool active = IsInputActive(_addressUrlInputField);
      bool empty = IsInputEmpty(_addressUrlInputField);
      if (active && (!_inputWasActive || (!_inputWasEmpty && empty)))
      {
        ApplyPrefixToEmptyField();
        empty = IsInputEmpty(_addressUrlInputField);
      }

      _inputWasActive = active;
      _inputWasEmpty = empty;
      SendCustomEventDelayedSeconds(nameof(WatchInputField), _inputWatchSeconds);
    }

    public void RefreshLoop()
    {
      if (!_keepPrefixWhenEmpty) return;
      ApplyPrefixToEmptyField();
      SendCustomEventDelayedSeconds(nameof(RefreshLoop), _refreshSeconds);
    }

    public void ApplyPrefixToEmptyField()
    {
      if (!_enableUrlPrefixOnInput) return;
      if (IsVizVidBusy()) return;
      ApplyPrefixToEmptyFieldNow();
    }

    public void ApplyPrefixToEmptyFieldNow()
    {
      if (!_enableUrlPrefixOnInput) return;
      if (!Utilities.IsValid(_addressUrlInputField) || VRCUrl.IsNullOrEmpty(_urlPrefix) || string.IsNullOrEmpty(_urlPrefix.Get())) return;

      VRCUrl currentUrl = _addressUrlInputField.GetUrl();
      if (VRCUrl.IsNullOrEmpty(currentUrl) || string.IsNullOrEmpty(currentUrl.Get()))
      {
        _addressUrlInputField.SetUrl(_urlPrefix);
      }
    }

    public void SetEnableUrlPrefixOnInput(bool enableUrlPrefixOnInput)
    {
      _enableUrlPrefixOnInput = enableUrlPrefixOnInput;
      UpdateUrlPrefixToggleButtonLabel();
      if (_enableUrlPrefixOnInput)
      {
        ApplyPrefixToEmptyField();
        SendCustomEventDelayedSeconds(nameof(WatchInputField), _inputWatchSeconds);
      }
      else
      {
        ClearPrefixIfOnlyPrefix();
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
      ApplyPrefixToEmptyField();
    }

    public void SetKeepPrefixWhenEmpty(bool keepPrefixWhenEmpty)
    {
      _keepPrefixWhenEmpty = keepPrefixWhenEmpty;
      if (_keepPrefixWhenEmpty) SendCustomEventDelayedSeconds(nameof(RefreshLoop), _refreshSeconds);
    }

    public void _onVideoEnd()
    {
      if (!_enableUrlPrefixOnInput) return;
      SendCustomEventDelayedSeconds(nameof(ApplyPrefixToEmptyFieldNow), 0.25f);
    }

    private bool IsInputActive(VRCUrlInputField inputField)
    {
      return Utilities.IsValid(inputField) && inputField.gameObject.activeInHierarchy;
    }

    private bool IsInputEmpty(VRCUrlInputField inputField)
    {
      if (!Utilities.IsValid(inputField)) return true;

      VRCUrl currentUrl = inputField.GetUrl();
      return VRCUrl.IsNullOrEmpty(currentUrl) || string.IsNullOrEmpty(currentUrl.Get());
    }

    private bool IsVizVidBusy()
    {
      if (!Utilities.IsValid(_core)) return false;
      if (_core.IsLoading || _core.IsPlaying || _core.IsPaused) return true;

      VRCUrl currentUrl = _core.Url;
      return !VRCUrl.IsNullOrEmpty(currentUrl) && !string.IsNullOrEmpty(currentUrl.Get());
    }

    private void ClearPrefixIfOnlyPrefix()
    {
      if (!Utilities.IsValid(_addressUrlInputField) || VRCUrl.IsNullOrEmpty(_urlPrefix)) return;

      VRCUrl currentUrl = _addressUrlInputField.GetUrl();
      if (VRCUrl.IsNullOrEmpty(currentUrl)) return;

      string current = currentUrl.Get();
      string prefix = _urlPrefix.Get();
      if (string.IsNullOrEmpty(current) || current == prefix)
      {
        _addressUrlInputField.SetUrl(VRCUrl.Empty);
      }
    }

    private void UpdateUrlPrefixToggleButtonLabel()
    {
      if (!Utilities.IsValid(_urlPrefixToggleButtonLabel)) return;
      _urlPrefixToggleButtonLabel.text = _enableUrlPrefixOnInput ? "URL Fill: On" : "URL Fill: Off";
    }
  }
}
