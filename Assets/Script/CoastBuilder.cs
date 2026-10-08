using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Land around the sea, as shaped low-poly terrain just outside the reef wall:
/// - The coast changes along its length: sandy beaches and coves in some stretches, rocky
///   cliffs and headlands in others, with a wandering shoreline.
/// - Inland it rises into rolling hills and ridges; steep faces and high tops are bare rock.
/// - Round each harbour the land flattens into port ground at quay height.
/// - Forests grow in patches (pines up high, round trees lower), grass is green or dry in
///   patches, and boulders lie along the shore.
/// Scenery only: one mesh, no colliders. The reef wall stays the boundary the ship hits.
/// Colours come from Resources/Land (plus rock and dry grass made here) so no scene has to
/// reference them.
/// </summary>
public static class CoastBuilder
{
    // Submeshes, one material each.
    private const int Sand = 0, Grass = 1, Hill = 2, Trunk = 3, Leaves = 4, Rock = 5, DryGrass = 6, Pine = 7, Paved = 8;
    private static readonly string[] MaterialNames = { "Sand", "Grass", "Hill", "Trunk", "Leaves" };
    private const float PortHeight = 2.4f;

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
        /// <summary>Harbour positions (x, z): the land round them is flat port ground.</summary>
        public Vector2[] Harbours;
        /// <summary>Port ground reaching out into the water behind each harbour (x/z rectangles), flat at quay height.</summary>
        public Rect[] Aprons;
    }

    private static Material[] _extra;

    /// <summary>Builds the land (and the towns on it). <paramref name="shape"/> answers height questions afterwards.</summary>
    public static GameObject Build(Transform parent, Settings s, out CoastShape shape)
    {
        shape = null;
        var mats = new Material[9];
        for (int i = 0; i < MaterialNames.Length; i++)
        {
            mats[i] = Resources.Load<Material>("Land/" + MaterialNames[i]);
            if (mats[i] == null)
            {
                Debug.LogWarning("[COAST]: Resources/Land/" + MaterialNames[i] + " is missing; no land built.");
                return null;
            }
        }
        if (_extra == null)
        {
            _extra = new[]
            {
                Tint(mats[Grass], "Rock", new Color(0.43f, 0.41f, 0.38f)),
                Tint(mats[Grass], "DryGrass", new Color(0.46f, 0.46f, 0.25f)),
                Tint(mats[Leaves], "Pine", new Color(0.06f, 0.20f, 0.12f)),
                Tint(mats[Grass], "Paved", new Color(0.40f, 0.40f, 0.39f)),
            };
        }
        mats[Rock] = _extra[0];
        mats[DryGrass] = _extra[1];
        mats[Pine] = _extra[2];
        mats[Paved] = _extra[3];

        var verts = new List<Vector3>(150000);
        var tris = new List<int>[mats.Length];
        for (int i = 0; i < tris.Length; i++) tris[i] = new List<int>(20000);

        var rng = new System.Random(s.Seed);
        var land = new CoastShape(s, (float)rng.NextDouble() * 1000f, (float)rng.NextDouble() * 1000f);
        shape = land;

        // The towns first, so the trees keep out of the streets.
        var buildings = TownBuilder.Build(parent, land, s.Harbours, s.Aprons, s.Seed);
        var built = new HashSet<Vector2Int>();
        foreach (var b in buildings)
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                    built.Add(new Vector2Int(Mathf.FloorToInt(b.Position.x / 8f) + dx, Mathf.FloorToInt(b.Position.y / 8f) + dz));

        Rect outer = new Rect(s.Water.xMin - s.Depth, s.Water.yMin - s.Depth, s.Water.width + 2f * s.Depth, s.Water.height + 2f * s.Depth);
        int nx = Mathf.CeilToInt(outer.width / s.Cell), nz = Mathf.CeilToInt(outer.height / s.Cell);

        // Heights on the grid corners, so neighbouring triangles share edges.
        var h = new float[nx + 1, nz + 1];
        for (int i = 0; i <= nx; i++)
            for (int j = 0; j <= nz; j++)
                h[i, j] = land.Height(outer.xMin + i * s.Cell, outer.yMin + j * s.Cell);

        int trees = 0, boulders = 0;
        for (int i = 0; i < nx; i++)
        {
            for (int j = 0; j < nz; j++)
            {
                float a = h[i, j], b = h[i + 1, j], c = h[i, j + 1], d = h[i + 1, j + 1];
                if (Mathf.Max(Mathf.Max(a, b), Mathf.Max(c, d)) < -0.6f) continue; // open water: nothing to draw

                float x0 = outer.xMin + i * s.Cell, z0 = outer.yMin + j * s.Cell, x1 = x0 + s.Cell, z1 = z0 + s.Cell;
                var p00 = new Vector3(x0, a, z0); var p10 = new Vector3(x1, b, z0);
                var p01 = new Vector3(x0, c, z1); var p11 = new Vector3(x1, d, z1);
                // Alternate the diagonal so the low-poly facets don't all lean one way.
                if (((i + j) & 1) == 0) { Tri(verts, tris, land, p00, p01, p11); Tri(verts, tris, land, p00, p11, p10); }
                else { Tri(verts, tris, land, p00, p01, p10); Tri(verts, tris, land, p10, p01, p11); }

                float mean = (a + b + c + d) * 0.25f;
                float slope = Mathf.Max(Mathf.Abs(a - d), Mathf.Abs(b - c)) / s.Cell;
                float cx = x0 + s.Cell * 0.5f, cz = z0 + s.Cell * 0.5f;

                // Trees: thick in forest patches, scattered elsewhere; none on port ground or sand.
                float density = s.TreeDensity * (land.Forest(cx, cz) > 0.55f ? 4f : 0.6f);
                bool town = built.Contains(new Vector2Int(Mathf.FloorToInt(cx / 8f), Mathf.FloorToInt(cz / 8f)));
                if (!town && mean > 2.6f && mean < 22f && slope < 0.5f && land.PortFlat(cx, cz) < 0.2f &&
                    rng.NextDouble() < density * s.Cell * s.Cell / 1000f)
                {
                    float tx = x0 + (float)rng.NextDouble() * s.Cell, tz = z0 + (float)rng.NextDouble() * s.Cell;
                    var foot = new Vector3(tx, land.Height(tx, tz) - 0.2f, tz);
                    float size = 0.8f + (float)rng.NextDouble() * 0.8f, yaw = (float)rng.NextDouble() * 360f;
                    if (mean > 9f || rng.NextDouble() < 0.35) PineTree(verts, tris, foot, size, yaw);
                    else RoundTree(verts, tris, foot, size, yaw);
                    trees++;
                }

                // Boulders along the shore and at the cliff feet.
                if (mean > -0.5f && mean < 2.2f && land.PortFlat(cx, cz) < 0.2f && rng.NextDouble() < (land.Cliffness(cx, cz) > 0.4f ? 0.35 : 0.06))
                {
                    float bx = x0 + (float)rng.NextDouble() * s.Cell, bz = z0 + (float)rng.NextDouble() * s.Cell;
                    Boulder(verts, tris[Rock], new Vector3(bx, land.Height(bx, bz) - 0.3f, bz), 0.6f + (float)rng.NextDouble() * 1.8f, rng);
                    boulders++;
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
        Debug.Log($"🏝️ [COAST]: {verts.Count / 3} triangles of land, {trees} trees, {boulders} boulders.");
        return go;
    }

    /// <summary>
    /// The border: where the channel meets the land, the shore itself stops the ship. Along
    /// each side of <paramref name="water"/>, a trigger strip just off the shoreline: over
    /// beaches it is shallows the ship runs aground on (a Shoal: no damage, back off astern),
    /// under cliffs it is rocks that hole the hull (tagged Hazard, called "Reef"). Port ground
    /// (<paramref name="aprons"/>) has its own quay bump and is skipped.
    /// </summary>
    public static void BuildBorder(Transform parent, CoastShape land, Rect water, Rect[] aprons)
    {
        var root = new GameObject("ShoreBorder").transform;
        root.SetParent(parent, false);
        const float step = 10f, depth = 40f;
        int shallows = 0, rocks = 0;

        // Each side: walk along it; at each stop, come in from the sea until the bottom is
        // shallow enough to ground on (1.4 m), and put the strip there, reaching inland.
        var sides = new[]
        {
            (from: new Vector2(water.xMin, water.yMin), to: new Vector2(water.xMin, water.yMax), inland: Vector2.left),
            (from: new Vector2(water.xMax, water.yMin), to: new Vector2(water.xMax, water.yMax), inland: Vector2.right),
            (from: new Vector2(water.xMin, water.yMin), to: new Vector2(water.xMax, water.yMin), inland: Vector2.down),
            (from: new Vector2(water.xMin, water.yMax), to: new Vector2(water.xMax, water.yMax), inland: Vector2.up),
        };
        foreach (var side in sides)
        {
            float length = Vector2.Distance(side.from, side.to);
            Vector2 along = (side.to - side.from).normalized;
            for (float t = step * 0.5f; t < length; t += step)
            {
                Vector2 edge = side.from + along * t;
                Vector2? hit = null;
                for (float d = -35f; d <= 45f; d += 1f)
                {
                    Vector2 p = edge + side.inland * d;
                    if (land.Height(p.x, p.y) > -1.4f) { hit = p; break; }
                }
                if (!hit.HasValue) continue;
                Vector2 h = hit.Value;
                bool port = false;
                if (aprons != null)
                    foreach (var a in aprons)
                    {
                        var grown = Rect.MinMaxRect(a.xMin - 6f, a.yMin - 6f, a.xMax + 6f, a.yMax + 6f);
                        if (grown.Contains(h)) port = true;
                    }
                if (port) continue;

                bool cliff = land.Cliffness(h.x, h.y) > 0.5f;
                Vector2 c = h + side.inland * (depth * 0.5f - 2f);
                var strip = new GameObject(cliff ? "Shore Rocks Reef" : "Shore Shallows");
                strip.transform.SetParent(root, false);
                strip.transform.position = new Vector3(c.x, 0f, c.y);
                var box = strip.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = side.inland.x != 0f ? new Vector3(depth, 12f, step + 1f) : new Vector3(step + 1f, 12f, depth);
                try { strip.tag = "Hazard"; } catch (UnityException) { }
                if (!cliff) strip.AddComponent<Shoal>();
                if (cliff) rocks++; else shallows++;
            }
        }
        Debug.Log($"🏖️ [COAST]: border of {shallows} shallows and {rocks} rocky stretches.");
    }

    private static Material Tint(Material like, string name, Color c)
    {
        var m = new Material(like) { name = name };
        m.SetColor("_BaseColor", c);
        return m;
    }

    /// <summary>The shape of the land: heights, and what kind of ground is where.</summary>
    public class CoastShape
    {
        private readonly Settings _s;
        private readonly float _ox, _oz;

        public CoastShape(Settings s, float ox, float oz) { _s = s; _ox = ox; _oz = oz; }

        private float N(float x, float z, float scale, float offset = 0f) =>
            Mathf.PerlinNoise(_ox + offset + x * scale, _oz + offset + z * scale);

        /// <summary>0 on a beach stretch, 1 on a cliff stretch (changes slowly along the coast).</summary>
        public float Cliffness(float x, float z) =>
            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.40f, 0.58f, N(x, z, 0.0065f, 300f))) * (1f - PortFlat(x, z));

        /// <summary>1 on the port ground round a harbour, fading to 0 further off.</summary>
        public float PortFlat(float x, float z)
        {
            float f = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-15f, 45f, -ApronDepth(x, z) + 30f));
            if (_s.Harbours == null) return f;
            foreach (var hb in _s.Harbours)
            {
                float d = Vector2.Distance(new Vector2(x, z), hb);
                f = Mathf.Max(f, 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(55f, 110f, d)));
            }
            return f;
        }

        /// <summary>
        /// How far inside the nearest port apron (x, z) is: positive inside, negative outside
        /// (minus the distance to it). Very negative when there are none.
        /// </summary>
        public float ApronDepth(float x, float z)
        {
            float best = -1000f;
            if (_s.Aprons == null) return best;
            foreach (var r in _s.Aprons)
            {
                float inX = Mathf.Min(x - r.xMin, r.xMax - x), inZ = Mathf.Min(z - r.yMin, r.yMax - z);
                float d = inX >= 0f && inZ >= 0f ? Mathf.Min(inX, inZ)
                        : -Mathf.Sqrt(Mathf.Pow(Mathf.Min(inX, 0f), 2f) + Mathf.Pow(Mathf.Min(inZ, 0f), 2f));
                best = Mathf.Max(best, d);
            }
            return best;
        }

        public float Forest(float x, float z) => N(x, z, 0.011f, 700f);
        public float DryPatch(float x, float z) => N(x, z, 0.02f, 900f);

        /// <summary>Height of the ground (or the sea bottom just off the shore) at (x, z).</summary>
        public float Height(float x, float z)
        {
            // Distance outside the water rectangle (0 on its edge, negative inside).
            float dx = Mathf.Max(_s.Water.xMin - x, x - _s.Water.xMax);
            float dz = Mathf.Max(_s.Water.yMin - z, z - _s.Water.yMax);
            float outside = dx > 0f && dz > 0f ? Mathf.Sqrt(dx * dx + dz * dz) : Mathf.Max(dx, dz);
            // Port aprons are land reaching into the water: the shore runs round them too.
            float apron = ApronDepth(x, z);
            outside = Mathf.Max(outside, apron);

            // A wandering shoreline with coves cut in and headlands pushing out; straight
            // along a port apron, where it is a quay wall.
            float wander = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-6f, -30f, apron));
            float shore = (_s.Ragged * N(x, z, 0.012f)
                        + 3f * N(x, z, 0.05f)
                        + 22f * (N(x, z, 0.0045f, 120f) - 0.5f)) * wander;
            float d = outside - shore; // distance inland from the shoreline
            float cliff = Cliffness(x, z);
            float port = PortFlat(x, z);

            if (d < 0f)
            {
                // Off the shore: beaches shelve gently, cliffs and quay walls drop away steeply.
                float shelf = Mathf.Lerp(0.25f, 0.9f, Mathf.Max(cliff, 1f - wander));
                return Mathf.Max(-6f, d * shelf) - 0.3f;
            }

            // The first rise: a beach, or a cliff face up to a cliff top.
            float beach = Mathf.SmoothStep(0f, 1.2f, Mathf.Clamp01(d / 12f));
            float cliffTop = 9f + 11f * N(x, z, 0.03f, 40f);
            float face = cliffTop * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(d / 5f));
            float first = Mathf.Lerp(beach, face, cliff);

            // Inland: rolling hills, and ridges further back.
            float rise = Mathf.Clamp01((d - 10f) / 40f);
            float hills = rise * (4f + 20f * N(x, z, 0.009f, 50f)) + rise * 3f * N(x, z, 0.06f);
            float ridge = 1f - Mathf.Abs(N(x, z, 0.006f, 210f) * 2f - 1f);
            hills += rise * rise * 26f * ridge * ridge * ridge;
            float ground = first + hills * Mathf.Lerp(1f, 0.6f, cliff);

            // Small lumps everywhere, so no slope is perfectly smooth.
            ground += 0.5f * (N(x, z, 0.16f, 77f) - 0.5f) * Mathf.Clamp01(d / 6f);

            // Port ground round a harbour: flat at quay height (a quay wall at the apron's edge).
            float portGround = apron > -1f ? PortHeight : Mathf.Min(PortHeight, beach * 2f + 0.3f);
            ground = Mathf.Lerp(ground, portGround, port);
            return ground - 0.3f;
        }
    }

    private static void Tri(List<Vector3> verts, List<int>[] tris, CoastShape land, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 centre = (a + b + c) / 3f;
        float steep = 1f - Mathf.Abs(Vector3.Cross(b - a, c - a).normalized.y);
        // Jittered bands, so sand, grass and rock meet along ragged lines.
        float jitter = Mathf.PerlinNoise(centre.x * 0.21f, centre.z * 0.21f) - 0.5f;
        int sub;
        if (land.ApronDepth(centre.x, centre.z) > 1f && steep < 0.3f) sub = Paved; // the port yard
        else if (steep > 0.5f + jitter * 0.1f) sub = Rock;
        else if (centre.y < 0.9f + jitter * 0.6f) sub = land.Cliffness(centre.x, centre.z) > 0.5f ? Rock : Sand;
        else if (centre.y > 26f + jitter * 5f) sub = Rock;
        else if (land.PortFlat(centre.x, centre.z) > 0.6f) sub = DryGrass;
        else if (land.Forest(centre.x, centre.z) > 0.55f && centre.y > 2.6f) sub = Hill;
        else sub = land.DryPatch(centre.x, centre.z) > 0.6f + jitter * 0.1f ? DryGrass : Grass;

        int n = verts.Count;
        verts.Add(a); verts.Add(b); verts.Add(c);
        tris[sub].Add(n); tris[sub].Add(n + 1); tris[sub].Add(n + 2);
    }

    /// <summary>A pine: a thin trunk and three stacked narrow cones.</summary>
    private static void PineTree(List<Vector3> verts, List<int>[] tris, Vector3 foot, float scale, float yaw)
    {
        var rot = Quaternion.Euler(0f, yaw, 0f);
        Prism(verts, tris[Trunk], foot, rot, 0.25f * scale, 0.2f * scale, 1.4f * scale, 5);
        Cone(verts, tris[Pine], foot + Vector3.up * 1.0f * scale, rot, 1.5f * scale, 2.6f * scale, 6);
        Cone(verts, tris[Pine], foot + Vector3.up * 2.4f * scale, rot, 1.15f * scale, 2.3f * scale, 6);
        Cone(verts, tris[Pine], foot + Vector3.up * 3.7f * scale, rot, 0.75f * scale, 1.9f * scale, 6);
    }

    /// <summary>A round broadleaf tree: a trunk and a faceted crown.</summary>
    private static void RoundTree(List<Vector3> verts, List<int>[] tris, Vector3 foot, float scale, float yaw)
    {
        var rot = Quaternion.Euler(0f, yaw, 0f);
        Prism(verts, tris[Trunk], foot, rot, 0.35f * scale, 0.28f * scale, 1.8f * scale, 5);
        Blob(verts, tris[Leaves], foot + Vector3.up * 3.0f * scale, rot, 1.9f * scale, 1.6f * scale);
    }

    /// <summary>A faceted ball (two six-sided pyramids sharing a waist), for crowns.</summary>
    private static void Blob(List<Vector3> verts, List<int> tris, Vector3 centre, Quaternion rot, float radius, float height)
    {
        const int sides = 6;
        Vector3 top = centre + Vector3.up * height, bottom = centre - Vector3.up * height * 0.7f;
        for (int k = 0; k < sides; k++)
        {
            Vector3 p = centre + rot * Ring(k, sides, radius), q = centre + rot * Ring(k + 1, sides, radius);
            int n = verts.Count;
            verts.Add(p); verts.Add(top); verts.Add(q);
            verts.Add(q); verts.Add(bottom); verts.Add(p);
            for (int i = 0; i < 6; i++) tris.Add(n + i);
        }
    }

    /// <summary>A lumpy low-poly boulder, half sunk into the ground.</summary>
    private static void Boulder(List<Vector3> verts, List<int> tris, Vector3 centre, float size, System.Random rng)
    {
        const int sides = 5;
        float r() => 0.75f + (float)rng.NextDouble() * 0.5f;
        var ring = new Vector3[sides];
        float yaw = (float)rng.NextDouble() * 360f;
        for (int k = 0; k < sides; k++) ring[k] = centre + Quaternion.Euler(0f, yaw, 0f) * Ring(k, sides, size * r()) + Vector3.up * size * 0.3f * r();
        Vector3 top = centre + Vector3.up * size * 0.9f * r(), bottom = centre - Vector3.up * size * 0.4f;
        for (int k = 0; k < sides; k++)
        {
            Vector3 p = ring[k], q = ring[(k + 1) % sides];
            int n = verts.Count;
            verts.Add(p); verts.Add(top); verts.Add(q);
            verts.Add(q); verts.Add(bottom); verts.Add(p);
            for (int i = 0; i < 6; i++) tris.Add(n + i);
        }
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
