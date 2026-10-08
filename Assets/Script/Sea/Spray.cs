using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Thrown water: droplets flung up where something hits the surface (a fluke going down, a
/// dolphin landing, a flipper slap), and a whale's spout, a puffing column of mist that rises,
/// spreads and drifts. Small low-poly bits, no physics, gone in a couple of seconds. Pair it
/// with a SeaSplash for the foam left on the water.
/// </summary>
public class Spray : MonoBehaviour
{
    private static readonly Color Water = new Color(0.9f, 0.95f, 0.97f);
    private static readonly Color Mist = new Color(0.86f, 0.9f, 0.93f);
    private static Mesh _drop;

    private struct Bit
    {
        public Transform T;
        public Vector3 Velocity;
        public float Born, Life, Size, Grow;
        public bool Floats; // mist: rises and spreads instead of falling
    }

    private readonly List<Bit> _bits = new List<Bit>();
    private float _gravity = 9.8f;

    /// <summary>Droplets flung up and out from <paramref name="at"/>; <paramref name="power"/> 1 is a dolphin, 3 a whale's fluke.</summary>
    public static void Burst(Vector3 at, float power, int count = 14)
    {
        var s = Make("Spray");
        for (int i = 0; i < count; i++)
        {
            Vector2 dir = Random.insideUnitCircle.normalized * Random.Range(0.4f, 1f);
            var v = new Vector3(dir.x * 2.2f, Random.Range(3f, 6f), dir.y * 2.2f) * Mathf.Sqrt(power);
            s.Add(at + new Vector3(dir.x, 0.1f, dir.y) * power * 0.6f, v, Random.Range(0.7f, 1.3f), Random.Range(0.12f, 0.28f) * Mathf.Sqrt(power), 0f, false, Water);
        }
    }

    /// <summary>A whale's blow at <paramref name="at"/> (the blowhole), rising about <paramref name="height"/> metres.</summary>
    public static void Spout(Vector3 at, float height)
    {
        var s = Make("Spout");
        s._gravity = 0f;
        for (int i = 0; i < 26; i++)
        {
            float delay = i * 0.035f;
            var v = new Vector3(Random.Range(-0.5f, 0.5f), height * Random.Range(1.4f, 2.1f), Random.Range(-0.5f, 0.5f));
            s.Add(at, v, Random.Range(1.6f, 2.4f), Random.Range(0.25f, 0.45f), Random.Range(1.2f, 2f), true, Mist, delay);
        }
    }

    /// <summary>A faint glowing wisp of mist drifting up off something uncanny (the ghost ship).</summary>
    public static void Wisp(Vector3 at, Color colour, float glow)
    {
        var s = Make("Wisp");
        s._gravity = 0f;
        var v = new Vector3(Random.Range(-0.4f, 0.4f), Random.Range(0.5f, 1.1f), Random.Range(-0.4f, 0.4f));
        s.Add(at, v, Random.Range(2.5f, 3.5f), Random.Range(0.2f, 0.35f), Random.Range(0.6f, 1f), true, colour, 0f, glow);
    }

    private static Spray Make(string name)
    {
        var go = new GameObject(name);
        return go.AddComponent<Spray>();
    }

    private void Add(Vector3 at, Vector3 velocity, float life, float size, float grow, bool floats, Color colour, float delay = 0f, float glow = 0f)
    {
        if (_drop == null)
        {
            var m = new LowPolyMesh();
            m.Ellipsoid(Vector3.zero, Vector3.one, 6, 4, Water, 0f, 1f);
            var tmp = new GameObject("tmp");
            m.AttachTo(tmp, false);
            _drop = tmp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(tmp);
        }
        var go = new GameObject("bit");
        go.transform.SetParent(transform, false);
        go.transform.position = at;
        go.transform.localScale = Vector3.zero;
        go.AddComponent<MeshFilter>().sharedMesh = _drop;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = glow > 0f ? LowPolyMesh.GlowFor(colour, glow) : LowPolyMesh.MaterialFor(colour);
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _bits.Add(new Bit { T = go.transform, Velocity = velocity, Born = Time.time + delay, Life = life, Size = size, Grow = grow, Floats = floats });
    }

    private void Update()
    {
        float dt = Time.deltaTime, now = Time.time;
        bool alive = false;
        for (int i = 0; i < _bits.Count; i++)
        {
            var b = _bits[i];
            if (b.T == null) continue;
            float age = now - b.Born;
            if (age < 0f) { alive = true; continue; }
            float t = age / b.Life;
            if (t >= 1f) { Destroy(b.T.gameObject); b.T = null; _bits[i] = b; continue; }
            alive = true;
            if (b.Floats)
            {
                // Mist: fast at the blowhole, slowing as it climbs, drifting with the wind.
                b.Velocity *= Mathf.Exp(-2.2f * dt);
                b.Velocity += new Vector3(0.6f, 0f, 0.3f) * dt;
                float s = b.Size * (1f + b.Grow * Mathf.Sqrt(t) * 3f) * (1f - t * t);
                b.T.localScale = Vector3.one * s;
            }
            else
            {
                b.Velocity += Vector3.down * _gravity * dt;
                b.T.localScale = Vector3.one * b.Size * (1f - t * 0.6f);
            }
            b.T.position += b.Velocity * dt;
            // Drops that fall back into the sea are gone.
            if (!b.Floats && b.Velocity.y < 0f && b.T.position.y < SeaWaves.HeightAt(b.T.position.x, b.T.position.z) - 0.2f)
            {
                Destroy(b.T.gameObject);
                b.T = null;
            }
            _bits[i] = b;
        }
        if (!alive) Destroy(gameObject);
    }
}
