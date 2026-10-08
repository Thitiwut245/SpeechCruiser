using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using Whisper;

/// <summary>What happened to a transmission. The HUD shows a plain-English hint for each.</summary>
public enum RadioStatus
{
    Recording,
    Decoding,
    Heard,
    TooShort,
    TooQuiet,
    NoAudio,
    NotUnderstood,
    Busy,
    NoMic,
    ModelNotReady,
    Cut,
    Failed,
    /// <summary>The transmission was dropped: the game paused or the scene changed mid-sentence.</summary>
    Cancelled,
    /// <summary>The radio key was pressed while the radio can't be used at all (a scene is loading, the report is up).</summary>
    NotAtSea,
    /// <summary>A radio check on the title screen or in the shipyard: the text is what was heard. Nothing is sent to the crew.</summary>
    Tested,
}

public class RadioMic : MonoBehaviour
{
    private const int SAMPLING_RATE = 16000;

    public enum RadioMode { None, Command, Conversation }

    [Header("Microphone")]
    [FormerlySerializedAs("selectedDeviceName")]
    [SerializeField] private string SelectedDeviceName = "";

    [FormerlySerializedAs("selectedDeviceIndex")]
    [SerializeField] private int SelectedDeviceIndex = 0;

    [Header("Whisper")]
    [Tooltip("WhisperManager in the scene. Found automatically if left empty.")]
    [FormerlySerializedAs("whisperManager")]
    [SerializeField] private WhisperManager _whisperManager;

    [Tooltip("Maximum push-to-talk length in seconds. Keep this in step with the WhisperManager 'Audio Ctx' setting.")]
    [Range(2, 20)]
    [FormerlySerializedAs("maxRecordDuration")]
    [SerializeField] private int MaxRecordDuration = 8;

    [Header("Silence Gates (anti-hallucination)")]
    [Tooltip("Recordings shorter than this are discarded without calling Whisper.")]
    [FormerlySerializedAs("minSpeechSeconds")]
    [SerializeField] private float MinSpeechSeconds = 0.35f;

    [Tooltip("whisper.cpp returns nothing for audio under one second, so shorter orders are padded with " +
             "silence up to this length before decoding. \"Stop!\" is usually well under a second.")]
    [SerializeField] private float MinDecodeSeconds = 1.2f;

    [Tooltip("Silence added after every recording, so a word cut off by releasing the key still decodes.")]
    [SerializeField] private float TrailingSilenceSeconds = 0.2f;

    [Tooltip("RMS energy below this counts as silence. Raise it if you get phantom transcriptions.")]
    [FormerlySerializedAs("silenceRmsThreshold")]
    [SerializeField] private float SilenceRmsThreshold = 0.006f;

    [Header("Input System (PTT keys: rebindable in Settings; gamepad RB/LB added at start)")]
    [FormerlySerializedAs("commandPttAction")]
    [SerializeField] private InputAction CommandPttAction = new InputAction("Command PTT", binding: "<Keyboard>/t");

    [FormerlySerializedAs("conversationPttAction")]
    [SerializeField] private InputAction ConversationPttAction = new InputAction("Conversation PTT", binding: "<Keyboard>/y");

    [Header("Debug")]
    [FormerlySerializedAs("logAudioStats")]
    [SerializeField] private bool LogAudioStats = true;

    private string _activeDevice;
    private AudioClip _recordingClip;
    private float[] _captureBuffer;
    private bool _isRecording;
    private float _recordStartTime;
    private int _recordingEpoch;
    private float _ignorePressesUntil;

    [Tooltip("Toggle-to-talk: after an automatic cut, radio key presses are ignored this long, so the " +
             "press the player meant as 'send' doesn't start a new message.")]
    [SerializeField] private float PressGraceAfterCut = 1.5f;
    private bool _transcribing;
    private RadioMode _currentMode = RadioMode.None;

