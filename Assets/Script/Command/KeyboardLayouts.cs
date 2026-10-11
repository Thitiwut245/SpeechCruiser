using System.Collections.Generic;
using System.Text;

/// <summary>
/// Keyboard-layout helpers. The radio keys are physical keys, so their names come from the
/// key's position ("T"), never from the active layout (on a Thai layout T types "ะ"). A
/// typed order written with the Thai layout still on is read back as the English keys.
/// </summary>
public static class KeyboardLayouts
{
    /// <summary>"&lt;Keyboard&gt;/t" → "T", "&lt;Keyboard&gt;/leftShift" → "LEFT SHIFT". Layout-independent.</summary>
    public static string PhysicalKeyName(string bindingPath, string fallback)
    {
        if (string.IsNullOrEmpty(bindingPath)) return fallback;
        int slash = bindingPath.LastIndexOf('/');
        string key = slash >= 0 ? bindingPath.Substring(slash + 1) : bindingPath;
        if (key.Length == 0) return fallback;

        switch (key)
        {
            case "backquote": return "`";
            case "minus": return "-";
            case "equals": return "=";
            case "leftBracket": return "[";
            case "rightBracket": return "]";
            case "backslash": return "\\";
            case "semicolon": return ";";
            case "quote": return "'";
            case "comma": return ",";
            case "period": return ".";
            case "slash": return "/";
        }

        if (key.StartsWith("numpad") && key.Length > 6) key = "numpad " + key.Substring(6);

        // camelCase control names become words: "leftShift" → "LEFT SHIFT", "f1" stays "F1".
        var sb = new StringBuilder(key.Length + 4);
        for (int i = 0; i < key.Length; i++)
        {
            if (i > 0 && char.IsUpper(key[i])) sb.Append(' ');
            sb.Append(char.ToUpperInvariant(key[i]));
        }
        return sb.ToString();
    }

    // The Thai Kedmanee layout (unshifted), key by key, and the US key it sits on.
    // Orders are typed in lower case, so the shifted layer isn't needed.
    public const string ThaiKeys = "_ๅ/-ภถุึคตจขชๆไำพะัีรนยบลฃฟหกดเ้่าสวงผปแอิืทมใฝ";
    public const string UsKeys   = "`1234567890-=qwertyuiop[]\\asdfghjkl;'zxcvbnm,./";

    private static Dictionary<char, char> _thaiToUs;

    /// <summary>
    /// If the text was typed with the Thai layout on (mostly Thai characters), returns what the
    /// same keys spell on a US layout: "ะีพื สำดะ" → "turn left". Otherwise returns null.
    /// </summary>
    public static string FromThaiKedmanee(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        int thai = 0, letters = 0;
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c)) continue;
            letters++;
            if (c >= '฀' && c <= '๿') thai++;
        }
        if (letters == 0 || thai * 2 < letters) return null;

        if (_thaiToUs == null)
        {
            _thaiToUs = new Dictionary<char, char>();
            for (int i = 0; i < ThaiKeys.Length && i < UsKeys.Length; i++) _thaiToUs[ThaiKeys[i]] = UsKeys[i];
        }

        var sb = new StringBuilder(text.Length);
        foreach (char c in text) sb.Append(_thaiToUs.TryGetValue(c, out char us) ? us : c);
        return sb.ToString().ToLowerInvariant();
    }
}
