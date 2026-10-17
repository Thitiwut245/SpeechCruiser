using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The low-poly models of the story encounters, built in code from LowPolyMesh shapes like the
/// rest of the story's things: the Kraken's arm (a chain of tapering joints that sways, rises out
/// of the sea and slams down), its head, a pirate skiff, and the navy frigate. No colliders: they
/// never touch the ship's physics; the encounters decide what hits.
/// </summary>
public static class EncounterModels
{
    // An octopus of the deep: dark slate-blue skin, a paler underside, cream suckers with dark rims.
    public static readonly Color KrakenSkin = new Color(0.2f, 0.27f, 0.35f);
    public static readonly Color KrakenDark = new Color(0.13f, 0.17f, 0.23f);
    public static readonly Color KrakenBelly = new Color(0.52f, 0.58f, 0.64f);
    private static readonly Color Sucker = new Color(0.9f, 0.85f, 0.74f);
    private static readonly Color SuckerRim = new Color(0.3f, 0.32f, 0.36f);
    private static readonly Color Eye = new Color(0.98f, 0.8f, 0.35f);
    private static readonly Color Pupil = new Color(0.04f, 0.04f, 0.05f);

    /// <summary>
    /// A Kraken arm, standing on its root at <paramref name="at"/>: fourteen joints tapering to a fine
    /// tip, dark above and pale below, with a row of suckers down the underside that shrink toward
    /// the tip. Its TentacleRig curls the tip into a spiral, sways it, raises and slams it.
    /// </summary>
    public static TentacleRig Tentacle(Transform parent, Vector3 at, float length, float radius)
    {
        var root = new GameObject("KrakenArm").transform;
        root.SetParent(parent, false);
        root.position = at;
        const int segments = 14;
        float seg = length / segments;
        var joints = new List<Transform>();
        Transform holder = root;
        for (int i = 0; i < segments; i++)
        {
            var joint = new GameObject("Joint" + i).transform;
            joint.SetParent(holder, false);
            joint.localPosition = i == 0 ? Vector3.zero : Vector3.up * seg;
            float k0 = i / (float)segments, k1 = (i + 1) / (float)segments;
            float r0 = radius * Mathf.Lerp(1f, 0.08f, Mathf.Pow(k0, 0.8f));
            float r1 = radius * Mathf.Lerp(1f, 0.08f, Mathf.Pow(k1, 0.8f));
            var m = new LowPolyMesh();
            // Back and belly: the pale underside is a flattened band on the sucker side (+Z).
            m.Frustum(Vector3.zero, r0, r1, seg * 1.06f, 10, 1f, 0.92f, KrakenSkin, i == segments - 1, false);
            m.Frustum(new Vector3(0f, 0f, r0 * 0.28f), r0 * 0.78f, r1 * 0.78f, seg * 1.06f, 8, 0.95f, 0.8f, KrakenBelly, false, false);
            // Suckers: a pair across the underside, on every joint but the very tip.
            if (i < segments - 1)
                foreach (float x in new[] { -0.36f, 0.36f })
                {
                    float r = r0 * 0.36f;
                    Vector3 c = new Vector3(x * r0, seg * (x < 0f ? 0.3f : 0.75f), r0 * 0.92f);
                    m.Ellipsoid(c, new Vector3(r * 1.15f, r * 1.15f, r * 0.45f), 8, 3, SuckerRim, 0f, 1f);
                    m.Ellipsoid(c + new Vector3(0f, 0f, r * 0.25f), new Vector3(r * 0.8f, r * 0.8f, r * 0.35f), 8, 3, Sucker, 0f, 1f);
                }
            var drawn = new GameObject("Segment");
            drawn.transform.SetParent(joint, false);
            m.AttachTo(drawn);
            joints.Add(joint);
            holder = joint;
        }
        var rig = root.gameObject.AddComponent<TentacleRig>();
        rig.Init(joints, length);
        return rig;
    }

