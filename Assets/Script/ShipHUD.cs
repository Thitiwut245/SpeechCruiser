using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using System.Collections.Generic;

/// <summary>
/// The sailing HUD, in the game's Alto-style look (UiTheme): thin white type and hairline
/// instruments straight over the sea, with dusk gradients at the top and bottom edges:
///   top centre    one bar: engine order and speed, a compass tape, the hull
///                 (with a red ribbon under it when a hazard is on course)
///   top left      the radar chart, bow up: channel, reefs, mines, dock, rescue, wake
///   bottom left   the engine telegraph (click a gear to order it) and the rudder arc
///   bottom centre "Try saying" phrase chips, the radio state, and the crew's line with
///                 the speaker's portrait
///   top right     HELP (the order card, also F1) and PAUSE (Esc), as words
/// Also the tutorial coach panel, the F1 radio card and the red hit flash. Everything is
/// built in code on the ShipUI document.
/// </summary>
public class ShipHUD : MonoBehaviour
{
    public static ShipHUD Instance { get; private set; }

    [Header("Hook-up")]
    [Tooltip("The UIDocument to attach the HUD to. Defaults to one on this GameObject.")]
    [SerializeField] private UIDocument Document;

    [Header("Layout")]
    [SerializeField] private float Margin = 24f;
    [SerializeField] private float ChartSize = 230f;

    /// <summary>Type sizes in 1080p pixels (the panel scales with the screen).</summary>
    private static class Hud
    {
        public static readonly Color Panel = HudLook.Glass;
        public static readonly Color PanelRaised = new Color(0.10f, 0.16f, 0.22f, 1f);
        public static readonly Color Text = HudLook.Text;
        public static readonly Color TextDim = HudLook.TextDim;
        public static readonly Color Accent = HudLook.Amber;
        public static readonly Color AccentInk = new Color(0.10f, 0.08f, 0.02f);
        public static readonly Color Harris = new Color(0.86f, 0.64f, 0.25f);
        public static readonly Color Finn = new Color(0.30f, 0.76f, 0.70f);

        public const float Caption = 16f;
        public const float Body = 18f;
        public const float Subtitle = 24f;
        public const float Title = 22f;
    }

    private static void StylePanel(VisualElement e, float padding) => HudLook.Panel(e, padding);

    // Top bar
    private VisualElement _topBar;
    private Label _gearText;
    private Label _speedText;
    private CompassTape _compass;
    private Label _headingText;
    private VisualElement _hullBox;
    private readonly List<VisualElement> _healthBlocks = new List<VisualElement>();
    private Label _warning;

    // Instruments
    private ChartScope _chart;
    private VisualElement _helmPanel;
    private TelegraphDial _telegraph;
    private RudderArc _rudder;

    // Radio: what the microphone and crew are doing right now
    private VisualElement _commsPanel;
    private VisualElement _radioPanel;
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

    // "Try saying"
    private VisualElement _hintsRow;
    private readonly List<string> _hints = new List<string>();
    private float _nextHints;

    // Crew line
    private VisualElement _crewPanel;
    private VisualElement _crewPortrait;
    private Label _crewName;
    private Label _crewChannelText;
    private Label _crewSubtitleText;
    private Label _crewSttLogText;
    private CharacterId _speaker = CharacterId.Harris;
    private bool _mouthOpen;
    private float _nextFlap;

    /// <summary>Which crew member the radio is tuned to (orders go to them), e.g. "FREQ: CH 1: CHIEF HARRIS".</summary>
    public Label CrewChannelText => _crewChannelText;
    public Label CrewSubtitleText => _crewSubtitleText;
    public Label CrewSttLogText => _crewSttLogText;

    // Coach panel: the live tutorial, docked on the left so the ship stays visible
    private VisualElement _coachPanel;
    private VisualElement _coachScrim;
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
    private readonly Dictionary<string, VisualElement> _highlightTargets = new Dictionary<string, VisualElement>();
    private VisualElement _highlighted;

    // Radio card (F1): every order, plain English first
    private VisualElement _radioCard;
    public bool IsRadioCardOpen => _radioCard != null && _radioCard.style.display == DisplayStyle.Flex;

