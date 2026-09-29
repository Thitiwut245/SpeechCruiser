using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using System.Collections.Generic;

public class ShipHUD : MonoBehaviour
{
    public static ShipHUD Instance { get; private set; }

    [Header("Hook-up")]
    [Tooltip("The UIDocument to attach the HUD to. Defaults to one on this GameObject.")]
    [SerializeField] private UIDocument Document;

    [Header("Layout")]
    [SerializeField] private float Margin = 24f;
    [SerializeField] private float TopClearance = 84f; // Gives space for the engine status line
    [SerializeField] private float BottomClearance = 120f; // Gives space for the rudder gauge
    [SerializeField] private float RadarSize = 160f;
    [SerializeField] private float CompassWidth = 240f;
    [SerializeField] private float CompassHeight = 40f;
    [SerializeField] private float RudderWidth = 320f;
    [SerializeField] private float RudderHeight = 14f;

    /// <summary>
    /// One palette and type scale for every HUD widget, in 1080p pixels (the panel scales
    /// with the screen). Panels are near-opaque and text is never darker than TextDim,
    /// so everything stays readable over the bright, busy water.
    /// </summary>
    private static class Hud
    {
        public static readonly Color Panel = new Color(0.03f, 0.07f, 0.11f, 0.96f);
        public static readonly Color PanelRaised = new Color(0.10f, 0.16f, 0.22f, 1f);
        public static readonly Color Text = new Color(0.95f, 0.97f, 0.98f);
        public static readonly Color TextDim = new Color(0.77f, 0.82f, 0.87f);
        public static readonly Color Accent = new Color(1f, 0.78f, 0.27f);
        public static readonly Color AccentInk = new Color(0.10f, 0.08f, 0.02f);
        public static readonly Color Port = new Color(0.93f, 0.33f, 0.33f);
        public static readonly Color Starboard = new Color(0.30f, 0.80f, 0.50f);
        public static readonly Color Harris = new Color(0.86f, 0.64f, 0.25f);
        public static readonly Color Finn = new Color(0.30f, 0.76f, 0.70f);

        public const float Caption = 16f;
        public const float Body = 18f;
        public const float Subtitle = 24f;
        public const float Title = 22f;
    }

    private static void StylePanel(VisualElement e, float padding)
    {
        e.style.backgroundColor = Hud.Panel;
        e.style.paddingTop = padding;
        e.style.paddingBottom = padding;
        e.style.paddingLeft = padding + 4f;
        e.style.paddingRight = padding + 4f;
        SetRadius(e, 10f);
    }
    
    // UI Elements
    private VisualElement _radarContainer;
    private VisualElement _radarBlip;
    
    private VisualElement _compassContainer;
    private Label _compassLabel;
    
    private VisualElement _healthContainer;
    private List<VisualElement> _healthBlocks = new List<VisualElement>();
    
    private VisualElement _telegraphContainer;
    private Dictionary<ShipEngine.TelegraphGear, Label> _gearLabels = new Dictionary<ShipEngine.TelegraphGear, Label>();
    
    private VisualElement _rudderContainer;
    private VisualElement _rudderMarker;
    private Label _rudderLabel;
    private Label _portraitIcon;
    
    // Radio: what the microphone and crew are doing right now
    private VisualElement _commsPanel;
    private VisualElement _radioDot;
    private Label _radioState;
    private Label _radioHint;
    private VisualElement _radioMeter;
    private VisualElement _radioMeterFill;
    private VisualElement _radioTimer;
    private VisualElement _radioTimerFill;
    private Label _radioFlash;
    private float _flashUntil;

    private const float FlashSeconds = 3.5f;
    private const float TimerWarnSeconds = 2f;
    private static readonly Color RadioRed = new Color(1f, 0.30f, 0.24f);
    private static readonly Color RadioAmber = new Color(1f, 0.78f, 0.25f);
    private static readonly Color RadioGreen = new Color(0.30f, 0.85f, 0.45f);
    private static readonly Color RadioBlue = new Color(0.35f, 0.75f, 1f);

    private RadioMic _radioMic;

    // Crew Panel
    private VisualElement _crewPanel;
    private VisualElement _crewPortrait;
    private Label _crewChannelText;
    private Label _crewSubtitleText;
    private Label _crewSttLogText;

    public Label CrewChannelText => _crewChannelText;
    public Label CrewSubtitleText => _crewSubtitleText;
    public Label CrewSttLogText => _crewSttLogText;

    // Coach panel: the live tutorial, docked on the left so the ship stays visible
    private VisualElement _coachPanel;
    private Label _coachStep;
    private Label _coachTitle;
    private Label _coachText;
    private Label _coachTry;
    private Label _coachHint;
    private Button _coachNext;
    private Button _coachSkip;

    public event System.Action CoachNextPressed;
    public event System.Action CoachSkipPressed;

    // Highlight: a pulsing outline on the HUD part the tutorial is teaching
    private VisualElement _rudderPanel;
    private readonly Dictionary<string, VisualElement> _highlightTargets = new Dictionary<string, VisualElement>();
    private VisualElement _highlighted;

    // Radio card (F1): every order, plain English first
    private VisualElement _radioCard;
    public bool IsRadioCardOpen => _radioCard != null && _radioCard.style.display == DisplayStyle.Flex;

    private void Awake()
    {
        Instance = this;

        if (Document == null) Document = GetComponent<UIDocument>();
        if (Document == null)
        {
            Debug.LogError("[ShipHUD]: No UIDocument assigned. HUD cannot be shown.");
            enabled = false;
            return;
        }

        _radioMic = FindAnyObjectByType<RadioMic>();

        BuildHUD();

        if (_radioMic != null) _radioMic.OnRadioStatus += HandleRadioStatus;
    }

