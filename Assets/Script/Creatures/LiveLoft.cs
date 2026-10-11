using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A lofted body whose spine moves every frame (the sea serpent): rings of corners around a
/// spine the caller bends, rebuilt in place, flat-shaded like the rest of the world. Colours
/// are fixed per ring and corner (scales, a pale belly, a crest of spines along the back), so
/// the triangles never change, only where they are.
/// </summary>
public sealed class LiveLoft
{
    private readonly int _rings, _sides;
    private readonly Mesh _mesh;
    private readonly Vector3[] _verts;
    private readonly Vector2[] _radii;
    private readonly float[] _crest; // height of the spine on top of each ring (0: none)
    private readonly Vector3[,] _ring;
    private Vector3[] _ups;
    private bool _flip, _flipHead, _flipTail;
    private int _capStart; // first vertex of the end caps

    /// <param name="colourOf">Colour of the face between ring i and i+1 at corner k; corner -1 is the crest.</param>
    public LiveLoft(GameObject go, IList<Vector2> radii, IList<float> crest, int sides, System.Func<int, int, Color> colourOf, float glow = 0f)
    {
        _rings = radii.Count;
        _sides = sides;
        _radii = new Vector2[_rings];
        _crest = new float[_rings];
        for (int i = 0; i < _rings; i++) { _radii[i] = radii[i]; _crest[i] = crest != null ? crest[i] : 0f; }
        _ring = new Vector3[_rings, sides];
        _ups = new Vector3[_rings];

        // Faces: (rings-1)*sides quads, a crest fin per ring gap, and the two end caps.
        int quads = (_rings - 1) * sides;
        int tris = quads * 2 + (_rings - 1) * 2 + sides * 2;
        _verts = new Vector3[tris * 3];
        var byColour = new Dictionary<Color, List<int>>();
        void Add(Color c, int tri)
        {
            c.a = 1f;
            if (!byColour.TryGetValue(c, out var l)) byColour[c] = l = new List<int>();
            l.Add(tri * 3); l.Add(tri * 3 + 1); l.Add(tri * 3 + 2);
        }
        int t = 0;
        for (int i = 0; i < _rings - 1; i++)
            for (int k = 0; k < sides; k++) { Color c = colourOf(i, k); Add(c, t++); Add(c, t++); }
        for (int i = 0; i < _rings - 1; i++) { Color c = colourOf(i, -1); Add(c, t++); Add(c, t++); }
        for (int k = 0; k < sides * 2; k++) Add(colourOf(k < sides ? 0 : _rings - 2, k % sides), t++);

        _mesh = new Mesh { name = go.name };
        _mesh.MarkDynamic();
        _mesh.vertices = _verts;
        var colours = new List<Color>(byColour.Keys);
        _mesh.subMeshCount = colours.Count;
        var mats = new Material[colours.Count];
        for (int i = 0; i < colours.Count; i++)
        {
            _mesh.SetTriangles(byColour[colours[i]], i, false);
            mats[i] = glow > 0f ? LowPolyMesh.GlowFor(colours[i], glow) : LowPolyMesh.MaterialFor(colours[i]);
        }
        go.AddComponent<MeshFilter>().sharedMesh = _mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterials = mats;

        // Which way round the corners go so faces point out: test on a straight spine.
        var straight = new Vector3[_rings];
        for (int i = 0; i < _rings; i++) straight[i] = new Vector3(0f, 0f, -i);
        Update(straight);
        _flip = Faces(0, true, Vector3.zero);
        _flipHead = Faces(_capStart, false, Vector3.forward);
        _flipTail = Faces(_capStart + _sides * 3, false, Vector3.back);
        Update(straight);
    }

    // True when the triangle at vertex <paramref name="v"/> points the wrong way (radially
    // outward for the body, or along <paramref name="dir"/> for a cap) on the straight spine.
    private bool Faces(int v, bool radial, Vector3 dir)
    {
        Vector3 a = _verts[v], b = _verts[v + 1], c = _verts[v + 2];
        Vector3 want = radial ? new Vector3(((a + b + c) / 3f).x, ((a + b + c) / 3f).y, 0f) : dir;
        return Vector3.Dot(Vector3.Cross(b - a, c - a), want) < 0f;
    }

    /// <summary>Places the body along <paramref name="spine"/> (local space, head first).</summary>
    public void Update(IList<Vector3> spine)
    {
        int n = _rings;
        for (int i = 0; i < n; i++)
        {
            Vector3 fwd = (spine[Mathf.Max(i - 1, 0)] - spine[Mathf.Min(i + 1, n - 1)]).normalized; // toward the head
            Vector3 up = Vector3.ProjectOnPlane(Vector3.up, fwd);
            up = up.sqrMagnitude > 1e-4f ? up.normalized : (i > 0 ? _ups[i - 1] : Vector3.up);
            _ups[i] = up;
            Vector3 right = Vector3.Cross(up, fwd);
            for (int k = 0; k < _sides; k++)
            {
                float a = k * Mathf.PI * 2f / _sides;
                _ring[i, k] = spine[i] + right * (Mathf.Cos(a) * _radii[i].x) + up * (Mathf.Sin(a) * _radii[i].y);
            }
        }

        int v = 0;
        void Tri(Vector3 a, Vector3 b, Vector3 c, bool flip)
        {
            _verts[v++] = a;
            if (flip) { _verts[v++] = c; _verts[v++] = b; }
            else { _verts[v++] = b; _verts[v++] = c; }
        }
        for (int i = 0; i < n - 1; i++)
            for (int k = 0; k < _sides; k++)
            {
                int k1 = (k + 1) % _sides;
                Tri(_ring[i, k], _ring[i + 1, k], _ring[i + 1, k1], _flip);
                Tri(_ring[i, k], _ring[i + 1, k1], _ring[i, k1], _flip);
            }
        // The crest: a thin spine fin standing on the back between rings.
        for (int i = 0; i < n - 1; i++)
        {
            Vector3 b0 = spine[i] + _ups[i] * _radii[i].y * 0.92f, b1 = spine[i + 1] + _ups[i + 1] * _radii[i + 1].y * 0.92f;
            Vector3 tip = Vector3.Lerp(b0, b1, 0.3f) + _ups[i] * _crest[i];
            Tri(b0, tip, b1, false);
            Tri(b0, tip, b1, true); // both sides
        }
        // End caps.
        _capStart = v;
        for (int end = 0; end < 2; end++)
        {
            int i = end == 0 ? 0 : n - 1;
            Vector3 along = end == 0 ? spine[0] - spine[1] : spine[n - 1] - spine[n - 2];
            Vector3 tip = spine[i] + along.normalized * Mathf.Min(_radii[i].x, _radii[i].y) * 0.8f;
            for (int k = 0; k < _sides; k++)
            {
                Tri(_ring[i, k], _ring[i, (k + 1) % _sides], tip, end == 0 ? _flipHead : _flipTail);
            }
        }
        _mesh.vertices = _verts;
        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();
    }
}
