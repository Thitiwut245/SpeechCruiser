using System.Collections.Generic;

/// <summary>
/// The closed phrase table for every order the player can give.
///
/// Two layers of tolerance:
///  1. Explicit aliases — real mishears observed from Whisper ("pork", "starbird",
///     "frank speed"). These match exactly, so they cost nothing and never misfire.
///  2. Fuzzy fallback in <see cref="Phonetics"/> — only for words of 5+ letters that are
///     not themselves in this table, so "fuel" never becomes "full". Short mishears
///     must be listed as aliases.
///
/// The phrase covering the most words wins, so "full astern" beats "full", and
/// "hard to port" beats "port". Add new wordings here rather than in any prompt.
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
        "thanks", "yeah", "yep", "wait", "standby",
        // beginners name the vessel: "turn the boat to the right"
        "ship", "boat",
    };

    /// <summary>
    /// Multi-word filler phrases, removed before single-token fillers. Affirmations live
    /// here so "that's right" is agreement, not a starboard order.
    /// </summary>
    public static readonly string[] FillerPhrases =
    {
        "all right", "alright", "very well", "aye aye", "if you please",
        "thats right", "that is right", "youre right", "you are right",
        "thats correct", "that is correct", "go on", "thank you",
        "no problem", "no worries", "roger that", "copy that", "got it", "understood",
        "hold on", "stand by", "one moment", "one second",
        "keep going", "carry on", "continue",
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
            "all stopped",
            // plain English for beginners
            "brake", "brakes", "break", "hit the brakes", "use the brakes", "stop the boat",
            "dont move", "do not move", "stay", "stay here", "stay still", "keep still",
            "hold position", "stop moving"),

        new Entry(ShipAction.EngineDeadSlow,
            "dead slow", "dead slow ahead", "creep", "creep ahead", "crawl",
            "dead slow speed", "bare steerage"),

        new Entry(ShipAction.EngineSlow,
            "slow ahead", "ahead slow", "slow speed", "start engine", "start the engine",
            "start engines", "start", "go", "move", "move out", "depart", "get underway",
            "underway", "begin", "engage", "ahead", "take us out", "cast off",
            "engine is start",
            // plain English for beginners
            "forward", "go forward", "move forward", "slow", "slowly", "go slow", "go slowly"),

        new Entry(ShipAction.EngineHalf,
            "half ahead", "ahead half", "half speed", "half", "medium speed", "medium",
            "half throttle"),

        new Entry(ShipAction.EngineFull,
            "full ahead", "ahead full", "full speed", "all ahead full", "full throttle",
            "full", "fast", "ahead fool", "full head", "top speed", "go go", "full power",
            // accented "full ahead" as Whisper writes it
            "for ahead", "four ahead", "fool ahead", "fall ahead", "full a head"),

        new Entry(ShipAction.EngineFlank,
            "flank speed", "flank", "ahead flank", "emergency speed", "maximum speed",
            "max power", "maximum power", "emergency full", "frank speed", "flank ahead",
            "all she has", "everything you have", "max speed", "maximum", "max"),

        // The telegraph has two astern gears, so every slower astern order lands on SLOW ASTERN.
        new Entry(ShipAction.EngineSlowAstern,
            "slow astern", "astern", "reverse", "back up", "go back", "back her down",
            "back astern", "engine astern", "reverse engine", "back", "go astern",
            "backward", "backwards", "go backward", "go backwards", "move backward",
            "half astern", "half speed astern", "dead slow astern", "slow stern", "half stern",
            "back slowly"),

        new Entry(ShipAction.EngineFullAstern,
            "full astern", "emergency astern", "crash stop", "full reverse",
            "all astern full", "hard astern", "full speed astern", "emergency full astern",
            "all astern", "emergency reverse", "full back", "back full", "go back fast",
            "backward fast", "backwards fast", "reverse fast", "brake hard", "emergency brake",
            // Whisper splits "astern": "full a stern" loses its filler "a"
            "full stern"),

        // ---------------- Engine: relative shifts ----------------
        new Entry(ShipAction.EngineIncrease,
            "faster", "speed up", "more speed", "increase speed", "more power",
            "give me more", "pick up speed", "accelerate", "up one", "more", "more fast",
            "go faster"),

        new Entry(ShipAction.EngineDecrease,
            "slower", "slow down", "reduce speed", "less speed", "ease off",
            "throttle back", "decrease speed", "less power", "down one", "less",
            "ease her back", "not so fast", "not too fast", "too fast", "slow it down",
            "less fast", "not fast", "more slow", "more slowly", "speed down", "slow more"),

        // ---------------- Helm ----------------
        new Entry(ShipAction.TurnPort,
            "turn port", "port", "to port", "come port", "port side", "turn left",
            "left", "left rudder", "port rudder", "helm to port", "come left",
            // "more" and "go" belong to the turn here, not to the engine
            "more left", "more port", "go left", "go port", "go to port", "portside",
            "left more", "port more", "left side", "to left",
            // observed Whisper mishears for "port"
            "part", "pork", "park", "pot", "pourt", "poured", "sport"),

        new Entry(ShipAction.TurnStarboard,
            "turn starboard", "starboard", "to starboard", "come starboard",
            "starboard side", "turn right", "right", "right rudder", "starboard rudder",
            "helm to starboard", "come right",
            "more right", "more starboard", "go right", "go starboard", "go to starboard",
            "right more", "starboard more", "right side", "to right",
            // observed Whisper mishears for "starboard"
            "star board", "starbird", "star bird", "star bored", "starbord", "starboared"),

        new Entry(ShipAction.HardPort,
            "hard port", "hard to port", "hard a port", "hard left", "hard over port", "full left", "full port",
            "full port rudder", "hard aport", "hard to part", "hard to pork"),

        new Entry(ShipAction.HardStarboard,
            "hard starboard", "hard to starboard", "hard a starboard", "hard right", "full right", "full starboard", "hard light",
            "hard over starboard", "full starboard rudder", "hard to star board"),

        new Entry(ShipAction.RudderMidships,
            "midships", "amidships", "rudder amidships", "rudder midships", "steady",
            "steady as she goes", "steady on", "hold course", "hold your course",
            "center rudder", "centre rudder", "straight", "straighten out",
            "mid ship", "mid ships", "middle", "center", "centre",
            // "stop turning" is a helm order, not an engine stop
            "stop turning", "stop the turn", "stop swinging", "stop the swing",
            "straighten up", "straighten her out", "steady her", "hold her steady",
            "keep her straight", "keep straight", "keep it straight", "go straight",
            "hold her straight",
            // easing the helm takes rudder off; "less" here is not an engine order
            "less rudder", "ease the rudder", "ease the helm", "ease your helm"),

        // ---------------- Queries ----------------
        new Entry(ShipAction.QueryHazard,
            "what do you see", "what can you see", "anything ahead", "any hazards",
            "how close", "distance to reef", "whats ahead", "what is ahead",
            // "how far" alone asks about the dock; with a hazard named it asks about the hazard
            "how far is reef", "how far reef", "how far is rock", "how far rock", "how far is rocks",
            "how far is mine", "how far mine", "how far is hazard", "how far is danger",
            "is it clear", "any reef", "any rocks", "see anything", "report ahead",
            "eyes ahead", "clear ahead", "what you see",
            // naming a hazard asks about it: "is there a reef ahead?"
            "reef", "reefs", "rock", "rocks", "mine", "mines", "hazard", "hazards",
            "reef ahead", "rocks ahead", "mine ahead", "mines ahead", "hazard ahead",
            "anything there", "is it safe", "all clear", "is clear", "clear",
            "danger", "dangerous", "in front", "what is in front"),

        // "the port" is normalised to "harbour" before matching, so "where is the port?"
        // asks the way instead of turning left.
        new Entry(ShipAction.QueryDestination,
            "harbour", "harbor", "dock", "destination", "where are we going",
            "where are we headed", "how far to go", "how much further", "how much farther",
            "which way", "where to", "how far", "into port", "in to port", "make for port",
            "head for port", "back to port", "our destination", "where is harbour",
            "how far to harbour", "how far to dock", "how far is dock", "distance to dock",
            "distance to harbour", "where we go", "where go", "finish", "finish line",
            "goal", "where is end", "the end", "where is finish", "where is goal"),

        new Entry(ShipAction.QueryFuel,
            "fuel status", "how much fuel", "fuel", "fuel level", "how is fuel",
            "fuel remaining", "bunker status", "how much fuel left"),

        new Entry(ShipAction.QueryHeading,
            "what is our heading", "our heading", "heading", "what course",
            "our course", "which way are we going", "bearing", "what bearing",
            "where are we pointed"),

        new Entry(ShipAction.QuerySpeed,
            "what is our speed", "how fast are we going", "our speed", "current speed",
            "how fast", "speed check", "what speed", "speed"),

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