    private void OnDestroy()
    {
        // RadioMic outlives this scene, so this unsubscribe is what prevents a leak.
        if (_radioMic != null) _radioMic.OnRadioStatus -= HandleRadioStatus;
        ShipHull.OnHullHit -= HandleHullHit;
        if (SettingsService.Instance != null) SettingsService.Instance.OnControlsChanged -= ApplySettings;
    }

    // ------------------------------------------------------------ hit feedback

    private VisualElement _hitFlash;
    private float _hitFlashUntil;
    private const float HitFlashSeconds = 0.45f;

    /// <summary>A red frame around the screen that flashes on every hit.</summary>
    private void BuildHitFlash(VisualElement root)
    {
        _hitFlash = new VisualElement();
        _hitFlash.pickingMode = PickingMode.Ignore;
        _hitFlash.style.position = Position.Absolute;
        _hitFlash.style.left = 0; _hitFlash.style.right = 0; _hitFlash.style.top = 0; _hitFlash.style.bottom = 0;
        var red = new Color(0.95f, 0.12f, 0.08f, 0.75f);
        _hitFlash.style.borderTopWidth = 70f; _hitFlash.style.borderBottomWidth = 70f;
        _hitFlash.style.borderLeftWidth = 90f; _hitFlash.style.borderRightWidth = 90f;
        _hitFlash.style.borderTopColor = red; _hitFlash.style.borderBottomColor = red;
        _hitFlash.style.borderLeftColor = red; _hitFlash.style.borderRightColor = red;
        _hitFlash.style.opacity = 0f;
        root.Insert(0, _hitFlash); // behind every panel
        ShipHull.OnHullHit += HandleHullHit;
    }

    private void HandleHullHit(int containers)
    {
        _hitFlashUntil = Time.unscaledTime + HitFlashSeconds;
    }

    private void UpdateHitFeedback()
    {
        if (_hitFlash != null)
        {
            float left = _hitFlashUntil - Time.unscaledTime;
            _hitFlash.style.opacity = left > 0f ? left / HitFlashSeconds : 0f;
        }

        // Hull blocks blink while the hull can't be hit again.
        bool blink = _hull != null && _hull.IsInvulnerable && Mathf.Repeat(Time.unscaledTime * 6f, 1f) < 0.5f;
        foreach (var block in _healthBlocks) block.style.opacity = blink ? 0.25f : 1f;
    }

