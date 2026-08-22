using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Attach this to the GameObject containing the Shipyard UI Document.
/// </summary>
public class ShipyardManager : MonoBehaviour
{
    [Tooltip("The UIDocument containing your Shipyard.uxml layout")]
    [SerializeField] private UIDocument _document;

    [Tooltip("The Transform where the ship should respawn when starting a mission")]
    [SerializeField] private Transform StartPort;
    
    private Label _creditsText;
    private Label _engineLevelText;
    private Label _helmLevelText;
    private Label _statusText;

    private Button _upgradeEngineBtn;
    private Button _upgradeHelmBtn;
    private Button _startMissionBtn;

    // Base costs
    private int _engineCost = 100;
    private int _helmCost = 100;

    private void Start()
    {
        if (_document == null) _document = GetComponent<UIDocument>();
        if (_document == null) return;

        var root = _document.rootVisualElement;

        // Ensure you create these matching names in your UI Builder!
        _creditsText = root.Q<Label>("credits-text");
        _engineLevelText = root.Q<Label>("engine-level-text");
        _helmLevelText = root.Q<Label>("helm-level-text");
        _statusText = root.Q<Label>("status-text");

        _upgradeEngineBtn = root.Q<Button>("upgrade-engine-btn");
        _upgradeHelmBtn = root.Q<Button>("upgrade-helm-btn");
        _startMissionBtn = root.Q<Button>("start-mission-btn");

        if (_upgradeEngineBtn != null) _upgradeEngineBtn.clicked += OnUpgradeEngine;
        if (_upgradeHelmBtn != null) _upgradeHelmBtn.clicked += OnUpgradeHelm;
        if (_startMissionBtn != null) _startMissionBtn.clicked += OnStartMission;

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnProgressUpdated += RefreshUI;
            GameManager.Instance.OnStateChanged += HandleStateChanged;
            
            // Initial UI state
            HandleStateChanged(GameManager.Instance.CurrentState);
        }

        RefreshUI();
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnProgressUpdated -= RefreshUI;
            GameManager.Instance.OnStateChanged -= HandleStateChanged;
        }
    }

    private void HandleStateChanged(GameState state)
    {
        if (_document != null && _document.rootVisualElement != null)
        {
            // Hide shipyard entirely while playing
            _document.rootVisualElement.style.display = (state == GameState.Playing) ? DisplayStyle.None : DisplayStyle.Flex;

            if (_statusText != null)
            {
                if (state == GameState.Shipyard) _statusText.text = "WELCOME TO THE SHIPYARD";
                else if (state == GameState.Victory) _statusText.text = "MISSION SUCCESS! Cargo Delivered.";
                else if (state == GameState.GameOver) _statusText.text = "MISSION FAILED! Ship sunk.";
            }
        }
    }

    private void RefreshUI()
    {
        if (GameManager.Instance == null) return;

        if (_creditsText != null) _creditsText.text = $"CREDITS: ${GameManager.Instance.Credits}";
        
        if (_engineLevelText != null) 
            _engineLevelText.text = $"Engine Lvl: {GameManager.Instance.EngineLevel} (Cost: ${_engineCost * GameManager.Instance.EngineLevel})";
            
        if (_helmLevelText != null) 
            _helmLevelText.text = $"Helm Lvl: {GameManager.Instance.HelmLevel} (Cost: ${_helmCost * GameManager.Instance.HelmLevel})";
    }

    private void OnUpgradeEngine()
    {
        int cost = _engineCost * GameManager.Instance.EngineLevel;
        GameManager.Instance.TryBuyUpgrade("Engine", cost);
    }

    private void OnUpgradeHelm()
    {
        int cost = _helmCost * GameManager.Instance.HelmLevel;
        GameManager.Instance.TryBuyUpgrade("Helm", cost);
    }

    private void OnStartMission()
    {
        // Build the ocean FIRST. The generator decides where the course starts,
        // so the ship has to be placed after it exists, not before.
        if (LevelGenerator.Instance != null)
        {
            LevelGenerator.Instance.GenerateLevel();
        }

        PlaceShipAtStart();

        GameManager.Instance.SetState(GameState.Playing);
    }

    private void PlaceShipAtStart()
    {
        var nav = ShipNavigator.Instance;
        if (nav == null) return;

        Vector3 position = Vector3.zero;
        Quaternion rotation = Quaternion.identity;

        if (LevelGenerator.Instance != null && LevelGenerator.Instance.IsLevelReady)
        {
            position = LevelGenerator.Instance.StartPosition;
            rotation = LevelGenerator.Instance.StartRotation;
        }
        else if (StartPort != null)
        {
            position = StartPort.position;
            rotation = StartPort.rotation;
        }

        nav.transform.SetPositionAndRotation(position, rotation);

        // Level the hull, in case the last run ended with it capsized.
        nav.transform.eulerAngles = new Vector3(0f, nav.transform.eulerAngles.y, 0f);

        ShipHull hull = nav.GetComponentInChildren<ShipHull>();
        if (hull != null) hull.RepairShip();
    }
}
