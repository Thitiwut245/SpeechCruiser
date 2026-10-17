using UnityEngine;

/// <summary>
/// The ship's signals, ordered by voice like everything else: the horn ("sound the horn") and the
/// distress call ("Mayday, under attack by pirates"). Each sounds, then whoever is listening
/// answers: a story encounter (KrakenFight drives the beast off with the horn; PirateChase calls
/// the navy with the Mayday) takes it by returning true from its handler; otherwise the crew say
/// something sensible ("A Mayday? We're not in danger, Captain.").
/// </summary>
public static class ShipSignals
{
    /// <summary>Raised when the horn sounds. A handler returns true if it has answered.</summary>
    public static System.Func<bool> OnHorn;

    /// <summary>Raised when a distress call is sent. A handler returns true if it has answered.</summary>
    public static System.Func<bool> OnMayday;

    private static bool AtSea => GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.Playing;

    public static bool Horn()
    {
        if (!AtSea) return false;
        var audio = AudioService.Instance;
        if (audio != null && audio.Sfx != null && audio.Sfx.ShipHorn != null) audio.Play(audio.Sfx.ShipHorn, AudioBus.Effects, 0.8f);
        if (OnHorn != null && OnHorn()) return true;
        Say(RadioChannel.Lookout, new[] { "Horn sounded, Sir. Over.", "One long blast, Sir. Over." }, "signal:horn");
        return true;
    }

    public static bool Mayday()
    {
        if (!AtSea) return false;
        if (OnMayday != null && OnMayday()) return true;
        Say(RadioChannel.ChiefEngineer, new[]
        {
            "A Mayday, Captain? We're not in danger. Save it for real trouble. Over.",
            "Mayday is for when the ship is in danger, Captain. All's well here. Over.",
        }, "signal:mayday");
        return true;
    }

    private static void Say(RadioChannel who, string[] lines, string key)
    {
        if (CrewVoice.Instance != null) CrewVoice.Instance.SpeakFrom(who, lines, ShipTelemetry.Capture(), key);
    }
}
