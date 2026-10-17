using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Mission 3-2, the pirates. In the Strait (open water, no rocks) two skiffs come up astern:
///   1. "Flank speed!" The faster the ship goes, the slower they close.
///   2. "Mayday, under attack by pirates." The navy answers: a warship, 45 seconds away (a
///      countdown on screen). Without a Mayday Finn keeps asking; after a long while a patrol
///      comes anyway, so nobody is stuck.
///   3. Until then, a skiff that gets close on one side has to be shaken off: turn away hard
///      (any standard turn away from it). In time, it falls back; late, it comes alongside and the
///      pirates take two containers before it drops away.
///   4. The frigate arrives, sounds its horn, and the skiffs flee. Then it is over.
/// The skiffs keep station on the ship in her own frame (a gap astern that closes at the
/// difference in speed), so the chase reads clearly from the camera.
/// </summary>
public class PirateChase : StoryEncounter
{
    private const float SkiffSpeed = 12.5f;     // the ship makes 10 at flank, 8 at full
    private const float StartGap = 90f;
    private const float CloseGap = 14f;         // this close: shake it off
    private const float NavySeconds = 45f;
    private const float PatrolSeconds = 120f;   // no Mayday at all: a patrol comes anyway

    private sealed class Skiff
    {
        public GameObject Go;
        public float Side;   // -1 port, +1 starboard
        public float Gap;    // metres astern of the ship
        public float Fleeing = -1f;
    }

    private readonly List<Skiff> _skiffs = new List<Skiff>();
    private bool _flank, _mayday;
    private float _navyAt = -1f, _started, _nextAsk, _nextMayday;
    private GameObject _frigate;
    private float _frigateT = -1f;   // seconds since it arrived
    private Label _countdown;
    private GameObject _countdownHost;

    protected override void Begin()
    {
        ShipSignals.OnMayday = OnMayday;
        _started = Time.time;
        foreach (float side in new[] { -1f, 1f })
        {
            var s = new Skiff { Side = side, Gap = StartGap + (side > 0f ? 12f : 0f) };
            s.Go = EncounterModels.Skiff(Props, side > 0f ? new Color(0.85f, 0.85f, 0.8f) : new Color(0.2f, 0.45f, 0.5f));
            _skiffs.Add(s);
        }
        Finn("Captain! Two fast boats behind us! Men with guns... Pirates!");
        Banner("THE STRAIT", "PIRATES", "Run, and call for help.");
        StartCoroutine(AskFlank(2.2f));
    }

    protected override void OnDestroy()
    {
        if (ShipSignals.OnMayday == (System.Func<bool>)OnMayday) ShipSignals.OnMayday = null;
        if (_countdownHost != null) Destroy(_countdownHost);
        base.OnDestroy();
    }

    // ------------------------------------------------------------ the chase

    protected override void Tick()
    {
        var nav = Ship;
        float speed = Mathf.Max(0f, nav.currentSpeed);
        foreach (var s in _skiffs)
        {
            if (s.Fleeing >= 0f) { s.Fleeing += Time.deltaTime; s.Gap += (SkiffSpeed + speed) * 0.9f * Time.deltaTime; }
            else s.Gap = Mathf.Max(0f, s.Gap - (SkiffSpeed - speed) * Time.deltaTime);
            Place(s);
        }
        UpdateNavy();
        if (Done || Prompt.Running) return;

        if (!_mayday && _flank && Time.time >= _nextMayday) AskMayday();
        else
        {
            var close = Closest();
            if (close != null && close.Gap <= CloseGap && Time.time >= _nextAsk) StartCoroutine(ShakeOff(close));
        }
        if (_navyAt < 0f && Time.time - _started > PatrolSeconds) CallNavy(15f, "Navy patrol heard the trouble. We're coming. Fifteen seconds.");
    }

    private Skiff Closest()
    {
        Skiff best = null;
        foreach (var s in _skiffs) if (s.Fleeing < 0f && (best == null || s.Gap < best.Gap)) best = s;
        return best;
    }