    private void BuildHUD()
    {
        VisualElement root = Document.rootVisualElement;

        // --- RADAR (Top Left) ---
        _radarContainer = new VisualElement();
        _radarContainer.style.position = Position.Absolute;
        _radarContainer.style.left = Margin;
        _radarContainer.style.top = TopClearance;
        _radarContainer.style.width = RadarSize;
        _radarContainer.style.height = RadarSize;
        _radarContainer.style.backgroundColor = new Color(0.02f, 0.12f, 0.06f, 0.96f);
        _radarContainer.style.borderTopLeftRadius = RadarSize / 2f;
        _radarContainer.style.borderTopRightRadius = RadarSize / 2f;
        _radarContainer.style.borderBottomLeftRadius = RadarSize / 2f;
        _radarContainer.style.borderBottomRightRadius = RadarSize / 2f;
        _radarContainer.style.borderTopWidth = 2f;
        _radarContainer.style.borderBottomWidth = 2f;
        _radarContainer.style.borderLeftWidth = 2f;
        _radarContainer.style.borderRightWidth = 2f;
        _radarContainer.style.borderTopColor = Color.green;
        _radarContainer.style.borderBottomColor = Color.green;
        _radarContainer.style.borderLeftColor = Color.green;
        _radarContainer.style.borderRightColor = Color.green;
        _radarContainer.pickingMode = PickingMode.Ignore;

        _radarBlip = new VisualElement();
        _radarBlip.style.position = Position.Absolute;
        _radarBlip.style.width = 8f;
        _radarBlip.style.height = 8f;
        _radarBlip.style.backgroundColor = Color.red;
        _radarBlip.style.borderTopLeftRadius = 4f;
        _radarBlip.style.borderTopRightRadius = 4f;
        _radarBlip.style.borderBottomLeftRadius = 4f;
        _radarBlip.style.borderBottomRightRadius = 4f;
        _radarBlip.style.display = DisplayStyle.None;
        _radarBlip.style.translate = new Translate(Length.Percent(-50f), Length.Percent(-50f));
        _radarContainer.Add(_radarBlip);
        
        // Radar center dot
        var radarCenter = new VisualElement();
        radarCenter.style.position = Position.Absolute;
        radarCenter.style.width = 4f;
        radarCenter.style.height = 4f;
        radarCenter.style.backgroundColor = Color.green;
        radarCenter.style.borderTopLeftRadius = 2f;
        radarCenter.style.borderTopRightRadius = 2f;
        radarCenter.style.borderBottomLeftRadius = 2f;
        radarCenter.style.borderBottomRightRadius = 2f;
        radarCenter.style.left = Length.Percent(50f);
        radarCenter.style.top = Length.Percent(50f);
        radarCenter.style.translate = new Translate(Length.Percent(-50f), Length.Percent(-50f));
        _radarContainer.Add(radarCenter);

        root.Add(_radarContainer);

        // --- COMPASS (Top Center) ---
        _compassContainer = new VisualElement();
        _compassContainer.style.position = Position.Absolute;
        _compassContainer.style.top = Margin;
        _compassContainer.style.left = Length.Percent(50f);
        _compassContainer.style.translate = new Translate(Length.Percent(-50f), 0f);
        _compassContainer.style.width = CompassWidth;
        _compassContainer.style.height = CompassHeight;
        _compassContainer.style.backgroundColor = Hud.Panel;
        SetRadius(_compassContainer, 10f);
        _compassContainer.style.justifyContent = Justify.Center;
        _compassContainer.style.alignItems = Align.Center;

        _compassLabel = new Label("HEADING 000°");
        _compassLabel.style.color = Hud.Text;
        _compassLabel.style.fontSize = Hud.Title;
        _compassLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        _compassContainer.Add(_compassLabel);
        root.Add(_compassContainer);

        // --- HULL INTEGRITY (Top Right) ---
        _healthContainer = new VisualElement();
        _healthContainer.style.position = Position.Absolute;
        _healthContainer.style.top = Margin;
        _healthContainer.style.right = Margin;
        _healthContainer.style.flexDirection = FlexDirection.Row;
        _healthContainer.style.alignItems = Align.Center;
        StylePanel(_healthContainer, 8f);
        
        var healthLabel = new Label("HULL");
        healthLabel.style.color = Hud.Text;
        healthLabel.style.fontSize = Hud.Body;
        healthLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        healthLabel.style.marginRight = 10f;
        _healthContainer.Add(healthLabel);

        for (int i = 0; i < 3; i++)
        {
            var block = new VisualElement();
            block.style.width = 24f;
            block.style.height = 24f;
            block.style.marginLeft = 6f;
            SetRadius(block, 4f);
            block.style.backgroundColor = Color.green;
            _healthBlocks.Add(block);
            _healthContainer.Add(block);
        }
        root.Add(_healthContainer);

        // --- ENGINE TELEGRAPH (Bottom Left) ---
        _telegraphContainer = new VisualElement();
        _telegraphContainer.style.position = Position.Absolute;
        _telegraphContainer.style.bottom = BottomClearance;
        _telegraphContainer.style.left = Margin;
        StylePanel(_telegraphContainer, 10f);
        _telegraphContainer.style.width = 180f;

        var telegraphTitle = new Label("ENGINE");
        telegraphTitle.style.fontSize = Hud.Caption;
        telegraphTitle.style.color = Hud.TextDim;
        telegraphTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
        telegraphTitle.style.unityTextAlign = TextAnchor.MiddleCenter;
        telegraphTitle.style.marginBottom = 4f;
        _telegraphContainer.Add(telegraphTitle);

        // Add gears in descending order
        var gears = new[] 
        { 
            ShipEngine.TelegraphGear.Flank,
            ShipEngine.TelegraphGear.Full,
            ShipEngine.TelegraphGear.Half,
            ShipEngine.TelegraphGear.Slow,
            ShipEngine.TelegraphGear.DeadSlow,
            ShipEngine.TelegraphGear.Stop,
            ShipEngine.TelegraphGear.SlowAstern,
            ShipEngine.TelegraphGear.FullAstern
        };

        foreach (var gear in gears)
        {
            var label = new Label(GearName(gear));
            label.style.fontSize = Hud.Body;
            label.style.color = Hud.TextDim;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.paddingTop = 2f;
            label.style.paddingBottom = 2f;
            label.style.marginTop = gear == ShipEngine.TelegraphGear.Stop || gear == ShipEngine.TelegraphGear.SlowAstern ? 4f : 0f;
            SetRadius(label, 6f);
            _telegraphContainer.Add(label);
            _gearLabels[gear] = label;
        }
        root.Add(_telegraphContainer);

        // --- RUDDER (bottom centre): label, then PORT [bar] STBD, all on one panel ---
        var rudderPanel = new VisualElement();
        _rudderPanel = rudderPanel;
        rudderPanel.style.position = Position.Absolute;
        rudderPanel.style.bottom = 16f;
        rudderPanel.style.left = Length.Percent(50f);
        rudderPanel.style.translate = new Translate(Length.Percent(-50f), 0f);
        rudderPanel.style.alignItems = Align.Center;
        StylePanel(rudderPanel, 6f);
        rudderPanel.pickingMode = PickingMode.Ignore;

        _rudderLabel = new Label("RUDDER MIDSHIPS");
        _rudderLabel.style.color = Hud.Text;
        _rudderLabel.style.fontSize = Hud.Caption;
        _rudderLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        _rudderLabel.style.whiteSpace = WhiteSpace.NoWrap;
        _rudderLabel.style.marginBottom = 6f;
        rudderPanel.Add(_rudderLabel);

        var rudderRow = new VisualElement();
        rudderRow.style.flexDirection = FlexDirection.Row;
        rudderRow.style.alignItems = Align.Center;
        rudderRow.style.paddingTop = 7f;
        rudderRow.style.paddingBottom = 7f;
        rudderPanel.Add(rudderRow);

        rudderRow.Add(SideLabel("PORT", Hud.Port));

        _rudderContainer = new VisualElement();
        _rudderContainer.style.width = RudderWidth;
        _rudderContainer.style.height = RudderHeight;
        _rudderContainer.style.backgroundColor = new Color(1f, 1f, 1f, 0.16f);
        SetRadius(_rudderContainer, RudderHeight / 2f);
        rudderRow.Add(_rudderContainer);

        rudderRow.Add(SideLabel("STBD", Hud.Starboard));

        var rudderCenterLine = new VisualElement();
        rudderCenterLine.style.position = Position.Absolute;
        rudderCenterLine.style.left = Length.Percent(50f);
        rudderCenterLine.style.top = 0f;
        rudderCenterLine.style.bottom = 0f;
        rudderCenterLine.style.width = 2f;
        rudderCenterLine.style.backgroundColor = Hud.Text;
        rudderCenterLine.style.translate = new Translate(Length.Percent(-50f), 0f);
        _rudderContainer.Add(rudderCenterLine);

        _rudderMarker = new VisualElement();
        _rudderMarker.style.position = Position.Absolute;
        _rudderMarker.style.width = 12f;
        _rudderMarker.style.height = RudderHeight + 14f;
        _rudderMarker.style.top = -7f;
        _rudderMarker.style.backgroundColor = Hud.Accent;
        SetRadius(_rudderMarker, 3f);
        _rudderMarker.style.left = Length.Percent(50f); // Default center
        _rudderMarker.style.translate = new Translate(Length.Percent(-50f), 0f);
        _rudderContainer.Add(_rudderMarker);

        root.Add(rudderPanel);

        // --- COMMS (bottom centre): radio state stacked above the crew panel ---
        _commsPanel = new VisualElement();
        _commsPanel.style.position = Position.Absolute;
        _commsPanel.style.bottom = BottomClearance;
        _commsPanel.style.left = Length.Percent(50f);
        _commsPanel.style.translate = new Translate(Length.Percent(-50f), 0f);
        _commsPanel.style.width = 640f;
        _commsPanel.pickingMode = PickingMode.Ignore;
        root.Add(_commsPanel);

        var radioPanel = new VisualElement();
        radioPanel.style.backgroundColor = Hud.Panel;
        SetRadius(radioPanel, 10f);
        radioPanel.style.paddingTop = 10f;
        radioPanel.style.paddingBottom = 10f;
        radioPanel.style.paddingLeft = 16f;
        radioPanel.style.paddingRight = 16f;
        radioPanel.style.marginBottom = 8f;
        _commsPanel.Add(radioPanel);

        var radioRow = new VisualElement();
        radioRow.style.flexDirection = FlexDirection.Row;
        radioRow.style.alignItems = Align.Center;
        radioPanel.Add(radioRow);

        _radioDot = new VisualElement();
        _radioDot.style.width = 14f;
        _radioDot.style.height = 14f;
        _radioDot.style.marginRight = 10f;
        SetRadius(_radioDot, 7f);
        radioRow.Add(_radioDot);

        _radioState = new Label("RADIO READY");
        _radioState.style.fontSize = 20f;
        _radioState.style.color = Color.white;
        _radioState.style.unityFontStyleAndWeight = FontStyle.Bold;
        _radioState.style.flexGrow = 1f;
        radioRow.Add(_radioState);

        _radioHint = new Label("");
        _radioHint.style.fontSize = 16f;
        _radioHint.style.color = new Color(0.80f, 0.86f, 0.92f);
        radioRow.Add(_radioHint);

        _radioMeter = MakeBar(8f, out _radioMeterFill);
        _radioMeter.style.marginTop = 8f;
        radioPanel.Add(_radioMeter);

        _radioTimer = MakeBar(6f, out _radioTimerFill);
        _radioTimer.style.marginTop = 6f;
        radioPanel.Add(_radioTimer);

        _radioFlash = new Label("");
        _radioFlash.style.fontSize = 18f;
        _radioFlash.style.color = RadioAmber;
        _radioFlash.style.unityFontStyleAndWeight = FontStyle.Bold;
        _radioFlash.style.marginTop = 6f;
        _radioFlash.style.whiteSpace = WhiteSpace.Normal;
        _radioFlash.style.display = DisplayStyle.None;
        radioPanel.Add(_radioFlash);

        // --- CREW INTERACTION PANEL (Middle/Bottom Center) ---
        _crewPanel = new VisualElement();
        _crewPanel.style.minHeight = 100f;
        _crewPanel.style.backgroundColor = Hud.Panel;
        _crewPanel.style.borderTopLeftRadius = 10f;
        _crewPanel.style.borderTopRightRadius = 10f;
        _crewPanel.style.borderBottomLeftRadius = 10f;
        _crewPanel.style.borderBottomRightRadius = 10f;
        _crewPanel.style.flexDirection = FlexDirection.Row;
        _crewPanel.style.paddingTop = 15f;
        _crewPanel.style.paddingBottom = 15f;
        _crewPanel.style.paddingLeft = 15f;
        _crewPanel.style.paddingRight = 15f;

        // Portrait Placeholder
        _crewPortrait = new VisualElement();
        _crewPortrait.style.width = 80f;
        _crewPortrait.style.height = 80f;
        _crewPortrait.style.backgroundColor = Hud.PanelRaised;
        _crewPortrait.style.borderTopLeftRadius = 5f;
        _crewPortrait.style.borderTopRightRadius = 5f;
        _crewPortrait.style.borderBottomLeftRadius = 5f;
        _crewPortrait.style.borderBottomRightRadius = 5f;
        _crewPortrait.style.marginRight = 15f;
        
        // Portrait Icon
        // Until real portraits exist: the speaker's initial in their colour.
        _portraitIcon = new Label("H");
        _portraitIcon.style.color = Hud.Harris;
        _portraitIcon.style.fontSize = 44f;
        _portraitIcon.style.unityFontStyleAndWeight = FontStyle.Bold;
        _portraitIcon.style.unityTextAlign = TextAnchor.MiddleCenter;
        _portraitIcon.style.flexGrow = 1;
        _crewPortrait.Add(_portraitIcon);
        
        _crewPanel.Add(_crewPortrait);

        // Text Container
        var textContainer = new VisualElement();
        textContainer.style.flexDirection = FlexDirection.Column;
        textContainer.style.flexGrow = 1;
        textContainer.style.justifyContent = Justify.SpaceBetween;
        _crewPanel.Add(textContainer);

        // Channel Info
        _crewChannelText = new Label("FREQ: CH 1: CHIEF HARRIS");
        _crewChannelText.style.color = Hud.TextDim;
        _crewChannelText.style.fontSize = Hud.Caption;
        _crewChannelText.style.unityFontStyleAndWeight = FontStyle.Bold;
        textContainer.Add(_crewChannelText);

        // Subtitle (AI Response)
        _crewSubtitleText = new Label("");
        _crewSubtitleText.style.color = Hud.Text;
        _crewSubtitleText.style.fontSize = Hud.Subtitle;
        _crewSubtitleText.style.marginTop = 5f;
        _crewSubtitleText.style.unityFontStyleAndWeight = FontStyle.Bold;
        _crewSubtitleText.style.whiteSpace = WhiteSpace.Normal;
        textContainer.Add(_crewSubtitleText);
        
        // STT Log (Player input)
        _crewSttLogText = new Label("");
        _crewSttLogText.style.color = Hud.TextDim;
        _crewSttLogText.style.fontSize = Hud.Body;
        _crewSttLogText.style.marginTop = 5f;
        _crewSttLogText.style.whiteSpace = WhiteSpace.Normal;
        textContainer.Add(_crewSttLogText);

        _commsPanel.Add(_crewPanel);

        BuildHitFlash(root);
        BuildCoachPanel(root);
        BuildRadioCard(root);
        BuildKeyStrip(root);

        _highlightTargets["radar"] = _radarContainer;
        _highlightTargets["telegraph"] = _telegraphContainer;
        _highlightTargets["rudder"] = _rudderPanel;
        _highlightTargets["radio"] = _commsPanel;
        _highlightTargets["crew"] = _crewPanel;
        _highlightTargets["hull"] = _healthContainer;
    }

