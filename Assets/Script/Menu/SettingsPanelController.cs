using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// Drives the shared Settings page (UI/Shared/SettingsPanel.uxml) inside any menu that
/// instances it. Reads and writes through SettingsService, so changes apply immediately.
/// The two radio keys are rebound here: click a key box, press the new key (Esc cancels).
/// </summary>
public class SettingsPanelController : IDisposable
{
    private const string NoMicrophone = "No Microphone Found";

    /// <summary>A key box is waiting for a key; other hotkeys (radio, Esc) stand down.</summary>
    public static bool IsRebinding { get; private set; }
    private static int _rebindEndFrame = -10;
    public static bool RebindEndedThisFrame => Time.frameCount - _rebindEndFrame <= 1;

    // Keys the game already uses for something else.
    private static readonly Dictionary<string, string> Reserved = new Dictionary<string, string>
    {
        { "<Keyboard>/escape", "pause" }, { "<Keyboard>/enter", "typed orders" }, { "<Keyboard>/numpadEnter", "typed orders" },
        { "<Keyboard>/tab", "the channel" }, { "<Keyboard>/m", "the chart" }, { "<Keyboard>/f1", "the radio card" },
        { "<Keyboard>/1", "channel 1" }, { "<Keyboard>/2", "channel 2" }, { "<Keyboard>/numpad1", "channel 1" },
        { "<Keyboard>/numpad2", "channel 2" }, { "<Keyboard>/f9", "the test autopilot" },
    };

    private readonly VisualElement _panel;
    private readonly DropdownField _micDropdown;
    private readonly Slider _volumeSlider;
    private readonly Slider _musicSlider;
    private readonly Slider _effectsSlider;
    private readonly Slider _radioSlider;
    private readonly Button _orderKeyButton;
    private readonly Button _talkKeyButton;
    private readonly Label _bindHint;
    private readonly Toggle _toggleToTalk;
    private readonly DropdownField _subtitleDropdown;
    private readonly Button _resetKeysButton;
    private readonly Button _backButton;
    private readonly Action _onBack;
    private float _lastRadioPreview;
    private InputActionRebindingExtensions.RebindingOperation _rebind;

    public bool IsOpen => _panel.style.display == DisplayStyle.Flex;

    private static RadioMic Mic => SettingsService.Instance != null ? SettingsService.Instance.GetComponent<RadioMic>() : null;

    public SettingsPanelController(VisualElement root, Action onBack)
    {
        _panel = Require<VisualElement>(root, "SettingsPanel");
        _micDropdown = Require<DropdownField>(root, "MicDropdown");
        _volumeSlider = Require<Slider>(root, "VolumeSlider");
        _musicSlider = Require<Slider>(root, "MusicSlider");
        _effectsSlider = Require<Slider>(root, "EffectsSlider");
        _radioSlider = Require<Slider>(root, "RadioSlider");
        _orderKeyButton = Require<Button>(root, "OrderKeyButton");
        _talkKeyButton = Require<Button>(root, "TalkKeyButton");
        _bindHint = Require<Label>(root, "BindHint");
        _toggleToTalk = Require<Toggle>(root, "ToggleToTalk");
        _subtitleDropdown = Require<DropdownField>(root, "SubtitleDropdown");
        _resetKeysButton = Require<Button>(root, "ResetKeysButton");
        _backButton = Require<Button>(root, "BackButton");
        _onBack = onBack;

        _subtitleDropdown.choices = new List<string>(SettingsService.SubtitleScaleNames);

        _backButton.clicked += _onBack;
        _orderKeyButton.clicked += RebindOrder;
        _talkKeyButton.clicked += RebindTalk;
        _resetKeysButton.clicked += ResetKeys;
        _micDropdown.RegisterValueChangedCallback(OnMicChanged);
        _volumeSlider.RegisterValueChangedCallback(OnVolumeChanged);
        _musicSlider.RegisterValueChangedCallback(OnMusicChanged);
        _effectsSlider.RegisterValueChangedCallback(OnEffectsChanged);
        _radioSlider.RegisterValueChangedCallback(OnRadioChanged);
        _toggleToTalk.RegisterValueChangedCallback(OnToggleToTalkChanged);
        _subtitleDropdown.RegisterValueChangedCallback(OnSubtitleChanged);
    }

