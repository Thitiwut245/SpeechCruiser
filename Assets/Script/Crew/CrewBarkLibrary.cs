using UnityEngine;

public enum CrewTension
{
    Calm,
    Tense,
    Emergency,
}

/// <summary>
/// TIER 2 — authored crew dialogue. Fires on the same frame as the order, costs
/// nothing, and never hallucinates.
///
/// Every acknowledgement echoes the correct Standard Marine phrase back at the
/// player. That was the real goal behind the old ESL prompt rules, and hand-written
/// lines do it more reliably than asking a 4B model to reason about phonetics
/// mid-roleplay.
///
/// These lines were authored deliberately. To swap in your own, or to attach
/// pre-rendered TTS audio, this is the only file you need to touch — see
/// <see cref="CrewVoice"/> for the audio hook.
/// </summary>
public static class CrewBarkLibrary
{
    private static readonly string[] Empty = new string[0];

    // ==================================================================
    // CHIEF ENGINEER HARRIS — machinery acknowledgements
    // Gruff, three decades in engine rooms, absolute loyalty, no ceremony.
    // ==================================================================
    private static string[] HarrisAck(ShipAction action, CrewTension tension)
    {
        bool urgent = tension == CrewTension.Emergency;

        switch (action)
        {
            case ShipAction.EngineStop:
                return urgent
                    ? new[] { "All stop! Shutting her down now! Over.", "All stop, aye! Killing the turns! Over.", "All stop! She'll coast a while yet, Sir! Over." }
                    : new[] { "All stop, aye. Over.", "Engine stopped, Sir. Over.", "All stop. Turns coming off now. Over.", "Aye, all stop. She'll drift a bit. Over." };

            case ShipAction.EngineDeadSlow:
                return new[] { "Dead slow ahead, aye. Over.", "Dead slow, Sir. Just a whisper of steam. Over.", "Dead slow ahead. Barely making way. Over." };

            case ShipAction.EngineSlow:
                return new[] { "Slow ahead, aye. Over.", "Slow ahead, Sir. {12} knots. Over.", "Aye, slow ahead. Easing her out. Over.", "Slow ahead it is. She's answering. Over." };

            case ShipAction.EngineHalf:
                return new[] { "Half ahead, aye. Over.", "Half ahead, Sir. {12} knots. Over.", "Aye, half ahead. Good working speed. Over." };

            case ShipAction.EngineFull:
                return urgent
                    ? new[] { "Full ahead! Sir, that reef is close! Over.", "Full ahead, aye — but I don't like it! Over." }
                    : new[] { "Full ahead, aye. Over.", "Full ahead, Sir. {12} knots. Over.", "Aye, full ahead. She's got it. Over." };

            case ShipAction.EngineFlank:
                return new[] { "Flank speed, aye! She'll drink fuel, Sir! Over.", "Flank! Everything she has! Over.", "Flank speed, aye. Can't hold this long, Sir. Over." };

            case ShipAction.EngineSlowAstern:
                return new[] { "Slow astern, aye. Over.", "Astern, Sir. Backing her down. Over.", "Slow astern. Watch your stern swing. Over." };

            case ShipAction.EngineFullAstern:
                return new[] { "Full astern! Crash stop, aye! Over.", "Full astern! Hold onto something, Sir! Over.", "Full astern, aye! Everything in reverse! Over." };

            case ShipAction.EngineIncrease:
                return new[] { "More turns, aye. Up one. Over.", "Increasing speed, Sir. Over.", "Aye, giving you more. Over." };

            case ShipAction.EngineDecrease:
                return new[] { "Easing back, aye. Down one. Over.", "Reducing speed, Sir. Over.", "Aye, coming off the throttle. Over." };

            default:
                return Empty;
        }
    }

    // ==================================================================
    // FINN — helm acknowledgements. Young, quick, and he always says the plain
    // meaning too (port = left), because the Captain may be new to ship words.
    // ==================================================================
    private static string[] FinnHelmAck(ShipAction action)
    {
        switch (action)
        {
            case ShipAction.TurnPort:
                return new[] { "Port rudder, aye! Turning left. Over.", "Coming left, Sir! Over.", "Aye, to port, Sir. That's left! Over." };

            case ShipAction.TurnStarboard:
                return new[] { "Starboard rudder, aye! Turning right. Over.", "Coming right, Sir! Over.", "Aye, to starboard, Sir. That's right! Over." };

            case ShipAction.HardPort:
                return new[] { "Hard to port! Full left rudder! Over.", "Hard a-port, aye! Turning left hard! Over." };

            case ShipAction.HardStarboard:
                return new[] { "Hard to starboard! Full right rudder! Over.", "Hard a-starboard, aye! Turning right hard! Over." };

            case ShipAction.RudderMidships:
                return new[] { "Midships, aye. Rudder straight. Over.", "Rudder centred, Sir. Going straight. Over.", "Steady, aye. Holding this course. Over." };

            default:
                return Empty;
        }
    }