    /// <summary>
    /// The Kraken's head breaking the surface, as an octopus holds it: a great rounded mantle
    /// leaning back over a broad brow, big eyes bulging on each side with slit pupils, and a dark
    /// mottling over the skin. Faces +Z (toward the ship).
    /// </summary>
    public static GameObject Head(Transform parent, Vector3 at, float yaw)
    {
        var go = new GameObject("KrakenHead");
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));

        // The brow and the base the arms grow from.
        var brow = new LowPolyMesh();
        brow.Ellipsoid(new Vector3(0f, 1.2f, 0.8f), new Vector3(6.2f, 3.6f, 5.6f), 16, 8, KrakenSkin, 0f, 1f);
        brow.Ellipsoid(new Vector3(0f, -0.6f, 2.2f), new Vector3(4.6f, 2.2f, 3.6f), 12, 6, KrakenBelly, 0f, 1f);
        var b = new GameObject("Brow");
        b.transform.SetParent(go.transform, false);
        brow.AttachTo(b);

        // The mantle: a tall bulb leaning back behind the eyes, with darker blotches.
        var mantle = new LowPolyMesh();
        mantle.Ellipsoid(new Vector3(0f, 6.5f, 0f), new Vector3(5.6f, 8.5f, 6.2f), 16, 12, KrakenSkin, 0f, 1f);
        var rng = new System.Random(7);
        for (int i = 0; i < 9; i++)
        {
            float a = (float)rng.NextDouble() * Mathf.PI * 2f, h = 3f + (float)rng.NextDouble() * 8f;
            float ring = Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow((h - 6.5f) / 8.5f, 2f)));
            Vector3 c = new Vector3(Mathf.Cos(a) * 5.4f * ring, h, Mathf.Sin(a) * 6f * ring);
            mantle.Ellipsoid(c, new Vector3(1.1f, 0.8f, 1.1f), 6, 4, KrakenDark, 0f, 1f);
        }
        var mt = new GameObject("Mantle");
        mt.transform.SetParent(go.transform, false);
        mt.transform.localPosition = new Vector3(0f, 1.5f, -2.5f);
        mt.transform.localRotation = Quaternion.Euler(-28f, 0f, 0f);
        mantle.AttachTo(mt);

        foreach (float side in new[] { -1f, 1f })
        {
            // A bulging eye socket, the eye, and a horizontal slit pupil.
            var socket = new LowPolyMesh();
            socket.Ellipsoid(new Vector3(side * 4.6f, 3f, 2.4f), new Vector3(2.3f, 2.1f, 2.3f), 10, 6, KrakenSkin, 0f, 1f);
            var s = new GameObject("Socket");
            s.transform.SetParent(go.transform, false);
            socket.AttachTo(s);

            var eye = new LowPolyMesh();
            eye.Ellipsoid(new Vector3(side * 5.2f, 3.2f, 3.6f), new Vector3(1.4f, 1.3f, 1.2f), 10, 6, Eye, 0f, 1f);
            var e = new GameObject("Eye");
            e.transform.SetParent(go.transform, false);
            eye.AttachTo(e, castShadows: false, glow: 1.6f);

            var pupil = new LowPolyMesh();
            pupil.Ellipsoid(new Vector3(side * 5.55f, 3.2f, 4.65f), new Vector3(0.95f, 0.28f, 0.25f), 8, 4, Pupil, 0f, 1f);
            var pp = new GameObject("Pupil");
            pp.transform.SetParent(go.transform, false);
            pupil.AttachTo(pp, castShadows: false);
        }
        return go;
    }

    /// <summary>A pirate skiff: a long narrow open boat with an outboard and three men in it.</summary>
    public static GameObject Skiff(Transform parent, Color hull)
    {
        var go = new GameObject("PirateSkiff");
        go.transform.SetParent(parent, false);
        var m = new LowPolyMesh();
        var centres = new List<Vector3> { new Vector3(0, 0.25f, -3.2f), new Vector3(0, 0.1f, -1.5f), new Vector3(0, 0f, 0.5f), new Vector3(0, 0.15f, 2.4f), new Vector3(0, 0.55f, 3.6f) };
        var radii = new List<Vector2> { new Vector2(0.75f, 0.45f), new Vector2(0.95f, 0.55f), new Vector2(0.95f, 0.55f), new Vector2(0.7f, 0.45f), new Vector2(0.12f, 0.15f) };
        m.Loft(centres, radii, 10, hull, new Color(0.15f, 0.15f, 0.17f));
        m.Box(new Vector3(0f, 0.55f, -3.45f), new Vector3(0.45f, 0.9f, 0.4f), new Color(0.12f, 0.12f, 0.13f)); // outboard
        Color[] shirts = { new Color(0.75f, 0.2f, 0.15f), new Color(0.2f, 0.25f, 0.3f), new Color(0.85f, 0.75f, 0.3f) };
        float[] zs = { -2.2f, -0.4f, 1.4f };
        for (int i = 0; i < 3; i++)
        {
            m.Frustum(new Vector3(0f, 0.2f, zs[i]), 0.32f, 0.26f, 0.9f, 6, 1f, 1f, shirts[i], true, false);
            m.Ellipsoid(new Vector3(0f, 1.32f, zs[i]), new Vector3(0.22f, 0.25f, 0.22f), 6, 4, new Color(0.45f, 0.3f, 0.2f), 0f, 1f);
            m.Ellipsoid(new Vector3(0f, 1.45f, zs[i]), new Vector3(0.24f, 0.13f, 0.24f), 6, 3, new Color(0.1f, 0.1f, 0.1f), 0.5f, 1f); // scarf/cap
        }
        var drawn = new GameObject("Hull");
        drawn.transform.SetParent(go.transform, false);
        m.AttachTo(drawn);
        return go;
    }

    /// <summary>The navy frigate: long grey hull, a stepped superstructure, a mast and a gun forward.</summary>
    public static GameObject Frigate(Transform parent)
    {
        var go = new GameObject("NavyFrigate");
        go.transform.SetParent(parent, false);
        Color grey = new Color(0.55f, 0.58f, 0.62f), dark = new Color(0.32f, 0.34f, 0.37f), deck = new Color(0.42f, 0.44f, 0.46f);
        var m = new LowPolyMesh();
        var centres = new List<Vector3>();
        var radii = new List<Vector2>();
        float[] z = { -18f, -12f, -4f, 6f, 13f, 18f, 21f };
        float[] w = { 3.4f, 4.2f, 4.4f, 4.3f, 3.3f, 1.8f, 0.2f };
        float[] h = { 2.6f, 2.8f, 2.8f, 2.8f, 2.9f, 3.1f, 3.3f };
        for (int i = 0; i < z.Length; i++) { centres.Add(new Vector3(0f, 0.6f, z[i])); radii.Add(new Vector2(w[i], h[i])); }
        m.Loft(centres, radii, 12, grey, dark, -0.1f);
        m.Box(new Vector3(0f, 3.2f, 0f), new Vector3(7f, 0.25f, 34f), deck);
        m.Box(new Vector3(0f, 5f, -3f), new Vector3(6f, 3.6f, 10f), grey);
        m.Box(new Vector3(0f, 7.6f, -1f), new Vector3(4.6f, 2f, 5f), grey);
        m.Box(new Vector3(0f, 7.9f, 1.55f), new Vector3(4.4f, 0.8f, 0.1f), new Color(0.12f, 0.16f, 0.22f)); // bridge windows
        m.Frustum(new Vector3(0f, 8.6f, -2f), 0.35f, 0.18f, 7f, 6, 1f, 1f, dark, true, false);           // mast
        m.Box(new Vector3(0f, 13.5f, -2f), new Vector3(3f, 0.3f, 0.3f), dark);
        m.Frustum(new Vector3(0f, 3.3f, 10f), 1.4f, 1.1f, 1.3f, 8, 1f, 1f, grey, true, true);              // gun turret
        m.Box(new Vector3(0f, 4.1f, 12.2f), new Vector3(0.35f, 0.35f, 3.4f), dark);                         // barrel
        m.Box(new Vector3(0f, 1.2f, 15f), new Vector3(0.1f, 1f, 2.4f), Color.white);                        // hull number
        var drawn = new GameObject("Hull");
        drawn.transform.SetParent(go.transform, false);
        m.AttachTo(drawn);
        return go;
    }
}

