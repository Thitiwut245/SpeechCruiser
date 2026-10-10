using UnityEngine;

/// <summary>
/// The live harbour behind the title screen: the level generator builds the real start
/// harbour and water, the ship is moored at the quay and bobs gently, and the camera drifts
/// slowly around it. The view is framed to the right of the title panel.
/// </summary>
public class MenuBackdrop : MonoBehaviour
{
    [SerializeField] private LevelGenerator Generator;
    [Tooltip("The ship model only (no gameplay components).")]
    [SerializeField] private GameObject ShipModel;
    [SerializeField] private Camera ViewCamera;

    [Header("Camera")]
    [SerializeField] private float OrthographicSize = 22f;
    [SerializeField] private float Distance = 80f;
    [SerializeField] private float Pitch = 35.26f;
    [SerializeField] private float BaseYaw = 45f;
    [Tooltip("How far the camera swings either side of its base angle, in degrees.")]
    [SerializeField] private float SwayDegrees = 10f;
    [SerializeField] private float SwayPeriodSeconds = 45f;
    [Tooltip("Moves the view left so the harbour sits to the right of the title panel (world units).")]
    [SerializeField] private float ScreenShift = 14f;


    private Transform _ship;
    private Vector3 _shipRest;
    private Vector3 _anchor;

    private void Start()
    {
        if (Generator == null) Generator = FindAnyObjectByType<LevelGenerator>();
        if (ViewCamera == null) ViewCamera = Camera.main;
        if (Generator == null || ViewCamera == null) return;

        Generator.GenerateLevel();

        _shipRest = Generator.StartPosition;
        if (ShipModel != null)
        {
            _ship = Instantiate(ShipModel, _shipRest, Generator.StartRotation).transform;
            // The model imports with its own root scale; the Game scene's ship overrides it
            // to 1, so match that here or the menu ship comes out tiny.
            _ship.localScale = Vector3.one;
            // Drawn as the procedural cargo ship, like the Game scene's.
            var oldRenderer = _ship.GetComponent<MeshRenderer>();
            if (oldRenderer != null) oldRenderer.enabled = false;
            var hull = new GameObject("HullVisual");
            hull.transform.SetParent(_ship, false);
            CargoShipModel.Dress(hull);
            ShipLook.Attach(hull);
        }

        // Look between the ship and the quay, a little closer to the ship.
        _anchor = _shipRest;
        var port = GameObject.Find("StartPort");
        if (port != null)
        {
            var bounds = new Bounds(port.transform.position, Vector3.zero);
            foreach (var r in port.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
            _anchor = Vector3.Lerp(_shipRest, new Vector3(bounds.center.x, 0f, bounds.center.z), 0.35f);
        }

        ViewCamera.orthographic = true;
        ViewCamera.orthographicSize = OrthographicSize;
        Place(0f);
    }

    private void Update()
    {
        if (ViewCamera == null) return;
        float t = Time.unscaledTime;
        Place(t);

        if (_ship != null)
        {
            // Moored, riding the same waves as the sea (SeaWaves).
            Vector3 forward = Generator.StartRotation * Vector3.forward;
            SeaWaves.HullPose(_shipRest, forward, 7.5f, 2.5f, out float heave, out float pitch, out float roll);
            _ship.position = _shipRest + Vector3.up * heave;
            _ship.rotation = Generator.StartRotation * Quaternion.Euler(pitch, 0f, roll);
        }
    }

    private void Place(float t)
    {
        float yaw = BaseYaw + Mathf.Sin(2f * Mathf.PI * t / SwayPeriodSeconds) * SwayDegrees;
        Quaternion rotation = Quaternion.Euler(Pitch, yaw, 0f);
        Vector3 focus = _anchor - (rotation * Vector3.right) * ScreenShift;
        ViewCamera.transform.SetPositionAndRotation(focus - (rotation * Vector3.forward) * Distance, rotation);
    }
}
