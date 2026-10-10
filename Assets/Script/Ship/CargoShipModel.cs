using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The player's cargo ship, built in code as a faceted low-poly model: a hull lofted through
/// cross-sections (raked, flared bow; parallel midbody for the containers; a square transom),
/// red below the waterline and navy above with a white boot stripe, a forecastle and bulwarks,
/// hatch covers, a white deckhouse and bridge aft with wings and windows, a funnel and a mast.
///
/// It keeps the old model's footprint and heights, in the ship root's space, so nothing else
/// moves: 5.4 m beam, stern at z = -8, bow at z = +9.3, deck at y = 1.35 where the containers
/// sit, and the waterline at y = -1 (the root rides 1 m above mean sea level), 1.2 m of draft.
/// </summary>
public static class CargoShipModel
{
    public const float WaterlineY = -1f;
    public const float DeckY = 1.35f;
    private const float BootTopY = -0.75f; // top of the red bottom paint
    private const float StripeY = -0.45f;   // top of the white boot stripe

    // Materials, in submesh order.
    private enum Paint { Topsides, Bottom, Stripe, Deck, House, Window, Funnel, Soot, Steel, Hatch }

    private static readonly Color[] Colours =
    {
        new Color(0.13f, 0.19f, 0.30f), // topsides: deep navy
        new Color(0.62f, 0.16f, 0.12f), // antifouling red
        new Color(0.93f, 0.93f, 0.90f), // boot stripe
        new Color(0.40f, 0.44f, 0.40f), // deck: grey-green
        new Color(0.94f, 0.93f, 0.90f), // superstructure white
        new Color(0.10f, 0.14f, 0.20f), // windows
        new Color(0.80f, 0.36f, 0.16f), // funnel: the company's orange
        new Color(0.08f, 0.08f, 0.09f), // funnel top
        new Color(0.55f, 0.57f, 0.58f), // steel: mast, rails, bollards
        new Color(0.62f, 0.30f, 0.20f), // hatch covers
    };

    private static Material[] _materials;
    private static Mesh _mesh;

    /// <summary>One cross-section of the hull at z: deck and bottom half-widths, keel depth, deck height.</summary>
    private struct Station
    {
        public float Z, Deck, Bottom, Keel, Top;
        public Station(float z, float deck, float bottom, float keel, float top) { Z = z; Deck = deck; Bottom = bottom; Keel = keel; Top = top; }
    }

    // Stern to bow. The sheer rises into a forecastle; the bow narrows to a raked stem.
    private static readonly Station[] Stations =
    {
        new Station(-8.0f, 2.30f, 1.80f, -1.55f, 1.55f),
        new Station(-7.2f, 2.55f, 2.10f, -1.95f, 1.45f),
        new Station(-5.8f, 2.70f, 2.35f, -2.20f, 1.35f),
        new Station(4.6f, 2.70f, 2.35f, -2.20f, 1.35f),
        new Station(6.2f, 2.58f, 1.85f, -2.20f, 1.50f),
        new Station(7.4f, 2.20f, 1.15f, -2.10f, 1.95f),
        new Station(8.4f, 1.45f, 0.45f, -1.85f, 2.05f),
        new Station(9.3f, 0.10f, 0.04f, -1.20f, 2.15f),
    };

    /// <summary>Puts the ship's mesh and paint on <paramref name="go"/> (adding a filter and renderer if needed).</summary>
    public static void Dress(GameObject go)
    {
        var filter = go.GetComponent<MeshFilter>();
        if (filter == null) filter = go.AddComponent<MeshFilter>();
        var renderer = go.GetComponent<MeshRenderer>();
        if (renderer == null) renderer = go.AddComponent<MeshRenderer>();
        filter.sharedMesh = Mesh;
        renderer.sharedMaterials = Materials;
    }

    /// <summary>The ship's mesh (shared) and its materials, one per submesh.</summary>
    public static Mesh Mesh => _mesh != null ? _mesh : (_mesh = Build());

    public static Material[] Materials
    {
        get
        {
            if (_materials != null) return _materials;
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            _materials = new Material[Colours.Length];
            for (int i = 0; i < Colours.Length; i++)
            {
                var m = new Material(lit) { name = "Ship " + (Paint)i };
                m.SetColor("_BaseColor", Colours[i]);
                m.SetFloat("_Smoothness", (Paint)i == Paint.Window ? 0.8f : 0.2f);
                _materials[i] = m;
            }
            return _materials;
        }
    }

