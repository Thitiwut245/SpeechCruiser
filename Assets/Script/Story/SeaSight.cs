using UnityEngine;

/// <summary>
/// Something strange in the water beside the route. It stays hidden until the story director
/// reveals it (the ship has come near), then plays out on its own and leaves. Scenery only:
/// no collider, not on the radar.
///   Deep shadow   a huge dark back and ridge breaking the surface, gliding across ahead.
///   Ghost ship    a pale glowing sailing ship drifting past the other way, then sinking away.
///   Serpent coils dark green humps rolling through the water, a head with glowing eyes.
///   Spirit light  a warm glowing light that leads the ship along the channel (GuideTarget).
/// </summary>
public class SeaSight : MonoBehaviour
{
    private const float HiddenY = -9f;
    private const float RiseSeconds = 3f;

    public SightKind Kind { get; private set; }
    public bool Revealed => _revealedAt >= 0f;

    /// <summary>Spirit light only: where it should float next (set by the director every frame).</summary>
    public Vector3? GuideTarget { get; set; }

    private float _revealedAt = -1f;
    private Vector3 _heading;
    private float _surface;
    private Transform _tail;
    private Transform[] _humps;
    private Transform _head;
    private Transform _halo;

    // Per kind: speed, height at rest (relative to the surface), how long it stays.
    private float _speed;
    private float _restY;
    private float _stay;

    /// <summary>A hidden sight at <paramref name="position"/> (y = the water's surface), heading along <paramref name="heading"/>.</summary>
    public static SeaSight Create(Transform parent, SightKind kind, Vector3 position, Vector3 heading)
    {
        var root = new GameObject("Sea Sight " + kind);
        root.transform.SetParent(parent, false);
        root.transform.position = new Vector3(position.x, position.y + HiddenY, position.z);
        root.transform.rotation = Quaternion.LookRotation(heading.normalized, Vector3.up);
        var sight = root.AddComponent<SeaSight>();
        sight.Kind = kind;
        sight._heading = heading.normalized;
        sight._surface = position.y;

        switch (kind)
        {
            case SightKind.GhostShip: sight.BuildGhostShip(); break;
            case SightKind.SerpentCoils: sight.BuildSerpent(); break;
            case SightKind.SpiritLight: sight.BuildSpiritLight(); break;
            default: sight.BuildDeepShadow(); break;
        }
        root.SetActive(false);
        return sight;
    }

    // ------------------------------------------------------------ the shapes

    private void BuildDeepShadow()
    {
        _speed = 3f; _restY = -1.05f; _stay = 11f;
        // Near black: the scene's post-processing lifts dark colours a lot.
        Color dark = new Color(0.004f, 0.008f, 0.016f), back = new Color(0.01f, 0.02f, 0.035f);
        var m = new LowPolyMesh();
        // A long body, far bigger than the ship, with a low ridge along the back.
        m.Ellipsoid(Vector3.zero, new Vector3(4.5f, 1.8f, 16f), 10, 7, dark, 0f, 1f);
        for (int i = 0; i < 7; i++)
        {
            float z = 9f - i * 3f;
            float h = 1.9f + 0.5f * Mathf.Sin(i * 0.9f);
            m.Pyramid(new Vector3(0f, 1.2f, z), new Vector3(0f, h + 0.6f, z - 0.8f), 0.7f, back);
        }
        m.Ellipsoid(new Vector3(-5f, -0.6f, 4f), new Vector3(2.6f, 0.3f, 1.2f), 6, 4, dark, 0f, 1f); // fins
        m.Ellipsoid(new Vector3(5f, -0.6f, 4f), new Vector3(2.6f, 0.3f, 1.2f), 6, 4, dark, 0f, 1f);
        Part("Body", m, 0f);

        var t = new LowPolyMesh();
        t.Ellipsoid(new Vector3(0f, 0f, -3f), new Vector3(1.2f, 0.9f, 4f), 8, 5, dark, 0f, 1f);
        t.Ellipsoid(new Vector3(-3f, 0f, -6.5f), new Vector3(3.2f, 0.25f, 1.6f), 6, 4, back, 0f, 1f);
        t.Ellipsoid(new Vector3(3f, 0f, -6.5f), new Vector3(3.2f, 0.25f, 1.6f), 6, 4, back, 0f, 1f);
        _tail = Part("Tail", t, 0f);
        _tail.localPosition = new Vector3(0f, 0f, -15f);
    }