    private void Update()
    {
        UpdateCompass();
        UpdateRadar();
        UpdateHull();
        UpdateTelegraph();
        UpdateRudder();
        UpdateRadio();
        UpdateHighlight();
        UpdateHitFeedback();

        if (Keyboard.current != null && !TypedOrders.BlocksHotkeys && Keyboard.current.f1Key.wasPressedThisFrame) SetRadioCardOpen(!IsRadioCardOpen);
    }

    private int _shownHeading = -1;

    private void UpdateCompass()
    {
        if (ShipNavigator.Instance != null && _compassLabel != null)
        {
            int heading = Mathf.RoundToInt(ShipNavigator.Instance.transform.eulerAngles.y) % 360;
            if (heading == _shownHeading) return;
            _shownHeading = heading;
            _compassLabel.text = $"HEADING {heading:000}°";
        }
    }

    private void UpdateRadar()
    {
        if (RadarSystem.Instance != null && _radarBlip != null)
        {
            if (RadarSystem.Instance.IsHazardDetected)
            {
                _radarBlip.style.display = DisplayStyle.Flex;
                
                float angle = RadarSystem.Instance.CurrentHazardAngle;
                float normalizedDist = Mathf.Clamp01(RadarSystem.Instance.CurrentHazardDistance / Mathf.Max(1f, RadarSystem.Instance.Range));
                
                float radius = (RadarSize / 2f) * normalizedDist;
                
                // Unity angle is clockwise from North (forward)
                float rad = (angle - 90f) * Mathf.Deg2Rad; // -90 so 0 is up
                
                float x = Mathf.Cos(rad) * radius;
                float y = Mathf.Sin(rad) * radius;

                // Center is 50%, 50%
                _radarBlip.style.left = (RadarSize / 2f) + x;
                _radarBlip.style.top = (RadarSize / 2f) + y;
            }
            else
            {
                _radarBlip.style.display = DisplayStyle.None;
            }
        }
    }

