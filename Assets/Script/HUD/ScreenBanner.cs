using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// A big word over the sea for a moment that should be seen (an upgrade fitted, a checkpoint
/// passed): a small kicker line, the title, and an optional line under it, in white with a navy
/// outline so it reads over bright water. It pops in, holds, then rises and fades. No box.
/// </summary>
public static class ScreenBanner
{
    private static readonly System.Collections.Generic.List<VisualElement> Live = new System.Collections.Generic.List<VisualElement>();

    /// <summary>Takes down every banner showing: an order is being asked for, and nothing may cover it.</summary>
    public static void ClearAll()
    {
        foreach (var b in Live) b.RemoveFromHierarchy();
        Live.Clear();
    }

    /// <param name="left">Left edge of the area it is centred in, in percent of the screen (the right edge is the screen's).</param>
    /// <param name="top">Its top, in percent of the screen.</param>
    public static void Show(VisualElement root, string kicker, string title, string line, float seconds = 2.8f,
                            float left = 0f, float top = 16f, Color? kickerColour = null)
    {
        if (root == null) return;
        var banner = new VisualElement { pickingMode = PickingMode.Ignore, name = "screen-banner" };
        banner.style.position = Position.Absolute;
        banner.style.left = Length.Percent(left);
        banner.style.right = 0f;
        banner.style.top = Length.Percent(top);
        banner.style.alignItems = Align.Center;
        banner.style.opacity = 0f;

        if (!string.IsNullOrEmpty(kicker))
        {
            var k = Text(kicker, 22f);
            k.style.letterSpacing = 6f;
            k.style.color = kickerColour ?? new Color(1f, 0.85f, 0.4f);
            banner.Add(k);
        }
        var t = Text(title, 58f);
        t.style.letterSpacing = 4f;
        banner.Add(t);
        if (!string.IsNullOrEmpty(line)) banner.Add(Text(line, 26f));
        root.Add(banner);
        Live.Add(banner);

        float start = Time.unscaledTime;
        IVisualElementScheduledItem tick = null;
        tick = banner.schedule.Execute(() =>
        {
            float s = Time.unscaledTime - start;
            // Pop in with a small overshoot, hold, then rise and fade.
            float pop = Mathf.Clamp01(s / 0.35f);
            float scale = pop < 1f ? Mathf.Lerp(0.6f, 1.08f, pop) : Mathf.Lerp(1.08f, 1f, Mathf.Clamp01((s - 0.35f) / 0.2f));
            float leave = Mathf.Clamp01((s - (seconds - 0.6f)) / 0.6f);
            banner.style.scale = new Scale(new Vector3(scale, scale, 1f));
            banner.style.opacity = Mathf.Clamp01(s / 0.2f) * (1f - leave);
            banner.style.translate = new Translate(0f, -40f * leave);
            if (s >= seconds || banner.panel == null) { tick.Pause(); banner.RemoveFromHierarchy(); Live.Remove(banner); }
        }).Every(16);
    }

    private static Label Text(string text, float size)
    {
        var l = new Label(text) { pickingMode = PickingMode.Ignore };
        l.style.fontSize = size;
        l.style.unityFontStyleAndWeight = FontStyle.Bold;
        l.style.color = Color.white;
        l.style.unityTextAlign = TextAnchor.MiddleCenter;
        l.style.unityTextOutlineColor = UiTheme.Ink;
        l.style.unityTextOutlineWidth = 0.25f;
        l.style.marginTop = 0f;
        l.style.marginBottom = 0f;
        UiTheme.Shadow(l);
        return l;
    }