/// <summary>
/// Animates a Kraken arm: it rises out of the sea (<see cref="Rise"/>), sways and curls while it
/// waits, and slams down toward a point (<see cref="SlamAt"/>), raising a splash.
/// </summary>
public class TentacleRig : MonoBehaviour
{
    private List<Transform> _joints;
    private float _length;
    private float _rise, _riseTarget;
    private float _slam = -1f;          // seconds since the slam began; < 0 none
    private Vector3 _slamDir;
    private float _seed;
    private Quaternion _rest = Quaternion.identity;   // how the arm leans while it waits
    private float _curl = 1f;                         // how tightly the tip coils

    /// <summary>True once the slam has landed.</summary>
    public bool Landed { get; private set; }

    public void Init(List<Transform> joints, float length)
    {
        _joints = joints;
        _length = length;
        _seed = Random.value * 10f;
        Apply(0f);
    }

    /// <summary>Leans the arm <paramref name="degrees"/> away from upright, toward <paramref name="direction"/>.</summary>
    public void Lean(Vector3 direction, float degrees)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f) return;
        _rest = Quaternion.AngleAxis(degrees, Vector3.Cross(Vector3.up, direction.normalized));
    }

    /// <summary>How tightly the tip coils: 0 straight, 1 a full spiral.</summary>
    public void Coil(float amount) => _curl = Mathf.Max(0f, amount);

    /// <summary>Rise out of the sea (1) or sink back (0).</summary>
    public void Rise(float to) => _riseTarget = Mathf.Clamp01(to);

    /// <summary>Slam down toward <paramref name="point"/>.</summary>
    public void SlamAt(Vector3 point)
    {
        Vector3 d = point - transform.position;
        d.y = 0f;
        _slamDir = d.sqrMagnitude > 0.01f ? d.normalized : transform.forward;
        _slam = 0f;
        Landed = false;
    }

    private void Update()
    {
        _rise = Mathf.MoveTowards(_rise, _riseTarget, Time.deltaTime * 0.9f);
        if (_slam >= 0f)
        {
            _slam += Time.deltaTime;
            if (!Landed && _slam > 0.45f) { Landed = true; Splash(); }
        }
        Apply(Time.time);
    }

    private void Apply(float t)
    {
        if (_joints == null) return;
        // Up out of the water as it rises; the waves carry the root.
        Vector3 p = transform.position;
        p.y = SeaWaves.Displacement(p.x, p.z, SeaWaves.Now).y - _length * (1f - _rise) * 1.05f;
        transform.position = p;

        float slam = _slam < 0f ? 0f : Mathf.Clamp01(_slam / 0.45f);
        slam = slam * slam;
        if (_slam >= 0f)
        {
            // The whole arm leans over toward the target, then lies on the water.
            var lean = Quaternion.FromToRotation(Vector3.up, Vector3.Lerp(Vector3.up, _slamDir + Vector3.down * 0.15f, slam).normalized);
            transform.rotation = lean;
        }
        else transform.rotation = _rest;
        // Each joint bends a little more than the last, so the tip rolls up into a spiral (away
        // from the suckers, as an octopus curls its arm), and a slow wave runs up it.
        int n = _joints.Count;
        float breathe = 0.85f + 0.15f * Mathf.Sin(t * 0.8f + _seed);
        for (int i = 1; i < n; i++)
        {
            float k = i / (float)n;
            float coil = (1f - slam * 0.85f) * _curl * breathe * Mathf.Lerp(2f, 46f, k * k * k);
            float wave = Mathf.Sin(t * 1.7f - i * 0.45f + _seed) * 10f * k;
            float twist = Mathf.Sin(t * 1.1f + _seed + i * 0.7f) * 7f * k;
            _joints[i].localRotation = Quaternion.Euler(-(coil + wave), twist, 0f);
        }
    }

    private void Splash()
    {
        Vector3 at = transform.position + _slamDir * _length * 0.75f;
        var audio = AudioService.Instance;
        if (audio != null && audio.Sfx != null && audio.Sfx.CargoSplash != null) audio.Play(audio.Sfx.CargoSplash, AudioBus.Effects, 0.9f, 0.7f);
        SplashRing.Spawn(at, 16f, new Color(1f, 1f, 1f, 0.9f));
    }
}