    private void Place(Skiff s)
    {
        if (s.Go == null) return;
        var t = Ship.transform;
        float across = 7f + s.Gap * 0.22f + (s.Fleeing >= 0f ? s.Fleeing * 6f : 0f);
        Vector3 p = t.position - t.forward * (s.Gap - 4f) + t.right * s.Side * across;
        p.y = SeaWaves.Displacement(p.x, p.z, SeaWaves.Now).y - 0.1f;
        Vector3 fwd = s.Fleeing >= 0f ? Vector3.Slerp(t.forward, t.right * s.Side, Mathf.Clamp01(s.Fleeing)) : t.forward;
        s.Go.transform.SetPositionAndRotation(p, Quaternion.LookRotation(Channel.Flat(fwd)) * Quaternion.Euler(-4f, 0f, Mathf.Sin(Time.time * 3f + s.Side) * 4f));
    }

    // ------------------------------------------------------------ the orders

    private IEnumerator AskFlank(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (Done || !AtSea) yield break;
        Harris("Captain, we must run! Say \"flank speed\"!");
        Prompt.Ask("PIRATES ASTERN", "flank speed", "everything she's got", 7f,
                   a => a == ShipAction.EngineFlank || a == ShipAction.EngineFull || a == ShipAction.EngineIncrease
                        ? EmergencyCall.Response.Saves
                        : a.IsAsternGear() || a == ShipAction.EngineStop ? EmergencyCall.Response.WrongWay : EmergencyCall.Response.Other,
                   ok =>
                   {
                       _flank = true;
                       _nextMayday = Time.time + 2.5f;
                       if (ok) Harris("Flank speed! She's flying, Captain!");
                       else { Ship.SetGear(ShipEngine.TelegraphGear.Flank); Harris("I'm giving her flank speed! Next time say it, Captain!"); }
                   },
                   () => Harris("Not slower! Faster! \"Flank speed\"!"));
    }

    private void AskMayday()
    {
        _nextMayday = Time.time + 18f; // asked again later if it isn't sent
        Finn("Call the navy, Captain! Say \"Mayday, under attack by pirates\"!");
        Prompt.Ask("CALL FOR HELP", "mayday, under attack by pirates", "the navy listens on the radio", 9f,
                   a => a == ShipAction.Mayday ? EmergencyCall.Response.Saves : EmergencyCall.Response.Other,
                   ok => { if (!ok) Finn("No Mayday yet, Captain. We need the navy!"); },
                   slowTime: false);
    }

    /// <summary>The distress call: the navy answers and comes. Any time during the chase.</summary>
    private bool OnMayday()
    {
        if (Done) return false;
        if (_mayday) { Finn("The navy is already coming, Captain! Keep running!"); return true; }
        _mayday = true;
        CallNavy(NavySeconds, "Mayday received. Navy warship on its way. Forty-five seconds. Keep running, Captain.");
        return true;
    }

    private IEnumerator ShakeOff(Skiff s)
    {
        _nextAsk = Time.time + 3f;
        var dodge = AwayFrom(s.Side);
        string call = EmergencyCall.CallFor(dodge, Rng.NextDouble(), false);
        Finn("A boat on the " + Side(s.Side) + " side! It's coming alongside!");
        bool? saved = null;
        Prompt.Ask("PIRATES · " + Side(s.Side).ToUpperInvariant() + " SIDE", call, "turn away from them", 5f,
                   a => a.IsSignal() ? EmergencyCall.Response.Other : EmergencyCall.Judge(a, dodge),
                   ok => saved = ok,
                   () => Finn("Other way, Sir! \"" + call + "\"!"));
        while (saved == null && !Done) yield return null;
        if (Done) yield break;
        if (saved == true)
        {
            s.Gap += 45f;
            Finn("They fell back! They can't hold on when we turn!");
        }
        else
        {
            s.Gap = 0f;
            Harris("They're climbing on the deck! ...They took two containers and jumped off!");
            ShipyardManager.ShedCargo(2);
            yield return new WaitForSeconds(1.5f);
            s.Gap += 55f;
        }
        yield return new WaitForSeconds(2f);
        if (AtSea && !Done) SteerBackToChannel();
        _nextAsk = Time.time + 4f;
    }

    // ------------------------------------------------------------ the navy

    private void CallNavy(float seconds, string line)
    {
        _navyAt = Time.time + seconds;
        StartCoroutine(NavyRadio(line));
        ShowCountdown();
    }

    private IEnumerator NavyRadio(string line)
    {
        yield return new WaitForSeconds(1.5f);
        // The navy's answer comes over Finn's set.
        Finn("Navy on the radio, Sir: \"" + line + "\"");
    }

