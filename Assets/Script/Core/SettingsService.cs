using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Owns the player's settings (microphone, volumes, controls, subtitles): loads them from PlayerPrefs at
/// startup, applies them, and saves changes. Lives on the persistent Services prefab next to RadioMic.
/// </summary>
public class SettingsService : Singleton<SettingsService>
{
    private const string MicrophoneKey = "SelectedMicrophone";
    private const string VolumeKey = "MasterVolume";
    private const string MusicKey = "MusicVolume";
    private const string EffectsKey = "EffectsVolume";
    private const string RadioKey = "RadioVolume";
    private const string ToggleToTalkKey = "ToggleToTalk";
    private const string SubtitleScaleKey = "SubtitleScale";
    private const string PathAssistKey = "PathAssist";
    private const string OrderBindingKey = "Binding.CommandPtt";
    private const string TalkBindingKey = "Binding.ConversationPtt";

    /// <summary>Subtitle size choices, as a multiplier on the HUD's 1080p font sizes.</summary>
    public static readonly float[] SubtitleScales = { 1f, 1.25f, 1.5f };
    public static readonly string[] SubtitleScaleNames = { "Normal", "Large", "Huge" };

    public string MicrophoneDevice { get; private set; }
    public float MasterVolume { get; private set; }
    public float MusicVolume { get; private set; }
    public float EffectsVolume { get; private set; }
    public float RadioVolume { get; private set; }

    /// <summary>Press the radio key once to start talking and again to send, instead of holding it.</summary>
    public bool ToggleToTalk { get; private set; }

    /// <summary>Story mode: the helmsman quietly keeps the ship toward the channel (PathAssist). Off by default: the sea is the player's.</summary>
    public bool PathAssist { get; private set; }
    public float SubtitleScale { get; private set; }

    /// <summary>Controls or subtitle size changed; the HUD re-reads key names and sizes.</summary>
    public event Action OnControlsChanged;

    protected override void Awake()
    {
        base.Awake();
        if (Instance != this) return;

        MicrophoneDevice = PlayerPrefs.GetString(MicrophoneKey, "");
        MasterVolume = PlayerPrefs.GetFloat(VolumeKey, 1f);
        MusicVolume = PlayerPrefs.GetFloat(MusicKey, 0.7f);
        EffectsVolume = PlayerPrefs.GetFloat(EffectsKey, 1f);
        RadioVolume = PlayerPrefs.GetFloat(RadioKey, 1f);
        ToggleToTalk = PlayerPrefs.GetInt(ToggleToTalkKey, 0) == 1;
        SubtitleScale = PlayerPrefs.GetFloat(SubtitleScaleKey, 1f);
        PathAssist = PlayerPrefs.GetInt(PathAssistKey, 0) == 1;
        AudioListener.volume = MasterVolume;
    }

    // Start, so RadioMic has built its actions (and gamepad bindings) first.
    private void Start()
    {
        if (Instance != this) return;
        var mic = GetComponent<RadioMic>();
        if (mic == null) return;
        LoadOverrides(mic.CommandAction, OrderBindingKey);
        LoadOverrides(mic.ConversationAction, TalkBindingKey);
        OnControlsChanged?.Invoke();
    }

    private static void LoadOverrides(InputAction action, string key)
    {
        string json = PlayerPrefs.GetString(key, "");
        if (action != null && !string.IsNullOrEmpty(json)) action.LoadBindingOverridesFromJson(json);
    }

    /// <summary>Saves the radio keys after a rebind (or a reset).</summary>
    public void SaveBindings()
    {
        var mic = GetComponent<RadioMic>();
        if (mic == null) return;
        PlayerPrefs.SetString(OrderBindingKey, mic.CommandAction.SaveBindingOverridesAsJson());
        PlayerPrefs.SetString(TalkBindingKey, mic.ConversationAction.SaveBindingOverridesAsJson());
        PlayerPrefs.Save();
        OnControlsChanged?.Invoke();
    }

    public void SetToggleToTalk(bool toggle)
    {
        ToggleToTalk = toggle;
        PlayerPrefs.SetInt(ToggleToTalkKey, toggle ? 1 : 0);
        PlayerPrefs.Save();
        OnControlsChanged?.Invoke();
    }

    public void SetPathAssist(bool on)
    {
        PathAssist = on;
        PlayerPrefs.SetInt(PathAssistKey, on ? 1 : 0);
        PlayerPrefs.Save();
    }

    public void SetSubtitleScale(float scale)
    {
        SubtitleScale = Mathf.Clamp(scale, 1f, 2f);
        PlayerPrefs.SetFloat(SubtitleScaleKey, SubtitleScale);
        PlayerPrefs.Save();
        OnControlsChanged?.Invoke();
    }

    /// <summary>Music, effects and crew radio each have their own slider under the master volume.</summary>
    public float GetBusVolume(AudioBus bus)
    {
        switch (bus)
        {
            case AudioBus.Music: return MusicVolume;
            case AudioBus.Radio: return RadioVolume;
            default: return EffectsVolume;
        }
    }

    public void SetBusVolume(AudioBus bus, float volume)
    {
        volume = Mathf.Clamp01(volume);
        switch (bus)
        {
            case AudioBus.Music: MusicVolume = volume; PlayerPrefs.SetFloat(MusicKey, volume); break;
            case AudioBus.Radio: RadioVolume = volume; PlayerPrefs.SetFloat(RadioKey, volume); break;
            default: EffectsVolume = volume; PlayerPrefs.SetFloat(EffectsKey, volume); break;
        }
        PlayerPrefs.Save();
    }

    public void SetMasterVolume(float volume)
    {
        MasterVolume = Mathf.Clamp01(volume);
        AudioListener.volume = MasterVolume;
        PlayerPrefs.SetFloat(VolumeKey, MasterVolume);
        PlayerPrefs.Save();
    }

    public void SetMicrophone(string device)
    {
        MicrophoneDevice = device;
        PlayerPrefs.SetString(MicrophoneKey, device);
        PlayerPrefs.Save();

        var radioMic = GetComponent<RadioMic>();
        if (radioMic != null && !radioMic.SetMicrophoneDevice(device))
        {
            Debug.LogWarning($"[SETTINGS]: RadioMic could not switch to '{device}' now (recording, or device gone). It will be used next launch.");
        }
    }
}