    private const int LevelWindowSamples = SAMPLING_RATE / 10; // 100 ms
    private readonly float[] _levelBuffer = new float[LevelWindowSamples];
    private float _inputLevel;

    /// <summary>Every transmission outcome, with the text to show: the transcript for Heard, a hint otherwise.</summary>
    public event Action<RadioStatus, string> OnRadioStatus;

    public string ActiveMicrophone => _activeDevice;
    public bool IsRecording => _isRecording;
    public bool IsTranscribing => _transcribing;
    public RadioMode Mode => _currentMode;
    public int MaxRecordSeconds => MaxRecordDuration;
    public float RecordElapsed => _isRecording ? Time.realtimeSinceStartup - _recordStartTime : 0f;
    public bool HasMicrophone => !string.IsNullOrEmpty(_activeDevice);
    public bool IsModelReady => _whisperManager != null && _whisperManager.IsLoaded;
    public bool IsModelLoading => _whisperManager != null && _whisperManager.IsLoading;

    /// <summary>Live microphone loudness while recording, 0..1, for the level meter.</summary>
    public float InputLevel => _isRecording ? _inputLevel : 0f;

    public InputAction CommandAction => CommandPttAction;
    public InputAction ConversationAction => ConversationPttAction;

    /// <summary>Display name of the keyboard key for orders ("T" unless rebound).</summary>
    public string OrderKeyName => KeyName(CommandPttAction, "T");
    public string TalkKeyName => KeyName(ConversationPttAction, "Y");

    /// <summary>Keyboard binding is always index 0; the gamepad default is added after it.</summary>
    public const int KeyboardBindingIndex = 0;

    private static string KeyName(InputAction action, string fallback)
    {
        if (action == null || action.bindings.Count == 0) return fallback;
        // The physical key ("T"), not what the active layout prints on it: on a Thai layout
        // the display name of T is "ะ", which the player can't find on the key or the font can't draw.
        return KeyboardLayouts.PhysicalKeyName(action.bindings[KeyboardBindingIndex].effectivePath, fallback);
    }

    private static bool Toggle => SettingsService.Instance != null && SettingsService.Instance.ToggleToTalk;

    private void Awake()
    {
        // Gamepad defaults: right shoulder gives an order, left shoulder talks.
        AddGamepadBinding(CommandPttAction, "<Gamepad>/rightShoulder");
        AddGamepadBinding(ConversationPttAction, "<Gamepad>/leftShoulder");
    }

    private static void AddGamepadBinding(InputAction action, string path)
    {
        if (action == null) return;
        foreach (var b in action.bindings) if (b.path.StartsWith("<Gamepad>")) return;
        bool wasEnabled = action.enabled;
        if (wasEnabled) action.Disable();
        action.AddBinding(path);
        if (wasEnabled) action.Enable();
    }

