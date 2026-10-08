using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A colourful harbour town on the land round each port: pastel houses with tiled or slate
/// roofs, doors and windows facing the harbour, packed close near the port and thinning out
/// into the countryside; a church with a spire; and a red-and-white lighthouse on the shore
/// near the harbour mouth, its lantern turning. Houses go only where the ground is high and
/// gentle enough (not on the beach, cliffs or the port yard). One mesh for all of it.
/// </summary>
public static class TownBuilder
{
    private const int Walls = 7; // wall colours come first
    private static readonly Color[] Colours =
    {
        new Color(0.95f, 0.86f, 0.66f), // cream
        new Color(0.94f, 0.62f, 0.52f), // coral
        new Color(0.62f, 0.78f, 0.90f), // sky
        new Color(0.96f, 0.86f, 0.42f), // yellow
        new Color(0.70f, 0.86f, 0.70f), // mint
        new Color(0.97f, 0.96f, 0.93f), // white
        new Color(0.86f, 0.64f, 0.80f), // pink
        new Color(0.70f, 0.30f, 0.20f), // 7 terracotta roof
        new Color(0.28f, 0.31f, 0.36f), // 8 slate roof
        new Color(0.16f, 0.20f, 0.26f), // 9 windows and doors
        new Color(0.80f, 0.16f, 0.14f), // 10 lighthouse red
        new Color(0.62f, 0.60f, 0.56f), // 11 church stone
    };
    private const int Terracotta = 7, Slate = 8, Window = 9, Red = 10, Stone = 11, White = 5;
    private static Material[] _materials;

    public struct Building
    {
        public Vector2 Position;
        public float Radius;
    }

    /// <summary>
    /// Builds the towns round <paramref name="harbours"/> on <paramref name="land"/>, keeping
    /// clear of <paramref name="aprons"/> (port ground). Returns where buildings stand, so
    /// trees can keep away from them.
    /// </summary>
    public static List<Building> Build(Transform parent, CoastBuilder.CoastShape land, Vector2[] harbours, Rect[] aprons, int seed)
    {
        var placed = new List<Building>();
        if (harbours == null || harbours.Length == 0) return placed;
        var rng = new System.Random(seed);
        var kit = new MeshKit(Colours.Length);
        var root = new GameObject("Towns").transform;
        root.SetParent(parent, false);

        foreach (var harbour in harbours)
        {
            bool church = false;
            const float spacing = 9f, reach = 230f;
            for (float x = harbour.x - reach; x <= harbour.x + reach; x += spacing)
                for (float z = harbour.y - reach; z <= harbour.y + reach; z += spacing)
                {
                    float px = x + (float)(rng.NextDouble() - 0.5) * 3f, pz = z + (float)(rng.NextDouble() - 0.5) * 3f;
                    float dist = Vector2.Distance(new Vector2(px, pz), harbour);
                    if (dist < 50f) continue;
                    if (!Buildable(land, px, pz, aprons, out float ground)) continue;

                    // Dense by the port, thinning out; in clusters, so there are streets and squares.
                    float near = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(70f, reach, dist));
                    float cluster = Mathf.PerlinNoise(px * 0.025f + seed % 97, pz * 0.025f) > 0.38f ? 1f : 0.25f;
                    if (rng.NextDouble() > 0.85 * near * cluster) continue;
                    if (Crowded(placed, px, pz, 7.5f)) continue;

                    float yaw = Mathf.Atan2(harbour.x - px, harbour.y - pz) * Mathf.Rad2Deg + (float)(rng.NextDouble() - 0.5) * 16f;
                    var at = new Vector3(px, ground, pz);
                    if (!church && dist > 80f && dist < 140f)
                    {
                        Church(kit, at, yaw);
                        placed.Add(new Building { Position = new Vector2(px, pz), Radius = 9f });
                        church = true;
                        continue;
                    }
                    House(kit, at, yaw, rng, near);
                    placed.Add(new Building { Position = new Vector2(px, pz), Radius = 5f });
                }

            Vector3? point = LighthouseSpot(land, harbour, aprons);
            if (point.HasValue)
            {
                Lighthouse(kit, root, point.Value);
                placed.Add(new Building { Position = new Vector2(point.Value.x, point.Value.z), Radius = 4f });
            }
        }

