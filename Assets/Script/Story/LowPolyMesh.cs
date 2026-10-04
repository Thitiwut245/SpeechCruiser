using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds flat-shaded low-poly meshes from simple shapes, one submesh per colour: the story's
/// portraits, rowboats and sea sights. Every triangle is turned to face away from the middle
/// of its shape, so normals point outward. Colours become copies of a URP Lit material.
/// </summary>
public sealed class LowPolyMesh
{
    public readonly List<Vector3> Verts = new List<Vector3>(4000);
    public readonly Dictionary<Color, List<int>> Tris = new Dictionary<Color, List<int>>();

    private List<int> For(Color c)
    {
        c.a = 1f;
        if (!Tris.TryGetValue(c, out var list)) Tris[c] = list = new List<int>(600);
        return list;
    }

    // The middle of the shape being built: every triangle is turned to face away from it,
    // so normals point out whichever way the corners were listed.
    private Vector3 _centre;

    public void Tri(Vector3 a, Vector3 b, Vector3 c, Color colour)
    {
        if (Vector3.Dot(Vector3.Cross(b - a, c - a), (a + b + c) / 3f - _centre) < 0f) { var s = b; b = c; c = s; }
        var t = For(colour);
        int n = Verts.Count;
        Verts.Add(a); Verts.Add(b); Verts.Add(c);
        t.Add(n); t.Add(n + 1); t.Add(n + 2);
    }

