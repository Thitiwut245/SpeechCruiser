using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Colourful low-poly reefs: a rock base in one of several stone colours, topped with coral
/// (branching, brain, fan and tube) in bright colours. Built once per session as templates
/// that LevelGenerator instantiates like prefabs; every instance shares its variant's mesh.
/// Names contain "Reef", so the radar and the crew call them reefs. Materials come from
/// Resources/Reef; if they are missing, Variants returns null and the level uses the Reef prefab.
/// </summary>
public static class ReefFactory
{
    private static readonly string[] RockNames = { "RockGrey", "RockSand", "RockDark", "RockMoss" };
    private static readonly string[] CoralNames = { "CoralPink", "CoralOrange", "CoralPurple", "CoralYellow", "CoralTeal", "CoralRed" };

    private const float CoralScale = 1.5f;

    private static GameObject _root;
    private static GameObject[] _coral, _shore;

    /// <summary>Reefs for open water: rock and plenty of coral.</summary>
    public static GameObject[] Coral { get { Ensure(); return _coral; } }

    /// <summary>Rockier reefs for the boundary wall along the shore.</summary>
    public static GameObject[] Shore { get { Ensure(); return _shore; } }

    private static void Ensure()
    {
        if (_root != null) return;

        var rocks = Load(RockNames);
        var corals = Load(CoralNames);
        if (rocks == null || corals == null) { _coral = _shore = null; return; }

        // Templates stay inactive in a hidden, persistent root; instances are activated.
        _root = new GameObject("ReefTemplates");
        _root.SetActive(false);
        Object.DontDestroyOnLoad(_root);

        var rng = new System.Random(20261004);
        _coral = new GameObject[10];
        for (int i = 0; i < _coral.Length; i++) _coral[i] = Make("Reef Coral " + i, rng, rocks, corals, 8 + rng.Next(6));
        _shore = new GameObject[4];
        for (int i = 0; i < _shore.Length; i++) _shore[i] = Make("Reef Shore " + i, rng, rocks, corals, rng.Next(3));
    }

    private static Material[] Load(string[] names)
    {
        var mats = new Material[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            mats[i] = Resources.Load<Material>("Reef/" + names[i]);
            if (mats[i] == null)
            {
                Debug.LogWarning("[REEF]: Resources/Reef/" + names[i] + " is missing; using the Reef prefab.");
                return null;
            }
        }
        return mats;
    }

