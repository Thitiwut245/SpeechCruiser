using System;
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
///
/// Safety rules, because a wrong order confidently read back is worse than none:
///  - Astern beats ahead: "full speed astern" never becomes FULL AHEAD.
///  - Questions never move the ship: "is there a reef ahead?" answers, it doesn't sail.
///  - The last correction wins: "port — no, starboard" turns starboard.
///  - Negated orders don't run: "don't stop" changes nothing.
///  - Contradictions are asked about, not guessed ("ahead or astern, Captain?").
///  - A real word is never fuzzy-matched to a different word ("fuel" is not "full").
/// </summary>
public static class CommandParser
{
    /// <summary>Score at or above which we act immediately, no model involved.</summary>
    public const float AcceptHigh = 0.90f;

    /// <summary>Score below which this is not a command at all.</summary>
    public const float AcceptLow = 0.70f;

    /// <summary>Most distinct orders we will pull out of one utterance.</summary>
    private const int MaxCompoundActions = 3;

    // Phrase words may be split by one other word ("full SPEED ahead"); each skipped word
    // costs a little, so "slow down, reef ahead" can't assemble "slow ... ahead".
    private const int MaxGap = 1;
    private const float GapPenalty = 0.1f;

    // A negator ("not", "don't", "never") reaches this many words forward, never past a comma.
    private const int NegationReach = 3;

    // A single everyday word ("right", "go", "ahead") only counts as an order when the
    // utterance is this short: "Right!" turns, "right, full ahead" doesn't.
    private const int MaxTokensForWeakWord = 2;

    // Fuzzy matching only between words at least this long; shorter mishears are listed
    // as explicit aliases in CommandVocabulary.
    private const int MinFuzzyLength = 5;

    // Only accept turn sizes in this range; larger numbers are courses, not turns.
    private const float MinTurnDegrees = 1f;
    private const float MaxTurnDegrees = 90f;

    public struct Match
    {
        public ShipAction Action { get; set; }
        public float Confidence { get; set; }
        public string Phrase { get; set; }
        internal int FirstToken { get; set; }
        internal int LastToken { get; set; }
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

        /// <summary>Order words were spoken inside a question, so nothing was executed.</summary>
        public bool OrderInQuestion { get; set; }

        /// <summary>Both ahead and astern were ordered. Ask instead of guessing.</summary>
        public bool DirectionConflict { get; set; }

        /// <summary>Both port and starboard were ordered. Ask instead of guessing.</summary>
        public bool SideConflict { get; set; }

        /// <summary>Orders were heard but negated or cancelled ("don't stop", "port... no"). Nothing changes.</summary>
        public bool NoChange { get; set; }

        /// <summary>Nothing but filler ("okay", "that's right").</summary>
        public bool WasFillerOnly { get; set; }

        /// <summary>Turn size spoken with the helm order ("port twenty"), in degrees.</summary>
        public float? Degrees { get; set; }

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

        /// <summary>True if at least one confident match physically changes the ship.</summary>
        public bool HasConfidentOrder
        {
            get
            {
                if (Matches == null) return false;
                for (int i = 0; i < Matches.Count; i++)
                {
                    if (Matches[i].Confidence >= AcceptHigh && Matches[i].Action.IsMachinery()) return true;
                }
                return false;
            }
        }

