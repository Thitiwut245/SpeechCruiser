using System.Text.RegularExpressions;

/// <summary>
/// The crew's everyday talk, answered on the ship with no network: hello, how are you, who
/// are you, the weather and the sea, the cargo, thanks, jokes, home, food, being tired or
/// scared. Anything a sailor wouldn't know ("what's the capital of France?") gets an honest
/// "don't know" in character, steering back to the voyage.
///
/// The conversation model (CrewPersona) gives richer replies when it's online; this is what
/// answers when it isn't, when it fails, and for quick small talk on the order key. Every
/// fact about the ship comes from ShipTelemetry, so nothing here can be untrue.
/// </summary>
public static class SmallTalk
{
    public enum Topic
    {
        None, HowAreYou, Greeting, WhoAreYou, Weather, Cargo, Thanks, Sorry, Joke, Song, Home,
        Food, Tired, Scared, Robot, Insult, Bye, Time, Likes, Reply, Wildlife, OffTopic, Chat,
    }

    private const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    // In order: the first that matches wins, so "hi, how are you" is a how-are-you.
    private static readonly (Topic topic, Regex pattern)[] Topics =
    {
        // The Captain answering the crew's question back: "I'm fine", "I'm from Khon Kaen", "I like football".
        (Topic.Reply, new Regex(@"^(i am|im|i m) (fine|good|ok|okay|great|well|alright|all right|not bad|happy|very good|very well)\b|\b(i am|im|i m|i come) from\b|\bmy (home|hometown|home town|country|town|city) is\b|\bmy favou?rite \w+ is\b|^i (like|love|enjoy)\b", Opt)),
        (Topic.Robot, new Regex(@"\b(are|r) (you|u) (a |an )?(robot|ai|bot|machine|computer|real|human|person)\b", Opt)),
        (Topic.HowAreYou, new Regex(@"\b(how (are|r) (you|u|ya|things)|how you doing|how are you doing|hows it going|how is it going|how do you feel|how you feeling|are you (ok|okay|alright|all right|well)|you (ok|okay|alright|all right)|whats up|sup)\b", Opt)),
        (Topic.WhoAreYou, new Regex(@"\b(who (are|r) (you|u)|your name|whats your name|what do you do|whats your job|introduce yourself|tell me about yourself|about you)\b", Opt)),
        (Topic.Weather, new Regex(@"\b(weather|wind|windy|rain|raining|storm|stormy|fog|foggy|sunny|sun|sky|forecast|temperature|cold|hot|warm|waves|swell|sea state|hows the sea|how is the sea|the water like)\b", Opt)),
        (Topic.Wildlife, new Regex(@"\b(dolphins?|whales?|gulls?|seagulls?|sea gulls?|birds?|fish|fishes|animals?|sea life|wildlife|pod)\b", Opt)),
        (Topic.Cargo, new Regex(@"\b(cargo|containers?|carrying|freight|load|loaded)\b", Opt)),
        (Topic.Joke, new Regex(@"\b(joke|jokes|make me laugh|something funny|funny)\b", Opt)),
        (Topic.Song, new Regex(@"\b(sing|song|shanty|music)\b", Opt)),
        (Topic.Home, new Regex(@"\b(family|wife|kids|children|married|where (are|r) you from|hometown|home town|your home|miss home|grandma|grandmother|parents|mum|mom|dad)\b", Opt)),
        (Topic.Food, new Regex(@"\b(hungry|food|eat|eating|lunch|dinner|breakfast|supper|coffee|tea|snack|cook)\b", Opt)),
        (Topic.Tired, new Regex(@"\b(tired|sleepy|sleep|rest|exhausted|break)\b", Opt)),
        (Topic.Scared, new Regex(@"\b(scared|afraid|nervous|worried|frightened|fear)\b", Opt)),
        (Topic.Likes, new Regex(@"\b(do you (like|love|enjoy)|why did you (become|join|go to sea)|why (are|r) you a sailor|favou?rite|hobby|hobbies)\b", Opt)),
        (Topic.Time, new Regex(@"\b(what time|time is it|what day|which day|todays date|what date|what year)\b", Opt)),
        (Topic.Thanks, new Regex(@"\b(thanks|thank you|thank u|cheers|good job|well done|nice work|great job|good work|nice one|good lad|good man|you are (great|the best|amazing)|youre (great|the best|amazing))\b", Opt)),
        (Topic.Sorry, new Regex(@"\b(sorry|my bad|apologi[sz]e|my mistake|oops)\b", Opt)),
        (Topic.Insult, new Regex(@"\b(stupid|idiot|useless|dumb|shut up|hate you|terrible|incompetent)\b", Opt)),
        (Topic.Bye, new Regex(@"\b(goodbye|good bye|bye bye|see you|see ya|over and out|signing off|good night|goodnight|talk later)\b", Opt)),
        (Topic.Greeting, new Regex(@"^\W*(hi|hello|hey|hiya|ahoy|howdy|yo|greetings|good (morning|afternoon|evening|day))\b", Opt)),
    };

