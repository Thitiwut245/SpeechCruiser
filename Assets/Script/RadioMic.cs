using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using Whisper;

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

    [Tooltip("RMS energy below this counts as silence. Raise it if you get phantom transcriptions.")]
    [FormerlySerializedAs("silenceRmsThreshold")]
    [SerializeField] private float SilenceRmsThreshold = 0.006f;

    [Header("Input System (PTT Key)")]
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
    private bool _legacyInputAvailable = true;
    private bool _transcribing;
    private RadioMode _currentMode = RadioMode.None;

    public event Action<string> OnPlayerSpeechUpdated;

    public string ActiveMicrophone => _activeDevice;

    private void OnEnable()  
    { 
        if (CommandPttAction != null) CommandPttAction.Enable(); 
        if (ConversationPttAction != null) ConversationPttAction.Enable(); 
    }

    private void OnDisable() 
    { 
        if (CommandPttAction != null) CommandPttAction.Disable(); 
        if (ConversationPttAction != null) ConversationPttAction.Disable(); 
    }

    private void Start()
    {
        RefreshAndSelectMicrophone();

        _captureBuffer = new float[MaxRecordDuration * SAMPLING_RATE];

        if (_whisperManager == null) _whisperManager = FindAnyObjectByType<WhisperManager>();

        if (_whisperManager == null)
        {
            Debug.LogWarning("[WHISPER]: WhisperManager not found in scene. Attach it to your GameManager.");
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
        bool cmdPressed = false, cmdReleased = false;
        bool convPressed = false, convReleased = false;

        if (Keyboard.current != null)
        {
            if (Keyboard.current.tKey.wasPressedThisFrame) cmdPressed = true;
            if (Keyboard.current.tKey.wasReleasedThisFrame) cmdReleased = true;
            if (Keyboard.current.yKey.wasPressedThisFrame) convPressed = true;
            if (Keyboard.current.yKey.wasReleasedThisFrame) convReleased = true;
        }

        if (CommandPttAction != null)
        {
            if (CommandPttAction.WasPressedThisFrame()) cmdPressed = true;
            if (CommandPttAction.WasReleasedThisFrame()) cmdReleased = true;
        }
        if (ConversationPttAction != null)
        {
            if (ConversationPttAction.WasPressedThisFrame()) convPressed = true;
            if (ConversationPttAction.WasReleasedThisFrame()) convReleased = true;
        }

        if (_legacyInputAvailable)
        {
            try
            {
                if (Input.GetKeyDown(KeyCode.T)) cmdPressed = true;
                if (Input.GetKeyUp(KeyCode.T)) cmdReleased = true;
                if (Input.GetKeyDown(KeyCode.Y)) convPressed = true;
                if (Input.GetKeyUp(KeyCode.Y)) convReleased = true;
            }
            catch (Exception)
            {
                _legacyInputAvailable = false;
            }
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

        if (_isRecording && Time.realtimeSinceStartup - _recordStartTime >= MaxRecordDuration)
        {
            Debug.Log("[RADIO]: hit the " + MaxRecordDuration + "s limit — transmitting.");
            StopRecordingAndProcess();
        }
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

    private void StartRecording(RadioMode mode)
    {
        if (_activeDevice == null)
        {
            Debug.LogError("[RADIO]: no microphone selected.");
            return;
        }

        if (_transcribing)
        {
            Debug.Log("[RADIO]: still transcribing the last transmission.");
            return;
        }

        if (_whisperManager == null)
        {
            _whisperManager = FindAnyObjectByType<WhisperManager>();
            if (_whisperManager == null)
            {
                Debug.LogError("[RADIO]: WhisperManager missing; cannot record.");
                return;
            }
        }

        ReleaseRecordingClip();

        _currentMode = mode;
        _isRecording = true;
        _recordStartTime = Time.realtimeSinceStartup;
        _recordingClip = Microphone.Start(_activeDevice, false, MaxRecordDuration, SAMPLING_RATE);

        if (TimeController.Instance != null) TimeController.Instance.SetPTTActive(true);
    }

    private async void StopRecordingAndProcess()
    {
        if (!_isRecording) return;
        
        RadioMode modeUsed = _currentMode;
        _currentMode = RadioMode.None;
        _isRecording = false;

        int sampleCount = Microphone.GetPosition(_activeDevice);
        Microphone.End(_activeDevice);

        if (_recordingClip == null || sampleCount <= 0)
        {
            Reject("(no audio captured)");
            return;
        }

        int channels = Mathf.Max(1, _recordingClip.channels);
        float seconds = (float)sampleCount / SAMPLING_RATE;
        
        if (seconds < MinSpeechSeconds)
        {
            Reject("(too short: " + seconds.ToString("0.00") + "s)");
            return;
        }

        int needed = sampleCount * channels;
        if (_captureBuffer == null || _captureBuffer.Length < needed)
        {
            _captureBuffer = new float[needed];
        }

        if (!_recordingClip.GetData(_captureBuffer, 0))
        {
            Reject("(could not read microphone buffer)");
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
            Reject("(silence: rms " + rms.ToString("0.0000") + ")");
            return;
        }

        var speech = new float[needed];
        Array.Copy(_captureBuffer, speech, needed);

        OnPlayerSpeechUpdated?.Invoke("...");

        _transcribing = true;
        try
        {
            var result = await _whisperManager.GetTextAsync(speech, SAMPLING_RATE, channels);
            string text = (result != null && result.Result != null) ? result.Result.Trim() : "";

            if (string.IsNullOrWhiteSpace(text))
            {
                Reject("(nothing intelligible)");
                return;
            }

            Debug.Log("[WHISPER]: \"" + text + "\"");
            OnPlayerSpeechUpdated?.Invoke(text);

            if (CommandRouter.Instance != null)
            {
                CommandRouter.Instance.RouteCommand(text, modeUsed == RadioMode.Conversation);
            }
            else
            {
                Debug.LogError("[RADIO]: no CommandRouter in scene.");
                if (TimeController.Instance != null) TimeController.Instance.SetPTTActive(false);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("[WHISPER]: transcription failed: " + ex.Message);
            OnPlayerSpeechUpdated?.Invoke("Transcription error.");
            if (TimeController.Instance != null) TimeController.Instance.SetPTTActive(false);
        }
        finally
        {
            _transcribing = false;
            ReleaseRecordingClip();
        }
    }

    private void Reject(string reason)
    {
        if (LogAudioStats) Debug.Log("[RADIO]: discarded " + reason);

        OnPlayerSpeechUpdated?.Invoke("(silence)");
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
