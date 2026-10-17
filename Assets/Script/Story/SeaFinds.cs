using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Things to find off the route in story mode, so the open sea is worth sailing: three per
/// mission (Logbook.KindFor: a drifting container, an old buoy, a wreck, floating barrels), each in
/// open water two or three cells (50-75 m) off the solution path, one in each third of the voyage,
/// always reachable from the start. The lookout reports each by bearing and distance, in bridge
/// words and plain words ("fine on the starboard bow, ahead on the right, about 200 metres"),
/// and again when the ship is close; steering over it picks it up: a logbook entry, points
/// (StoryScore.FindPoints) and a ring of coins round it. Two more spots in tight water between
/// rocks hold a few coins for whoever dares. Checkpoints keep what was found.
/// The StoryDirector adds it.
/// </summary>
public class SeaFinds : MonoBehaviour
{
    private const float ReportRange = 230f;   // the lookout's first call
    private const float CloseRange = 110f;    // and the second, if it's still out there
    private const float RepeatAfter = 20f;
    private const float Gap = 12f;            // between any two of his reports
    private const float Reach = 9f;           // from the hull's centre line: steered over it
    private const float BowZ = 9.3f, SternZ = -8f;

    public static SeaFinds Current { get; private set; }

    public int Total => _finds.Count;
    public int Found { get; private set; }

    private sealed class Find
    {
        public FindKind Kind;
        public int Index;
        public Vector3 At;
        public Transform T;
        public bool Taken;
        public int Reports;
        public float LastReport = -100f, TakenAt = -1f, Phase;
    }

    private readonly List<Find> _finds = new List<Find>();
    private readonly List<Vector3> _risk = new List<Vector3>();
    private Transform _root;
    private bool _placed;
    private float _nextReport, _nextRipple;
    private Checkpoints.Snapshot _pending;

    private static string MissionId => StoryProgress.ActiveMission != null ? StoryProgress.ActiveMission.Id : "";

    private void Awake() => Current = this;

    private void OnDestroy()
    {
        if (Current == this) Current = null;
        if (_root != null) Destroy(_root.gameObject);
    }

    // ------------------------------------------------------------ choosing the cells

    /// <summary>
    /// Cells for <paramref name="count"/> finds: open, reachable from <paramref name="start"/>, inside the
    /// map's edge and clear of both harbours, two or three cells off the path (one if nothing else
    /// will do), one in each of <paramref name="count"/> bands along the route. The same seed, the same cells.
    /// </summary>
    public static List<Vector2Int> ChooseFinds(int columns, int rows, Func<int, int, bool> blocked, Vector2Int start,
                                               ICollection<Vector2Int> path, int seed, int count)
    {
        var open = Reachable(columns, rows, blocked, start);
        var rng = new System.Random(seed);
        var chosen = new List<Vector2Int>();
        int first = 4, last = rows - 5;
        if (last < first) return chosen;
        for (int band = 0; band < count; band++)
        {
            int from = first + (last - first + 1) * band / count, to = first + (last - first + 1) * (band + 1) / count - 1;
            var best = new List<Vector2Int>();
            var fallback = new List<Vector2Int>();
            for (int y = from; y <= to; y++)
                for (int x = 1; x < columns - 1; x++)
                {
                    var c = new Vector2Int(x, y);
                    if (!open.Contains(c)) continue;
                    int d = DistanceTo(path, c);
                    if (d == 2 || d == 3) best.Add(c);
                    else if (d >= 1) fallback.Add(c);
                }
            var pool = best.Count > 0 ? best : fallback;
            if (pool.Count > 0) chosen.Add(pool[rng.Next(pool.Count)]);
        }
        return chosen;
    }

    /// <summary>Open, reachable cells off the path with rocks on two sides or more: tight water. One per band.</summary>
    public static List<Vector2Int> ChooseRisk(int columns, int rows, Func<int, int, bool> blocked, Vector2Int start,
                                              ICollection<Vector2Int> path, ICollection<Vector2Int> taken, int seed, int count)
    {
        var open = Reachable(columns, rows, blocked, start);
        var rng = new System.Random(seed * 7 + 3);
        var chosen = new List<Vector2Int>();
        int first = 4, last = rows - 5;
        if (last < first) return chosen;
        for (int band = 0; band < count; band++)
        {
            int from = first + (last - first + 1) * band / count, to = first + (last - first + 1) * (band + 1) / count - 1;
            var pool = new List<Vector2Int>();
            for (int y = from; y <= to; y++)
                for (int x = 1; x < columns - 1; x++)
                {
                    var c = new Vector2Int(x, y);
                    if (!open.Contains(c) || taken.Contains(c) || DistanceTo(path, c) < 1) continue;
                    int rocks = 0;
                    foreach (var n in Neighbours(c))
                        if (n.x < 0 || n.y < 0 || n.x >= columns || n.y >= rows || blocked(n.x, n.y)) rocks++;
                    if (rocks >= 2) pool.Add(c);
                }
            if (pool.Count > 0) chosen.Add(pool[rng.Next(pool.Count)]);
        }
        return chosen;
    }

