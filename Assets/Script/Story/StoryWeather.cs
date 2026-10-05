using UnityEngine;
using UnityEngine.UIElements;

/// <summary>How the sea looks on a story mission.</summary>
public enum Weather
{
    Clear,
    /// <summary>Thick fog: only the water close around the ship can be seen.</summary>
    Fog,
    /// <summary>Night and fog: darker still; glowing things show through.</summary>
    NightFog,
    /// <summary>A storm: dark sky, driving rain, lightning, and wind that pushes the ship.</summary>
    Storm,
}

/// <summary>
/// The story's weather, drawn over the world but under the HUD: a soft vignette that hides the
/// sea away from the ship (fog, night), rain streaks and lightning flashes (storm). It also dims
/// the scene's sun and, in a storm, gives the wind a direction for the director to push the
/// ship with. The radar and Finn still see everything: in bad weather, ask.
/// </summary>
public class StoryWeather : MonoBehaviour
{
    private const int SortingOrder = -10; // under the HUD and the shipyard (both 0)

    public Weather Kind { get; private set; }

    /// <summary>Storm wind in world units per second (zero otherwise). Gusts come and go.</summary>
    public Vector3 Wind => Kind == Weather.Storm ? _windDir * (0.3f + 0.45f * Mathf.PerlinNoise(Time.time * 0.15f, 3.1f)) : Vector3.zero;

    private VisualElement _veil;
    private VisualElement _flash;
    private VisualElement _rain;
    private Light[] _suns;
    private float[] _sunIntensity;
    private float _nextLightning = 6f;
    private float _flashUntil;
    private Vector3 _windDir;
    private AudioSource _rainLoop;
    private Vector2[] _drops;

    public static StoryWeather Create(Weather kind)
    {
        var go = new GameObject("StoryWeather");
        go.SetActive(false);
        var host = SceneLoader.Instance != null ? SceneLoader.Instance.GetComponent<UIDocument>() : null;
        var doc = go.AddComponent<UIDocument>();
        if (host != null) doc.panelSettings = host.panelSettings;
        doc.sortingOrder = SortingOrder;
        var w = go.AddComponent<StoryWeather>();
        w.Kind = kind;
        go.SetActive(true);
        return w;
    }

    private void OnEnable()
    {
        var root = GetComponent<UIDocument>().rootVisualElement;
        root.pickingMode = PickingMode.Ignore;
        root.style.position = Position.Absolute;
        root.style.left = 0; root.style.right = 0; root.style.top = 0; root.style.bottom = 0;

        _veil = Layer(root);
        _rain = Layer(root);
        _flash = Layer(root);
        _flash.style.backgroundColor = new Color(0.9f, 0.95f, 1f);
        _flash.style.opacity = 0f;

        switch (Kind)
        {
            case Weather.Fog:
                _veil.style.backgroundImage = Vignette(new Color(0.78f, 0.82f, 0.85f), 0.24f, 0.62f, 0.97f);
                break;
            case Weather.NightFog:
                _veil.style.backgroundImage = Vignette(new Color(0.05f, 0.08f, 0.16f), 0.24f, 0.6f, 0.97f);
                break;
            case Weather.Storm:
                _veil.style.backgroundImage = Vignette(new Color(0.12f, 0.14f, 0.18f), 0.3f, 0.8f, 0.85f);
                _rain.generateVisualContent += DrawRain;
                _drops = new Vector2[160];
                for (int i = 0; i < _drops.Length; i++) _drops[i] = new Vector2(Random.value, Random.value);
                float a = Random.Range(0f, Mathf.PI * 2f);
                _windDir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                break;
        }
    }

    private static VisualElement Layer(VisualElement root)
    {
        var e = new VisualElement { pickingMode = PickingMode.Ignore };
        e.style.position = Position.Absolute;
        e.style.left = 0; e.style.right = 0; e.style.top = 0; e.style.bottom = 0;
        root.Add(e);
        return e;
    }

