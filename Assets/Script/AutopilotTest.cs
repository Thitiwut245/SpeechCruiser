using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Developer tool: F9 sails the ship along the level's solution path, for testing levels
/// without talking. It removes itself from release builds, so players can never trigger it.
/// </summary>
public class AutopilotTest : MonoBehaviour
{
    [Header("Autopilot Settings")]
    [SerializeField] private bool _isActive = false;
    [SerializeField] private float _waypointRadius = 25f; // CellSize is 25, so this is 1 cell
    [SerializeField] private float _steerMultiplier = 1.5f;

    private int _currentWaypointIndex = 0;
    private List<Vector3> _path;

#if !(UNITY_EDITOR || DEVELOPMENT_BUILD)
    private void Awake()
    {
        Destroy(this);
    }
#endif

    private void Update()
    {
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
