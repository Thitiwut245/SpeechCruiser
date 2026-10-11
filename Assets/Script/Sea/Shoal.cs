using UnityEngine;

/// <summary>
/// A sandbank: a mound of sand rising from the seabed to just under the surface, seen as
/// paler, greener water (the sea shader shows the bottom through the shallows). Sailing onto
/// it doesn't break the hull, it runs the ship aground (ShipHull): the ship stops dead and
/// only backing off (astern) frees it. Its trigger is tagged Hazard, so the radar and the
/// lookout warn about it ("Sandbank").
/// </summary>
public class Shoal : MonoBehaviour
{
    private const float TopDepth = 2.0f; // metres of water over the top, at mean sea level
    private static readonly Color Sand = new Color(0.70f, 0.64f, 0.47f);
    private static Material _sand;

    /// <summary>A sandbank about <paramref name="radius"/> across its half-width, centred on <paramref name="centre"/>.</summary>
    public static GameObject Build(Transform parent, Vector3 centre, float radius, float seabedDepth)
    {
        var go = new GameObject("Sandbank Shoal");
        go.transform.SetParent(parent, false);
        go.transform.position = new Vector3(centre.x, 0f, centre.z);
        go.AddComponent<Shoal>();
        try { go.tag = "Hazard"; } catch (UnityException) { } // so the radar and the lookout see it

        go.AddComponent<MeshFilter>().sharedMesh = Mound(radius, seabedDepth);
        var r = go.AddComponent<MeshRenderer>();
        if (_sand == null)
        {
            _sand = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Sandbank" };
            _sand.SetColor("_BaseColor", Sand);
            _sand.SetFloat("_Smoothness", 0.05f);
        }
        r.sharedMaterial = _sand;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // The part shallow enough to ground on: the flat top, out to about 70% of the radius.
        var trigger = go.AddComponent<CapsuleCollider>();
        trigger.isTrigger = true;
        trigger.direction = 1;
        trigger.radius = radius * 0.7f;
        trigger.height = 12f;
        trigger.center = Vector3.zero;
        return go;
    }

    /// <summary>A round, lumpy mound: a flat-ish top just under the water, sloping down to the seabed.</summary>
    private static Mesh Mound(float radius, float seabedDepth)
    {
        const int rings = 8, spokes = 48;
        float seed = Random.Range(0f, 100f);
        var vertices = new Vector3[1 + rings * spokes];
        vertices[0] = new Vector3(0f, -TopDepth + 0.15f, 0f);
        for (int ring = 1; ring <= rings; ring++)
        {
            float t = ring / (float)rings;
            for (int s = 0; s < spokes; s++)
            {
                float a = s * Mathf.PI * 2f / spokes;
                // A lumpy outline, so it reads as a natural bank and not a disc.
                float wobble = 0.75f + 0.45f * Mathf.PerlinNoise(seed + Mathf.Cos(a) * 0.9f, seed + Mathf.Sin(a) * 0.9f);
                float r = radius * t * wobble;
                // A gently domed top, then a long soft slope down to the bed.
                float fall = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.2f, 1f, t));
                float y = Mathf.Lerp(-TopDepth, -seabedDepth - 0.5f, fall) + Mathf.PerlinNoise(seed + a, ring * 0.7f) * 0.3f;
                vertices[1 + (ring - 1) * spokes + s] = new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
            }
        }

        var triangles = new System.Collections.Generic.List<int>();
        for (int s = 0; s < spokes; s++)
        {
            int n = (s + 1) % spokes;
            triangles.Add(0); triangles.Add(1 + n); triangles.Add(1 + s);
        }
        for (int ring = 1; ring < rings; ring++)
            for (int s = 0; s < spokes; s++)
            {
                int n = (s + 1) % spokes;
                int a = 1 + (ring - 1) * spokes, b = 1 + ring * spokes;
                triangles.Add(a + s); triangles.Add(a + n); triangles.Add(b + n);
                triangles.Add(a + s); triangles.Add(b + n); triangles.Add(b + s);
            }

        var mesh = new Mesh { name = "Sandbank", vertices = vertices, triangles = triangles.ToArray() };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
