using UnityEngine;

/// <summary>
/// The sea's waves: a sum of four Gerstner waves, computed the same way here and in the water
/// shader (Resources/Sea/Sea.shader), so anything that floats rides exactly the waves that are
/// drawn. Mean sea level is y = 0.
///
/// A Gerstner wave moves each point of the surface round a small circle, so crests come out
/// sharp and troughs flat, like real swell. Each wave's speed follows deep-water physics
/// (longer waves travel faster). <see cref="Roughness"/> scales the whole sea: 1 is a fresh
/// breeze, a storm is about twice that.
/// </summary>
public static class SeaWaves
{
    public const int Count = 4;
    private const float Gravity = 9.81f;

    private struct Wave
    {
        public Vector2 Dir;
        public float Length;    // metres crest to crest
        public float Steepness; // 0..1 of a breaking wave; the four together stay well under 1
        public Wave(float dx, float dz, float length, float steepness)
        {
            Dir = new Vector2(dx, dz).normalized;
            Length = length;
            Steepness = steepness;
        }
    }

    // A long swell, two cross seas and a short chop, from slightly different directions so
    // the pattern never visibly repeats.
    private static readonly Wave[] Waves =
    {
        new Wave(1f, 0.35f, 58f, 0.075f),
        new Wave(0.55f, -1f, 31f, 0.07f),
        new Wave(-0.45f, 1f, 17f, 0.06f),
        new Wave(1f, 1.1f, 8.5f, 0.05f),
    };

    private static float _roughness = 1f;

    /// <summary>Harbours: (x, z, calm radius, open-sea radius). Inside, the water is nearly flat.</summary>
    public const int CalmCount = 2;
    public static readonly Vector4[] Calm = new Vector4[CalmCount];

    /// <summary>Sheltered water round a harbour: still within <paramref name="inner"/>, open sea past <paramref name="outer"/>.</summary>
    public static void SetCalm(int slot, Vector3 centre, float inner, float outer)
    {
        if (slot >= 0 && slot < CalmCount) Calm[slot] = new Vector4(centre.x, centre.z, inner, outer);
    }

    public static void ClearCalm()
    {
        for (int i = 0; i < CalmCount; i++) Calm[i] = Vector4.zero;
    }

    /// <summary>How much of the waves reach (x, z): about 0.1 inside a harbour, 1 at sea.</summary>
    public static float Exposure(float x, float z)
    {
        float f = 1f;
        for (int i = 0; i < CalmCount; i++)
        {
            Vector4 c = Calm[i];
            if (c.w <= 0f) continue;
            float d = Mathf.Sqrt((x - c.x) * (x - c.x) + (z - c.y) * (z - c.y));
            f = Mathf.Min(f, Mathf.Lerp(0.1f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(c.z, c.w, d))));
        }
        return f;
    }

    /// <summary>How rough the sea is: 1 a fresh breeze, about 2 a storm.</summary>
    public static float Roughness
    {
        get => _roughness;
        set => _roughness = Mathf.Clamp(value, 0f, 2.5f);
    }

    /// <summary>The waves' clock, shared with the shader (pushed as _SeaTime each frame).</summary>
    public static float Now => Time.timeSinceLevelLoad;

    /// <summary>Highest a crest can rise above mean sea level, in metres.</summary>
    public static float MaxHeight
    {
        get
        {
            float h = 0f;
            for (int i = 0; i < Count; i++) h += Amplitude(i);
            return h;
        }
    }

    private static float K(int i) => 2f * Mathf.PI / Waves[i].Length;
    private static float Steep(int i) => Mathf.Min(Waves[i].Steepness * _roughness, 0.22f);
    private static float Amplitude(int i) => Steep(i) / K(i);
    private static float Speed(int i) => Mathf.Sqrt(Gravity / K(i));

    /// <summary>
    /// The shader's wave table: A = (dir.x, dir.z, k, amplitude), B = (phase speed, steepness, 0, 0).
    /// </summary>
    public static void Fill(Vector4[] a, Vector4[] b)
    {
        for (int i = 0; i < Count; i++)
        {
            a[i] = new Vector4(Waves[i].Dir.x, Waves[i].Dir.y, K(i), Amplitude(i));
            b[i] = new Vector4(Speed(i), Steep(i), 0f, 0f);
        }
    }

    /// <summary>Where the surface point that starts at rest at (x, z) has moved to, at time t.</summary>
    public static Vector3 Displacement(float x, float z, float t)
    {
        float exposure = Exposure(x, z);
        Vector3 d = Vector3.zero;
        for (int i = 0; i < Count; i++)
        {
            var w = Waves[i];
            float k = K(i), a = Amplitude(i) * exposure;
            float f = k * (w.Dir.x * x + w.Dir.y * z - Speed(i) * t);
            float c = Mathf.Cos(f);
            d.x += w.Dir.x * a * c;
            d.y += a * Mathf.Sin(f);
            d.z += w.Dir.y * a * c;
        }
        return d;
    }

    /// <summary>
    /// The water's height above mean sea level at world (x, z), now. The waves also move the
    /// surface sideways, so this finds the resting point that ends up here (a few fixed-point steps).
    /// </summary>
    public static float HeightAt(float x, float z) => HeightAt(x, z, Now);

    public static float HeightAt(float x, float z, float t)
    {
        float px = x, pz = z;
        for (int step = 0; step < 4; step++)
        {
            Vector3 d = Displacement(px, pz, t);
            px = x - d.x;
            pz = z - d.z;
        }
        return Displacement(px, pz, t).y;
    }

    public static float HeightAt(Vector3 world) => HeightAt(world.x, world.z, Now);

    /// <summary>
    /// How a hull at <paramref name="centre"/>, heading <paramref name="forward"/>, sits on the
    /// water now: its rise (the mean of bow, stern and both sides), its pitch in degrees (bow
    /// down positive) and its roll in degrees (starboard up positive), roll damped by the beam.
    /// </summary>
    public static void HullPose(Vector3 centre, Vector3 forward, float halfLength, float halfBeam,
                                out float heave, out float pitch, out float roll)
    {
        forward.y = 0f;
        forward.Normalize();
        Vector3 right = new Vector3(forward.z, 0f, -forward.x);
        float bow = HeightAt(centre + forward * halfLength);
        float stern = HeightAt(centre - forward * halfLength);
        float starboard = HeightAt(centre + right * halfBeam);
        float port = HeightAt(centre - right * halfBeam);
        heave = (bow + stern + starboard + port) * 0.25f;
        pitch = Mathf.Atan2(stern - bow, halfLength * 2f) * Mathf.Rad2Deg;
        roll = Mathf.Atan2(starboard - port, halfBeam * 2f) * Mathf.Rad2Deg * 0.6f;
    }
}
