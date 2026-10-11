using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Title screen in the Menu scene, on paper (navy ink on a sand gradient, projector-safe):
/// the compass-rose emblem, Story (which names the next mission), Free Sail, Settings and
/// Quit, and a radio lamp whose line says how the radio is: warming up, ready, no
/// microphone, or speech system failed. The order key runs a radio check here, and the
/// line says what was heard. The story screen and settings share the same sand ground.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class MainMenuController : MonoBehaviour
{
    private VisualElement _mainPanel;
    private Button _storyButton;
    private Button _playButton;
    private Button _settingButton;
    private Button _quitButton;
    private Button _newCareerButton;
    private Label _careerLabel;
    private Label _storyTitle;
    private Label _storySub;
    private Label _radioStatusLabel;
    private VisualElement _radioDot;
    private CompassRose _emblem;
    private VisualElement _scrim;
    // The sand ground reaches past the title column, and further for the wide story screen.
    private const float TitleScrimWidth = 1300f, StoryScrimWidth = 2200f;
    // New Career takes a second press, so nobody wipes a career by accident.
    private bool _confirmNewCareer;
    private SettingsPanelController _settings;
    private StoryMenuController _story;

    private RadioMic _radioMic;

    // A radio-check message, shown instead of the status until this time (unscaled).
    private string _checkLine;
    private RadioStatus _lastStatus = RadioStatus.Heard;
    private float _checkUntil;

    private void OnEnable()
    {
        var root = GetComponent<UIDocument>().rootVisualElement;

        _mainPanel = root.Q<VisualElement>("MainPanel");
        _storyButton = root.Q<Button>("StoryButton");
        _playButton = root.Q<Button>("PlayButton");
        _settingButton = root.Q<Button>("SettingButton");
        _quitButton = root.Q<Button>("QuitButton");
        _radioStatusLabel = root.Q<Label>("RadioStatusLabel");
        _newCareerButton = root.Q<Button>("NewCareerButton");
        _careerLabel = root.Q<Label>("CareerLabel");
        UiTheme.Apply(root);
        var container = root.Q<VisualElement>("MainMenuContainer");
        UiTheme.Paper(container);
        _scrim = container?.Q<VisualElement>("TitleScrim");
        if (container != null && _scrim == null)
        {
            _scrim = UiTheme.SideScrim(TitleScrimWidth, 0.96f, UiTheme.Sand);
            _scrim.name = "TitleScrim";
            container.Insert(0, _scrim);
        }
        _storyTitle = root.Q<Label>("StoryButtonTitle");
        _storySub = root.Q<Label>("StoryButtonSub");
        _radioDot = root.Q<VisualElement>("RadioDot");
        var emblemSlot = root.Q<VisualElement>("Emblem");
        if (emblemSlot != null && _emblem == null)
        {
            _emblem = new CompassRose(84f, UiTheme.Ink, UiTheme.InkSoft, UiTheme.Rust);
            emblemSlot.Add(_emblem);
        }
        _settings = new SettingsPanelController(root, ShowMainPanel);
        // The settings page is a full-screen dim: its template wrapper must fill the screen
        // too (it has no size of its own), without catching clicks while it's hidden.
        var settingsWrapper = root.Q<VisualElement>("SettingsPanel")?.parent;
        if (settingsWrapper != null && settingsWrapper != container)
        {
            settingsWrapper.style.position = Position.Absolute;
            settingsWrapper.style.left = 0; settingsWrapper.style.right = 0;
            settingsWrapper.style.top = 0; settingsWrapper.style.bottom = 0;
            settingsWrapper.pickingMode = PickingMode.Ignore;
        }
        _story = new StoryMenuController(root, ShowMainPanel);

        _storyButton.clicked += OnStoryClicked;
        _playButton.clicked += OnPlayClicked;
        _settingButton.clicked += OnSettingClicked;
        _quitButton.clicked += SceneLoader.QuitGame;
        _newCareerButton.clicked += OnNewCareerClicked;

        ShowMainPanel();
    }

    private void OnDisable()
    {
        _storyButton.clicked -= OnStoryClicked;
        _playButton.clicked -= OnPlayClicked;
        _settingButton.clicked -= OnSettingClicked;
        _quitButton.clicked -= SceneLoader.QuitGame;
        _newCareerButton.clicked -= OnNewCareerClicked;
        _settings.Dispose();
        if (_radioMic != null) _radioMic.OnRadioStatus -= HandleRadioStatus; // RadioMic outlives this scene
    }

    private void Start()
    {
        RefreshCareer();
        // Back from a story mission: straight to the story screen.
        if (StoryProgress.ActiveMission != null) OnStoryClicked();
        _radioMic = FindAnyObjectByType<RadioMic>();
        if (_radioMic != null) _radioMic.OnRadioStatus += HandleRadioStatus;
    }

    private void HandleRadioStatus(RadioStatus status, string text)
    {
        // "Message cut" arrives the same frame the cut message starts decoding: keep it up.
        bool keepCut = status == RadioStatus.Decoding && _lastStatus == RadioStatus.Cut;
        _lastStatus = status;
        if (keepCut) return;
        string line = ShipyardManager.RadioCheckText(status, text, _radioMic);
        if (line == null) return;
        _checkLine = line;
        _checkUntil = Time.unscaledTime + ShipyardManager.RadioLineSeconds(status);
    }

    // Deep enough to read on sand.
    private static readonly Color LampGreen = new Color(0.05f, 0.55f, 0.35f);
    private static readonly Color LampAmber = new Color(0.82f, 0.52f, 0.02f);
    private static readonly Color LampRed = new Color(0.75f, 0.12f, 0.12f);

    private void Update()
    {
        if (_emblem != null) _emblem.Tick(Time.unscaledTime);
        if (_radioStatusLabel == null) return;

        Color lamp;
        if (_checkLine != null && Time.unscaledTime < _checkUntil)
        {
            _radioStatusLabel.text = _checkLine;
            lamp = LampAmber;
        }
        else if (_radioMic == null || !_radioMic.HasMicrophone)
        {
            _radioStatusLabel.text = "No microphone. Plug one in, then see Settings.";
            lamp = LampRed;
        }
        else if (_radioMic.IsModelReady)
        {
            _radioStatusLabel.text =
                (SettingsService.Instance != null && SettingsService.Instance.ToggleToTalk ? "Press " : "Hold ") +
                _radioMic.OrderKeyName + " and say \"radio check\"";
            lamp = LampGreen;
        }
        else if (_radioMic.IsModelLoading)
        {
            _radioStatusLabel.text = "Radio warming up...";
            lamp = LampAmber * (0.6f + 0.4f * Mathf.PingPong(Time.unscaledTime * 2f, 1f));
            lamp.a = 1f;
        }
        else
        {
            _radioStatusLabel.text = "Voice orders are off: the speech system did not load.";
            lamp = LampRed;
        }
        if (_radioDot != null) _radioDot.style.backgroundColor = lamp;
    }

    /// <summary>
    /// Story names the next mission, in a few words; Free Sail needs no explanation. The less
    /// there is to read here, the sooner the player is sailing and speaking.
    /// </summary>
    private void RefreshCareer()
    {
        var session = GameSession.Instance;
        var next = StoryProgress.NextToPlay;
        _storyTitle.text = StoryProgress.DoneCount == 0 ? "START THE STORY" : next == null ? "STORY" : "CONTINUE THE STORY";
        _storySub.text = next == null ? "Complete! Sail any mission again." : "Mission " + next.Id + "  ·  " + next.Title;
        bool anything = (session != null && session.HasAnySave) || StoryProgress.DoneCount > 0;
        _newCareerButton.style.display = anything ? DisplayStyle.Flex : DisplayStyle.None;
        _careerLabel.style.display = DisplayStyle.None;
        _confirmNewCareer = false;
        _newCareerButton.text = "NEW CAREER";
        _newCareerButton.RemoveFromClassList("confirm");
    }

    private void OnNewCareerClicked()
    {
        if (!_confirmNewCareer)
        {
            _confirmNewCareer = true;
            _newCareerButton.text = "ERASE ALL PROGRESS? CLICK AGAIN";
            _newCareerButton.AddToClassList("confirm");
            return;
        }
        GameSession.Instance.StartNewCareer();
        RefreshCareer();
    }

    private void OnStoryClicked()
    {
        RefreshCareer(); // leaving the title cancels a half-confirmed New Career
        _mainPanel.style.display = DisplayStyle.None;
        if (_scrim != null) _scrim.style.width = StoryScrimWidth;
        _story.Show();
    }

    /// <summary>Free Sail: the career's shipyard, jobs and upgrades.</summary>
    private void OnPlayClicked()
    {
        StoryProgress.Leave();
        SceneLoader.Instance.Load(SceneId.Game);
    }

    private void OnSettingClicked()
    {
        RefreshCareer(); // leaving the title cancels a half-confirmed New Career
        _mainPanel.style.display = DisplayStyle.None;
        _settings.Show();
    }

    private void ShowMainPanel()
    {
        _settings.Hide();
        _story.Hide();
        RefreshCareer();
        if (_scrim != null) _scrim.style.width = TitleScrimWidth;
        _mainPanel.style.display = DisplayStyle.Flex;
    }
}
