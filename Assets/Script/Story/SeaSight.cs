using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Something strange in the water beside the route. It stays hidden until the story director
/// reveals it (the ship has come near), then plays out on its own and leaves. Scenery only:
/// no collider, not on the radar. Each rides the real waves and throws up foam and spray where
/// it breaks the surface (SeaSplash, Spray).
///   Deep shadow   a giant dark whale: it surfaces and blows, rolls to slap a long flipper,
///                 then arches and dives, lifting its fluke clear of the water.
///   Ghost ship    an old sailing ship of glowing, tattered sails and lanterns, trailing mist,
///                 drifting past the other way, then sinking by the bow.
///   Serpent       one long scaled body swimming in rolling coils; it lifts its horned head,
///                 turns to look at the ship and opens its jaws, then slides under head first.
///   Spirit light  a glowing manta-like spirit with a trailing ribbon that leads the ship along
///                 the channel (GuideTarget), touching ripples onto the water as it goes.
/// </summary>
public class SeaSight : MonoBehaviour
{
    private const float HiddenY = -9f;

    public SightKind Kind { get; private set; }
    public bool Revealed => _revealedAt >= 0f;

    /// <summary>Spirit light only: where it should float next (set by the director every frame).</summary>
    public Vector3? GuideTarget { get; set; }

    private float _revealedAt = -1f;
    private Vector3 _heading;
    private float _surface;
    private float _speed;
    private float _released = -1f;
    private float _nextFoam;

