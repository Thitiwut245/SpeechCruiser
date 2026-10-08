using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Docking at the destination, the way a real port does it, by radio:
///  1. Come in slowly near the berth (within <see cref="RequestRange"/> metres, slow ahead
///     or less) and say "request docking". Too far or too fast and harbour control says so.
///  2. The harbour pilot brings the ship alongside the berth: a smooth curve to the quay,
///     ending parallel to it, bow to the north. Hazards can't be hit on the way.
///  3. Alongside, say "make fast": the mooring lines go out to the bollards, and the voyage
///     is over. Sailing off the berth instead cancels the docking.
/// A story mission that isn't finished yet (a rescue) keeps the berth shut (EndPort.CanFinish).
/// The voyage can only end here, at the berth.
/// </summary>
public class DockingPilot : MonoBehaviour
{
    public enum Stage { AtSea, Piloting, Alongside, MakingFast, Moored }

    public const float RequestRange = 55f;
    public const float MaxRequestSpeed = 3.6f;
    private const float PilotSpeed = 2.4f;
    private const float LeaveDistance = 5f;
    private const float LinesSeconds = 1.4f;

    public static DockingPilot Instance { get; private set; }

    public Stage Current { get; private set; } = Stage.AtSea;
    public Quay Quay { get; private set; }

    /// <summary>The pilot has the ship (or it is tied up): the hull can't hit anything.</summary>
    public static bool Busy => Instance != null && Instance.Current != Stage.AtSea;

    /// <summary>Close enough and slow enough for harbour control to say yes.</summary>
    public bool CanRequest
    {
        get
        {
            var nav = ShipNavigator.Instance;
            if (Current != Stage.AtSea || nav == null || Quay == null) return false;
            return Distance(nav.transform.position) <= RequestRange && Mathf.Abs(nav.Engine.Speed) <= MaxRequestSpeed;
        }
    }

    /// <summary>Metres from the ship to its berth.</summary>
    public float DistanceToBerth => ShipNavigator.Instance != null && Quay != null ? Distance(ShipNavigator.Instance.transform.position) : float.MaxValue;

    private readonly Vector3[] _curve = new Vector3[4];
    private float _length, _travelled, _linesStart;
    private readonly List<LineRenderer> _lines = new List<LineRenderer>();
    private static readonly Vector3[] ShipCleats = { new Vector3(1.8f, 2.2f, 7.6f), new Vector3(2.4f, 1.8f, 2f), new Vector3(2.4f, 1.8f, -2.5f), new Vector3(2.0f, 1.8f, -7.4f) };

    public static DockingPilot Create(Transform parent, Quay quay)
    {
        var go = new GameObject("DockingPilot");
        go.transform.SetParent(parent, false);
        var pilot = go.AddComponent<DockingPilot>();
        pilot.Quay = quay;
        Instance = pilot;
        return pilot;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private float Distance(Vector3 p)
    {
        Vector3 d = p - Quay.Berth;
        d.y = 0f;
        return d.magnitude;
    }

    // ------------------------------------------------------------ orders

    /// <summary>"Request docking." Harbour control answers through Finn.</summary>
    public bool RequestDocking()
    {
        var nav = ShipNavigator.Instance;
        if (nav == null || Quay == null) return false;

        switch (Current)
        {
            case Stage.Piloting: Say("The pilot is already bringing us in, Captain."); return true;
            case Stage.Alongside: Say("We're alongside, Captain. Say \"make fast\" to tie her up."); return true;
            case Stage.MakingFast:
            case Stage.Moored: return true;
        }

        if (EndPort.CanFinish != null && !EndPort.CanFinish()) return true; // the story says why
        float distance = Distance(nav.transform.position);
        if (distance > RequestRange)
        {
            Say("Harbour control says we're too far out, Captain. Bring her within " + Mathf.RoundToInt(RequestRange) +
                " metres of the yellow berth, then ask again. We're " + Mathf.RoundToInt(distance / 10f) * 10 + " metres away.");
            return true;
        }
        if (Mathf.Abs(nav.Engine.Speed) > MaxRequestSpeed)
        {
            Say("Harbour control says slow down first, Captain. Slow ahead or less, then ask again.");
            return true;
        }

        StartPiloting(nav);
        Say("Harbour control: the berth is yours, Captain. The pilot is bringing us alongside.");
        return true;
    }

    /// <summary>"Make fast": the lines go out, if the ship is alongside.</summary>
    public bool MakeFast()
    {
        switch (Current)
        {
            case Stage.Alongside:
                Current = Stage.MakingFast;
                _linesStart = Time.time;
                Say("Lines out! Bow line, springs, stern line... All fast, Captain!", RadioChannel.ChiefEngineer);
                return true;
            case Stage.Piloting:
                Say("Not yet, Captain. The pilot is still bringing us in.");
                return true;
            case Stage.MakingFast:
            case Stage.Moored:
                return true;
            default:
                Say("We're not alongside a berth, Captain. Come in slowly and say \"request docking\" first.");
                return true;
        }
    }

    private void Say(string line, RadioChannel who = RadioChannel.Lookout)
    {
        if (CrewVoice.Instance != null)
            CrewVoice.Instance.SpeakFrom(who, new[] { line }, ShipTelemetry.Capture(), "dock");
    }

    // ------------------------------------------------------------ the pilot

    private void StartPiloting(ShipNavigator nav)
    {
        nav.SetGear(ShipEngine.TelegraphGear.Stop);
        nav.RudderMidships();
        nav.Engine.AbsorbImpact(0f);
        if (nav.Movement != null) nav.Movement.KillTurn();

        // A smooth curve from the ship to its berth, arriving bow-first along the quay.
        Vector3 from = nav.transform.position, to = Quay.Berth;
        from.y = to.y = nav.transform.position.y;
        Vector3 forward = nav.transform.forward; forward.y = 0f; forward.Normalize();
        float reach = Mathf.Max(Vector3.Distance(from, to), 6f) * 0.4f;
        _curve[0] = from;
        _curve[1] = from + forward * reach;
        _curve[2] = to - Vector3.forward * reach;
        _curve[3] = to;
        _length = 0f;
        Vector3 last = from;
        for (int i = 1; i <= 32; i++)
        {
            Vector3 p = Bezier(i / 32f);
            _length += Vector3.Distance(last, p);
            last = p;
        }
        _travelled = 0f;
        Current = Stage.Piloting;
    }

    private Vector3 Bezier(float t)
    {
        float u = 1f - t;
        return u * u * u * _curve[0] + 3f * u * u * t * _curve[1] + 3f * u * t * t * _curve[2] + t * t * t * _curve[3];
    }

    private void Update()
    {
        var nav = ShipNavigator.Instance;
        if (nav == null || Quay == null) return;
        if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameState.Playing && Current != Stage.Moored) return;

        switch (Current)
        {
            case Stage.Piloting: Pilot(nav); break;
            case Stage.Alongside:
                // Sailed off the berth before tying up: start again.
                if (Distance(nav.transform.position) > LeaveDistance)
                {
                    Current = Stage.AtSea;
                    Say("We've left the berth, Captain. Request docking again when you're ready.");
                }
                break;
            case Stage.MakingFast: ThrowLines(nav); break;
        }
        if (_lines.Count > 0) TendLines(nav);
        Quay.ShowOutline(Current != Stage.Moored && Current != Stage.MakingFast);
    }

