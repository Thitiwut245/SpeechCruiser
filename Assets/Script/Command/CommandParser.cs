using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// TIER 0 — the deterministic command path. No model, no network, no allocation
/// storm, and structurally incapable of producing an action outside
/// <see cref="ShipAction"/>.
///
/// Runs in well under a millisecond, so the ship can respond on the same frame the
/// transcription lands. Anything it cannot resolve confidently is handed upward to
/// the constrained classifier (Tier 1) or the conversation model (Tier 3).
/// </summary>
public static class CommandParser
{
    /// <summary>Score at or above which we act immediately, no model involved.</summary>
    public const float AcceptHigh = 0.90f;

    /// <summary>Score below which this is not a command at all.</summary>
    public const float AcceptLow = 0.70f;

    /// <summary>Most distinct orders we will pull out of one utterance.</summary>
    private const int MaxCompoundActions = 3;

    public struct Match
    {
        public ShipAction Action { get; set; }
        public float Confidence { get; set; }
        public string Phrase { get; set; }
    }

    public struct Result
    {
        /// <summary>Recognised orders, best first. Empty if nothing matched.</summary>
        public List<Match> Matches { get; set; }

        /// <summary>Crew member the player named explicitly, if any.</summary>
        public RadioChannel? AddressedCrew { get; set; }

        /// <summary>Utterance after normalisation, for logging.</summary>
        public string Normalized { get; set; }

        /// <summary>True if the phrasing looks like a question or open conversation.</summary>
        public bool LooksLikeQuestion { get; set; }

        /// <summary>True if the player said nothing but a crew member's name.</summary>
        public bool WasNameOnly { get; set; }

        public float TopConfidence
        {
            get { return (Matches != null && Matches.Count > 0) ? Matches[0].Confidence : 0f; }
        }

        public bool HasConfidentCommand
        {
            get { return TopConfidence >= AcceptHigh; }
        }

        public bool IsAmbiguous
        {
            get { return TopConfidence >= AcceptLow && TopConfidence < AcceptHigh; }
        }
    }

    // ------------------------------------------------------------------
    // Vague phrases lose ties to explicit ones. "go fast" must land on FULL,
    // not on the SLOW that bare "go" implies.
    // ------------------------------------------------------------------
    private static readonly HashSet<string> VaguePhrases = new HashSet<string>
    {
        "go", "move", "start", "begin", "engage", "ahead", "underway",
        "more", "less", "back", "report", "status",
    };
    private const float VaguePenalty = 0.97f;

    // Pre-normalised phrase table, built once on first use.
    private struct CompiledPhrase
    {
        public ShipAction Action { get; set; }
        public string[] Tokens { get; set; }
        public string Original { get; set; }
        public float Weight { get; set; }
    }

    private static CompiledPhrase[] _compiled;
    private static HashSet<string> _fillerSet;
    private static HashSet<string> _lookoutNames;
    private static HashSet<string> _engineerNames;

    private static void EnsureCompiled()
    {
        if (_compiled != null) return;

        _fillerSet = new HashSet<string>(CommandVocabulary.Fillers);
        _lookoutNames = new HashSet<string>(CommandVocabulary.LookoutNames);
        _engineerNames = new HashSet<string>(CommandVocabulary.EngineerNames);

        var list = new List<CompiledPhrase>(256);
        foreach (var entry in CommandVocabulary.Entries)
        {
            foreach (var phrase in entry.Phrases)
            {
                // Phrases go through the exact same normalisation as the player's
                // speech, so filler removal can never desynchronise the two sides.
                var tokens = Tokenize(Normalize(phrase), stripCrewNames: false, addressed: null);
                if (tokens.Count == 0) continue;

                list.Add(new CompiledPhrase
                {
                    Action = entry.Action,
                    Tokens = tokens.ToArray(),
                    Original = phrase,
                    Weight = VaguePhrases.Contains(phrase) ? VaguePenalty : 1f,
                });
            }
        }
        _compiled = list.ToArray();
    }

