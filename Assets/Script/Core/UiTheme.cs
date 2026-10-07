using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

/// <summary>
/// The game's look, after Alto's Adventure: spaced capitals, a warm sunset accent, no pill
/// buttons and no boxes (controls are words, with an accent line under the main one).
/// Two grounds:
/// - At sea (HUD, dialogue): white type with a dark outline and shadow on dusk gradients.
/// - The menus (title, story, settings, shipyard): "paper", dark navy ink on a warm sand
///   gradient from the left edge. Projectors can't show black (room light turns it grey), so
///   white-on-dark washes out there; dark ink on a light ground keeps its contrast (~14:1).
/// Body text is Noto Sans (bundled in Resources/Fonts); the panel's Thai fallback still applies.
/// </summary>
public static class UiTheme
{
    public static readonly Color White = new Color(1f, 1f, 1f, 0.96f);
    public static readonly Color Dim = new Color(1f, 1f, 1f, 0.82f);
    public static readonly Color Faint = new Color(1f, 1f, 1f, 0.3f);
    public static readonly Color Hairline = new Color(1f, 1f, 1f, 0.38f);
    public static readonly Color Peach = new Color(1f, 0.74f, 0.58f);
    public static readonly Color Coral = new Color(1f, 0.52f, 0.46f);
    public static readonly Color Mint = new Color(0.62f, 0.95f, 0.82f);
    /// <summary>Behind cards that must be read over the sea: a deep dusk violet, see-through.</summary>
    public static readonly Color Card = new Color(0.10f, 0.08f, 0.15f, 0.82f);
    public static readonly Color Dusk = new Color(0.10f, 0.07f, 0.14f);

    // Paper: the menus' ground and inks. Contrast on Sand: Ink ~14:1, InkSoft ~8:1, Rust ~6:1, Teal ~5.6:1.
    public static readonly Color Sand = new Color(0.98f, 0.95f, 0.894f);
    public static readonly Color Ink = new Color(0.086f, 0.141f, 0.251f);
    public static readonly Color InkSoft = new Color(0.243f, 0.290f, 0.392f);
    public static readonly Color InkLine = new Color(0.086f, 0.141f, 0.251f, 0.3f);
    public static readonly Color Rust = new Color(0.627f, 0.196f, 0.094f);
    public static readonly Color Teal = new Color(0.047f, 0.424f, 0.384f);

    private static FontAsset _font, _thin, _strong;
    private static bool _searched, _searchedThin, _searchedStrong;
    private static StyleSheet _sheet;

    // Noto Sans is bundled (Resources/Fonts/NotoSans.ttf); these are only a fallback.
    private static readonly string[,] BodyFaces =
    {
        { "Noto Sans", "Regular" }, { "Segoe UI", "Regular" }, { "Helvetica Neue", "Regular" }, { "Arial", "Regular" },
    };
    private static readonly string[,] StrongFaces =
    {
        { "Segoe UI", "Semibold" }, { "Segoe UI Semibold", "Regular" }, { "Helvetica Neue", "Medium" }, { "Noto Sans", "Medium" }, { "Arial", "Bold" },
    };
    private static readonly string[,] ThinFaces =
    {
        { "Segoe UI", "Light" }, { "Segoe UI Light", "Regular" }, { "Helvetica Neue", "Light" }, { "Noto Sans", "Light" },
    };

    /// <summary>The body face, Noto Sans (bold is drawn from it), or null for the panel's default.</summary>
    public static FontAsset Font => Find(BodyFaces, ref _font, ref _searched, "Fonts/NotoSans");

    /// <summary>A semibold face for text straight over the bright sea (the HUD), or null.</summary>
    public static FontAsset StrongFont => Find(StrongFaces, ref _strong, ref _searchedStrong);

    /// <summary>The thin face for big titles only, or null.</summary>
    public static FontAsset ThinFont => Find(ThinFaces, ref _thin, ref _searchedThin);

    private static FontAsset Find(string[,] faces, ref FontAsset font, ref bool searched, string bundled = null)
    {
        if (searched) return font;
        searched = true;
        var file = bundled != null ? Resources.Load<Font>(bundled) : null;
        if (file != null) font = FontAsset.CreateFontAsset(file);
        for (int i = 0; i < faces.GetLength(0) && font == null; i++)
            font = FontAsset.CreateFontAsset(faces[i, 0], faces[i, 1]);
        if (font == null) Debug.LogWarning("[THEME]: no system font found for the UI; using the default.");
        else if (SystemFontFallback.Font != null)
        {
            // A font made at runtime starts with no fallback list.
            if (font.fallbackFontAssetTable == null) font.fallbackFontAssetTable = new System.Collections.Generic.List<FontAsset>();
            if (!font.fallbackFontAssetTable.Contains(SystemFontFallback.Font)) font.fallbackFontAssetTable.Add(SystemFontFallback.Font); // Thai still shows
        }
        return font;
    }

    /// <summary>
    /// Themes everything under <paramref name="root"/>: the body face (semibold over the open
    /// sea), and, through Resources/UI/Theme.uss, a dark shadow on every piece of text plus a
    /// dark outline over the sea. Rules in a style sheet reach text added later, too.
    /// </summary>
    public static void Apply(VisualElement root, bool overSea = false)
    {
        if (root == null) return;
        var face = overSea && StrongFont != null ? StrongFont : Font;
        if (face != null) root.style.unityFontDefinition = FontDefinition.FromSDFFont(face);
        if (_sheet == null) _sheet = Resources.Load<StyleSheet>("UI/Theme");
        if (_sheet != null && !root.styleSheets.Contains(_sheet)) root.styleSheets.Add(_sheet);
        root.AddToClassList("theme-root");
        root.EnableInClassList("theme-sea", overSea);
    }

