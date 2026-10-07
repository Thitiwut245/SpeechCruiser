using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>A brass compass rose: the game's emblem on the title screen and the sea charts.</summary>
public class CompassRose : VisualElement
{
    private readonly Color _ring, _star, _north;
    private float _spin;

    public CompassRose(float size, Color ring, Color star, Color north)
    {
        style.width = size;
        style.height = size;
        _ring = ring;
        _star = star;
        _north = north;
        pickingMode = PickingMode.Ignore;
        generateVisualContent += Draw;
    }

    /// <summary>A slow drift of the needle, like a real compass settling.</summary>
    public void Tick(float time)
    {
        _spin = Mathf.Sin(time * 0.6f) * 4f;
        MarkDirtyRepaint();
    }

    private void Draw(MeshGenerationContext ctx)
    {
        var p = ctx.painter2D;
        float s = contentRect.width;
        var c = new Vector2(s / 2f, s / 2f);
        float r = s / 2f - 3f;

        HudLook.Ring(p, c, r, _ring, 3f);
        HudLook.Ring(p, c, r - 7f, new Color(_ring.r, _ring.g, _ring.b, 0.5f), 1.5f);
        p.strokeColor = _ring;
        for (int i = 0; i < 32; i++)
        {
            float a = i * 360f / 32f;
            p.lineWidth = i % 4 == 0 ? 2f : 1f;
            p.BeginPath();
            p.MoveTo(HudLook.OnCircle(c, r - 7f, a));
            p.LineTo(HudLook.OnCircle(c, r - (i % 4 == 0 ? 15f : 11f), a));
            p.Stroke();
        }

        // Eight points: the four long ones, then the four between them, each in two shades.
        for (int i = 0; i < 8; i++)
        {
            float a = 90f + i * 45f + _spin;
            float len = i % 2 == 0 ? r - 16f : (r - 16f) * 0.55f;
            float w = i % 2 == 0 ? 0.16f : 0.12f;
            Vector2 tip = HudLook.OnCircle(c, len, a);
            Vector2 left = HudLook.OnCircle(c, len * w * 2.2f, a + 45f);
            Vector2 right = HudLook.OnCircle(c, len * w * 2.2f, a - 45f);
            Color main = i == 0 ? _north : _star;
            p.fillColor = main;
            p.BeginPath(); p.MoveTo(c); p.LineTo(left); p.LineTo(tip); p.ClosePath(); p.Fill();
            p.fillColor = main * 0.7f + Color.black * 0.3f;
            p.BeginPath(); p.MoveTo(c); p.LineTo(tip); p.LineTo(right); p.ClosePath(); p.Fill();
        }
        HudLook.Disc(p, c, 5f, _ring);
    }
}

/// <summary>
/// An old sea chart: faint grid lines, a coast along the top, and the voyage route as a dashed
/// ink line through the missions' markers (solid where it has been sailed).
/// </summary>
public class ChartRoute : VisualElement
{
    public static readonly Color Ink = new Color(0.25f, 0.17f, 0.09f);

    private readonly List<Vector2> _stops = new List<Vector2>();
    private int _sailed;
    private readonly int _seed;

    public ChartRoute(int seed)
    {
        _seed = seed;
        pickingMode = PickingMode.Ignore;
        generateVisualContent += Draw;
    }

    /// <summary>Marker positions as fractions of the chart (0..1), and how many are done.</summary>
    public void SetRoute(IEnumerable<Vector2> stops, int sailed)
    {
        _stops.Clear();
        _stops.AddRange(stops);
        _sailed = sailed;
        MarkDirtyRepaint();
    }

