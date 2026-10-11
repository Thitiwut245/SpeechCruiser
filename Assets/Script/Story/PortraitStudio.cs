using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Low-poly 3D busts for the dialogue scenes, built in code from a CharacterProfile (skin,
/// hair, hat, beard, glasses, clothes) and rendered once into two small textures each: mouth
/// closed and mouth open, so a speaking face can flap. The bust is built far below the
/// world, rendered by its own camera in the same frame, and destroyed straight away, so no
/// game camera ever sees it. Textures are kept for the whole session.
/// </summary>
public static class PortraitStudio
{
    public const int Size = 256;

    private static readonly Vector3 StudioAt = new Vector3(0f, -4000f, 0f);
    private static bool _warmed;
    private static readonly Dictionary<CharacterId, RenderTexture[]> _cache = new Dictionary<CharacterId, RenderTexture[]>();

    /// <summary>The portrait with the mouth closed (open = false) or open (open = true). Null if it can't be drawn.</summary>
    public static Texture Get(CharacterId id, bool open = false)
    {
        if (!_cache.TryGetValue(id, out var pair) || pair[0] == null || !pair[0].IsCreated())
        {
            pair = Render(StoryCast.Get(id));
            _cache[id] = pair;
        }
        return pair == null ? null : pair[open ? 1 : 0];
    }

    // ------------------------------------------------------------ rendering

    private static RenderTexture[] Render(CharacterProfile p)
    {
        if (LowPolyMesh.BaseMaterial == null) return null;

        var root = new GameObject("Portrait " + p.Name);
        root.transform.position = StudioAt;
        var closedMouth = new GameObject("MouthClosed");
        var openMouth = new GameObject("MouthOpen");
        closedMouth.transform.SetParent(root.transform, false);
        openMouth.transform.SetParent(root.transform, false);

        Attach(root, BuildBust(p));
        Attach(closedMouth, BuildMouth(p, false));
        Attach(openMouth, BuildMouth(p, true));

        var camGo = new GameObject("PortraitCamera");
        var cam = camGo.AddComponent<Camera>();
        cam.enabled = false; // rendered by hand below, never by the pipeline's loop
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = p.Backdrop;
        cam.fieldOfView = 24f;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 12f;
        cam.transform.position = StudioAt + new Vector3(0.55f, 0.05f, -4.6f);
        cam.transform.LookAt(StudioAt + new Vector3(0f, -0.18f, 0f));

        // Studio lighting only, so a face looks the same in every scene: the scene's lights are
        // switched off for the moment of the render, and the ambient light is a fixed soft grey.
        var sceneLights = new List<Light>();
        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (l.enabled) { l.enabled = false; sceneLights.Add(l); }
        var lightGo = new GameObject("PortraitKey");
        var key = lightGo.AddComponent<Light>();
        key.type = LightType.Directional;
        key.intensity = 1.25f;
        key.color = new Color(1f, 0.96f, 0.9f);
        key.shadows = LightShadows.None;
        lightGo.transform.rotation = Quaternion.Euler(25f, 35f, 0f);

        bool fog = RenderSettings.fog;
        RenderSettings.fog = false;
        var probe = RenderSettings.ambientProbe;
        var studio = new UnityEngine.Rendering.SphericalHarmonicsL2();
        studio.AddAmbientLight(new Color(0.42f, 0.44f, 0.48f));
        RenderSettings.ambientProbe = studio;

        var result = new RenderTexture[2];
        // The very first render of a session can come out unlit: draw one and throw it away.
        if (!_warmed)
        {
            _warmed = true;
            var warm = RenderTexture.GetTemporary(Size, Size, 24);
            cam.targetTexture = warm;
            cam.Render();
            cam.targetTexture = null;
            RenderTexture.ReleaseTemporary(warm);
        }
        for (int i = 0; i < 2; i++)
        {
            closedMouth.SetActive(i == 0);
            openMouth.SetActive(i == 1);
            var rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4, name = p.Name + (i == 0 ? " closed" : " open") };
            rt.Create();
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = null;
            result[i] = rt;
        }

        RenderSettings.fog = fog;
        RenderSettings.ambientProbe = probe;
        foreach (var l in sceneLights) if (l != null) l.enabled = true;
        Object.DestroyImmediate(lightGo);
        Object.DestroyImmediate(camGo);
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true)) Object.DestroyImmediate(mf.sharedMesh);
        Object.DestroyImmediate(root);
        return result;
    }

    private static void Attach(GameObject go, LowPolyMesh build) => build.AttachTo(go, false);

    // ------------------------------------------------------------ the bust

    // Head: an ellipsoid centred here; the face looks down -z (toward the camera).
    private static readonly Vector3 HeadC = new Vector3(0f, 0.05f, 0f);
    private static readonly Vector3 HeadR = new Vector3(0.45f, 0.56f, 0.47f);

    /// <summary>A point on the face surface at (x, y), nudged out by <paramref name="lift"/>.</summary>
    private static Vector3 OnFace(float x, float y, float lift = 0.012f)
    {
        float u = x / HeadR.x, v = (y - HeadC.y) / HeadR.y;
        float z = -HeadR.z * Mathf.Sqrt(Mathf.Max(0.02f, 1f - u * u - v * v));
        return new Vector3(x, y, z - lift);
    }

    private static LowPolyMesh BuildBust(CharacterProfile p)
    {
        var b = new LowPolyMesh();
        Color skin = p.Skin, shade = p.Skin * 0.86f, dark = new Color(0.12f, 0.09f, 0.08f);
        shade.a = 1f;

        // Shoulders and chest, a collar, the neck.
        b.Frustum(new Vector3(0f, -1.75f, 0.05f), 1.3f, 0.82f, 1.05f, 7, 1f, 0.62f, p.Clothes, true, false);
        b.Frustum(new Vector3(0f, -0.74f, 0.04f), 0.46f, 0.36f, 0.14f, 7, 1f, 0.85f, p.Trim, true, false);
        b.Frustum(new Vector3(0f, -0.72f, 0.03f), 0.2f, 0.19f, 0.4f, 6, 1f, 1f, skin, false, false);

        // Head, ears, nose, eyes and eyebrows.
        b.Ellipsoid(HeadC, HeadR, 10, 7, skin, 0f, 1f); // 10 sides: one facet faces straight ahead
        b.Box(new Vector3(-0.46f, 0.02f, 0.02f), new Vector3(0.08f, 0.17f, 0.12f), shade);
        b.Box(new Vector3(0.46f, 0.02f, 0.02f), new Vector3(0.08f, 0.17f, 0.12f), shade);
        Vector3 noseBase = OnFace(0f, -0.04f, 0f);
        b.Pyramid(noseBase + new Vector3(0f, 0.02f, 0.02f), noseBase + new Vector3(0f, -0.06f, -0.13f), 0.07f, shade);
        foreach (float sx in new[] { -0.17f, 0.17f })
        {
            b.Box(OnFace(sx, 0.09f, 0.01f), new Vector3(0.1f, 0.07f, 0.03f), Color.white);
            b.Box(OnFace(sx, 0.085f, 0.026f), new Vector3(0.05f, 0.06f, 0.02f), dark);
            b.Box(OnFace(sx, 0.2f, 0.02f), new Vector3(0.14f, 0.03f, 0.03f), p.HairColour * 0.8f + Color.black * 0.2f);
        }
        // Rosy cheeks.
        b.Box(OnFace(-0.24f, -0.08f, 0.004f), new Vector3(0.09f, 0.05f, 0.02f), Color.Lerp(skin, new Color(0.9f, 0.4f, 0.4f), 0.35f));
        b.Box(OnFace(0.24f, -0.08f, 0.004f), new Vector3(0.09f, 0.05f, 0.02f), Color.Lerp(skin, new Color(0.9f, 0.4f, 0.4f), 0.35f));

        if (p.Beard)
        {
            b.Ellipsoid(new Vector3(0f, -0.2f, -0.06f), new Vector3(0.44f, 0.4f, 0.45f), 9, 7, p.HairColour, 0.5f, 1f, frontOnly: true);
            b.Box(OnFace(0f, -0.16f, 0.07f), new Vector3(0.26f, 0.06f, 0.05f), p.HairColour); // moustache
        }
        if (p.Glasses)
        {
            Color frame = new Color(0.1f, 0.1f, 0.12f);
            foreach (float sx in new[] { -0.17f, 0.17f })
            {
                Vector3 c = OnFace(sx, 0.09f, 0.05f);
                b.Box(c + new Vector3(0f, 0.075f, 0f), new Vector3(0.2f, 0.025f, 0.02f), frame);
                b.Box(c + new Vector3(0f, -0.075f, 0f), new Vector3(0.2f, 0.025f, 0.02f), frame);
                b.Box(c + new Vector3(-0.1f, 0f, 0f), new Vector3(0.025f, 0.17f, 0.02f), frame);
                b.Box(c + new Vector3(0.1f, 0f, 0f), new Vector3(0.025f, 0.17f, 0.02f), frame);
            }
            b.Box(OnFace(0f, 0.11f, 0.07f), new Vector3(0.1f, 0.025f, 0.02f), frame);
        }

        Hair(b, p);
        Hat(b, p);
        return b;
    }

    private static void Hair(LowPolyMesh b, CharacterProfile p)
    {
        Color h = p.HairColour;
        bool capped = p.Hat == CharacterProfile.HatStyle.Beanie || p.Hat == CharacterProfile.HatStyle.SouWester || p.Hat == CharacterProfile.HatStyle.Scarf;
        switch (p.Hair)
        {
            case CharacterProfile.HairStyle.None:
                return;
            case CharacterProfile.HairStyle.Curly:
                // A mop of little lumps over the top and back of the head.
                var rng = new System.Random((int)p.Id * 7919 + 13);
                for (int i = 0; i < 26; i++)
                {
                    float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                    float up = 0.15f + (float)rng.NextDouble() * 0.85f;
                    var dir = new Vector3(Mathf.Cos(a) * Mathf.Sqrt(1f - up * up), up, Mathf.Sin(a) * Mathf.Sqrt(1f - up * up));
                    if (dir.z < -0.35f && dir.y < 0.6f) continue; // keep the face clear
                    var at = HeadC + Vector3.Scale(dir, HeadR + Vector3.one * 0.04f) + Vector3.up * 0.05f;
                    float r = 0.14f + (float)rng.NextDouble() * 0.06f;
                    b.Ellipsoid(at, Vector3.one * r, 6, 4, h, 0f, 1f);
                }
                return;
        }

        // A cap of hair over the top of the head; with a hat it only shows below the brim.
        if (!capped) b.Ellipsoid(new Vector3(0f, 0.2f, 0.05f), new Vector3(0.5f, 0.44f, 0.53f), 9, 6, h, 0f, 0.5f);
        b.Box(new Vector3(0f, -0.02f, 0.3f), new Vector3(0.8f, 0.5f, 0.3f), h); // the back of the head
        b.Box(new Vector3(-0.44f, 0.12f, 0.1f), new Vector3(0.1f, 0.3f, 0.36f), h); // sideburns
        b.Box(new Vector3(0.44f, 0.12f, 0.1f), new Vector3(0.1f, 0.3f, 0.36f), h);

        if (p.Hair == CharacterProfile.HairStyle.Bun)
            b.Ellipsoid(new Vector3(0f, 0.58f, 0.32f), new Vector3(0.22f, 0.2f, 0.22f), 7, 5, h, 0f, 1f);
        if (p.Hair == CharacterProfile.HairStyle.Long)
        {
            b.Box(new Vector3(0f, -0.42f, 0.32f), new Vector3(0.86f, 0.75f, 0.24f), h);
            b.Box(new Vector3(-0.47f, -0.25f, 0.05f), new Vector3(0.12f, 0.75f, 0.4f), h);
            b.Box(new Vector3(0.47f, -0.25f, 0.05f), new Vector3(0.12f, 0.75f, 0.4f), h);
        }
    }

    private static void Hat(LowPolyMesh b, CharacterProfile p)
    {
        Color c = p.HatColour;
        switch (p.Hat)
        {
            case CharacterProfile.HatStyle.Beanie:
                b.Ellipsoid(new Vector3(0f, 0.24f, 0.04f), new Vector3(0.53f, 0.5f, 0.55f), 9, 6, c, 0f, 0.5f);
                b.Frustum(new Vector3(0f, 0.16f, 0.04f), 0.56f, 0.55f, 0.14f, 9, 0.98f, 1.02f, c * 0.85f + Color.black * 0.15f, false, false);
                b.Ellipsoid(new Vector3(0f, 0.8f, 0.08f), Vector3.one * 0.1f, 6, 4, p.Trim, 0f, 1f);
                break;
            case CharacterProfile.HatStyle.CaptainCap:
                b.Frustum(new Vector3(0f, 0.36f, 0.04f), 0.5f, 0.56f, 0.3f, 10, 1f, 1.05f, c, true, true);
                b.Frustum(new Vector3(0f, 0.36f, 0.04f), 0.505f, 0.505f, 0.08f, 10, 1f, 1.05f, new Color(0.85f, 0.7f, 0.3f), false, false);
                b.Box(new Vector3(0f, 0.38f, -0.56f), new Vector3(0.6f, 0.04f, 0.28f), new Color(0.06f, 0.06f, 0.08f), Quaternion.Euler(-12f, 0f, 0f));
                break;
            case CharacterProfile.HatStyle.SouWester:
                b.Ellipsoid(new Vector3(0f, 0.24f, 0.06f), new Vector3(0.54f, 0.52f, 0.56f), 9, 6, c, 0f, 0.5f);
                b.Frustum(new Vector3(0f, 0.12f, 0.08f), 0.86f, 0.56f, 0.14f, 10, 1f, 1.05f, c * 0.9f + Color.black * 0.1f, true, false);
                break;
            case CharacterProfile.HatStyle.Scarf:
                b.Ellipsoid(new Vector3(0f, 0.22f, 0.05f), new Vector3(0.52f, 0.47f, 0.55f), 9, 6, c, 0f, 0.5f);
                b.Box(new Vector3(0f, 0.18f, 0.58f), new Vector3(0.16f, 0.16f, 0.12f), c * 0.85f + Color.black * 0.15f);
                b.Box(new Vector3(0.06f, 0.02f, 0.62f), new Vector3(0.1f, 0.24f, 0.05f), c, Quaternion.Euler(0f, 0f, 18f));
                break;
        }
    }

    private static LowPolyMesh BuildMouth(CharacterProfile p, bool open)
    {
        var b = new LowPolyMesh();
        float lift = p.Beard ? 0.075f : 0.012f;
        if (open)
        {
            b.Box(OnFace(0f, -0.27f, lift), new Vector3(0.16f, 0.1f, 0.03f), new Color(0.35f, 0.08f, 0.1f));
            b.Box(OnFace(0f, -0.235f, lift + 0.012f), new Vector3(0.13f, 0.025f, 0.02f), Color.white);
        }
        else
        {
            b.Box(OnFace(0f, -0.26f, lift), new Vector3(0.18f, 0.035f, 0.03f), new Color(0.45f, 0.18f, 0.18f));
        }
        return b;
    }
}
