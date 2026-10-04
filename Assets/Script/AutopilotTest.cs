using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Developer tool: F9 sails the ship along the level's solution path, for testing levels
/// without talking. P is a speed run: it skips the drill, turns the autopilot on and runs the
/// game SpeedRunScale times faster until the ship docks (P again stops it). It removes itself
/// from release builds, so players can never trigger it.
/// </summary>
public class AutopilotTest : MonoBehaviour
{
    [Header("Autopilot Settings")]
    [SerializeField] private bool _isActive = false;
    [SerializeField] private float _waypointRadius = 25f; // CellSize is 25, so this is 1 cell
    [SerializeField] private float _steerMultiplier = 1.5f;

    [Header("Speed Run (P)")]
    [Tooltip("How many times faster the game runs during a speed run.")]
    [SerializeField] private float _speedRunScale = 10f;

    private int _currentWaypointIndex = 0;
    private List<Vector3> _path;
    private bool _speedRun;
    private UnityEngine.UIElements.Label _speedRunLabel;

    private static bool AtSea => GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.Playing;

#if !(UNITY_EDITOR || DEVELOPMENT_BUILD)
    private void Awake()
    {
        Destroy(this);
    }
#endif

    private void Update()
    {
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && !TypedOrders.BlocksHotkeys && kb.pKey.wasPressedThisFrame && (AtSea || _speedRun)) SetSpeedRun(!_speedRun);
        if (_speedRun && !AtSea) SetSpeedRun(false); // docked, sank or left: back to normal

        if (UnityEngine.InputSystem.Keyboard.current != null && !TypedOrders.BlocksHotkeys && UnityEngine.InputSystem.Keyboard.current.f9Key.wasPressedThisFrame)
        {
            _isActive = !_isActive;
            Debug.Log($"[AUTOPILOT] Toggled: {_isActive}");
            
            if (_isActive && GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.Playing)
            {
                InitializePath();
            }
            else if (!_isActive)
            {
                if (ShipNavigator.Instance != null)
                {
                    ShipNavigator.Instance.SetGear(ShipEngine.TelegraphGear.Stop);
                    ShipNavigator.Instance.RudderMidships();
                }
            }
        }