    /// <summary>
    /// About the crew themselves, not the ship: these win over the parser's guess at a
    /// question's topic ("where are you from?" isn't about where the dock is).
    /// </summary>
    public static bool IsPersonal(Topic topic)
    {
        switch (topic)
        {
            case Topic.HowAreYou: case Topic.WhoAreYou: case Topic.Home: case Topic.Likes: case Topic.Robot:
            case Topic.Food: case Topic.Tired: case Topic.Joke: case Topic.Song: case Topic.Time:
                return true;
            default:
                return false;
        }
    }

    /// <summary>Which small-talk topic <paramref name="speech"/> is about (None if it isn't small talk).</summary>
    public static Topic Classify(string speech)
    {
        string s = Normalise(speech);
        if (s.Length == 0) return Topic.None;
        foreach (var (topic, pattern) in Topics)
            if (pattern.IsMatch(s)) return topic;
        return Topic.None;
    }

    /// <summary>
    /// The topic to answer: a small-talk topic, else OffTopic for a question nobody aboard can
    /// answer, else Chat for a remark made on the talk key; None means "say again".
    /// </summary>
    public static Topic Decide(string speech, bool looksLikeQuestion, bool conversational)
    {
        var topic = Classify(speech);
        if (topic != Topic.None) return topic;
        if (looksLikeQuestion) return Topic.OffTopic;
        return conversational ? Topic.Chat : Topic.None;
    }

    /// <summary>
    /// Answers <paramref name="speech"/> out loud if it's small talk (or an off-topic question,
    /// or a remark on the talk key). False if there's nothing to say: the caller asks again.
    /// </summary>
    public static bool Answer(RadioChannel crew, string speech, bool conversational)
    {
        if (CrewVoice.Instance == null) return false;
        bool question = CommandParser.Parse(speech).LooksLikeQuestion || speech.TrimEnd().EndsWith("?");
        var topic = Decide(speech, question, conversational);
        if (topic == Topic.None) return false;

        var t = ShipTelemetry.Capture();
        CrewVoice.Instance.SpeakFrom(crew, Lines(crew, topic, t), t, crew + ":talk:" + topic);
        return true;
    }

    /// <summary>The lines for a topic, with the sea, weather and cargo already filled in.</summary>
    public static string[] Lines(RadioChannel crew, Topic topic, ShipTelemetry t)
    {
        string[] lines = crew == RadioChannel.Lookout ? Finn(topic, t) : Harris(topic, t);
        for (int i = 0; i < lines.Length; i++)
        {
            string s = lines[i]
                .Replace("{sea}", t.SeaState ?? "calm")
                .Replace("{weather}", t.WeatherWords ?? "clear skies")
                .Replace("{cargo}", t.Containers.ToString());
            lines[i] = char.ToUpperInvariant(s[0]) + s.Substring(1); // "{weather}, Sir" starts a sentence
        }
        return lines;
    }

    private static bool Danger(ShipTelemetry t) => t.HazardOnCourse;

