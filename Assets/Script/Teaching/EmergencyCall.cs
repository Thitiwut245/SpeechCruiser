using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Story mode's emergency call: when the ship is about to hit a reef, a rock or a mine, time
/// slows, Finn shouts where the danger is and the order to give appears big. The order depends
/// on the danger (<see cref="Choose"/>): something off to one side is dodged the other way;
/// something dead ahead is dodged toward the channel, or, a mine, stopped short of. The words are
/// picked at random among the right ones ("hard a starboard", "starboard twenty", "full astern",
/// "stop engine"), so the Captain learns more than left and right. Saying it in time (or any order
/// that means the same) saves the ship: the next touch only scrapes, and the helmsman then brings
/// her back onto the channel. A standard phrase is praised; plain English is modelled ("Hard a
/// starboard, aye: that's the call!"). The wrong way gets "Other way!", and the clock keeps
/// going. Nothing, or too late, and the ship takes the hit as usual.
///
/// Fair to the speaker: the clock stops while the radio records or decodes, so the speech
/// system's delay never costs the player. At most three a mission, the first one the most
/// generous. Every emergency is written to emergencies.csv (ResearchLog) and counted on the
/// results page (LanguageLog).
/// </summary>
public class EmergencyCall : MonoBehaviour
{
    public const int MaxPerMission = 3;
    private const float TriggerSeconds = 4.5f;  // game seconds to impact
    private const float Cooldown = 25f;         // real seconds after one ends
    private const float SpareSeconds = 6f;      // game seconds the hull is spared after a save

    public enum Response { Saves, WrongWay, Other }

    /// <summary>The way out of an emergency: turn to starboard, turn to port, or stop short.</summary>
    public enum Dodge { Starboard, Port, Stop }

    /// <summary>Within this of dead ahead (degrees), a danger can be dodged either way.</summary>
    public const float DeadAhead = 12f;

    public static EmergencyCall Active { get; private set; }

    /// <summary>An emergency call is on now (the crew's usual hazard warnings stand down).</summary>
    public static bool Running => Active != null && Active._running;

    /// <summary>Until then (real time) the router doesn't add its own recast: the emergency's line already models the phrase.</summary>
    public static float QuietUntil { get; private set; } = -1f;

    private int _count;
    private float _nextAllowed;
    private bool _running;
    private float _started, _clock, _window;
    private Dodge _dodge;
    private string _callPhrase;
    private readonly System.Random _rng = new System.Random();
    private int _tries;
    private string _hazard;
    private float _distance;

    private VisualElement _root;
    private Label _call, _alt;
    private VisualElement _bar;

    // ------------------------------------------------------------------ pure rules (tested)

    /// <summary>Seconds to answer the <paramref name="index"/>th emergency (0-based): generous first, then quicker.</summary>
    public static float WindowFor(int index) => index == 0 ? 6f : 4.5f;

    /// <summary>
    /// The way out. <paramref name="threatBearing"/> is where the danger is (degrees, + starboard);
    /// <paramref name="channelBearing"/> where the channel lies ahead. To one side, turn away from it.
    /// Dead ahead, a mine is stopped short of; anything else is passed on the channel's side, so
    /// the dodge also heads home.
    /// </summary>
    public static Dodge Choose(float threatBearing, float channelBearing, string kind)
    {
        if (Mathf.Abs(threatBearing) > DeadAhead) return threatBearing < 0f ? Dodge.Starboard : Dodge.Port;
        if (kind == "Mine") return Dodge.Stop;
        return channelBearing >= 0f ? Dodge.Starboard : Dodge.Port;
    }

    /// <summary>The standard orders that make this dodge, any of which may be called.</summary>
    public static string[] PhrasesFor(Dodge dodge)
    {
        switch (dodge)
        {
            case Dodge.Starboard: return new[] { "hard a starboard", "starboard twenty", "starboard thirty" };
            case Dodge.Port: return new[] { "hard a port", "port twenty", "port thirty" };
            default: return new[] { "full astern", "stop engine" };
        }
    }

    /// <summary>
    /// The order to call: one of <see cref="PhrasesFor"/> picked by <paramref name="roll"/> (0..1),
    /// but always hard over when there is little time: twenty degrees may not swing her in time.
    /// </summary>
    public static string CallFor(Dodge dodge, double roll, bool hurry)
    {
        var phrases = PhrasesFor(dodge);
        if (hurry && dodge != Dodge.Stop) return phrases[0];
        return phrases[Mathf.Clamp((int)(roll * phrases.Length), 0, phrases.Length - 1)];
    }