    private void UpdateNavy()
    {
        if (_navyAt < 0f) return;
        float left = _navyAt - Time.time;
        if (_countdown != null) _countdown.text = left > 0f ? "NAVY ARRIVING  ·  " + Mathf.CeilToInt(left) + " s" : "";
        if (left > 0f || _frigate != null) { MoveFrigate(); return; }

        // Here it comes: up from ahead on the starboard side, horn sounding, to escort her.
        _frigate = EncounterModels.Frigate(Props);
        _frigateT = 0f;
        MoveFrigate();
        var audio = AudioService.Instance;
        if (audio != null && audio.Sfx != null && audio.Sfx.ShipHorn != null) audio.Play(audio.Sfx.ShipHorn, AudioBus.Effects, 0.9f, 0.75f);
        foreach (var s in _skiffs) s.Fleeing = 0f;
        Prompt.Cancel();
        Finn("The NAVY! Look at that ship! The pirates are running!");
        Banner("THE NAVY", "SAFE", "Now take the payment to Southport.");
        if (_countdownHost != null) Destroy(_countdownHost);
        StartCoroutine(FinishSoon());
    }

    private IEnumerator FinishSoon()
    {
        yield return new WaitForSeconds(3f);
        SteerBackToChannel();
        if (Ship != null && Ship.currentSpeed > 9f) Ship.SetGear(ShipEngine.TelegraphGear.Full);
        Harris("Easing her to full ahead. Well done, Captain.");
        Finish();
    }

    /// <summary>
    /// The frigate keeps station in the ship's frame: in from 70 m ahead to abeam on the
    /// starboard side (6 s), escorts her there, then after 20 s drops astern and is gone.
    /// </summary>
    private void MoveFrigate()
    {
        if (_frigate == null || Ship == null) return;
        _frigateT += Time.deltaTime;
        float ahead = _frigateT < 6f ? Mathf.Lerp(70f, 4f, Mathf.SmoothStep(0f, 1f, _frigateT / 6f))
                    : _frigateT < 20f ? 4f
                    : 4f - (_frigateT - 20f) * 7f;
        var t = Ship.transform;
        Vector3 p = t.position + t.forward * ahead + t.right * 30f;
        p.y = SeaWaves.Displacement(p.x, p.z, SeaWaves.Now).y - 0.4f;
        _frigate.transform.SetPositionAndRotation(p, Quaternion.LookRotation(Channel.Flat(t.forward)));
        if (_frigateT > 34f) { Destroy(_frigate); _frigate = null; }
    }

    private void ShowCountdown()
    {
        if (_countdownHost != null) return;
        _countdownHost = new GameObject("NavyCountdown");
        var layer = ScreenBanner.Layer(_countdownHost, 4);
        if (layer == null) return;
        _countdown = new Label { pickingMode = PickingMode.Ignore };
        _countdown.style.position = Position.Absolute;
        _countdown.style.left = 0; _countdown.style.right = 0;
        _countdown.style.top = 104f;
        _countdown.style.unityTextAlign = TextAnchor.MiddleCenter;
        _countdown.style.fontSize = 22f;
        _countdown.style.letterSpacing = 4f;
        _countdown.style.unityFontStyleAndWeight = FontStyle.Bold;
        _countdown.style.color = new Color(0.62f, 0.95f, 0.82f);
        UiTheme.Shadow(_countdown);
        layer.Add(_countdown);
    }

    protected override void Ended()
    {
        if (ShipSignals.OnMayday == (System.Func<bool>)OnMayday) ShipSignals.OnMayday = null;
        foreach (var s in _skiffs) if (s.Go != null) Destroy(s.Go, 8f);
        if (_countdownHost != null) Destroy(_countdownHost);
    }

    private void LateUpdate()
    {
        // The frigate keeps sailing after the chase is over, and the skiffs keep fleeing until removed.
        if (!Done) return;
        MoveFrigate();
        foreach (var s in _skiffs)
        {
            if (s.Go == null || Ship == null) continue;
            s.Fleeing += Time.deltaTime;
            s.Gap += (SkiffSpeed + Mathf.Max(0f, Ship.currentSpeed)) * 0.9f * Time.deltaTime;
            Place(s);
        }
    }
}