    /// <summary>
    /// Puts <paramref name="e"/> on paper: Noto Sans in navy ink with no outline or glow
    /// (Theme.uss's .theme-paper rule), for a column standing on a <see cref="SideScrim"/> of Sand.
    /// </summary>
    public static void Paper(VisualElement e)
    {
        if (e == null) return;
        if (Font != null) e.style.unityFontDefinition = FontDefinition.FromSDFFont(Font);
        e.AddToClassList("theme-paper");
    }

    /// <summary>Big titles in the thin face.</summary>
    public static void Thin(VisualElement e)
    {
        if (e != null && ThinFont != null) e.style.unityFontDefinition = FontDefinition.FromSDFFont(ThinFont);
    }

    /// <summary>A shadow under text (inherited by children), so white reads over bright water.</summary>
    public static void Shadow(VisualElement e, float blur = 6f, float alpha = 0.55f)
    {
        e.style.textShadow = new TextShadow { offset = new Vector2(0f, 1.5f), blurRadius = blur, color = new Color(0f, 0f, 0f, alpha) };
    }

    /// <summary>A full-screen dusk dim (not a box): the backing for overlays like the order card.</summary>
    public static void FullDim(VisualElement e, float alpha = 0.8f)
    {
        e.style.position = Position.Absolute;
        e.style.left = 0; e.style.right = 0; e.style.top = 0; e.style.bottom = 0;
        e.style.translate = new Translate(0f, 0f);
        e.style.width = StyleKeyword.Auto;
        e.style.backgroundColor = new Color(Dusk.r, Dusk.g, Dusk.b, alpha);
        HudLook.Border(e, 0f, Color.clear);
        HudLook.Radius(e, 0f);
        e.style.alignItems = Align.Center;
        e.style.justifyContent = Justify.Center;
    }

    /// <summary>A word-button: spaced capitals, no box, with an accent line under it when <paramref name="primary"/>.</summary>
    public static void WordButton(Button b, bool primary, float size = 16f)
    {
        b.style.backgroundColor = Color.clear;
        HudLook.Border(b, 0f, Color.clear);
        HudLook.Radius(b, 0f);
        b.style.fontSize = size;
        b.style.letterSpacing = 4f;
        b.style.color = primary ? Peach : White;
        b.style.paddingLeft = 4f; b.style.paddingRight = 4f; b.style.paddingTop = 4f; b.style.paddingBottom = 4f;
        if (primary)
        {
            b.style.borderBottomWidth = 2f;
            b.style.borderBottomColor = Peach;
        }
    }

    /// <summary>A hairline pill outline.</summary>
    public static void Pill(VisualElement e, Color line, float width = 1.5f)
    {
        HudLook.Border(e, width, line);
        HudLook.Radius(e, 999f);
    }

    /// <summary>A see-through dusk card with soft corners, for things read over the sea (help, coach).</summary>
    public static void CardBack(VisualElement e, float padding = 18f)
    {
        e.style.backgroundColor = Card;
        HudLook.Border(e, 0f, Color.clear);
        HudLook.Radius(e, 18f);
        e.style.paddingTop = padding; e.style.paddingBottom = padding;
        e.style.paddingLeft = padding + 4f; e.style.paddingRight = padding + 4f;
    }

    /// <summary>
    /// A gradient from the left edge of the screen (dusk, or <paramref name="tint"/>), fading
    /// out by <paramref name="width"/>: the ground for a menu column over the live harbour.
    /// </summary>
    public static VisualElement SideScrim(float width, float alpha, Color? tint = null)
    {
        var ground = tint ?? Dusk;
        // Built from thin solid strips (a 32x1 gradient texture didn't draw): solid for the
        // first half of the width, then fading out.
        var e = new VisualElement { pickingMode = PickingMode.Ignore };
        e.style.position = Position.Absolute;
        e.style.left = 0; e.style.top = 0; e.style.bottom = 0;
        e.style.width = width;
        e.style.flexDirection = FlexDirection.Row;
        const int strips = 160; // thin enough that the fade shows no bands
        for (int i = 0; i < strips; i++)
        {
            float t = (i + 0.5f) / strips;
            // Mathf.SmoothStep(a, b, t) blends between a and b, so ease the fade's own 0..1.
            float a = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 1f, t));
            var s = new VisualElement { pickingMode = PickingMode.Ignore };
            s.style.flexGrow = 1f;
            s.style.flexBasis = 0f;
            s.style.backgroundColor = new Color(ground.r, ground.g, ground.b, a * alpha);
            e.Add(s);
        }
        return e;
    }

    /// <summary>
    /// A dusk gradient along one edge of the screen (top or bottom), so the HUD's thin white
    /// text has something calm to sit on. Ignores the pointer.
    /// </summary>
    public static VisualElement Scrim(bool top, float height, float alpha)
    {
        var e = new VisualElement { pickingMode = PickingMode.Ignore };
        e.style.position = Position.Absolute;
        e.style.left = 0; e.style.right = 0;
        if (top) e.style.top = 0; else e.style.bottom = 0;
        e.style.height = height;
        var tex = new Texture2D(1, 32, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Scrim" };
        for (int y = 0; y < 32; y++)
        {
            float t = y / 31f; // 0 at the texture's bottom row
            float a = top ? Mathf.SmoothStep(0f, 1f, t) : Mathf.SmoothStep(0f, 1f, 1f - t);
            tex.SetPixel(0, y, new Color(Dusk.r, Dusk.g, Dusk.b, a * alpha));
        }
        tex.Apply();
        e.style.backgroundImage = new StyleBackground(tex);
        e.style.backgroundSize = new BackgroundSize(Length.Percent(100f), Length.Percent(100f));
        return e;
    }
}
