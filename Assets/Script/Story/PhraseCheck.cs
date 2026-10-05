using System.Collections.Generic;
using System.Text;

/// <summary>
/// Did the player say the phrase being practised? An order counts when the game hears the
/// same order (so "turn to the left" passes for "turn left", the way it would at sea); a
/// question or a sentence counts when most of its words are there. A phrase with "please"
/// must have the "please": chapter 3 teaches polite orders.
/// </summary>
public static class PhraseCheck
{
    public enum Verdict { Pass, Missed, NeedsPlease }

    private static readonly HashSet<string> Small = new HashSet<string> { "a", "an", "the", "is", "are", "to", "it", "we", "you", "i", "of" };

    public static Verdict Check(string phrase, string heard)
    {
        if (string.IsNullOrWhiteSpace(heard)) return Verdict.Missed;
        var want = Words(phrase);
        var got = Words(heard);
        bool pass = SameOrder(phrase, heard) || Overlap(want, got) >= 0.75f;
        if (!pass) return Verdict.Missed;
        if (want.Contains("please") && !got.Contains("please")) return Verdict.NeedsPlease;
        return Verdict.Pass;
    }

    /// <summary>Both are confident orders, and the order is the same.</summary>
    private static bool SameOrder(string phrase, string heard)
    {
        var a = CommandParser.Parse(phrase);
        if (!a.HasConfidentCommand || a.LooksLikeQuestion) return false;
        var b = CommandParser.Parse(heard);
        if (!b.HasConfidentCommand) return false;
        return Top(a) == Top(b) && Top(a) != ShipAction.None;
    }

    private static ShipAction Top(CommandParser.Result r)
    {
        ShipAction best = ShipAction.None;
        float conf = -1f;
        foreach (var m in r.Matches)
            if (m.Confidence > conf) { conf = m.Confidence; best = m.Action; }
        return best;
    }

    /// <summary>Share of the phrase's meaningful words that were said.</summary>
    private static float Overlap(List<string> want, List<string> got)
    {
        int needed = 0, found = 0;
        foreach (var w in want)
        {
            if (Small.Contains(w)) continue;
            needed++;
            if (got.Contains(w)) found++;
        }
        return needed == 0 ? 1f : found / (float)needed;
    }

    public static List<string> Words(string text)
    {
        var sb = new StringBuilder();
        foreach (char raw in text.ToLowerInvariant())
        {
            char c = raw == '’' ? '\'' : raw;
            sb.Append(char.IsLetterOrDigit(c) || c == '\'' ? c : ' ');
        }
        string s = " " + sb + " ";
        // Contractions, so "what's" and "what is" are the same.
        s = s.Replace("what's", "what is").Replace("where's", "where is").Replace("it's", "it is")
             .Replace("there's", "there is").Replace("let's", "let us").Replace("'", "");
        var words = new List<string>();
        foreach (var w in s.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries)) words.Add(w);
        return words;
    }
}
