using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// In-game pause menu for the Game scene. Esc toggles it (or backs out of Settings).
/// Pausing goes through TimeController so it can't fight slow-mo or the tutorial.
/// It wears the title screen's compass-rose emblem on its brass-rimmed card.
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

    // Main Menu mid-voyage abandons it, so it takes a second click.
    private bool _confirmAbandon;
    private static bool AtSea => GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.Playing;

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

        if (_mainPanel != null && _mainPanel.Q<CompassRose>() == null)
        {
            var emblem = new CompassRose(70f, HudLook.Brass, new Color(0.85f, 0.88f, 0.9f), HudLook.Amber);
            emblem.AddToClassList("menu-emblem");
            _mainPanel.Insert(0, emblem);
        }

        _resumeButton.clicked += Close;
        _settingButton.clicked += OnSettingClicked;
        _mainMenuButton.clicked += OnMainMenuClicked;
        _quitButton.clicked += OnQuitClicked;
        _replayTutorialButton.clicked += OnReplayTutorialClicked;

        _container.style.display = DisplayStyle.None;
    }

    private void OnDisable()
    {
        _resumeButton.clicked -= Close;
        _settingButton.clicked -= OnSettingClicked;
        _mainMenuButton.clicked -= OnMainMenuClicked;
        _quitButton.clicked -= OnQuitClicked;
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
        _confirmAbandon = false;
        _confirmQuit = false;
        _mainMenuButton.text = "Main Menu";
        _quitButton.text = "Quit";
        _settings.Hide();
        _mainPanel.style.display = DisplayStyle.Flex;
    }

    // Quitting at sea is abandoning too (the next launch would charge it), so it asks the same way.
    private bool _confirmQuit;

    private void OnQuitClicked()
    {
        if (AtSea && !_confirmQuit)
        {
            _confirmQuit = true;
            _quitButton.text = AbandonQuestion("Quit and lose the voyage?");
            return;
        }
        if (AtSea) GameSession.Instance.AbandonVoyage();
        SceneLoader.QuitGame();
    }

    private static string AbandonQuestion(string question)
    {
        int cost = GameSession.Instance.AbandonCost;
        return question + (cost > 0 ? " (-$" + cost + ")" : " (free: tutorial)");
    }

    private void OnReplayTutorialClicked()
    {
        Close();
        if (TutorialManager.Instance != null) TutorialManager.Instance.Replay();
    }

    private void OnMainMenuClicked()
    {
        if (AtSea && !_confirmAbandon)
        {
            _confirmAbandon = true;
            _mainMenuButton.text = AbandonQuestion("Abandon voyage?");
            return;
        }
        if (AtSea) GameSession.Instance.AbandonVoyage();

        // Hide the menu but keep the game paused through the fade: the scene load resets
        // time itself, so the ship can't sail on (or be hit) while the screen goes dark.
        IsOpen = false;
        _container.style.display = DisplayStyle.None;
        SceneLoader.Instance.Load(SceneId.Menu);
    }
}