    private void BuildGhostShip()
    {
        _speed = 2.2f; _restY = 0f; _stay = 16f;
        // Deep colours with a soft glow: post-processing would wash brighter ones out to white.
        Color hull = new Color(0.08f, 0.42f, 0.5f), sail = new Color(0.25f, 0.7f, 0.8f), mast = new Color(0.06f, 0.3f, 0.38f);
        var m = new LowPolyMesh();
        // An old sailing ship: a long hull with a raised stern, three masts with square sails.
        m.Frustum(new Vector3(0f, -1.2f, 0f), 2.4f, 3.2f, 2.4f, 10, 0.6f, 4.2f, hull, false, false);
        m.Box(new Vector3(0f, 1.1f, -7f), new Vector3(3.6f, 1.6f, 4f), hull); // stern castle
        m.Box(new Vector3(0f, 1.15f, 0f), new Vector3(3.4f, 0.2f, 14f), mast);  // deck
        for (int i = 0; i < 3; i++)
        {
            float z = 5f - i * 5f, h = 12f - Mathf.Abs(i - 1) * 2f;
            m.Box(new Vector3(0f, 1f + h / 2f, z), new Vector3(0.35f, h, 0.35f), mast);
            for (int s = 0; s < 2; s++)
            {
                float y = 4.5f + s * 3.6f, w = 6f - s * 1.4f;
                m.Box(new Vector3(0f, y, z), new Vector3(w, 2.8f, 0.12f), sail, Quaternion.Euler(0f, 0f, (s == 0 ? -4f : 6f) * (i - 1)));
            }
        }
        m.Box(new Vector3(0f, 2.2f, 10.5f), new Vector3(0.3f, 0.3f, 6f), mast, Quaternion.Euler(-20f, 0f, 0f)); // bowsprit
        Part("Ship", m, 0.6f);
        _halo = Part("Haze", HazeRing(new Color(0.1f, 0.5f, 0.6f), 9f, 12f), 0.5f);
    }

    private void BuildSerpent()
    {
        _speed = 3.5f; _restY = -0.2f; _stay = 12f;
        Color skin = new Color(0.03f, 0.16f, 0.1f), belly = new Color(0.12f, 0.3f, 0.18f), fin = new Color(0.25f, 0.06f, 0.05f);
        _humps = new Transform[4];
        for (int i = 0; i < _humps.Length; i++)
        {
            var hump = new LowPolyMesh();
            // A half ring standing up out of the water: one coil of the body.
            int segs = 7;
            for (int k = 0; k < segs; k++)
            {
                float a0 = Mathf.PI * k / segs, a1 = Mathf.PI * (k + 1) / segs;
                Vector3 c0 = new Vector3(0f, Mathf.Sin(a0) * 2.4f, Mathf.Cos(a0) * 2.8f), c1 = new Vector3(0f, Mathf.Sin(a1) * 2.4f, Mathf.Cos(a1) * 2.8f);
                hump.Box((c0 + c1) / 2f, new Vector3(1.4f, 1.3f, Vector3.Distance(c0, c1) + 0.3f), k % 2 == 0 ? skin : belly,
                         Quaternion.LookRotation(c1 - c0, Vector3.up));
            }
            hump.Pyramid(new Vector3(0f, 2.9f, 0f), new Vector3(0f, 4f, -0.6f), 0.4f, fin);
            _humps[i] = Part("Coil " + i, hump, 0f);
            _humps[i].localPosition = new Vector3(0f, 0f, -i * 7f);
        }
        var head = new LowPolyMesh();
        head.Ellipsoid(new Vector3(0f, 0f, 1.6f), new Vector3(1.1f, 0.9f, 2f), 8, 5, skin, 0f, 1f);
        head.Box(new Vector3(0f, 0.5f, 0.6f), new Vector3(0.3f, 1.2f, 0.9f), fin);
        _head = Part("Head", head, 0f);
        _head.localPosition = new Vector3(0f, 1.2f, 5f);
        var eyes = new LowPolyMesh();
        eyes.Box(new Vector3(-0.75f, 0.35f, 2.6f), new Vector3(0.25f, 0.2f, 0.4f), new Color(1f, 0.85f, 0.2f));
        eyes.Box(new Vector3(0.75f, 0.35f, 2.6f), new Vector3(0.25f, 0.2f, 0.4f), new Color(1f, 0.85f, 0.2f));
        Part("Eyes", eyes, 3f).SetParent(_head, false);
    }

