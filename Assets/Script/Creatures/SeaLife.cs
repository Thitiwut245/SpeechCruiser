using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Everyday life at sea, in every mode: gulls wheeling over both harbours (two come out to
/// follow the ship while it is near one), a pod of dolphins now and then riding alongside the
/// bow, leaping, and, in Free Sail, a humpback blowing far off. Finn calls out what he sees,
/// and the crew can be asked about it (SmallTalk), which is good speaking practice. Scenery
/// only: no colliders, not on the radar.
/// </summary>
public class SeaLife : MonoBehaviour
{
    public static SeaLife Instance { get; private set; }

    /// <summary>What was last seen ("dolphins", "a whale"), and when (game time); null if nothing yet.</summary>
    public static string LastSeen { get; private set; }
    public static float LastSeenAt { get; private set; } = -999f;

    /// <summary>True if <paramref name="what"/> was seen in the last <paramref name="seconds"/>.</summary>
    public static bool Recently(string what, float seconds = 90f) => LastSeen == what && Time.time - LastSeenAt < seconds;

    private Vector3[] _harbours;
    private Rect _water;
    private readonly List<Gull> _gulls = new List<Gull>();
    private float _nextDolphins, _nextWhale = -1f;
    private bool _whaleDone;

    private class Gull
    {
        public CreatureModels.GullRig Rig;
        public Vector3 Home;
        public float Radius, Height, Speed, Phase, NextFlap, FlapUntil;
        public bool Follower;
        public Vector3 Pos;
    }

    /// <summary>Puts life into a new level: gulls at <paramref name="harbours"/>, the rest over <paramref name="water"/>.</summary>
    public static SeaLife Create(Transform parent, Vector3[] harbours, Rect water)
    {
        var go = new GameObject("Sea Life");
        go.transform.SetParent(parent, false);
        var life = go.AddComponent<SeaLife>();
        life._harbours = harbours;
        life._water = water;
        Instance = life;
        LastSeen = null;
        LastSeenAt = -999f;
        life.Begin();
        return life;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Begin()
    {
        for (int h = 0; h < _harbours.Length; h++)
            for (int i = 0; i < 5; i++)
            {
                var g = new Gull
                {
                    Rig = CreatureModels.Gull(transform),
                    Home = _harbours[h] + new Vector3(Random.Range(6f, 22f), 0f, Random.Range(-14f, 14f)),
                    Radius = Random.Range(6f, 14f),
                    Height = Random.Range(9f, 17f),
                    Speed = Random.Range(0.35f, 0.55f) * (Random.value < 0.5f ? -1f : 1f),
                    Phase = Random.Range(0f, Mathf.PI * 2f),
                    Follower = i < 2,
                };
                g.Pos = g.Home + new Vector3(g.Radius, g.Height, 0f);
                _gulls.Add(g);
            }
        _nextDolphins = Time.time + Random.Range(40f, 70f);
        // A whale far off, once a voyage, in Free Sail only (the story has its own deep shadow).
        if (StoryProgress.ActiveMission == null) _nextWhale = Time.time + Random.Range(55f, 140f);
    }

    private static bool AtSea => GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.Playing;

    private void Update()
    {
        var nav = ShipNavigator.Instance;
        UpdateGulls(nav);
        if (nav == null || !AtSea) return;

        if (Time.time >= _nextDolphins)
        {
            if (CanShow(nav, 120f, 2.5f))
            {
                StartCoroutine(DolphinPod(nav));
                _nextDolphins = Time.time + Random.Range(100f, 160f);
            }
            else _nextDolphins = Time.time + 8f;
        }
        if (!_whaleDone && _nextWhale > 0f && Time.time >= _nextWhale)
        {
            if (CanShow(nav, 160f, 0f)) { _whaleDone = true; DistantWhale(nav); }
            else _nextWhale = Time.time + 10f;
        }
    }

    /// <summary>Open water, under way, nothing dangerous ahead, away from the harbours.</summary>
    private bool CanShow(ShipNavigator nav, float awayFromHarbour, float minKnots)
    {
        if (Mathf.Abs(nav.currentSpeed) < minKnots) return false;
        if (RadarSystem.Instance != null && RadarSystem.Instance.IsThreat) return false;
        Vector3 p = nav.transform.position;
        foreach (var h in _harbours) if (Vector2.Distance(new Vector2(p.x, p.z), new Vector2(h.x, h.z)) < awayFromHarbour) return false;
        return true;
    }

    // ------------------------------------------------------------------ gulls

    private void UpdateGulls(ShipNavigator nav)
    {
        Vector3 ship = nav != null ? nav.transform.position : Vector3.zero;
        bool shipNearPort = false;
        if (nav != null) foreach (var h in _harbours) if (Vector3.Distance(ship, h) < 110f) shipNearPort = true;

        foreach (var g in _gulls)
        {
            // Followers leave the harbour to wheel over the ship's stern while it is near port.
            Vector3 centre = g.Follower && shipNearPort ? ship - (nav != null ? nav.transform.forward * 6f : Vector3.zero) : g.Home;
            float radius = g.Follower && shipNearPort ? 5f : g.Radius;
            float a = Time.time * g.Speed + g.Phase;
            Vector3 target = centre + new Vector3(Mathf.Cos(a) * radius, g.Height + Mathf.Sin(a * 2.3f) * 1.2f, Mathf.Sin(a) * radius);
            Vector3 before = g.Pos;
            g.Pos = Vector3.Lerp(g.Pos, target, 1f - Mathf.Exp(-1.6f * Time.deltaTime));
            Vector3 v = g.Pos - before;
            var t = g.Rig.Root;
            t.position = g.Pos;
            if (v.sqrMagnitude > 1e-6f)
            {
                float bank = Mathf.Clamp(-Mathf.Sign(g.Speed) * 28f, -35f, 35f);
                t.rotation = Quaternion.Slerp(t.rotation, Quaternion.LookRotation(v.normalized) * Quaternion.Euler(0f, 0f, bank), 4f * Time.deltaTime);
            }
            // Flap for a moment, then glide on slightly raised wings.
            if (Time.time >= g.NextFlap)
            {
                g.FlapUntil = Time.time + Random.Range(0.8f, 1.6f);
                g.NextFlap = g.FlapUntil + Random.Range(1.5f, 3.5f);
            }
            float wing = Time.time < g.FlapUntil ? Mathf.Sin(Time.time * 13f + g.Phase) * 38f : 10f + Mathf.Sin(Time.time * 2f + g.Phase) * 3f;
            g.Rig.WingR.localRotation = Quaternion.Euler(0f, 0f, wing);
            g.Rig.WingL.localRotation = Quaternion.Euler(0f, 0f, -wing);
        }
    }

    // ------------------------------------------------------------------ dolphins

    private System.Collections.IEnumerator DolphinPod(ShipNavigator nav)
    {
        int count = Random.Range(2, 5);
        float side = Random.value < 0.5f ? -1f : 1f;
        var pod = new List<(CreatureModels.DolphinRig rig, float lateral, float ahead, float period, float phase)>();
        for (int i = 0; i < count; i++)
        {
            var rig = CreatureModels.Dolphin(transform);
            float lateral = side * Random.Range(9f, 15f), ahead = Random.Range(4f, 16f);
            rig.Root.position = nav.transform.position + nav.transform.right * lateral * 2.5f + nav.transform.forward * (ahead + 12f) + Vector3.down * 2f;
            pod.Add((rig, lateral, ahead, Random.Range(2.2f, 3.1f), Random.Range(0f, 3f)));
        }
        Seen("dolphins");
        Call(side < 0f
            ? new[] { "Dolphins on the left, Sir! They're racing the bow! Over.", "Look, Captain! A pod of dolphins, port side! Over." }
            : new[] { "Dolphins on the right, Sir! They're racing the bow! Over.", "Look, Captain! A pod of dolphins, starboard side! Over." }, "dolphins");

        const float stay = 22f;
        float start = Time.time;
        var wasUp = new bool[count];
        while (Time.time - start < stay + 6f && nav != null)
        {
            float age = Time.time - start;
            float leave = Mathf.Clamp01((age - stay) / 6f);
            for (int i = 0; i < pod.Count; i++)
            {
                var (rig, lateral, ahead, period, phase) = pod[i];
                Vector3 target = nav.transform.position + nav.transform.right * lateral * (1f + leave * 2.5f) + nav.transform.forward * (ahead + leave * 20f);
                Vector3 p = rig.Root.position;
                Vector3 flat = Vector3.Lerp(new Vector3(p.x, 0f, p.z), new Vector3(target.x, 0f, target.z), 1f - Mathf.Exp(-1.2f * Time.deltaTime));
                // Porpoising: a short leap clear of the water, then a stretch just under it.
                float u = Mathf.Repeat((age + phase) / period, 1f);
                float leap = u < 0.38f ? Mathf.Sin(Mathf.PI * u / 0.38f) : 0f;
                float y = SeaWaves.HeightAt(flat.x, flat.z) + Mathf.Lerp(-0.9f, 1.3f, leap) - leave * 3f - Mathf.Clamp01(1f - age / 1.5f) * 2f;
                Vector3 now = new Vector3(flat.x, y, flat.z);
                Vector3 move = now - p;
                rig.Root.position = now;
                Vector3 across = new Vector3(move.x, 0f, move.z);
                Vector3 facing = across.sqrMagnitude > 1e-4f ? across.normalized : nav.transform.forward;
                // Nose up leaving the water, down going back in, as if always swimming forward.
                float rise = Mathf.Cos(Mathf.PI * Mathf.Clamp01(u / 0.38f)) * (u < 0.38f ? 1f : 0f);
                float pitch = -rise * 35f;
                rig.Root.rotation = Quaternion.Slerp(rig.Root.rotation, Quaternion.LookRotation(facing) * Quaternion.Euler(pitch, 0f, 0f), 10f * Time.deltaTime);
                rig.Fluke.localRotation = Quaternion.Euler(Mathf.Sin(age * 9f + i) * 18f, 0f, 0f);
                bool up = y > SeaWaves.HeightAt(now.x, now.z) + 0.15f;
                if (up != wasUp[i] && leave < 0.5f)
                {
                    Spray.Burst(new Vector3(now.x, SeaWaves.HeightAt(now.x, now.z), now.z), 1f, 10);
                    SeaSplash.Add(now, 1.6f, 0.8f, 1.6f);
                }
                wasUp[i] = up;
            }
            yield return null;
        }
        foreach (var d in pod) if (d.rig.Root != null) Destroy(d.rig.Root.gameObject);
    }

    // ------------------------------------------------------------------ the distant whale

    private void DistantWhale(ShipNavigator nav)
    {
        float side = Random.value < 0.5f ? -1f : 1f;
        Vector3 at = nav.transform.position + nav.transform.right * side * Random.Range(55f, 80f) + nav.transform.forward * Random.Range(30f, 60f);
        at.x = Mathf.Clamp(at.x, _water.xMin + 10f, _water.xMax - 10f);
        at.z = Mathf.Clamp(at.z, _water.yMin + 10f, _water.yMax - 10f);
        at.y = 0f;
        Vector3 heading = Quaternion.Euler(0f, Random.Range(-25f, 25f), 0f) * nav.transform.forward;
        SeaSight.CreateWhale(transform, at, heading, 15f, CreatureModels.WhaleColours.Humpback);
        Seen("a whale");
        Call(side < 0f
            ? new[] { "Whale on the left, Captain! Look at that spout! Over.", "Captain, a whale, port side! Over." }
            : new[] { "Whale on the right, Captain! Look at that spout! Over.", "Captain, a whale, starboard side! Over." }, "whale");
    }

    private static void Seen(string what)
    {
        LastSeen = what;
        LastSeenAt = Time.time;
    }

    /// <summary>Finn calls it out, unless something more important is going on.</summary>
    private static void Call(string[] lines, string key)
    {
        if (CrewVoice.Instance == null) return;
        if (TutorialManager.Instance != null && TutorialManager.Instance.IsActive) return;
        var t = ShipTelemetry.Capture();
        if (t.HazardOnCourse) return;
        CrewVoice.Instance.SpeakFrom(RadioChannel.Lookout, lines, t, "life:" + key);
    }
}