    public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color colour)
    {
        Tri(a, b, c, colour);
        Tri(a, c, d, colour);
    }

    /// <summary>
    /// An ellipsoid band from <paramref name="from"/> (0 = top) to <paramref name="to"/> (1 = bottom)
    /// of the way down. frontOnly keeps the half facing the camera (for beards).
    /// </summary>
    public void Ellipsoid(Vector3 c, Vector3 r, int segs, int rings, Color colour, float from, float to, bool frontOnly = false)
    {
        _centre = c;
        for (int i = 0; i < rings; i++)
        {
            float t0 = i / (float)rings, t1 = (i + 1) / (float)rings;
            if (t1 <= from + 0.001f || t0 >= to - 0.001f) continue;
            float p0 = Mathf.Lerp(0f, Mathf.PI, Mathf.Max(t0, from)), p1 = Mathf.Lerp(0f, Mathf.PI, Mathf.Min(t1, to));
            for (int k = 0; k < segs; k++)
            {
                float a0 = k * Mathf.PI * 2f / segs, a1 = (k + 1) * Mathf.PI * 2f / segs;
                if (frontOnly && Mathf.Sin((a0 + a1) * 0.5f) > 0.05f) continue;
                Vector3 v00 = P(c, r, p0, a0), v01 = P(c, r, p0, a1), v10 = P(c, r, p1, a0), v11 = P(c, r, p1, a1);
                Quad(v00, v01, v11, v10, colour);
            }
        }
    }

    private static Vector3 P(Vector3 c, Vector3 r, float polar, float azimuth)
    {
        // azimuth 0 at +x, -pi/2 toward -z (the face).
        return c + new Vector3(r.x * Mathf.Sin(polar) * Mathf.Cos(azimuth), r.y * Mathf.Cos(polar), r.z * Mathf.Sin(polar) * Mathf.Sin(azimuth));
    }

    /// <summary>A many-sided frustum standing on <paramref name="foot"/>, optionally capped.</summary>
    public void Frustum(Vector3 foot, float r0, float r1, float height, int sides, float sx, float sz, Color colour, bool capTop, bool capTopIsFlat)
    {
        Vector3 top = foot + Vector3.up * height;
        _centre = foot + Vector3.up * (height * 0.5f);
        for (int k = 0; k < sides; k++)
        {
            float a0 = k * Mathf.PI * 2f / sides, a1 = (k + 1) * Mathf.PI * 2f / sides;
            Vector3 d0 = new Vector3(Mathf.Cos(a0) * sx, 0f, Mathf.Sin(a0) * sz), d1 = new Vector3(Mathf.Cos(a1) * sx, 0f, Mathf.Sin(a1) * sz);
            Vector3 b0 = foot + d0 * r0, b1 = foot + d1 * r0, t0 = top + d0 * r1, t1 = top + d1 * r1;
            Quad(b0, t0, t1, b1, colour);
            if (capTop) Tri(t0, top + (capTopIsFlat ? Vector3.zero : Vector3.up * 0.04f), t1, colour);
        }
    }

    public void Box(Vector3 c, Vector3 size, Color colour) => Box(c, size, colour, Quaternion.identity);

    public void Box(Vector3 c, Vector3 size, Color colour, Quaternion rot)
    {
        Vector3 h = size * 0.5f;
        _centre = c;
        Vector3 V(float x, float y, float z) => c + rot * new Vector3(x * h.x, y * h.y, z * h.z);
        Vector3 a = V(-1, -1, -1), b = V(1, -1, -1), cc = V(1, 1, -1), d = V(-1, 1, -1);
        Vector3 e = V(-1, -1, 1), f = V(1, -1, 1), g = V(1, 1, 1), hh = V(-1, 1, 1);
        Quad(a, d, cc, b, colour); // front (-z)
        Quad(f, g, hh, e, colour); // back
        Quad(e, hh, d, a, colour); // left
        Quad(b, cc, g, f, colour); // right
        Quad(d, hh, g, cc, colour); // top
        Quad(e, a, b, f, colour); // bottom
    }

    /// <summary>A flat ring lying on the XZ plane, facing up.</summary>
    public void Ring(Vector3 c, float inner, float outer, int sides, Color colour)
    {
        _centre = c - Vector3.up;
        for (int k = 0; k < sides; k++)
        {
            float a0 = k * Mathf.PI * 2f / sides, a1 = (k + 1) * Mathf.PI * 2f / sides;
            Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
            Quad(c + d0 * inner, c + d0 * outer, c + d1 * outer, c + d1 * inner, colour);
        }
    }

    /// <summary>Triangles added next face away from this point (for shapes built by hand).</summary>
    public void Around(Vector3 centre) => _centre = centre;

    /// <summary>A four-sided pyramid from a square base around <paramref name="baseCentre"/> to <paramref name="tip"/>.</summary>
    public void Pyramid(Vector3 baseCentre, Vector3 tip, float half, Color colour)
    {
        _centre = Vector3.Lerp(baseCentre, tip, 0.25f);
        Vector3 a = baseCentre + new Vector3(-half, half, 0f), b = baseCentre + new Vector3(half, half, 0f);
        Vector3 c = baseCentre + new Vector3(half, -half, 0f), d = baseCentre + new Vector3(-half, -half, 0f);
        Tri(a, tip, b, colour);
        Tri(b, tip, c, colour);
        Tri(c, tip, d, colour);
        Tri(d, tip, a, colour);
    }

    // ------------------------------------------------------------ to a GameObject

    private static readonly Dictionary<Color, Material> _materials = new Dictionary<Color, Material>();
    private static Material _base;

    /// <summary>The URP Lit material every colour copies; null (with a warning) if it's missing.</summary>
    public static Material BaseMaterial
    {
        get
        {
            if (_base != null) return _base;
            _base = Resources.Load<Material>("Reef/RockGrey");
            if (_base == null) Debug.LogWarning("[LOWPOLY]: Resources/Reef/RockGrey is missing; story models can't be drawn.");
            return _base;
        }
    }

    public static Material MaterialFor(Color c)
    {
        c.a = 1f;
        if (_materials.TryGetValue(c, out var m) && m != null) return m;
        m = new Material(BaseMaterial) { name = "LowPoly " + ColorUtility.ToHtmlStringRGB(c) };
        m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.15f);
        _materials[c] = m;
        return m;
    }

    /// <summary>Puts the mesh on <paramref name="go"/> (a MeshFilter and MeshRenderer).</summary>
    public MeshRenderer AttachTo(GameObject go, bool castShadows = true)
    {
        var mesh = new Mesh { name = go.name };
        if (Verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(Verts);
        var colours = new List<Color>(Tris.Keys);
        mesh.subMeshCount = colours.Count;
        var mats = new Material[colours.Count];
        for (int i = 0; i < colours.Count; i++)
        {
            mesh.SetTriangles(Tris[colours[i]], i, false);
            mats[i] = MaterialFor(colours[i]);
        }
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterials = mats;
        r.shadowCastingMode = castShadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
        return r;
    }
}