/// <summary>A white ring spreading on the water and fading: a splash, seen from above.</summary>
public class SplashRing : MonoBehaviour
{
    private static Material _material;
    private LineRenderer _line;
    private float _t, _radius;
    private Color _colour;
    private Vector3 _centre;

    public static void Spawn(Vector3 centre, float radius, Color colour)
    {
        var go = new GameObject("SplashRing");
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        var ring = go.AddComponent<SplashRing>();
        ring._centre = centre;
        ring._radius = radius;
        ring._colour = colour;
        ring._line = go.AddComponent<LineRenderer>();
        ring._line.useWorldSpace = true;
        ring._line.alignment = LineAlignment.TransformZ;
        ring._line.loop = true;
        ring._line.positionCount = 40;
        ring._line.widthMultiplier = 1.2f;
        ring._line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        if (_material == null) _material = new Material(Shader.Find("Sprites/Default")) { name = "Splash", renderQueue = 3100 };
        ring._line.sharedMaterial = _material;
    }

    private void Update()
    {
        _t += Time.deltaTime / 1.1f;
        if (_t >= 1f) { Destroy(gameObject); return; }
        float r = Mathf.Lerp(2f, _radius, 1f - (1f - _t) * (1f - _t));
        for (int i = 0; i < 40; i++)
        {
            float a = i * Mathf.PI * 2f / 40f;
            float x = _centre.x + Mathf.Cos(a) * r, z = _centre.z + Mathf.Sin(a) * r;
            _line.SetPosition(i, new Vector3(x, SeaWaves.Displacement(x, z, SeaWaves.Now).y + 0.3f, z));
        }
        var c = _colour;
        c.a *= 1f - _t;
        _line.startColor = _line.endColor = c;
    }
}