    // ------------------------------------------------------------ building

    private static readonly List<Vector3> V = new List<Vector3>();
    private static readonly List<int>[] T = new List<int>[System.Enum.GetValues(typeof(Paint)).Length];

    private static Mesh Build()
    {
        V.Clear();
        for (int i = 0; i < T.Length; i++) T[i] = new List<int>();

        Hull();
        Bulwarks();
        DeckFittings();
        Superstructure();

        var mesh = new Mesh { name = "CargoShip" };
        mesh.SetVertices(V);
        mesh.subMeshCount = T.Length;
        for (int i = 0; i < T.Length; i++) mesh.SetTriangles(T[i], i);
        mesh.RecalculateNormals(); // every face has its own vertices: flat, faceted shading
        mesh.RecalculateBounds();
        return mesh;
    }

    // Which way is "out" for a face at a point: every face is turned to face that way, so
    // nothing is inside-out whatever order its corners were listed in.
    private static System.Func<Vector3, Vector3> _out = c => c;

    private static void Tri(Paint p, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 n = Vector3.Cross(b - a, c - a);
        if (Vector3.Dot(n, _out((a + b + c) / 3f)) < 0f) { var t = b; b = c; c = t; }
        int i = V.Count;
        V.Add(a); V.Add(b); V.Add(c);
        T[(int)p].Add(i); T[(int)p].Add(i + 1); T[(int)p].Add(i + 2);
    }

    /// <summary>A quad a-b-c-d, wound so it faces the side it's seen from when listed clockwise.</summary>
    private static void Quad(Paint p, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        Tri(p, a, b, c);
        Tri(p, a, c, d);
    }

    private static void Box(Paint p, Vector3 centre, Vector3 size)
    {
        var keep = _out;
        _out = c => c - centre;
        Vector3 h = size * 0.5f;
        Vector3 c000 = centre + new Vector3(-h.x, -h.y, -h.z), c100 = centre + new Vector3(h.x, -h.y, -h.z);
        Vector3 c010 = centre + new Vector3(-h.x, h.y, -h.z), c110 = centre + new Vector3(h.x, h.y, -h.z);
        Vector3 c001 = centre + new Vector3(-h.x, -h.y, h.z), c101 = centre + new Vector3(h.x, -h.y, h.z);
        Vector3 c011 = centre + new Vector3(-h.x, h.y, h.z), c111 = centre + new Vector3(h.x, h.y, h.z);
        Quad(p, c010, c011, c111, c110); // top
        Quad(p, c000, c100, c101, c001); // bottom
        Quad(p, c000, c010, c110, c100); // back (-z)
        Quad(p, c101, c111, c011, c001); // front (+z)
        Quad(p, c001, c011, c010, c000); // left (-x)
        Quad(p, c100, c110, c111, c101); // right (+x)
        _out = keep;
    }

    /// <summary>A box narrower at the top (the funnel).</summary>
    private static void Tapered(Paint p, Vector3 baseCentre, Vector2 bottom, Vector2 top, float height, float lean)
    {
        Vector3 b = baseCentre, t = baseCentre + new Vector3(0f, height, -lean);
        var keep = _out;
        Vector3 core = (b + t) * 0.5f;
        _out = c => c - core;
        Vector3[] lo =
        {
            b + new Vector3(-bottom.x, 0, -bottom.y), b + new Vector3(bottom.x, 0, -bottom.y),
            b + new Vector3(bottom.x, 0, bottom.y), b + new Vector3(-bottom.x, 0, bottom.y),
        };
        Vector3[] hi =
        {
            t + new Vector3(-top.x, 0, -top.y), t + new Vector3(top.x, 0, -top.y),
            t + new Vector3(top.x, 0, top.y), t + new Vector3(-top.x, 0, top.y),
        };
        for (int i = 0; i < 4; i++)
        {
            int j = (i + 1) % 4;
            Quad(p, lo[i], hi[i], hi[j], lo[j]);
        }
        Quad(p, hi[0], hi[3], hi[2], hi[1]);
        _out = keep;
    }

    // ---- the hull

