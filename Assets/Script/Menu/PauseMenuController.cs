using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// In-game pause menu for the Game scene. Esc toggles it (or backs out of Settings).
/// Pausing goes through TimeController so it can't fight slow-mo or the tutorial.
/// It is on paper like the title screen (navy ink on a full sand page, projector-safe),
/// with the title's compass-rose emblem.
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
    private Button _checkpointButton;
    private Button _restartButton;
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
        _checkpointButton = root.Q<Button>("CheckpointButton");
        _restartButton = root.Q<Button>("RestartButton");
        _settings = new SettingsPanelController(root, () => SceneLoader.Page(ShowMainPanel));
        UiTheme.Apply(root);
        UiTheme.Paper(_container);

        if (_mainPanel != null && _mainPanel.Q<CompassRose>() == null)
        {
            var emblem = new CompassRose(64f, UiTheme.Ink, UiTheme.InkSoft, UiTheme.Rust);
            emblem.AddToClassList("menu-emblem");
            _mainPanel.Insert(0, emblem);
        }

        _resumeButton.clicked += Close;
        _settingButton.clicked += OnSettingClicked;
        _mainMenuButton.clicked += OnMainMenuClicked;
        _quitButton.clicked += OnQuitClicked;
        _replayTutorialButton.clicked += OnReplayTutorialClicked;
        _checkpointButton.clicked += OnCheckpointClicked;
        _restartButton.clicked += OnRestartClicked;

        _container.style.display = DisplayStyle.None;
    }

    private void OnDisable()
    {
        _resumeButton.clicked -= Close;
        _settingButton.clicked -= OnSettingClicked;
        _mainMenuButton.clicked -= OnMainMenuClicked;
        _quitButton.clicked -= OnQuitClicked;
        _replayTutorialButton.clicked -= OnReplayTutorialClicked;
        _checkpointButton.clicked -= OnCheckpointClicked;
        _restartButton.clicked -= OnRestartClicked;
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
        else if (_settings.IsOpen) SceneLoader.Page(ShowMainPanel);
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

    private void OnSettingClicked() => SceneLoader.Page(() =>
    {
        _mainPanel.style.display = DisplayStyle.None;
        _settings.Show();
    });

    private void ShowMainPanel()
    {
        _confirmAbandon = false;
        _confirmQuit = false;
        _mainMenuButton.text = "Main Menu";
        _quitButton.text = "Quit";
        // Story voyages can be restarted, from the start or from the last checkpoint passed.
        bool story = AtSea && StoryProgress.ActiveMission != null;
        _restartButton.style.display = story ? DisplayStyle.Flex : DisplayStyle.None;
        _checkpointButton.style.display = story && Checkpoints.Saved != null ? DisplayStyle.Flex : DisplayStyle.None;
        if (Checkpoints.Saved != null) _checkpointButton.text = "Restart from Checkpoint " + Checkpoints.Saved.Number;
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

    // A restart reloads the scene; the game stays paused through the fade, as for Main Menu.
    private void OnCheckpointClicked() => Restart(VoyageRestart.Mode.FromCheckpoint);
    private void OnRestartClicked() => Restart(VoyageRestart.Mode.FromStart);

    private void Restart(VoyageRestart.Mode mode)
    {
        IsOpen = false;
        _container.style.display = DisplayStyle.None;
        VoyageRestart.Request(mode);
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