    /// <summary>A radial veil: clear in the middle, <paramref name="edge"/> opaque at the corners.</summary>
    private static StyleBackground Vignette(Color c, float clear, float full, float edge)
    {
        const int w = 128, h = 72;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "WeatherVeil" };
        var px = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                // Distance from the centre in screen-height units, so the clear patch is round.
                float dx = (x + 0.5f - w / 2f) / h, dy = (y + 0.5f - h / 2f) / h;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(clear, full, d));
                px[y * w + x] = new Color(c.r, c.g, c.b, t * edge);
            }
        tex.SetPixels(px);
        tex.Apply();
        return new StyleBackground(tex);
    }

    private void Start()
    {
        // Dim the sun: night is dark, a storm is grey. Scene lights reset when the scene reloads.
        float scale = Kind == Weather.NightFog ? 0.35f : Kind == Weather.Storm ? 0.55f : Kind == Weather.Fog ? 0.8f : 1f;
        _suns = FindObjectsByType<Light>(FindObjectsSortMode.None);
        _sunIntensity = new float[_suns.Length];
        for (int i = 0; i < _suns.Length; i++)
        {
            _sunIntensity[i] = _suns[i].intensity;
            if (_suns[i].type == LightType.Directional) _suns[i].intensity *= scale;
        }

        var sfx = AudioService.Instance != null ? AudioService.Instance.Sfx : null;
        if (Kind == Weather.Storm && sfx != null && sfx.WashLoop != null)
        {
            _rainLoop = gameObject.AddComponent<AudioSource>();
            _rainLoop.clip = sfx.WashLoop; // the bow wash, higher and softer, makes a fair rain hiss
            _rainLoop.loop = true;
            _rainLoop.pitch = 1.7f;
            _rainLoop.volume = 0.25f * AudioService.Volume(AudioBus.Effects);
            _rainLoop.spatialBlend = 0f;
            _rainLoop.Play();
        }
    }

    private void OnDestroy()
    {
        if (_suns == null) return;
        for (int i = 0; i < _suns.Length; i++) if (_suns[i] != null) _suns[i].intensity = _sunIntensity[i];
    }

    private void Update()
    {
        if (Kind != Weather.Storm) return;
        _rain.MarkDirtyRepaint();

        // Lightning every 8-20 s: a double flash, then thunder a moment later.
        if (Time.time >= _nextLightning)
        {
            _nextLightning = Time.time + Random.Range(8f, 20f);
            _flashUntil = Time.unscaledTime + 0.35f;
            var audio = AudioService.Instance;
            if (audio != null && audio.Sfx != null)
                audio.Play(audio.Sfx.MineExplosion, AudioBus.Effects, 0.35f, 0.35f, Random.Range(0.4f, 1.2f));
        }
        float left = _flashUntil - Time.unscaledTime;
        _flash.style.opacity = left > 0f ? (left > 0.25f || left < 0.12f ? 0.55f : 0.1f) : 0f;
    }

    // Rain: short slanted streaks falling across the screen.
    private void DrawRain(MeshGenerationContext ctx)
    {
        var rect = _rain.contentRect;
        if (rect.width < 1f || _drops == null) return;
        var p = ctx.painter2D;
        p.strokeColor = new Color(0.8f, 0.86f, 0.95f, 0.35f);
        p.lineWidth = 1.5f;
        float t = Time.unscaledTime;
        p.BeginPath();
        for (int i = 0; i < _drops.Length; i++)
        {
            float x = Mathf.Repeat(_drops[i].x + t * 0.12f, 1f) * rect.width;
            float y = Mathf.Repeat(_drops[i].y + t * (0.9f + 0.4f * (i % 3)), 1f) * rect.height;
            p.MoveTo(new Vector2(x, y));
            p.LineTo(new Vector2(x - 9f, y + 26f));
        }
        p.Stroke();
    }
}