    /// <summary>A section's outline down the starboard side: deck edge, stripe, boot top, chine, keel.</summary>
    private static Vector3[] Outline(Station s, float side)
    {
        float chineY = s.Keel + 0.45f;
        // Width at a height, between the deck edge and the chine (straight flare).
        float At(float y) => Mathf.Lerp(s.Bottom, s.Deck, Mathf.InverseLerp(chineY, s.Top, y));
        return new[]
        {
            new Vector3(side * s.Deck, s.Top, s.Z),
            new Vector3(side * At(StripeY), StripeY, s.Z),
            new Vector3(side * At(BootTopY), BootTopY, s.Z),
            new Vector3(side * s.Bottom, chineY, s.Z),
            new Vector3(side * s.Bottom * 0.35f, s.Keel, s.Z),
            new Vector3(0f, s.Keel, s.Z),
        };
    }

    private static readonly Paint[] Bands = { Paint.Topsides, Paint.Stripe, Paint.Bottom, Paint.Bottom, Paint.Bottom };

    private static void Hull()
    {
        // Out is away from the hull's centre line (and down for the bottom, fore and aft at the ends).
        _out = c => c - new Vector3(0f, 0.3f, Mathf.Clamp(c.z, -7.5f, 8.8f));
        for (int k = 0; k < Stations.Length - 1; k++)
        {
            Station a = Stations[k], b = Stations[k + 1];
            foreach (float side in new[] { 1f, -1f })
            {
                var oa = Outline(a, side);
                var ob = Outline(b, side);
                for (int r = 0; r < Bands.Length; r++)
                {
                    if (side > 0) Quad(Bands[r], oa[r], ob[r], ob[r + 1], oa[r + 1]);
                    else Quad(Bands[r], oa[r], oa[r + 1], ob[r + 1], ob[r]);
                }
            }
            // The deck between the two stations.
            var hullOut = _out;
            _out = c => Vector3.up;
            Quad(Paint.Deck, new Vector3(-a.Deck, a.Top, a.Z), new Vector3(-b.Deck, b.Top, b.Z),
                             new Vector3(b.Deck, b.Top, b.Z), new Vector3(a.Deck, a.Top, a.Z));
            _out = hullOut;
        }

        // The transom: the flat stern, painted like the sides.
        var s0 = Stations[0];
        var right = Outline(s0, 1f);
        var left = Outline(s0, -1f);
        for (int r = 0; r < Bands.Length; r++)
            Quad(Bands[r], left[r], right[r], right[r + 1], left[r + 1]);

        // The stem: close the bow.
        var sb = Stations[Stations.Length - 1];
        var bowR = Outline(sb, 1f);
        var bowL = Outline(sb, -1f);
        for (int r = 0; r < Bands.Length; r++)
            Quad(Bands[r], bowR[r], bowL[r], bowL[r + 1], bowR[r + 1]);
        _out = c => Vector3.up;
        Tri(Paint.Deck, new Vector3(-sb.Deck, sb.Top, sb.Z), new Vector3(sb.Deck, sb.Top, sb.Z), new Vector3(0f, sb.Top, sb.Z + 0.01f));
    }

    /// <summary>A low wall along the deck edge, higher round the forecastle, with a white rail on top.</summary>
    private static void Bulwarks()
    {
        for (int k = 0; k < Stations.Length - 1; k++)
        {
            Station a = Stations[k], b = Stations[k + 1];
            float ha = a.Z > 5f ? 0.75f : 0.35f, hb = b.Z > 5f ? 0.75f : 0.35f;
            foreach (float side in new[] { 1f, -1f })
            {
                Vector3 a0 = new Vector3(side * a.Deck, a.Top, a.Z), b0 = new Vector3(side * b.Deck, b.Top, b.Z);
                Vector3 a1 = a0 + Vector3.up * ha, b1 = b0 + Vector3.up * hb;
                Vector3 inA = new Vector3(-side * 0.12f, 0f, 0f);
                // Outer face, inner face, and the rail on top.
                float s = side;
                _out = c => new Vector3(s, 0f, 0f);
                Quad(Paint.Topsides, a0, a1, b1, b0);
                _out = c => new Vector3(-s, 0f, 0f);
                Quad(Paint.House, a0 + inA, b0 + inA, b1 + inA, a1 + inA);
                _out = c => Vector3.up;
                Quad(Paint.House, a1, a1 + inA, b1 + inA, b1);
            }
        }
        // A rail across the stern.
        var s0 = Stations[0];
        Box(Paint.House, new Vector3(0f, s0.Top + 0.2f, s0.Z + 0.06f), new Vector3(s0.Deck * 2f, 0.4f, 0.12f));
    }

