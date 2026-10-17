using System.Collections.Generic;

/// <summary>
/// The radio English the game teaches: for each order, the standard phrase (Standard Marine
/// Communication Phrases, simplified, as on the radio card), and which wordings count as
/// standard. "Turn left" moves the ship just as well, but the crew then recast it: they carry
/// it out and model the standard phrase ("On the radio we say 'port twenty', Sir"), never
/// calling it wrong (the Affective Filter: correction without fear).
/// </summary>
public static class StandardPhrases
{
    private static readonly Dictionary<ShipAction, string[]> Standard = new Dictionary<ShipAction, string[]>
    {
        { ShipAction.EngineStop, new[] { "stop engine", "stop engines", "stop", "all stop", "engine stop", "full stop", "stop the engine" } },
        { ShipAction.EngineDeadSlow, new[] { "dead slow ahead", "dead slow", "ahead dead slow" } },
        { ShipAction.EngineSlow, new[] { "slow ahead", "ahead slow" } },
        { ShipAction.EngineHalf, new[] { "half ahead", "ahead half" } },
        { ShipAction.EngineFull, new[] { "full ahead", "ahead full", "all ahead full" } },
        { ShipAction.EngineFlank, new[] { "flank speed", "ahead flank", "flank ahead", "flank" } },
        { ShipAction.EngineSlowAstern, new[] { "slow astern", "half astern", "dead slow astern", "astern" } },
        { ShipAction.EngineFullAstern, new[] { "full astern", "all astern full" } },
        { ShipAction.EngineIncrease, new[] { "increase speed" } },
        { ShipAction.EngineDecrease, new[] { "reduce speed", "decrease speed" } },
        { ShipAction.RudderMidships, new[] { "midships", "rudder midships", "amidships", "steady", "steady as she goes" } },
        { ShipAction.Belay, new[] { "belay that", "belay" } },
        { ShipAction.RequestDocking, new[] { "request docking" } },
        { ShipAction.MakeFast, new[] { "make fast" } },
        { ShipAction.SoundHorn, new[] { "sound the horn", "sound horn" } },
        { ShipAction.FollowCourse, new[] { "follow the light", "take us home", "follow it", "bring us home" } },
        { ShipAction.Mayday, new[] { "mayday", "may day", "mayday mayday", "mayday mayday mayday", "under attack by pirates", "we are under attack", "under attack" } },
    };

    private static readonly Dictionary<ShipAction, string> Taught = new Dictionary<ShipAction, string>
    {
        { ShipAction.EngineStop, "stop engine" },
        { ShipAction.EngineDeadSlow, "dead slow ahead" },
        { ShipAction.EngineSlow, "slow ahead" },
        { ShipAction.EngineHalf, "half ahead" },
        { ShipAction.EngineFull, "full ahead" },
        { ShipAction.EngineFlank, "flank speed" },
        { ShipAction.EngineSlowAstern, "slow astern" },
        { ShipAction.EngineFullAstern, "full astern" },
        { ShipAction.EngineIncrease, "increase speed" },
        { ShipAction.EngineDecrease, "reduce speed" },
        { ShipAction.TurnPort, "port twenty" },
        { ShipAction.TurnStarboard, "starboard twenty" },
        { ShipAction.HardPort, "hard a port" },
        { ShipAction.HardStarboard, "hard a starboard" },
        { ShipAction.RudderMidships, "midships" },
        { ShipAction.QueryStatus, "status report" },
        { ShipAction.QueryHazard, "any danger?" },
        { ShipAction.QueryFuel, "fuel report" },
        { ShipAction.QueryHeading, "what is our heading?" },
        { ShipAction.QuerySpeed, "what is our speed?" },
        { ShipAction.QueryDestination, "where is the dock?" },
        { ShipAction.Belay, "belay that" },
        { ShipAction.RequestDocking, "request docking" },
        { ShipAction.MakeFast, "make fast" },
        { ShipAction.SoundHorn, "sound the horn" },
        { ShipAction.FollowCourse, "follow the light" },
        { ShipAction.Mayday, "mayday, under attack by pirates" },
    };

    /// <summary>The phrase taught for <paramref name="action"/> ("port twenty"), or null.</summary>
    public static string For(ShipAction action, float? degrees = null)
    {
        if (degrees.HasValue && degrees.Value > 0f)
        {
            int d = UnityEngine.Mathf.RoundToInt(degrees.Value);
            if (action == ShipAction.TurnPort) return "port " + d;
            if (action == ShipAction.TurnStarboard) return "starboard " + d;
        }
        return Taught.TryGetValue(action, out var p) ? p : null;
    }

    /// <summary>
    /// True if <paramref name="matchedPhrase"/> (the wording the parser recognised) is radio
    /// English for <paramref name="action"/>. Questions are never judged: they are free.
    /// Rudder orders are standard when they use "port" or "starboard" rather than left/right.
    /// </summary>
    public static bool IsStandard(ShipAction action, string matchedPhrase)
    {
        if (action.IsQuery()) return true;
        string p = Clean(matchedPhrase);
        if (p.Length == 0) return false;
        switch (action)
        {
            case ShipAction.TurnPort:
                return HasWord(p, "port") && !HasWord(p, "left");
            case ShipAction.TurnStarboard:
                return HasWord(p, "starboard") && !HasWord(p, "right");
            case ShipAction.HardPort:
                return HasWord(p, "hard") && HasWord(p, "port");
            case ShipAction.HardStarboard:
                return HasWord(p, "hard") && HasWord(p, "starboard");
        }
        if (!Standard.TryGetValue(action, out var ok)) return true;
        foreach (var s in ok) if (p == s) return true;
        return false;
    }

    /// <summary>What the crew say after carrying out a non-standard order: the standard phrase, kindly.</summary>
    public static string[] Recast(RadioChannel crew, string phrase)
    {
        string q = "\"" + phrase + "\"";
        if (crew == RadioChannel.Lookout)
            return new[]
            {
                "On the radio we say " + q + ", Sir. Over.",
                "Done, Sir! The proper order is " + q + ". Over.",
                "Got it! Next time you can say " + q + ", Sir. Over.",
            };
        return new[]
        {
            "Proper order for that is " + q + ", Captain. Over.",
            "Done. On the radio it's " + q + ", Captain. Over.",
            "Aye. Say " + q + " and any engineer will know. Over.",
        };
    }

    private static string Clean(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        s = s.ToLowerInvariant().Replace("-", " ").Replace("'", "");
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (char c in s) sb.Append(char.IsLetterOrDigit(c) || c == ' ' ? c : ' ');
        return System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    }

    private static bool HasWord(string s, string w) => (" " + s + " ").Contains(" " + w + " ");
}
