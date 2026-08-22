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
                return new[] { "Slow ahead, aye. Over.", "Slow ahead, Sir. Three knots. Over.", "Aye, slow ahead. Easing her out. Over.", "Slow ahead it is. She's answering. Over." };

            case ShipAction.EngineHalf:
                return new[] { "Half ahead, aye. Over.", "Half ahead, Sir. Five knots. Over.", "Aye, half ahead. Good working speed. Over." };

            case ShipAction.EngineFull:
                return urgent
                    ? new[] { "Full ahead! Sir, that reef is close! Over.", "Full ahead, aye — but I don't like it! Over." }
                    : new[] { "Full ahead, aye. Over.", "Full ahead, Sir. Eight knots. Over.", "Aye, full ahead. She's got it. Over." };

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

            case ShipAction.TurnPort:
                return new[] { "Port rudder, aye. Coming left. Over.", "Rudder to port, Sir. Over.", "Port, aye. She's swinging left. Over." };

            case ShipAction.TurnStarboard:
                return new[] { "Starboard rudder, aye. Coming right. Over.", "Rudder to starboard, Sir. Over.", "Starboard, aye. She's swinging right. Over." };

            case ShipAction.HardPort:
                return new[] { "Hard a-port! Rudder full left! Over.", "Hard to port, aye! Over.", "Hard a-port! She's heeling, Sir! Over." };

            case ShipAction.HardStarboard:
                return new[] { "Hard a-starboard! Rudder full right! Over.", "Hard to starboard, aye! Over.", "Hard a-starboard! Hold on! Over." };

            case ShipAction.RudderMidships:
                return new[] { "Rudder amidships, aye. Steady as she goes. Over.", "Midships, Sir. Holding this course. Over.", "Amidships, aye. Steady on. Over." };

            default:
                return Empty;
        }
    }

    // ==================================================================
    // Query answers. These carry live numbers, so they are format strings —
    // {0} and {1} are filled from a ShipTelemetry snapshot, never invented.
    // ==================================================================
    private static string[] HarrisQuery(ShipAction action)
    {
        switch (action)
        {
            case ShipAction.QueryStatus:
                return new[] { "Telegraph {0}, making {1} knots, fuel {2}%. Over.", "Engine room reports {0}, {1} knots, {2}% fuel. Over." };

            case ShipAction.QueryFuel:
                return new[] { "Fuel at {2}%, Sir. Over.", "We're carrying {2}% in the bunkers. Over." };

            case ShipAction.QuerySpeed:
                return new[] { "Making {1} knots, telegraph {0}. Over.", "{1} knots through the water, Sir. Over." };

            case ShipAction.QueryHeading:
                return new[] { "Heading {3}, Sir. Over.", "We're steering {3}. Over." };

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
                return t.HazardDetected
                    ? new[] { "Reef! {4} metres dead ahead, Sir! Over.", "Rocks ahead — {4} metres and closing! Over.", "Reef dead ahead, {4} metres! Recommend you come off this course! Over." }
                    : new[] { "Open water ahead, all clear, Sir! Over.", "Nothing on the water, Sir. Clear as far as I can see. Over.", "All clear ahead, Sir! Over." };

            case ShipAction.QueryHeading:
                return new[] { "We're heading {3}, Sir! Over.", "Bow's pointing {3}, Sir! Over." };

            case ShipAction.QuerySpeed:
                return new[] { "Feels like {1} knots from up here, Sir! Over.", "About {1} knots, Sir — Chief would know better. Over." };

            case ShipAction.QueryFuel:
                return new[] { "That's the Chief's ledger, Sir, not mine! Over.", "Couldn't say, Sir — ask the engine room. Over." };

            default:
                return Empty;
        }
    }

    // ==================================================================
    // Refusals, confusion, and unprompted alerts
    // ==================================================================

    /// <summary>Finn was handed a machinery order. He has no authority over the engine.</summary>
    public static string[] Redirect(RadioChannel crew)
    {
        if (crew == RadioChannel.Lookout)
        {
            return new[]
            {
                "That's Chief Harris's station, Sir! I've only got eyes up here. Over.",
                "Can't touch the engine from the bow, Sir — call the Chief! Over.",
                "Not my station, Sir! Try channel one. Over.",
            };
        }

        return new[] { "Say again, Bridge? Over." };
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

    /// <summary>Player keyed the mic and said only a crew member's name.</summary>
    public static string[] NameOnly(RadioChannel crew)
    {
        if (crew == RadioChannel.Lookout)
            return new[] { "Bow here, Sir! Go ahead. Over.", "Finn listening, Sir! Over.", "Lookout standing by, Sir. Over." };

        return new[] { "Engine room, go ahead. Over.", "Harris here, Sir. Over.", "Chief listening. Over." };
    }

    /// <summary>Finn spots a reef without being asked.</summary>
    public static string[] AlertReef()
    {
        return new[]
        {
            "Reef! {4} metres dead ahead, Sir! Come off this course! Over.",
            "Rocks ahead, {4} metres! Recommend hard turn, Sir! Over.",
            "Sir! Reef {4} metres and closing fast! Over.",
        };
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
                : HarrisQuery(action);
        }

        // Machinery orders belong to the engine room alone.
        if (crew == RadioChannel.Lookout) return Redirect(crew);

        return HarrisAck(action, tension);
    }

    /// <summary>
    /// Fill the telemetry placeholders. Kept in one place so no bark can ever
    /// reference a number that isn't real.
    ///   {0} telegraph gear   {1} speed kn   {2} fuel %   {3} compass   {4} hazard m
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
            .Replace("{4}", t.HazardDistanceM.ToString());
    }

    /// <summary>Reads current danger level so barks can shift register.</summary>
    public static CrewTension CurrentTension(ShipTelemetry t)
    {
        if (t.HazardDetected && t.HazardDistanceM <= 15 && t.SpeedKn >= 3) return CrewTension.Emergency;
        if (t.FuelPct <= 10) return CrewTension.Emergency;
        if (t.HazardDetected && t.HazardDistanceM <= 30) return CrewTension.Tense;
        if (t.FuelPct <= 25) return CrewTension.Tense;
        return CrewTension.Calm;
    }
}
