using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Two guides drawn on the water while sailing, so a learner sees what their orders do:
///   Turn arc     a solid line from the bow showing where the ship will go in the next few
///                seconds if no new order is given (ShipMovement.Predict, the same rules the ship
///                sails by). It bends as the rudder goes over, green to starboard, red to port.
///   Course       amber dots along the safe channel (Channel) to the destination berth, from where the
///                ship is now; they stop short of the berth, where its yellow outline takes over.
/// Shown only at sea. Sits on the ship (ShipMovement adds it); the lines are drawn in world space.
/// </summary>
public class CourseGuides : MonoBehaviour
{
    private const float ArcSeconds = 7f;
    private const float ArcStep = 0.1f;
    private const float BowOffset = 8f;        // the arc starts at the bow, not mid-ship
    private const float MinSpeed = 0.3f;       // stopped: nothing to predict
    private const float CourseLength = 220f;   // how far ahead the dots run
    private const float CourseHideNear = 35f;  // near the berth its outline is the guide
    private const float Lift = 0.35f;          // above the waves

    private static readonly Color Straight = new Color(1f, 1f, 1f);
    private static readonly Color Port = new Color(1f, 0.38f, 0.32f);
    private static readonly Color Starboard = new Color(0.35f, 0.95f, 0.5f);
    private static readonly Color Amber = new Color(1f, 0.82f, 0.2f);

    private static Material _solid, _dotted;

    private ShipMovement _movement;
    private ShipHelm _helm;
    private LineRenderer _arc, _course;
    private readonly List<Vector3> _predicted = new List<Vector3>();
    private readonly List<Vector3> _drawn = new List<Vector3>();

    private int _progress; // the channel point last reached (Channel)

    private void Awake()
    {
        _movement = GetComponent<ShipMovement>();
        _helm = GetComponent<ShipHelm>();
        Materials();
        _arc = Line("TurnArc", _solid, 0.55f);
        _course = Line("CourseDots", _dotted, 0.6f);
        _course.textureMode = LineTextureMode.Tile;
        _course.textureScale = new Vector2(1f / 2.4f, 1f); // a dot every 2.4 m
    }

    private void OnDestroy()
    {
        if (_arc != null) Destroy(_arc.gameObject);
        if (_course != null) Destroy(_course.gameObject);
    }

    private void LateUpdate()
    {
        bool atSea = GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.Playing;
        DrawArc(atSea);
        DrawCourse(atSea);
    }

    // ------------------------------------------------------------ turn arc

    private void DrawArc(bool atSea)
    {
        var engine = _movement != null ? _movement.GetComponent<ShipEngine>() : null;
        bool show = atSea && _movement != null && engine != null && Mathf.Abs(engine.Speed) > MinSpeed;
        _arc.enabled = show;
        if (!show) return;

        _movement.Predict(_predicted, ArcSeconds, ArcStep);
        // From the bow (or the stern going astern) onward.
        _drawn.Clear();
        Vector3 start = _predicted[0];
        foreach (var p in _predicted)
            if ((p - start).sqrMagnitude >= BowOffset * BowOffset) _drawn.Add(OnWater(p));
        if (_drawn.Count < 2) { _arc.enabled = false; return; }

        _arc.positionCount = _drawn.Count;
        _arc.SetPositions(_drawn.ToArray());

        // Coloured by the side the rudder is over, the way the HUD's rudder arc is.
        float rudder = _helm != null && _helm.MaxAngle > 0f ? _helm.CurrentRudderAngle / _helm.MaxAngle : 0f;
        Color side = Color.Lerp(Straight, rudder < 0f ? Port : Starboard, Mathf.Clamp01(Mathf.Abs(rudder) * 2.5f));
        _arc.colorGradient = Fade(side, 0.9f);
    }

    // ------------------------------------------------------------ course to the berth

    private void DrawCourse(bool atSea)
    {
        Vector3 berth = Quay.Destination != null ? Quay.Destination.Berth : Vector3.zero;
        bool show = atSea && Quay.Destination != null && Channel.Ready &&
                    Flat(transform.position - berth).magnitude > CourseHideNear;
        _course.enabled = show;
        if (!show) return;

        // The ship only moves forward along the channel: search on from the last point reached.
        var points = Channel.Points;
        if (_progress >= points.Count) _progress = 0;
        _progress = Channel.Nearest(transform.position, Mathf.Max(0, _progress - 3), out _);

        _drawn.Clear();
        _drawn.Add(OnWater(transform.position + transform.forward * BowOffset));
        float run = 0f;
        for (int i = _progress + 1; i < points.Count && run < CourseLength; i++)
        {
            Vector3 p = points[i];
            if (Flat(p - berth).magnitude < CourseHideNear * 0.6f) break; // the outline takes over
            run += Flat(p - _drawn[_drawn.Count - 1]).magnitude;
            _drawn.Add(OnWater(p));
        }
        if (_drawn.Count < 2) { _course.enabled = false; return; }

        _course.positionCount = _drawn.Count;
        _course.SetPositions(_drawn.ToArray());
        _course.colorGradient = Fade(Amber, 0.85f);
    }

    // ------------------------------------------------------------ drawing

    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    private static Vector3 OnWater(Vector3 p)
    {
        p.y = SeaWaves.Displacement(p.x, p.z, SeaWaves.Now).y + Lift;
        return p;
    }

    /// <summary>Full colour near the ship, fading out at the far end.</summary>
    private static Gradient Fade(Color c, float alpha)
    {
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                  new[] { new GradientAlphaKey(alpha, 0f), new GradientAlphaKey(alpha, 0.6f), new GradientAlphaKey(0f, 1f) });
        return g;
    }

    private LineRenderer Line(string name, Material material, float width)
    {
        var go = new GameObject(name);
        // Lies flat on the water: the line faces its transform's Z, turned to point up.
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.alignment = LineAlignment.TransformZ;
        line.sharedMaterial = material;
        line.widthMultiplier = width;
        line.numCapVertices = 4;
        line.numCornerVertices = 3;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.enabled = false;
        return line;
    }

    /// <summary>Sprites/Default: vertex colour and alpha, always in the build (GraphicsSettings).</summary>
    private static void Materials()
    {
        if (_solid != null) return;
        var shader = Shader.Find("Sprites/Default");
        _solid = new Material(shader) { name = "Turn arc", renderQueue = 3100 };
        // One round dot at the start of a 64x16 tile: the line is 0.6 m wide and a tile 2.4 m long
        // (textureScale), so the dot is round and the gap three dots long.
        var dots = new Texture2D(64, 16, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, name = "Course dots" };
        var px = new Color32[64 * 16];
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 64; x++)
            {
                float d = new Vector2(x - 8f, y - 7.5f).magnitude;
                byte a = (byte)(255f * Mathf.Clamp01(7f - d));
                px[y * 64 + x] = new Color32(255, 255, 255, a);
            }
        dots.SetPixels32(px);
        dots.Apply();
        _dotted = new Material(shader) { name = "Course dots", mainTexture = dots, renderQueue = 3100 };
    }
}
