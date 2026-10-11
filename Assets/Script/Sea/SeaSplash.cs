using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// White water where something breaks the surface: a whale's back, a diving fluke, a leaping
/// dolphin, a serpent's coils. Each splash is a patch of foam that spreads into a ring and
/// fades; the sea shader draws them (like ShipWake), so the foam is part of the water itself.
/// At most <see cref="Max"/> at a time; the oldest gives way.
/// </summary>
public static class SeaSplash
{
    public const int Max = 24;

    private struct Splash
    {
        public float X, Z, Born, Radius, Strength, Life;
    }

    private static readonly List<Splash> Live = new List<Splash>();
    private static readonly Vector4[] Buffer = new Vector4[Max];
    private static readonly int SplashId = Shader.PropertyToID("_SeaSplash");
    private static readonly int CountId = Shader.PropertyToID("_SeaSplashCount");

    /// <summary>A splash at <paramref name="at"/>, spreading to <paramref name="radius"/> metres over <paramref name="seconds"/>.</summary>
    public static void Add(Vector3 at, float radius, float strength = 1f, float seconds = 2.5f)
    {
        if (Live.Count >= Max) Live.RemoveAt(0);
        Live.Add(new Splash { X = at.x, Z = at.z, Born = SeaWaves.Now, Radius = radius, Strength = Mathf.Clamp01(strength), Life = Mathf.Max(0.2f, seconds) });
    }

    /// <summary>Forgets every splash (a new level).</summary>
    public static void Clear() => Live.Clear();

    /// <summary>Hands the live splashes to the shader: x, z, current radius, current strength (SeaSurface, each frame).</summary>
    public static void Push()
    {
        float now = SeaWaves.Now;
        Live.RemoveAll(s => now - s.Born > s.Life || now < s.Born);
        int n = Live.Count;
        for (int i = 0; i < Max; i++)
        {
            if (i >= n) { Buffer[i] = Vector4.zero; continue; }
            var s = Live[i];
            float t = (now - s.Born) / s.Life;
            float radius = s.Radius * (0.35f + 0.65f * Mathf.Sqrt(t));
            float fade = (1f - t) * (1f - t) * s.Strength;
            Buffer[i] = new Vector4(s.X, s.Z, radius, fade);
        }
        Shader.SetGlobalVectorArray(SplashId, Buffer);
        Shader.SetGlobalFloat(CountId, n);
    }
}
