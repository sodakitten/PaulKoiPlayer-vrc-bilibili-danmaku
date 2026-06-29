using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDKBase;

namespace YamaBiliDanmakuTabletV3
{
  [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
  public class YamaBiliTabletUrlPrefixHelper3 : UdonSharpBehaviour
  {
    [Header("YamaPlayer URL Inputs")]
    [SerializeField] private VRCUrlInputField _topUrlInputField;
    [SerializeField] private VRCUrlInputField _bottomUrlInputField;

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

    private void Start()
    {
      _topInputWasActive = false;
      _bottomInputWasActive = false;
      _topInputWasEmpty = true;
      _bottomInputWasEmpty = true;
      UpdateUrlPrefixToggleButtonLabel();
      if (_enableUrlPrefixOnInput) SendCustomEventDelayedSeconds(nameof(WatchInputFields), _inputWatchSeconds);
      if (_keepPrefixWhenEmpty) SendCustomEventDelayedSeconds(nameof(RefreshLoop), _refreshSeconds);
    }

    public void WatchInputFields()
    {
      if (!_enableUrlPrefixOnInput) return;

      WatchInputField(_topUrlInputField, true);
      WatchInputField(_bottomUrlInputField, false);

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
      if (!_enableUrlPrefixOnInput) return;
      ApplyPrefixToEmptyField(_topUrlInputField);
    }

    public void ApplyPrefixToBottomInput()
    {
      if (!_enableUrlPrefixOnInput) return;
      ApplyPrefixToEmptyField(_bottomUrlInputField);
    }

    public void SetEnableUrlPrefixOnInput(bool enableUrlPrefixOnInput)
    {
      _enableUrlPrefixOnInput = enableUrlPrefixOnInput;
      UpdateUrlPrefixToggleButtonLabel();
      if (_enableUrlPrefixOnInput)
      {
        ApplyPrefixToEmptyFields();
        SendCustomEventDelayedSeconds(nameof(WatchInputFields), _inputWatchSeconds);
      }
      else
      {
        ClearPrefixIfOnlyPrefix(_topUrlInputField);
        ClearPrefixIfOnlyPrefix(_bottomUrlInputField);
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
    }

    public void SetKeepPrefixWhenEmpty(bool keepPrefixWhenEmpty)
    {
      _keepPrefixWhenEmpty = keepPrefixWhenEmpty;
      if (_keepPrefixWhenEmpty) SendCustomEventDelayedSeconds(nameof(RefreshLoop), _refreshSeconds);
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

      VRCUrl currentUrl = inputField.GetUrl();
      return VRCUrl.IsNullOrEmpty(currentUrl) || string.IsNullOrEmpty(currentUrl.Get());
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

    private void UpdateUrlPrefixToggleButtonLabel()
    {
      if (!Utilities.IsValid(_urlPrefixToggleButtonLabel)) return;
      _urlPrefixToggleButtonLabel.text = _enableUrlPrefixOnInput ? "URL Fill: On" : "URL Fill: Off";
    }
  }
}
