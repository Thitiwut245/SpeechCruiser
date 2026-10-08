using UnityEngine;

/// <summary>
/// The sea: a fine grid of water that follows the camera (so the waves have vertices to move,
/// wherever the camera looks), drawn by Resources/Sea/Sea.shader. Each frame it hands the
/// shader the wave table and clock from <see cref="SeaWaves"/>, so floating things and the
/// drawn surface agree. The grid snaps to its own spacing as it follows, and the shader works
/// in world space, so the waves never swim.
/// The shader also draws foam where the water meets anything (rocks, hulls, the shore), on
/// wave crests, and along ship wakes (<see cref="ShipWake"/>).
/// </summary>
public class SeaSurface : MonoBehaviour
{
    [Tooltip("Width of the square of water around the camera's view, in metres.")]
    [SerializeField] private float Size = 400f;

    [Tooltip("Distance between the grid's vertices, in metres. Must keep (Size/Spacing+1)^2 under 65,535.")]
    [SerializeField] private float Spacing = 1.6f;

    private static readonly int WaveAId = Shader.PropertyToID("_SeaWaveA");
    private static readonly int WaveBId = Shader.PropertyToID("_SeaWaveB");
    private static readonly int TimeId = Shader.PropertyToID("_SeaTime");
    private static readonly int RoughId = Shader.PropertyToID("_SeaRough");
    private readonly Vector4[] _a = new Vector4[SeaWaves.Count];
    private readonly Vector4[] _b = new Vector4[SeaWaves.Count];
    private Camera _camera;

    /// <summary>Makes the sea, with a roughness to suit the weather.</summary>
    public static SeaSurface Create(Transform parent, float roughness)
    {
        var shader = Resources.Load<Shader>("Sea/Sea");
        if (shader == null)
        {
            Debug.LogError("❌ [SEA]: Resources/Sea/Sea.shader is missing; there will be no water.");
            return null;
        }
        SeaWaves.Roughness = roughness;
        var go = new GameObject("Water");
        go.transform.SetParent(parent, false);
        var sea = go.AddComponent<SeaSurface>();
        go.AddComponent<MeshFilter>().sharedMesh = sea.BuildGrid();
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = new Material(shader) { name = "Sea" };
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ShipWake.Clear();
        sea.PushWaves();
        return sea;
    }

    private Mesh BuildGrid()
    {
        int n = Mathf.Min(Mathf.CeilToInt(Size / Spacing) + 1, 255);
        float half = (n - 1) * Spacing * 0.5f;
        var vertices = new Vector3[n * n];
        for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
                vertices[z * n + x] = new Vector3(x * Spacing - half, 0f, z * Spacing - half);

        var triangles = new int[(n - 1) * (n - 1) * 6];
        int t = 0;
        for (int z = 0; z < n - 1; z++)
            for (int x = 0; x < n - 1; x++)
            {
                int i = z * n + x;
                triangles[t++] = i; triangles[t++] = i + n; triangles[t++] = i + 1;
                triangles[t++] = i + 1; triangles[t++] = i + n; triangles[t++] = i + n + 1;
            }

        var mesh = new Mesh { name = "SeaGrid", vertices = vertices, triangles = triangles };
        // The shader lifts and shifts the vertices: keep the bounds generous so it is never culled.
        mesh.bounds = new Bounds(Vector3.zero, new Vector3(n * Spacing + 20f, 20f, n * Spacing + 20f));
        return mesh;
    }

    private void PushWaves()
    {
        SeaWaves.Fill(_a, _b);
        Shader.SetGlobalVectorArray(WaveAId, _a);
        Shader.SetGlobalVectorArray(WaveBId, _b);
        Shader.SetGlobalFloat(TimeId, SeaWaves.Now);
        Shader.SetGlobalFloat(RoughId, SeaWaves.Roughness);
    }

    private void LateUpdate()
    {
        PushWaves();
        ShipWake.Push();
        FollowCamera();
    }

    /// <summary>Centres the grid where the camera looks at sea level, snapped to the grid spacing.</summary>
    private void FollowCamera()
    {
        if (_camera == null) _camera = Camera.main;
        if (_camera == null) return;
        var ray = new Ray(_camera.transform.position, _camera.transform.forward);
        Vector3 focus = _camera.transform.position;
        if (Mathf.Abs(ray.direction.y) > 0.01f)
        {
            float d = -ray.origin.y / ray.direction.y;
            if (d > 0f) focus = ray.GetPoint(d);
        }
        transform.position = new Vector3(Mathf.Round(focus.x / Spacing) * Spacing, 0f, Mathf.Round(focus.z / Spacing) * Spacing);
    }
}
