using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

/// <summary>
/// The UI font has Latin letters only. Thai (a Thai keyboard in the typed-order box, a
/// Windows microphone named "ไมโครโฟน", key names) would show as empty boxes. At startup
/// this adds a Thai-capable font already installed on the player's computer as a fallback
/// for every UI panel, so no font file has to ship with the game.
/// </summary>
public static class SystemFontFallback
{
    // Windows ships Leelawadee UI and Tahoma with Thai; the others cover macOS and Linux.
    private static readonly string[] Families = { "Leelawadee UI", "Leelawadee", "Tahoma", "Noto Sans Thai", "Thonburi", "Sukhumvit Set" };

    private static FontAsset _font;
    private static bool _searched;
    private static readonly HashSet<PanelSettings> _done = new HashSet<PanelSettings>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init()
    {
        SceneManager.sceneLoaded += (scene, mode) => ApplyToAllPanels();
        ApplyToAllPanels();
    }

    /// <summary>The font found, or null when none of the families is installed.</summary>
    public static FontAsset Font
    {
        get
        {
            if (_searched) return _font;
            _searched = true;
            foreach (var family in Families)
            {
                _font = FontAsset.CreateFontAsset(family, "Regular");
                if (_font != null)
                {
                    Debug.Log("[FONT]: Thai fallback from system font \"" + family + "\".");
                    return _font;
                }
            }
            Debug.LogWarning("[FONT]: no Thai-capable system font found; Thai text will show as boxes.");
            return null;
        }
    }

    public static void ApplyToAllPanels()
    {
        foreach (var doc in Object.FindObjectsByType<UIDocument>(FindObjectsInactive.Include))
            Apply(doc.panelSettings);
    }

    private static void Apply(PanelSettings panel)
    {
        if (panel == null || _done.Contains(panel) || Font == null) return;
        _done.Add(panel);

        var settings = panel.textSettings;
        if (settings == null)
        {
            // A runtime copy, so the project asset itself is never changed.
            settings = ScriptableObject.CreateInstance<PanelTextSettings>();
            panel.textSettings = settings;
        }
        if (settings.fallbackFontAssets == null) settings.fallbackFontAssets = new List<FontAsset>();
        if (!settings.fallbackFontAssets.Contains(Font)) settings.fallbackFontAssets.Add(Font);
    }
}