    private ShipHull _hull;
    private int _shownHealth = -1;

    private void UpdateHull()
    {
        if (_hull == null && ShipNavigator.Instance != null) _hull = ShipNavigator.Instance.GetComponentInChildren<ShipHull>();
        ShipHull hull = _hull;
        if (hull != null && _healthBlocks.Count == 3 && hull.CurrentHealth != _shownHealth)
        {
            int health = hull.CurrentHealth;
            _shownHealth = health;
            for (int i = 0; i < 3; i++)
            {
                if (i < health)
                {
                    _healthBlocks[i].style.backgroundColor = (health == 1) ? Color.red : Color.green;
                }
                else
                {
                    _healthBlocks[i].style.backgroundColor = new Color(0.2f, 0.2f, 0.2f);
                }
            }
        }
    }

    private ShipEngine.TelegraphGear? _shownGear;

    private void UpdateTelegraph()
    {
        ShipNavigator nav = ShipNavigator.Instance;
        if (nav != null && nav.Engine != null && nav.Engine.Gear != _shownGear)
        {
            ShipEngine.TelegraphGear current = nav.Engine.Gear;
            _shownGear = current;
            foreach (var kvp in _gearLabels)
            {
                bool active = kvp.Key == current;
                kvp.Value.style.backgroundColor = active ? Hud.Accent : Color.clear;
                kvp.Value.style.color = active ? Hud.AccentInk : Hud.TextDim;
                kvp.Value.style.unityFontStyleAndWeight = active ? FontStyle.Bold : FontStyle.Normal;
            }
        }
    }

    private void UpdateRudder()
    {
        ShipNavigator nav = ShipNavigator.Instance;
        if (nav != null && nav.Helm != null && _rudderMarker != null)
        {
            float maxRudder = nav.Helm.MaxAngle;
            float current = nav.Helm.CurrentRudderAngle;
            
            // map -max..max to 0..100%
            float normalized = (current + maxRudder) / (maxRudder * 2f);
            normalized = Mathf.Clamp01(normalized);
            
            _rudderMarker.style.left = Length.Percent(normalized * 100f);

            int angle = Mathf.RoundToInt(Mathf.Abs(current));
            if (_rudderLabel != null)
            {
                _rudderLabel.text = angle < 2 ? "RUDDER MIDSHIPS"
                                  : "RUDDER " + angle + "° " + (current < 0f ? "PORT (LEFT)" : "STBD (RIGHT)");
            }
        }
    }

    // ==================================================================
    // Coach panel (tutorial)
    // ==================================================================

