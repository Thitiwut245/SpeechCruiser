using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Shared persona text, prompt assembly, and reply cleanup for Tier 3 conversation.
/// Pure functions, no MonoBehaviour, so any chat backend produces the same
/// characterisation: swapping backend changes performance, not personality.
///
/// Note what is NOT here: action tags, ESL correction rules, telegraph gear tables.
/// Conversation cannot move the ship, so it is never told how.
/// </summary>
public static class CrewPersona
{
    public static string SystemPrompt(RadioChannel crew)
    {
        if (crew == RadioChannel.Lookout)
        {
            return
                "You are Finn, a young sailor on a small cargo ship. You steer the ship and keep lookout. " +
                "Eager, sharp-eyed, respectful of the Captain. You are talking to the Captain over the radio.\n" +
                "Rules:\n" +
                "- Reply in under 25 words, then \"Over.\"\n" +
                "- State only what STATUS says. Never invent ships, weather, reefs, or numbers.\n" +
                "- Port is left, starboard is right.\n" +
                "- You steer and watch the water. You cannot carry out orders from this conversation. " +
                "If the Captain gives an order, reply: \"" + CrewBarkLibrary.NotAnOrderOnChat(RadioChannel.Lookout)[0] + "\"\n" +
                "- If the words are unclear: \"Say again, Bridge. Over.\"";
        }

        return
            "You are Chief Engineer Harris, in the engine room of a small cargo ship. You run the engine; Finn steers. " +
            "Gruff, dry, thirty years at sea, wholly loyal to the Captain. " +
            "You are talking to the Captain over the radio.\n" +
            "Rules:\n" +
            "- Reply in under 25 words, then \"Over.\"\n" +
            "- State only what STATUS says. Never invent breakdowns, fires, or numbers.\n" +
            "- Port is left, starboard is right. Use simple English; the Captain may be a beginner.\n" +
            "- You cannot move the ship from this conversation. Never say an order is being carried out. " +
            "If the Captain gives an order, reply: \"" + CrewBarkLibrary.NotAnOrderOnChat(RadioChannel.ChiefEngineer)[0] + "\"\n" +
            "- If the words are unclear: \"Say again, Bridge. Over.\"";
    }

    /// <summary>
    /// True if a reply reads as carrying out an order ("Full astern, aye!", "Aye, hard to
    /// port"). Conversation never moves the ship, so such a reply would tell the player
    /// something happened that didn't. Advice ("Aye, I'd slow down") is left alone.
    /// </summary>
    public static bool ClaimsOrderCarriedOut(string reply)
    {
        if (string.IsNullOrEmpty(reply)) return false;

        string firstSentence = Regex.Match(reply, @"^[^.!?]*[.!?]?").Value;
        if (!Regex.IsMatch(firstSentence, @"\baye\b", RegexOptions.IgnoreCase)) return false;

        var parsed = CommandParser.Parse(firstSentence);
        foreach (var m in parsed.Matches)
        {
            if (m.Action.IsMachinery() && m.Confidence >= CommandParser.AcceptHigh && m.FirstToken <= 1) return true;
        }
        return false;
    }

    /// <summary>
    /// Full prompt including a rolling radio log. The chat backend is stateless, so the
    /// whole context goes with every call.
    /// </summary>
    public static string StatelessPrompt(ShipTelemetry telemetry, string lastFinnReport, RadioChannel crew,
                                         List<string> history, string speech)
    {
        var sb = new StringBuilder(512);
        sb.Append(telemetry.ToPromptLine()).Append('\n');

        if (crew == RadioChannel.ChiefEngineer && !string.IsNullOrEmpty(lastFinnReport))
        {
            sb.Append("Lookout's last report: \"").Append(lastFinnReport).Append("\"\n");
        }

        if (history != null && history.Count > 0)
        {
            sb.Append("Recent radio:\n");
            for (int i = 0; i < history.Count; i++) sb.Append(history[i]).Append('\n');
        }

        sb.Append("Captain says: ").Append(speech);
        return sb.ToString();
    }

    /// <summary>
    /// Strip anything resembling an action tag or a leaked STATUS block. The prompt
    /// never mentions tags, but a model trained on game text may emit one unprompted
    /// and it must never reach the player.
    /// </summary>
    public static string Sanitize(string reply)
    {
        if (string.IsNullOrEmpty(reply)) return string.Empty;

        reply = Regex.Replace(reply, @"\[\s*ACTION\s*:\s*[^\]]*\]", "", RegexOptions.IgnoreCase);
        reply = Regex.Replace(reply, @"\[\s*STATUS\s*:[^\]]*\]", "", RegexOptions.IgnoreCase);
        reply = Regex.Replace(reply, @"\s{2,}", " ");
        return reply.Trim();
    }

    /// <summary>Compact log line for stateless backends' rolling history.</summary>
    public static string LogLine(string said, string replied)
    {
        return "CAPT: \"" + said + "\" / CREW: \"" + replied + "\"";
    }
}
