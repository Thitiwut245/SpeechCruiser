using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Title screen in the Menu scene, styled as a ship's bridge board: the compass-rose emblem,
/// Story (which names the next mission) and Free Sail (which shows the career), Settings and
/// Quit, and a radio plate whose lamp and line say how the radio is: warming up, ready, no
/// microphone, or speech system failed. The order key runs a radio check here, and the
/// line says what was heard.
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
        _storyTitle = root.Q<Label>("StoryButtonTitle");
        _storySub = root.Q<Label>("StoryButtonSub");
        _radioDot = root.Q<VisualElement>("RadioDot");
        var emblemSlot = root.Q<VisualElement>("Emblem");
        if (emblemSlot != null && _emblem == null)
        {
            _emblem = new CompassRose(96f, HudLook.Brass, new Color(0.85f, 0.88f, 0.9f), HudLook.Amber);
            emblemSlot.Add(_emblem);
        }
        _settings = new SettingsPanelController(root, ShowMainPanel);
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

    private static readonly Color LampGreen = new Color(0.30f, 0.85f, 0.45f);
    private static readonly Color LampAmber = new Color(1f, 0.78f, 0.25f);
    private static readonly Color LampRed = new Color(1f, 0.30f, 0.24f);

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
            _radioStatusLabel.text = "No microphone found. Plug one in, then check Settings.";
            lamp = LampRed;
        }
        else if (_radioMic.IsModelReady)
        {
            _radioStatusLabel.text = "Radio ready. Test it: " +
                (SettingsService.Instance != null && SettingsService.Instance.ToggleToTalk ? "press " : "hold ") +
                _radioMic.OrderKeyName + " and say \"radio check\".";
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
            _radioStatusLabel.text = "Speech system did not load. Voice orders won't work.";
            lamp = LampRed;
        }
        if (_radioDot != null) _radioDot.style.backgroundColor = lamp;
    }

    /// <summary>A saved career says so; Story names the next mission. Free Sail carries on with the career.</summary>
    private void RefreshCareer()
    {
        var session = GameSession.Instance;
        bool saved = session != null && session.HasSavedCareer;
        var next = StoryProgress.NextToPlay;
        _storyTitle.text = StoryProgress.DoneCount == 0 ? "Start the Story" : next == null ? "Story" : "Continue the Story";
        _storySub.text = next == null ? "Complete! Sail any mission again."
                       : "Mission " + next.Id + ": " + next.Title + (StoryProgress.DoneCount == 0 ? ". A new captain's first day." : "");
        bool anything = (session != null && session.HasAnySave) || StoryProgress.DoneCount > 0;
        _newCareerButton.style.display = anything ? DisplayStyle.Flex : DisplayStyle.None;
        _careerLabel.text = "Choose your own jobs, earn money, upgrade the ship.";
        if (saved)
        {
            int n = session.MissionsCompleted;
            _careerLabel.text = "Your career: $" + session.Credits + " · " + n + (n == 1 ? " delivery" : " deliveries");
            // A voyage left unfinished last time (the game was closed at sea): say what it cost.
            string last = session.LastMissionSummary;
            if (!string.IsNullOrEmpty(last) && (last.StartsWith("VOYAGE ABANDONED") || last.StartsWith("TUTORIAL VOYAGE LEFT")))
                _careerLabel.text += "\nLast time: " + last.Substring(0, 1) + last.Substring(1).ToLowerInvariant();
        }
        _confirmNewCareer = false;
        _newCareerButton.text = "New Career";
        _newCareerButton.RemoveFromClassList("confirm");
    }

    private void OnNewCareerClicked()
    {
        if (!_confirmNewCareer)
        {
            _confirmNewCareer = true;
            _newCareerButton.text = "Erase all progress? Click again";
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
        _mainPanel.style.display = DisplayStyle.Flex;
    }
}