    /// <summary>Where the danger is, as a lookout says it ("fine on the port bow").</summary>
    public static string Where(float bearing)
    {
        float a = Mathf.Abs(bearing);
        string side = bearing < 0f ? "port" : "starboard";
        if (a <= DeadAhead) return "dead ahead";
        if (a <= 30f) return "fine on the " + side + " bow";
        if (a <= 70f) return "on the " + side + " bow";
        return "on the " + side + " beam";
    }

    /// <summary>Does <paramref name="action"/> save the ship when the way out is <paramref name="dodge"/>?</summary>
    public static Response Judge(ShipAction action, Dodge dodge)
    {
        switch (action)
        {
            case ShipAction.TurnStarboard:
            case ShipAction.HardStarboard:
                return dodge == Dodge.Port ? Response.WrongWay : Response.Saves; // dead ahead, either side clears it
            case ShipAction.TurnPort:
            case ShipAction.HardPort:
                return dodge == Dodge.Starboard ? Response.WrongWay : Response.Saves;
            case ShipAction.EngineStop:
            case ShipAction.EngineSlowAstern:
            case ShipAction.EngineFullAstern:
                return Response.Saves; // stopping short works too
            case ShipAction.EngineIncrease:
            case ShipAction.EngineFull:
            case ShipAction.EngineFlank:
                return Response.WrongWay; // faster into it
            default:
                return Response.Other;
        }
    }

    // ------------------------------------------------------------------ life

    /// <summary>Starts watching for emergencies on this story mission.</summary>
    public static EmergencyCall Create(Transform parent)
    {
        var go = new GameObject("Emergency Call");
        go.transform.SetParent(parent, false);
        return go.AddComponent<EmergencyCall>();
    }

    private void OnEnable()
    {
        Active = this;
        LanguageLog.OnOrder += HandleOrder;
    }

    private void OnDisable()
    {
        LanguageLog.OnOrder -= HandleOrder;
        if (_running) End("ended", false);
        if (Active == this) Active = null;
    }

    private static bool AtSea => GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.Playing;

    private void Update()
    {
        if (_running) { Run(); return; }
        if (_count >= MaxPerMission || Time.unscaledTime < _nextAllowed || !AtSea) return;
        if (TutorialManager.Instance != null && TutorialManager.Instance.IsActive) return;
        if (DockingPilot.Busy) return;
        var radar = RadarSystem.Instance;
        var nav = ShipNavigator.Instance;
        if (radar == null || nav == null || !radar.IsThreat) return;
        if (radar.TimeToImpact > TriggerSeconds || Mathf.Abs(nav.currentSpeed) < 1.5f) return;
        string kind = radar.ThreatKind;
        if (kind == "Sandbank" || kind == "Shallows") return; // running aground does no harm
        Begin(radar, kind);
    }

    private void Begin(RadarSystem radar, string kind)
    {
        _running = true;
        _started = Time.unscaledTime;
        _clock = 0f;
        _window = WindowFor(_count) + (GameSession.Instance != null && GameSession.Instance.CrewLevel >= 3 ? 1.5f : 0f); // a seasoned crew buys time
        _count++;
        _tries = 0;
        _hazard = kind;
        _distance = radar.ThreatDistance;
        var nav = ShipNavigator.Instance;
        _dodge = Choose(radar.ThreatBearing, ChannelBearing(nav), kind);
        _callPhrase = CallFor(_dodge, _rng.NextDouble(), radar.TimeToImpact < 3f);
        if (TimeController.Instance != null) TimeController.Instance.SetEmergency(true);

        ScreenBanner.ClearAll(); // nothing covers the order
        ShowBanner(_callPhrase);
        string where = Where(radar.ThreatBearing);
        string what = kind == "Mine" ? "A mine" : kind == "Rock" ? "Rocks" : kind;
        string q = "\"" + _callPhrase + "\"";
        Say(new[]
        {
            "EMERGENCY, Sir! " + what + " " + where + "! Say " + q + "! Now!",
            "Danger, Captain! " + what + " " + where + "! Order " + q + "!",
            what + " " + where + ", Sir! " + Cap(_callPhrase) + ", now! Say it!",
        }, "emergency:start");
    }

