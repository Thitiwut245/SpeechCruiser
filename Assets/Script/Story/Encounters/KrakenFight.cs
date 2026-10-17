using System.Collections;
using UnityEngine;

/// <summary>
/// Mission 2-4, the Kraken. In the Deep Water (no rocks) it comes for the ship:
///   1. Strikes. An arm rises out of the sea off one bow; Finn calls the dodge, any of the standard
///      turns away from it ("hard a port", "port twenty"...). In time, the arm slams into empty
///      sea; late, it lands on the ship (a hit, containers overboard). After each, the helmsman
///      steers back to the channel. Three strikes.
///   2. The face. The head breaks the surface ahead and Finn calls "sound the horn": the old
///      sailors' cure. The horn drives it under; late, it strikes again and asks again (three
///      tries, then it tires and sinks anyway).
///   3. Away. "Flank speed!" to leave the Deep Water. Then it is over, and the dock opens.
/// Orders are asked for with a CallPrompt: time slows, and the radio's delay never counts.
/// </summary>
public class KrakenFight : StoryEncounter
{
    private const int Strikes = 3;
    private const float StrikeGap = 6f;     // game seconds between strikes
    private const float HornTries = 3;
    private const int RingArms = 8;

    private enum Stage { Strikes, Face, Away }
    private Stage _stage;
    private int _strike;
    private float _next;
    private int _hornTries;
    private TentacleRig _arm;
    private GameObject _head;
    private float _faceSide;
    private float _headDepth = 16f;
    private readonly System.Collections.Generic.List<TentacleRig> _ring = new System.Collections.Generic.List<TentacleRig>();

    protected override void Begin()
    {
        ShipSignals.OnHorn = OnHorn;
        _stage = Stage.Strikes;
        _next = Time.time + 4f;
        Finn("Captain! Something huge, under the water! It's moving with us!");
        Harris("The old stories... Steady, everyone. Steady.");
        Banner("THE DEEP WATER", "KRAKEN", "Dodge its arms. Then sound the horn.");
    }

    protected override void OnDestroy()
    {
        if (ShipSignals.OnHorn == (System.Func<bool>)OnHorn) ShipSignals.OnHorn = null;
        base.OnDestroy();
    }

    protected override void Tick()
    {
        if (Prompt.Running || Time.time < _next) return;
        switch (_stage)
        {
            case Stage.Strikes:
                if (_strike < Strikes) StartCoroutine(Strike());
                else StartCoroutine(Face());
                _next = float.MaxValue; // the coroutine sets the next time
                break;
        }
    }

    // ------------------------------------------------------------ 1. strikes

    private IEnumerator Strike()
    {
        _strike++;
        float side = Rng.NextDouble() < 0.5 ? -1f : 1f;
        _arm = EncounterModels.Tentacle(Props, Beside(side, 13f, 18f), 22f, 1.3f);
        _arm.Rise(1f);
        Finn("An arm! On the " + Side(side) + " bow!");
        yield return new WaitForSeconds(1.2f);

        var dodge = AwayFrom(side);
        string call = EmergencyCall.CallFor(dodge, Rng.NextDouble(), false);
        bool? saved = null;
        Prompt.Ask("KRAKEN · ARM ON THE " + Side(side).ToUpperInvariant() + " BOW", call, "turn away from it",
                   _strike == 1 ? 6.5f : 5f,
                   a => a.IsSignal() ? EmergencyCall.Response.Other : EmergencyCall.Judge(a, dodge),
                   ok => saved = ok,
                   () => Finn("Other way, Sir! \"" + call + "\"!"));
        while (saved == null && !Done) yield return null;
        if (Done) yield break;

        // In time, it slams where the ship was going; late, it lands on her.
        var nav = Ship;
        if (saved == true)
        {
            _arm.SlamAt(nav.transform.position + nav.transform.forward * 22f + nav.transform.right * side * 4f);
            Finn("It missed us! Good order, Captain!", "Missed! Keep turning, Sir!");
        }
        else
        {
            _arm.SlamAt(nav.transform.position);
            yield return new WaitForSeconds(0.45f);
            var hull = nav.GetComponentInChildren<ShipHull>();
            if (hull != null) hull.Strike(1);
            Harris("It hit us! Next time turn away faster, Captain!");
        }
        yield return new WaitForSeconds(1.6f);
        _arm.Rise(0f);
        Destroy(_arm.gameObject, 3f);
        if (!AtSea) yield break;
        SteerBackToChannel();
        _next = Time.time + StrikeGap;
    }

    // ------------------------------------------------------------ 2. the face

    private IEnumerator Face()
    {
        _stage = Stage.Face;
        var nav = Ship;
        _faceSide = Rng.NextDouble() < 0.5 ? -1f : 1f;
        Vector3 at = HeadSpot();
        _head = EncounterModels.Head(Props, at + Vector3.down * _headDepth, 0f);
        for (int i = 0; i < RingArms; i++)
        {
            var arm = EncounterModels.Tentacle(Props, at, i % 2 == 0 ? 21f : 17f, 1.25f);
            arm.Rise(0.9f);
            arm.Coil(i % 2 == 0 ? 1f : 0.75f);
            _ring.Add(arm);
        }
        nav.SetGear(ShipEngine.TelegraphGear.Slow); // the helmsman eases her: there's a monster ahead
        Finn("It's coming up! Dead ahead! Look at its EYES!");
        for (float t = 0f; t < 2.2f; t += Time.deltaTime)
        {
            _headDepth = Mathf.Lerp(16f, 2.5f, Mathf.SmoothStep(0f, 1f, t / 2.2f));
            yield return null;
        }
        AskHorn();
    }

