using UnityEngine;

/// <summary>
/// Makes a small object ride the waves (a mine, a rowboat): it rises and falls with the water
/// under it and tilts with the slope, keeping its own heading. Its resting height is where it
/// was placed, which counts as calm water.
/// </summary>
public class SeaFloat : MonoBehaviour
{
    [Tooltip("Half the object's size: how far apart the slope is measured.")]
    [SerializeField] public float Radius = 1.5f;

    [Tooltip("How much of the slope it follows (0 stays level).")]
    [Range(0f, 1f)]
    [SerializeField] public float Tilt = 0.8f;

    private float _restY;
    private float _yaw;

    private void Start()
    {
        _restY = transform.position.y;
        _yaw = transform.eulerAngles.y;
    }

    private void Update()
    {
        Vector3 p = transform.position;
        float h = SeaWaves.HeightAt(p.x, p.z);
        float hx = SeaWaves.HeightAt(p.x + Radius, p.z) - SeaWaves.HeightAt(p.x - Radius, p.z);
        float hz = SeaWaves.HeightAt(p.x, p.z + Radius) - SeaWaves.HeightAt(p.x, p.z - Radius);
        transform.position = new Vector3(p.x, _restY + h, p.z);

        var normal = new Vector3(-hx * Tilt, 2f * Radius, -hz * Tilt).normalized;
        transform.rotation = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0f, _yaw, 0f);
    }
}
