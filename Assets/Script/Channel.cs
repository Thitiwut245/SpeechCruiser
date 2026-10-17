using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The safe channel of the current level as a helmsman would sail it: LevelGenerator's grid path
/// with its corners cut (one Chaikin pass), ending at the destination berth, and split into points
/// a few metres apart with the distance along the channel to each. Built once per level, on first
/// use. The course dots (CourseGuides), the path assist and the checkpoints all read it.
/// </summary>
public static class Channel
{
    private const float Spacing = 4f;

    private static List<Vector3> _source;
    private static readonly List<Vector3> _points = new List<Vector3>();
    private static readonly List<float> _along = new List<float>();

    /// <summary>The points, start to berth. Empty when there is no level.</summary>
    public static IReadOnlyList<Vector3> Points { get { Refresh(); return _points; } }

    /// <summary>Metres along the channel to each point.</summary>
    public static IReadOnlyList<float> Along { get { Refresh(); return _along; } }

    public static float Length { get { Refresh(); return _along.Count > 0 ? _along[_along.Count - 1] : 0f; } }

    public static bool Ready { get { Refresh(); return _points.Count >= 2; } }

    /// <summary>The point nearest <paramref name="at"/> (flat distance), searching from index <paramref name="from"/> on.</summary>
    public static int Nearest(Vector3 at, int from, out float distance)
    {
        Refresh();
        int best = Mathf.Clamp(from, 0, Mathf.Max(0, _points.Count - 1));
        float bestSq = float.MaxValue;
        for (int i = best; i < _points.Count; i++)
        {
            float d = Flat(_points[i] - at).sqrMagnitude;
            if (d < bestSq) { bestSq = d; best = i; }
        }
        distance = _points.Count > 0 ? Mathf.Sqrt(bestSq) : float.MaxValue;
        return best;
    }

    /// <summary>The index of the first point at least <paramref name="metres"/> along the channel.</summary>
    public static int IndexAt(float metres)
    {
        Refresh();
        for (int i = 0; i < _along.Count; i++)
            if (_along[i] >= metres) return i;
        return Mathf.Max(0, _along.Count - 1);
    }

    /// <summary>The channel's direction at point <paramref name="i"/> (flat, normalised).</summary>
    public static Vector3 Direction(int i)
    {
        Refresh();
        if (_points.Count < 2) return Vector3.forward;
        int a = Mathf.Clamp(i, 0, _points.Count - 2);
        Vector3 d = Flat(_points[a + 1] - _points[a]);
        return d.sqrMagnitude > 0.0001f ? d.normalized : Vector3.forward;
    }

    public static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    /// <summary>
    /// Degrees off <paramref name="ship"/>'s bow (+ starboard) to the channel point <paramref name="ahead"/>
    /// metres on from the one nearest it: where to steer to get back on the channel.
    /// </summary>
    public static float BearingFrom(Transform ship, float ahead)
    {
        if (ship == null || !Ready) return 0f;
        int near = Nearest(ship.position, 0, out _);
        Vector3 to = Flat(_points[IndexAt(_along[near] + ahead)] - ship.position);
        if (to.sqrMagnitude < 1f) return 0f;
        return Mathf.DeltaAngle(ship.eulerAngles.y, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg);
    }

    private static void Refresh()
    {
        var level = LevelGenerator.Instance;
        var path = level != null ? level.SolutionWorldPath : null;
        if (path == _source) return;
        _source = path;
        _points.Clear();
        _along.Clear();
        if (path == null || path.Count < 2) return;

        var raw = new List<Vector3>(path);
        if (Quay.Destination != null) raw.Add(Quay.Destination.Berth);

        // One pass of corner cutting: the grid path turns in steps, a helmsman doesn't.
        var cut = new List<Vector3> { raw[0] };
        for (int i = 0; i + 1 < raw.Count; i++)
        {
            cut.Add(Vector3.Lerp(raw[i], raw[i + 1], 0.25f));
            cut.Add(Vector3.Lerp(raw[i], raw[i + 1], 0.75f));
        }
        cut.Add(raw[raw.Count - 1]);

        _points.Add(Flat(cut[0]));
        _along.Add(0f);
        for (int i = 1; i < cut.Count; i++)
        {
            Vector3 a = Flat(cut[i - 1]), b = Flat(cut[i]);
            int n = Mathf.Max(1, Mathf.CeilToInt((b - a).magnitude / Spacing));
            for (int k = 1; k <= n; k++)
            {
                Vector3 p = Vector3.Lerp(a, b, k / (float)n);
                _along.Add(_along[_along.Count - 1] + (p - _points[_points.Count - 1]).magnitude);
                _points.Add(p);
            }
        }
    }
}