    // Chart data
    private readonly List<ChartScope.Blip> _blips = new List<ChartScope.Blip>();
    private readonly List<Vector3> _trail = new List<Vector3>();
    private float _nextTrail;
    private RescueTarget _rescue;
    private float _nextRescueLook;

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
        if (LevelGenerator.Instance != null)
        {
            LevelGenerator.Instance.OnLevelGenerated -= RefreshChartWorld;
            LevelGenerator.Instance.OnHazardCleared -= RefreshChartWorld;
        }
    }

    private void Start()
    {
        if (SettingsService.Instance != null) SettingsService.Instance.OnControlsChanged += ApplySettings;
        ApplySettings();

        // The old engine/warning line from ShipUI.uxml: the top bar does that job now.
        var oldStatus = Document.rootVisualElement.Q<Label>("status-text");
        if (oldStatus != null) oldStatus.style.display = DisplayStyle.None;

        if (LevelGenerator.Instance != null)
        {
            LevelGenerator.Instance.OnLevelGenerated += RefreshChartWorld;
            LevelGenerator.Instance.OnHazardCleared += RefreshChartWorld;
            if (LevelGenerator.Instance.IsLevelReady) RefreshChartWorld();
        }

        // Render the crew's faces now, in the shipyard, not mid-voyage.
        PortraitStudio.Get(CharacterId.Finn);
        PortraitStudio.Get(CharacterId.Harris);
        SetSpeaker("CHIEF HARRIS");
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

    // ------------------------------------------------------------ building

    private void BuildHUD()
    {
        VisualElement root = Document.rootVisualElement;
        UiTheme.Apply(root, overSea: true);
        // Dusk at the top and bottom edges, and on the left while the coach is up: calm
        // ground for the white text over the bright sea. No boxes.
        root.Add(UiTheme.Scrim(true, 230f, 0.85f));
        root.Add(UiTheme.Scrim(false, 400f, 0.9f));
        _coachScrim = UiTheme.SideScrim(760f, 0.7f);
        _coachScrim.style.display = DisplayStyle.None;
        root.Add(_coachScrim);

        BuildTopBar(root);
        BuildChart(root);
        BuildHelm(root);
        BuildComms(root);
        BuildHitFlash(root);
        BuildCoachPanel(root);
        BuildRadioCard(root);
        BuildKeyStrip(root);

        _highlightTargets["radar"] = _chart;
        _highlightTargets["telegraph"] = _helmPanel;
        _highlightTargets["rudder"] = _helmPanel;
        _highlightTargets["radio"] = _radioPanel;
        _highlightTargets["crew"] = _crewPanel;
        _highlightTargets["hull"] = _hullBox;
    }

    private void BuildTopBar(VisualElement root)
    {
        var holder = new VisualElement();
        holder.style.position = Position.Absolute;
        holder.style.top = 16f;
        holder.style.left = 0; holder.style.right = 0;
        holder.style.alignItems = Align.Center;
        holder.pickingMode = PickingMode.Ignore;
        root.Add(holder);

        _topBar = new VisualElement();
        _topBar.style.flexDirection = FlexDirection.Row;
        _topBar.style.alignItems = Align.Center;
        StylePanel(_topBar, 8f);
        _topBar.pickingMode = PickingMode.Ignore;
        holder.Add(_topBar);

        // Engine order and speed.
        var engine = new VisualElement();
        engine.style.width = 170f;
        engine.style.alignItems = Align.Center;
        engine.Add(HudLook.Words("ENGINE", 12f, HudLook.Brass, true));
        _gearText = HudLook.Words("STOP", 20f, HudLook.Amber, true);
        engine.Add(_gearText);
        _speedText = HudLook.Words("0 kn", 14f, HudLook.TextDim);
        engine.Add(_speedText);
        _topBar.Add(engine);
        _topBar.Add(Divider());

        // The compass.
        var compass = new VisualElement();
        compass.style.alignItems = Align.Center;
        compass.style.marginLeft = 8f; compass.style.marginRight = 8f;
        _compass = new CompassTape();
        compass.Add(_compass);
        _headingText = HudLook.Words("HEADING 000° N", 15f, HudLook.Text, true);
        _headingText.style.marginTop = 2f;
        compass.Add(_headingText);
        _topBar.Add(compass);
        _topBar.Add(Divider());

        // The hull.
        _hullBox = new VisualElement();
        _hullBox.style.width = 150f;
        _hullBox.style.alignItems = Align.Center;
        _hullBox.Add(HudLook.Words("HULL", 12f, HudLook.Brass, true));
        var pips = new VisualElement();
        pips.style.flexDirection = FlexDirection.Row;
        pips.style.marginTop = 6f;
        for (int i = 0; i < 5; i++) // up to two more with hull plating; the unused ones hide
        {
            var block = new VisualElement();
            block.style.width = 14f;
            block.style.height = 14f;
            block.style.marginLeft = 4f; block.style.marginRight = 4f;
            HudLook.Radius(block, 7f);
            HudLook.Border(block, 1.5f, UiTheme.White);
            block.style.backgroundColor = UiTheme.White;
            _healthBlocks.Add(block);
            pips.Add(block);
        }
        _hullBox.Add(pips);
        _topBar.Add(_hullBox);

        // A hazard on course: a red ribbon under the bar, in plain words.
        _warning = HudLook.Words("", 19f, Color.white, true);
        _warning.style.marginTop = 6f;
        _warning.style.color = UiTheme.Coral;
        _warning.style.fontSize = 21f;
        _warning.style.letterSpacing = 3f;
        _warning.style.paddingBottom = 4f;
        _warning.style.borderBottomWidth = 2f;
        _warning.style.borderBottomColor = UiTheme.Coral;
        _warning.style.display = DisplayStyle.None;
        holder.Add(_warning);
    }

    private static VisualElement Divider()
    {
        var d = new VisualElement();
        d.style.width = 2f;
        d.style.height = 46f;
        d.style.backgroundColor = new Color(HudLook.BrassDim.r, HudLook.BrassDim.g, HudLook.BrassDim.b, 0.6f);
        d.style.marginLeft = 8f; d.style.marginRight = 8f;
        return d;
    }

    private void BuildChart(VisualElement root)
    {
        var holder = new VisualElement();
        holder.style.position = Position.Absolute;
        holder.style.left = Margin;
        holder.style.top = 16f;
        holder.style.alignItems = Align.Center;
        holder.pickingMode = PickingMode.Ignore;
        _chart = new ChartScope(ChartSize);
        holder.Add(_chart);
        var caption = HudLook.Words("RADAR · bow up", 12f, HudLook.Brass, true);
        caption.style.marginTop = 4f;
        caption.style.paddingTop = 2f;
        holder.Add(caption);
        root.Add(holder);
    }

    private void BuildHelm(VisualElement root)
    {
        _helmPanel = new VisualElement();
        _helmPanel.style.position = Position.Absolute;
        _helmPanel.style.left = Margin;
        _helmPanel.style.bottom = 16f;
        _helmPanel.style.alignItems = Align.Center;
        StylePanel(_helmPanel, 8f);
        HudLook.Radius(_helmPanel, 16f);

        _telegraph = new TelegraphDial();
        _telegraph.GearClicked += OrderGearByClick;
        _helmPanel.Add(_telegraph);
        _rudder = new RudderArc();
        _rudder.style.marginTop = 4f;
        _helmPanel.Add(_rudder);
        root.Add(_helmPanel);
    }

    /// <summary>A gear clicked on the telegraph: ordered at once, and the words to say it next time.</summary>
    private void OrderGearByClick(ShipEngine.TelegraphGear gear)
    {
        var nav = ShipNavigator.Instance;
        if (nav == null || GameManager.Instance == null || GameManager.Instance.CurrentState != GameState.Playing) return;
        nav.SetGear(gear);
        ShowRadioNote("Next time, say it on the radio: \"" + SayFor(gear) + "\"");
    }

    private static string SayFor(ShipEngine.TelegraphGear gear)
    {
        switch (gear)
        {
            case ShipEngine.TelegraphGear.FullAstern: return "full astern";
            case ShipEngine.TelegraphGear.SlowAstern: return "slow astern";
            case ShipEngine.TelegraphGear.DeadSlow: return "dead slow";
            case ShipEngine.TelegraphGear.Slow: return "slow ahead";
            case ShipEngine.TelegraphGear.Half: return "half ahead";
            case ShipEngine.TelegraphGear.Full: return "full ahead";
            case ShipEngine.TelegraphGear.Flank: return "flank speed";
            default: return "stop";
        }
    }

    private void BuildComms(VisualElement root)
    {
        _commsPanel = new VisualElement();
        _commsPanel.style.position = Position.Absolute;
        _commsPanel.style.bottom = 16f;
        _commsPanel.style.left = Length.Percent(50f);
        _commsPanel.style.translate = new Translate(Length.Percent(-50f), 0f);
        _commsPanel.style.width = 660f;
        _commsPanel.pickingMode = PickingMode.Ignore;
        root.Add(_commsPanel);

        // The crew's line, with the speaker's face.
        _crewPanel = new VisualElement();
        _crewPanel.style.flexDirection = FlexDirection.Row;
        _crewPanel.style.alignItems = Align.Center;
        StylePanel(_crewPanel, 10f);
        _crewPanel.style.marginBottom = 8f;
        _crewPanel.style.display = DisplayStyle.None;

        _crewPortrait = new VisualElement();
        _crewPortrait.style.width = 76f;
        _crewPortrait.style.height = 76f;
        _crewPortrait.style.flexShrink = 0f;
        _crewPortrait.style.marginRight = 14f;
        _crewPortrait.style.backgroundColor = new Color(0f, 0f, 0f, 0.2f);
        HudLook.Radius(_crewPortrait, 38f); // a round portrait in a thin ring
        HudLook.Border(_crewPortrait, 1.5f, Hud.Harris);
        _crewPanel.Add(_crewPortrait);

        var text = new VisualElement();
        text.style.flexGrow = 1f;
        text.style.flexShrink = 1f;
        _crewName = HudLook.Words("CHIEF HARRIS", 15f, Hud.Harris, true);
        text.Add(_crewName);
        _crewSubtitleText = HudLook.Words("", Hud.Subtitle, Hud.Text, true);
        _crewSubtitleText.style.whiteSpace = WhiteSpace.Normal;
        _crewSubtitleText.style.marginTop = 2f;
        text.Add(_crewSubtitleText);
        _crewSttLogText = HudLook.Words("", Hud.Body, Hud.TextDim);
        _crewSttLogText.style.whiteSpace = WhiteSpace.Normal;
        _crewSttLogText.style.marginTop = 4f;
        text.Add(_crewSttLogText);
        _crewPanel.Add(text);
        _commsPanel.Add(_crewPanel);

        // "Try saying": phrases for the moment. Click one to hear it.
        _hintsRow = new VisualElement();
        _hintsRow.style.flexDirection = FlexDirection.Row;
        _hintsRow.style.alignItems = Align.Center;
        _hintsRow.style.justifyContent = Justify.Center;
        _hintsRow.style.marginBottom = 6f;
        _commsPanel.Add(_hintsRow);

        // The radio's state.
        _radioPanel = new VisualElement();
        StylePanel(_radioPanel, 8f);
        _commsPanel.Add(_radioPanel);

        var radioRow = new VisualElement();
        radioRow.style.flexDirection = FlexDirection.Row;
        radioRow.style.alignItems = Align.Center;
        _radioPanel.Add(radioRow);

        _radioDot = new VisualElement();
        _radioDot.style.width = 14f;
        _radioDot.style.height = 14f;
        _radioDot.style.marginRight = 10f;
        HudLook.Radius(_radioDot, 7f);
        radioRow.Add(_radioDot);

        _radioState = HudLook.Words("RADIO READY", 18f, Color.white, true);
        _radioState.style.flexGrow = 1f;
        radioRow.Add(_radioState);

        // Who the orders go to.
        _crewChannelText = HudLook.Words("FREQ: CH 1: CHIEF HARRIS", 13f, HudLook.Brass, true);
        radioRow.Add(_crewChannelText);

        _radioHint = HudLook.Words("", 14f, new Color(0.80f, 0.86f, 0.92f));
        _radioHint.style.marginTop = 3f;
        _radioPanel.Add(_radioHint);

        _radioMeter = MakeBar(8f, out _radioMeterFill);
        _radioMeter.style.marginTop = 6f;
        _radioPanel.Add(_radioMeter);

        _radioTimer = MakeBar(6f, out _radioTimerFill);
        _radioTimer.style.marginTop = 5f;
        _radioPanel.Add(_radioTimer);

        _radioFlash = HudLook.Words("", 17f, RadioAmber, true);
        _radioFlash.style.marginTop = 5f;
        _radioFlash.style.whiteSpace = WhiteSpace.Normal;
        _radioFlash.style.display = DisplayStyle.None;
        _radioPanel.Add(_radioFlash);
    }

    // ------------------------------------------------------------ every frame

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        UpdateTopBar();
        UpdateChart(dt);
        UpdateHull();
        UpdateHelm(dt);
        UpdateRadio();
        UpdateCrewPanel();
        UpdateHints();
        UpdateHighlight();
        UpdateHitFeedback();

        if (Keyboard.current != null && !TypedOrders.BlocksHotkeys && Keyboard.current.f1Key.wasPressedThisFrame) SetRadioCardOpen(!IsRadioCardOpen);
    }

    private int _shownHeading = -1;

    private void UpdateTopBar()
    {
        var nav = ShipNavigator.Instance;
        if (nav == null) return;

        float heading = nav.transform.eulerAngles.y;
        _compass.Set(heading);
        int h = Mathf.RoundToInt(heading) % 360;
        if (h != _shownHeading)
        {
            _shownHeading = h;
            _headingText.text = $"HEADING {h:000}°  " + ShipTelemetry.CompassPoint(heading);
        }

        if (nav.Engine != null)
        {
            _gearText.text = GearName(nav.Engine.Gear);
            _gearText.style.color = nav.Engine.Gear < 0 ? HudLook.Port : nav.Engine.Gear == ShipEngine.TelegraphGear.Stop ? HudLook.Text : HudLook.Amber;
            _speedText.text = Mathf.Abs(nav.currentSpeed).ToString("0.0") + " kn" + (nav.currentSpeed < -0.05f ? " astern" : "");
        }

        // A hazard on course: say what, where, how far, and which way to turn.
        var radar = RadarSystem.Instance;
        if (radar != null && radar.IsThreat)
        {
            var t = ShipTelemetry.Capture();
            string avoid = t.HazardAvoid.StartsWith("left") ? "LEFT" : "RIGHT";
            _warning.text = $"{t.HazardKind.ToUpperInvariant()} {t.HazardWhere.ToUpperInvariant()} · {t.HazardDistanceM} m · TURN {avoid}";
            _warning.style.display = DisplayStyle.Flex;
        }
        else _warning.style.display = DisplayStyle.None;
    }

    private void RefreshChartWorld()
    {
        _blips.Clear();
        foreach (var c in FindObjectsByType<Collider>(FindObjectsSortMode.None))
        {
            if (!c.enabled || !c.CompareTag("Hazard")) continue;
            if (c.name.StartsWith("Shore")) continue; // the coast is drawn by the land itself, not a row of dots
            _blips.Add(new ChartScope.Blip { Position = c.bounds.center, Mine = RadarSystem.KindOf(c) == "Mine" });
        }
        _trail.Clear();
        var level = LevelGenerator.Instance;
        if (_chart != null) _chart.SetWorld(_blips, level != null ? level.SolutionWorldPath : null);
    }

    private void UpdateChart(float dt)
    {
        var nav = ShipNavigator.Instance;
        var level = LevelGenerator.Instance;
        if (nav == null || _chart == null) return;

        Vector3 ship = nav.transform.position;
        if (Time.unscaledTime >= _nextTrail)
        {
            _nextTrail = Time.unscaledTime + 0.6f;
            if (_trail.Count == 0 || (_trail[_trail.Count - 1] - ship).sqrMagnitude > 4f) _trail.Add(ship);
            if (_trail.Count > 60) _trail.RemoveAt(0);
        }
        if (Time.unscaledTime >= _nextRescueLook)
        {
            _nextRescueLook = Time.unscaledTime + 1f;
            _rescue = FindAnyObjectByType<RescueTarget>();
        }
        Vector3? rescue = _rescue != null && !_rescue.Boarded ? _rescue.transform.position : (Vector3?)null;
        Vector3? dock = level != null && level.IsLevelReady ? level.EndPosition : (Vector3?)null;
        if (RadarSystem.Instance != null) _chart.RangeMetres = Mathf.Max(90f, RadarSystem.Instance.Range * 1.4f);
        _chart.Set(ship, nav.transform.eulerAngles.y, _trail, dock, rescue, dt);
    }

    private ShipHull _hull;
    private int _shownHealth = -1, _shownMax = -1;

    private void UpdateHull()
    {
        if (_hull == null && ShipNavigator.Instance != null) _hull = ShipNavigator.Instance.GetComponentInChildren<ShipHull>();
        ShipHull hull = _hull;
        if (hull != null && _healthBlocks.Count == 5 && (hull.CurrentHealth != _shownHealth || hull.MaxHealthAmount != _shownMax))
        {
            int health = hull.CurrentHealth;
            _shownHealth = health;
            _shownMax = hull.MaxHealthAmount;
            for (int i = 0; i < 5; i++)
            {
                _healthBlocks[i].style.display = i < _shownMax ? DisplayStyle.Flex : DisplayStyle.None;
                bool full = i < health;
                Color c = health == 1 ? UiTheme.Coral : UiTheme.White;
                _healthBlocks[i].style.backgroundColor = full ? c : new Color(0f, 0f, 0f, 0f);
                HudLook.Border(_healthBlocks[i], 1.5f, full ? c : UiTheme.Dim);
            }
        }
    }

    private ShipEngine.TelegraphGear? _shownGear;

    private void UpdateHelm(float dt)
    {
        ShipNavigator nav = ShipNavigator.Instance;
        if (nav == null) return;
        if (nav.Engine != null && nav.Engine.Gear != _shownGear)
        {
            _shownGear = nav.Engine.Gear;
            _telegraph.SetGear(nav.Engine.Gear);
        }
        _telegraph.Tick(dt);
        if (nav.Helm != null)
        {
            float max = Mathf.Max(1f, nav.Helm.MaxAngle);
            _rudder.Set(nav.Helm.CurrentRudderAngle / max, nav.Helm.CurrentRudderAngle);
        }
    }

    // ------------------------------------------------------------ crew line and hints

    /// <summary>Shows who is speaking in the crew panel: their face, name and colour.</summary>
    public void SetSpeaker(string crewDisplayName)
    {
        if (_crewName == null || string.IsNullOrEmpty(crewDisplayName)) return;
        bool finn = crewDisplayName.IndexOf("FINN", System.StringComparison.OrdinalIgnoreCase) >= 0;
        _speaker = finn ? CharacterId.Finn : CharacterId.Harris;
        Color c = finn ? Hud.Finn : Hud.Harris;
        _crewName.text = finn ? "LOOKOUT FINN" : "CHIEF HARRIS";
        _crewName.style.color = c;
        HudLook.Border(_crewPortrait, 1.5f, c);
        SetPortrait(false);
    }

    private void SetPortrait(bool open)
    {
        _mouthOpen = open;
        var tex = PortraitStudio.Get(_speaker, open) as RenderTexture;
        _crewPortrait.style.backgroundImage = tex != null ? Background.FromRenderTexture(tex) : new StyleBackground(StyleKeyword.None);
    }

    private void UpdateCrewPanel()
    {
        bool any = !string.IsNullOrEmpty(_crewSubtitleText.text) || !string.IsNullOrEmpty(_crewSttLogText.text);
        _crewPanel.style.display = any ? DisplayStyle.Flex : DisplayStyle.None;
        if (!any) return;

        // The speaker's mouth moves while they are on the air.
        bool talking = AudioService.Instance != null && AudioService.Instance.IsChattering;
        if (talking && Time.unscaledTime >= _nextFlap)
        {
            _nextFlap = Time.unscaledTime + 0.11f * Random.Range(0.7f, 1.4f);
            SetPortrait(!_mouthOpen);
        }
        else if (!talking && _mouthOpen) SetPortrait(false);
    }

    private void UpdateHints()
    {
        if (Time.unscaledTime < _nextHints || _hintsRow == null) return;
        _nextHints = Time.unscaledTime + 0.5f;

        var nav = ShipNavigator.Instance;
        if (nav == null || nav.Engine == null) return;
        var t = ShipTelemetry.Capture();
        var mission = StoryProgress.ActiveMission;
        var s = new PhraseHints.Situation
        {
            Threat = RadarSystem.Instance != null && RadarSystem.Instance.IsThreat,
            AvoidSide = t.HazardAvoid.StartsWith("left") ? "left" : "right",
            Gear = (int)nav.Engine.Gear,
            SpeedKn = Mathf.RoundToInt(Mathf.Abs(nav.currentSpeed)),
            DockClose = t.DestinationKnown && t.DestinationDistanceM < 140,
            DockReady = DockingPilot.Instance != null && DockingPilot.Instance.Current == DockingPilot.Stage.AtSea &&
                        DockingPilot.Instance.DistanceToBerth <= DockingPilot.RequestRange,
            Alongside = DockingPilot.Instance != null && DockingPilot.Instance.Current == DockingPilot.Stage.Alongside,
            Fog = mission != null && (mission.Weather == Weather.Fog || mission.Weather == Weather.NightFog),
            RescueNear = _rescue != null && !_rescue.Boarded && (Flat(_rescue.transform.position - nav.transform.position)).magnitude < 80f,
            Polite = mission != null && mission.Chapter == 3,
        };
        var hints = PhraseHints.For(s);
        if (hints.Count == _hints.Count && hints.TrueForAll(_hints.Contains)) return;

        _hints.Clear();
        _hints.AddRange(hints);
        _hintsRow.Clear();
        var label = HudLook.Words("TRY SAYING", 12f, HudLook.Amber, true);
        label.style.marginRight = 10f;
        _hintsRow.Add(label);
        foreach (var phrase in hints) _hintsRow.Add(HintChip(phrase));
    }

    private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    /// <summary>A phrase chip: clicking it says the phrase aloud, so the learner hears how it sounds.</summary>
    private static Button HintChip(string phrase)
    {
        var chip = new Button(() =>
        {
            if (AudioService.Instance != null)
                AudioService.Instance.SpeakFace(new CrewSpeech.Voice { Name = "Zira", Rate = 0.9f, Pitch = 1f }, phrase);
        })
        { text = "\"" + phrase + "\"" };
        // A phrase in words with a mint line under it: no pill.
        UiTheme.WordButton(chip, false, 19f);
        chip.style.letterSpacing = 0.5f;
        chip.style.borderBottomWidth = 2f;
        chip.style.borderBottomColor = UiTheme.Mint;
        chip.style.marginLeft = 10f; chip.style.marginRight = 10f;
        chip.tooltip = "Click to hear it";
        return chip;
    }

    // ==================================================================
    // Coach panel (tutorial)
    // ==================================================================

    private void BuildCoachPanel(VisualElement root)
    {
        _coachPanel = new VisualElement();
        _coachPanel.style.position = Position.Absolute;
        _coachPanel.style.left = Margin;
        _coachPanel.style.top = 16f + ChartSize + 40f;
        _coachPanel.style.width = 480f;
        // No box: a peach line down its left side, on the dusk gradient from the screen edge.
        _coachPanel.style.paddingLeft = 18f;
        _coachPanel.style.borderLeftWidth = 3f;
        _coachPanel.style.borderLeftColor = UiTheme.Peach;
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

        _coachSkip = MakeButton("SKIP TUTORIAL", false, Hud.Caption);
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
        _coachTry.style.color = UiTheme.Mint;
        _coachTry.style.marginTop = 10f;
        _coachTry.style.paddingTop = 2f;
        _coachTry.style.paddingBottom = 2f;
        _coachTry.style.paddingLeft = 12f;
        _coachTry.style.borderLeftWidth = 2f;
        _coachTry.style.borderLeftColor = UiTheme.Mint;
        _coachPanel.Add(_coachTry);

        _coachHint = new Label("");
        _coachHint.style.fontSize = Hud.Caption;
        _coachHint.style.color = RadioAmber;
        _coachHint.style.whiteSpace = WhiteSpace.Normal;
        _coachHint.style.marginTop = 8f;
        _coachHint.style.display = DisplayStyle.None;
        _coachPanel.Add(_coachHint);

        _coachNext = MakeButton("NEXT", true, Hud.Body);
        _coachNext.style.alignSelf = Align.FlexStart;
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
        _coachScrim.style.display = DisplayStyle.Flex;
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
        if (_coachScrim != null) _coachScrim.style.display = DisplayStyle.None;
        Highlight(null);
    }

    /// <summary>Outlines one HUD part ("radio", "telegraph", "rudder", "crew", "radar", "hull"); null clears.</summary>
    public void Highlight(string key)
    {
        if (_highlighted != null) RestoreBorder(_highlighted);
        _highlighted = null;
        if (key != null) _highlightTargets.TryGetValue(key, out _highlighted);
    }

    // Each highlightable part goes back to its own rim when the highlight moves on.
    private void RestoreBorder(VisualElement e)
    {
        if (e == _chart) HudLook.Border(e, 1.5f, HudLook.Brass);
        else HudLook.Border(e, 0f, Color.clear);
    }

    // The part being taught gets a pulsing peach line under it (a ring round the radar), not a box.
    private void UpdateHighlight()
    {
        if (_highlighted == null) return;
        float pulse = 0.35f + 0.65f * Mathf.PingPong(Time.unscaledTime * 1.6f, 1f);
        Color c = Hud.Accent;
        c.a = pulse;
        if (_highlighted == _chart) HudLook.Border(_highlighted, 4f, c);
        else
        {
            _highlighted.style.borderBottomWidth = 4f;
            _highlighted.style.borderBottomColor = c;
        }
    }

    // ==================================================================
    // Radio card (F1) and the help button
    // ==================================================================

    private void BuildRadioCard(VisualElement root)
    {
        _radioCard = new VisualElement();
        // A full-screen dim, not a box: dark enough that the panels behind don't show through.
        UiTheme.FullDim(_radioCard, 0.92f);
        _radioCard.style.paddingLeft = Length.Percent(24f);
        _radioCard.style.paddingRight = Length.Percent(24f);
        _radioCard.style.alignItems = Align.Stretch;
        _radioCard.style.display = DisplayStyle.None;
        _radioCard.pickingMode = PickingMode.Ignore;

        _radioCardTitle = new Label("RADIO CARD · hold T and say an order · F1 to close");
        var title = _radioCardTitle;
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
        CardLine(engine, "\"increase speed\" / \"reduce speed\"", "one step up or down");
        CardLine(engine, "\"stop engine\"", "stop the engine (the ship drifts on)");
        CardLine(engine, "\"slow astern\" (go back)", "reverse slowly");
        CardLine(engine, "\"full astern\"", "brake hard, then reverse");

        var helm = CardColumn(columns, "FINN · STEERING", Hud.Finn);
        CardLine(helm, "\"port twenty\" (turn left)", "turn left 20°");
        CardLine(helm, "\"starboard twenty\" (turn right)", "turn right 20°");
        CardLine(helm, "\"hard a port\" / \"hard a starboard\"", "turn 60°, full rudder");
        CardLine(helm, "\"port\" / \"starboard\"", "turn left / right 30°");
        CardLine(helm, "\"midships\" (straight)", "stop turning");

        var bottom = new VisualElement();
        bottom.style.flexDirection = FlexDirection.Row;
        bottom.style.marginTop = 10f;
        _radioCard.Add(bottom);

        var ask = CardColumn(bottom, "ASK", Hud.Text);
        CardLine(ask, "\"any danger?\"", "Finn reports rocks and mines");
        CardLine(ask, "\"where is the dock?\"", "direction and distance");
        CardLine(ask, "\"what's our heading?\"", "Finn names the direction");

        var radio = CardColumn(bottom, "RADIO", Hud.Text);
        CardLine(radio, "\"belay that\"", "undo the last order");
        CardLine(radio, "\"radio check\"", "test your microphone");
        CardLine(radio, "Y (hold)", "just talk to the crew");

        // The keys live here now instead of along the bottom of the screen.
        _keyStrip = new Label("");
        _keyStrip.style.fontSize = Hud.Caption;
        _keyStrip.style.color = Hud.TextDim;
        _keyStrip.style.marginTop = 12f;
        _keyStrip.style.whiteSpace = WhiteSpace.Normal;
        _radioCard.Add(_keyStrip);

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
    private Label _radioCardTitle;

    private string OrderKey => _radioMic != null ? _radioMic.OrderKeyName : "T";
    private string TalkKey => _radioMic != null ? _radioMic.TalkKeyName : "Y";
    private static string ChartKey { get { var map = Object.FindAnyObjectByType<LevelMap>(); return map != null ? map.ToggleKeyName : "M"; } }
    private static bool ToggleToTalk => SettingsService.Instance != null && SettingsService.Instance.ToggleToTalk;

    /// <summary>Key names and subtitle size follow the player's settings.</summary>
    private void ApplySettings()
    {
        if (_radioCardTitle != null)
            _radioCardTitle.text = "RADIO CARD · " + (ToggleToTalk ? "press " : "hold ") + OrderKey + " and say an order · F1 to close";
        if (_keyStrip != null)
            _keyStrip.text = "KEYS   " + OrderKey + ": order · " + TalkKey + ": talk · Enter: type · Tab: channel · " + ChartKey + ": chart · F1: this card · Esc: pause";

        float scale = SettingsService.Instance != null ? SettingsService.Instance.SubtitleScale : 1f;
        if (_crewSubtitleText != null) _crewSubtitleText.style.fontSize = Hud.Subtitle * scale;
        if (_crewSttLogText != null) _crewSttLogText.style.fontSize = Hud.Body * scale;
    }

    /// <summary>Words in the top-right corner: HELP opens the order card (F1), PAUSE pauses (Esc).</summary>
    private void BuildKeyStrip(VisualElement root)
    {
        var row = new VisualElement();
        row.style.position = Position.Absolute;
        row.style.right = Margin;
        row.style.top = 18f;
        row.style.flexDirection = FlexDirection.Row;
        row.Add(RoundIcon("HELP", "Orders and keys (F1)", () => SetRadioCardOpen(!IsRadioCardOpen)));
        row.Add(RoundIcon("PAUSE", "Pause (Esc)", () =>
        {
            var pause = FindAnyObjectByType<PauseMenuController>();
            if (pause != null) pause.Open();
        }));
        root.Add(row);
    }

    private static Button RoundIcon(string glyph, string tip, System.Action onClick)
    {
        var b = new Button(onClick) { text = glyph, tooltip = tip };
        UiTheme.WordButton(b, false, 16f);
        b.style.marginLeft = 22f;
        return b;
    }

    private static Button MakeButton(string text, bool primary, float fontSize)
    {
        var b = new Button { text = text };
        UiTheme.WordButton(b, primary, fontSize);
        return b;
    }

    private static string GearName(ShipEngine.TelegraphGear gear)
    {
        switch (gear)
        {
            case ShipEngine.TelegraphGear.DeadSlow: return "DEAD SLOW";
            case ShipEngine.TelegraphGear.SlowAstern: return "SLOW ASTERN";
            case ShipEngine.TelegraphGear.FullAstern: return "FULL ASTERN";
            case ShipEngine.TelegraphGear.Slow: return "SLOW AHEAD";
            case ShipEngine.TelegraphGear.Half: return "HALF AHEAD";
            case ShipEngine.TelegraphGear.Full: return "FULL AHEAD";
            default: return gear.ToString().ToUpperInvariant();
        }
    }

    private static VisualElement MakeBar(float height, out VisualElement fill)
    {
        var bar = new VisualElement();
        bar.style.height = height;
        bar.style.backgroundColor = new Color(1f, 1f, 1f, 0.12f);
        bar.style.overflow = Overflow.Hidden;
        HudLook.Radius(bar, height / 2f);

        fill = new VisualElement();
        fill.style.height = Length.Percent(100f);
        fill.style.width = Length.Percent(0f);
        HudLook.Radius(fill, height / 2f);
        bar.Add(fill);
        return bar;
    }

    // ------------------------------------------------------------ radio state

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
            hint = verb + OrderKey + ": give an order  ·  " + verb + TalkKey + ": talk  ·  Enter: type";
        }

        dot.a = 1f;
        _radioDot.style.backgroundColor = dot;
        _radioState.text = state;
        _radioHint.text = hint;
        _radioHint.style.display = string.IsNullOrEmpty(hint) ? DisplayStyle.None : DisplayStyle.Flex;

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
            _noteShown = false;
            _radioFlash.style.display = DisplayStyle.None;
        }
    }

    private void HandleRadioStatus(RadioStatus status, string text)
    {
        switch (status)
        {
            case RadioStatus.Recording:
                _noteShown = false;
                if (_radioFlash != null) _radioFlash.style.display = DisplayStyle.None;
                return;

            case RadioStatus.Decoding:
            case RadioStatus.Heard:
                // "Message cut" arrives the same frame the cut message starts decoding: let it be read.
                if (_radioFlash != null && _flashStatus != RadioStatus.Cut && !_noteShown) _radioFlash.style.display = DisplayStyle.None;
                return;

            case RadioStatus.NoMic:
            case RadioStatus.Failed:
            case RadioStatus.ModelNotReady:
                _flashStatus = status;
                ShowFlash(text, RadioRed);
                return;

            default:
                _flashStatus = status;
                ShowFlash(text, RadioAmber);
                return;
        }
    }

    private RadioStatus _flashStatus = RadioStatus.Recording;

    /// <summary>A short note in the radio box, e.g. that a typed order was read from a Thai layout.</summary>
    public void ShowRadioNote(string text)
    {
        _flashStatus = RadioStatus.Heard;
        _noteShown = true; // the order it explains is "heard" next; keep the note up
        ShowFlash(text, RadioBlue);
    }

    private bool _noteShown;

    private void ShowFlash(string text, Color color)
    {
        if (_radioFlash == null || string.IsNullOrEmpty(text)) return;

        _radioFlash.text = text;
        _radioFlash.style.color = color;
        _radioFlash.style.display = DisplayStyle.Flex;
        _flashUntil = Time.unscaledTime + FlashSeconds;
    }
}