    /// <summary>
    /// SET SAIL, impossible to miss: a slanted band of navy ink sweeps in across the middle of the
    /// screen carrying the words, holds while they spread out, and sweeps off to the right, over the
    /// ship's horn, the telegraph bell and a deep swell. On a layer of its own (the shipyard's UI
    /// is hidden once the ship is at sea), removed when done. The layer's object sits at the scene
    /// root: under another UIDocument's object it would join that document's tree.
    /// </summary>
    public static void SetSail()
    {
        var go = new GameObject("SetSailMoment");
        var root = Layer(go, 30);
        if (root == null) { Object.Destroy(go); return; }
        root.style.overflow = Overflow.Hidden;

        var band = new VisualElement { pickingMode = PickingMode.Ignore };
        band.style.position = Position.Absolute;
        band.style.top = Length.Percent(34f);
        band.style.height = Length.Percent(30f);
        band.style.left = Length.Percent(-10f);
        band.style.width = Length.Percent(120f);
        band.style.backgroundColor = new Color(0.055f, 0.09f, 0.165f, 0.94f);
        band.style.borderTopWidth = 6f; band.style.borderBottomWidth = 6f;
        band.style.borderTopColor = UiTheme.Rust; band.style.borderBottomColor = UiTheme.Rust;
        band.style.rotate = new Rotate(new Angle(-4f, AngleUnit.Degree));
        band.style.justifyContent = Justify.Center;
        band.style.alignItems = Align.Center;

        var words = Text("SET SAIL", 112f);
        words.style.unityTextOutlineWidth = 0f;
        var line = Text("All hands, cast off!", 30f);
        line.style.color = new Color(1f, 0.85f, 0.4f);
        line.style.unityTextOutlineWidth = 0f;
        band.Add(words);
        band.Add(line);
        root.Add(band);

        var audio = AudioService.Instance;
        if (audio != null && audio.Sfx != null)
        {
            if (audio.Sfx.ShipHorn != null) audio.Play(audio.Sfx.ShipHorn, AudioBus.Effects, 0.75f);
            if (audio.Sfx.TelegraphBell != null) audio.Play(audio.Sfx.TelegraphBell, AudioBus.Effects, 0.5f, 1f, 0.15f);
        }
        UiSounds.Play(UiSounds.Swell, 0.6f);

        const float inSeconds = 0.35f, hold = 1.3f, outSeconds = 0.45f;
        float start = Time.unscaledTime;
        IVisualElementScheduledItem tick = null;
        tick = band.schedule.Execute(() =>
        {
            float s = Time.unscaledTime - start;
            float width = root.resolvedStyle.width;
            if (float.IsNaN(width) || width <= 0f) width = Screen.width;
            float x;
            if (s < inSeconds) { float k = s / inSeconds; x = -1.3f * (1f - k) * (1f - k); }
            else if (s < inSeconds + hold) x = 0f;
            else { float k = Mathf.Clamp01((s - inSeconds - hold) / outSeconds); x = 1.3f * k * k; }
            band.style.translate = new Translate(x * width, 0f);
            // The words spread out while the band holds.
            float spread = Mathf.Clamp01((s - inSeconds) / hold);
            words.style.letterSpacing = Mathf.Lerp(10f, 34f, 1f - (1f - spread) * (1f - spread));
            if (s >= inSeconds + hold + outSeconds) { tick.Pause(); Object.Destroy(go); }
        }).Every(16);
    }

    /// <summary>A full-screen UI layer of its own on <paramref name="go"/>, sharing the game's panel, at <paramref name="order"/>.</summary>
    public static VisualElement Layer(GameObject go, int order)
    {
        var host = SceneLoader.Instance != null ? SceneLoader.Instance.GetComponent<UIDocument>() : null;
        if (host == null || host.panelSettings == null) return null;
        var doc = go.AddComponent<UIDocument>();
        doc.panelSettings = host.panelSettings;
        doc.sortingOrder = order;
        var root = doc.rootVisualElement;
        UiTheme.Apply(root, overSea: true);
        root.pickingMode = PickingMode.Ignore;
        root.style.position = Position.Absolute;
        root.style.left = 0; root.style.right = 0; root.style.top = 0; root.style.bottom = 0;
        return root;
    }
}