        public bool NeedsClarification
        {
            get { return DirectionConflict || SideConflict; }
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

    /// <summary>Single-word phrases that are everyday English first and orders second.</summary>
    private static readonly HashSet<string> WeakWords = new HashSet<string>
    {
        "right", "left", "ahead", "go", "move", "start", "begin", "engage", "back",
        "more", "less", "half", "medium", "fast", "full", "straight", "middle",
        "center", "centre", "halt", "creep", "crawl", "report", "status", "heading",
        "bearing", "situation", "underway", "speed", "slow", "slowly", "forward",
        "maximum", "max", "finish", "goal", "danger", "end",
    };

    private static readonly HashSet<string> Negators = new HashSet<string>
    {
        "not", "dont", "never", "no", "doesnt", "wont", "cant", "shouldnt", "mustnt",
    };

    /// <summary>First words that make a sentence a question.</summary>
    private static readonly HashSet<string> Interrogatives = new HashSet<string>
    {
        "what", "whats", "why", "how", "hows", "who", "whos", "when", "where", "wheres",
        "which", "is", "isnt", "are", "arent", "was", "were", "do", "does", "did",
        "can", "could", "should", "will", "would", "shall", "have", "has", "am",
        "any", "anything",
    };

    /// <summary>"Can you turn to port?" is a polite order, not a question.</summary>
    private static readonly HashSet<string> PoliteModals = new HashSet<string> { "can", "could", "would", "will" };

    /// <summary>Words skipped when looking for the first word of a sentence.</summary>
    private static readonly HashSet<string> LeadIns = new HashSet<string>
    {
        "hey", "ok", "okay", "so", "well", "uh", "um", "er", "oh", "captain", "sir", "bridge", "and",
    };

    /// <summary>
    /// Real English words that sit one edit away from a long vocabulary word. They are
    /// heard often enough in chatter that fuzzy matching them would fire orders.
    /// </summary>
    private static readonly HashSet<string> CommonWordGuard = new HashSet<string>
    {
        "plank", "blank", "flask", "enter", "master", "father", "caster", "taster", "fester",
        "lower", "flower", "blower", "glower", "slowed", "tower", "mower", "poser", "eastern",
        "reading", "leading", "heeding", "beading", "hearing", "wearing", "tearing", "fearing",
        "nearing", "creek", "crepe", "resort", "deport", "statue", "riddle", "fiddle", "muddle",
        "meddle", "piddle", "curse", "coarse", "clean", "cheek", "chuck", "spend", "steed",
        "spear", "rubber", "udder", "ruder", "steamy", "brawl", "drawl", "trawl", "begun",
        "enrage", "socks", "locks", "racks", "rocky", "heard", "board",
    };

    // Corrections: the words after the LAST marker are the order. A bare "no" only counts
    // when it stands alone ("No, starboard!", "no no", "no, not port"), not in "no problem".
    private static readonly Regex CorrectionMarker = new Regex(
        @"\b(?<m>no wait|wait no|i mean|correction|negative|actually|belay that|belay|scratch that|cancel that|cancel the order|cancel order|cancel|as you were|disregard that|disregard|never mind|nevermind|forget it|forget that|sorry)\b" +
        @"|\b(?<m>no)\b(?=\s*(?:[,.!?;:—–-]|$|no\b|not\b))",
        RegexOptions.IgnoreCase);

    // Only these undo an order that already ran. "Never mind" / "forget it" cancel just the
    // sentence they end, so "Finn, what's... never mind" can't reverse the engine.
    private static readonly HashSet<string> BelayMarkers = new HashSet<string>
    {
        "belay that", "belay", "scratch that", "cancel that", "cancel the order", "cancel order",
        "cancel", "as you were", "disregard that", "disregard",
    };

    /// <summary>
    /// A helm order with no side: ask which way instead of guessing. "Hard over!" arrives
    /// as a lone "hard" because "over" is radio filler, so that case is checked separately.
    /// </summary>
    private static readonly string[][] SidelessHelm =
    {
        new[] { "hard", "rudder" }, new[] { "turn", "around" }, new[] { "come", "about" },
        new[] { "turn", "about" }, new[] { "come", "around" },
        new[] { "bring", "her", "about" }, new[] { "bring", "her", "around" },
        new[] { "turn", "back" }, new[] { "u", "turn" }, new[] { "uturn" },
    };

    /// <summary>Two-word crew names, removed before single-word matching.</summary>
    private static readonly string[] MultiWordNames = { "engine room", "look out" };

    private static readonly Regex LeadingNo = new Regex(@"^[\s,.!;:—–-]*(?:no\b[\s,.!;:—–-]*)+", RegexOptions.IgnoreCase);

    // "the port" is the harbour you're sailing to; bare "port" is the left side.
    private static readonly Regex ThePort = new Regex(@"\b(?:the|our) port\b(?!\s+(?:side|bow|quarter|beam|rudder|helm))");

    private static readonly Regex SentenceSplit = new Regex(@"[^.!?]+[.!?]*");
    private static readonly char[] ClauseSeparators = { ',', ';', ':', '—', '–' };

    // Pre-normalised phrase table, built once on first use.
    private struct CompiledPhrase
    {
        public ShipAction Action { get; set; }
        public string[] Tokens { get; set; }
        public string Original { get; set; }
        public float Weight { get; set; }
        public bool IsWeak { get; set; }
        public bool StartsNegated { get; set; }
    }

    private static CompiledPhrase[] _compiled;
    private static HashSet<string> _fillerSet;
    private static HashSet<string> _lookoutNames;
    private static HashSet<string> _engineerNames;
    private static HashSet<string> _vocabularyWords;

    private static void EnsureCompiled()
    {
        if (_compiled != null) return;

        _fillerSet = new HashSet<string>(CommandVocabulary.Fillers);
        _lookoutNames = new HashSet<string>(CommandVocabulary.LookoutNames);
        _engineerNames = new HashSet<string>(CommandVocabulary.EngineerNames);

        _vocabularyWords = new HashSet<string>(_fillerSet);
        _vocabularyWords.UnionWith(_lookoutNames);
        _vocabularyWords.UnionWith(_engineerNames);
        _vocabularyWords.UnionWith(CommandVocabulary.QuestionWords);
        _vocabularyWords.UnionWith(Negators);
        _vocabularyWords.UnionWith(Interrogatives);

        var list = new List<CompiledPhrase>(320);
        foreach (var entry in CommandVocabulary.Entries)
        {
            foreach (var phrase in entry.Phrases)
            {
                // Phrases go through the exact same normalisation as the player's
                // speech, so filler removal can never desynchronise the two sides.
                var tokens = Tokenize(Normalize(phrase), stripCrewNames: false);
                if (tokens.Count == 0) continue;

                _vocabularyWords.UnionWith(tokens);
                list.Add(new CompiledPhrase
                {
                    Action = entry.Action,
                    Tokens = tokens.ToArray(),
                    Original = phrase,
                    Weight = VaguePhrases.Contains(phrase) ? VaguePenalty : 1f,
                    IsWeak = tokens.Count == 1 && WeakWords.Contains(tokens[0]),
                    StartsNegated = Negators.Contains(tokens[0]),
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

        var sb = new StringBuilder(raw.Length + 2);
        sb.Append(' ');
        foreach (char c in raw.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c)) sb.Append(c);
            else if (c == '\'' || c == '’') { /* drop apostrophes: what's -> whats */ }
            else sb.Append(' ');
        }
        sb.Append(' ');

        string text = Regex.Replace(sb.ToString(), @"\s+", " ");
        text = ThePort.Replace(text, "harbour");

        // Multi-word fillers first. "all right" must die before its "right" is
        // mistaken for a starboard order.
        foreach (var fp in CommandVocabulary.FillerPhrases)
        {
            string padded = " " + fp + " ";
            while (text.Contains(padded)) text = text.Replace(padded, " ");
        }

        return text.Trim();
    }

    private static List<string> Tokenize(string normalized, bool stripCrewNames)
    {
        var result = new List<string>(12);

        if (stripCrewNames)
        {
            string padded = " " + normalized + " ";
            foreach (var name in MultiWordNames) padded = padded.Replace(" " + name + " ", " ");
            normalized = padded.Trim();
        }

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

        if (string.IsNullOrWhiteSpace(rawSpeech)) return EmptyResult();

        System.Text.RegularExpressions.Match marker = null;
        foreach (System.Text.RegularExpressions.Match m in CorrectionMarker.Matches(rawSpeech)) marker = m;

        if (marker == null) return ParseClause(rawSpeech);

        string before = rawSpeech.Substring(0, marker.Index);
        string after = LeadingNo.Replace(rawSpeech.Substring(marker.Index + marker.Length), "");
        bool belay = BelayMarkers.Contains(marker.Groups["m"].Value.ToLowerInvariant());

        Result afterResult = ParseClause(after);
        if (afterResult.Matches.Count > 0 || afterResult.NeedsClarification || afterResult.OrderInQuestion)
        {
            if (afterResult.AddressedCrew == null) afterResult.AddressedCrew = FindAddressedCrew(Normalize(rawSpeech));
            return afterResult;
        }

        // Nothing actionable after the marker: it cancels what came before it.
        Result beforeResult = ParseClause(before);
        var result = EmptyResult();
        result.Normalized = Normalize(rawSpeech);
        result.AddressedCrew = afterResult.AddressedCrew ?? beforeResult.AddressedCrew;

        if (beforeResult.HasConfidentOrder || beforeResult.NeedsClarification)
        {
            result.NoChange = true;            // "hard to port, no" / "full ahead, belay that"
        }
        else if (belay)
        {
            result.Matches.Add(new Match       // "belay that" on its own: undo the last order
            {
                Action = ShipAction.Belay,
                Confidence = 1f,
                Phrase = marker.Groups["m"].Value,
                FirstToken = -1,
                LastToken = -1,
            });
        }
        else
        {
            result.NoChange = true;            // "No." / "Negative."
        }

        return result;
    }

    private static Result EmptyResult()
    {
        return new Result
        {
            Matches = new List<Match>(MaxCompoundActions),
            AddressedCrew = null,
            Normalized = string.Empty,
        };
    }

    private static RadioChannel? FindAddressedCrew(string normalized)
    {
        var rawTokens = Tokenize(normalized, stripCrewNames: false);
        foreach (var t in rawTokens)
        {
            if (_lookoutNames.Contains(t)) return RadioChannel.Lookout;
            if (_engineerNames.Contains(t)) return RadioChannel.ChiefEngineer;
        }

        // "look out" and "engine room" arrive as two tokens — catch those too.
        if (normalized.Contains("look out")) return RadioChannel.Lookout;
        if (normalized.Contains("engine room")) return RadioChannel.ChiefEngineer;
        return null;
    }

    /// <summary>Per-utterance token data shared by the scoring passes.</summary>
    private sealed class Clause
    {
        public readonly List<string> Tokens = new List<string>(16);
        public readonly List<bool> InQuestion = new List<bool>(16);
        public readonly List<int> ClauseId = new List<int>(16);
        public readonly List<int> SentenceId = new List<int>(16);
        public bool[] Negated;
    }

    private static Result ParseClause(string raw)
    {
        var result = EmptyResult();
        if (string.IsNullOrWhiteSpace(raw)) return result;

        string wholeNormalized = Normalize(raw);
        result.Normalized = wholeNormalized;
        result.AddressedCrew = FindAddressedCrew(wholeNormalized);

        var clause = new Clause();
        int clauseCounter = 0, sentenceCounter = 0;
        bool anyQuestionSentence = false;

        foreach (System.Text.RegularExpressions.Match s in SentenceSplit.Matches(raw))
        {
            string sentence = s.Value;
            bool question = IsQuestionSentence(sentence);
            anyQuestionSentence |= question;

            foreach (string part in sentence.Split(ClauseSeparators))
            {
                var tokens = Tokenize(Normalize(part), stripCrewNames: true);
                foreach (var t in tokens)
                {
                    // Shouted repeats count once past the second: "left, left, left!" is "left left"
                    // (which still reads as one order, and "go go" keeps meaning full ahead).
                    int n = clause.Tokens.Count;
                    if (n >= 2 && clause.Tokens[n - 1] == t && clause.Tokens[n - 2] == t) continue;
                    clause.Tokens.Add(t);
                    clause.InQuestion.Add(question);
                    clause.ClauseId.Add(clauseCounter);
                    clause.SentenceId.Add(sentenceCounter);
                }
                clauseCounter++;
            }
            sentenceCounter++;
        }

        var toks = clause.Tokens;

        // Player said only a name: "Finn?" — treat as a request for that crew's report.
        if (toks.Count == 0)
        {
            result.WasNameOnly = result.AddressedCrew != null;
            result.WasFillerOnly = !result.WasNameOnly && HasLetters(raw);
            return result;
        }

        result.LooksLikeQuestion = anyQuestionSentence;
        foreach (var t in toks)
        {
            if (CommandVocabulary.QuestionWords.Contains(t)) { result.LooksLikeQuestion = true; break; }
        }

        MarkNegations(clause);

        // ------------------------------------------------------------------
        // Contradictions and vetoes, decided before any order is picked.
        // ------------------------------------------------------------------
        bool asternSpoken = FamilyEligible(clause, a => a.IsAsternGear());
        bool aheadWordSpoken = false;
        for (int i = 0; i < toks.Count; i++)
        {
            if ((toks[i] == "ahead" || toks[i] == "forward") && !clause.InQuestion[i] && !clause.Negated[i])
            {
                aheadWordSpoken = true;
                break;
            }
        }

        result.DirectionConflict = asternSpoken && aheadWordSpoken && FamilyEligible(clause, a => a.IsAheadGear());
        result.SideConflict = FamilyEligible(clause, a => a.IsPortTurn()) && FamilyEligible(clause, a => a.IsStarboardTurn());

        // ------------------------------------------------------------------
        // Greedy multi-match: pull out the best order, consume its words, repeat.
        // Handles "full ahead and hard to port" as two actions in one breath.
        // ------------------------------------------------------------------
        var used = new bool[toks.Count];
        var consumed = new List<int>(4);
        var bestConsumed = new List<int>(4);
        bool droppedForQuestion = false;
        bool droppedForNegation = false;

        // At most one order per family. "go fast and turn left" must yield one engine
        // order and one helm order, never two competing speeds.
        var claimedCategories = new HashSet<ActionCategory>();

        // "Turn back" / "U-turn" name no side. Claim their words first so "back" can't
        // become an astern order, then ask which way unless a real turn is also given.
        bool sidelessHelm = ClaimSidelessHelm(clause, used);

        for (int pass = 0; pass < MaxCompoundActions; pass++)
        {
            float bestMean = 0f;
            float bestSum = 0f;
            int bestLast = -1;
            int bestLen = 0;
            int bestIdx = -1;
            bestConsumed.Clear();

            for (int c = 0; c < _compiled.Length; c++)
            {
                var cp = _compiled[c];
                if (claimedCategories.Contains(cp.Action.Category())) continue;
                if (cp.IsWeak && toks.Count > MaxTokensForWeakWord) continue;
                if (result.DirectionConflict && (cp.Action.IsAheadGear() || cp.Action.IsAsternGear())) continue;
                if (asternSpoken && cp.Action.IsAheadGear()) continue;
                if (result.SideConflict && (cp.Action.IsPortTurn() || cp.Action.IsStarboardTurn())) continue;

                float sum;
                float mean = ScorePhrase(toks, used, cp.Tokens, consumed, out sum);
                if (mean <= 0f) continue;
                mean *= cp.Weight;
                sum *= cp.Weight;

                if (cp.Action.IsMachinery() && TouchesQuestion(clause, consumed))
                {
                    droppedForQuestion |= mean >= AcceptHigh;
                    continue;
                }

                if (cp.Action.IsMachinery() && !cp.StartsNegated && TouchesNegation(clause, consumed))
                {
                    droppedForNegation |= mean >= AcceptHigh;
                    continue;
                }

                // Most evidence wins; on a tie the later words win (people correct themselves
                // at the end of a sentence), then the more specific (longer) phrase.
                int last = consumed[consumed.Count - 1];
                bool better = sum > bestSum + 0.0001f ||
                              (Mathf.Abs(sum - bestSum) <= 0.0001f &&
                               (last > bestLast || (last == bestLast && cp.Tokens.Length > bestLen)));
                if (!better) continue;

                bestMean = mean;
                bestSum = sum;
                bestLast = last;
                bestLen = cp.Tokens.Length;
                bestIdx = c;
                bestConsumed.Clear();
                bestConsumed.AddRange(consumed);
            }

            if (bestIdx < 0 || bestMean < AcceptLow) break;

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
                    Confidence = Mathf.Clamp01(bestMean),
                    Phrase = _compiled[bestIdx].Original,
                    FirstToken = bestConsumed[0],
                    LastToken = bestConsumed[bestConsumed.Count - 1],
                });

                claimedCategories.Add(_compiled[bestIdx].Action.Category());
            }

            for (int i = 0; i < bestConsumed.Count; i++) used[bestConsumed[i]] = true;

            // Nothing left to match against.
            bool anyFree = false;
            for (int i = 0; i < used.Length; i++) if (!used[i]) { anyFree = true; break; }
            if (!anyFree) break;
        }

        bool helmMatched = false;
        for (int i = 0; i < result.Matches.Count; i++) helmMatched |= result.Matches[i].Action.IsHelm();
        if (!helmMatched && sidelessHelm) result.SideConflict = true;

        if (result.Matches.Count == 0 && !result.NeedsClarification)
        {
            if (droppedForQuestion) result.OrderInQuestion = true;
            else if (droppedForNegation || HasLiveNegator(clause)) result.NoChange = true;   // "don't turn yet", "not yet"
        }

        result.Degrees = FindTurnDegrees(clause, result.Matches);

        // Best first.
        result.Matches.Sort((a, b) => b.Confidence.CompareTo(a.Confidence));
        return result;
    }