    private void BuildCoachPanel(VisualElement root)
    {
        _coachPanel = new VisualElement();
        _coachPanel.style.position = Position.Absolute;
        _coachPanel.style.left = Margin;
        _coachPanel.style.top = TopClearance + RadarSize + 16f;
        _coachPanel.style.width = 480f;
        StylePanel(_coachPanel, 14f);
        SetBorder(_coachPanel, 2f, new Color(0.35f, 0.75f, 1f));
        _coachPanel.style.display = DisplayStyle.None;

        var header = new VisualElement();
        header.style.flexDirection = FlexDirection.Row;
        header.style.alignItems = Align.Center;
        _coachPanel.Add(header);

        _coachStep = new Label("STEP 1 OF 9");
        _coachStep.style.fontSize = Hud.Caption;
        _coachStep.style.color = Hud.TextDim;
        _coachStep.style.unityFontStyleAndWeight = FontStyle.Bold;
        _coachStep.style.flexGrow = 1f;
        header.Add(_coachStep);

        _coachSkip = MakeButton("Skip tutorial", new Color(1f, 1f, 1f, 0.14f), Hud.TextDim, Hud.Caption);
        _coachSkip.clicked += () => CoachSkipPressed?.Invoke();
        header.Add(_coachSkip);

        _coachTitle = new Label("");
        _coachTitle.style.fontSize = Hud.Title;
        _coachTitle.style.color = Hud.Accent;
        _coachTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
        _coachTitle.style.marginTop = 6f;
        _coachPanel.Add(_coachTitle);

        _coachText = new Label("");
        _coachText.style.fontSize = Hud.Body;
        _coachText.style.color = Hud.Text;
        _coachText.style.whiteSpace = WhiteSpace.Normal;
        _coachText.style.marginTop = 6f;
        _coachPanel.Add(_coachText);

        _coachTry = new Label("");
        _coachTry.style.fontSize = 20f;
        _coachTry.style.color = Hud.Text;
        _coachTry.style.unityFontStyleAndWeight = FontStyle.Bold;
        _coachTry.style.whiteSpace = WhiteSpace.Normal;
        _coachTry.style.backgroundColor = Hud.PanelRaised;
        _coachTry.style.marginTop = 10f;
        _coachTry.style.paddingTop = 8f;
        _coachTry.style.paddingBottom = 8f;
        _coachTry.style.paddingLeft = 12f;
        _coachTry.style.paddingRight = 12f;
        SetRadius(_coachTry, 6f);
        _coachPanel.Add(_coachTry);

        _coachHint = new Label("");
        _coachHint.style.fontSize = Hud.Caption;
        _coachHint.style.color = RadioAmber;
        _coachHint.style.whiteSpace = WhiteSpace.Normal;
        _coachHint.style.marginTop = 8f;
        _coachHint.style.display = DisplayStyle.None;
        _coachPanel.Add(_coachHint);

        _coachNext = MakeButton("Next", new Color(0.2f, 0.5f, 0.8f), Color.white, Hud.Body);
        _coachNext.style.marginTop = 12f;
        _coachNext.clicked += () => CoachNextPressed?.Invoke();
        _coachPanel.Add(_coachNext);

        root.Add(_coachPanel);
    }

    /// <summary>
    /// Shows one tutorial step. <paramref name="practice"/> marks the no-damage drill.
    /// <paramref name="nextLabel"/> null hides the button: the step completes by doing it.
    /// </summary>
    public void ShowCoach(int step, int total, bool practice, string title, string text, string tryLine, string nextLabel)
    {
        if (_coachPanel == null) return;

        _coachStep.text = practice
            ? "PRACTICE " + step + "/" + total + "  ·  NO DAMAGE"
            : "TUTORIAL " + step + "/" + total + "  ·  HITS COUNT";
        _coachTitle.text = title;
        _coachText.text = text;
        _coachTry.text = tryLine;
        _coachTry.style.display = string.IsNullOrEmpty(tryLine) ? DisplayStyle.None : DisplayStyle.Flex;
        _coachHint.style.display = DisplayStyle.None;
        _coachNext.text = nextLabel ?? "";
        _coachNext.style.display = nextLabel == null ? DisplayStyle.None : DisplayStyle.Flex;
        _coachPanel.style.display = DisplayStyle.Flex;
    }

    public void ShowCoachHint(string hint)
    {
        if (_coachHint == null) return;
        _coachHint.text = hint;
        _coachHint.style.display = DisplayStyle.Flex;
    }

    public void HideCoach()
    {
        if (_coachPanel != null) _coachPanel.style.display = DisplayStyle.None;
        Highlight(null);
    }

    /// <summary>Outlines one HUD part ("radio", "telegraph", "rudder", "crew", "radar", "hull"); null clears.</summary>
    public void Highlight(string key)
    {
        if (_highlighted != null) SetBorder(_highlighted, 0f, Color.clear);
        _highlighted = null;
        if (key != null) _highlightTargets.TryGetValue(key, out _highlighted);
    }

    private void UpdateHighlight()
    {
        if (_highlighted == null) return;
        float pulse = 0.35f + 0.65f * Mathf.PingPong(Time.unscaledTime * 1.6f, 1f);
        Color c = Hud.Accent;
        c.a = pulse;
        SetBorder(_highlighted, 3f, c);
    }

    // ==================================================================
    // Radio card (F1) and key strip
    // ==================================================================

