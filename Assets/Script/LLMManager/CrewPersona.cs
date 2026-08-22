using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Shared persona text, prompt assembly, and reply cleanup for Tier 3 conversation.
/// Pure functions, no MonoBehaviour, so both the Ollama and LLMUnity backends produce
/// identical characterisation — swapping backend changes performance, not personality.
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
                "You are Finn, a young lookout on the bow of a small cargo ship, binoculars in hand. " +
                "Eager, sharp-eyed, respectful of the Captain. You are talking to the Captain over the radio.\n" +
                "Rules:\n" +
                "- Reply in under 25 words, then \"Over.\"\n" +
                "- State only what STATUS says. Never invent ships, weather, reefs, or numbers.\n" +
                "- Port is left, starboard is right.\n" +
                "- You watch the water; you do not operate machinery.\n" +
                "- If the words are unclear: \"Say again, Bridge. Over.\"";
        }

        return
            "You are Chief Engineer Harris, in the engine room of a small cargo ship. " +
            "Gruff, dry, thirty years at sea, wholly loyal to the Captain. " +
            "You are talking to the Captain over the radio.\n" +
            "Rules:\n" +
            "- Reply in under 25 words, then \"Over.\"\n" +
            "- State only what STATUS says. Never invent breakdowns, fires, or numbers.\n" +
            "- Port is left, starboard is right.\n" +
            "- Orders are already being carried out by the helm; you are only talking here.\n" +
            "- If the words are unclear: \"Say again, Bridge. Over.\"";
    }

    /// <summary>
    /// One turn's worth of context. Used by backends that keep their own conversation
    /// history (LLMUnity's LLMAgent does), so no log is injected here.
    /// </summary>
    public static string TurnPrompt(ShipTelemetry telemetry, string lastFinnReport, RadioChannel crew, string speech)
    {
        var sb = new StringBuilder(256);
        sb.Append(telemetry.ToPromptLine()).Append('\n');

        if (crew == RadioChannel.ChiefEngineer && !string.IsNullOrEmpty(lastFinnReport))
        {
            sb.Append("Lookout's last report: \"").Append(lastFinnReport).Append("\"\n");
        }

        sb.Append("Captain says: ").Append(speech);
        return sb.ToString();
    }

    /// <summary>
    /// Full prompt including a rolling radio log, for stateless backends that send the
    /// whole context each call (Ollama's /api/generate).
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