    // ==================================================================
    // Query answers. These carry live numbers, so they are format strings —
    // {0} and {1} are filled from a ShipTelemetry snapshot, never invented.
    // ==================================================================
    private static string[] HarrisQuery(ShipAction action, ShipTelemetry t)
    {
        switch (action)
        {
            case ShipAction.QueryDestination:
                return t.DestinationKnown
                    ? new[] { "Chart puts the dock {13}, {6} metres, Sir. Over.", "By the chart, the dock is {6} metres {13}. Over." }
                    : new[] { "No dock on the chart yet, Sir. Over." };

            case ShipAction.QueryStatus:
                return new[] { "Telegraph {0}, making {1} knots. Over.", "Engine room reports {0}, {1} knots. Over." };

            case ShipAction.QueryFuel:
                // Tanks are filled at every dock and no route empties them.
                return new[] { "Plenty for this run, Sir. We fill up at every dock. Over.", "Tanks are fine, Captain, {2}% left. Over." };

            case ShipAction.QuerySpeed:
                return new[] { "Making {1} knots, telegraph {0}. Over.", "{1} knots through the water, Sir. Over." };

            case ShipAction.QueryHeading:
                return new[] { "Heading {14}, Sir. Over.", "We're steering {14}. Over." };

            case ShipAction.QueryHazard:
                return new[] { "That's the lookout's eyes, Sir — I'm below decks. Over.", "Can't see a thing from the engine room, Sir. Ask Finn. Over." };

            default:
                return Empty;
        }
    }

    private static string[] FinnQuery(ShipAction action, ShipTelemetry t)
    {
        switch (action)
        {
            case ShipAction.QueryHazard:
            case ShipAction.QueryStatus:
                if (t.HazardOnCourse)
                    return new[] { "{8} {9}, {4} metres, right on our course! Turn {10}! Over.", "{8} {9}, {4} metres! We're heading for it, Sir. Turn {10}! Over." };
                return t.HazardDetected
                    ? new[] { "{8} {9}, {4} metres. Not on our course, Sir. Over.", "Nearest is a {11} {9}, {4} metres off. We're clear of it. Over." }
                    : new[] { "Open water all around, Sir! Over.", "Nothing on the water nearby, Sir. Clear as far as I can see. Over.", "All clear, Sir! Over." };

            case ShipAction.QueryHeading:
                return new[] { "We're heading {14}, Sir! Over.", "Bow's pointing {14}, Sir! Over." };

            case ShipAction.QuerySpeed:
                return new[] { "Feels like {1} knots from up here, Sir! Over.", "About {1} knots, Sir — Chief would know better. Over." };

            case ShipAction.QueryFuel:
                return new[] { "That's the Chief's ledger, Sir, not mine! Over.", "Couldn't say, Sir — ask the engine room. Over." };

            case ShipAction.QueryDestination:
                return t.DestinationKnown
                    ? new[] { "The dock is {13}, about {6} metres, Sir! Over.", "Harbour's {13}, {6} metres off, Sir! Over." }
                    : new[] { "No harbour in sight yet, Sir! Over." };

            default:
                return Empty;
        }
    }

    // ==================================================================
    // Clarifications. When an order contradicts itself, ask; never guess.
    // ==================================================================

    public static string[] AskAheadOrAstern()
    {
        return new[] { "Forward or back, Captain? Ahead or astern? Over.", "Which way, Sir — forward or back? Over." };
    }

    public static string[] AskPortOrStarboard(RadioChannel crew)
    {
        if (crew == RadioChannel.Lookout)
            return new[] { "Left or right, Sir? Port or starboard? Over.", "Which side, Sir — left or right? Over." };

        return new[] { "Left or right, Captain? Port or starboard? Over.", "Which way, Sir — left or right? Over." };
    }