    private void BuildRadioCard(VisualElement root)
    {
        _radioCard = new VisualElement();
        _radioCard.style.position = Position.Absolute;
        _radioCard.style.left = Length.Percent(50f);
        _radioCard.style.top = Length.Percent(50f);
        _radioCard.style.translate = new Translate(Length.Percent(-50f), Length.Percent(-50f));
        _radioCard.style.width = 820f;
        StylePanel(_radioCard, 18f);
        // Fully opaque: it covers the radio and subtitle panels, which must not show through.
        _radioCard.style.backgroundColor = new Color(0.03f, 0.07f, 0.11f, 1f);
        SetBorder(_radioCard, 2f, Hud.Accent);
        _radioCard.style.display = DisplayStyle.None;
        _radioCard.pickingMode = PickingMode.Ignore;

        var title = new Label("RADIO CARD · hold T and say an order · F1 to close");
        title.style.fontSize = Hud.Title;
        title.style.color = Hud.Accent;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.marginBottom = 10f;
        _radioCard.Add(title);

        var columns = new VisualElement();
        columns.style.flexDirection = FlexDirection.Row;
        _radioCard.Add(columns);

        var engine = CardColumn(columns, "CHIEF HARRIS · ENGINE", Hud.Harris);
        CardLine(engine, "\"slow ahead\"", "go forward slowly");
        CardLine(engine, "\"half ahead\" / \"full ahead\"", "go faster");
        CardLine(engine, "\"faster\" / \"slower\"", "one step up or down");
        CardLine(engine, "\"stop\"", "stop the engine (the ship drifts on)");
        CardLine(engine, "\"go back\" (slow astern)", "reverse slowly");
        CardLine(engine, "\"full astern\"", "brake hard, then reverse");

        var helm = CardColumn(columns, "FINN · STEERING", Hud.Finn);
        CardLine(helm, "\"turn left\" (port)", "turn left 30°");
        CardLine(helm, "\"turn right\" (starboard)", "turn right 30°");
        CardLine(helm, "\"hard left\" / \"hard right\"", "turn 60°, full rudder");
        CardLine(helm, "\"port 20\" / \"starboard 20\"", "turn by 20 degrees");
        CardLine(helm, "\"straight\" (midships)", "stop turning");

        var bottom = new VisualElement();
        bottom.style.flexDirection = FlexDirection.Row;
        bottom.style.marginTop = 10f;
        _radioCard.Add(bottom);

        var ask = CardColumn(bottom, "ASK", Hud.Text);
        CardLine(ask, "\"any danger?\"", "Finn reports rocks and mines");
        CardLine(ask, "\"where is the dock?\"", "direction and distance");
        CardLine(ask, "\"how much fuel?\"", "Harris reports");

        var radio = CardColumn(bottom, "RADIO", Hud.Text);
        CardLine(radio, "\"belay that\"", "undo the last order");
        CardLine(radio, "\"radio check\"", "test your microphone");
        CardLine(radio, "Y (hold)", "just talk to the crew");

        root.Add(_radioCard);
    }

    private VisualElement CardColumn(VisualElement parent, string heading, Color color)
    {
        var col = new VisualElement();
        col.style.flexGrow = 1f;
        col.style.flexBasis = 0f;
        col.style.marginRight = 16f;
        parent.Add(col);

        var h = new Label(heading);
        h.style.fontSize = Hud.Caption;
        h.style.color = color;
        h.style.unityFontStyleAndWeight = FontStyle.Bold;
        h.style.marginBottom = 4f;
        col.Add(h);
        return col;
    }

    private static void CardLine(VisualElement col, string say, string meaning)
    {
        var row = new VisualElement();
        row.style.marginBottom = 4f;

        var s = new Label(say);
        s.style.fontSize = Hud.Body;
        s.style.color = Hud.Text;
        s.style.unityFontStyleAndWeight = FontStyle.Bold;
        row.Add(s);

        var m = new Label(meaning);
        m.style.fontSize = Hud.Caption;
        m.style.color = Hud.TextDim;
        row.Add(m);

        col.Add(row);
    }