    // Whale
    private CreatureModels.WhaleRig _whale;
    private bool _flukeAbove, _spout1, _spout2, _slapped;
    // Serpent
    private LiveLoft _serpent;
    private CreatureModels.SerpentHeadRig _head;
    private Vector3[] _spine;
    private const int SerpentRings = 34;
    private const float SerpentSpacing = 1.15f;
    // Ghost ship
    private Transform _ship;
    private Transform[] _sails;
    private Transform[] _lanterns;
    private float _nextWisp;
    // Spirit
    private Transform _wingL, _wingR, _core;
    private Transform[] _sparks, _ribbon;
    private readonly List<Vector3> _trail = new List<Vector3>();
    private float _nextTrail, _nextRipple;

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
            default: sight.BuildWhale(); break;
        }
        root.SetActive(false);
        return sight;
    }

    /// <summary>
    /// A real whale, not a story sight (SeaLife, far off in Free Sail): surfaces at once, blows,
    /// rolls, and dives with its fluke up, like the deep shadow but its own size and colours.
    /// </summary>
    public static SeaSight CreateWhale(Transform parent, Vector3 position, Vector3 heading, float length, CreatureModels.WhaleColours colours)
    {
        var root = new GameObject("Whale");
        root.transform.SetParent(parent, false);
        root.transform.position = new Vector3(position.x, position.y + HiddenY, position.z);
        root.transform.rotation = Quaternion.LookRotation(heading.normalized, Vector3.up);
        var sight = root.AddComponent<SeaSight>();
        sight.Kind = SightKind.DeepShadow;
        sight._heading = heading.normalized;
        sight._surface = position.y;
        sight._speed = 2f;
        sight._whale = CreatureModels.Whale(root.transform, "Whale", length, colours);
        sight.Reveal();
        return sight;
    }

    // ------------------------------------------------------------ the shapes

    private void BuildWhale()
    {
        _speed = 3f;
        _whale = CreatureModels.Whale(transform, "Whale", 30f, CreatureModels.WhaleColours.Shadow);
    }

    private void BuildSerpent()
    {
        _speed = 3.5f;
        Color skin = new Color(0.03f, 0.17f, 0.11f), scale = new Color(0.02f, 0.12f, 0.08f), spot = new Color(0.09f, 0.28f, 0.17f);
        Color belly = new Color(0.42f, 0.44f, 0.24f), crest = new Color(0.42f, 0.07f, 0.05f), dark = new Color(0.015f, 0.06f, 0.04f);
        var radii = new List<Vector2>(); var crestH = new List<float>();
        for (int i = 0; i < SerpentRings; i++)
        {
            float t = i / (float)(SerpentRings - 1);
            float r = Mathf.Lerp(0.82f, 0.14f, t * t) * (1f + 0.08f * Mathf.Sin(t * 9f));
            radii.Add(new Vector2(r, r * 0.92f));
            crestH.Add(Mathf.Lerp(0.75f, 0.12f, t) * (i % 2 == 0 ? 1f : 0.6f));
        }
        const int sides = 9;
        var go = new GameObject("Body");
        go.transform.SetParent(transform, false);
        _serpent = new LiveLoft(go, radii, crestH, sides, (ring, k) =>
        {
            if (k < 0) return crest;
            float mid = (k + 0.5f) * Mathf.PI * 2f / sides;
            if (Mathf.Sin(mid) < -0.45f) return belly;
            if (k == sides / 4 && ring % 4 == 1) return spot; // a row of pale spots along the back
            return ring % 2 == 0 ? skin : scale;           // scale bands
        });
        _spine = new Vector3[SerpentRings];
        _head = CreatureModels.SerpentHead(transform, skin, dark, belly, crest);
    }

    private void BuildGhostShip()
    {
        _speed = 2.2f;
        // Deep colours with a soft glow: post-processing would wash brighter ones out to white.
        Color hull = new Color(0.07f, 0.38f, 0.46f), plank = new Color(0.04f, 0.24f, 0.3f), sail = new Color(0.22f, 0.62f, 0.72f);
        Color wood = new Color(0.05f, 0.28f, 0.34f), lamp = new Color(0.75f, 0.85f, 0.45f), window = new Color(0.55f, 0.8f, 0.5f);
        var ship = new GameObject("Ghost Ship").transform;
        ship.SetParent(transform, false);
        _ship = ship;

        var m = new LowPolyMesh();
        // A galleon's hull: lofted bow to stern, a raised stern castle and forecastle, planking lines.
        m.Loft(new[] { new Vector3(0f, 1.2f, 10.5f), new Vector3(0f, 0.6f, 8.5f), new Vector3(0f, 0.2f, 5f), new Vector3(0f, 0.1f, 0f), new Vector3(0f, 0.2f, -5f), new Vector3(0f, 0.6f, -8.2f) },
               new[] { new Vector2(0.3f, 0.5f), new Vector2(1.7f, 1.5f), new Vector2(2.7f, 1.9f), new Vector2(3.0f, 2.0f), new Vector2(2.8f, 1.9f), new Vector2(2.2f, 1.7f) }, 10, hull, plank, -0.6f);
        m.Box(new Vector3(0f, 1.9f, 0f), new Vector3(5.2f, 0.25f, 15f), wood); // deck
        m.Box(new Vector3(0f, 3.0f, -6.6f), new Vector3(4.6f, 2.2f, 3.6f), hull); // stern castle
        m.Box(new Vector3(0f, 4.2f, -6.6f), new Vector3(4.8f, 0.2f, 3.8f), wood);
        m.Box(new Vector3(0f, 2.6f, 7.3f), new Vector3(3.6f, 1.3f, 2.6f), hull); // forecastle
        for (int k = 0; k < 3; k++)
        {
            float y = 0.9f + k * 0.45f;
            m.Box(new Vector3(2.85f - k * 0.1f, y, 0f), new Vector3(0.08f, 0.06f, 13f - k * 1.5f), plank);
            m.Box(new Vector3(-2.85f + k * 0.1f, y, 0f), new Vector3(0.08f, 0.06f, 13f - k * 1.5f), plank);
        }
        for (int s = -1; s <= 1; s += 2) // rails
            m.Box(new Vector3(s * 2.55f, 2.3f, 0.5f), new Vector3(0.12f, 0.6f, 12f), wood);
        // Figurehead under the bowsprit, the bowsprit, a broken spar hanging off the mizzen.
        m.Ellipsoid(new Vector3(0f, 1.5f, 11f), new Vector3(0.3f, 0.6f, 0.45f), 6, 5, wood, 0f, 1f);
        m.Pyramid(new Vector3(0f, 1.9f, 11.2f), new Vector3(0f, 2.6f, 12.2f), 0.18f, wood);
        m.Box(new Vector3(0f, 3.2f, 12.6f), new Vector3(0.28f, 0.28f, 6f), wood, Quaternion.Euler(-18f, 0f, 0f));
        // Masts, yards and rigging.
        float[] mastZ = { 4.6f, 0f, -4.4f }, mastH = { 13f, 15f, 11f };
        for (int i = 0; i < 3; i++)
        {
            m.Box(new Vector3(0f, 2f + mastH[i] / 2f, mastZ[i]), new Vector3(0.35f, mastH[i], 0.35f), wood);
            for (int y = 0; y < 2; y++)
                m.Box(new Vector3(0f, 6f + y * 4.2f, mastZ[i]), new Vector3(7f - y * 1.8f, 0.18f, 0.18f), wood);
            Vector3 top = new Vector3(0f, 2f + mastH[i], mastZ[i]);
            foreach (var foot in new[] { new Vector3(2.6f, 2.2f, mastZ[i] - 1.2f), new Vector3(-2.6f, 2.2f, mastZ[i] - 1.2f) })
                Line(m, top, foot, wood);
            if (i < 2) Line(m, top, new Vector3(0f, 2f + mastH[i + 1] * 0.8f, mastZ[i + 1]), wood);
        }
        Line(m, new Vector3(0f, 2f + mastH[0], mastZ[0]), new Vector3(0f, 4.2f, 15.2f), wood); // forestay to the bowsprit
        m.Box(new Vector3(0.6f, 12.2f, -4.4f), new Vector3(0.18f, 3.5f, 0.18f), wood, Quaternion.Euler(0f, 0f, -35f)); // broken topmast
        // Stern windows.
        for (int w = -1; w <= 1; w++) m.Box(new Vector3(w * 1.2f, 3.1f, -8.43f), new Vector3(0.6f, 0.7f, 0.05f), window);
        CreatureModels.Part(ship, "Hull", m, 0.55f, false);

        // Tattered sails: each a grid of patches with some torn away and a ragged foot.
        _sails = new Transform[6];
        var rng = new System.Random(7);
        for (int i = 0; i < 3; i++)
            for (int y = 0; y < 2; y++)
            {
                var s = new LowPolyMesh();
                float w = 6.6f - y * 1.8f, h = 3.8f, top = 6f + y * 4.2f + h - 0.1f;
                int cols = 5, rows = 3;
                for (int c = 0; c < cols; c++)
                    for (int r = 0; r < rows; r++)
                    {
                        if (r > 0 && rng.NextDouble() < 0.18) continue; // torn away
                        float x0 = -w / 2f + c * w / cols, x1 = x0 + w / cols;
                        float y0 = top - r * h / rows, y1 = y0 - h / rows - (r == rows - 1 ? (float)rng.NextDouble() * 0.9f : 0f);
                        float belly = 0.35f * Mathf.Sin(Mathf.PI * (c + 0.5f) / cols);
                        s.Around(new Vector3(0f, (y0 + y1) / 2f, -2f));
                        s.Quad(new Vector3(x0, y0, belly), new Vector3(x1, y0, belly), new Vector3(x1, y1, belly + 0.15f), new Vector3(x0, y1, belly + 0.15f), sail);
                    }
                var t = CreatureModels.Part(ship, "Sail", s, 0.7f, false);
                t.localPosition = new Vector3(0f, 0f, mastZ[i] + 0.25f);
                t.localRotation = Quaternion.Euler(0f, (i - 1) * 6f, 0f);
                _sails[i * 2 + y] = t;
            }

        // Lanterns at the bow and stern, and a tattered pennant.
        _lanterns = new Transform[3];
        Vector3[] at = { new Vector3(0f, 5.2f, -8.2f), new Vector3(2.2f, 3.0f, 8.4f), new Vector3(-2.2f, 3.0f, 8.4f) };
        for (int i = 0; i < 3; i++)
        {
            var l = new LowPolyMesh();
            l.Box(Vector3.zero, new Vector3(0.45f, 0.6f, 0.45f), lamp);
            _lanterns[i] = CreatureModels.Part(ship, "Lantern", l, 2.2f, false);
            _lanterns[i].localPosition = at[i];
        }
        var flag = new LowPolyMesh();
        flag.Around(new Vector3(0f, 0f, -1f));
        flag.Tri(new Vector3(0f, 17.2f, 0f), new Vector3(0f, 16.4f, 0f), new Vector3(0f, 16.9f, -2.6f), sail);
        CreatureModels.Part(ship, "Pennant", flag, 0.7f, false);

    }

    private static void Line(LowPolyMesh m, Vector3 a, Vector3 b, Color c)
    {
        m.Box((a + b) * 0.5f, new Vector3(0.06f, 0.06f, (b - a).magnitude), c, Quaternion.LookRotation(b - a));
    }

    private void BuildSpiritLight()
    {
        _speed = 9f;
        Color warm = new Color(0.9f, 0.55f, 0.12f), pale = new Color(0.95f, 0.75f, 0.3f), core = new Color(1f, 0.85f, 0.45f);
        var body = new LowPolyMesh();
        // A manta-like spirit: a flat diamond body, a rounded head lobe, the glowing heart inside.
        body.Fin(new[] { new Vector2(0f, 1.6f), new Vector2(0.7f, 0.4f), new Vector2(0.4f, -1.4f), new Vector2(0f, -1.8f), new Vector2(-0.4f, -1.4f), new Vector2(-0.7f, 0.4f) },
                 0.35f, warm, Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(90f, 0f, 0f), Vector3.one));
        body.Ellipsoid(new Vector3(0f, 0f, 1.3f), new Vector3(0.55f, 0.3f, 0.45f), 8, 5, warm, 0f, 1f);
        CreatureModels.Part(transform, "Spirit", body, 1.0f, false);
        var heart = new LowPolyMesh();
        heart.Ellipsoid(Vector3.zero, Vector3.one * 0.55f, 8, 6, core, 0f, 1f);
        _core = CreatureModels.Part(transform, "Heart", heart, 2.2f, false);
        _core.localPosition = new Vector3(0f, 0.25f, 0.2f);
        _wingR = SpiritWing(1f, pale);
        _wingL = SpiritWing(-1f, pale);

        _sparks = new Transform[4];
        for (int i = 0; i < _sparks.Length; i++) // little sparks circling it
        {
            var s = new LowPolyMesh();
            s.Ellipsoid(Vector3.zero, Vector3.one * 0.22f, 5, 4, pale, 0f, 1f);
            _sparks[i] = CreatureModels.Part(transform, "Spark " + i, s, 1.5f, false);
        }
        // The ribbon of light trailing behind: diamonds that follow where it has been.
        _ribbon = new Transform[10];
        for (int i = 0; i < _ribbon.Length; i++)
        {
            var r = new LowPolyMesh();
            float size = Mathf.Lerp(0.42f, 0.12f, i / (float)_ribbon.Length);
            r.Fin(new[] { new Vector2(0f, size * 1.6f), new Vector2(size, 0f), new Vector2(0f, -size * 1.6f), new Vector2(-size, 0f) }, 0.05f, pale,
                  Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(90f, 0f, 0f), Vector3.one));
            _ribbon[i] = CreatureModels.Part(transform.parent, "Ribbon " + i, r, 1.3f, false);
            _ribbon[i].gameObject.SetActive(false);
        }
    }

    private Transform SpiritWing(float side, Color colour)
    {
        var w = new LowPolyMesh();
        var outline = new List<Vector2> { new Vector2(0f, 0.9f), new Vector2(1.4f, 0.5f), new Vector2(2.6f, -0.3f), new Vector2(1.2f, -0.6f), new Vector2(0f, -1.1f) };
        if (side < 0f) { for (int i = 0; i < outline.Count; i++) outline[i] = new Vector2(-outline[i].x, outline[i].y); outline.Reverse(); }
        w.Fin(outline, 0.12f, colour, Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(90f, 0f, 0f), Vector3.one));
        var t = CreatureModels.Part(transform, side > 0 ? "Wing R" : "Wing L", w, 1.1f, false);
        t.localPosition = new Vector3(side * 0.6f, 0f, 0f);
        return t;
    }

    // ------------------------------------------------------------ behaviour

    /// <summary>Starts it: rise, play out, go.</summary>
    public void Reveal()
    {
        if (Revealed) return;
        _revealedAt = Time.time;
        gameObject.SetActive(true);
        if (_ribbon != null) foreach (var r in _ribbon) r.gameObject.SetActive(true);
    }

    /// <summary>Spirit light: it has led the ship home; it slows and sinks away.</summary>
    public void Release()
    {
        if (!Revealed || _released >= 0f) return;
        _released = Time.time;
        GuideTarget = null;
        _heading = transform.forward;
        _speed = 2f;
    }

    private void OnDestroy()
    {
        if (_ribbon != null) foreach (var r in _ribbon) if (r != null) Destroy(r.gameObject);
    }

    private void Update()
    {
        if (!Revealed) return;
        float age = Time.time - _revealedAt;
        bool done;
        switch (Kind)
        {
            case SightKind.GhostShip: done = UpdateGhostShip(age); break;
            case SightKind.SerpentCoils: done = UpdateSerpent(age); break;
            case SightKind.SpiritLight: done = UpdateSpirit(age); break;
            default: done = UpdateWhale(age); break;
        }
        if (done) Destroy(gameObject);
    }

    private float Wave(Vector3 p) => _surface + SeaWaves.HeightAt(p.x, p.z);

    private void Glide(float speed)
    {
        transform.position += _heading * speed * Time.deltaTime;
    }

    // The whale: 0-3 s rising, 3-11 at the surface (blows twice, rolls and slaps a flipper),
    // 11-16 the dive (nose down, tail up, the fluke lifts clear and slides under).
    private bool UpdateWhale(float age)
    {
        var w = _whale;
        float f = w.Length / 30f;
        Glide(age < 11f ? _speed : Mathf.Lerp(_speed, 0.8f, (age - 11f) / 6f));
        Vector3 p = transform.position;
        float surfaceY = Wave(p);
        float atSurface = -2.85f * f + 0.55f;            // the back just breaks the water
        float y, pitch = 0f, roll = 0f;
        if (age < 3f) y = Mathf.Lerp(HiddenY, atSurface - 0.8f, Mathf.SmoothStep(0f, 1f, age / 3f)) + Mathf.SmoothStep(0f, 0.8f, age - 2.2f);
        else if (age < 11f)
        {
            y = atSurface + Mathf.Sin(age * 0.9f) * 0.15f;
            pitch = Mathf.Sin(age * 0.7f) * 1.5f;
            // Roll onto the right side, lift the flipper, slap it down.
            float rollT = Mathf.InverseLerp(5.8f, 7.2f, age) - Mathf.InverseLerp(8.6f, 9.8f, age);
            roll = -Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(rollT)) * 38f;
        }
        else
        {
            // Turning away as it goes down, so the broad fluke faces the bridge as it lifts.
            var cam = Camera.main;
            if (cam != null)
            {
                Vector3 away = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
                if (away.sqrMagnitude > 0.5f) _heading = Vector3.Slerp(_heading, away, 1.6f * Time.deltaTime).normalized;
            }
            // The dive, 0..1 over six seconds: first the nose goes down while the body stays
            // at the surface, so the tail rises and the fluke stands clear of the water; then
            // the whale slides down its own length and is gone.
            float d = (age - 11f) / 6f;
            pitch = Mathf.SmoothStep(0f, 68f, Mathf.Clamp01(d / 0.45f));
            y = atSurface - d * 1.6f * f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1f, d)) * 24f * f;
        }
        transform.position = new Vector3(p.x, surfaceY + y, p.z);
        transform.rotation = Quaternion.LookRotation(_heading) * Quaternion.Euler(pitch, 0f, roll);

        // The tail beats slowly; the fluke follows a little behind.
        float beat = Mathf.Sin(age * 1.3f);
        w.Tail.localRotation = Quaternion.Euler(beat * 5f - (age > 11f ? Mathf.Clamp01((age - 11f) / 2f) * 14f : 0f), 0f, 0f);
        // In the dive the fluke bends back, showing its broad underside as it lifts clear.
        float bend = age > 11f ? Mathf.SmoothStep(0f, 1f, (age - 11f) / 2.2f) * -55f : 0f;
        w.Fluke.localRotation = Quaternion.Euler(Mathf.Sin(age * 1.3f - 0.8f) * 12f * (age > 11f ? 0.3f : 1f) + bend, 0f, 0f);
        float lift = Mathf.Clamp01(Mathf.InverseLerp(6.4f, 7.6f, age) - Mathf.InverseLerp(8.5f, 8.8f, age));
        w.FlipperR.localRotation = Quaternion.Euler(0f, 32f, -28f + lift * 70f);
        w.FlipperL.localRotation = Quaternion.Euler(0f, -32f, 28f + Mathf.Sin(age * 0.8f) * 6f);

        // Blows, the flipper slap, foam along the back, and the fluke going down.
        if (!_spout1 && age > 3.4f) { _spout1 = true; Blow(14f * f); }
        if (!_spout2 && age > 9.6f) { _spout2 = true; Blow(12f * f); }
        if (!_slapped && age > 8.75f)
        {
            _slapped = true;
            Vector3 tip = w.FlipperR.TransformPoint(new Vector3(7f, 0f, 0f) * f);
            tip.y = surfaceY;
            Spray.Burst(tip, 2.2f, 22);
            SeaSplash.Add(tip, 5f * f, 1f, 2.6f);
        }
        if (age > 3f && age < 13f && Time.time >= _nextFoam)
        {
            _nextFoam = Time.time + 0.35f;
            Vector3 back = transform.TransformPoint(new Vector3(0f, 0f, 3f * f));
            SeaSplash.Add(new Vector3(back.x, 0f, back.z), 5.5f * f, 0.55f, 2.2f);
        }
        Vector3 flukeAt = w.Fluke.position;
        bool above = flukeAt.y > Wave(flukeAt) + 0.3f;
        if (_flukeAbove && !above && age > 11f)
        {
            Spray.Burst(new Vector3(flukeAt.x, Wave(flukeAt), flukeAt.z), 3f, 26);
            SeaSplash.Add(flukeAt, 7f * f, 1f, 3.2f);
        }
        _flukeAbove = above;
        return age > 22f;
    }

    private void Blow(float height)
    {
        Vector3 hole = _whale.Body.TransformPoint(_whale.Blowhole);
        Spray.Spout(hole, height);
        SeaSplash.Add(hole, 2.5f, 0.6f, 2f);
    }

    // The serpent: one body in rolling coils, rising for 3 s, swimming past, lifting its head
    // to look at the ship (5-9.5 s, the jaws open), then sliding under head first.
    private bool UpdateSerpent(float age)
    {
        Glide(_speed);
        Vector3 p = transform.position;
        transform.position = new Vector3(p.x, Wave(p), p.z);

        float rise = Mathf.SmoothStep(-6f, 0f, Mathf.Clamp01(age / 3f));
        float look = Mathf.Clamp01(Mathf.InverseLerp(5f, 6.2f, age)) * (1f - Mathf.Clamp01(Mathf.InverseLerp(9f, 10f, age)));
        float dive = Mathf.Max(0f, age - 11f) * 6f; // metres of the body that have gone under, from the head back
        float k = Mathf.PI * 2f / 11f, w = 2.4f;
        for (int i = 0; i < SerpentRings; i++)
        {
            float z = -i * SerpentSpacing;
            float y = Mathf.Sin(k * z + w * age) * 1.6f - 0.55f + rise;
            float x = Mathf.Sin(z * 0.21f + age * 1.3f) * 0.7f;
            if (i < 6) y += look * 3.2f * (1f - i / 6f) * (1f - i / 6f);   // the neck rises
            y -= Mathf.Clamp(dive - i * SerpentSpacing, 0f, 9f) * 0.8f;
            _spine[i] = new Vector3(x, y, z);
        }
        _serpent.Update(_spine);

        // The head sits on the front of the body; while looking it turns toward the ship.
        Vector3 neck = (_spine[0] - _spine[1]).normalized;
        Quaternion along = Quaternion.LookRotation(neck, Vector3.up);
        var nav = ShipNavigator.Instance;
        if (nav != null && look > 0f)
        {
            Vector3 toShip = transform.InverseTransformPoint(nav.transform.position) - _spine[0];
            toShip.y = 0f;
            if (toShip.sqrMagnitude > 1f) along = Quaternion.Slerp(along, Quaternion.LookRotation(toShip.normalized + Vector3.down * 0.15f), look * 0.85f);
        }
        _head.Root.localPosition = _spine[0] + neck * 0.2f;
        _head.Root.localRotation = along;
        float hiss = Mathf.Clamp01(Mathf.InverseLerp(6.4f, 7f, age)) * (1f - Mathf.Clamp01(Mathf.InverseLerp(8.4f, 9f, age)));
        _head.Jaw.localRotation = Quaternion.Euler(hiss * 32f + Mathf.Sin(age * 3f) * 2f, 0f, 0f);

        // White water where the coils cut the surface.
        if (Time.time >= _nextFoam && age < 15f)
        {
            _nextFoam = Time.time + 0.22f;
            int made = 0;
            for (int i = 0; i < SerpentRings - 1 && made < 4; i++)
            {
                if ((_spine[i].y > 0f) == (_spine[i + 1].y > 0f)) continue;
                Vector3 at = transform.TransformPoint(_spine[i]);
                SeaSplash.Add(at, 2f, 0.7f, 1.6f);
                made++;
            }
        }
        return age > 17f;
    }

    // The ghost ship: drifts past rolling gently, its sails stirring and lanterns flickering,
    // mist rising off it; after 16 s it sinks by the bow.
    private bool UpdateGhostShip(float age)
    {
        const float stay = 16f;
        Glide(_speed);
        Vector3 p = transform.position;
        float sink = age < stay ? 0f : (age - stay) / 5f;
        float y = age < 3f ? Mathf.Lerp(HiddenY, 0f, Mathf.SmoothStep(0f, 1f, age / 3f)) : -sink * sink * 14f;
        transform.position = new Vector3(p.x, Wave(p) + y, p.z);
        transform.rotation = Quaternion.LookRotation(_heading) * Quaternion.Euler(Mathf.Sin(age * 0.7f) * 2f + sink * 18f, 0f, Mathf.Sin(age * 0.5f) * 4f);
        for (int i = 0; i < _sails.Length; i++)
            _sails[i].localRotation = Quaternion.Euler(Mathf.Sin(age * 1.1f + i) * 4f, (i / 2 - 1) * 6f + Mathf.Sin(age * 0.6f + i * 0.7f) * 5f, 0f);
        for (int i = 0; i < _lanterns.Length; i++)
            _lanterns[i].localScale = Vector3.one * (0.85f + 0.25f * Mathf.PerlinNoise(age * 3f, i * 7f));
        if (Time.time >= _nextWisp && sink < 0.8f)
        {
            _nextWisp = Time.time + 0.25f;
            Vector3 at = _ship.TransformPoint(new Vector3(Random.Range(-2.5f, 2.5f), Random.Range(2f, 9f), Random.Range(-8f, 9f)));
            Spray.Wisp(at, new Color(0.2f, 0.55f, 0.62f), 0.5f);
        }
        if (age > 3f && Time.time >= _nextFoam && sink < 1f)
        {
            // A pale ring of disturbed water around the hull, and spray at the bow.
            _nextFoam = Time.time + 0.5f;
            SeaSplash.Add(_ship.position, 12f, 0.25f, 3f);
            SeaSplash.Add(_ship.TransformPoint(new Vector3(0f, 0f, 10f)), 3f, 0.4f, 2f);
        }
        return age > stay + 7f;
    }

    // The spirit: leads the ship along the channel, wings beating slowly, its ribbon trailing,
    // touching ripples onto the water; once released it slows and sinks away.
    private bool UpdateSpirit(float age)
    {
        Vector3 p = transform.position;
        if (GuideTarget.HasValue)
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
        else p += _heading * _speed * Time.deltaTime;

        float sinkT = _released >= 0f ? (Time.time - _released) / 4f : 0f;
        float rise = Mathf.SmoothStep(HiddenY, 2f, Mathf.Clamp01(age / 3f));
        float y = rise + Mathf.Sin(age * 0.8f) * 0.25f - sinkT * sinkT * 11f;
        transform.position = new Vector3(p.x, _surface + y, p.z);

        float flap = Mathf.Sin(age * 2.4f);
        _wingR.localRotation = Quaternion.Euler(0f, 0f, flap * 24f);
        _wingL.localRotation = Quaternion.Euler(0f, 0f, -flap * 24f);
        _core.localScale = Vector3.one * (1f + 0.15f * Mathf.Sin(age * 3.1f));
        for (int i = 0; i < _sparks.Length; i++)
        {
            float a = age * 2.2f + i * Mathf.PI * 2f / _sparks.Length;
            _sparks[i].localPosition = new Vector3(Mathf.Cos(a) * 2.6f, Mathf.Sin(age * 3f + i) * 0.6f, Mathf.Sin(a) * 2.6f);
        }

        // The ribbon: each diamond sits where the spirit's tail was a moment ago.
        if (Time.time >= _nextTrail)
        {
            _nextTrail = Time.time + 0.06f;
            _trail.Insert(0, transform.TransformPoint(new Vector3(0f, 0f, -1.7f)));
            if (_trail.Count > _ribbon.Length * 2 + 2) _trail.RemoveAt(_trail.Count - 1);
        }
        for (int i = 0; i < _ribbon.Length; i++)
        {
            int at = Mathf.Min(i * 2 + 1, _trail.Count - 1);
            if (at < 0) continue;
            Vector3 here = _trail[at] + Vector3.up * Mathf.Sin(age * 4f - i * 0.6f) * 0.15f;
            Vector3 ahead = at > 0 ? _trail[at - 1] : transform.position;
            _ribbon[i].position = here;
            if ((ahead - here).sqrMagnitude > 1e-4f) _ribbon[i].rotation = Quaternion.LookRotation(ahead - here);
        }

        if (Time.time >= _nextRipple && sinkT < 0.5f && age > 3f)
        {
            _nextRipple = Time.time + 1.1f;
            SeaSplash.Add(transform.position, 3.5f, 0.3f, 2.2f);
        }
        return _released >= 0f && sinkT > 1.6f;
    }
}