    /// <summary>An order was asked as a question. Questions never move the ship.</summary>
    public static string[] IsThatAnOrder(RadioChannel crew)
    {
        if (crew == RadioChannel.Lookout)
            return new[] { "Is that an order, Sir? Say it plain and I'll pass it on! Over.", "Asking or ordering, Sir? Over." };

        return new[] { "Is that an order, Sir? Give it plain and I'll carry it out. Over.", "Question or order, Captain? Over." };
    }

    /// <summary>The order was negated or cancelled in the same breath. Nothing was touched.</summary>
    public static string[] NoChange(RadioChannel crew)
    {
        if (crew == RadioChannel.Lookout)
            return new[] { "Understood, Sir. No change. Over.", "Aye, Sir — nothing changed. Over." };

        return new[] { "Aye, no change, Sir. Over.", "Understood. Holding as we are. Over." };
    }

    /// <summary>Acknowledgement with nothing to do ("okay", "that's right").</summary>
    public static string[] StandingBy(RadioChannel crew)
    {
        if (crew == RadioChannel.Lookout)
            return new[] { "Aye, Sir! Standing by. Over.", "Lookout standing by. Over." };

        return new[] { "Aye. Standing by. Over.", "Engine room standing by. Over." };
    }

    // ==================================================================
    // Belay: undo the last order. {0} is the gear it went back to.
    // ==================================================================

    public static string[] BelayEngine()
    {
        return new[] { "Belay that, aye. Back to {0}. Over.", "Belay, aye. Telegraph back to {0}. Over." };
    }

    public static string[] BelayHelm()
    {
        return new[] { "Belay, aye. Rudder amidships. Over.", "Belay that, aye. Rudder back to centre. Over." };
    }

    public static string[] BelayEngineAndHelm()
    {
        return new[] { "Belay that, aye. Back to {0}, rudder amidships. Over." };
    }

    public static string[] NothingToBelay()
    {
        return new[] { "Nothing to belay, Sir. Standing by. Over." };
    }

    /// <summary>
    /// Replaces a conversation reply that claimed to carry out an order. Chat can never
    /// move the ship, so it must never say it did.
    /// </summary>
    public static string[] NotAnOrderOnChat(RadioChannel crew)
    {
        if (crew == RadioChannel.Lookout)
            return new[] { "Say that as a standard order, Sir, and it'll be done. Over." };

        return new[] { "Didn't catch a standard order, Captain. Say it plain. Over." };
    }

    // ==================================================================
    // Refusals, confusion, and unprompted alerts
    // ==================================================================

    /// <summary>An order could not be carried out (no ship to give it to). Orders reach the right crew member on any channel, so this is never about stations.</summary>
    public static string[] Redirect(RadioChannel crew)
    {
        return crew == RadioChannel.Lookout
            ? new[] { "Can't do that right now, Sir! Over." }
            : new[] { "Can't do that right now, Captain. Over." };
    }

    /// <summary>Nothing recognisable came through.</summary>
    public static string[] SayAgain(RadioChannel crew)
    {
        if (crew == RadioChannel.Lookout)
        {
            return new[]
            {
                "Bridge, you're breaking up! Say again, Sir! Over.",
                "Didn't catch that, Sir — wind's fierce up here! Over.",
                "Say again, Bridge? Over.",
            };
        }

        return new[]
        {
            "Say again, Bridge — machinery's loud down here. Over.",
            "Didn't get that, Sir. Repeat your order. Over.",
            "You're breaking up, Bridge. Say again. Over.",
        };
    }

    public static string[] RadioCheck(RadioChannel crew)
    {
        if (crew == RadioChannel.Lookout)
            return new[] { "Loud and clear, Sir! Over.", "Hearing you fine, Sir! Over." };

        return new[] { "Loud and clear, Captain. Over.", "Reading you five by five, Captain. Over." };
    }

    public static string[] HelpCard(RadioChannel crew)
    {
        return new[] { "Radio card is up, Captain. Press F1 to close it. Over." };
    }

    /// <summary>The player spoke while the crew was still answering the last message.</summary>
    public static string[] OneAtATime(RadioChannel crew)
    {
        return crew == RadioChannel.Lookout
            ? new[] { "Stand by, Sir, still answering you. One at a time, please. Over." }
            : new[] { "Stand by, Captain, one thing at a time. Ask again in a moment. Over." };
    }