    /// <summary>Where the head rides: ahead of the bow, a little to one side, keeping pace with the ship.</summary>
    private Vector3 HeadSpot() => Beside(_faceSide, 10f, 33f);

    // The head and its arms keep their place ahead of the ship, facing her, while it rises and sinks.
    private void LateUpdate()
    {
        if (_head == null || Ship == null) return;
        Vector3 at = HeadSpot();
        Vector3 look = Channel.Flat(Ship.transform.position - at);
        float yaw = look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look).eulerAngles.y : 0f;
        _head.transform.SetPositionAndRotation(at + Vector3.down * _headDepth, Quaternion.Euler(0f, yaw, 0f));
        // The arms grow from under the head all round it and reach outward, curling at the tips;
        // the ones toward the ship reach furthest.
        for (int i = 0; i < _ring.Count; i++)
        {
            if (_ring[i] == null) continue;
            float a = yaw + i * 360f / _ring.Count + 22f;
            Vector3 outward = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
            Vector3 p = at + outward * 5.5f;
            p.y = _ring[i].transform.position.y;
            _ring[i].transform.position = p;
            float front = Vector3.Dot(outward, Quaternion.Euler(0f, yaw, 0f) * Vector3.forward);
            _ring[i].Lean(outward, 42f + 14f * front + Mathf.Sin(Time.time * 0.7f + i) * 6f);
        }
    }

    private void AskHorn()
    {
        _hornTries++;
        Prompt.Ask("THE KRAKEN", "sound the horn", "the old sailors' cure", _hornTries == 1 ? 7f : 6f,
                   a => a == ShipAction.SoundHorn ? EmergencyCall.Response.Saves : EmergencyCall.Response.Other,
                   ok => StartCoroutine(ok ? DrivenOff() : StruckAgain()));
        Finn("The horn, Captain! Say \"sound the horn\"!");
    }

    private bool OnHorn()
    {
        // During the face the prompt hears the order itself; any other time the beast just flinches.
        if (_stage != Stage.Face) Finn("It hates that horn, Sir! Good!");
        return true;
    }

    private IEnumerator StruckAgain()
    {
        var nav = Ship;
        var arm = _ring.Count > 0 ? _ring[Rng.Next(_ring.Count)] : null;
        if (arm != null) arm.SlamAt(nav.transform.position);
        yield return new WaitForSeconds(0.45f);
        var hull = nav.GetComponentInChildren<ShipHull>();
        if (hull != null) hull.Strike(1);
        if (!AtSea) yield break;
        if (_hornTries >= HornTries)
        {
            Harris("It's... tired. It's going down. Lucky, lucky us.");
            yield return Sink();
            yield break;
        }
        Harris("It hit us again! The horn, Captain! Now!");
        yield return new WaitForSeconds(1.2f);
        AskHorn();
    }

    private IEnumerator DrivenOff()
    {
        Finn("Look at it shake! It's going under!");
        yield return Sink();
    }

    private IEnumerator Sink()
    {
        foreach (var arm in _ring) if (arm != null) arm.Rise(0f);
        float depth = _headDepth;
        for (float t = 0f; t < 2.5f && _head != null; t += Time.deltaTime)
        {
            _headDepth = Mathf.Lerp(depth, 20f, Mathf.SmoothStep(0f, 1f, t / 2.5f));
            yield return null;
        }
        if (_head != null) SplashRing.Spawn(HeadSpot(), 26f, new Color(1f, 1f, 1f, 0.8f));
        if (!AtSea) yield break;
        _stage = Stage.Away;
        yield return new WaitForSeconds(0.8f);
        Prompt.Ask("GET AWAY", "flank speed", "out of the Deep Water", 7f,
                   a => a == ShipAction.EngineFlank || a == ShipAction.EngineFull || a == ShipAction.EngineIncrease
                        ? EmergencyCall.Response.Saves
                        : a.IsAsternGear() || a == ShipAction.EngineStop ? EmergencyCall.Response.WrongWay : EmergencyCall.Response.Other,
                   ok =>
                   {
                       if (!ok && Ship != null) { Ship.SetGear(ShipEngine.TelegraphGear.Full); Harris("I'm giving her full ahead anyway, Captain!"); }
                       else Harris("Everything she's got, Captain!");
                       SteerBackToChannel();
                       Banner("THE KRAKEN", "DRIVEN OFF", "Now take Sam's supplies home.");
                       Finish();
                   },
                   () => Harris("Not backwards, Captain! Forward! \"Flank speed\"!"));
        Finn("Now get us out of here! Say \"flank speed\"!");
    }

    protected override void Ended()
    {
        if (ShipSignals.OnHorn == (System.Func<bool>)OnHorn) ShipSignals.OnHorn = null;
        foreach (var arm in _ring) if (arm != null) { arm.Rise(0f); Destroy(arm.gameObject, 3f); }
        _ring.Clear();
        if (_head != null) Destroy(_head, 3f);
    }
}
