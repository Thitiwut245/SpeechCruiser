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

    private string _currentChannelName = "CH 1: CHIEF HARRIS";

    private void Awake() 
    { 
        Instance = this; 

        if (GetComponent<ShipHUD>() == null)
        {
            gameObject.AddComponent<ShipHUD>();
        }
    }

    private void Start()
    {
        InitializeUI();

        if (ShipHUD.Instance != null)
        {
            if (ShipHUD.Instance.CrewSttLogText != null) ShipHUD.Instance.CrewSttLogText.text = "";
            if (ShipHUD.Instance.CrewSubtitleText != null) ShipHUD.Instance.CrewSubtitleText.text = "";
            if (ShipHUD.Instance.CrewChannelText != null) ShipHUD.Instance.CrewChannelText.text = $"FREQ: {_currentChannelName}";
        }

        SubscribeToEvents();

        // The mission HUD belongs to sailing; the shipyard and result screens stand alone.
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnStateChanged += HandleStateChanged;
            HandleStateChanged(GameManager.Instance.CurrentState);
        }
    }

    private void HandleStateChanged(GameState state)
    {
        if (Document == null || Document.rootVisualElement == null) return;
        Document.rootVisualElement.style.display = state == GameState.Playing ? DisplayStyle.Flex : DisplayStyle.None;
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
            mic.OnRadioStatus += HandleRadioStatus;
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
        ShipHull.OnHealthChanged += HandleHealthChanged;
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
            mic.OnRadioStatus -= HandleRadioStatus;
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

        if (GameManager.Instance != null) GameManager.Instance.OnStateChanged -= HandleStateChanged;

        ShipHull.OnShipDestroyed -= HandleShipDestroyed;
        ShipHull.OnHealthChanged -= HandleHealthChanged;
    }

    public void UpdateRadioChannelDisplay(string channelName)
    {
        _currentChannelName = channelName;
        if (ShipHUD.Instance == null) return;
        
        if (ShipHUD.Instance.CrewChannelText != null)
        {
            ShipHUD.Instance.CrewChannelText.text = $"FREQ: {channelName}";
        }
        ShipHUD.Instance.SetSpeaker(channelName);
        if (ShipHUD.Instance.CrewSttLogText != null && !ShipHUD.Instance.CrewSttLogText.text.StartsWith("You:"))
        {
            ShipHUD.Instance.CrewSttLogText.text = $"[{channelName}] Radio tuned.";
        }
    }

    // Failures are shown by the radio widget in ShipHUD; only a real transcript lands here.
    private void HandleRadioStatus(RadioStatus status, string text)
    {
        if (status == RadioStatus.Heard) UpdatePlayerSpeech(text);
    }

    /// <summary>Shows what the radio heard. The crew line is left alone: a reply sets it when one is actually coming.</summary>
    public void UpdatePlayerSpeech(string speech)
    {
        if (ShipHUD.Instance == null) return;
        if (ShipHUD.Instance.CrewSttLogText != null) ShipHUD.Instance.CrewSttLogText.text = $"You: \"{speech}\"";
    }

    public void UpdateAISubtitle(string aiResponse, string crewRole)
    {
        if (ShipHUD.Instance == null) return;
        if (ShipHUD.Instance.CrewSubtitleText != null) ShipHUD.Instance.CrewSubtitleText.text = $"<b>[{crewRole}]:</b> {aiResponse}";
        ShipHUD.Instance.SetSpeaker(crewRole);
    }

    public void UpdateShipStatus(string speedStatus)
    {
        if (_statusText == null) return;
        _statusText.text = $"ENGINE: {speedStatus}";
        _statusText.RemoveFromClassList("warning");
    }

    public void HandleShipDestroyed()
    {
        SetWarning("HULL BREACHED - GAME OVER");
    }

    private void SetWarning(string text)
    {
        if (_statusText == null) return;
        _statusText.text = text;
        _statusText.AddToClassList("warning");
    }

    private void HandleHealthChanged(int currentHealth, int maxHealth)
    {
        // HUD is now rendered by ShipHUD.cs
    }

    private bool _warningShown;

    // Read every frame so the distance counts down instead of freezing at first sight.
    private void Update()
    {
        var radar = RadarSystem.Instance;
        if (radar != null && radar.IsThreat)
        {
            var t = ShipTelemetry.Capture();
            SetWarning($"{t.HazardKind} {t.HazardWhere}, {t.HazardDistanceM} m: turn {t.HazardAvoid}".ToUpperInvariant());
            _warningShown = true;
        }
        else if (_warningShown)
        {
            _warningShown = false;
            HandlePathClear();
        }
    }

    private void HandlePathClear()
    {
        var nav = ShipNavigator.Instance;
        UpdateShipStatus(nav != null ? nav.telegraphGearDisplay : "STOP");
    }
}