    public void Show()
    {
        RefreshValues();
        _panel.style.display = DisplayStyle.Flex;
    }

    public void Hide()
    {
        CancelRebind();
        _panel.style.display = DisplayStyle.None;
    }

    public void Dispose()
    {
        CancelRebind();
        _backButton.clicked -= _onBack;
        _orderKeyButton.clicked -= RebindOrder;
        _talkKeyButton.clicked -= RebindTalk;
        _resetKeysButton.clicked -= ResetKeys;
        _micDropdown.UnregisterValueChangedCallback(OnMicChanged);
        _volumeSlider.UnregisterValueChangedCallback(OnVolumeChanged);
        _musicSlider.UnregisterValueChangedCallback(OnMusicChanged);
        _effectsSlider.UnregisterValueChangedCallback(OnEffectsChanged);
        _radioSlider.UnregisterValueChangedCallback(OnRadioChanged);
        _toggleToTalk.UnregisterValueChangedCallback(OnToggleToTalkChanged);
        _subtitleDropdown.UnregisterValueChangedCallback(OnSubtitleChanged);
    }

    private void RefreshValues()
    {
        var devices = new List<string>(Microphone.devices);
        if (devices.Count == 0) devices.Add(NoMicrophone);
        _micDropdown.choices = devices;

        var settings = SettingsService.Instance;
        string current = settings.MicrophoneDevice;
        _micDropdown.SetValueWithoutNotify(devices.Contains(current) ? current : devices[0]);
        _micDropdown.tooltip = _micDropdown.value;
        _volumeSlider.SetValueWithoutNotify(settings.MasterVolume);
        _musicSlider.SetValueWithoutNotify(settings.MusicVolume);
        _effectsSlider.SetValueWithoutNotify(settings.EffectsVolume);
        _radioSlider.SetValueWithoutNotify(settings.RadioVolume);
        _toggleToTalk.SetValueWithoutNotify(settings.ToggleToTalk);

        int scaleIndex = Array.IndexOf(SettingsService.SubtitleScales, settings.SubtitleScale);
        _subtitleDropdown.SetValueWithoutNotify(SettingsService.SubtitleScaleNames[Mathf.Max(0, scaleIndex)]);

        RefreshKeyNames();
    }

    private void RefreshKeyNames()
    {
        var mic = Mic;
        _orderKeyButton.text = mic != null ? mic.OrderKeyName : "T";
        _talkKeyButton.text = mic != null ? mic.TalkKeyName : "Y";
    }

    // ------------------------------------------------------------ key rebinding

    private void RebindOrder() { StartRebind(Mic?.CommandAction, Mic?.ConversationAction, _orderKeyButton); }
    private void RebindTalk() { StartRebind(Mic?.ConversationAction, Mic?.CommandAction, _talkKeyButton); }

    private void StartRebind(InputAction action, InputAction other, Button button)
    {
        if (action == null || _rebind != null) return;

        IsRebinding = true;
        button.text = "PRESS A KEY";
        button.AddToClassList("listening");
        _bindHint.text = "Press the new key now. Esc cancels.";

        action.Disable();
        _rebind = action.PerformInteractiveRebinding(RadioMic.KeyboardBindingIndex)
            .WithControlsHavingToMatchPath("<Keyboard>")
            .WithCancelingThrough("<Keyboard>/escape")
            .OnMatchWaitForAnother(0.1f)
            .OnComplete(op =>
            {
                string path = action.bindings[RadioMic.KeyboardBindingIndex].effectivePath;
                string problem = Conflict(path, other);
                if (problem != null)
                {
                    action.RemoveBindingOverride(RadioMic.KeyboardBindingIndex);
                    _bindHint.text = problem;
                }
                else
                {
                    _bindHint.text = "Saved. Click a key box to change it again.";
                    SettingsService.Instance.SaveBindings();
                }
                EndRebind(action, button);
            })
            .OnCancel(op =>
            {
                _bindHint.text = "Kept the old key.";
                EndRebind(action, button);
            })
            .Start();
    }

