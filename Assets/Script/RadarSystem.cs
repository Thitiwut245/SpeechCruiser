using UnityEngine;
using UnityEngine.Serialization;

public class RadarSystem : MonoBehaviour
{
    public static RadarSystem Instance;
    public static event System.Action<float> OnHazardDetected;
    public static event System.Action OnPathClear;

    [Header("Radar Settings")]
    [FormerlySerializedAs("scanDistance")]
    [SerializeField] private float ScanDistance = 30f; 

    [Header("Live Radar Telemetry (For AI Grounding)")]
    [FormerlySerializedAs("hazardDetected")]
    [SerializeField] private bool HazardDetected = false;
    
    [FormerlySerializedAs("hazardDistance")]
    [SerializeField] private float HazardDistance = 0f;

    public bool IsHazardDetected => HazardDetected;
    public float CurrentHazardDistance => HazardDistance;

    private void Awake()
    {
        Instance = this;
    }

    private void Update()
    {
        Debug.DrawRay(transform.position, transform.forward * ScanDistance, Color.green);

        RaycastHit hit;
        if (Physics.Raycast(transform.position, transform.forward, out hit, ScanDistance))
        {
            if (hit.collider.CompareTag("Hazard"))
            {
                HazardDetected = true;
                HazardDistance = hit.distance;

                OnHazardDetected?.Invoke(hit.distance);
            }
            else
            {
                HazardDetected = false;
                HazardDistance = 0f;
            }
        }
        else
        {
            HazardDetected = false;
            HazardDistance = 0f;

            if (ShipNavigator.Instance != null && ShipNavigator.Instance.currentSpeed > 0)
            {
                OnPathClear?.Invoke();
            }
        }
    }
}