    private static bool HasLetters(string s)
    {
        foreach (char c in s) if (char.IsLetter(c)) return true;
        return false;
    }

    private static bool IsQuestionSentence(string sentence)
    {
        var words = Normalize(sentence).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        int first = 0;
        while (first < words.Length &&
               (LeadIns.Contains(words[first]) || _lookoutNames.Contains(words[first]) || _engineerNames.Contains(words[first])))
        {
            first++;
        }
        if (first >= words.Length) return false;

        // "Can you turn to port?" is how polite captains give orders.
        if (PoliteModals.Contains(words[first]) && first + 1 < words.Length &&
            (words[first + 1] == "you" || words[first + 1] == "u" || words[first + 1] == "ya"))
        {
            return false;
        }

        return sentence.TrimEnd().EndsWith("?") || Interrogatives.Contains(words[first]);
    }

    private static void MarkNegations(Clause clause)
    {
        var toks = clause.Tokens;
        clause.Negated = new bool[toks.Count];

        for (int i = 0; i < toks.Count; i++)
        {
            if (!Negators.Contains(toks[i])) continue;
            for (int j = i + 1; j <= i + NegationReach && j < toks.Count && clause.ClauseId[j] == clause.ClauseId[i]; j++)
            {
                clause.Negated[j] = true;
            }
        }
    }

