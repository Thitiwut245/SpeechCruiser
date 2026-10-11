using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Watches every hazard around the ship.
///
/// Two answers, because they serve different people:
///  - Closest hazard (any direction): what the radar scope and "any danger?" report.
///  - Threat: the hazard actually on the ship's course, with its time to impact. Only a
///    threat triggers warnings, so a rock sliding past the beam stays quiet.
/// </summary>
public class RadarSystem : MonoBehaviour
{
    public static RadarSystem Instance;

    /// <summary>A hazard just came onto the ship's course (distance in metres).</summary>
    public static event System.Action<float> OnHazardDetected;

    /// <summary>The course is clear again.</summary>
    public static event System.Action OnPathClear;

    [Header("Radar Settings")]
    [FormerlySerializedAs("scanDistance")]
    [SerializeField] private float ScanDistance = 30f;

    [Tooltip("Sweep speed of the radar scope's beam. Cosmetic: detection scans all round every tick.")]
    [SerializeField] private float SpinSpeed = 180f;

    [Tooltip("Seconds between scans (real time, so slow motion doesn't delay warnings).")]
    [SerializeField] private float ScanInterval = 0.1f;

    [Header("Threat")]
    [Tooltip("A hazard is on course when it would pass closer than this to the centreline (half the hull width plus a margin).")]
    [SerializeField] private float PathHalfWidth = 5.5f;

    [Tooltip("Distance from the ship's pivot to its bow.")]
    [SerializeField] private float BowOffset = 8f;

    [Tooltip("A hazard on course becomes a threat when impact is closer than this many seconds.")]
    [SerializeField] private float ThreatSeconds = 6f;

    [Tooltip("At crawling speed, a hazard on course this close still counts as a threat.")]
    [SerializeField] private float ThreatRangeAtLowSpeed = 12f;

    [Header("Live Radar Telemetry (For AI Grounding)")]
    [FormerlySerializedAs("hazardDetected")]
    [SerializeField] private bool HazardDetected = false;

    [FormerlySerializedAs("hazardDistance")]
    [SerializeField] private float HazardDistance = 0f;

    [SerializeField] private float HazardAngle = 0f;
    [SerializeField] private bool ThreatDetected = false;
    [SerializeField] private float ThreatTimeToImpact = float.PositiveInfinity;

    private readonly Collider[] _hits = new Collider[128];
    private float _nextScan;
    private float _sweepAngle;

    // ---- Closest hazard, any direction ----
    public bool IsHazardDetected => HazardDetected;
    public float CurrentHazardDistance => HazardDistance;
    /// <summary>Bearing of the closest hazard relative to the bow, clockwise 0..360.</summary>
    public float CurrentHazardAngle => HazardAngle;
    public string CurrentHazardKind { get; private set; } = "Hazard";

    // ---- Threat: the hazard on the ship's course ----
    public bool IsThreat => ThreatDetected;
    public float ThreatDistance { get; private set; }
    /// <summary>Bearing of the threat relative to the bow, -180..180 (negative = port / left).</summary>
    public float ThreatBearing { get; private set; }
    public float TimeToImpact => ThreatTimeToImpact;
    public string ThreatKind { get; private set; } = "Hazard";
    /// <summary>Stable id of the threatening hazard, so alerts can escalate per hazard.</summary>
    public int ThreatId { get; private set; }

    /// <summary>The hazard on course, or null. The crew keep it to tell when it has really been passed.</summary>
    public Collider ThreatCollider { get; private set; }

    /// <summary>How far the radar sees: further with better lookout gear (GameSession.RadarRangeFactor).</summary>
    public float Range => ScanDistance * (GameSession.Instance != null ? GameSession.Instance.RadarRangeFactor : 1f);
    public float SweepAngle => _sweepAngle;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        _sweepAngle = (_sweepAngle + SpinSpeed * Time.unscaledDeltaTime) % 360f;

        if (Time.unscaledTime < _nextScan) return;
        _nextScan = Time.unscaledTime + ScanInterval;
        Scan();
    }

    private void Scan()
    {
        Vector3 pos = transform.position;
        Vector3 forward = Flat(transform.forward).normalized;
        float speed = ShipNavigator.Instance != null ? ShipNavigator.Instance.currentSpeed : 0f;
        Vector3 heading = speed < 0f ? -forward : forward;
        float absSpeed = Mathf.Abs(speed);

        int count = Physics.OverlapSphereNonAlloc(pos, Range, _hits, ~0, QueryTriggerInteraction.Collide);

        float closestDist = float.PositiveInfinity;
        Collider closest = null;
        float closestAngle = 0f;

        float bestTti = float.PositiveInfinity;
        Collider threat = null;
        float threatDist = 0f, threatBearing = 0f;

        for (int i = 0; i < count; i++)
        {
            var c = _hits[i];
            if (c == null || !c.CompareTag("Hazard") || c.transform.IsChildOf(transform)) continue;

            Vector3 to = Flat(c.ClosestPoint(pos) - pos);
            float dist = to.magnitude;

            if (dist < closestDist)
            {
                closestDist = dist;
                closest = c;
                closestAngle = (Vector3.SignedAngle(forward, to, Vector3.up) + 360f) % 360f;
            }

            // On course: ahead along the direction of travel, and within the path's width.
            float along = Vector3.Dot(to, heading);
            if (along <= 0f) continue;

            float across = Mathf.Abs(Vector3.Dot(to, Vector3.Cross(Vector3.up, heading)));
            if (across > PathHalfWidth) continue;

            float gap = Mathf.Max(0f, along - BowOffset);
            float tti = absSpeed > 0.1f ? gap / absSpeed : float.PositiveInfinity;
            bool isThreat = tti <= ThreatSeconds || (gap <= ThreatRangeAtLowSpeed && absSpeed > 0.3f);
            if (!isThreat || tti >= bestTti && threat != null) continue;

            bestTti = tti;
            threat = c;
            threatDist = gap;
            threatBearing = Vector3.SignedAngle(forward, to, Vector3.up);
        }

        HazardDetected = closest != null;
        HazardDistance = closest != null ? closestDist : 0f;
        HazardAngle = closestAngle;
        CurrentHazardKind = closest != null ? KindOf(closest) : "Hazard";

        bool wasThreat = ThreatDetected;
        ThreatDetected = threat != null;
        ThreatTimeToImpact = threat != null ? bestTti : float.PositiveInfinity;
        ThreatDistance = threatDist;
        ThreatBearing = threatBearing;
        ThreatKind = threat != null ? KindOf(threat) : "Hazard";
        ThreatId = threat != null ? threat.GetHashCode() : 0;
        ThreatCollider = threat;

        if (ThreatDetected && !wasThreat) OnHazardDetected?.Invoke(threatDist);
        else if (!ThreatDetected && wasThreat) OnPathClear?.Invoke();
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v;
    }

    /// <summary>"Mine", "Sandbank" or "Reef", from the hazard's object name (the generator spawns prefab copies).</summary>
    public static string KindOf(Collider c)
    {
        for (Transform t = c.transform; t != null; t = t.parent)
        {
            string n = t.name;
            if (n.IndexOf("mine", System.StringComparison.OrdinalIgnoreCase) >= 0) return "Mine";
            if (n.IndexOf("sandbank", System.StringComparison.OrdinalIgnoreCase) >= 0) return "Sandbank";
            if (n.IndexOf("shallows", System.StringComparison.OrdinalIgnoreCase) >= 0) return "Shallows";
            if (n.IndexOf("reef", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("rock", System.StringComparison.OrdinalIgnoreCase) >= 0) return "Reef";
        }
        return "Hazard";
    }
}
