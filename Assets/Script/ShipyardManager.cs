using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;

/// <summary>
/// The shipyard phase of the Game scene. The store is a panel on the left; the harbour is
/// built and the ship moored as soon as the scene loads, so the player sees the ship they are
/// upgrading on the right. SET SAIL is always visible. Pressing it hides the store and the
/// cargo loading becomes the mission intro (Space skips it).
/// Attach to the GameObject with the Shipyard UIDocument.
/// </summary>
public class ShipyardManager : MonoBehaviour
{
    [Tooltip("The UIDocument containing your Shipyard.uxml layout")]
    [SerializeField] private UIDocument _document;

    [Tooltip("The Transform where the ship should respawn when starting a mission")]
    [SerializeField] private Transform StartPort;

    [Header("Cargo Loading")]
    [Tooltip("Assign multiple container prefab variants here to spawn them randomly")]
    [SerializeField] private List<GameObject> ContainerPrefabs = new List<GameObject>();
    [SerializeField] private Vector3 CargoStartOffset = new Vector3(0, 1.5f, 0f);
    [SerializeField] private Vector3 ContainerSpacing = new Vector3(2.5f, 2.5f, 6.0f);
    [SerializeField] private int Columns = 3;
    [SerializeField] private int Rows = 4;

    [Header("Camera")]
    [Tooltip("Where the ship sits on screen in the shipyard (0 = centre, 0.5 = right edge), clear of the store panel.")]
    [SerializeField] private float ShipyardScreenX = 0.16f;

    [Header("Cargo Loss")]
    [Tooltip("Seconds a container knocked overboard stays visible before it is removed.")]
    [SerializeField] private float OverboardSeconds = 3f;

    private VisualElement _storePanel;
    private Label _statusText;
    private Label _toastText;
    private Label _creditsText;
    private VisualElement _hullPips;
    private Label _hullText;
    private Button _repairBtn;
    private Button _jobEasyBtn;
    private Button _jobNormalBtn;
    private Button _jobHardBtn;
    private Label _engineLevelText;
    private Label _helmLevelText;
    private Button _upgradeEngineBtn;
    private Button _upgradeHelmBtn;
    private Label _startWarning;
    private Button _startMissionBtn;
    private Label _startTitle;
    private Label _startDetail;
    private Label _loadingCaption;

    private JobSpec _selectedJob = JobSpec.Easy;
    private bool _loading;
    private bool _skipLoading;
    private bool _confirmDamagedStart;
    private float _toastUntil;
    private CinemachinePositionComposer _composer;
    private CinemachineCamera _vcam;

    private readonly List<GameObject> _spawnedContainers = new List<GameObject>();

    private void Start()
    {
        if (_document == null) _document = GetComponent<UIDocument>();
        if (_document == null) return;

        var root = _document.rootVisualElement;

        _storePanel = root.Q<VisualElement>("store-panel");
        _statusText = root.Q<Label>("status-text");
        _toastText = root.Q<Label>("toast-text");
        _creditsText = root.Q<Label>("credits-text");
        _hullPips = root.Q<VisualElement>("hull-pips");
        _hullText = root.Q<Label>("hull-text");
        _repairBtn = root.Q<Button>("repair-btn");
        _jobEasyBtn = root.Q<Button>("job-btn-easy");
        _jobNormalBtn = root.Q<Button>("job-btn-normal");
        _jobHardBtn = root.Q<Button>("job-btn-hard");
        _engineLevelText = root.Q<Label>("engine-level-text");
        _helmLevelText = root.Q<Label>("helm-level-text");
        _upgradeEngineBtn = root.Q<Button>("upgrade-engine-btn");
        _upgradeHelmBtn = root.Q<Button>("upgrade-helm-btn");
        _startWarning = root.Q<Label>("start-warning");
        _startMissionBtn = root.Q<Button>("start-mission-btn");
        _startTitle = root.Q<Label>("start-title");
        _startDetail = root.Q<Label>("start-detail");
        _loadingCaption = root.Q<Label>("loading-caption");

        _repairBtn.clicked += OnRepairHull;
        _jobEasyBtn.clicked += () => SelectJob(JobSpec.Easy);
        _jobNormalBtn.clicked += () => SelectJob(JobSpec.Normal);
        _jobHardBtn.clicked += () => SelectJob(JobSpec.Hard);
        _upgradeEngineBtn.clicked += OnUpgradeEngine;
        _upgradeHelmBtn.clicked += OnUpgradeHelm;
        _startMissionBtn.clicked += OnStartMission;

        _vcam = FindAnyObjectByType<CinemachineCamera>();
        _composer = FindAnyObjectByType<CinemachinePositionComposer>();

        if (GameSession.Instance != null) GameSession.Instance.OnProgressUpdated += RefreshUI;
        ShipHull.OnHullHit += DropCargo;
        ShipHull.OnHealthChanged += HandleHullChanged;

        SelectJob(GameSession.Instance != null ? GameSession.Instance.CurrentJob : JobSpec.Easy); // remembers the last job
        PrepareHarbour();

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnStateChanged += HandleStateChanged;
            HandleStateChanged(GameManager.Instance.CurrentState);
        }