    private void BuildSpiritLight()
    {
        _speed = 9f; _restY = 2f; _stay = float.MaxValue;
        Color warm = new Color(0.9f, 0.55f, 0.12f);
        var m = new LowPolyMesh();
        m.Ellipsoid(Vector3.zero, new Vector3(1.3f, 1.3f, 1.3f), 8, 6, warm, 0f, 1f);
        Part("Light", m, 1.2f);
        _halo = Part("Ring", HazeRing(new Color(0.85f, 0.5f, 0.1f), 3.2f, 4.2f), 0.9f);
        _halo.localPosition = new Vector3(0f, -1.9f, 0f);
        _humps = new Transform[3];
        for (int i = 0; i < _humps.Length; i++) // little sparks circling it
        {
            var s = new LowPolyMesh();
            s.Ellipsoid(Vector3.zero, Vector3.one * 0.3f, 5, 4, new Color(0.95f, 0.75f, 0.3f), 0f, 1f);
            _humps[i] = Part("Spark " + i, s, 1.5f);
        }
    }

    private static LowPolyMesh HazeRing(Color c, float inner, float outer)
    {
        var m = new LowPolyMesh();
        m.Ring(Vector3.zero, inner, outer, 28, c);
        return m;
    }

    private Transform Part(string name, LowPolyMesh mesh, float glow)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        mesh.AttachTo(go, false, glow);
        return go.transform;
    }

    // ------------------------------------------------------------ behaviour

    /// <summary>Starts it: rise, play out, go.</summary>
    public void Reveal()
    {
        if (Revealed) return;
        _revealedAt = Time.time;
        gameObject.SetActive(true);
    }

    /// <summary>Spirit light: it has led the ship home; it slows and sinks away.</summary>
    public void Release()
    {
        if (!Revealed) return;
        _stay = Mathf.Max(0f, Time.time - _revealedAt - RiseSeconds);
        GuideTarget = null;
        _heading = transform.forward;
        _speed = 2f;
    }

    private void Update()
    {
        if (!Revealed) return;
        float age = Time.time - _revealedAt;

        float y;
        float leave = RiseSeconds + _stay;
        if (age < RiseSeconds) y = Mathf.Lerp(HiddenY, _restY, Mathf.SmoothStep(0f, 1f, age / RiseSeconds));
        else if (age < leave) y = _restY + Mathf.Sin(age * 0.8f) * 0.25f;
        else y = Mathf.Lerp(_restY, HiddenY, (age - leave) / 4f);

        Vector3 p = transform.position;
        if (Kind == SightKind.SpiritLight && GuideTarget.HasValue)
        {
            // Float toward the next stretch of the channel, facing the way it goes.
            Vector3 to = GuideTarget.Value - p;
            to.y = 0f;
            if (to.sqrMagnitude > 0.25f)
            {
                p += Vector3.ClampMagnitude(to, _speed * Time.deltaTime);
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to.normalized), 2f * Time.deltaTime);
            }
        }
        else
        {
            p += _heading * _speed * Time.deltaTime;
        }
        transform.position = new Vector3(p.x, _surface + y, p.z);

        switch (Kind)
        {
            case SightKind.DeepShadow:
                if (_tail != null) _tail.localRotation = Quaternion.Euler(Mathf.Sin(age * 1.6f) * 10f, Mathf.Sin(age * 0.8f) * 12f, 0f);
                break;
            case SightKind.GhostShip:
                transform.rotation = Quaternion.LookRotation(_heading) * Quaternion.Euler(Mathf.Sin(age * 0.7f) * 2f, 0f, Mathf.Sin(age * 0.5f) * 4f);
                if (_halo != null) _halo.localScale = Vector3.one * (1f + 0.08f * Mathf.Sin(age * 2f));
                break;
            case SightKind.SerpentCoils:
                // The coils roll: each rises and sinks a little after the one in front.
                for (int i = 0; i < _humps.Length; i++)
                    _humps[i].localPosition = new Vector3(Mathf.Sin(age * 1.4f - i * 0.9f) * 0.8f, -1.2f + Mathf.Sin(age * 2f - i * 1.1f) * 1.1f, -i * 7f);
                if (_head != null) _head.localRotation = Quaternion.Euler(Mathf.Sin(age * 1.3f) * 8f - 10f, Mathf.Sin(age * 1.4f + 0.5f) * 20f, 0f);
                break;
            case SightKind.SpiritLight:
                float pulse = 1f + 0.12f * Mathf.Sin(age * 3f);
                if (_halo != null) _halo.localScale = new Vector3(pulse, 1f, pulse);
                for (int i = 0; i < _humps.Length; i++)
                {
                    float a = age * 2.2f + i * Mathf.PI * 2f / _humps.Length;
                    _humps[i].localPosition = new Vector3(Mathf.Cos(a) * 2.4f, Mathf.Sin(age * 3f + i) * 0.6f, Mathf.Sin(a) * 2.4f);
                }
                break;
        }

        if (age > leave + 4f) Destroy(gameObject);
    }
}