    public void SetRadioCardOpen(bool open)
    {
        if (_radioCard != null) _radioCard.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private Label _keyStrip;

    private string OrderKey => _radioMic != null ? _radioMic.OrderKeyName : "T";
    private string TalkKey => _radioMic != null ? _radioMic.TalkKeyName : "Y";
    private static bool ToggleToTalk => SettingsService.Instance != null && SettingsService.Instance.ToggleToTalk;

    /// <summary>Key names and subtitle size follow the player's settings.</summary>
    private void ApplySettings()
    {
        if (_keyStrip != null)
            _keyStrip.text = OrderKey + ": order · " + TalkKey + ": talk · Enter: type · Tab: channel · M: chart · F1: orders · Esc: pause";

        float scale = SettingsService.Instance != null ? SettingsService.Instance.SubtitleScale : 1f;
        if (_crewSubtitleText != null) _crewSubtitleText.style.fontSize = Hud.Subtitle * scale;
        if (_crewSttLogText != null) _crewSttLogText.style.fontSize = Hud.Body * scale;
    }

    private void Start()
    {
        if (SettingsService.Instance != null) SettingsService.Instance.OnControlsChanged += ApplySettings;
        ApplySettings();
    }

    private void BuildKeyStrip(VisualElement root)
    {
        var strip = new Label("");
        _keyStrip = strip;
        strip.style.position = Position.Absolute;
        strip.style.right = Margin;
        strip.style.bottom = 16f;
        strip.style.fontSize = Hud.Caption;
        strip.style.color = Hud.TextDim;
        StylePanel(strip, 6f);
        strip.pickingMode = PickingMode.Ignore;
        root.Add(strip);
    }

    private static Button MakeButton(string text, Color background, Color textColor, float fontSize)
    {
        var b = new Button { text = text };
        b.style.backgroundColor = background;
        b.style.color = textColor;
        b.style.fontSize = fontSize;
        b.style.unityFontStyleAndWeight = FontStyle.Bold;
        b.style.paddingTop = 6f;
        b.style.paddingBottom = 6f;
        b.style.paddingLeft = 12f;
        b.style.paddingRight = 12f;
        SetBorder(b, 0f, Color.clear);
        SetRadius(b, 6f);
        return b;
    }

    private static void SetBorder(VisualElement e, float width, Color color)
    {
        e.style.borderTopWidth = width;
        e.style.borderBottomWidth = width;
        e.style.borderLeftWidth = width;
        e.style.borderRightWidth = width;
        e.style.borderTopColor = color;
        e.style.borderBottomColor = color;
        e.style.borderLeftColor = color;
        e.style.borderRightColor = color;
    }

    private static string GearName(ShipEngine.TelegraphGear gear)
    {
        switch (gear)
        {
            case ShipEngine.TelegraphGear.DeadSlow: return "DEAD SLOW";
            case ShipEngine.TelegraphGear.SlowAstern: return "SLOW ASTERN";
            case ShipEngine.TelegraphGear.FullAstern: return "FULL ASTERN";
            default: return gear.ToString().ToUpperInvariant();
        }
    }

    private static Label SideLabel(string text, Color color)
    {
        var label = new Label(text);
        label.style.marginLeft = 10f;
        label.style.marginRight = 10f;
        label.style.color = color;
        label.style.fontSize = Hud.Caption;
        label.style.unityFontStyleAndWeight = FontStyle.Bold;
        return label;
    }

    /// <summary>Shows who is speaking in the crew panel: their initial in their colour.</summary>
    public void SetSpeaker(string crewDisplayName)
    {
        if (_portraitIcon == null || string.IsNullOrEmpty(crewDisplayName)) return;

        bool finn = crewDisplayName.IndexOf("FINN", System.StringComparison.OrdinalIgnoreCase) >= 0;
        _portraitIcon.text = finn ? "F" : "H";
        _portraitIcon.style.color = finn ? Hud.Finn : Hud.Harris;
    }

    private static void SetRadius(VisualElement e, float r)
    {
        e.style.borderTopLeftRadius = r;
        e.style.borderTopRightRadius = r;
        e.style.borderBottomLeftRadius = r;
        e.style.borderBottomRightRadius = r;
    }

    private static VisualElement MakeBar(float height, out VisualElement fill)
    {
        var bar = new VisualElement();
        bar.style.height = height;
        bar.style.backgroundColor = new Color(1f, 1f, 1f, 0.12f);
        bar.style.overflow = Overflow.Hidden;
        SetRadius(bar, height / 2f);

        fill = new VisualElement();
        fill.style.height = Length.Percent(100f);
        fill.style.width = Length.Percent(0f);
        SetRadius(fill, height / 2f);
        bar.Add(fill);
        return bar;
    }

    private void UpdateRadio()
    {
        if (_radioState == null) return;

        var router = CommandRouter.Instance;
        RadioChannel channel = router != null ? router.CurrentChannel : RadioChannel.ChiefEngineer;
        string crew = CrewVoice.DisplayName(channel);
        float blink = 0.55f + 0.45f * Mathf.PingPong(Time.unscaledTime * 2f, 1f);

        Color dot;
        string state, hint;
        bool recording = false;

        if (_radioMic == null || !_radioMic.HasMicrophone)
        {
            dot = RadioRed;
            state = "NO MICROPHONE";
            hint = "Plug one in, then check Settings";
        }
        else if (!_radioMic.IsModelReady)
        {
            bool loading = _radioMic.IsModelLoading;
            dot = loading ? RadioAmber * blink : RadioRed;
            state = loading ? "RADIO STARTING..." : "RADIO OFFLINE";
            hint = loading ? "Speech system loading" : "Speech system did not load";
        }
        else if (_radioMic.IsRecording)
        {
            bool order = _radioMic.Mode == RadioMic.RadioMode.Command;
            recording = true;
            dot = RadioRed * blink;
            state = order ? "SENDING ORDER" : "TALKING TO " + crew;
            hint = (ToggleToTalk ? "Press " : "Let go of ") + (order ? OrderKey : TalkKey) + " to send · " +
                   Mathf.CeilToInt(Mathf.Max(0f, _radioMic.MaxRecordSeconds - _radioMic.RecordElapsed)) + "s left";
        }
        else if (_radioMic.IsTranscribing)
        {
            dot = RadioBlue * blink;
            state = "DECODING...";
            hint = "Understanding your words";
        }
        else if (router != null && router.IsWaitingForReply)
        {
            dot = RadioBlue * blink;
            state = "WAITING FOR " + crew;
            hint = "";
        }
        else
        {
            dot = RadioGreen;
            state = "RADIO READY";
            string verb = ToggleToTalk ? "Press " : "Hold ";
            hint = verb + OrderKey + ": give order · " + verb + TalkKey + ": talk · Enter: type";
        }

        dot.a = 1f;
        _radioDot.style.backgroundColor = dot;
        _radioState.text = state;
        _radioHint.text = hint;

        _radioMeter.style.display = recording ? DisplayStyle.Flex : DisplayStyle.None;
        _radioTimer.style.display = recording ? DisplayStyle.Flex : DisplayStyle.None;
        if (recording)
        {
            _radioMeterFill.style.width = Length.Percent(_radioMic.InputLevel * 100f);
            _radioMeterFill.style.backgroundColor = RadioGreen;

            float max = Mathf.Max(1f, _radioMic.MaxRecordSeconds);
            float left = Mathf.Max(0f, max - _radioMic.RecordElapsed);
            _radioTimerFill.style.width = Length.Percent(left / max * 100f);
            _radioTimerFill.style.backgroundColor = left <= TimerWarnSeconds ? RadioAmber : RadioBlue;
        }

        if (_radioFlash.style.display == DisplayStyle.Flex && Time.unscaledTime > _flashUntil)
        {
            _radioFlash.style.display = DisplayStyle.None;
        }
    }

    private void HandleRadioStatus(RadioStatus status, string text)
    {
        switch (status)
        {
            case RadioStatus.Recording:
            case RadioStatus.Decoding:
            case RadioStatus.Heard:
                if (_radioFlash != null) _radioFlash.style.display = DisplayStyle.None;
                return;

            case RadioStatus.NoMic:
            case RadioStatus.Failed:
            case RadioStatus.ModelNotReady:
                ShowFlash(text, RadioRed);
                return;

            default:
                ShowFlash(text, RadioAmber);
                return;
        }
    }

    private void ShowFlash(string text, Color color)
    {
        if (_radioFlash == null || string.IsNullOrEmpty(text)) return;

        _radioFlash.text = text;
        _radioFlash.style.color = color;
        _radioFlash.style.display = DisplayStyle.Flex;
        _flashUntil = Time.unscaledTime + FlashSeconds;
    }
}