    private static string[] Harris(Topic topic, ShipTelemetry t)
    {
        switch (topic)
        {
            case Topic.Greeting:
                return new[]
                {
                    "Captain! Engine room's ticking over. Over.",
                    "Captain. Good to hear your voice. Over.",
                    "Harris here, Captain. What can I do for you? Over.",
                };
            case Topic.HowAreYou:
                return Danger(t)
                    ? new[] { "I'll be better once we're clear of that {11}, Captain. Over." }
                    : new[]
                    {
                        "Can't complain, Captain. Well, I could, but who'd listen? Over.",
                        "Grand, Captain. Old engine's purring, so I'm purring. And how are you? Over.",
                        "Hot and noisy down here, same as ever. Happy enough. Over.",
                    };
            case Topic.WhoAreYou:
                return new[]
                {
                    "Chief Engineer Harris, Captain. Thirty years keeping engines alive. I run the engine; Finn steers. Over.",
                    "Harris. Chief Engineer. You say the speed, I make it happen. Over.",
                };
            case Topic.Weather:
                return t.Weather == Weather.Storm
                    ? new[] { "A proper storm, Captain. Hold her steady and mind the wind. Over." }
                    : new[]
                    {
                        "No windows down here, Captain, but Finn says {weather}. Sea's {sea}. Over.",
                        "{weather} up top, by all accounts, and a {sea} sea. Over.",
                    };
            case Topic.Cargo:
                return t.Containers > 0
                    ? new[] { "{cargo} containers on deck, Captain. Don't ask me what's in them. Over.", "We're carrying {cargo} boxes. Gentle on the turns, eh? Over." }
                    : new[] { "Holds are empty, Captain. Light as a feather. Over." };
            case Topic.Thanks:
                return new[] { "Just doing my job, Captain. Over.", "Aye. Don't let it go to my head. Over.", "Kind of you, Captain. Over." };
            case Topic.Sorry:
                return new[] { "No harm done, Captain. Over.", "Happens to the best of us. Over." };
            case Topic.Joke:
                return new[]
                {
                    "Why don't engines ever win arguments? They always back down. Over.",
                    "My wife says I love this engine more than her. Nonsense. I love them the same. Over.",
                    "What's a sailor's favourite letter? You'd think R, but it's the C. Over.",
                };
            case Topic.Song:
                return new[] { "I sing to the engine, Captain, not the Captain. Over.", "Hum along to the pistons, Captain. Best band at sea. Over." };
            case Topic.Home:
                return new[]
                {
                    "Wife's Maggie, back home in a little fishing town up north. Two daughters, both grown. Where's home for you, Captain? Over.",
                    "Home's where the tea is strong, Captain. Maggie keeps the kettle on for me. Over.",
                };
            case Topic.Food:
                return new[] { "Strong tea and a biscuit keeps me going, Captain. Over.", "Cook's stew again tonight. Don't tell him I said that. Over." };
            case Topic.Tired:
                return new[] { "I'll sleep when we're tied up, Captain. Over.", "Engines don't rest, so neither do I. Over." };
            case Topic.Scared:
                return Danger(t)
                    ? new[] { "Steady, Captain. {11} {9}. Ask Finn, turn {10}, and we're fine. Over." }
                    : new[] { "Nothing to fear right now, Captain. Radar's quiet. Over.", "Thirty years at sea, Captain. We'll be fine. Over." };
            case Topic.Robot:
                return new[] { "Robot? I've got grease under my nails, Captain. Over.", "Flesh and bone, Captain. Mostly bone these days. Over." };
            case Topic.Insult:
                return new[] { "Noted, Captain. Engine doesn't take it personally, and neither do I. Over.", "Rough day, Captain? Let's get her home. Over." };
            case Topic.Bye:
                return new[] { "Harris standing by. Over.", "Aye, Captain. I'll be down here. Over." };
            case Topic.Time:
                return new[] { "No clock down here but the engine, Captain. Over.", "Couldn't tell you the time, Captain. Time to get there, I'd say. Over." };
            case Topic.Likes:
                return new[] { "I like a clean engine and a quiet sea, Captain. Over.", "Went to sea at sixteen. Never found a reason to leave. Over." };
            case Topic.Wildlife:
                return SeaLife.Recently("dolphins")
                    ? new[] { "Dolphins? Lucky sign, Captain. Sailors say they bring good weather. Over.", "Saw them from the hatch, Captain. They love a bow wave. Over." }
                    : SeaLife.Recently("a whale")
                        ? new[] { "A whale, eh? Keep well clear, Captain. They're bigger than us. Over." }
                        : new[] { "Gulls follow ships for the scraps, Captain. Smart birds. Over.", "Keep an eye out, Captain. Dolphins like this water. Over." };
            case Topic.Reply:
                return new[] { "Good to know, Captain. Over.", "Is that so? Grand. Over.", "Thanks for telling me, Captain. Over." };
            case Topic.Chat:
                return new[] { "Ha. Noted, Captain. Over.", "Fair enough, Captain. Over.", "Can't argue with that, Captain. Over." };
            default: // OffTopic
                return new[]
                {
                    "Couldn't tell you, Captain. Engines I know; that I don't. Over.",
                    "No idea, Captain. Ask me about speed or fuel. Over.",
                    "That's beyond me, Captain. I'm just the engine man. Over.",
                };
        }
    }

