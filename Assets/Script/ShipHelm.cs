using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Arcade helm. The rudder still swings rather than snapping, so orders read as
/// orders, but it swings fast enough that a "hard starboard" bites within a few
/// frames instead of a second and a half. The autopilot steers to an absolute
/// target heading with a proportional rudder, which is what stops it sailing
/// past the ordered course the way the old dead-reckoned version did.
/// </summary>
public class ShipHelm : MonoBehaviour
{
    [Header("Rudder")]
    [Tooltip("Maximum rudder deflection in degrees. This is the full-scale reading on the rudder gauge.")]
    [FormerlySerializedAs("maxRudderAngle")] [SerializeField] private float MaxRudderAngle = 35f;

    [Tooltip("How fast the rudder swings to its ordered angle (degrees/sec). High on purpose — arcade response.")]
    [FormerlySerializedAs("rudderSpeed")] [SerializeField] private float RudderSpeed = 140f;

    [Tooltip("Rudder eases back to amidships once a standing turn order times out.")]
    [FormerlySerializedAs("autoCenterRudder")] [SerializeField] private bool AutoCenterRudder = true;

    [Tooltip("How long a plain turn order (\"port twenty\") holds the rudder over before it eases back. " +
             "Together with ShipMovement.TurningPower this is what sets the size of the course change: " +
             "~50° for a standard order, ~80° for hard over.")]
    [SerializeField] private float RudderHoldTime = 2.5f;

    [Header("Autopilot")]
    [Tooltip("Degrees of rudder applied per degree of heading error. Low enough that the rudder starts easing " +
             "well before the ordered course, so the ship settles on it instead of swinging through.")]
    [SerializeField] private float AutopilotGain = 1.2f;

    [Tooltip("Heading error, in degrees, at which the autopilot calls the turn complete.")]
    [SerializeField] private float AutopilotTolerance = 1f;

    private float _targetRudderAngle = 0f;
    private float _currentRudderAngle = 0f;
    private bool _isRudderCommandActive = false;
    private float _rudderHoldTimer = 0f;

    private bool _isAutopilotActive = false;
    private float _targetHeading = 0f;
    private float _autopilotDirection = 1f;

    public float CurrentRudderAngle => _currentRudderAngle;
    public float TargetRudderAngle => _targetRudderAngle;
    public float MaxAngle => MaxRudderAngle;

    private void Update()
    {
        UpdateAutopilot();
        UpdateRudder();
    }

    public void OrderHeadingChange(float degreesToTurn)
    {
        _isAutopilotActive = true;
        _isRudderCommandActive = true;
        _targetHeading = transform.eulerAngles.y + degreesToTurn;
        _autopilotDirection = Mathf.Sign(degreesToTurn);

        string direction = degreesToTurn < 0 ? "PORT" : "STARBOARD";
        Debug.Log($"🚢 [HELM]: Coming {Mathf.Abs(degreesToTurn):F0} degrees {direction}, steering for {NormalizeHeading(_targetHeading):F0}°.");
    }

    public void TurnShip(float rudderDegrees)
    {
        _isAutopilotActive = false;
        _targetRudderAngle = Mathf.Clamp(rudderDegrees, -MaxRudderAngle, MaxRudderAngle);
        _isRudderCommandActive = true;
        _rudderHoldTimer = RudderHoldTime;

        string direction = rudderDegrees < 0 ? "PORT" : "STARBOARD";
        Debug.Log($"🚢 [HELM]: Rudder {Mathf.Abs(_targetRudderAngle):F0}° {direction}.");
    }

    public void HardTurn(float direction)
    {
        TurnShip(direction < 0 ? -MaxRudderAngle : MaxRudderAngle);
        Debug.Log("🚢 [HELM]: HARD OVER!");
    }

    public void RudderMidships()
    {
        _isAutopilotActive = false;
        _targetRudderAngle = 0f;
        _isRudderCommandActive = false;
        _rudderHoldTimer = 0f;
        Debug.Log("🚢 [HELM]: Rudder amidships. Steady as she goes.");
    }

    private void UpdateAutopilot()
    {
        if (!_isAutopilotActive) return;

        float error = Mathf.DeltaAngle(transform.eulerAngles.y, _targetHeading);

        if (Mathf.Abs(error) <= AutopilotTolerance)
        {
            Debug.Log($"🚢 [HELM]: Steady on {NormalizeHeading(_targetHeading):F0}°.");
            RudderMidships();
            return;
        }

        // Proportional rudder: hard over while the error is large, easing off as
        // the bow comes round so the ship settles on the course instead of
        // swinging through it.
        //
        // Past 90° of error the rudder is pinned to the ordered side instead.
        // DeltaAngle is ambiguous at exactly 180°, so an ordered "starboard one
        // eighty" would otherwise come round to port.
        float rudder = Mathf.Abs(error) > 90f
            ? MaxRudderAngle * _autopilotDirection
            : error * AutopilotGain;

        _targetRudderAngle = Mathf.Clamp(rudder, -MaxRudderAngle, MaxRudderAngle);
        _isRudderCommandActive = true;
    }

    private void UpdateRudder()
    {
        float speedMultiplier = 1f;
        if (GameManager.Instance != null)
        {
            speedMultiplier += (GameManager.Instance.HelmLevel - 1) * 0.3f; // 30% faster rudder per level
        }
        float actualRudderSpeed = RudderSpeed * speedMultiplier;

        _currentRudderAngle = Mathf.MoveTowards(_currentRudderAngle, _targetRudderAngle, actualRudderSpeed * Time.deltaTime);

        if (_isAutopilotActive) return;

        if (_isRudderCommandActive)
        {
            _rudderHoldTimer -= Time.deltaTime;
            if (_rudderHoldTimer <= 0f) _isRudderCommandActive = false;
            return;
        }

        if (AutoCenterRudder)
        {
            _targetRudderAngle = Mathf.MoveTowards(_targetRudderAngle, 0f, actualRudderSpeed * 0.5f * Time.deltaTime);
        }
    }

    private static float NormalizeHeading(float heading)
    {
        return ((heading % 360f) + 360f) % 360f;
    }
}