    /// <summary>Where the channel lies, a little way ahead along it: degrees off the bow (+ starboard).</summary>
    private static float ChannelBearing(ShipNavigator nav)
    {
        if (nav == null || !Channel.Ready) return 0f;
        Vector3 aim = ChannelAhead(nav.transform.position, 45f);
        Vector3 to = Channel.Flat(aim - nav.transform.position);
        if (to.sqrMagnitude < 1f) return 0f;
        return Mathf.DeltaAngle(nav.transform.eulerAngles.y, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg);
    }

    /// <summary>The channel point <paramref name="metres"/> on from the one nearest <paramref name="at"/>.</summary>
    private static Vector3 ChannelAhead(Vector3 at, float metres)
    {
        int near = Channel.Nearest(at, 0, out _);
        return Channel.Points[Channel.IndexAt(Channel.Along[near] + metres)];
    }

    private void Run()
    {
        // The clock only runs while the Captain could be speaking: not while the radio records,
        // decodes or the crew's AI thinks. The speech system's delay is never the player's fault.
        bool radioBusy = TimeController.Instance != null && TimeController.Instance.RadioBusy;
        if (!radioBusy && !(TimeController.Instance != null && TimeController.Instance.IsPaused)) _clock += Time.unscaledDeltaTime;
        float left = Mathf.Clamp01(1f - _clock / _window);
        if (_bar != null)
        {
            _bar.style.width = Length.Percent(left * 100f);
            _bar.style.backgroundColor = radioBusy ? HudLook.BrassDim : HudLook.Danger;
        }

        var radar = RadarSystem.Instance;
        if (radar == null || !radar.IsThreat) { End("cleared", false); return; } // steered clear already
        if (_clock >= _window)
        {
            Say(new[] { "Brace, brace! Hold on, Captain!" }, "emergency:late");
            End("hit", false);
        }
    }

    private void HandleOrder(ShipAction action, bool standard)
    {
        if (!_running) return;
        _tries++;
        var verdict = Judge(action, _dodge);
        if (verdict == Response.WrongWay)
        {
            Say(new[] { _dodge == Dodge.Stop ? "Not faster, Sir! \"" + _callPhrase + "\"!" : "Other way, Sir! \"" + _callPhrase + "\"!" }, "emergency:wrong");
            return;
        }
        if (verdict != Response.Saves) return;

        var hull = FindAnyObjectByType<ShipHull>();
        if (hull != null) hull.Spare(SpareSeconds);
        QuietUntil = Time.unscaledTime + 6f;
        End("saved", standard);
        string call = _callPhrase;
        StartCoroutine(BackToChannel());
        StartCoroutine(Praise(standard
            ? new[] { "Textbook, Captain! We're clear of it! Over.", "Perfect order, Sir! That was close! Over." }
            : new[] { "\"" + Cap(call) + "\", aye: that's the call, Sir! We're clear! Over.", "We made it! On the radio that's \"" + call + "\", Sir. Over." }));
    }

    /// <summary>
    /// After a save the helmsman brings her back: once the danger is behind (or a few seconds on),
    /// he steers for the channel a little way ahead, and if she was stopped or going astern, rings
    /// slow ahead once she points the right way. The dodge heads somewhere useful, not anywhere.
    /// </summary>
    private System.Collections.IEnumerator BackToChannel()
    {
        float until = Time.unscaledTime + 5f;
        yield return new WaitForSecondsRealtime(2f);
        while (Time.unscaledTime < until && RadarSystem.Instance != null && RadarSystem.Instance.IsThreat) yield return null;
        var nav = ShipNavigator.Instance;
        if (!AtSea || nav == null || _running || !Channel.Ready || DockingPilot.Busy) yield break;

        float turn = ChannelBearing(nav);
        var helm = nav.GetComponent<ShipHelm>();
        if (helm != null && Mathf.Abs(turn) > 4f) helm.OrderHeadingChange(turn, 25f);
        bool stopped = nav.currentSpeed < 0.5f;
        Say(new[] { "Bringing her back to the channel, Sir. Over.", "Helm's coming round to the channel, Captain. Over." }, "emergency:recover");
        if (!stopped) yield break;
        yield return new WaitForSecondsRealtime(2.5f);
        if (AtSea && !_running && ShipNavigator.Instance != null) ShipNavigator.Instance.SetGear(ShipEngine.TelegraphGear.Slow);
    }