    private void Pilot(ShipNavigator nav)
    {
        // Steady at first, easing off over the last few metres so she settles gently.
        float left = _length - _travelled;
        float speed = Mathf.Lerp(0.6f, PilotSpeed, Mathf.Clamp01(left / 8f));
        _travelled = Mathf.Min(_length, _travelled + speed * Time.deltaTime);
        float t = _length > 0.01f ? _travelled / _length : 1f;

        Vector3 p = Bezier(t);
        Vector3 ahead = Bezier(Mathf.Min(1f, t + 0.02f)) - p;
        ahead.y = 0f;
        Quaternion heading = ahead.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(ahead) : Quaternion.identity;
        // The last stretch lines her up exactly with the quay.
        heading = Quaternion.Slerp(heading, Quaternion.identity, Mathf.InverseLerp(0.85f, 1f, t));
        nav.transform.SetPositionAndRotation(p, heading);

        if (t >= 1f)
        {
            Current = Stage.Alongside;
            Say("We're alongside, Captain! Say \"make fast\" to tie her up.");
        }
    }

    private void ThrowLines(ShipNavigator nav)
    {
        if (_lines.Count == 0)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Mooring line" };
            mat.SetColor("_BaseColor", new Color(0.86f, 0.72f, 0.42f));
            for (int i = 0; i < ShipCleats.Length && i < Quay.Bollards.Count; i++)
            {
                var lr = new GameObject("MooringLine").AddComponent<LineRenderer>();
                lr.transform.SetParent(transform, false);
                lr.sharedMaterial = mat;
                lr.widthMultiplier = 0.22f;
                lr.positionCount = 12;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _lines.Add(lr);
            }
        }
        if (Time.time - _linesStart < LinesSeconds) return;

        Current = Stage.Moored;
        if (GameManager.Instance != null) GameManager.Instance.MissionSuccess();
    }

    /// <summary>The lines: thrown out from the ship, sagging a little, following the ship as it moves on the water.</summary>
    private void TendLines(ShipNavigator nav)
    {
        Transform hull = nav.Movement != null ? nav.Movement.Visual : nav.transform;
        float grow = Mathf.Clamp01((Time.time - _linesStart) / LinesSeconds);
        for (int i = 0; i < _lines.Count; i++)
        {
            Vector3 a = hull.TransformPoint(ShipCleats[i]);
            Vector3 b = Vector3.Lerp(a, Quay.Bollards[i], Mathf.Clamp01(grow * 1.3f - i * 0.1f));
            for (int k = 0; k < 12; k++)
            {
                float s = k / 11f;
                _lines[i].SetPosition(k, Vector3.Lerp(a, b, s) + Vector3.down * Mathf.Sin(s * Mathf.PI) * 0.6f);
            }
        }
    }
}
