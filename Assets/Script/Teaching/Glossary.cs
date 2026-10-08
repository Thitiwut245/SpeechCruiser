using System.Collections.Generic;
using System.Text.RegularExpressions;

/// <summary>
/// The crew as language helpers: "what does starboard mean?" gets a plain-English meaning and
/// the phrase to use it in; "how do I say slow down?" gets the standard radio order for it
/// (the words are run through the order parser, so anything the ship understands can be
/// asked about). Simple English throughout, for beginners. Pure logic, so it can be tested.
/// </summary>
public static class Glossary
{
    private struct Word
    {
        public string Meaning;
        public string Say; // a phrase that uses it, or null
        public Word(string meaning, string say) { Meaning = meaning; Say = say; }
    }

    private static readonly Dictionary<string, Word> Words = new Dictionary<string, Word>
    {
        { "port", new Word("Port means left, looking towards the front of the ship.", "port twenty") },
        { "starboard", new Word("Starboard means right, looking towards the front of the ship.", "starboard twenty") },
        { "ahead", new Word("Ahead means forward: the engine pushes the ship to the front.", "slow ahead") },
        { "astern", new Word("Astern means backwards: the engine pulls the ship back.", "slow astern") },
        { "midships", new Word("Midships means the rudder is straight, so the ship goes straight.", "midships") },
        { "amidships", new Word("Amidships means the rudder is straight, so the ship goes straight.", "midships") },
        { "hard a port", new Word("Hard a port means turn the rudder all the way left, for a sharp turn.", "hard a port") },
        { "hard a starboard", new Word("Hard a starboard means turn the rudder all the way right, for a sharp turn.", "hard a starboard") },
        { "rudder", new Word("The rudder is the flat blade at the back that turns the ship.", "midships") },
        { "helm", new Word("The helm is the wheel that moves the rudder. Finn is at the helm.", null) },
        { "bow", new Word("The bow is the front of the ship.", null) },
        { "stern", new Word("The stern is the back of the ship.", null) },
        { "knot", new Word("A knot is a speed: about two kilometres an hour.", "what is our speed?") },
        { "knots", new Word("Knots are speed at sea: one knot is about two kilometres an hour.", "what is our speed?") },
        { "telegraph", new Word("The telegraph sends your speed order to the engine room.", "half ahead") },
        { "dead slow", new Word("Dead slow is the slowest speed going forward.", "dead slow ahead") },
        { "flank", new Word("Flank speed is the fastest the engine can go.", "flank speed") },
        { "flank speed", new Word("Flank speed is the fastest the engine can go.", "flank speed") },
        { "heading", new Word("The heading is the direction the ship points, like north or east.", "what is our heading?") },
        { "bearing", new Word("A bearing is the direction to something, as a compass number.", null) },
        { "course", new Word("The course is the way the ship is going.", null) },
        { "berth", new Word("A berth is the parking place for a ship at the quay.", "request docking") },
        { "quay", new Word("A quay is the stone edge of the port where ships tie up. Say it like \"key\".", "request docking") },
        { "dock", new Word("The dock is the port where we unload the cargo.", "where is the dock?") },
        { "make fast", new Word("Make fast means tie the ship to the quay with ropes.", "make fast") },
        { "pilot", new Word("A pilot is a harbour expert who brings ships to the berth.", "request docking") },
        { "reef", new Word("A reef is rock or coral just under the water. It breaks the hull.", "any danger?") },
        { "shoal", new Word("A shoal is shallow sand under the water. The ship gets stuck on it.", "any danger?") },
        { "sandbank", new Word("A sandbank is shallow sand under the water. The ship gets stuck on it.", "any danger?") },
        { "shallows", new Word("The shallows are water that is not deep enough for the ship.", "any danger?") },
        { "mine", new Word("A sea mine is a floating bomb. Steer well away from it.", "any danger?") },
        { "hazard", new Word("A hazard is anything dangerous in the water.", "any danger?") },
        { "hull", new Word("The hull is the body of the ship, the part in the water.", "status report") },
        { "lookout", new Word("The lookout watches the sea for danger. That's Finn.", "any danger?") },
        { "belay", new Word("Belay that means cancel the last order.", "belay that") },
        { "belay that", new Word("Belay that means cancel the last order.", "belay that") },
        { "aye", new Word("Aye means yes, I understand and I will do it.", null) },
        { "over", new Word("Over means I have finished talking, now you speak.", null) },
        { "say again", new Word("Say again means please repeat what you said.", null) },
        { "cargo", new Word("Cargo is what the ship carries. Ours is containers.", null) },
        { "fender", new Word("A fender is a soft bumper between the ship and the quay.", null) },
        { "bollard", new Word("A bollard is a short post on the quay for the ropes.", "make fast") },
        { "captain", new Word("The captain is the person in charge of the ship. That's you!", null) },
        { "engineer", new Word("The engineer runs the engine. That's Harris.", "status report") },
    };