        RefreshUI();
    }

    private void OnDestroy()
    {
        // GameSession outlives this scene, so this unsubscribe is what prevents a leak.
        if (GameSession.Instance != null) GameSession.Instance.OnProgressUpdated -= RefreshUI;
        ShipHull.OnHullHit -= DropCargo;
        ShipHull.OnHealthChanged -= HandleHullChanged;
        if (GameManager.Instance != null) GameManager.Instance.OnStateChanged -= HandleStateChanged;
    }

    private void Update()
    {
        if (_toastText != null && _toastText.text.Length > 0 && Time.unscaledTime > _toastUntil) _toastText.text = "";

        var kb = Keyboard.current;
        if (kb == null) return;
        if (_loading && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame)) _skipLoading = true;
    }

    /// <summary>Builds the route for the selected job and moors the ship at the dock, camera snapped to it.</summary>
    private void PrepareHarbour()
    {
        if (GameSession.Instance != null) GameSession.Instance.SetJob(_selectedJob);
        if (LevelGenerator.Instance != null) LevelGenerator.Instance.GenerateLevel();
        PlaceShipAtStart();
        if (_vcam != null) _vcam.PreviousStateIsValid = false; // snap, don't slide
    }

    private void SetShipyardFraming(bool shipyard)
    {
        if (_composer == null) return;
        var composition = _composer.Composition;
        composition.ScreenPosition = new Vector2(shipyard ? ShipyardScreenX : 0f, 0f);
        _composer.Composition = composition;
    }

    private void SelectJob(JobSpec job)
    {
        _selectedJob = job;
        _confirmDamagedStart = false;

        _jobEasyBtn.EnableInClassList("active-job", job.Name == JobSpec.Easy.Name);
        _jobNormalBtn.EnableInClassList("active-job", job.Name == JobSpec.Normal.Name);
        _jobHardBtn.EnableInClassList("active-job", job.Name == JobSpec.Hard.Name);

        RefreshStartButton();
    }

    private void HandleStateChanged(GameState state)
    {
        var root = _document != null ? _document.rootVisualElement : null;
        if (root == null) return;

        // Hidden while sailing; the results panel owns the screen after a voyage.
        root.style.display = state == GameState.Playing ? DisplayStyle.None : DisplayStyle.Flex;
        _storePanel.style.display = state == GameState.Shipyard && !_loading ? DisplayStyle.Flex : DisplayStyle.None;
        if (state != GameState.Shipyard) _loadingCaption.style.display = DisplayStyle.None;
        SetShipyardFraming(state == GameState.Shipyard && !_loading);

        if (state == GameState.Shipyard)
        {
            string summary = GameSession.Instance != null ? GameSession.Instance.LastMissionSummary : "";
            _statusText.text = string.IsNullOrEmpty(summary) ? "Welcome aboard, Captain" : summary;
            RefreshUI();
        }
    }

    private void HandleHullChanged(int current, int max)
    {
        RefreshUI();
    }

    private ShipHull Hull()
    {
        var nav = ShipNavigator.Instance;
        return nav != null ? nav.GetComponentInChildren<ShipHull>() : null;
    }

    private void RefreshUI()
    {
        var session = GameSession.Instance;
        if (session == null || _creditsText == null) return;

        _creditsText.text = "$" + session.Credits;

        // Hull meter with one-click repair.
        var hull = Hull();
        if (hull != null)
        {
            _hullPips.Clear();
            for (int i = 0; i < hull.MaxHealthAmount; i++)
            {
                var pip = new VisualElement();
                pip.AddToClassList("hull-pip");
                if (i >= hull.CurrentHealth) pip.AddToClassList("hull-pip--empty");
                _hullPips.Add(pip);
            }
            _hullText.text = hull.CurrentHealth + "/" + hull.MaxHealthAmount;

            int missing = hull.MaxHealthAmount - hull.CurrentHealth;
            int repairCost = missing * GameSession.RepairCostPerHullPoint;
            if (missing <= 0)
            {
                _repairBtn.text = "FULL";
                _repairBtn.SetEnabled(false);
            }
            else
            {
                _repairBtn.text = session.Credits >= repairCost ? "REPAIR  $" + repairCost : "NEED $" + (repairCost - session.Credits);
                _repairBtn.SetEnabled(session.Credits >= repairCost);
            }
        }

        SetRouteText("route-easy", JobSpec.Easy);
        SetRouteText("route-normal", JobSpec.Normal);
        SetRouteText("route-hard", JobSpec.Hard);

        _engineLevelText.text = "ENGINE  Mk " + session.EngineLevel;
        _helmLevelText.text = "RUDDER  Mk " + session.HelmLevel;
        SetBuyButton(_upgradeEngineBtn, session.EngineUpgradeCost, session.Credits);
        SetBuyButton(_upgradeHelmBtn, session.HelmUpgradeCost, session.Credits);

        RefreshStartButton();
    }

    private void SetRouteText(string labelName, JobSpec job)
    {
        var label = _document.rootVisualElement.Q<Label>(labelName);
        if (label == null || LevelGenerator.Instance == null) return;

        var session = GameSession.Instance;
        string text = Mathf.RoundToInt(LevelGenerator.Instance.RouteLength(job)) + " m route";
        if (session != null && session.IsGentle(job)) text += " · learning layout";
        label.text = text;
    }

    private static void SetBuyButton(Button button, int cost, int credits)
    {
        bool affordable = credits >= cost;
        button.text = affordable ? "BUY  $" + cost : "NEED $" + (cost - credits) + " MORE";
        button.SetEnabled(affordable);
    }

    private void RefreshStartButton()
    {
        if (_startDetail == null) return;

        string name = _selectedJob.Name.Substring(0, 1) + _selectedJob.Name.Substring(1).ToLowerInvariant();
        _startDetail.text = $"{name} run · {_selectedJob.Containers} containers · up to ${_selectedJob.Reward}";

        var hull = Hull();
        bool damaged = hull != null && hull.CurrentHealth < hull.MaxHealthAmount;
        if (hull != null && hull.CurrentHealth <= 0)
        {
            _startWarning.text = "The hull is broken. Repair it before you sail.";
            _startTitle.text = "SET SAIL";
            _startMissionBtn.SetEnabled(false);
            return;
        }

        _startMissionBtn.SetEnabled(!_loading);
        if (damaged)
        {
            _startWarning.text = hull.CurrentHealth == 1
                ? "Hull 1/" + hull.MaxHealthAmount + ": one more hit sinks the ship. Repair first?"
                : "Hull " + hull.CurrentHealth + "/" + hull.MaxHealthAmount + ": the ship is damaged. Repair first?";
            _startTitle.text = _confirmDamagedStart ? "PRESS AGAIN TO SAIL" : "SET SAIL";
        }
        else
        {
            _startWarning.text = "";
            _startTitle.text = "SET SAIL";
        }
    }

    private void ShowToast(string message)
    {
        _toastText.text = message;
        _toastUntil = Time.unscaledTime + 2.5f;
    }

    private void OnUpgradeEngine()
    {
        var session = GameSession.Instance;
        if (session.TryBuyUpgrade("Engine", session.EngineUpgradeCost))
            ShowToast("Engine upgraded to Mk " + session.EngineLevel);
    }

    private void OnUpgradeHelm()
    {
        var session = GameSession.Instance;
        if (session.TryBuyUpgrade("Helm", session.HelmUpgradeCost))
            ShowToast("Rudder upgraded to Mk " + session.HelmLevel);
    }

    private void OnRepairHull()
    {
        var hull = Hull();
        if (hull == null) return;

        int missing = hull.MaxHealthAmount - hull.CurrentHealth;
        int cost = missing * GameSession.RepairCostPerHullPoint;
        if (missing <= 0 || GameSession.Instance.Credits < cost) return;

        GameSession.Instance.SpendCredits(cost);
        hull.RepairShip();
        _confirmDamagedStart = false;
        ShowToast("Hull repaired");
        RefreshUI();
    }

    private void OnStartMission()
    {
        if (_loading) return;

        var hull = Hull();
        if (hull != null && hull.CurrentHealth <= 0) return;

        // Sailing damaged takes a second press, so nobody leaves on one hull point by accident.
        if (hull != null && hull.CurrentHealth < hull.MaxHealthAmount && !_confirmDamagedStart)
        {
            _confirmDamagedStart = true;
            RefreshStartButton();
            return;
        }

        _confirmDamagedStart = false;
        _loading = true;
        _skipLoading = false;

        // The route is rebuilt for the chosen job and current credits; the dock sits at the
        // origin every time, so the moored ship doesn't move.
        PrepareHarbour();

        _storePanel.style.display = DisplayStyle.None;
        SetShipyardFraming(false);
        StartCoroutine(LoadContainersRoutine(_selectedJob.Containers));
    }

    private IEnumerator LoadContainersRoutine(int count)
    {
        var nav = ShipNavigator.Instance;
        if (nav == null) yield break;

        Transform shipTransform = nav.transform;

        // Fallback spot high and to the right in the shipyard for the containers to fly from
        Vector3 shipyardSpawnPoint = shipTransform.position + shipTransform.right * 15f + Vector3.up * 10f;

        // Find the actual StartPort model in the level
        GameObject startPort = GameObject.Find("StartPort");
        if (startPort != null)
        {
            shipyardSpawnPoint = startPort.transform.position + Vector3.up * 5f;
        }

        ClearContainers();
        _loadingCaption.style.display = DisplayStyle.Flex;

        // Calculate dynamic spacing from the first available prefab's BoxCollider
        Vector3 dynamicSpacing = ContainerSpacing;
        if (ContainerPrefabs != null && ContainerPrefabs.Count > 0)
        {
            foreach (var prefab in ContainerPrefabs)
            {
                if (prefab != null)
                {
                    BoxCollider col = prefab.GetComponentInChildren<BoxCollider>();
                    if (col != null)
                    {
                        Vector3 scale = prefab.transform.localScale;
                        // Add a tiny margin to prevent Z-fighting or clipping
                        dynamicSpacing = new Vector3(col.size.x * scale.x, col.size.y * scale.y, col.size.z * scale.z) + new Vector3(0.05f, 0.05f, 0.05f);
                    }
                    break;
                }
            }
        }

        for (int i = 0; i < count; i++)
        {
            _loadingCaption.text = $"LOADING CARGO {i + 1} / {count}  ·  SPACE TO SKIP";

            // Calculate grid position (Layer -> Row -> Col)
            int containersPerLayer = Columns * Rows;
            int layer = i / containersPerLayer;
            int remainder = i % containersPerLayer;
            int zIndex = remainder / Columns; // how deep (rows)
            int xIndex = remainder % Columns; // left to right (cols)

            // Center the grid around the offset
            float xOffset = (xIndex - (Columns - 1) / 2.0f) * dynamicSpacing.x;
            float zOffset = (zIndex - (Rows - 1) / 2.0f) * dynamicSpacing.z;
            float yOffset = layer * dynamicSpacing.y;

            Vector3 targetLocalPos = CargoStartOffset + new Vector3(xOffset, yOffset, -zOffset);
            GameObject container = SpawnContainer(shipyardSpawnPoint, shipTransform.rotation);
            // On the banking model so it rolls with the deck; no colliders, or they would join the
            // ship's physics and make it hit things with its cargo.
            container.transform.SetParent(nav.Movement != null ? nav.Movement.Visual : shipTransform);
            foreach (var col in container.GetComponentsInChildren<Collider>()) col.enabled = false;
            _spawnedContainers.Add(container);

            Vector3 targetWorldPos = container.transform.parent.TransformPoint(targetLocalPos);

            // Animate
            float duration = 0.3f;
            float elapsed = 0f;
            while (elapsed < duration && !_skipLoading)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;

                // Arc
                Vector3 currentPos = Vector3.Lerp(shipyardSpawnPoint, targetWorldPos, t);
                currentPos.y += Mathf.Sin(t * Mathf.PI) * 8f; // arc height

                container.transform.position = currentPos;
                yield return null;
            }
            container.transform.position = targetWorldPos;
        }

        _loadingCaption.text = "ALL ABOARD";
        if (!_skipLoading) yield return new WaitForSeconds(0.5f);
        _loadingCaption.style.display = DisplayStyle.None;

        _loading = false;
        GameSession.Instance.LoadCargo(_spawnedContainers.Count);
        GameManager.Instance.SetState(GameState.Playing);
    }

    private GameObject SpawnContainer(Vector3 position, Quaternion rotation)
    {
        GameObject prefab = ContainerPrefabs != null && ContainerPrefabs.Count > 0
            ? ContainerPrefabs[Random.Range(0, ContainerPrefabs.Count)]
            : null;
        if (prefab != null) return Instantiate(prefab, position, rotation);

        // Placeholder when no prefab is assigned.
        var container = GameObject.CreatePrimitive(PrimitiveType.Cube);
        container.transform.SetPositionAndRotation(position, rotation);
        container.transform.localScale = new Vector3(2f, 2.5f, 4.5f);
        var r = container.GetComponent<Renderer>();
        if (r != null) r.material.color = new Color(Random.value, Random.value, Random.value);
        return container;
    }

    /// <summary>A hit shakes containers off the deck: they fall into the sea and are not paid for.</summary>
    private void DropCargo(int containers)
    {
        if (GameSession.Instance == null) return;

        int lost = GameSession.Instance.LoseCargo(containers);
        for (int i = 0; i < lost && _spawnedContainers.Count > 0; i++)
        {
            GameObject box = _spawnedContainers[_spawnedContainers.Count - 1];
            _spawnedContainers.RemoveAt(_spawnedContainers.Count - 1);
            if (box == null) continue;

            box.transform.SetParent(null, true);
            foreach (var col in box.GetComponentsInChildren<Collider>()) col.enabled = false;
            var body = box.GetComponent<Rigidbody>();
            if (body == null) body = box.AddComponent<Rigidbody>();
            body.isKinematic = false;
            body.useGravity = true;
            body.AddForce((Vector3.up * 4f + Random.insideUnitSphere * 3f), ForceMode.VelocityChange);
            body.AddTorque(Random.insideUnitSphere * 2f, ForceMode.VelocityChange);
            Destroy(box, OverboardSeconds);
        }
        // ShipHull's damage report says how many went over.
    }

    private void ClearContainers()
    {
        foreach (var c in _spawnedContainers)
        {
            if (c != null) Destroy(c);
        }
        _spawnedContainers.Clear();
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
        nav.transform.eulerAngles = new Vector3(0f, nav.transform.eulerAngles.y, 0f);
    }
}