    private static string Conflict(string path, InputAction other)
    {
        if (path == null) return "That key can't be used.";
        if (Reserved.TryGetValue(path, out string use)) return "That key opens " + use + ". Pick another one.";
        if (other != null && other.bindings.Count > 0 && other.bindings[RadioMic.KeyboardBindingIndex].effectivePath == path)
            return "That key is already the other radio key. Pick another one.";
        return null;
    }

    private void EndRebind(InputAction action, Button button)
    {
        _rebind?.Dispose();
        _rebind = null;
        action.Enable();
        IsRebinding = false;
        _rebindEndFrame = Time.frameCount;
        button.RemoveFromClassList("listening");
        RefreshKeyNames();
    }

    private void CancelRebind()
    {
        _rebind?.Cancel();
    }

    private void ResetKeys()
    {
        var mic = Mic;
        if (mic == null) return;
        mic.CommandAction.RemoveAllBindingOverrides();
        mic.ConversationAction.RemoveAllBindingOverrides();
        SettingsService.Instance.SaveBindings();
        _bindHint.text = "Keys reset to " + mic.OrderKeyName + " and " + mic.TalkKeyName + ".";
        RefreshKeyNames();
    }

    // ------------------------------------------------------------ other settings

    private void OnToggleToTalkChanged(ChangeEvent<bool> evt)
    {
        SettingsService.Instance.SetToggleToTalk(evt.newValue);
    }

    private void OnSubtitleChanged(ChangeEvent<string> evt)
    {
        int i = Array.IndexOf(SettingsService.SubtitleScaleNames, evt.newValue);
        if (i >= 0) SettingsService.Instance.SetSubtitleScale(SettingsService.SubtitleScales[i]);
    }

    private void OnMicChanged(ChangeEvent<string> evt)
    {
        _micDropdown.tooltip = evt.newValue;
        if (evt.newValue == NoMicrophone) return;
        SettingsService.Instance.SetMicrophone(evt.newValue);
    }

    private void OnVolumeChanged(ChangeEvent<float> evt)
    {
        SettingsService.Instance.SetMasterVolume(evt.newValue);
    }

    private void OnMusicChanged(ChangeEvent<float> evt)
    {
        SettingsService.Instance.SetBusVolume(AudioBus.Music, evt.newValue);
    }

    private void OnEffectsChanged(ChangeEvent<float> evt)
    {
        SettingsService.Instance.SetBusVolume(AudioBus.Effects, evt.newValue);
    }

    private void OnRadioChanged(ChangeEvent<float> evt)
    {
        SettingsService.Instance.SetBusVolume(AudioBus.Radio, evt.newValue);
        // Let the player hear the new level (throttled while dragging).
        if (Time.unscaledTime - _lastRadioPreview < 0.25f) return;
        _lastRadioPreview = Time.unscaledTime;
        if (AudioService.Instance != null && AudioService.Instance.Sfx != null)
            AudioService.Instance.Play(AudioService.Instance.Sfx.RadioRoger, AudioBus.Radio, 0.4f);
    }

    private static T Require<T>(VisualElement root, string name) where T : VisualElement
    {
        return root.Q<T>(name) ?? throw new InvalidOperationException($"[SETTINGS]: '{name}' ({typeof(T).Name}) missing from the menu UXML.");
    }
}