    /// <summary>Hatch covers under the containers, bollards, the forecastle mast and a windlass.</summary>
    private static void DeckFittings()
    {
        for (int i = 0; i < 3; i++)
            Box(Paint.Hatch, new Vector3(0f, DeckY + 0.06f, -3.0f + i * 2.4f), new Vector3(3.9f, 0.12f, 2.1f));
        Box(Paint.Hatch, new Vector3(0f, DeckY + 0.06f, 4.3f), new Vector3(3.2f, 0.12f, 1.4f));

        foreach (float side in new[] { 1f, -1f })
        {
            Box(Paint.Steel, new Vector3(side * 2.1f, DeckY + 0.2f, -6.9f), new Vector3(0.3f, 0.4f, 0.3f));
            Box(Paint.Steel, new Vector3(side * 1.7f, 2.05f, 7.5f), new Vector3(0.3f, 0.4f, 0.3f));
        }
        Box(Paint.Steel, new Vector3(0f, 2.15f, 7.0f), new Vector3(1.2f, 0.45f, 0.6f)); // windlass
        Box(Paint.Steel, new Vector3(0f, 3.6f, 6.4f), new Vector3(0.18f, 3.4f, 0.18f)); // foremast
        Box(Paint.Steel, new Vector3(0f, 4.6f, 6.4f), new Vector3(1.4f, 0.1f, 0.1f));   // its yard
    }

    /// <summary>The deckhouse and bridge aft, with wings and a band of windows, and the funnel behind.</summary>
    private static void Superstructure()
    {
        const float back = -7.6f, front = -4.4f;
        float mid = (back + front) * 0.5f, len = front - back;

        // Two decks of accommodation, the upper a little shorter; then the bridge on top.
        Box(Paint.House, new Vector3(0f, DeckY + 1.0f, mid), new Vector3(4.4f, 2.0f, len));
        Box(Paint.House, new Vector3(0f, DeckY + 2.9f, mid - 0.2f), new Vector3(4.0f, 1.8f, len - 0.4f));
        Box(Paint.House, new Vector3(0f, DeckY + 4.45f, mid + 0.15f), new Vector3(3.6f, 1.3f, len - 0.9f));
        // Bridge wings out to the ship's sides, and the roof.
        Box(Paint.House, new Vector3(0f, DeckY + 4.0f, front - 0.6f), new Vector3(5.4f, 0.14f, 1.2f));
        Box(Paint.House, new Vector3(0f, DeckY + 5.17f, mid + 0.15f), new Vector3(3.9f, 0.14f, len - 0.6f));

        // Windows: the bridge's wide front band, and a row on each deck below.
        float bridgeFront = mid + 0.15f + (len - 0.9f) * 0.5f;
        Box(Paint.Window, new Vector3(0f, DeckY + 4.6f, bridgeFront + 0.02f), new Vector3(3.3f, 0.55f, 0.06f));
        for (int d = 0; d < 2; d++)
        {
            float y = DeckY + 1.25f + d * 1.85f, z = d == 0 ? front : front - 0.2f;
            for (int w = -2; w <= 2; w++)
                Box(Paint.Window, new Vector3(w * 0.8f, y, z + 0.02f), new Vector3(0.45f, 0.4f, 0.06f));
        }
        foreach (float side in new[] { 1f, -1f })
            for (int w = 0; w < 3; w++)
                Box(Paint.Window, new Vector3(side * 2.21f, DeckY + 1.25f, back + 0.8f + w * 0.9f), new Vector3(0.06f, 0.4f, 0.45f));

        // The funnel, raked back, in company colours with a black top; a radar mast in front.
        Tapered(Paint.Funnel, new Vector3(0f, DeckY + 5.2f, back + 0.9f), new Vector2(0.75f, 0.6f), new Vector2(0.6f, 0.5f), 1.5f, 0.25f);
        // The black top starts just inside the funnel and a hair wider, so no two faces share a plane.
        Tapered(Paint.Soot, new Vector3(0f, DeckY + 6.66f, back + 0.66f), new Vector2(0.62f, 0.52f), new Vector2(0.6f, 0.5f), 0.39f, 0.05f);
        Box(Paint.Steel, new Vector3(0f, DeckY + 6.0f, mid + 0.6f), new Vector3(0.12f, 1.5f, 0.12f));
        Box(Paint.Steel, new Vector3(0f, DeckY + 6.6f, mid + 0.6f), new Vector3(1.1f, 0.08f, 0.2f));
    }
}
