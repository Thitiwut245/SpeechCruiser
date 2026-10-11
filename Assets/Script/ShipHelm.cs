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

    /// <summary>
    /// The orders on the helm and where the rudder stands: everything one frame steps. Kept in
    /// one value so CourseGuides can copy it and step the copy ahead to draw where the ship goes.
    /// </summary>
    public struct State
    {
        public float Rudder;       // the blade now, degrees (+ = starboard)
        public float Target;       // where it is swinging to
        public bool Ordered;       // a turn order is holding the rudder over
        public float HoldLeft;     // seconds before a plain turn order eases off
        public bool Autopilot;     // steering for a heading
        public float Heading;      // the heading ordered
        public float Direction;    // the side ordered (+1 starboard, -1 port)
        public float MaxRudder;    // the rudder the helmsman may use on the autopilot
    }

    private State _s = new State { MaxRudder = 35f };
    private ShipMovement _movement;

    private void Awake()
    {
        _movement = GetComponent<ShipMovement>();
    }

    public float CurrentRudderAngle => _s.Rudder;
    public float TargetRudderAngle => _s.Target;
    public float MaxAngle => MaxRudderAngle;

    /// <summary>
    /// Rudder the helmsman holds when no order is on, in degrees (PathAssist, story mode): the
    /// blade eases to this instead of to midships. Orders always come first.
    /// </summary>
    public float Assist { get; set; }

    /// <summary>A copy of the helm as it stands, to step ahead with <see cref="Step"/>.</summary>
    public State Snapshot => _s;

    private void Update()
    {
        bool steering = _s.Autopilot;
        float rate = _movement != null ? _movement.TurnRate : 0f;
        Step(ref _s, transform.eulerAngles.y, rate, Time.deltaTime);
        if (steering && !_s.Autopilot) Debug.Log($"🚢 [HELM]: Steady on {NormalizeHeading(_s.Heading):F0}°.");
    }

    /// <param name="maxRudder">Rudder the helmsman may use; 0 or less means hard over.</param>
    public void OrderHeadingChange(float degreesToTurn, float maxRudder = 0f)
    {
        _s.MaxRudder = maxRudder > 0f ? Mathf.Min(maxRudder, MaxRudderAngle) : MaxRudderAngle;
        _s.Autopilot = true;
        _s.Ordered = true;
        _s.Heading = transform.eulerAngles.y + degreesToTurn;
        _s.Direction = Mathf.Sign(degreesToTurn);

        string direction = degreesToTurn < 0 ? "PORT" : "STARBOARD";
        Debug.Log($"🚢 [HELM]: Coming {Mathf.Abs(degreesToTurn):F0} degrees {direction}, steering for {NormalizeHeading(_s.Heading):F0}°.");
    }

    public void TurnShip(float rudderDegrees)
    {
        _s.Autopilot = false;
        float newTarget = Mathf.Clamp(rudderDegrees, -MaxRudderAngle, MaxRudderAngle);
        
        bool isNewCommand = Mathf.Abs(newTarget - _s.Target) > 1f || !_s.Ordered;
        
        _s.Target = newTarget;
        _s.Ordered = true;
        _s.HoldLeft = RudderHoldTime;

        if (isNewCommand)
        {
            string direction = rudderDegrees < 0 ? "PORT" : "STARBOARD";
            Debug.Log($"🚢 [HELM]: Rudder {Mathf.Abs(_s.Target):F0}° {direction}.");
        }
    }

    public void HardTurn(float direction)
    {
        TurnShip(direction < 0 ? -MaxRudderAngle : MaxRudderAngle);
        Debug.Log("🚢 [HELM]: HARD OVER!");
    }

    public void RudderMidships()
    {
        Midships(ref _s);
        Debug.Log("🚢 [HELM]: Rudder amidships. Steady as she goes.");
    }

    private static void Midships(ref State s)
    {
        s.Autopilot = false;
        s.Target = 0f;
        s.Ordered = false;
        s.HoldLeft = 0f;
    }

    /// <summary>
    /// One frame of the helm: the autopilot sets the rudder for the ordered heading, then the
    /// blade swings toward its target and a plain turn order times out. <paramref name="heading"/>
    /// and <paramref name="turnRate"/> are the bow's, now or as predicted.
    /// </summary>
    public void Step(ref State s, float heading, float turnRate, float dt)
    {
        StepAutopilot(ref s, heading, turnRate);
        StepRudder(ref s, dt);
    }

    private void StepAutopilot(ref State s, float heading, float turnRate)
    {
        if (!s.Autopilot) return;

        // A heavy hull keeps swinging after the rudder comes off, so steer on where the
        // bow will be once the swing stops (omega^2 / 2a), not where it is now.
        if (_movement != null && _movement.YawAcceleration > 0f)
            heading += Mathf.Sign(turnRate) * turnRate * turnRate / (2f * _movement.YawAcceleration);
        float error = Mathf.DeltaAngle(heading, s.Heading);

        if (Mathf.Abs(error) <= AutopilotTolerance)
        {
            Midships(ref s);
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
            ? s.MaxRudder * s.Direction
            : error * AutopilotGain;

        // Going astern the rudder swings the bow the other way, so the helmsman reverses it.
        if (_movement != null) rudder *= _movement.RudderEffect;

        s.Target = Mathf.Clamp(rudder, -s.MaxRudder, s.MaxRudder);
        s.Ordered = true;
    }

    private void StepRudder(ref State s, float dt)
    {
        // The rudder upgrade swings the blade faster too (ShipMovement applies it to the turn itself).
        float actualRudderSpeed = RudderSpeed * (GameSession.Instance != null ? GameSession.Instance.HelmTurnFactor : 1f);

        s.Rudder = Mathf.MoveTowards(s.Rudder, s.Target, actualRudderSpeed * dt);

        if (s.Autopilot) return;

        if (s.Ordered)
        {
            s.HoldLeft -= dt;
            if (s.HoldLeft <= 0f) s.Ordered = false;
            return;
        }

        if (AutoCenterRudder)
        {
            s.Target = Mathf.MoveTowards(s.Target, Assist, actualRudderSpeed * 0.5f * dt);
        }
    }

    private static float NormalizeHeading(float heading)
    {
        return ((heading % 360f) + 360f) % 360f;
    }
}