    private const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private static readonly Regex[] MeaningPatterns =
    {
        // "what's" arrives as "whats" once the apostrophe is gone
        new Regex(@"^(?:\w+ )?what(?: does| do| is|s) (?:the word )?(?<w>[a-z ]+?) mean$", Opt),
        new Regex(@"^(?:\w+ )?what(?: is|s) (?:the )?meaning of (?:the word )?(?<w>[a-z ]+)$", Opt),
        new Regex(@"^(?:\w+ )?(?:define|explain) (?:the word )?(?<w>[a-z ]+)$", Opt),
        new Regex(@"^(?:\w+ )?what(?: is|s| are) (?:a |an |the )?(?<w>[a-z ]+)$", Opt), // only bare glossary words
    };
    private static readonly Regex[] HowToSayPatterns =
    {
        new Regex(@"^(?:\w+ )?how (?:do|can|should|would) (?:i|we) (?:say|tell (?:you|him|them|finn|harris) to|order (?:you |him |them )?to|ask (?:you |him |them )?to|ask for|ask) (?<x>.+)$", Opt),
        new Regex(@"^(?:\w+ )?what (?:do|should|can) (?:i|we) say (?:to|for|if i want to|when i want to|when) (?<x>.+)$", Opt),
        new Regex(@"^(?:\w+ )?how (?:to|do you) say (?<x>.+)$", Opt),
        new Regex(@"^(?:\w+ )?what is the (?:order|command|word|phrase) (?:for|to) (?<x>.+)$", Opt),
        // "how do I stop the ship?": a beginner asking for the order itself
        new Regex(@"^(?:\w+ )?how (?:do|can|should) (?:i|we) (?<x>(?:turn|go|stop|slow|speed|reverse|back|dock|park|steer|move|cancel|make|get) .+|stop|slow down|speed up|reverse|dock|park)$", Opt),
    };

    /// <summary>
    /// If <paramref name="speech"/> asks about a word or how to say something, the crew's
    /// answer (<paramref name="lines"/>); false otherwise (so orders are never caught here).
    /// </summary>
    public static bool TryAnswer(RadioChannel crew, string speech, out string[] lines)
    {
        lines = null;
        string s = Normalise(speech);
        if (s.Length == 0) return false;
        string you = crew == RadioChannel.Lookout ? "Sir" : "Captain";

        foreach (var re in HowToSayPatterns)
        {
            var m = re.Match(s);
            if (!m.Success) continue;
            string x = m.Groups["x"].Value.Trim();
            string phrase = PhraseFor(x);
            lines = phrase != null
                ? new[] { "Say \"" + phrase + "\", " + you + ". Over.", "The order is \"" + phrase + "\", " + you + ". Over." }
                : new[] { "Not sure there's an order for that, " + you + ". Press F1 for the radio card. Over." };
            return true;
        }

        for (int i = 0; i < MeaningPatterns.Length; i++)
        {
            var m = MeaningPatterns[i].Match(s);
            if (!m.Success) continue;
            string raw = m.Groups["w"].Value.Trim();
            // "what does that mean?", "what is our heading?": not a word to look up.
            if (Regex.IsMatch(raw, @"^(?:it|that|this|you|he|she|they|our|my|your|their|his|her)\b")) return false;
            string w = Strip(raw);
            if (!Words.TryGetValue(w, out var word))
            {
                if (i == MeaningPatterns.Length - 1) continue; // "what is our heading?" is a question, not a word
                lines = new[] { "Don't know that word, " + you + ". Ask me about port, starboard, ahead or astern. Over." };
                return true;
            }
            lines = new[] { word.Meaning + (word.Say != null ? " Say \"" + word.Say + "\"." : "") + " Over." };
            return true;
        }
        return false;
    }

    /// <summary>The standard phrase for what <paramref name="x"/> asks the ship to do ("slow down" → "reduce speed").</summary>
    public static string PhraseFor(string x)
    {
        x = Strip(x);
        if (Words.TryGetValue(x, out var word) && word.Say != null) return word.Say;
        // Before the parser, which hears "park" as "port".
        if (Regex.IsMatch(x, @"\b(park|dock|berth|moor|tie up|arrive)\b")) return "request docking";
        if (Regex.IsMatch(x, @"\b(cancel|undo|forget)\b")) return "belay that";
        var parsed = CommandParser.Parse(x);
        foreach (var m in parsed.Matches)
        {
            if (m.Confidence < CommandParser.AcceptHigh) continue;
            string p = StandardPhrases.For(m.Action, parsed.Degrees);
            if (p != null) return p;
        }
        return null;
    }

    private static string Strip(string w)
    {
        w = Regex.Replace(w.Trim(), @"^(?:a|an|the|word|our|this|that) ", "");
        w = Regex.Replace(w, @" (?:mean|means|please|sir|captain|in english)$", "");
        return w.Trim();
    }

    private static string Normalise(string speech)
    {
        if (string.IsNullOrWhiteSpace(speech)) return string.Empty;
        string s = speech.ToLowerInvariant().Replace("’", "'").Replace("'", "").Replace("-", " ");
        s = Regex.Replace(s, @"[^a-z0-9 ]", " ");
        s = Regex.Replace(s, @"\b(please|sir|captain|finn|harris|chief|hey|ok|okay|so|um|uh)\b", " ");
        return Regex.Replace(s, @"\s+", " ").Trim();
    }
}