        if (_materials == null) _materials = MeshKit.Paints("Town", Colours, Window);
        kit.Finish(root, "Houses", _materials);
        Debug.Log($"🏘️ [TOWN]: {placed.Count} buildings round {harbours.Length} harbours.");
        return placed;
    }

    /// <summary>Solid ground for a house: above the beach, gentle, not port ground.</summary>
    private static bool Buildable(CoastBuilder.CoastShape land, float x, float z, Rect[] aprons, out float ground)
    {
        ground = land.Height(x, z);
        if (ground < 3.2f || ground > 24f) return false;
        if (land.ApronDepth(x, z) > -10f) return false;
        float slope = Mathf.Max(Mathf.Abs(land.Height(x + 3f, z) - land.Height(x - 3f, z)),
                                Mathf.Abs(land.Height(x, z + 3f) - land.Height(x, z - 3f)));
        if (slope > 2.4f) return false;
        ground = Mathf.Min(ground, land.Height(x + 2.5f, z), land.Height(x - 2.5f, z), land.Height(x, z + 2.5f), land.Height(x, z - 2.5f));
        return true;
    }

    private static bool Crowded(List<Building> placed, float x, float z, float gap)
    {
        foreach (var b in placed)
            if ((b.Position - new Vector2(x, z)).sqrMagnitude < gap * gap) return true;
        return false;
    }

    private static void House(MeshKit kit, Vector3 foot, float yaw, System.Random rng, float near)
    {
        float w = 4.5f + (float)rng.NextDouble() * 2.5f, d = 4f + (float)rng.NextDouble() * 2f;
        int floors = near > 0.6f && rng.NextDouble() < 0.5 ? 2 + rng.Next(2) : 1 + rng.Next(2);
        float h = 2.8f * floors + 0.6f;
        int wall = rng.Next(Walls);
        Quaternion r = Quaternion.Euler(0f, yaw, 0f);
        kit.Box(wall, foot + Vector3.up * (h * 0.5f - 0.5f), new Vector3(w, h + 1f, d), yaw); // 0.5 m sunk into the slope
        kit.Gable(rng.NextDouble() < 0.7 ? Terracotta : Slate, foot + Vector3.up * (h - 0.5f), new Vector3(w + 0.5f, 1.6f + w * 0.12f, d + 0.4f), yaw + 90f);
        // Door and windows on the front (towards the harbour).
        Vector3 front = r * Vector3.forward * (d * 0.5f + 0.03f);
        kit.Box(Window, foot + front + Vector3.up * 0.6f, new Vector3(0.9f, 1.8f, 0.06f), yaw);
        for (int f = 0; f < floors; f++)
            foreach (float side in new[] { -1f, 1f })
                kit.Box(Window, foot + front + r * new Vector3(side * w * 0.3f, 0f, 0f) + Vector3.up * (1.3f + f * 2.8f),
                        new Vector3(0.8f, 0.9f, 0.06f), yaw);
    }

    private static void Church(MeshKit kit, Vector3 foot, float yaw)
    {
        Quaternion r = Quaternion.Euler(0f, yaw, 0f);
        kit.Box(White, foot + Vector3.up * 3f, new Vector3(7f, 7f, 13f), yaw);
        kit.Gable(Slate, foot + Vector3.up * 6.5f, new Vector3(7.6f, 3.4f, 13.6f), yaw);
        Vector3 tower = foot + r * new Vector3(0f, 0f, 7.5f);
        kit.Box(Stone, tower + Vector3.up * 6f, new Vector3(3.6f, 13f, 3.6f), yaw);
        kit.Cone(Slate, tower + Vector3.up * 12.5f, 2.6f, 6f, 4);
        kit.Box(Window, tower + r * new Vector3(0f, 0f, 1.83f) + Vector3.up * 10f, new Vector3(1.2f, 1.6f, 0.06f), yaw);
    }

    /// <summary>A shore point near the harbour on the side towards open water, for the lighthouse.</summary>
    private static Vector3? LighthouseSpot(CoastBuilder.CoastShape land, Vector2 harbour, Rect[] aprons)
    {
        Vector3? best = null;
        float bestScore = float.MaxValue;
        for (float a = 0f; a < 360f; a += 6f)
            for (float r = 45f; r <= 150f; r += 7f)
            {
                float x = harbour.x + Mathf.Cos(a * Mathf.Deg2Rad) * r, z = harbour.y + Mathf.Sin(a * Mathf.Deg2Rad) * r;
                float h = land.Height(x, z);
                if (h < 1.5f || h > 6f || land.ApronDepth(x, z) > -12f) continue;
                // Right by the water: the sea must be close in some direction.
                bool shore = land.Height(x - 9f, z) < -0.5f || land.Height(x + 9f, z) < -0.5f ||
                             land.Height(x, z - 9f) < -0.5f || land.Height(x, z + 9f) < -0.5f;
                if (!shore) continue;
                float score = Mathf.Abs(r - 70f) + (x > harbour.x ? 40f : 0f); // prefer the channel (west) side
                if (score < bestScore) { bestScore = score; best = new Vector3(x, h - 0.3f, z); }
            }
        return best;
    }

    private static void Lighthouse(MeshKit kit, Transform root, Vector3 foot)
    {
        kit.Prism(Stone, foot, 2.6f, 1.2f, 8);
        for (int band = 0; band < 4; band++)
            kit.Prism(band % 2 == 0 ? White : Red, foot + Vector3.up * (1.2f + band * 3.2f), 1.9f - band * 0.12f, 3.2f, 8, 1.9f - (band + 1) * 0.12f);
        Vector3 gallery = foot + Vector3.up * 14f;
        kit.Prism(Window, gallery, 2.0f, 0.3f, 8);
        kit.Cone(Red, gallery + Vector3.up * 2.1f, 1.6f, 1.6f, 8);

        // The lantern: a glowing glass room with a beam that turns.
        var lantern = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Object.Destroy(lantern.GetComponent<Collider>());
        lantern.name = "Lantern";
        lantern.transform.SetParent(root, false);
        lantern.transform.position = gallery + Vector3.up * 1.15f;
        lantern.transform.localScale = new Vector3(2.2f, 0.9f, 2.2f);
        var glow = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Lantern" };
        glow.SetColor("_BaseColor", new Color(1f, 0.93f, 0.6f));
        lantern.GetComponent<Renderer>().sharedMaterial = glow;
        lantern.AddComponent<LighthouseBeam>();
    }
}

/// <summary>A lighthouse's turning beam: a long pale wedge of light sweeping round the lantern.</summary>
public class LighthouseBeam : MonoBehaviour
{
    private Transform _beam;

    private void Start()
    {
        _beam = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
        Destroy(_beam.GetComponent<Collider>());
        _beam.name = "Beam";
        _beam.SetParent(transform, false);
        _beam.localScale = new Vector3(0.25f, 0.35f, 22f);
        _beam.localPosition = new Vector3(0f, 0f, 11f);
        var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Beam" };
        mat.SetColor("_BaseColor", new Color(1f, 0.95f, 0.7f));
        _beam.GetComponent<Renderer>().sharedMaterial = mat;
        _beam.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    private void Update()
    {
        transform.Rotate(0f, 40f * Time.deltaTime, 0f, Space.World);
    }
}
