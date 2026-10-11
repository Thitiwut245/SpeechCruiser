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

    /// <summary>
    /// A body lofted along a spine (a whale, a dolphin, a gull): an elliptical ring of
    /// <paramref name="sides"/> corners at each centre, <paramref name="radii"/> wide (x) and
    /// tall (y), closed at both ends. Rings face along the spine with world up as up. Faces on
    /// the underside get <paramref name="belly"/> (countershading, as real sea animals have).
    /// </summary>
    public void Loft(IList<Vector3> centres, IList<Vector2> radii, int sides, Color top, Color belly, float bellyLine = -0.35f)
    {
        int n = centres.Count;
        var rings = new Vector3[n, sides];
        for (int i = 0; i < n; i++)
        {
            Vector3 fwd = (centres[Mathf.Min(i + 1, n - 1)] - centres[Mathf.Max(i - 1, 0)]).normalized;
            Vector3 up = Vector3.ProjectOnPlane(Vector3.up, fwd).normalized;
            Vector3 right = Vector3.Cross(up, fwd);
            for (int k = 0; k < sides; k++)
            {
                float a = k * Mathf.PI * 2f / sides;
                rings[i, k] = centres[i] + right * (Mathf.Cos(a) * radii[i].x) + up * (Mathf.Sin(a) * radii[i].y);
            }
        }
        for (int i = 0; i < n - 1; i++)
            for (int k = 0; k < sides; k++)
            {
                int k1 = (k + 1) % sides;
                float mid = (k + 0.5f) * Mathf.PI * 2f / sides;
                _centre = (centres[i] + centres[i + 1]) * 0.5f;
                Quad(rings[i, k], rings[i + 1, k], rings[i + 1, k1], rings[i, k1], Mathf.Sin(mid) < bellyLine ? belly : top);
            }
        // Close the ends with a fan to a point just beyond them.
        for (int end = 0; end < 2; end++)
        {
            int i = end == 0 ? 0 : n - 1;
            Vector3 along = end == 0 ? centres[0] - centres[1] : centres[n - 1] - centres[n - 2];
            Vector3 tip = centres[i] + along.normalized * Mathf.Min(radii[i].x, radii[i].y) * 0.6f;
            _centre = centres[i] - along.normalized * 0.01f;
            for (int k = 0; k < sides; k++)
            {
                float mid = (k + 0.5f) * Mathf.PI * 2f / sides;
                Tri(rings[i, k], rings[i, (k + 1) % sides], tip, Mathf.Sin(mid) < bellyLine ? belly : top);
            }
        }
    }

    /// <summary>
    /// A thin fin (a flipper, a fluke half, a dorsal fin): a flat outline given as points in its
    /// own plane (x along, y across), given a little thickness, placed by <paramref name="at"/>.
    /// </summary>
    public void Fin(IList<Vector2> outline, float thickness, Color colour, Matrix4x4 at)
    {
        int n = outline.Count;
        Vector2 mid = Vector2.zero;
        foreach (var p in outline) mid += p;
        mid /= n;
        Vector3 c = at.MultiplyPoint3x4(new Vector3(mid.x, mid.y, 0f));
        Vector3 Top(int i) => at.MultiplyPoint3x4(new Vector3(outline[i].x, outline[i].y, thickness * 0.5f));
        Vector3 Bot(int i) => at.MultiplyPoint3x4(new Vector3(outline[i].x, outline[i].y, -thickness * 0.5f));
        Vector3 ct = at.MultiplyPoint3x4(new Vector3(mid.x, mid.y, thickness * 0.5f));
        Vector3 cb = at.MultiplyPoint3x4(new Vector3(mid.x, mid.y, -thickness * 0.5f));
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            _centre = c - (ct - c) * 4f; Tri(ct, Top(i), Top(j), colour);
            _centre = c + (ct - c) * 4f; Tri(cb, Bot(j), Bot(i), colour);
            _centre = c; Quad(Top(i), Bot(i), Bot(j), Top(j), colour);
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
    private static readonly Dictionary<Color, Material> _glows = new Dictionary<Color, Material>();
    private static Material _base, _glowBase;

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

    /// <summary>
    /// A colour that glows (ghost ships, spirit lights): a copy of Resources/Story/Glow, an
    /// emissive material the game already ships, so its glow survives in a build. Falls back
    /// to the plain colour if it's missing.
    /// </summary>
    public static Material GlowFor(Color c, float strength)
    {
        c.a = 1f;
        var key = new Color(c.r, c.g, c.b, strength);
        if (_glows.TryGetValue(key, out var m) && m != null) return m;
        if (_glowBase == null) _glowBase = Resources.Load<Material>("Story/Glow");
        if (_glowBase == null) return MaterialFor(c);
        m = new Material(_glowBase) { name = "Glow " + ColorUtility.ToHtmlStringRGB(c) };
        m.SetColor("_BaseColor", c);
        m.SetColor("_EmissionColor", c * strength);
        m.EnableKeyword("_EMISSION");
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.1f);
        if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
        _glows[key] = m;
        return m;
    }

    /// <summary>Puts the mesh on <paramref name="go"/> (a MeshFilter and MeshRenderer). Glow &gt; 0 makes every colour emissive.</summary>
    public MeshRenderer AttachTo(GameObject go, bool castShadows = true, float glow = 0f)
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
            mats[i] = glow > 0f ? GlowFor(colours[i], glow) : MaterialFor(colours[i]);
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