    private static HashSet<Vector2Int> Reachable(int columns, int rows, Func<int, int, bool> blocked, Vector2Int start)
    {
        var seen = new HashSet<Vector2Int> { start };
        var queue = new Queue<Vector2Int>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            foreach (var n in Neighbours(c))
            {
                if (n.x < 0 || n.y < 0 || n.x >= columns || n.y >= rows || blocked(n.x, n.y) || !seen.Add(n)) continue;
                queue.Enqueue(n);
            }
        }
        return seen;
    }

    private static IEnumerable<Vector2Int> Neighbours(Vector2Int c)
    {
        yield return new Vector2Int(c.x + 1, c.y);
        yield return new Vector2Int(c.x - 1, c.y);
        yield return new Vector2Int(c.x, c.y + 1);
        yield return new Vector2Int(c.x, c.y - 1);
    }

    /// <summary>Cells to the nearest path cell, diagonals counting one (Chebyshev).</summary>
    private static int DistanceTo(ICollection<Vector2Int> path, Vector2Int c)
    {
        int best = int.MaxValue;
        foreach (var p in path) best = Mathf.Min(best, Mathf.Max(Mathf.Abs(p.x - c.x), Mathf.Abs(p.y - c.y)));
        return best;
    }

    // ------------------------------------------------------------ placing

    /// <summary>Lays out the finds for the level just built (once); PathCoins asks for its coin spots here too.</summary>
    public void Place()
    {
        var level = LevelGenerator.Instance;
        var mission = StoryProgress.ActiveMission;
        if (_placed || level == null || mission == null || level.SolutionWorldPath == null || level.SolutionWorldPath.Count < 2) return;
        _placed = true;

        var path = new HashSet<Vector2Int>();
        foreach (var p in level.SolutionWorldPath) path.Add(level.CellFromWorld(p));
        int seed = mission.Job.Seed != 0 ? mission.Job.Seed : mission.Id.GetHashCode();
        var cells = ChooseFinds(level.GridColumns, level.GridRows, level.IsObstacle, level.StartCell, path, seed, Logbook.FindsPerMission);
        var risk = ChooseRisk(level.GridColumns, level.GridRows, level.IsObstacle, level.StartCell, path, cells, seed, 2);

        _root = new GameObject("SeaFinds").transform;
        for (int i = 0; i < cells.Count; i++)
        {
            Vector3 at = level.WorldFromCell(cells[i].x, cells[i].y);
            at.y = 0f;
            var kind = Logbook.KindFor(mission, i);
            var f = new Find { Kind = kind, Index = i, At = at, Phase = i * 1.7f };
            f.T = Model(kind, _root);
            _finds.Add(f);
        }
        foreach (var c in risk)
        {
            Vector3 at = level.WorldFromCell(c.x, c.y);
            at.y = 0f;
            _risk.Add(at);
        }
        if (_pending != null) Restore(_pending);
    }

    /// <summary>Where the coins off the channel go: a ring of six round each find, a short arc of five in each tight spot.</summary>
    public List<Vector3> CoinSpots()
    {
        Place();
        var spots = new List<Vector3>();
        foreach (var f in _finds)
            for (int k = 0; k < 6; k++)
            {
                float a = k * Mathf.PI * 2f / 6f;
                spots.Add(f.At + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 11f);
            }
        foreach (var r in _risk)
            for (int k = 0; k < 5; k++)
                spots.Add(r + new Vector3((k - 2) * 3f, 0f, Mathf.Abs(k - 2) * -1.5f));
        return spots;
    }

    // ------------------------------------------------------------ checkpoints

    public void Save(Checkpoints.Snapshot s)
    {
        s.FindsTaken = new bool[_finds.Count];
        for (int i = 0; i < _finds.Count; i++) s.FindsTaken[i] = _finds[i].Taken;
    }

    public void Restore(Checkpoints.Snapshot s)
    {
        if (!_placed) { _pending = s; return; }
        _pending = null;
        Found = 0;
        for (int i = 0; i < _finds.Count; i++)
        {
            var f = _finds[i];
            f.Taken = s.FindsTaken != null && i < s.FindsTaken.Length && s.FindsTaken[i];
            f.TakenAt = -1f;
            f.T.gameObject.SetActive(!f.Taken);
            f.T.localScale = Vector3.one;
            if (f.Taken) { Found++; f.Reports = 2; }
        }
    }

    // ------------------------------------------------------------ at sea

    private void Update()
    {
        var gm = GameManager.Instance;
        if (gm == null || gm.CurrentState != GameState.Playing) return;
        Place();
        var nav = ShipNavigator.Instance;
        if (nav == null) return;
        Transform ship = nav.transform;

        foreach (var f in _finds)
        {
            if (f.Taken)
            {
                if (f.TakenAt < 0f) continue;
                float k = (Time.time - f.TakenAt) / 0.8f;
                if (k >= 1f) { f.T.gameObject.SetActive(false); f.TakenAt = -1f; continue; }
                f.T.localScale = Vector3.one * (1f - k * 0.9f);
                Float(f, k * 4f);
                continue;
            }
            Float(f, 0f);
            if (!DockingPilot.Busy && PathCoins.Reaches(ship.position + ship.forward * BowZ, ship.position + ship.forward * SternZ, f.At, Reach))
                Take(f);
        }

        // A little white water round the nearest one, so it can be seen against the waves.
        if (Time.time >= _nextRipple)
        {
            _nextRipple = Time.time + 1.4f;
            foreach (var f in _finds)
                if (!f.Taken && Channel.Flat(f.At - ship.position).magnitude < 160f) SeaSplash.Add(f.At, 4f, 0.3f, 2f);
        }

        Report(ship);
    }

    private void Float(Find f, float rise)
    {
        float t = Time.time + f.Phase;
        float wave = SeaWaves.HeightAt(f.At.x, f.At.z);
        f.T.position = new Vector3(f.At.x, wave + Mathf.Sin(t * 0.9f) * 0.15f + rise, f.At.z);
        f.T.rotation = Quaternion.Euler(Mathf.Sin(t * 0.7f) * 4f, f.Phase * 40f + Mathf.Sin(t * 0.2f) * 6f, Mathf.Sin(t * 0.6f) * 5f);
    }

    private static bool Busy()
    {
        if (DialoguePlayer.IsPlaying || EmergencyCall.Running || DockingPilot.Busy) return true;
        if (StoryEncounter.Current != null && !StoryEncounter.Current.Done) return true;
        foreach (var p in FindObjectsByType<CallPrompt>(FindObjectsSortMode.None)) if (p.Running) return true;
        return false;
    }

    /// <summary>The lookout calls the nearest find not yet picked up: once in range, once more when close.</summary>
    private void Report(Transform ship)
    {
        if (Time.time < _nextReport || CrewVoice.Instance == null) return;
        Find near = null;
        float best = float.MaxValue;
        foreach (var f in _finds)
        {
            if (f.Taken || f.Reports >= 2) continue;
            float d = Channel.Flat(f.At - ship.position).magnitude;
            bool due = f.Reports == 0 ? d <= ReportRange : d <= CloseRange && Time.time - f.LastReport >= RepeatAfter;
            if (due && d < best) { best = d; near = f; }
        }
        if (near == null || Busy()) return;

        near.Reports++;
        near.LastReport = Time.time;
        _nextReport = Time.time + Gap;
        CrewVoice.Instance.SpeakFrom(RadioChannel.Lookout, new[] { ReportLine(near.Kind, ship, near.At, near.Reports == 1) },
                                     ShipTelemetry.Capture(), "finds:report");
    }

    /// <summary>"Captain, I see a wreck, fine on the port bow, ahead on the left. About 200 metres. Over."</summary>
    public static string ReportLine(FindKind kind, Transform ship, Vector3 at, bool first)
    {
        Vector3 to = Channel.Flat(at - ship.position);
        float relative = Vector3.SignedAngle(Channel.Flat(ship.forward), to, Vector3.up);
        int metres = Mathf.Max(10, Mathf.RoundToInt(to.magnitude / 10f) * 10);
        return ReportLine(kind, relative, metres, first);
    }

    public static string ReportLine(FindKind kind, float relative, int metres, bool first)
    {
        string what = Logbook.Spoken(kind);
        string bridge = ShipTelemetry.RelativeBearing(relative), plain = ShipTelemetry.PlainDirection(relative);
        string where = bridge == plain ? bridge : bridge + ", " + plain; // "dead ahead" once, not twice
        return first
            ? "Captain, I see " + what + ", " + where + ". About " + metres + " metres. Over."
            : "We are close to " + what + " now, " + where + ", " + metres + " metres. Steer over it and we pick it up. Over.";
    }

    private void Take(Find f)
    {
        f.Taken = true;
        f.TakenAt = Time.time;
        Found++;
        Spray.Burst(f.At, 1.6f, 18);
        UiSounds.Play(UiSounds.Coin(6), 0.55f);
        bool first = Logbook.RecordFind(MissionId, f.Index);
        if (CrewVoice.Instance != null)
            CrewVoice.Instance.SpeakFrom(RadioChannel.Lookout,
                new[] { first ? "Got it, Captain! I'll write the " + Logbook.Title(f.Kind).ToLowerInvariant() + " in the logbook. Over."
                              : "Got it, Captain! The " + Logbook.Title(f.Kind).ToLowerInvariant() + " is on board. Over." },
                ShipTelemetry.Capture(), "finds:taken");
    }

    // ------------------------------------------------------------ the models

    private static Transform Model(FindKind kind, Transform parent)
    {
        var go = new GameObject("Find " + kind);
        go.transform.SetParent(parent, false);
        var m = new LowPolyMesh();
        var glow = new LowPolyMesh();
        switch (kind)
        {
            case FindKind.Buoy:
                m.Frustum(new Vector3(0f, -0.6f, 0f), 1.3f, 0.55f, 2.6f, 10, 1f, 1f, new Color(0.78f, 0.16f, 0.1f), true, true);
                m.Ring(new Vector3(0f, 0.25f, 0f), 1.0f, 1.32f, 10, new Color(0.95f, 0.93f, 0.88f));
                m.Box(new Vector3(0f, 2.5f, 0f), new Vector3(0.2f, 1.1f, 0.2f), new Color(0.2f, 0.2f, 0.22f));
                glow.Ellipsoid(new Vector3(0f, 3.15f, 0f), new Vector3(0.32f, 0.32f, 0.32f), 8, 5, new Color(1f, 0.85f, 0.35f), 0f, 1f);
                break;
            case FindKind.Wreck:
                m.Box(new Vector3(0f, 0.6f, 0f), new Vector3(3.4f, 3.2f, 8f), new Color(0.22f, 0.2f, 0.19f), Quaternion.Euler(-24f, 0f, 12f));
                m.Box(new Vector3(0f, 1.2f, 2.6f), new Vector3(2.4f, 0.3f, 3f), new Color(0.42f, 0.24f, 0.14f), Quaternion.Euler(-24f, 0f, 12f));
                m.Box(new Vector3(0.6f, 4.2f, -0.8f), new Vector3(0.28f, 7f, 0.28f), new Color(0.36f, 0.26f, 0.18f), Quaternion.Euler(-8f, 0f, 22f));
                m.Box(new Vector3(1.1f, 6.2f, -1.2f), new Vector3(3f, 0.18f, 0.18f), new Color(0.36f, 0.26f, 0.18f), Quaternion.Euler(0f, 0f, 22f));
                break;
            case FindKind.Barrels:
                Color[] drums = { new Color(0.12f, 0.33f, 0.62f), new Color(0.9f, 0.48f, 0.1f), new Color(0.12f, 0.33f, 0.62f), new Color(0.2f, 0.5f, 0.3f) };
                Vector3[] at = { new Vector3(0f, 0f, 0f), new Vector3(1.6f, 0f, 0.7f), new Vector3(-1.2f, 0f, 1.3f), new Vector3(0.5f, 0f, -1.6f) };
                for (int i = 0; i < at.Length; i++)
                {
                    m.Frustum(at[i] + Vector3.down * 0.5f, 0.55f, 0.55f, 1.4f, 10, 1f, 1f, drums[i], true, true);
                    m.Ring(at[i] + Vector3.up * 0.45f, 0.5f, 0.58f, 10, new Color(0.85f, 0.85f, 0.82f));
                }
                break;
            default:
                Color box = new Color(0.66f, 0.2f, 0.12f);
                m.Box(new Vector3(0f, 0.25f, 0f), new Vector3(2.5f, 2.6f, 6.1f), box, Quaternion.Euler(0f, 0f, 9f));
                for (int i = -2; i <= 2; i++)
                    m.Box(new Vector3(0f, 0.25f, i * 1.15f), new Vector3(2.6f, 2.7f, 0.12f), box * 0.75f, Quaternion.Euler(0f, 0f, 9f));
                break;
        }
        m.AttachTo(go, castShadows: false);
        if (glow.Verts.Count > 0)
        {
            var light = new GameObject("Light");
            light.transform.SetParent(go.transform, false);
            glow.AttachTo(light, castShadows: false, glow: 1.2f);
        }
        return go.transform;
    }
}
