using System.Text;

/// <summary>
/// Cheap phonetic + edit-distance matching, tuned for one job: recognising nautical
/// commands from an ESL speaker through an imperfect speech recogniser.
///
/// The trick is the consonant SKELETON. Whisper's mistakes are overwhelmingly vowel
/// mistakes ("part" for "port", "starbird" for "starboard"). Collapsing vowels makes
/// those pairs identical, while keeping genuinely different words apart:
///
///   port -> prt        part -> prt        (match — same skeleton)
///   starboard -> strbrd    starbird -> strbrd   (match)
///   port -> prt        sort -> srt        (no match — different consonants)
///
/// No allocations beyond two small buffers per call. Comfortably sub-millisecond
/// for the whole vocabulary.
/// </summary>
public static class Phonetics
{
    /// <summary>
    /// Reduce a word to its consonant skeleton. Vowels are dropped (except a leading
    /// one), near-homophone consonants are folded into one class, doubles collapse.
    /// </summary>
    public static string Skeleton(string word)
    {
        if (string.IsNullOrEmpty(word)) return string.Empty;

        string w = word.ToLowerInvariant();

        // Digraphs first — these are single sounds and must not be treated as two letters.
        w = w.Replace("ph", "f")
             .Replace("ck", "k")
             .Replace("kn", "n")
             .Replace("wr", "r")
             .Replace("qu", "kw")
             .Replace("sh", "s")
             .Replace("ch", "c")
             .Replace("th", "t")
             .Replace("gh", "g");

        var sb = new StringBuilder(w.Length);
        for (int i = 0; i < w.Length; i++)
        {
            char c = w[i];
            if (!char.IsLetter(c)) continue;

            // Fold consonants that Whisper and ESL speakers routinely swap.
            switch (c)
            {
                case 'k': c = 'c'; break;
                case 'x': c = 'c'; break;
                case 'z': c = 's'; break;
                case 'v': c = 'f'; break;
                case 'j': c = 'g'; break;
                case 'y': c = 'i'; break;
            }

            bool isVowel = (c == 'a' || c == 'e' || c == 'i' || c == 'o' || c == 'u');

            // Keep a leading vowel (distinguishes "astern" from "stern"), drop the rest.
            if (isVowel && sb.Length > 0) continue;

            // Collapse doubled letters ("full" -> "fl").
            if (sb.Length > 0 && sb[sb.Length - 1] == c) continue;

            sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>Standard Levenshtein edit distance, two-row rolling buffer.</summary>
    public static int Levenshtein(string a, string b)
    {
        if (string.IsNullOrEmpty(a)) return string.IsNullOrEmpty(b) ? 0 : b.Length;
        if (string.IsNullOrEmpty(b)) return a.Length;

        int n = a.Length, m = b.Length;
        var prev = new int[m + 1];
        var curr = new int[m + 1];

        for (int j = 0; j <= m; j++) prev[j] = j;

        for (int i = 1; i <= n; i++)
        {
            curr[0] = i;
            for (int j = 1; j <= m; j++)
            {
                int cost = (a[i - 1] == b[j - 1]) ? 0 : 1;
                int del = prev[j] + 1;
                int ins = curr[j - 1] + 1;
                int sub = prev[j - 1] + cost;
                curr[j] = del < ins ? (del < sub ? del : sub) : (ins < sub ? ins : sub);
            }
            var swap = prev; prev = curr; curr = swap;
        }

        return prev[m];
    }

    /// <summary>Edit distance normalised to 0..1, where 1 is identical.</summary>
    public static float Similarity(string a, string b)
    {
        if (string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b)) return 1f;
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0f;
        if (a == b) return 1f;

        int max = a.Length > b.Length ? a.Length : b.Length;
        return 1f - ((float)Levenshtein(a, b) / max);
    }

    /// <summary>
    /// Best of raw and skeleton similarity. Skeleton catches the vowel mistakes,
    /// raw catches everything else.
    /// </summary>
    public static float FuzzyScore(string a, string b)
    {
        if (a == b) return 1f;

        float raw = Similarity(a, b);
        float skel = Similarity(Skeleton(a), Skeleton(b));
        return raw > skel ? raw : skel;
    }

    /// <summary>
    /// Minimum score for two tokens to count as "the same word".
    ///
    /// Short words demand a near-exact match, because on a 4-letter word a single
    /// edit is already 0.75 similarity — that would let "sort" match "port".
    /// Requiring ~1.0 for short words means they must be equal either raw or by
    /// skeleton, which still accepts "part"/"port" (identical skeletons) while
    /// rejecting "sort"/"port" (skeletons srt vs prt).
    /// </summary>
    public static float TokenThreshold(string a, string b)
    {
        int max = a.Length > b.Length ? a.Length : b.Length;
        return max <= 4 ? 0.99f : 0.74f;
    }

    /// <summary>True if two tokens should be considered the same word.</summary>
    public static bool TokensMatch(string a, string b, out float score)
    {
        score = FuzzyScore(a, b);
        return score >= TokenThreshold(a, b);
    }
}
