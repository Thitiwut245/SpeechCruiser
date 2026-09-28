using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Title screen in the Menu scene: Play / Settings / Quit, plus a radio status line:
/// warming up, ready, no microphone, or speech system failed.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class MainMenuController : MonoBehaviour
{
    private VisualElement _mainPanel;
    private Button _playButton;
    private Button _settingButton;
    private Button _quitButton;
    private Label _radioStatusLabel;
    private SettingsPanelController _settings;

    private RadioMic _radioMic;

    private void OnEnable()
    {
        var root = GetComponent<UIDocument>().rootVisualElement;

        _mainPanel = root.Q<VisualElement>("MainPanel");
        _playButton = root.Q<Button>("PlayButton");
        _settingButton = root.Q<Button>("SettingButton");
        _quitButton = root.Q<Button>("QuitButton");
        _radioStatusLabel = root.Q<Label>("RadioStatusLabel");
        _settings = new SettingsPanelController(root, ShowMainPanel);

        _playButton.clicked += OnPlayClicked;
        _settingButton.clicked += OnSettingClicked;
        _quitButton.clicked += SceneLoader.QuitGame;

        ShowMainPanel();
    }

    private void OnDisable()
    {
        _playButton.clicked -= OnPlayClicked;
        _settingButton.clicked -= OnSettingClicked;
        _quitButton.clicked -= SceneLoader.QuitGame;
        _settings.Dispose();
    }

    private void Start()
    {
        _radioMic = FindAnyObjectByType<RadioMic>();
    }

    private void Update()
    {
        if (_radioStatusLabel == null) return;

        if (_radioMic == null || !_radioMic.HasMicrophone)
            _radioStatusLabel.text = "No microphone found. Plug one in, then check Settings.";
        else if (_radioMic.IsModelReady)
            _radioStatusLabel.text = "Radio ready";
        else if (_radioMic.IsModelLoading)
            _radioStatusLabel.text = "Radio warming up...";
        else
            _radioStatusLabel.text = "Speech system did not load. Voice orders won't work.";
    }

    private void OnPlayClicked()
    {
        SceneLoader.Instance.Load(SceneId.Game);
    }

    private void OnSettingClicked()
    {
        _mainPanel.style.display = DisplayStyle.None;
        _settings.Show();
    }

    private void ShowMainPanel()
    {
        _settings.Hide();
        _mainPanel.style.display = DisplayStyle.Flex;
    }
}