    private static bool HasLiveNegator(Clause clause)
    {
        for (int i = 0; i < clause.Tokens.Count; i++)
        {
            if (Negators.Contains(clause.Tokens[i]) && !clause.InQuestion[i]) return true;
        }
        return false;
    }

    /// <summary>
    /// Finds an unnegated, non-question "hard over" / "turn around" and marks its words used.
    /// </summary>
    private static bool ClaimSidelessHelm(Clause clause, bool[] used)
    {
        var toks = clause.Tokens;

        // "Hard over!" reduces to a lone "hard"; "hard left" and "hard astern" are real orders.
        if (toks.Count == 1 && toks[0] == "hard" && !clause.InQuestion[0] && !clause.Negated[0])
        {
            used[0] = true;
            return true;
        }

        foreach (var phrase in SidelessHelm)
        {
            for (int start = 0; start + phrase.Length <= toks.Count; start++)
            {
                bool hit = true;
                for (int k = 0; k < phrase.Length && hit; k++)
                {
                    int i = start + k;
                    hit = toks[i] == phrase[k] && !used[i] && !clause.InQuestion[i] && !clause.Negated[i];
                }
                if (hit)
                {
                    for (int k = 0; k < phrase.Length; k++) used[start + k] = true;
                    return true;
                }
            }
        }
        return false;
    }

