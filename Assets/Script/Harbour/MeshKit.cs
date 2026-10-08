using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Collects simple low-poly shapes (boxes, pitched roofs, prisms, cones) into one mesh, a
/// submesh per material, flat-shaded: how the terminal and the town are drawn cheaply.
/// Every face is wound to point outward, so nothing is inside-out.
/// </summary>
public class MeshKit
{
    private readonly List<Vector3> _v = new List<Vector3>();
    private readonly List<int>[] _t;

    public MeshKit(int materials)
    {
        _t = new List<int>[materials];
        for (int i = 0; i < materials; i++) _t[i] = new List<int>();
    }

    public void Quad(int m, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
    {
        if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f) { var t = b; b = d; d = t; }
        int n = _v.Count;
        _v.Add(a); _v.Add(b); _v.Add(c); _v.Add(a); _v.Add(c); _v.Add(d);
        for (int i = 0; i < 6; i++) _t[m].Add(n + i);
    }

    public void Tri(int m, Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
    {
        if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f) { var t = b; b = c; c = t; }
        int n = _v.Count;
        _v.Add(a); _v.Add(b); _v.Add(c);
        _t[m].Add(n); _t[m].Add(n + 1); _t[m].Add(n + 2);
    }

    /// <summary>A box of <paramref name="size"/> centred on <paramref name="c"/>, turned by <paramref name="yaw"/> degrees.</summary>
    public void Box(int m, Vector3 c, Vector3 size, float yaw = 0f)
    {
        Quaternion r = Quaternion.Euler(0f, yaw, 0f);
        Vector3 h = size * 0.5f;
        Vector3 P(float x, float y, float z) => c + r * new Vector3(x * h.x, y * h.y, z * h.z);
        Quad(m, P(-1, 1, -1), P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), Vector3.up);
        Quad(m, P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1), Vector3.down);
        Quad(m, P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), P(1, -1, -1), r * Vector3.back);
        Quad(m, P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), r * Vector3.forward);
        Quad(m, P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1), r * Vector3.left);
        Quad(m, P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1), r * Vector3.right);
    }

    /// <summary>A pitched roof: ridge along local z, eaves along local x, <paramref name="eaves"/> its base centre.</summary>
    public void Gable(int m, Vector3 eaves, Vector3 size, float yaw = 0f)
    {
        Quaternion r = Quaternion.Euler(0f, yaw, 0f);
        float hx = size.x * 0.5f, hz = size.z * 0.5f, h = size.y;
        Vector3 P(float x, float y, float z) => eaves + r * new Vector3(x, y, z);
        Vector3 a = P(-hx, 0, -hz), b = P(hx, 0, -hz), c = P(hx, 0, hz), d = P(-hx, 0, hz);
        Vector3 r0 = P(0, h, -hz), r1 = P(0, h, hz);
        Quad(m, a, d, r1, r0, r * new Vector3(-1, 1, 0));
        Quad(m, b, r0, r1, c, r * new Vector3(1, 1, 0));
        Tri(m, a, r0, b, r * Vector3.back);
        Tri(m, d, c, r1, r * Vector3.forward);
    }

    /// <summary>An upright prism (a round tower, low-poly), from <paramref name="foot"/> up.</summary>
    public void Prism(int m, Vector3 foot, float radius, float height, int sides, float topRadius = -1f)
    {
        if (topRadius < 0f) topRadius = radius;
        Vector3 top = foot + Vector3.up * height;
        for (int k = 0; k < sides; k++)
        {
            float a0 = k * Mathf.PI * 2f / sides, a1 = (k + 1) * Mathf.PI * 2f / sides;
            Vector3 d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
            Vector3 outward = (d0 + d1).normalized;
            Quad(m, foot + d0 * radius, foot + d1 * radius, top + d1 * topRadius, top + d0 * topRadius, outward);
            Tri(m, top, top + d0 * topRadius, top + d1 * topRadius, Vector3.up);
        }
    }

    /// <summary>A cone (a spire, a lighthouse cap).</summary>
    public void Cone(int m, Vector3 foot, float radius, float height, int sides)
    {
        Vector3 tip = foot + Vector3.up * height;
        for (int k = 0; k < sides; k++)
        {
            float a0 = k * Mathf.PI * 2f / sides, a1 = (k + 1) * Mathf.PI * 2f / sides;
            Vector3 d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
            Tri(m, foot + d0 * radius, foot + d1 * radius, tip, (d0 + d1).normalized + Vector3.up * 0.5f);
        }
    }

    public GameObject Finish(Transform parent, string name, Material[] mats)
    {
        var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(_v);
        mesh.subMeshCount = _t.Length;
        for (int i = 0; i < _t.Length; i++) mesh.SetTriangles(_t[i], i);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterials = mats;
        return go;
    }

    /// <summary>Plain lit materials from a list of colours (shared, made once per caller).</summary>
    public static Material[] Paints(string prefix, Color[] colours, int glossy = -1)
    {
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        var mats = new Material[colours.Length];
        for (int i = 0; i < colours.Length; i++)
        {
            mats[i] = new Material(lit) { name = prefix + " " + i };
            mats[i].SetColor("_BaseColor", colours[i]);
            mats[i].SetFloat("_Smoothness", i == glossy ? 0.7f : 0.15f);
        }
        return mats;
    }
}
