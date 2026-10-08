using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A ship's wake, drawn by the sea shader: the stern's track over the last few seconds, kept
/// as points (position, when it was laid, how fast the ship was going). The shader turns it
/// into churned white water behind the stern that widens and fades, plus the two arms of a
/// Kelvin wake spreading out from it. Attach to the ship; one wake is drawn at a time.
/// </summary>
public class ShipWake : MonoBehaviour
{
    public const int MaxPoints = 32;
    private const float PointSeconds = 0.3f;

    [Tooltip("How far behind the ship's centre its stern is, in metres.")]
    [SerializeField] private float SternOffset = 7f;

    [Tooltip("Speed (knots) at which the wake is at full strength.")]
    [SerializeField] private float FullSpeed = 9f;

    private static readonly int WakeId = Shader.PropertyToID("_SeaWake");
    private static readonly int WakeCountId = Shader.PropertyToID("_SeaWakeCount");
    private static readonly List<Vector4> Track = new List<Vector4>();
    private static readonly Vector4[] Buffer = new Vector4[MaxPoints];
    private static ShipWake _live;

    private ShipEngine _engine;
    private float _nextPoint;

    private void OnEnable()
    {
        _live = this;
        _engine = GetComponent<ShipEngine>();
    }

    private void OnDisable()
    {
        if (_live == this) _live = null;
    }

    /// <summary>Forgets every wake (a new level).</summary>
    public static void Clear() => Track.Clear();

    private void Update()
    {
        float speed = _engine != null ? Mathf.Abs(_engine.Speed) : 0f;
        Vector3 stern = transform.position - transform.forward * SternOffset;
        var point = new Vector4(stern.x, stern.z, SeaWaves.Now, Mathf.Clamp01(speed / FullSpeed));

        // The newest point rides the stern; a fixed one is left behind every PointSeconds.
        if (Track.Count == 0 || SeaWaves.Now >= _nextPoint)
        {
            _nextPoint = SeaWaves.Now + PointSeconds;
            Track.Add(point);
            if (Track.Count > MaxPoints) Track.RemoveAt(0);
        }
        else Track[Track.Count - 1] = point;
    }

    /// <summary>Hands the track to the shader (SeaSurface calls this each frame).</summary>
    public static void Push()
    {
        int n = Track.Count;
        for (int i = 0; i < MaxPoints; i++) Buffer[i] = i < n ? Track[i] : Vector4.zero;
        Shader.SetGlobalVectorArray(WakeId, Buffer);
        Shader.SetGlobalFloat(WakeCountId, n);
    }
}