    private static bool TouchesQuestion(Clause clause, List<int> consumed)
    {
        for (int i = 0; i < consumed.Count; i++) if (clause.InQuestion[consumed[i]]) return true;
        return false;
    }

    private static bool TouchesNegation(Clause clause, List<int> consumed)
    {
        for (int i = 0; i < consumed.Count; i++) if (clause.Negated[consumed[i]]) return true;
        return false;
    }

    /// <summary>True if any phrase of this family would be accepted as a live order in the utterance.</summary>
    private static bool FamilyEligible(Clause clause, Func<ShipAction, bool> family)
    {
        var toks = clause.Tokens;
        var used = new bool[toks.Count];
        var consumed = new List<int>(4);

        for (int c = 0; c < _compiled.Length; c++)
        {
            var cp = _compiled[c];
            if (!family(cp.Action)) continue;
            if (cp.IsWeak && toks.Count > MaxTokensForWeakWord) continue;

            float sum;
            float mean = ScorePhrase(toks, used, cp.Tokens, consumed, out sum) * cp.Weight;
            if (mean < AcceptHigh) continue;
            if (cp.Action.IsMachinery() && TouchesQuestion(clause, consumed)) continue;
            if (cp.Action.IsMachinery() && !cp.StartsNegated && TouchesNegation(clause, consumed)) continue;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Score one phrase against the utterance. Every word of the phrase must be
    /// present, in order, among the not-yet-consumed tokens, with at most
    /// <see cref="MaxGap"/> other words between neighbours — otherwise the phrase fails
    /// outright. Returns the mean per-word quality; <paramref name="sum"/> is the total.
    /// </summary>
    private static float ScorePhrase(List<string> utter, bool[] used, string[] phrase, List<int> consumed, out float sum)
    {
        consumed.Clear();
        sum = 0f;

        int cursor = 0;
        int previous = -1;

        for (int p = 0; p < phrase.Length; p++)
        {
            float best = 0f;
            int bestIdx = -1;

            int end = previous < 0 ? utter.Count - 1 : Mathf.Min(utter.Count - 1, previous + 1 + MaxGap);
            for (int i = cursor; i <= end; i++)
            {
                if (used[i]) continue;

                float s;
                if (!WordsMatch(utter[i], phrase[p], out s)) continue;

                if (previous >= 0) s -= GapPenalty * (i - previous - 1);
                if (s > best) { best = s; bestIdx = i; }
                if (s >= 1f) break; // exact adjacent hit, stop looking
            }

            if (bestIdx < 0)
            {
                sum = 0f;
                return 0f;
            }

            sum += best;
            consumed.Add(bestIdx);
            previous = bestIdx;
            cursor = bestIdx + 1;
        }

        return sum / phrase.Length;
    }

    /// <summary>
    /// True if a heard word counts as a phrase word. A real word is never heard as a
    /// different word: "fuel" is not "full", "heard" is not "hard", "have" is not "half".
    /// Fuzzy matching is kept for non-words the recogniser invents ("starbord", "astarn").
    /// </summary>
    private static bool WordsMatch(string heard, string phraseWord, out float score)
    {
        if (heard == phraseWord)
        {
            score = 1f;
            return true;
        }

        score = 0f;
        if (_vocabularyWords.Contains(heard) || CommonWordGuard.Contains(heard)) return false;
        if (heard.Length < MinFuzzyLength || phraseWord.Length < MinFuzzyLength) return false;

        // Everyday words are only ever heard exactly: "tight" is not "right".
        if (WeakWords.Contains(phraseWord)) return false;

        int lengthDiff = Mathf.Abs(heard.Length - phraseWord.Length);
        if (lengthDiff > 2) return false;

        float threshold = Phonetics.TokenThreshold(heard, phraseWord);
        float raw = Phonetics.Similarity(heard, phraseWord);
        if (raw >= threshold)
        {
            score = raw;
            return true;
        }

        // Past the spelling check, only an identical consonant skeleton of nearly equal
        // length counts ("starbird" = "starboard"). "steer" is not "faster", "bring" is not "bearing".
        if (lengthDiff <= 1 && Phonetics.Skeleton(heard) == Phonetics.Skeleton(phraseWord))
        {
            score = threshold;
            return true;
        }
        return false;
    }

    // ------------------------------------------------------------------
    // Numbers
    // ------------------------------------------------------------------
    private static readonly Dictionary<string, int> Units = new Dictionary<string, int>
    {
        {"zero", 0}, {"oh", 0}, {"one", 1}, {"two", 2}, {"three", 3}, {"four", 4}, {"five", 5},
        {"six", 6}, {"seven", 7}, {"eight", 8}, {"nine", 9},
    };

    private static readonly Dictionary<string, int> Teens = new Dictionary<string, int>
    {
        {"ten", 10}, {"eleven", 11}, {"twelve", 12}, {"thirteen", 13}, {"fourteen", 14},
        {"fifteen", 15}, {"sixteen", 16}, {"seventeen", 17}, {"eighteen", 18}, {"nineteen", 19},
    };

    private static readonly Dictionary<string, int> Tens = new Dictionary<string, int>
    {
        {"twenty", 20}, {"thirty", 30}, {"forty", 40}, {"fourty", 40}, {"fifty", 50},
        {"sixty", 60}, {"seventy", 70}, {"eighty", 80}, {"ninety", 90},
    };

    /// <summary>
    /// Reads one number starting at a token: "20", "twenty", "twenty five",
    /// "fifteen", or digit by digit ("one five" = 15, "two seven zero" = 270).
    /// </summary>
    private static bool TryReadNumber(List<string> toks, int start, out int value, out int length)
    {
        value = 0;
        length = 0;
        if (start < 0 || start >= toks.Count) return false;

        string t = toks[start];
        int digits;
        if (int.TryParse(t, out digits))
        {
            value = digits;
            length = 1;
            return true;
        }

        int n;
        if (Teens.TryGetValue(t, out n))
        {
            value = n;
            length = 1;
            return true;
        }

        if (Tens.TryGetValue(t, out n))
        {
            value = n;
            length = 1;
            int unit;
            if (start + 1 < toks.Count && Units.TryGetValue(toks[start + 1], out unit) && unit > 0)
            {
                value += unit;
                length = 2;
            }
            return true;
        }

        if (Units.TryGetValue(t, out n))
        {
            value = n;
            length = 1;
            int next;
            while (start + length < toks.Count && Units.TryGetValue(toks[start + length], out next))
            {
                value = value * 10 + next;
                length++;
            }
            return true;
        }

        return false;
    }

    /// <summary>
    /// The turn size belongs to the helm order it was spoken with: right after it
    /// ("port twenty", "starboard one five"), or before it with "degrees"
    /// ("ten degrees to port"). A number anywhere else is not a turn.
    /// </summary>
    private static float? FindTurnDegrees(Clause clause, List<Match> matches)
    {
        var toks = clause.Tokens;

        for (int m = 0; m < matches.Count; m++)
        {
            var action = matches[m].Action;
            if (action != ShipAction.TurnPort && action != ShipAction.TurnStarboard) continue;

            int last = matches[m].LastToken;
            for (int i = last + 1; i <= last + 2 && i < toks.Count; i++)
            {
                if (clause.SentenceId[i] != clause.SentenceId[last]) break;

                int value, length;
                if (TryReadNumber(toks, i, out value, out length)) return ValidTurn(value);
            }

            int first = matches[m].FirstToken;
            if (first >= 2 && IsDegreesWord(toks[first - 1]))
            {
                for (int start = first - 2; start >= 0 && start >= first - 3; start--)
                {
                    int value, length;
                    if (TryReadNumber(toks, start, out value, out length) && start + length == first - 1)
                    {
                        return ValidTurn(value);
                    }
                }
            }
        }

        return null;
    }

    private static bool IsDegreesWord(string t)
    {
        return t == "degrees" || t == "degree" || t == "deg";
    }

    private static float? ValidTurn(int value)
    {
        if (value < MinTurnDegrees || value > MaxTurnDegrees) return null;
        return value;
    }

    /// <summary>
    /// First turn-sized number in free text, for orders the classifier resolved.
    /// Whole words only, so "everyone" is not "one".
    /// </summary>
    public static float? ExtractDegrees(string text)
    {
        EnsureCompiled();
        if (string.IsNullOrWhiteSpace(text)) return null;

        var toks = new List<string>(Normalize(text).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
        for (int i = 0; i < toks.Count; i++)
        {
            int value, length;
            if (!TryReadNumber(toks, i, out value, out length)) continue;

            float? turn = ValidTurn(value);
            if (turn.HasValue) return turn;
            i += length - 1;
        }
        return null;
    }

    /// <summary>Degrees a plain "left"/"right" turns the ship.</summary>
    public const float PlainTurnDegrees = 30f;
    /// <summary>Degrees a "hard left"/"hard right" turns the ship.</summary>
    public const float HardTurnDegrees = 60f;
    /// <summary>Rudder angle the helmsman uses for a plain turn (hard turns use full rudder).</summary>
    public const float StandardRudder = 20f;

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

            // Every turn is a heading change, so the same words turn the same amount at any
            // speed: "left" = 30 degrees on a standard rudder, "hard left" = 60 on full rudder,
            // "left twenty" = exactly 20.
            case ShipAction.TurnPort:
                nav.ChangeHeading(-(explicitDegrees ?? PlainTurnDegrees), explicitDegrees.HasValue ? 0f : StandardRudder);
                return true;
            case ShipAction.TurnStarboard:
                nav.ChangeHeading(explicitDegrees ?? PlainTurnDegrees, explicitDegrees.HasValue ? 0f : StandardRudder);
                return true;
            case ShipAction.HardPort:         nav.ChangeHeading(-HardTurnDegrees); return true;
            case ShipAction.HardStarboard:    nav.ChangeHeading(HardTurnDegrees);  return true;
            case ShipAction.RudderMidships:   nav.RudderMidships(); return true;

            // Belay needs the previous order, which only the router remembers.
            default: return false;
        }
    }
}
