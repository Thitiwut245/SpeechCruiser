using UnityEngine;

/// <summary>
/// The sea floor: a sandy bottom with gentle ripples and dips, under the whole level. The sea
/// shader colours the water by how far down this is (and sees it through the shallows), so it
/// is not only scenery. No collider: the ship is moved by code, never by physics.
/// </summary>
public static class Seabed
{
    private static readonly Color Sand = new Color(0.78f, 0.72f, 0.56f);

    /// <summary>A bottom <paramref name="depth"/> metres down (on average), covering sizeX × sizeZ around centre.</summary>
    public static GameObject Build(Transform parent, Vector3 centre, float sizeX, float sizeZ, float depth)
    {
        const float spacing = 6f;
        int nx = Mathf.Clamp(Mathf.CeilToInt(sizeX / spacing) + 1, 2, 250);
        int nz = Mathf.Clamp(Mathf.CeilToInt(sizeZ / spacing) + 1, 2, 250);
        float stepX = sizeX / (nx - 1), stepZ = sizeZ / (nz - 1);
        float seed = Random.Range(0f, 100f);

        var vertices = new Vector3[nx * nz];
        for (int z = 0; z < nz; z++)
            for (int x = 0; x < nx; x++)
            {
                float wx = centre.x - sizeX * 0.5f + x * stepX;
                float wz = centre.z - sizeZ * 0.5f + z * stepZ;
                // Broad dips and rises, with sand ripples on top.
                float h = (Mathf.PerlinNoise(seed + wx * 0.012f, seed + wz * 0.012f) - 0.5f) * 3.2f
                        + (Mathf.PerlinNoise(seed + wx * 0.06f, wz * 0.06f) - 0.5f) * 0.8f;
                vertices[z * nx + x] = new Vector3(wx, -depth + h, wz);
            }

        var triangles = new int[(nx - 1) * (nz - 1) * 6];
        int t = 0;
        for (int z = 0; z < nz - 1; z++)
            for (int x = 0; x < nx - 1; x++)
            {
                int i = z * nx + x;
                triangles[t++] = i; triangles[t++] = i + nx; triangles[t++] = i + 1;
                triangles[t++] = i + 1; triangles[t++] = i + nx; triangles[t++] = i + nx + 1;
            }

        var mesh = new Mesh { name = "Seabed", vertices = vertices, triangles = triangles };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        var go = new GameObject("SeaFloor");
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        var mat = new Material(shader) { name = "Sand" };
        mat.SetColor("_BaseColor", Sand);
        mat.SetFloat("_Smoothness", 0.1f);
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go;
    }
}