    /// <summary>One variant: a rock in one stone colour plus <paramref name="corals"/> coral pieces.</summary>
    private static GameObject Make(string name, System.Random rng, Material[] rocks, Material[] corals, int coralPieces)
    {
        // Submesh 0 is the rock; then up to three coral colours.
        var palette = new List<Material> { rocks[rng.Next(rocks.Length)] };
        int colours = Mathf.Min(coralPieces, 1 + rng.Next(3));
        while (palette.Count < 1 + colours)
        {
            var c = corals[rng.Next(corals.Length)];
            if (!palette.Contains(c)) palette.Add(c);
        }

        var verts = new List<Vector3>(2000);
        var tris = new List<int>[palette.Count];
        for (int i = 0; i < tris.Length; i++) tris[i] = new List<int>(600);

        // The rock: a lumpy, flattened low-poly blob, its foot under the water.
        float rockTop = Rock(verts, tris[0], rng);

        for (int k = 0; k < coralPieces; k++)
        {
            var sub = tris[1 + rng.Next(palette.Count - 1)];
            float a = (float)rng.NextDouble() * Mathf.PI * 2f, r = Mathf.Sqrt((float)rng.NextDouble()) * 0.62f; // spread over the whole top
            var foot = new Vector3(Mathf.Cos(a) * r, rockTop * (1f - r * 0.9f) - 0.1f, Mathf.Sin(a) * r);
            // Coral reads from the top-down camera only if it's big: everything at 1.5x.
            int start = verts.Count;
            switch (rng.Next(4))
            {
                case 0: Branching(verts, sub, foot, rng); break;
                case 1: Brain(verts, sub, foot, rng); break;
                case 2: Fan(verts, sub, foot, rng); break;
                default: Tubes(verts, sub, foot, rng); break;
            }
            for (int v = start; v < verts.Count; v++) verts[v] = foot + (verts[v] - foot) * CoralScale;
        }

        var mesh = new Mesh { name = name };
        mesh.SetVertices(verts);
        mesh.subMeshCount = tris.Length;
        for (int i = 0; i < tris.Length; i++) mesh.SetTriangles(tris[i], i, false);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        var go = new GameObject(name);
        go.transform.SetParent(_root.transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterials = palette.ToArray();
        return go;
    }

    // ------------------------------------------------------------ shapes (unit-ish size; LevelGenerator scales)

    private static float Rock(List<Vector3> verts, List<int> tris, System.Random rng)
    {
        // A ring-and-cap blob: 3 rings of 7-9 points, jittered, flattened.
        int sides = 7 + rng.Next(3);
        float[] heights = { -0.35f, 0.12f, 0.4f };
        float[] radii = { 1f, 0.92f, 0.55f };
        var rings = new Vector3[heights.Length, sides];
        float stretch = 0.75f + (float)rng.NextDouble() * 0.5f; // oval footprints
        for (int h = 0; h < heights.Length; h++)
            for (int k = 0; k < sides; k++)
            {
                float t = (k + (float)rng.NextDouble() * 0.4f) / sides * Mathf.PI * 2f;
                float r = radii[h] * (0.78f + (float)rng.NextDouble() * 0.3f);
                rings[h, k] = new Vector3(Mathf.Cos(t) * r * stretch, heights[h] + (float)rng.NextDouble() * 0.1f, Mathf.Sin(t) * r);
            }
        var top = new Vector3(((float)rng.NextDouble() - 0.5f) * 0.3f, 0.5f + (float)rng.NextDouble() * 0.15f, ((float)rng.NextDouble() - 0.5f) * 0.3f);

        for (int h = 0; h < heights.Length - 1; h++)
            for (int k = 0; k < sides; k++)
            {
                int n = (k + 1) % sides;
                Quad(verts, tris, rings[h, k], rings[h + 1, k], rings[h + 1, n], rings[h, n]);
            }
        for (int k = 0; k < sides; k++) Tri(verts, tris, rings[heights.Length - 1, k], top, rings[heights.Length - 1, (k + 1) % sides]);
        return top.y;
    }

    private static void Branching(List<Vector3> verts, List<int> tris, Vector3 foot, System.Random rng)
    {
        // A staghorn: a few thin, tapering limbs leaning out, each with a fork.
        int limbs = 3 + rng.Next(3);
        for (int i = 0; i < limbs; i++)
        {
            var dir = Quaternion.Euler(-20f - (float)rng.NextDouble() * 40f, (float)rng.NextDouble() * 360f, 0f) * Vector3.up;
            float len = 0.3f + (float)rng.NextDouble() * 0.25f;
            var tip = foot + dir * len;
            Limb(verts, tris, foot, tip, 0.055f, 0.03f);
            var fork = Quaternion.AngleAxis(35f, Vector3.Cross(dir, Vector3.right).normalized) * dir;
            Limb(verts, tris, tip, tip + fork * len * 0.6f, 0.03f, 0.015f);
        }
    }

    private static void Brain(List<Vector3> verts, List<int> tris, Vector3 foot, System.Random rng)
    {
        // A squat dome of facets.
        float r = 0.16f + (float)rng.NextDouble() * 0.12f;
        int sides = 7;
        var ring = new Vector3[sides];
        for (int k = 0; k < sides; k++)
        {
            float t = k * Mathf.PI * 2f / sides;
            ring[k] = foot + new Vector3(Mathf.Cos(t) * r, r * 0.45f, Mathf.Sin(t) * r);
        }
        var cap = foot + Vector3.up * r * 0.9f;
        for (int k = 0; k < sides; k++)
        {
            var b0 = foot + (ring[k] - foot) * 1.15f - Vector3.up * r * 0.45f;
            var b1 = foot + (ring[(k + 1) % sides] - foot) * 1.15f - Vector3.up * r * 0.45f;
            Quad(verts, tris, b0, ring[k], ring[(k + 1) % sides], b1);
            Tri(verts, tris, ring[k], cap, ring[(k + 1) % sides]);
        }
    }

    private static void Fan(List<Vector3> verts, List<int> tris, Vector3 foot, System.Random rng)
    {
        // A flat sea fan, double-sided.
        float h = 0.3f + (float)rng.NextDouble() * 0.2f, w = h * 0.9f;
        var rot = Quaternion.Euler(0f, (float)rng.NextDouble() * 180f, 0f);
        int blades = 5;
        for (int i = 0; i < blades; i++)
        {
            float a0 = Mathf.Lerp(-60f, 60f, i / (float)blades) * Mathf.Deg2Rad, a1 = Mathf.Lerp(-60f, 60f, (i + 1) / (float)blades) * Mathf.Deg2Rad;
            var p0 = foot + rot * new Vector3(Mathf.Sin(a0) * w, Mathf.Cos(a0) * h, 0f);
            var p1 = foot + rot * new Vector3(Mathf.Sin(a1) * w, Mathf.Cos(a1) * h, 0f);
            Tri(verts, tris, foot, p0, p1);
            Tri(verts, tris, foot, p1, p0);
        }
    }

    private static void Tubes(List<Vector3> verts, List<int> tris, Vector3 foot, System.Random rng)
    {
        // A cluster of short open-topped tubes.
        int count = 3 + rng.Next(3);
        for (int i = 0; i < count; i++)
        {
            var at = foot + new Vector3(((float)rng.NextDouble() - 0.5f) * 0.18f, 0f, ((float)rng.NextDouble() - 0.5f) * 0.18f);
            float len = 0.12f + (float)rng.NextDouble() * 0.2f;
            Limb(verts, tris, at, at + new Vector3(((float)rng.NextDouble() - 0.5f) * 0.08f, len, ((float)rng.NextDouble() - 0.5f) * 0.08f), 0.04f, 0.05f);
        }
    }

    // ------------------------------------------------------------ primitives (flat-shaded: no shared vertices)

    private static void Limb(List<Vector3> verts, List<int> tris, Vector3 from, Vector3 to, float r0, float r1)
    {
        var axis = (to - from).normalized;
        var side = Vector3.Cross(axis, Mathf.Abs(axis.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
        var up = Vector3.Cross(side, axis);
        const int sides = 5;
        for (int k = 0; k < sides; k++)
        {
            float t0 = k * Mathf.PI * 2f / sides, t1 = (k + 1) * Mathf.PI * 2f / sides;
            Vector3 o0 = side * Mathf.Cos(t0) + up * Mathf.Sin(t0), o1 = side * Mathf.Cos(t1) + up * Mathf.Sin(t1);
            Quad(verts, tris, from + o0 * r0, to + o0 * r1, to + o1 * r1, from + o1 * r0);
            Tri(verts, tris, to + o0 * r1, to, to + o1 * r1);
        }
    }

    private static void Quad(List<Vector3> verts, List<int> tris, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        Tri(verts, tris, a, b, c);
        Tri(verts, tris, a, c, d);
    }

    private static void Tri(List<Vector3> verts, List<int> tris, Vector3 a, Vector3 b, Vector3 c)
    {
        int n = verts.Count;
        verts.Add(a); verts.Add(b); verts.Add(c);
        tris.Add(n); tris.Add(n + 1); tris.Add(n + 2);
    }
}
