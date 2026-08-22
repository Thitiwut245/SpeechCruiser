using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Serialization;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance;

    [Header("UI Toolkit")]
    [Tooltip("The UIDocument containing the ShipUI layout.")]
    [FormerlySerializedAs("Document")] [SerializeField] private UIDocument Document;

    private Label _statusText;
    private Label _sttLogText;
    private Label _subtitleText;
    private Label _channelText;

    private string _currentChannelName = "CH 1: CHIEF HARRIS";

    private void Awake() 
    { 
        Instance = this; 
    }

    private void Start()
    {
        InitializeUI();

        if (_sttLogText != null) _sttLogText.text = $"[{_currentChannelName}] Radio standing by...";
        if (_subtitleText != null) _subtitleText.text = "";
        if (_channelText != null) _channelText.text = $"FREQ: {_currentChannelName}";

        SubscribeToEvents();
    }

    private void InitializeUI()
    {
        if (Document == null)
        {
            Document = GetComponent<UIDocument>();
            if (Document == null)
            {
                Debug.LogError("[UIManager]: No UIDocument found! Please assign one or attach it to this GameObject.");
                return;
            }
        }

        var root = Document.rootVisualElement;
        
        _statusText = root.Q<Label>("status-text");
        _sttLogText = root.Q<Label>("stt-log-text");
        _subtitleText = root.Q<Label>("subtitle-text");
        _channelText = root.Q<Label>("channel-text");
    }

    private void SubscribeToEvents()
    {
        ShipEngine engine = FindAnyObjectByType<ShipEngine>();
        if (engine != null)
        {
            engine.OnGearChanged += UpdateShipStatus;
        }

        CommandRouter router = FindAnyObjectByType<CommandRouter>();
        if (router != null)
        {
            router.OnChannelChanged += UpdateRadioChannelDisplay;
        }

        RadioMic mic = FindAnyObjectByType<RadioMic>();
        if (mic != null)
        {
            mic.OnPlayerSpeechUpdated += UpdatePlayerSpeech;
        }

        OpenAIClient openAI = FindAnyObjectByType<OpenAIClient>();
        if (openAI != null)
        {
            openAI.OnSubtitleUpdated += UpdateAISubtitle;
        }

        CrewVoice crewVoice = FindAnyObjectByType<CrewVoice>();
        if (crewVoice != null)
        {
            crewVoice.OnSubtitleUpdated += UpdateAISubtitle;
        }

        ShipHull.OnShipDestroyed += HandleShipDestroyed;
        RadarSystem.OnHazardDetected += HandleHazardDetected;
        RadarSystem.OnPathClear += HandlePathClear;
    }

    private void OnDestroy()
    {
        ShipEngine engine = FindAnyObjectByType<ShipEngine>();
        if (engine != null)
        {
            engine.OnGearChanged -= UpdateShipStatus;
        }

        CommandRouter router = FindAnyObjectByType<CommandRouter>();
        if (router != null)
        {
            router.OnChannelChanged -= UpdateRadioChannelDisplay;
        }

        RadioMic mic = FindAnyObjectByType<RadioMic>();
        if (mic != null)
        {
            mic.OnPlayerSpeechUpdated -= UpdatePlayerSpeech;
        }

        OpenAIClient openAI = FindAnyObjectByType<OpenAIClient>();
        if (openAI != null)
        {
            openAI.OnSubtitleUpdated -= UpdateAISubtitle;
        }

        CrewVoice crewVoice = FindAnyObjectByType<CrewVoice>();
        if (crewVoice != null)
        {
            crewVoice.OnSubtitleUpdated -= UpdateAISubtitle;
        }

        ShipHull.OnShipDestroyed -= HandleShipDestroyed;
        RadarSystem.OnHazardDetected -= HandleHazardDetected;
        RadarSystem.OnPathClear -= HandlePathClear;
    }

    public void UpdateRadioChannelDisplay(string channelName)
    {
        _currentChannelName = channelName;
        if (_channelText != null)
        {
            _channelText.text = $"FREQ: {channelName}";
        }
        if (_sttLogText != null && !_sttLogText.text.StartsWith("You:"))
        {
            _sttLogText.text = $"[{channelName}] Radio tuned.";
        }
    }

    public void UpdatePlayerSpeech(string speech)
    {
        if (_sttLogText != null) _sttLogText.text = $"[{_currentChannelName}] You: \"{speech}\"";
        if (_subtitleText != null) _subtitleText.text = "...";
    }

    public void UpdateAISubtitle(string aiResponse, string crewRole)
    {
        if (_subtitleText != null) _subtitleText.text = $"<b>[{crewRole}]:</b> {aiResponse}";
    }

    public void UpdateShipStatus(string speedStatus)
    {
        if (_statusText != null) _statusText.text = $"ENGINE: {speedStatus}";
    }

    public void HandleShipDestroyed()
    {
        if (_statusText != null) 
        {
            _statusText.text = "HULL BREACHED - GAME OVER";
            _statusText.style.color = new StyleColor(Color.red);
        }
    }

    private void HandleHazardDetected(float distance)
    {
        if (_statusText != null)
        {
            _statusText.text = $"WARNING: REEF AHEAD ({distance:F0}m)";
            _statusText.style.color = new StyleColor(Color.red);
        }
    }

    private void HandlePathClear()
    {
        if (_statusText != null)
        {
            _statusText.text = "ENGINE: AHEAD";
            _statusText.style.color = new StyleColor(Color.white);
        }
    }
}