    private void Draw(MeshGenerationContext ctx)
    {
        var p = ctx.painter2D;
        var rect = contentRect;
        if (rect.width < 10f) return;
        var rng = new System.Random(_seed);

        // Grid lines, as on a chart.
        p.strokeColor = new Color(Ink.r, Ink.g, Ink.b, 0.10f);
        p.lineWidth = 1f;
        for (float x = 40f; x < rect.width; x += 60f) { p.BeginPath(); p.MoveTo(new Vector2(x, 0)); p.LineTo(new Vector2(x, rect.height)); p.Stroke(); }
        for (float y = 30f; y < rect.height; y += 60f) { p.BeginPath(); p.MoveTo(new Vector2(0, y)); p.LineTo(new Vector2(rect.width, y)); p.Stroke(); }

        // A coast along the top edge, and a small island.
        p.fillColor = new Color(0.62f, 0.55f, 0.36f, 0.55f);
        p.BeginPath();
        p.MoveTo(new Vector2(0, 0));
        float h = 18f;
        for (float x = 0; x <= rect.width + 20f; x += 20f)
        {
            h = Mathf.Clamp(h + (float)(rng.NextDouble() - 0.5) * 14f, 8f, 30f);
            p.LineTo(new Vector2(x, h));
        }
        p.LineTo(new Vector2(rect.width, 0));
        p.ClosePath();
        p.Fill();
        Vector2 island = new Vector2(rect.width * (0.25f + 0.5f * (float)rng.NextDouble()), rect.height * 0.78f);
        p.BeginPath();
        for (int i = 0; i <= 12; i++)
        {
            float a = i * 30f;
            float rr = 16f + 7f * (float)rng.NextDouble();
            Vector2 pt = HudLook.OnCircle(island, rr, a) + new Vector2(rr * 0.6f * Mathf.Cos(a * Mathf.Deg2Rad), 0f);
            if (i == 0) p.MoveTo(pt); else p.LineTo(pt);
        }
        p.ClosePath();
        p.Fill();

        if (_stops.Count < 2) return;
        // The route: a gentle curve through each stop.
        var pts = new List<Vector2>();
        foreach (var s in _stops) pts.Add(new Vector2(s.x * rect.width, s.y * rect.height));
        for (int i = 1; i < pts.Count; i++)
        {
            Vector2 a = pts[i - 1], b = pts[i];
            Vector2 mid = (a + b) / 2f + new Vector2(0f, (i % 2 == 0 ? 1f : -1f) * 22f);
            bool sailed = i <= _sailed;
            p.strokeColor = new Color(Ink.r, Ink.g, Ink.b, sailed ? 0.85f : 0.55f);
            p.lineWidth = sailed ? 3f : 2.2f;
            p.lineCap = LineCap.Round;
            const int steps = 40;
            for (int k = 0; k < steps; k++)
            {
                if (!sailed && k % 2 == 1) continue; // dashes ahead, a solid line behind
                Vector2 p0 = Quad(a, mid, b, k / (float)steps), p1 = Quad(a, mid, b, (k + 1) / (float)steps);
                p.BeginPath(); p.MoveTo(p0); p.LineTo(p1); p.Stroke();
            }
        }
    }

    private static Vector2 Quad(Vector2 a, Vector2 c, Vector2 b, float t)
    {
        float u = 1f - t;
        return u * u * a + 2f * u * t * c + t * t * b;
    }
}

/// <summary>
/// The engine-room lever on the SET SAIL plate: a slot with a brass handle that stands back
/// at rest and throws forward (with a little overshoot) when the voyage starts.
/// </summary>
public class SailLever : VisualElement
{
    private const float Back = -32f, Forward = 34f;
    private float _angle = Back, _from = Back, _to = Back, _start;
    private const float ThrowSeconds = 0.28f;

    public SailLever()
    {
        style.position = Position.Absolute;
        style.left = 0; style.right = 0; style.top = 0; style.bottom = 0;
        pickingMode = PickingMode.Ignore;
        generateVisualContent += Draw;
    }

    public void Throw() => Animate(Forward);
    public void ResetBack() => Animate(Back);

    private void Animate(float to)
    {
        _from = _angle;
        _to = to;
        _start = Time.unscaledTime;
        schedule.Execute(Step).Every(16).Until(() => Time.unscaledTime - _start > ThrowSeconds + 0.05f); // one step past the end lands it exactly
    }

    private void Step()
    {
        float t = Mathf.Clamp01((Time.unscaledTime - _start) / ThrowSeconds);
        // Ease out with a small overshoot, like a heavy lever hitting its stop.
        float k = 1f + 2.2f * Mathf.Pow(t - 1f, 3f) + 1.2f * Mathf.Pow(t - 1f, 2f);
        _angle = Mathf.LerpUnclamped(_from, _to, k);
        MarkDirtyRepaint();
    }

    private void Draw(MeshGenerationContext ctx)
    {
        var p = ctx.painter2D;
        var r = contentRect;
        if (r.width < 4f) return;
        var pivot = new Vector2(r.width / 2f, r.height - 10f);
        float reach = r.height - 18f;
        Color ink = new Color(0.30f, 0.20f, 0.08f);

        // The slot the lever runs in: a narrow arc, with a notch at each end (back, forward).
        HudLook.Sector(p, pivot, reach - 12f, reach - 4f, 90f - Forward - 4f, 90f - Back + 4f, new Color(0.22f, 0.15f, 0.06f, 0.9f));
        foreach (float a in new[] { Back, Forward })
        {
            p.strokeColor = ink;
            p.lineWidth = 2f;
            p.BeginPath(); p.MoveTo(HudLook.OnCircle(pivot, reach - 15f, 90f - a)); p.LineTo(HudLook.OnCircle(pivot, reach - 1f, 90f - a)); p.Stroke();
        }

        // The handle: a dark rod with a red knob, on a dark hub.
        Vector2 tip = HudLook.OnCircle(pivot, reach, 90f - _angle);
        p.strokeColor = ink;
        p.lineWidth = 6f;
        p.lineCap = LineCap.Round;
        p.BeginPath(); p.MoveTo(pivot); p.LineTo(tip); p.Stroke();
        HudLook.Disc(p, tip, 9f, new Color(0.72f, 0.18f, 0.12f));
        HudLook.Disc(p, tip + new Vector2(-2.5f, -2.5f), 3f, new Color(1f, 0.6f, 0.5f));
        HudLook.Disc(p, pivot, 8f, ink);
        HudLook.Disc(p, pivot, 3f, new Color(0.85f, 0.7f, 0.4f));
    }
}
