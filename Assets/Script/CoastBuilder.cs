using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Land around the sea: a ragged low-poly coast just outside the reef wall, beach rising to
/// green hills with trees, wrapping behind the departure harbour and the destination dock.
/// Scenery only: one mesh, no colliders. The reef wall stays the boundary the ship hits.
/// Colours come from Resources/Land so no scene has to reference them.
/// </summary>
public static class CoastBuilder
{
    // Submeshes, one material each.
    private const int Sand = 0, Grass = 1, Hill = 2, Trunk = 3, Leaves = 4;
    private static readonly string[] MaterialNames = { "Sand", "Grass", "Hill", "Trunk", "Leaves" };

    public struct Settings
    {
        /// <summary>The open water the coast surrounds (x/z, world units).</summary>
        public Rect Water;
        /// <summary>How far inland the land goes.</summary>
        public float Depth;
        /// <summary>Triangle size: smaller is smoother and heavier.</summary>
        public float Cell;
        /// <summary>How far the shoreline wanders out from the water rectangle.</summary>
        public float Ragged;
        /// <summary>Trees per 1000 square units of grass.</summary>
        public float TreeDensity;
        public int Seed;
    }

    public static GameObject Build(Transform parent, Settings s)
    {
        var mats = new Material[MaterialNames.Length];
        for (int i = 0; i < mats.Length; i++)
        {
            mats[i] = Resources.Load<Material>("Land/" + MaterialNames[i]);
            if (mats[i] == null)
            {
                Debug.LogWarning("[COAST]: Resources/Land/" + MaterialNames[i] + " is missing; no land built.");
                return null;
            }
        }

        var verts = new List<Vector3>(80000);
        var tris = new List<int>[mats.Length];
        for (int i = 0; i < tris.Length; i++) tris[i] = new List<int>(20000);

        var rng = new System.Random(s.Seed);
        float ox = (float)rng.NextDouble() * 1000f, oz = (float)rng.NextDouble() * 1000f;

        Rect outer = new Rect(s.Water.xMin - s.Depth, s.Water.yMin - s.Depth, s.Water.width + 2f * s.Depth, s.Water.height + 2f * s.Depth);
        int nx = Mathf.CeilToInt(outer.width / s.Cell), nz = Mathf.CeilToInt(outer.height / s.Cell);

        // Heights on the grid corners, so neighbouring triangles share edges.
        var h = new float[nx + 1, nz + 1];
        for (int i = 0; i <= nx; i++)
            for (int j = 0; j <= nz; j++)
                h[i, j] = Height(outer.xMin + i * s.Cell, outer.yMin + j * s.Cell, s, ox, oz);

        for (int i = 0; i < nx; i++)
        {
            for (int j = 0; j < nz; j++)
            {
                float a = h[i, j], b = h[i + 1, j], c = h[i, j + 1], d = h[i + 1, j + 1];
                if (Mathf.Max(Mathf.Max(a, b), Mathf.Max(c, d)) < -0.4f) continue; // open water: nothing to draw

                float x0 = outer.xMin + i * s.Cell, z0 = outer.yMin + j * s.Cell, x1 = x0 + s.Cell, z1 = z0 + s.Cell;
                var p00 = new Vector3(x0, a, z0); var p10 = new Vector3(x1, b, z0);
                var p01 = new Vector3(x0, c, z1); var p11 = new Vector3(x1, d, z1);
                // Alternate the diagonal so the low-poly facets don't all lean one way.
                if (((i + j) & 1) == 0) { Tri(verts, tris, p00, p01, p11); Tri(verts, tris, p00, p11, p10); }
                else { Tri(verts, tris, p00, p01, p10); Tri(verts, tris, p10, p01, p11); }

                // Trees on gentle grass, away from the beach.
                float mean = (a + b + c + d) * 0.25f;
                float slope = Mathf.Max(Mathf.Abs(a - d), Mathf.Abs(b - c)) / s.Cell;
                if (mean > 2.2f && slope < 0.45f && rng.NextDouble() < s.TreeDensity * s.Cell * s.Cell / 1000f)
                {
                    float tx = x0 + (float)rng.NextDouble() * s.Cell, tz = z0 + (float)rng.NextDouble() * s.Cell;
                    Tree(verts, tris, new Vector3(tx, Height(tx, tz, s, ox, oz) - 0.2f, tz), 0.8f + (float)rng.NextDouble() * 0.7f, (float)rng.NextDouble() * 360f);
                }
            }
        }

        var mesh = new Mesh { name = "Coast", indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(verts);
        mesh.subMeshCount = tris.Length;
        for (int i = 0; i < tris.Length; i++) mesh.SetTriangles(tris[i], i, false);
        mesh.RecalculateNormals(); // vertices aren't shared, so every facet is flat
        mesh.RecalculateBounds();

        var go = new GameObject("Coast");
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterials = mats;
        mr.shadowCastingMode = ShadowCastingMode.On;
        Debug.Log($"🏝️ [COAST]: {verts.Count / 3} triangles of land around the sea.");
        return go;
    }

    /// <summary>
    /// Ground height: below water inside the sea, rising through a beach to hills inland.
    /// The shoreline sits Ragged units (varying) outside the water rectangle.
    /// </summary>
    private static float Height(float x, float z, Settings s, float ox, float oz)
    {
        // Distance outside the water rectangle (0 on its edge, negative inside).
        float dx = Mathf.Max(s.Water.xMin - x, x - s.Water.xMax);
        float dz = Mathf.Max(s.Water.yMin - z, z - s.Water.yMax);
        float outside = dx > 0f && dz > 0f ? Mathf.Sqrt(dx * dx + dz * dz) : Mathf.Max(dx, dz);

        // A wandering shoreline: further out in some places, right at the edge in others.
        float shore = s.Ragged * Mathf.PerlinNoise(ox + x * 0.012f, oz + z * 0.012f)
                    + 3f * Mathf.PerlinNoise(ox + x * 0.05f, oz + z * 0.05f);
        float d = outside - shore; // distance inland from the shoreline

        if (d < 0f) return Mathf.Max(-3f, d * 0.25f) - 0.3f;         // shelving into the sea
        float beach = Mathf.SmoothStep(0f, 1.1f, Mathf.Clamp01(d / 10f)); // sand, almost flat
        float rise = Mathf.Clamp01((d - 10f) / 35f);
        float hills = rise * (2.5f + 7f * Mathf.PerlinNoise(ox + x * 0.02f + 50f, oz + z * 0.02f + 50f))
                    + rise * 1.5f * Mathf.PerlinNoise(ox + x * 0.08f, oz + z * 0.08f);
        return beach + hills - 0.3f;
    }

    private static void Tri(List<Vector3> verts, List<int>[] tris, Vector3 a, Vector3 b, Vector3 c)
    {
        float mean = (a.y + b.y + c.y) / 3f;
        float steep = 1f - Mathf.Abs(Vector3.Cross(b - a, c - a).normalized.y);
        int sub = mean < 0.75f ? Sand : (mean > 6.5f || steep > 0.55f) ? Hill : Grass;
        int n = verts.Count;
        verts.Add(a); verts.Add(b); verts.Add(c);
        tris[sub].Add(n); tris[sub].Add(n + 1); tris[sub].Add(n + 2);
    }

    /// <summary>A low-poly tree: a six-sided trunk and two stacked cones of leaves.</summary>
    private static void Tree(List<Vector3> verts, List<int>[] tris, Vector3 foot, float scale, float yaw)
    {
        var rot = Quaternion.Euler(0f, yaw, 0f);
        Prism(verts, tris[Trunk], foot, rot, 0.35f * scale, 0.3f * scale, 1.6f * scale, 5);
        Cone(verts, tris[Leaves], foot + Vector3.up * 1.2f * scale, rot, 1.9f * scale, 3.2f * scale, 6);
        Cone(verts, tris[Leaves], foot + Vector3.up * 2.8f * scale, rot, 1.4f * scale, 2.6f * scale, 6);
    }

    private static void Cone(List<Vector3> verts, List<int> tris, Vector3 baseCentre, Quaternion rot, float radius, float height, int sides)
    {
        Vector3 top = baseCentre + Vector3.up * height;
        for (int k = 0; k < sides; k++)
        {
            Vector3 p = baseCentre + rot * Ring(k, sides, radius), q = baseCentre + rot * Ring(k + 1, sides, radius);
            int n = verts.Count;
            verts.Add(p); verts.Add(top); verts.Add(q);
            tris.Add(n); tris.Add(n + 1); tris.Add(n + 2);
        }
    }

    private static void Prism(List<Vector3> verts, List<int> tris, Vector3 foot, Quaternion rot, float r0, float r1, float height, int sides)
    {
        for (int k = 0; k < sides; k++)
        {
            Vector3 a = foot + rot * Ring(k, sides, r0), b = foot + rot * Ring(k + 1, sides, r0);
            Vector3 c = foot + Vector3.up * height + rot * Ring(k, sides, r1), d = foot + Vector3.up * height + rot * Ring(k + 1, sides, r1);
            int n = verts.Count;
            verts.Add(a); verts.Add(c); verts.Add(d); verts.Add(a); verts.Add(d); verts.Add(b);
            for (int i = 0; i < 6; i++) tris.Add(n + i);
        }
    }

    private static Vector3 Ring(int k, int sides, float r)
    {
        float t = k * Mathf.PI * 2f / sides;
        return new Vector3(Mathf.Cos(t) * r, 0f, Mathf.Sin(t) * r);
    }
}