    // ------------------------------------------------------------------
    // Normalisation
    // ------------------------------------------------------------------
    private static string Normalize(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;

        var sb = new StringBuilder(raw.Length);
        foreach (char c in raw.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c)) sb.Append(c);
            else if (c == '\'') { /* drop apostrophes: what's -> whats */ }
            else sb.Append(' ');
        }

        string text = sb.ToString();

        // Multi-word fillers first. "all right" must die before its "right" is
        // mistaken for a starboard order.
        foreach (var fp in CommandVocabulary.FillerPhrases)
        {
            text = text.Replace(" " + fp + " ", " ");
            if (text.StartsWith(fp + " ")) text = text.Substring(fp.Length + 1);
            if (text.EndsWith(" " + fp)) text = text.Substring(0, text.Length - fp.Length - 1);
            if (text.Trim() == fp) text = " ";
        }

        return text;
    }

    private static List<string> Tokenize(string normalized, bool stripCrewNames, RadioChannel? addressed)
    {
        var result = new List<string>(12);
        var parts = normalized.Split(' ');

        foreach (var p in parts)
        {
            if (p.Length == 0) continue;
            if (_fillerSet != null && _fillerSet.Contains(p)) continue;
            if (stripCrewNames && (_lookoutNames.Contains(p) || _engineerNames.Contains(p))) continue;
            result.Add(p);
        }

        return result;
    }

    // ------------------------------------------------------------------
    // Public entry point
    // ------------------------------------------------------------------
    public static Result Parse(string rawSpeech)
    {
        EnsureCompiled();

        var result = new Result
        {
            Matches = new List<Match>(MaxCompoundActions),
            AddressedCrew = null,
            Normalized = string.Empty,
            LooksLikeQuestion = false,
            WasNameOnly = false,
        };

        if (string.IsNullOrWhiteSpace(rawSpeech)) return result;

        string normalized = Normalize(rawSpeech);
        result.Normalized = normalized.Trim();

        // Who did the player address? Checked before crew names are stripped.
        var rawTokens = Tokenize(normalized, stripCrewNames: false, addressed: null);
        foreach (var t in rawTokens)
        {
            if (_lookoutNames.Contains(t)) { result.AddressedCrew = RadioChannel.Lookout; break; }
            if (_engineerNames.Contains(t)) { result.AddressedCrew = RadioChannel.ChiefEngineer; break; }
        }

        // "look out" and "engine room" arrive as two tokens — catch those too.
        if (result.AddressedCrew == null)
        {
            if (normalized.Contains("look out")) result.AddressedCrew = RadioChannel.Lookout;
            else if (normalized.Contains("engine room")) result.AddressedCrew = RadioChannel.ChiefEngineer;
        }

        var tokens = Tokenize(normalized, stripCrewNames: true, addressed: result.AddressedCrew);

        // Player said only a name: "Finn?" — treat as a request for that crew's report.
        if (tokens.Count == 0)
        {
            result.WasNameOnly = result.AddressedCrew != null;
            return result;
        }

        foreach (var t in tokens)
        {
            if (CommandVocabulary.QuestionWords.Contains(t)) { result.LooksLikeQuestion = true; break; }
        }

        // ------------------------------------------------------------------
        // Greedy multi-match: pull out the best order, consume its words, repeat.
        // Handles "full ahead and hard to port" as two actions in one breath.
        // ------------------------------------------------------------------
        var used = new bool[tokens.Count];
        var consumed = new List<int>(4);
        var bestConsumed = new List<int>(4);

        // At most one order per family. "go fast and turn left" must yield one engine
        // order and one helm order, never two competing speeds.
        var claimedCategories = new HashSet<ActionCategory>();

        for (int pass = 0; pass < MaxCompoundActions; pass++)
        {
            float bestScore = 0f;
            int bestLen = 0;
            int bestIdx = -1;
            bestConsumed.Clear();

            for (int c = 0; c < _compiled.Length; c++)
            {
                var cp = _compiled[c];
                if (claimedCategories.Contains(cp.Action.Category())) continue;

                float score = ScorePhrase(tokens, used, cp.Tokens, consumed) * cp.Weight;
                if (score <= 0f) continue;

                // Higher score wins; on a tie the more specific (longer) phrase wins.
                bool better = score > bestScore + 0.0001f ||
                              (Mathf.Abs(score - bestScore) <= 0.0001f && cp.Tokens.Length > bestLen);
                if (!better) continue;

                bestScore = score;
                bestLen = cp.Tokens.Length;
                bestIdx = c;
                bestConsumed.Clear();
                bestConsumed.AddRange(consumed);
            }

            if (bestIdx < 0 || bestScore < AcceptLow) break;

            // Don't report the same action twice from one utterance.
            bool duplicate = false;
            for (int i = 0; i < result.Matches.Count; i++)
            {
                if (result.Matches[i].Action == _compiled[bestIdx].Action) { duplicate = true; break; }
            }

            if (!duplicate)
            {
                result.Matches.Add(new Match
                {
                    Action = _compiled[bestIdx].Action,
                    Confidence = Mathf.Clamp01(bestScore),
                    Phrase = _compiled[bestIdx].Original,
                });

                claimedCategories.Add(_compiled[bestIdx].Action.Category());
            }

            for (int i = 0; i < bestConsumed.Count; i++) used[bestConsumed[i]] = true;

            // Nothing left to match against.
            bool anyFree = false;
            for (int i = 0; i < used.Length; i++) if (!used[i]) { anyFree = true; break; }
            if (!anyFree) break;
        }

        // Best first.
        result.Matches.Sort((a, b) => b.Confidence.CompareTo(a.Confidence));
        return result;
    }

    /// <summary>
    /// Score one phrase against the utterance. Every word of the phrase must be
    /// present, in order, among the not-yet-consumed tokens — otherwise the phrase
    /// fails outright. Returns the mean per-word match quality.
    /// </summary>
    private static float ScorePhrase(List<string> utter, bool[] used, string[] phrase, List<int> consumed)
    {
        consumed.Clear();

        int cursor = 0;
        float total = 0f;

        for (int p = 0; p < phrase.Length; p++)
        {
            float best = 0f;
            int bestIdx = -1;

            for (int i = cursor; i < utter.Count; i++)
            {
                if (used[i]) continue;

                float s;
                if (!Phonetics.TokensMatch(utter[i], phrase[p], out s)) continue;

                if (s > best) { best = s; bestIdx = i; }
                if (s >= 1f) break; // exact hit, stop looking
            }

            if (bestIdx < 0) return 0f;

            total += best;
            consumed.Add(bestIdx);
            cursor = bestIdx + 1;
        }

        return total / phrase.Length;
    }

    /// <summary>Maps a parsed action onto the ship. Returns false if nothing was applied.</summary>
    public static bool Execute(ShipAction action, float? explicitDegrees = null)
    {
        var nav = ShipNavigator.Instance;
        if (nav == null || !action.IsMachinery()) return false;

        switch (action)
        {
            case ShipAction.EngineFullAstern: nav.SetGear(ShipEngine.TelegraphGear.FullAstern); return true;
            case ShipAction.EngineSlowAstern: nav.SetGear(ShipEngine.TelegraphGear.SlowAstern); return true;
            case ShipAction.EngineStop:       nav.SetGear(ShipEngine.TelegraphGear.Stop);       return true;
            case ShipAction.EngineDeadSlow:   nav.SetGear(ShipEngine.TelegraphGear.DeadSlow);   return true;
            case ShipAction.EngineSlow:       nav.SetGear(ShipEngine.TelegraphGear.Slow);       return true;
            case ShipAction.EngineHalf:       nav.SetGear(ShipEngine.TelegraphGear.Half);       return true;
            case ShipAction.EngineFull:       nav.SetGear(ShipEngine.TelegraphGear.Full);       return true;
            case ShipAction.EngineFlank:      nav.SetGear(ShipEngine.TelegraphGear.Flank);      return true;

            case ShipAction.EngineIncrease:   nav.IncreaseSpeed(); return true;
            case ShipAction.EngineDecrease:   nav.DecreaseSpeed(); return true;

            case ShipAction.TurnPort:
                if (explicitDegrees.HasValue) nav.ChangeHeading(-explicitDegrees.Value);
                else nav.TurnShip(-20f); 
                return true;
            case ShipAction.TurnStarboard:
                if (explicitDegrees.HasValue) nav.ChangeHeading(explicitDegrees.Value);
                else nav.TurnShip(20f);  
                return true;
            case ShipAction.HardPort:         nav.HardTurn(-1f);  return true;
            case ShipAction.HardStarboard:    nav.HardTurn(1f);   return true;
            case ShipAction.RudderMidships:   nav.RudderMidships(); return true;

            default: return false;
        }
    }

    private static readonly Dictionary<string, float> SpelledOutNumbers = new Dictionary<string, float>()
    {
        {"one", 1f}, {"two", 2f}, {"three", 3f}, {"four", 4f}, {"five", 5f}, 
        {"six", 6f}, {"seven", 7f}, {"eight", 8f}, {"nine", 9f}, {"ten", 10f},
        {"eleven", 11f}, {"twelve", 12f}, {"thirteen", 13f}, {"fourteen", 14f}, {"fifteen", 15f},
        {"sixteen", 16f}, {"seventeen", 17f}, {"eighteen", 18f}, {"nineteen", 19f}, {"twenty", 20f},
        {"thirty", 30f}, {"forty", 40f}, {"fourty", 40f}, {"fifty", 50f}, {"sixty", 60f},
        {"seventy", 70f}, {"eighty", 80f}, {"ninety", 90f},
        {"twenty five", 25f}, {"thirty five", 35f}, {"forty five", 45f}, {"fourty five", 45f}
    };

    public static float? ExtractDegrees(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        // Try extracting digits
        var match = Regex.Match(text, @"\b(\d+)\b");
        if (match.Success && float.TryParse(match.Groups[1].Value, out float num))
        {
            return num;
        }

        // Try spelled out numbers
        string lower = text.ToLowerInvariant();
        float bestMatchVal = -1f;
        int bestMatchLen = 0;
        foreach (var kvp in SpelledOutNumbers)
        {
            // If the string contains the word, pick the longest one (e.g. "forty five" over "forty")
            if (lower.Contains(kvp.Key) && kvp.Key.Length > bestMatchLen)
            {
                bestMatchVal = kvp.Value;
                bestMatchLen = kvp.Key.Length;
            }
        }
        
        if (bestMatchVal > 0f) return bestMatchVal;

        return null;
    }
}