    private void OnEnable()  
    { 
        if (CommandPttAction != null) CommandPttAction.Enable(); 
        if (ConversationPttAction != null) ConversationPttAction.Enable(); 
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable() 
    { 
        if (CommandPttAction != null) CommandPttAction.Disable(); 
        if (ConversationPttAction != null) ConversationPttAction.Disable(); 
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        // A decode still running keeps _transcribing true until it ends; its result is
        // dropped by the epoch check, so it can't reach the new scene.
        if (_isRecording) CancelRecording("Transmission cancelled.");

        if (CommandPttAction != null)
        {
            CommandPttAction.Disable();
            CommandPttAction.Enable();
        }
        if (ConversationPttAction != null)
        {
            ConversationPttAction.Disable();
            ConversationPttAction.Enable();
        }
    }

    private void Start()
    {
        RefreshAndSelectMicrophone();

        _captureBuffer = new float[MaxRecordDuration * SAMPLING_RATE];

        if (_whisperManager == null) _whisperManager = FindAnyObjectByType<WhisperManager>();

        if (_whisperManager == null)
        {
            Debug.LogWarning("[WHISPER]: WhisperManager not found. It belongs on Assets/Resources/Services.prefab.");
        }
        else if (LogAudioStats)
        {
            Debug.Log("[WHISPER]: ready (" + (_whisperManager.IsLoaded ? "model loaded" : "model loading") + ").");
        }
    }

    private void OnDestroy()
    {
        ReleaseRecordingClip();
    }

    private void Update()
    {
        // Pausing mid-sentence drops the transmission: nothing said in a menu reaches the crew.
        bool paused = TimeController.Instance != null && TimeController.Instance.IsPaused;
        if (paused)
        {
            if (_isRecording) CancelRecording("Transmission cancelled: the game was paused.");
            return;
        }

        bool cmdPressed = false, cmdReleased = false;
        bool convPressed = false, convReleased = false;

        // Keys come only from the (rebindable) actions. While the player types an order or
        // picks a new key, the radio stays off so the letters don't key the mic.
        bool blocked = TypedOrders.BlocksHotkeys;
        if (CommandPttAction != null && !blocked)
        {
            if (CommandPttAction.WasPressedThisFrame()) cmdPressed = true;
            if (CommandPttAction.WasReleasedThisFrame()) cmdReleased = true;
        }
        if (ConversationPttAction != null && !blocked)
        {
            if (ConversationPttAction.WasPressedThisFrame()) convPressed = true;
            if (ConversationPttAction.WasReleasedThisFrame()) convReleased = true;
        }

        // Toggle-to-talk: the second press sends; releasing the key does nothing.
        if (Toggle)
        {
            cmdReleased = _isRecording && _currentMode == RadioMode.Command && cmdPressed;
            convReleased = _isRecording && _currentMode == RadioMode.Conversation && convPressed;
            if (_isRecording) { cmdPressed = false; convPressed = false; }
        }

        if (!_isRecording && Time.realtimeSinceStartup < _ignorePressesUntil)
        {
            cmdPressed = false;
            convPressed = false;
        }

        if (!_isRecording)
        {
            if (cmdPressed) StartRecording(RadioMode.Command);
            else if (convPressed) StartRecording(RadioMode.Conversation);
        }
        else
        {
            if (_currentMode == RadioMode.Command && cmdReleased) StopRecordingAndProcess();
            else if (_currentMode == RadioMode.Conversation && convReleased) StopRecordingAndProcess();
        }

        if (_isRecording) UpdateInputLevel();

        if (_isRecording && Time.realtimeSinceStartup - _recordStartTime >= MaxRecordDuration)
        {
            Debug.Log("[RADIO]: hit the " + MaxRecordDuration + "s limit — transmitting.");
            Emit(RadioStatus.Cut, "Message cut at " + MaxRecordDuration + " seconds. Keep orders short.");
            if (Toggle) _ignorePressesUntil = Time.realtimeSinceStartup + PressGraceAfterCut;
            StopRecordingAndProcess();
        }
    }

    private void UpdateInputLevel()
    {
        if (_recordingClip == null) return;

        int position = Microphone.GetPosition(_activeDevice);
        if (position < LevelWindowSamples) return;
        if (!_recordingClip.GetData(_levelBuffer, position - LevelWindowSamples)) return;

        double sum = 0.0;
        for (int i = 0; i < _levelBuffer.Length; i++) sum += (double)_levelBuffer[i] * _levelBuffer[i];
        float rms = (float)Math.Sqrt(sum / _levelBuffer.Length);

        // Speech sits around 0.01-0.1 RMS; scale so normal talking fills most of the meter.
        float target = Mathf.Clamp01(rms * 12f);
        _inputLevel = Mathf.Lerp(_inputLevel, target, 0.5f);
    }

    private void Emit(RadioStatus status, string text)
    {
        OnRadioStatus?.Invoke(status, text);
    }

    [ContextMenu("Refresh & Log Available Microphones")]
    public void RefreshAndSelectMicrophone()
    {
        string[] devices = Microphone.devices;
        if (devices.Length == 0)
        {
            Debug.LogError("[RADIO]: no microphone devices detected!");
            _activeDevice = null;
            return;
        }

        string prefMic = SettingsService.Instance != null ? SettingsService.Instance.MicrophoneDevice : "";
        if (!string.IsNullOrEmpty(prefMic))
        {
            SelectedDeviceName = prefMic;
        }

        if (!string.IsNullOrWhiteSpace(SelectedDeviceName))
        {
            foreach (string dev in devices)
            {
                if (dev.IndexOf(SelectedDeviceName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _activeDevice = dev;
                    Debug.Log("[RADIO]: using microphone '" + _activeDevice + "'");
                    return;
                }
            }
            Debug.LogWarning("[RADIO]: no microphone matching '" + SelectedDeviceName +
                             "'. Falling back to index " + SelectedDeviceIndex + ".");
        }

        int index = Mathf.Clamp(SelectedDeviceIndex, 0, devices.Length - 1);
        _activeDevice = devices[index];
        Debug.Log("[RADIO]: using microphone [" + index + "] '" + _activeDevice + "'");
    }

    public bool SetMicrophoneDevice(string deviceName)
    {
        if (_isRecording) return false;

        foreach (string dev in Microphone.devices)
        {
            if (dev.IndexOf(deviceName, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _activeDevice = dev;
                SelectedDeviceName = dev;
                return true;
            }
        }
        return false;
    }

    public bool SetMicrophoneIndex(int index)
    {
        if (_isRecording) return false;

        string[] devices = Microphone.devices;
        if (index < 0 || index >= devices.Length) return false;

        _activeDevice = devices[index];
        SelectedDeviceIndex = index;
        return true;
    }

    public string[] GetAvailableMicrophones() 
    { 
        return Microphone.devices; 
    }

    /// <summary>Orders reach the crew only at sea.</summary>
    private static bool AtSea => GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.Playing;

    /// <summary>
    /// On the title screen and in the shipyard the key runs a radio check: the player hears
    /// whether the microphone works before sailing, and nothing is sent to the crew.
    /// </summary>
    private static bool CanTest
    {
        get
        {
            if (SceneLoader.Instance != null && SceneLoader.Instance.IsLoading) return false;
            var game = GameManager.Instance;
            if (game == null) return true; // title screen
            return game.CurrentState == GameState.Shipyard && !ShipyardManager.IsLoadingCargo;
        }
    }

    public const string TestHeardPrefix = "Radio check OK. I heard: ";

    // The transmission being recorded is a radio check, not an order.
    private bool _testing;

    /// <summary>
    /// SET SAIL ends a radio check still being recorded, so the next press at sea is an order,
    /// not the end of the check.
    /// </summary>
    public void EndRadioCheck()
    {
        if (_isRecording && _testing) CancelRecording("Radio check ended: you're setting sail.");
    }

    /// <summary>A radio check is recording or decoding. It never slows time.</summary>
    public bool IsTesting => _testing && (_isRecording || _transcribing);

    private void StartRecording(RadioMode mode)
    {
        bool test = !AtSea;
        if (test && !CanTest)
        {
            string why = ShipyardManager.IsLoadingCargo ? "Loading cargo. The radio works as soon as you're at sea."
                : GameManager.Instance != null && GameManager.Instance.CurrentState != GameState.Shipyard
                    ? "The radio works at sea. Continue to the shipyard, then SET SAIL."
                    : "One moment, the radio is changing over.";
            Emit(RadioStatus.NotAtSea, why);
            return;
        }

        if (_activeDevice == null)
        {
            Debug.LogError("[RADIO]: no microphone selected.");
            Emit(RadioStatus.NoMic, "No microphone found. Plug one in and check Settings.");
            return;
        }

        if (_transcribing)
        {
            Debug.Log("[RADIO]: still transcribing the last transmission.");
            Emit(RadioStatus.Busy, "Radio busy. Wait for your last message first.");
            return;
        }

        if (_whisperManager == null) _whisperManager = FindAnyObjectByType<WhisperManager>();
        if (_whisperManager == null || !_whisperManager.IsLoaded)
        {
            bool loading = _whisperManager != null && _whisperManager.IsLoading;
            Debug.LogWarning("[RADIO]: speech model " + (loading ? "still loading" : "not available") + "; cannot record.");
            Emit(RadioStatus.ModelNotReady, loading
                ? "Radio is still starting up. Try again in a moment."
                : "Speech system did not load. Voice orders won't work.");
            return;
        }

        ReleaseRecordingClip();

        _currentMode = mode;
        _testing = test;
        _isRecording = true;
        _inputLevel = 0f;
        _recordStartTime = Time.realtimeSinceStartup;
        _recordingEpoch = RadioEpoch.Current;
        // A second longer than the cut: a clip that fills up reads back as position 0 ("no sound").
        _recordingClip = Microphone.Start(_activeDevice, false, MaxRecordDuration + 1, SAMPLING_RATE);

        if (!test && TimeController.Instance != null) TimeController.Instance.SetPTTActive(true);
        Emit(RadioStatus.Recording, "");
    }

    private async void StopRecordingAndProcess()
    {
        if (!_isRecording) return;

        RadioMode modeUsed = _currentMode;
        bool testing = _testing;
        int epoch = _recordingEpoch;
        _currentMode = RadioMode.None;
        _isRecording = false;

        int sampleCount = Microphone.GetPosition(_activeDevice);
        Microphone.End(_activeDevice);

        if (_recordingClip == null || sampleCount <= 0)
        {
            Reject(RadioStatus.NoAudio, "No sound from the microphone. Check Settings.", "(no audio captured)");
            return;
        }

        int channels = Mathf.Max(1, _recordingClip.channels);
        float seconds = (float)sampleCount / SAMPLING_RATE;

        if (seconds < MinSpeechSeconds)
        {
            Reject(RadioStatus.TooShort, "Too short. Hold the key while you speak.", "(too short: " + seconds.ToString("0.00") + "s)");
            return;
        }

        int needed = sampleCount * channels;
        if (_captureBuffer == null || _captureBuffer.Length < needed)
        {
            _captureBuffer = new float[needed];
        }

        if (!_recordingClip.GetData(_captureBuffer, 0))
        {
            Reject(RadioStatus.NoAudio, "Could not read the microphone. Check Settings.", "(could not read microphone buffer)");
            return;
        }

        double sumSquares = 0.0;
        float peak = 0f;
        for (int i = 0; i < needed; i++)
        {
            float s = _captureBuffer[i];
            sumSquares += (double)s * s;
            float a = s < 0f ? -s : s;
            if (a > peak) peak = a;
        }
        float rms = (float)Math.Sqrt(sumSquares / needed);

        if (LogAudioStats)
        {
            Debug.Log("[RADIO]: " + seconds.ToString("0.00") + "s, rms " +
                      rms.ToString("0.0000") + ", peak " + peak.ToString("0.000"));
        }

        if (rms < SilenceRmsThreshold)
        {
            Reject(RadioStatus.TooQuiet, "Too quiet. Speak louder or move closer to the mic.", "(silence: rms " + rms.ToString("0.0000") + ")");
            return;
        }

        var speech = PadForWhisper(_captureBuffer, sampleCount, channels);

        // The samples are copied, so the clip can go now. Releasing it after the await would
        // destroy whatever newer recording had taken its place.
        ReleaseRecordingClip();

        Emit(RadioStatus.Decoding, "");

        // Only the Whisper call is guarded here, so a bug in routing is never reported
        // to the player as a speech error.
        string text;
        _transcribing = true;
        try
        {
            var result = await _whisperManager.GetTextAsync(speech, SAMPLING_RATE, channels);
            text = (result != null && result.Result != null) ? result.Result.Trim() : null;
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            _transcribing = false;
            Reject(RadioStatus.Failed, "Radio error. Try again.", "(transcription failed)");
            return;
        }
        _transcribing = false;

        // Spoken on an earlier voyage (the scene changed while Whisper worked): drop it.
        // TimeController was already reset by the scene load, so leave it alone.
        if (this == null || !RadioEpoch.IsCurrent(epoch))
        {
            Debug.Log("[RADIO]: dropped a transcript from the previous scene: \"" + text + "\"");
            return;
        }

        if (text == null)
        {
            Reject(RadioStatus.ModelNotReady, "Speech system is not ready. Try again in a moment.", "(no result from Whisper)");
            return;
        }

        if (CommandRouter.LooksLikeNoise(text))
        {
            LanguageLog.Call();
            LanguageLog.Unclear();
            Reject(RadioStatus.NotUnderstood, "Didn't catch that. Try again, short and clear.", "(nothing intelligible: \"" + text + "\")");
            return;
        }

        Debug.Log("[WHISPER]: \"" + text + "\"");
        // A check still decoding when the voyage began is dropped: the radio is for orders now.
        if (testing && AtSea)
        {
            Debug.Log("[RADIO]: radio check finished after sailing; dropped \"" + text + "\"");
            return;
        }
        if (testing)
        {
            if (TimeController.Instance != null) TimeController.Instance.SetPTTActive(false);
            Emit(RadioStatus.Tested, text);
            return;
        }
        Emit(RadioStatus.Heard, text);

        CommandRouter router = CommandRouter.Instance;
        if (router == null)
        {
            router = FindAnyObjectByType<CommandRouter>();
        }

        if (router != null)
        {
            router.RouteCommand(text, modeUsed == RadioMode.Conversation);
        }
        else
        {
            Debug.LogWarning("[RADIO]: no CommandRouter in scene. (Likely due to scene transition)");
            if (TimeController.Instance != null) TimeController.Instance.SetPTTActive(false);
        }
    }

    /// <summary>
    /// Copies the recording and pads it with silence: a trailing pad always, and up to
    /// MinDecodeSeconds for short clips, which whisper.cpp would otherwise reject as too short.
    /// </summary>
    public float[] PadForWhisper(float[] samples, int frames, int channels)
    {
        int padded = Mathf.Max(frames + Mathf.RoundToInt(TrailingSilenceSeconds * SAMPLING_RATE),
                               Mathf.CeilToInt(MinDecodeSeconds * SAMPLING_RATE));
        var speech = new float[padded * channels]; // the tail stays zero: silence
        Array.Copy(samples, speech, frames * channels);
        return speech;
    }

    /// <summary>
    /// A typed order: shown and routed exactly like a transcript, so the crew, the
    /// tutorial and the HUD treat it the same as speech.
    /// </summary>
    public void SubmitTyped(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        text = text.Trim();
        Emit(RadioStatus.Heard, text);
        var router = CommandRouter.Instance != null ? CommandRouter.Instance : FindAnyObjectByType<CommandRouter>();
        if (router != null) router.RouteCommand(text, false);
    }

    /// <summary>Stops a recording without sending it, and frees the microphone device.</summary>
    private void CancelRecording(string hint)
    {
        if (!_isRecording) return;

        _isRecording = false;
        _currentMode = RadioMode.None;
        if (_activeDevice != null) Microphone.End(_activeDevice);
        ReleaseRecordingClip();
        if (TimeController.Instance != null) TimeController.Instance.SetPTTActive(false);
        Debug.Log("[RADIO]: " + hint);
        Emit(RadioStatus.Cancelled, hint);
    }

    private void Reject(RadioStatus status, string hint, string logReason)
    {
        if (LogAudioStats) Debug.Log("[RADIO]: discarded " + logReason);

        Emit(status, hint);
        if (TimeController.Instance != null) TimeController.Instance.SetPTTActive(false);

        ReleaseRecordingClip();
    }

    private void ReleaseRecordingClip()
    {
        if (_recordingClip == null) return;

        Destroy(_recordingClip);
        _recordingClip = null;
    }
}
