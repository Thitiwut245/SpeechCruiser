using System.Collections.Generic;

/// <summary>
/// The closed phrase table for every order the player can give.
///
/// Two layers of tolerance:
///  1. Explicit aliases — real mishears observed from Whisper ("pork", "starbird",
///     "frank speed"). These match exactly, so they cost nothing and never misfire.
///  2. Fuzzy fallback in <see cref="Phonetics"/> — catches variants nobody listed.
///
/// Longer phrases win ties, so "full astern" beats "full", and "hard to port" beats
/// "port". Add new wordings here rather than in any prompt.
/// </summary>
public static class CommandVocabulary
{
    public struct Entry
    {
        public ShipAction Action;
        public string[] Phrases;

        public Entry(ShipAction action, params string[] phrases)
        {
            Action = action;
            Phrases = phrases;
        }
    }

    /// <summary>
    /// Filler the player says around the actual order. Stripped before matching so
    /// "all right, turn to port now sir" reduces to "turn to port".
    /// Note "all right" is stripped as a unit — otherwise its "right" would read as STARBOARD.
    /// </summary>
    public static readonly string[] Fillers =
    {
        "please", "now", "sir", "captain", "over", "roger", "copy", "okay", "ok",
        "uh", "um", "er", "hey", "hello", "well", "just", "the", "a", "we", "lets",
        "let", "us", "you", "i", "want", "need", "would", "like", "can", "could",
        "and", "then", "also", "some", "very", "really", "bit", "little",
    };

    /// <summary>Multi-word filler phrases, removed before single-token fillers.</summary>
    public static readonly string[] FillerPhrases =
    {
        "all right", "alright", "very well", "aye aye", "if you please",
    };

    /// <summary>Names that address a specific crew member rather than forming part of an order.</summary>
    public static readonly string[] LookoutNames = { "finn", "fin", "fen", "lookout", "look out", "bow" };
    public static readonly string[] EngineerNames = { "harris", "chief", "engineer", "harrison", "haris", "engine room" };

    /// <summary>
    /// Ordered only for readability — matching considers every entry and picks the
    /// highest-scoring, longest phrase.
    /// </summary>
    public static readonly Entry[] Entries =
    {
        // ---------------- Engine: absolute gears ----------------
        new Entry(ShipAction.EngineStop,
            "stop", "all stop", "stop engine", "stop engines", "stop the engine",
            "halt", "cut engine", "cut the engine", "shut down", "shutdown",
            "kill engine", "kill the engine", "full stop", "engine stop", "stop her",
            "all stopped"),

        new Entry(ShipAction.EngineDeadSlow,
            "dead slow", "dead slow ahead", "creep", "creep ahead", "crawl",
            "dead slow speed", "bare steerage"),

        new Entry(ShipAction.EngineSlow,
            "slow ahead", "ahead slow", "slow speed", "start engine", "start the engine",
            "start engines", "start", "go", "move", "move out", "depart", "get underway",
            "underway", "begin", "engage", "ahead", "take us out", "cast off",
            "engine is start"),

        new Entry(ShipAction.EngineHalf,
            "half ahead", "ahead half", "half speed", "half", "medium speed", "medium",
            "half throttle"),

        new Entry(ShipAction.EngineFull,
            "full ahead", "ahead full", "full speed", "all ahead full", "full throttle",
            "full", "fast", "ahead fool", "full head", "top speed"),

        new Entry(ShipAction.EngineFlank,
            "flank speed", "flank", "ahead flank", "emergency speed", "maximum speed",
            "max power", "maximum power", "emergency full", "frank speed", "flank ahead",
            "all she has", "everything you have"),

        new Entry(ShipAction.EngineSlowAstern,
            "slow astern", "astern", "reverse", "back up", "go back", "back her down",
            "back astern", "engine astern", "reverse engine", "back"),

        new Entry(ShipAction.EngineFullAstern,
            "full astern", "emergency astern", "crash stop", "full reverse",
            "all astern full", "hard astern"),

        // ---------------- Engine: relative shifts ----------------
        new Entry(ShipAction.EngineIncrease,
            "faster", "speed up", "more speed", "increase speed", "more power",
            "give me more", "pick up speed", "accelerate", "up one", "more"),

        new Entry(ShipAction.EngineDecrease,
            "slower", "slow down", "reduce speed", "less speed", "ease off",
            "throttle back", "decrease speed", "less power", "down one", "less",
            "ease her back"),

        // ---------------- Helm ----------------
        new Entry(ShipAction.TurnPort,
            "turn port", "port", "to port", "come port", "port side", "turn left",
            "left", "left rudder", "port rudder", "helm to port", "come left",
            // observed Whisper mishears for "port"
            "part", "pork", "park", "pot", "pourt", "poured", "sport"),

        new Entry(ShipAction.TurnStarboard,
            "turn starboard", "starboard", "to starboard", "come starboard",
            "starboard side", "turn right", "right", "right rudder", "starboard rudder",
            "helm to starboard", "come right",
            // observed Whisper mishears for "starboard"
            "star board", "starbird", "star bird", "star bored", "starbord", "starboared"),

        new Entry(ShipAction.HardPort,
            "hard port", "hard to port", "hard a port", "hard left", "hard over port",
            "full port rudder", "hard aport", "hard to part", "hard to pork"),

        new Entry(ShipAction.HardStarboard,
            "hard starboard", "hard to starboard", "hard a starboard", "hard right",
            "hard over starboard", "full starboard rudder", "hard to star board"),

        new Entry(ShipAction.RudderMidships,
            "midships", "amidships", "rudder amidships", "rudder midships", "steady",
            "steady as she goes", "steady on", "hold course", "hold your course",
            "center rudder", "centre rudder", "straight", "straighten out",
            "mid ship", "mid ships", "middle", "center", "centre"),

        // ---------------- Queries ----------------
        new Entry(ShipAction.QueryHazard,
            "what do you see", "what can you see", "anything ahead", "any hazards",
            "how far", "how close", "distance to reef", "whats ahead", "what is ahead",
            "is it clear", "any reef", "any rocks", "see anything", "report ahead",
            "eyes ahead", "clear ahead", "what you see"),

        new Entry(ShipAction.QueryFuel,
            "fuel status", "how much fuel", "fuel", "fuel level", "how is fuel",
            "fuel remaining", "bunker status", "how much fuel left"),

        new Entry(ShipAction.QueryHeading,
            "what is our heading", "our heading", "heading", "what course",
            "our course", "which way are we going", "bearing", "what bearing",
            "where are we pointed"),

        new Entry(ShipAction.QuerySpeed,
            "what is our speed", "how fast are we going", "our speed", "current speed",
            "how fast", "speed check", "what speed"),

        new Entry(ShipAction.QueryStatus,
            "status", "status report", "report status", "report", "sitrep",
            "all systems", "engine status", "how are things", "situation",
            "give me a report", "whats our status"),
    };

    /// <summary>
    /// Words that appear in question phrasing. Used to decide whether an unmatched
    /// utterance should go to the conversation model rather than the classifier.
    /// </summary>
    public static readonly HashSet<string> QuestionWords = new HashSet<string>
    {
        "what", "why", "how", "who", "when", "where", "which", "whats", "hows",
        "do", "does", "did", "is", "are", "was", "were", "tell", "think", "feel",
        "believe", "remember", "story", "family", "home", "afraid", "scared",
    };
}