    private System.Collections.IEnumerator Praise(string[] lines)
    {
        yield return new WaitForSecondsRealtime(1.6f); // after the read-back
        Say(lines, "emergency:saved");
    }

    private void End(string outcome, bool standard)
    {
        _running = false;
        _nextAllowed = Time.unscaledTime + Cooldown;
        if (TimeController.Instance != null) TimeController.Instance.SetEmergency(false);
        HideBanner();
        bool saved = outcome == "saved";
        if (outcome != "ended" && outcome != "cleared") LanguageLog.Emergency(saved);
        int reactionMs = Mathf.RoundToInt((Time.unscaledTime - _started) * 1000f);
        ResearchLog.Emergency(_hazard, _distance, _window, outcome, saved ? reactionMs : -1, _tries, saved ? (standard ? "1" : "0") : "", _callPhrase);
    }

    private static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

    private static void Say(string[] lines, string key)
    {
        if (CrewVoice.Instance == null) return;
        CrewVoice.Instance.SpeakFrom(RadioChannel.Lookout, lines, ShipTelemetry.Capture(), key);
    }

    // ------------------------------------------------------------------ the banner

    private void ShowBanner(string call)
    {
        if (_root == null) BuildBanner();
        if (_root == null) return;
        _call.text = call.ToUpperInvariant() + "!";
        string other = _dodge == Dodge.Stop ? "or turn away" : "or \"full astern\"";
        _alt.text = other + "  ·  hold " + (FindAnyObjectByType<RadioMic>() is RadioMic mic ? mic.OrderKeyName : "T") + " and say it";
        _bar.style.width = Length.Percent(100f);
        _root.style.display = DisplayStyle.Flex;
    }

    private void HideBanner()
    {
        if (_root != null) _root.style.display = DisplayStyle.None;
    }

    private void BuildBanner()
    {
        var host = SceneLoader.Instance != null ? SceneLoader.Instance.GetComponent<UIDocument>() : null;
        if (host == null || host.panelSettings == null) return;
        var doc = gameObject.AddComponent<UIDocument>();
        doc.panelSettings = host.panelSettings;
        doc.sortingOrder = 5; // over the HUD
        var root = doc.rootVisualElement;
        UiTheme.Apply(root, overSea: true); // the HUD's type, made to read over the sea
        root.pickingMode = PickingMode.Ignore;
        root.style.position = Position.Absolute;
        root.style.left = 0; root.style.right = 0; root.style.top = 0; root.style.bottom = 0;

        _root = new VisualElement { pickingMode = PickingMode.Ignore };
        _root.style.position = Position.Absolute;
        _root.style.left = 0; _root.style.right = 0; _root.style.top = Length.Percent(17f);
        _root.style.alignItems = Align.Center;

        var tag = HudLook.Words("EMERGENCY", 20f, HudLook.Danger);
        tag.style.letterSpacing = 8f;
        tag.style.unityFontStyleAndWeight = FontStyle.Bold;
        _call = HudLook.Words("", 72f, HudLook.Text);
        _call.style.unityFontStyleAndWeight = FontStyle.Bold;
        _call.style.letterSpacing = 3f;
        _call.style.textShadow = new TextShadow { offset = new Vector2(0f, 3f), blurRadius = 8f, color = new Color(0.05f, 0.03f, 0.08f, 0.85f) };
        _alt = HudLook.Words("", 22f, HudLook.TextDim);
        _alt.style.textShadow = new TextShadow { offset = new Vector2(0f, 2f), blurRadius = 6f, color = new Color(0.05f, 0.03f, 0.08f, 0.85f) };

        // The time left: a line that shortens (grey while the radio is listening: the clock is stopped).
        var track = new VisualElement { pickingMode = PickingMode.Ignore };
        track.style.width = 560f; track.style.height = 4f; track.style.marginTop = 14f;
        track.style.backgroundColor = new Color(1f, 1f, 1f, 0.2f);
        _bar = new VisualElement { pickingMode = PickingMode.Ignore };
        _bar.style.height = 4f;
        _bar.style.backgroundColor = HudLook.Danger;
        track.Add(_bar);

        _root.Add(tag);
        _root.Add(_call);
        _root.Add(_alt);
        _root.Add(track);
        _root.style.display = DisplayStyle.None;
        root.Add(_root);
    }
}
