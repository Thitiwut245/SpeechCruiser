using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Title screen in the Menu scene: Play / Settings / Quit, plus a radio status line:
/// warming up, ready, no microphone, or speech system failed. The order key runs a radio
/// check here, and the line says what was heard.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class MainMenuController : MonoBehaviour
{
    private VisualElement _mainPanel;
    private Button _playButton;
    private Button _settingButton;
    private Button _quitButton;
    private Button _newCareerButton;
    private Label _careerLabel;
    private Label _radioStatusLabel;
    // New Career takes a second press, so nobody wipes a career by accident.
    private bool _confirmNewCareer;
    private SettingsPanelController _settings;

    private RadioMic _radioMic;

    // A radio-check message, shown instead of the status until this time (unscaled).
    private string _checkLine;
    private float _checkUntil;

    private void OnEnable()
    {
        var root = GetComponent<UIDocument>().rootVisualElement;

        _mainPanel = root.Q<VisualElement>("MainPanel");
        _playButton = root.Q<Button>("PlayButton");
        _settingButton = root.Q<Button>("SettingButton");
        _quitButton = root.Q<Button>("QuitButton");
        _radioStatusLabel = root.Q<Label>("RadioStatusLabel");
        _newCareerButton = root.Q<Button>("NewCareerButton");
        _careerLabel = root.Q<Label>("CareerLabel");
        _settings = new SettingsPanelController(root, ShowMainPanel);

        _playButton.clicked += OnPlayClicked;
        _settingButton.clicked += OnSettingClicked;
        _quitButton.clicked += SceneLoader.QuitGame;
        _newCareerButton.clicked += OnNewCareerClicked;

        ShowMainPanel();
    }

    private void OnDisable()
    {
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
        _radioMic = FindAnyObjectByType<RadioMic>();
        if (_radioMic != null) _radioMic.OnRadioStatus += HandleRadioStatus;
    }

    private void HandleRadioStatus(RadioStatus status, string text)
    {
        string line = ShipyardManager.RadioCheckText(status, text, _radioMic);
        if (line == null) return;
        _checkLine = line;
        // Recording and decoding stay up until the next status replaces them.
        _checkUntil = Time.unscaledTime + (status == RadioStatus.Recording || status == RadioStatus.Decoding ? 30f
                                         : status == RadioStatus.Tested ? 6f : 4f);
    }

    private void Update()
    {
        if (_radioStatusLabel == null) return;

        if (_checkLine != null && Time.unscaledTime < _checkUntil)
            _radioStatusLabel.text = _checkLine;
        else if (_radioMic == null || !_radioMic.HasMicrophone)
            _radioStatusLabel.text = "No microphone found. Plug one in, then check Settings.";
        else if (_radioMic.IsModelReady)
            _radioStatusLabel.text = "Radio ready. Test it: " +
                (SettingsService.Instance != null && SettingsService.Instance.ToggleToTalk ? "press " : "hold ") +
                _radioMic.OrderKeyName + " and say \"radio check\".";
        else if (_radioMic.IsModelLoading)
            _radioStatusLabel.text = "Radio warming up...";
        else
            _radioStatusLabel.text = "Speech system did not load. Voice orders won't work.";
    }

    /// <summary>A saved career says so, and Set Sail carries on with it.</summary>
    private void RefreshCareer()
    {
        var session = GameSession.Instance;
        bool saved = session != null && session.HasSavedCareer;
        _playButton.text = saved ? "Continue" : "Set Sail";
        _newCareerButton.style.display = saved ? DisplayStyle.Flex : DisplayStyle.None;
        _careerLabel.style.display = saved ? DisplayStyle.Flex : DisplayStyle.None;
        if (saved)
        {
            int n = session.MissionsCompleted;
            _careerLabel.text = "Your career: $" + session.Credits + " · " + n + (n == 1 ? " delivery" : " deliveries");
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
            _newCareerButton.text = "Erase progress? Click again";
            _newCareerButton.AddToClassList("confirm");
            return;
        }
        GameSession.Instance.StartNewCareer();
        RefreshCareer();
    }

    private void OnPlayClicked()
    {
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
        _mainPanel.style.display = DisplayStyle.Flex;
    }
}
