using UnityEngine;

/// <summary>
/// A story mission's encounter in open water (the Kraken, the pirates): it begins once the ship is
/// <see cref="StoryMission.EncounterAt"/> of the way along, asks for orders with a CallPrompt, and
/// keeps the dock closed until it is over. The StoryDirector installs the right one for the
/// mission's goal. A checkpoint passed after it ended remembers that it ended (<see cref="Done"/>).
/// </summary>
public abstract class StoryEncounter : MonoBehaviour
{
    public static StoryEncounter Current { get; private set; }

    /// <summary>It is over (won, or it gave up on the ship): the voyage goes on to the dock.</summary>
    public bool Done { get; private set; }

    protected StoryMission Mission { get; private set; }
    protected CallPrompt Prompt { get; private set; }
    protected readonly System.Random Rng = new System.Random();
    protected Transform Props { get; private set; }

    private bool _begun;

    public static StoryEncounter Install(GameObject host, StoryMission mission)
    {
        StoryEncounter e;
        if (mission.Goal == MissionGoal.Kraken) e = host.AddComponent<KrakenFight>();
        else if (mission.Goal == MissionGoal.Pirates) e = host.AddComponent<PirateChase>();
        else return null;
        e.Mission = mission;
        return e;
    }

    protected virtual void Awake()
    {
        Current = this;
        Prompt = CallPrompt.Create(transform);
        Props = new GameObject(GetType().Name + "Props").transform;
    }

    protected virtual void OnDestroy()
    {
        if (Current == this) Current = null;
        if (Props != null) Destroy(Props.gameObject);
    }

    protected static bool AtSea => GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.Playing;
    protected static ShipNavigator Ship => ShipNavigator.Instance;

    private void Update()
    {
        if (Done || !AtSea || Ship == null) return;
        if (!_begun)
        {
            if (DialoguePlayer.IsPlaying || StoryDirector.RouteShare(Ship.transform.position) < Mission.EncounterAt) return;
            _begun = true;
            Begin();
            return;
        }
        Tick();
    }

    /// <summary>The encounter starts (the ship has come far enough).</summary>
    protected abstract void Begin();

    /// <summary>Every frame at sea while it runs.</summary>
    protected abstract void Tick();

    /// <summary>It is over: clean up and let the ship go on to the dock.</summary>
    protected void Finish()
    {
        if (Done) return;
        Done = true;
        Prompt.Cancel();
        Ended();
    }

    /// <summary>Back at a checkpoint passed after it ended: it stays ended.</summary>
    public void Skip()
    {
        _begun = true;
        Finish();
    }

    protected virtual void Ended() { }

    // ------------------------------------------------------------ helpers

    protected void Say(RadioChannel who, params string[] lines)
    {
        if (CrewVoice.Instance != null) CrewVoice.Instance.SpeakFrom(who, lines, ShipTelemetry.Capture(), "encounter:" + GetType().Name + lines[0].GetHashCode());
    }

    protected void Finn(params string[] lines) => Say(RadioChannel.Lookout, lines);
    protected void Harris(params string[] lines) => Say(RadioChannel.ChiefEngineer, lines);

    /// <summary>The helmsman steers back toward the channel a little way ahead (after a dodge).</summary>
    protected static void SteerBackToChannel()
    {
        var nav = Ship;
        if (nav == null || !Channel.Ready) return;
        float turn = Channel.BearingFrom(nav.transform, 45f);
        var helm = nav.GetComponent<ShipHelm>();
        if (helm != null && Mathf.Abs(turn) > 4f) helm.OrderHeadingChange(turn, 25f);
    }

    /// <summary>A point beside the ship: <paramref name="side"/> +1 starboard, -1 port; <paramref name="ahead"/> along the bow.</summary>
    protected static Vector3 Beside(float side, float across, float ahead)
    {
        var t = Ship.transform;
        Vector3 p = t.position + t.right * side * across + t.forward * ahead;
        p.y = 0f;
        return p;
    }

    /// <summary>Turn away from <paramref name="side"/>: the dodge, as EmergencyCall names it.</summary>
    protected static EmergencyCall.Dodge AwayFrom(float side) => side > 0f ? EmergencyCall.Dodge.Port : EmergencyCall.Dodge.Starboard;

    protected static string Side(float side) => side > 0f ? "starboard" : "port";

    protected static void Banner(string kicker, string title, string line)
    {
        var host = new GameObject("EncounterBanner");
        var layer = ScreenBanner.Layer(host, 25);
        if (layer == null) { Destroy(host); return; }
        ScreenBanner.Show(layer, kicker, title, line, 3.2f, top: 20f);
        Destroy(host, 4f);
    }
}