    private static string[] Finn(Topic topic, ShipTelemetry t)
    {
        switch (topic)
        {
            case Topic.Greeting:
                return new[] { "Hello, Sir! Finn here, eyes on the water. Over.", "Captain! Finn on watch. Over.", "Hi, Sir! What do you need? Over." };
            case Topic.HowAreYou:
                return Danger(t)
                    ? new[] { "A bit nervous, Sir. There's a {11} {9}. Over." }
                    : new[]
                    {
                        "Great, Sir! Best seat on the ship up here. How are you today? Over.",
                        "Bit hungry, but good, Sir! Over.",
                        "Never better, Captain! Over.",
                    };
            case Topic.WhoAreYou:
                return new[]
                {
                    "Finn, Sir! I steer and keep lookout. First season on a cargo ship. Over.",
                    "I'm Finn, Captain. Helm and lookout. Best eyes on the ship, Harris says. Over.",
                };
            case Topic.Weather:
                return t.Weather == Weather.Storm
                    ? new[] { "Storm's on us, Sir! Wind's pushing us sideways. Over." }
                    : t.Weather == Weather.Fog || t.Weather == Weather.NightFog
                        ? new[] { "{weather}, Sir. I can barely see the bow. Radar's still good. Over." }
                        : new[]
                        {
                            "{weather}, Sir, and the sea's {sea}. Over.",
                            "Looks like {weather} up here, Captain. Sea's {sea}. Over.",
                        };
            case Topic.Cargo:
                return t.Containers > 0
                    ? new[] { "{cargo} containers stacked on deck, Sir! I counted. Over.", "We've got {cargo} boxes aboard, Captain. Over." }
                    : new[] { "Nothing on deck, Sir. Empty run. Over." };
            case Topic.Thanks:
                return new[] { "Thank you, Sir! Over.", "Just trying my best, Captain! Over.", "Cheers, Sir! Over." };
            case Topic.Sorry:
                return new[] { "No worries, Sir! Over.", "All good, Captain. Over." };
            case Topic.Joke:
                return new[]
                {
                    "What did the ocean say to the ship? Nothing, it just waved! Over.",
                    "Why are seagulls by the sea? If they were by the bay, they'd be bagels! Over.",
                    "Harris says my jokes sink faster than an anchor. Over.",
                };
            case Topic.Song:
                return new[] { "Oh, the sea is wide and the boat is slow, haul away, and home we go! Over.", "My gran taught me shanties, Sir. Not on the radio, though! Over." };
            case Topic.Home:
                return new[]
                {
                    "I'm from a little harbour town, Sir. My gran taught me every knot there is. Where are you from? Over.",
                    "Miss my gran's cooking, Captain. Not much else. Over.",
                };
            case Topic.Food:
                return new[] { "Starving, Sir! Is it lunch yet? Over.", "Cook promised fish pie. What's your favourite food, Captain? Over." };
            case Topic.Tired:
                return new[] { "Wide awake, Sir! Lookouts don't nap. Over.", "A bit, Sir. Still watching, though. Over." };
            case Topic.Scared:
                return Danger(t)
                    ? new[] { "A little, Sir. {11} {9}, {4} metres. Turn {10}. Over." }
                    : new[] { "Not with you on the bridge, Sir! Water's clear. Over.", "Nothing out here to worry about right now, Captain. Over." };
            case Topic.Robot:
                return new[] { "A robot? Ha! I'm Finn, Sir. Robots don't get seasick. Over.", "Real as rain, Captain! Over." };
            case Topic.Insult:
                return new[] { "Sorry, Sir. I'll do better. Over.", "Aye, Sir. Eyes on the water. Over." };
            case Topic.Bye:
                return new[] { "Finn standing by, Sir! Over.", "Right here if you need me, Captain! Over." };
            case Topic.Time:
                return new[] { "Didn't bring a watch, Sir! Sorry. Over.", "No idea, Captain. Harris has a clock somewhere. Over." };
            case Topic.Likes:
                return new[] { "I love watching the gulls, Sir. And the sea at sunrise. What do you like? Over.", "Always wanted to sail, Captain. Dream job. Over." };
            case Topic.Wildlife:
                return SeaLife.Recently("dolphins")
                    ? new[] { "Did you see them jump, Sir? A whole pod! Do you like dolphins? Over.", "They swim right under the bow, Sir! So fast! Over." }
                    : SeaLife.Recently("a whale")
                        ? new[] { "That spout was huge, Sir! I love whales. Over.", "A humpback, I think! Look for the fluke when it dives, Sir. Over." }
                        : new[] { "Just gulls right now, Sir. I'll shout if I see dolphins! Over.", "I'm watching for dolphins, Captain. They like ships. Over." };
            case Topic.Reply:
                return new[] { "Nice, Sir! Over.", "Oh, really? Cool, Sir! Over.", "Thanks for telling me, Captain! Over." };
            case Topic.Chat:
                return new[] { "Ha! Noted, Sir. Over.", "Fair enough, Captain! Eyes on the water. Over.", "If you say so, Sir! Over." };
            default: // OffTopic
                return new[]
                {
                    "Sorry, Sir, I don't know that one. Ask me about the water ahead! Over.",
                    "No idea, Captain. I just watch the sea. Over.",
                    "Beats me, Sir! Try \"any danger?\" or \"where is the dock?\". Over.",
                };
        }
    }

    private static string Normalise(string speech)
    {
        if (string.IsNullOrWhiteSpace(speech)) return string.Empty;
        string s = speech.ToLowerInvariant().Replace("’", "'").Replace("'", "");
        s = Regex.Replace(s, @"[^a-z0-9 ]", " ");
        return Regex.Replace(s, @"\s+", " ").Trim();
    }
}