        if (_isActive && GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.Playing)
        {
            if (_path == null || _path.Count == 0)
            {
                InitializePath();
            }
            
            if (_path != null && _path.Count > 0)
            {
                Navigate();
            }
        }
    }

    /// <summary>P: autopilot on at high speed, drill skipped; or everything back to normal.</summary>
    private void SetSpeedRun(bool on)
    {
        _speedRun = on;
        if (TimeController.Instance != null) TimeController.Instance.SetSpeedRun(on ? _speedRunScale : 1f);

        if (on)
        {
            if (TutorialManager.Instance != null && TutorialManager.Instance.IsActive) TutorialManager.Instance.Skip();
            _isActive = true;
            InitializePath();
        }
        else if (_isActive)
        {
            _isActive = false;
            if (AtSea && ShipNavigator.Instance != null)
            {
                ShipNavigator.Instance.SetGear(ShipEngine.TelegraphGear.Stop);
                ShipNavigator.Instance.RudderMidships();
            }
        }

        ShowSpeedRunLabel(on);
        Debug.Log("[AUTOPILOT] Speed run " + (on ? "on (x" + _speedRunScale + ")" : "off") + ".");
    }

    private void ShowSpeedRunLabel(bool on)
    {
        if (_speedRunLabel == null)
        {
            var hud = FindAnyObjectByType<ShipHUD>();
            var doc = hud != null ? hud.GetComponent<UnityEngine.UIElements.UIDocument>() : null;
            if (doc == null) return;
            _speedRunLabel = new UnityEngine.UIElements.Label();
            var s = _speedRunLabel.style;
            s.position = UnityEngine.UIElements.Position.Absolute;
            s.top = 150f;
            s.left = UnityEngine.UIElements.Length.Percent(50f);
            s.translate = new UnityEngine.UIElements.Translate(UnityEngine.UIElements.Length.Percent(-50f), 0f);
            s.paddingLeft = 14f; s.paddingRight = 14f; s.paddingTop = 6f; s.paddingBottom = 6f;
            s.backgroundColor = new Color(0.55f, 0.1f, 0.1f, 0.92f);
            s.color = Color.white;
            s.fontSize = 22f;
            s.unityFontStyleAndWeight = FontStyle.Bold;
            s.borderTopLeftRadius = 6f; s.borderTopRightRadius = 6f; s.borderBottomLeftRadius = 6f; s.borderBottomRightRadius = 6f;
            _speedRunLabel.pickingMode = UnityEngine.UIElements.PickingMode.Ignore;
            doc.rootVisualElement.Add(_speedRunLabel);
        }
        _speedRunLabel.text = ">> SPEED RUN x" + _speedRunScale + "  ·  P to stop";
        _speedRunLabel.style.display = on ? UnityEngine.UIElements.DisplayStyle.Flex : UnityEngine.UIElements.DisplayStyle.None;
    }

    private void OnDestroy()
    {
        if (_speedRun && TimeController.Instance != null) TimeController.Instance.SetSpeedRun(1f);
    }

    private void InitializePath()
    {
        if (LevelGenerator.Instance != null && LevelGenerator.Instance.SolutionWorldPath != null)
        {
            _path = new List<Vector3>(LevelGenerator.Instance.SolutionWorldPath);
            
            // Find the closest waypoint to start from instead of strictly index 0
            _currentWaypointIndex = 0;
            var nav = ShipNavigator.Instance;
            if (nav != null && _path.Count > 0)
            {
                float closestDist = float.MaxValue;
                Vector3 currentPos = nav.transform.position;
                currentPos.y = 0;

                for (int i = 0; i < _path.Count; i++)
                {
                    Vector3 wp = _path[i];
                    wp.y = 0;
                    float dist = Vector3.Distance(currentPos, wp);
                    if (dist < closestDist)
                    {
                        closestDist = dist;
                        _currentWaypointIndex = i;
                    }
                }
                
                // Look ahead 1 waypoint
                if (_currentWaypointIndex < _path.Count - 1)
                {
                    _currentWaypointIndex++;
                }

                nav.SetGear(ShipEngine.TelegraphGear.Half);
            }
            
            Debug.Log($"[AUTOPILOT] Path acquired with {_path.Count} waypoints. Starting at index {_currentWaypointIndex}.");
        }
    }

    private void Navigate()
    {
        if (_currentWaypointIndex >= _path.Count)
        {
            return;
        }

        var nav = ShipNavigator.Instance;
        if (nav == null) return;

        Vector3 currentPos = nav.transform.position;
        Vector3 targetPos = _path[_currentWaypointIndex];
        
        currentPos.y = 0;
        targetPos.y = 0;
        
        float distance = Vector3.Distance(currentPos, targetPos);
        
        if (distance < _waypointRadius)
        {
            _currentWaypointIndex++;
            if (_currentWaypointIndex >= _path.Count)
            {
                Debug.Log("[AUTOPILOT] Reached final waypoint.");
                nav.SetGear(ShipEngine.TelegraphGear.Stop);
                nav.RudderMidships();
                _isActive = false; // Turn off
                return;
            }
            targetPos = _path[_currentWaypointIndex];
            targetPos.y = 0;
        }

        // Steer
        Vector3 direction = (targetPos - currentPos).normalized;
        float desiredHeading = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        float currentHeading = nav.transform.eulerAngles.y;
        
        float headingError = Mathf.DeltaAngle(currentHeading, desiredHeading);
        
        float rudderAngle = headingError * _steerMultiplier;
        
        nav.TurnShip(rudderAngle);

        if (Mathf.Abs(headingError) > 25f)
        {
            nav.SetGear(ShipEngine.TelegraphGear.Slow);
        }
        else
        {
            nav.SetGear(ShipEngine.TelegraphGear.Half);
        }
    }
}