    /// <summary>A bump during the tutorial drill, which costs nothing.</summary>
    public static string[] PracticeBump(bool mine)
    {
        string what = mine ? "a mine" : "a rock";
        return new[]
        {
            $"Bump! We touched {what}, Captain. In practice that's free. At sea it costs hull and cargo. Over.",
            $"Easy, Captain, that was {what}! No harm in practice, but steer wide of it at sea. Over.",
        };
    }

    /// <summary>After a hit: hull, cargo and engine in one plain report (Harris runs the engine room).</summary>
    public static string[] HullDamage(int hull, int maxHull, int lost, int aboard, ShipEngine.TelegraphGear gear)
    {
        string cargo = lost > 0 ? $" Lost {lost} container{(lost == 1 ? "" : "s")}, {aboard} left." : "";
        string engine = gear == ShipEngine.TelegraphGear.Stop ? " Engine stopped."
                      : gear == ShipEngine.TelegraphGear.DeadSlow ? " Engine eased to dead slow."
                      : gear == ShipEngine.TelegraphGear.Slow ? " Engine eased to slow ahead."
                      : gear == ShipEngine.TelegraphGear.Half ? " Engine eased to half ahead."
                      : " Engine eased back.";
        string warning = hull == 1 ? " One more hit and she sinks!" : "";
        return new[]
        {
            $"We hit it, Captain! Hull {hull} of {maxHull}.{cargo}{engine}{warning} Over.",
            $"Hull damage, Captain! {hull} of {maxHull} left.{cargo}{engine}{warning} Over.",
        };
    }

    /// <summary>After repeated misses: suggest a short, plain order.</summary>
    public static string[] TryHint(RadioChannel crew)
    {
        if (crew == RadioChannel.Lookout)
            return new[] { "Didn't catch that, Sir. Try a short order, like \"turn left\". Press F1 for the list. Over." };

        return new[] { "Didn't catch that, Captain. Try a short order, like \"slow ahead\". Press F1 for the list. Over." };
    }

    /// <summary>Player keyed the mic and said only a crew member's name.</summary>
    public static string[] NameOnly(RadioChannel crew)
    {
        if (crew == RadioChannel.Lookout)
            return new[] { "Bow here, Sir! Go ahead. Over.", "Finn listening, Sir! Over.", "Lookout standing by, Sir. Over." };

        return new[] { "Engine room, go ahead. Over.", "Harris here, Sir. Over.", "Chief listening. Over." };
    }

    // Finn warns without being asked, in three steps as a hazard on our course gets closer.
    // Each names the hazard, where it is in plain words, and which way to turn.

    public static string[] AlertHazardAhead()
    {
        return new[]
        {
            "{8} {9}, {4} metres! Turn {10}! Over.",
            "Watch out, Sir! {8} {9}, {4} metres. Turn {10}! Over.",
        };
    }

    public static string[] AlertHazardClosing()
    {
        return new[]
        {
            "{8} closing fast, {4} metres! Turn {10} now! Over.",
            "Sir! {8} {4} metres! Turn {10} or stop! Over.",
        };
    }

    public static string[] AlertHazardBrace()
    {
        return new[] { "Brace! Brace! We're going to hit it!", "Brace for impact, Sir!" };
    }

    // On a good line the crew still talk: a word when a warned hazard is passed, progress
    // calls on the way, the dock sighting, and a report after a long quiet spell.

    /// <summary>Harris, told to "continue" while already making way ahead.</summary>
    public static string[] KeepGoing()
    {
        return new[] { "Aye, keeping her going at {12} knots, Sir. Over.", "Steady as she goes, Captain. {12} knots. Over." };
    }

    /// <summary>Finn, after a hazard he warned about goes by without a hit.</summary>
    public static string[] HazardPassed()
    {
        return new[]
        {
            "Clear of it, Captain. Nice steering! Over.",
            "We're past it, Sir. Water's clear. Over.",
            "Good turn, Captain! That one's behind us. Over.",
        };
    }

    /// <summary>Finn, as the ship passes a quarter, half and three quarters of the way.</summary>
    public static string[] Progress(int quarter)
    {
        switch (quarter)
        {
            case 1: return new[] { "A quarter of the way, Captain. The dock is {6} metres {13}. Over." };
            case 2: return new[] { "Halfway there, Sir! {6} metres to the dock. Over.", "We're halfway, Captain. Dock {6} metres {13}. Over." };
            default: return new[] { "Nearly there, Captain! Dock {6} metres {13}. Over." };
        }
    }

