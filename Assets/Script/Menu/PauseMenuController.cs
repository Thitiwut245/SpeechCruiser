using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// In-game pause menu for the Game scene. Esc toggles it (or backs out of Settings).
/// Pausing goes through TimeController so it can't fight slow-mo or the tutorial.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class PauseMenuController : MonoBehaviour
{
    private VisualElement _container;
    private VisualElement _mainPanel;
    private Button _resumeButton;
    private Button _settingButton;
    private Button _mainMenuButton;
    private Button _quitButton;
    private Button _replayTutorialButton;
    private SettingsPanelController _settings;

    public bool IsOpen { get; private set; }

    private void OnEnable()
    {
        var root = GetComponent<UIDocument>().rootVisualElement;

        _container = root.Q<VisualElement>("PauseMenuContainer");
        _mainPanel = root.Q<VisualElement>("MainPanel");
        _resumeButton = root.Q<Button>("ResumeButton");
        _settingButton = root.Q<Button>("SettingButton");
        _mainMenuButton = root.Q<Button>("MainMenuButton");
        _quitButton = root.Q<Button>("QuitButton");
        _replayTutorialButton = root.Q<Button>("ReplayTutorialButton");
        _settings = new SettingsPanelController(root, ShowMainPanel);

        _resumeButton.clicked += Close;
        _settingButton.clicked += OnSettingClicked;
        _mainMenuButton.clicked += OnMainMenuClicked;
        _quitButton.clicked += SceneLoader.QuitGame;
        _replayTutorialButton.clicked += OnReplayTutorialClicked;

        _container.style.display = DisplayStyle.None;
    }

    private void OnDisable()
    {
        _resumeButton.clicked -= Close;
        _settingButton.clicked -= OnSettingClicked;
        _mainMenuButton.clicked -= OnMainMenuClicked;
        _quitButton.clicked -= SceneLoader.QuitGame;
        _replayTutorialButton.clicked -= OnReplayTutorialClicked;
        _settings.Dispose();

        if (IsOpen && TimeController.Instance != null) TimeController.Instance.ReleasePause(this);
        IsOpen = false;
    }

    private void Update()
    {
        if (Keyboard.current == null || !Keyboard.current.escapeKey.wasPressedThisFrame) return;
        // Esc belongs to the typed-order box or the key picker while they are open.
        if (TypedOrders.BlocksHotkeys) return;

        if (!IsOpen) Open();
        else if (_settings.IsOpen) ShowMainPanel();
        else Close();
    }

    public void Open()
    {
        if (IsOpen || SceneLoader.Instance.IsLoading) return;

        IsOpen = true;
        _container.style.display = DisplayStyle.Flex;
        ShowMainPanel();
        TimeController.Instance.RequestPause(this);
    }

    public void Close()
    {
        if (!IsOpen) return;

        IsOpen = false;
        _container.style.display = DisplayStyle.None;
        TimeController.Instance.ReleasePause(this);
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

    private void OnReplayTutorialClicked()
    {
        Close();
        if (TutorialManager.Instance != null) TutorialManager.Instance.Replay();
    }

    private void OnMainMenuClicked()
    {
        Close();
        SceneLoader.Instance.Load(SceneId.Menu);
    }
}