    /// <summary>Finn sees the dock. Plain advice for beginners: slow down early.</summary>
    public static string[] DockSighted()
    {
        return new[]
        {
            "Dock in sight, Captain! {6} metres {13}. Steer for the gold line. Over.",
            "I see the dock, Sir! {6} metres {13}. Take her through the gold line. Over.",
        };
    }

    /// <summary>A report after a long quiet spell at sea.</summary>
    public static string[] QuietReport(RadioChannel crew)
    {
        return crew == RadioChannel.Lookout
            ? new[] { "Lookout here. Water's clear for now, Sir. Dock {6} metres {13}. Over.", "All quiet up front, Captain. Dock {6} metres {13}. Over." }
            : new[] { "Engine room here. All steady at {1} knots, Captain. Over.", "Harris here. Engine's running sweet, {1} knots. Over." };
    }

    /// <summary>Harris warns about fuel without being asked.</summary>
    public static string[] AlertFuelLow()
    {
        return new[]
        {
            "Bridge, fuel's down to {2}%. Recommend easing back to conserve. Over.",
            "Sir, we're at {2}% fuel. Might want to reduce speed. Over.",
            "Fuel getting thin, Sir — {2}%. Over.",
        };
    }

    public static string[] AlertFuelCritical()
    {
        return new[]
        {
            "Bridge! Fuel critical at {2}%! She'll die under us! Over.",
            "Sir, {2}% fuel! Recommend all stop before we're dead in the water! Over.",
            "Fuel emergency, Sir! {2}%! Over.",
        };
    }

    // ==================================================================
    // Lookup
    // ==================================================================

    /// <summary>
    /// Candidate lines for a crew member responding to an action. May be empty,
    /// in which case the caller should fall back to <see cref="SayAgain"/>.
    /// </summary>
    public static string[] Lines(RadioChannel crew, ShipAction action, CrewTension tension, ShipTelemetry telemetry)
    {
        if (action.IsQuery())
        {
            return crew == RadioChannel.Lookout
                ? FinnQuery(action, telemetry)
                : HarrisQuery(action, telemetry);
        }

        // Finn has the helm; Harris has the engine.
        return action.IsHelm() ? FinnHelmAck(action) : HarrisAck(action, tension);
    }

    /// <summary>
    /// Fill the telemetry placeholders. Kept in one place so no bark can ever
    /// reference a number that isn't real.
    ///   {0} telegraph gear   {1} speed kn   {2} fuel %   {3} compass   {4} hazard m
    ///   {5} dock relative to the bow   {6} dock distance m   {7} dock bearing (three digits)
    ///   {8} hazard kind ("Mine")   {9} where it is ("ahead on the left")   {10} which way to turn
    ///   {11} hazard kind in lower case   {12} ordered speed kn   {13} dock direction in plain words
    ///   {14} compass point in full words ("north-east")
    /// </summary>
    public static string Fill(string template, ShipTelemetry t)
    {
        if (string.IsNullOrEmpty(template)) return string.Empty;
        if (template.IndexOf('{') < 0) return template;

        return template
            .Replace("{0}", t.Gear)
            .Replace("{1}", t.SpeedKn.ToString())
            .Replace("{2}", t.FuelPct.ToString())
            .Replace("{3}", t.Compass)
            .Replace("{4}", t.HazardDistanceM.ToString())
            .Replace("{5}", t.DestinationRelative ?? "")
            .Replace("{6}", t.DestinationDistanceM.ToString())
            .Replace("{7}", t.DestinationBearing.ToString("000"))
            .Replace("{8}", t.HazardKind ?? "Hazard")
            .Replace("{9}", t.HazardWhere ?? "")
            .Replace("{10}", t.HazardAvoid ?? "")
            .Replace("{11}", (t.HazardKind ?? "hazard").ToLowerInvariant())
            .Replace("{12}", t.OrderedKn.ToString("0.#"))
            .Replace("{13}", t.DestinationWhere ?? "")
            .Replace("{14}", t.CompassWords ?? t.Compass ?? "");
    }

    /// <summary>Reads current danger level so barks can shift register.</summary>
    public static CrewTension CurrentTension(ShipTelemetry t)
    {
        if (t.HazardOnCourse && t.HazardSecondsToImpact <= 4f) return CrewTension.Emergency;
        if (t.FuelPct <= 10) return CrewTension.Emergency;
        if (t.HazardOnCourse) return CrewTension.Tense;
        if (t.FuelPct <= 25) return CrewTension.Tense;
        return CrewTension.Calm;
    }
}
