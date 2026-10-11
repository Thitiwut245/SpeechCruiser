using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The HUD's look, after Alto's Adventure (see UiTheme): white hairlines and thin type straight
/// over the scene, a warm peach accent, coral for danger. The old instrument names stay
/// (Brass is now the hairline white, Amber the peach) so every widget follows.
/// </summary>
public static class HudLook
{
    public static readonly Color Glass = new Color(0f, 0f, 0f, 0f);
    public static readonly Color GlassDeep = new Color(0.10f, 0.08f, 0.15f, 0.30f);
    public static readonly Color Brass = new Color(1f, 1f, 1f, 0.85f);
    public static readonly Color BrassDim = new Color(1f, 1f, 1f, 0.38f);
    public static readonly Color Amber = new Color(1f, 0.74f, 0.58f);
    public static readonly Color Text = new Color(1f, 1f, 1f, 0.96f);
    public static readonly Color TextDim = new Color(1f, 1f, 1f, 0.86f);
    public static readonly Color Scope = new Color(0.10f, 0.08f, 0.15f, 0.30f);
    public static readonly Color ScopeLine = new Color(1f, 1f, 1f, 0.9f);
    public static readonly Color Port = new Color(1f, 0.52f, 0.46f);
    public static readonly Color Starboard = new Color(0.62f, 0.95f, 0.82f);
    public static readonly Color Danger = new Color(0.95f, 0.38f, 0.33f);

    /// <summary>No panel at all: just spacing (the text floats over the scene).</summary>
    public static void Panel(VisualElement e, float padding, float radius = 12f)
    {
        e.style.backgroundColor = Glass;
        e.style.paddingTop = padding; e.style.paddingBottom = padding;
        e.style.paddingLeft = padding + 2f; e.style.paddingRight = padding + 2f;
        Border(e, 0f, Color.clear);
        Radius(e, radius);
    }

    public static void Border(VisualElement e, float width, Color color)
    {
        e.style.borderTopWidth = width; e.style.borderBottomWidth = width;
        e.style.borderLeftWidth = width; e.style.borderRightWidth = width;
        e.style.borderTopColor = color; e.style.borderBottomColor = color;
        e.style.borderLeftColor = color; e.style.borderRightColor = color;
    }

    public static void Radius(VisualElement e, float r)
    {
        e.style.borderTopLeftRadius = r; e.style.borderTopRightRadius = r;
        e.style.borderBottomLeftRadius = r; e.style.borderBottomRightRadius = r;
    }

    public static Label Words(string text, float size, Color color, bool bold = false)
    {
        var l = new Label(text);
        l.style.fontSize = Mathf.Max(size, 14f); // nothing smaller over the sea
        l.style.color = color;
        l.style.unityFontStyleAndWeight = FontStyle.Normal; // thin type throughout, as in Alto
        if (bold) l.style.letterSpacing = size <= 15f ? 3f : 1.5f; // "bold" captions are spaced out instead
        l.style.marginTop = 0; l.style.marginBottom = 0; l.style.marginLeft = 0; l.style.marginRight = 0;
        l.style.paddingTop = 0; l.style.paddingBottom = 0; l.style.paddingLeft = 0; l.style.paddingRight = 0;
        return l;
    }

    /// <summary>A point at <paramref name="degrees"/> (0 = right, 90 = up) on a circle, in UI space (y down).</summary>
    public static Vector2 OnCircle(Vector2 centre, float radius, float degrees)
    {
        float r = degrees * Mathf.Deg2Rad;
        return centre + new Vector2(Mathf.Cos(r), -Mathf.Sin(r)) * radius;
    }

    /// <summary>A filled ring sector between two angles (degrees, 0 = right, 90 = up).</summary>
    public static void Sector(Painter2D p, Vector2 c, float r0, float r1, float a0, float a1, Color fill)
    {
        int steps = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(a1 - a0) / 4f));
        p.fillColor = fill;
        p.BeginPath();
        p.MoveTo(OnCircle(c, r1, a0));
        for (int i = 1; i <= steps; i++) p.LineTo(OnCircle(c, r1, Mathf.Lerp(a0, a1, i / (float)steps)));
        for (int i = steps; i >= 0; i--) p.LineTo(OnCircle(c, r0, Mathf.Lerp(a0, a1, i / (float)steps)));
        p.ClosePath();
        p.Fill();
    }

    public static void Ring(Painter2D p, Vector2 c, float r, Color color, float width, float a0 = 0f, float a1 = 360f)
    {
        int steps = Mathf.Max(8, Mathf.CeilToInt(Mathf.Abs(a1 - a0) / 4f));
        p.strokeColor = color;
        p.lineWidth = width;
        p.BeginPath();
        p.MoveTo(OnCircle(c, r, a0));
        for (int i = 1; i <= steps; i++) p.LineTo(OnCircle(c, r, Mathf.Lerp(a0, a1, i / (float)steps)));
        p.Stroke();
    }

    public static void Disc(Painter2D p, Vector2 c, float r, Color fill)
    {
        Sector(p, c, 0f, r, 0f, 360f, fill);
    }
}

/// <summary>
/// The engine telegraph: a brass half-dial with the gears around it, astern on the left, ahead on
/// the right, and a lever that swings to the gear ordered. Each gear can be clicked.
/// </summary>
public class TelegraphDial : VisualElement
{
    public const float W = 290f, H = 168f;
    private static readonly Vector2 Hub = new Vector2(W / 2f, H - 18f);
    private const float Radius = 128f;

    private static readonly ShipEngine.TelegraphGear[] Order =
    {
        ShipEngine.TelegraphGear.FullAstern, ShipEngine.TelegraphGear.SlowAstern, ShipEngine.TelegraphGear.Stop,
        ShipEngine.TelegraphGear.DeadSlow, ShipEngine.TelegraphGear.Slow, ShipEngine.TelegraphGear.Half,
        ShipEngine.TelegraphGear.Full, ShipEngine.TelegraphGear.Flank,
    };
    private static readonly string[] Names = { "FULL ASTERN", "SLOW ASTERN", "STOP", "DEAD SLOW", "SLOW", "HALF", "FULL", "FLANK" };

    public event Action<ShipEngine.TelegraphGear> GearClicked;

    private readonly Label[] _labels = new Label[Order.Length];
    private float _needle = 90f, _target = 90f;
    private int _current = 2;

    private static float Step => 180f / Order.Length;
    private static float AngleOf(int i) => 180f - (i + 0.5f) * Step;

    public TelegraphDial()
    {
        style.width = W;
        style.height = H;
        generateVisualContent += Draw;
        // The names run outward from the hub like spokes, as on a real telegraph face; on the
        // astern side they are turned the other way so they never read upside down.
        const float LabelW = 84f, LabelH = 16f;
        for (int i = 0; i < Order.Length; i++)
        {
            int index = i;
            float a = AngleOf(i);
            var l = HudLook.Words(Names[i], 11f, HudLook.TextDim, true);
            l.style.position = Position.Absolute;
            l.style.width = LabelW;
            l.style.height = LabelH;
            l.style.unityTextAlign = a > 90f ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight;
            Vector2 at = HudLook.OnCircle(Hub, (30f + Radius) / 2f + 4f, a);
            l.style.left = at.x - LabelW / 2f;
            l.style.top = at.y - LabelH / 2f;
            l.style.rotate = new Rotate(a > 90f ? 180f - a : -a);
            l.pickingMode = PickingMode.Position;
            l.RegisterCallback<ClickEvent>(_ => GearClicked?.Invoke(Order[index]));
            l.tooltip = "Click to order " + Names[i].ToLowerInvariant();
            _labels[i] = l;
            Add(l);
        }
    }

    public void SetGear(ShipEngine.TelegraphGear gear)
    {
        int i = Array.IndexOf(Order, gear);
        if (i < 0) return;
        _current = i;
        _target = AngleOf(i);
        for (int k = 0; k < _labels.Length; k++)
        {
            bool on = k == i;
            _labels[k].style.color = on ? HudLook.Amber : (k < 2 ? HudLook.Port : k == 2 ? HudLook.Text : HudLook.TextDim);
            _labels[k].style.fontSize = on ? 12.5f : 11f;
        }
    }

    /// <summary>Swings the lever toward the ordered gear; call every frame.</summary>
    public void Tick(float dt)
    {
        if (Mathf.Abs(_needle - _target) < 0.05f) return;
        _needle = Mathf.MoveTowards(_needle, _target, 240f * dt);
        MarkDirtyRepaint();
    }

    private void Draw(MeshGenerationContext ctx)
    {
        var p = ctx.painter2D;
        // A faint dusk wash so the hairlines read over bright water, then one thin arc with a
        // tick for each gear (astern ticks coral) and a peach dot on the gear ordered.
        HudLook.Sector(p, Hub, 0f, Radius + 8f, 0f, 180f, HudLook.GlassDeep);
        HudLook.Ring(p, Hub, Radius, HudLook.Brass, 1.5f, 0f, 180f);
        for (int i = 0; i < Order.Length; i++)
        {
            float a = AngleOf(i);
            p.strokeColor = i < 2 ? HudLook.Port : HudLook.BrassDim;
            p.lineWidth = 1.5f;
            p.BeginPath(); p.MoveTo(HudLook.OnCircle(Hub, Radius - 7f, a)); p.LineTo(HudLook.OnCircle(Hub, Radius, a)); p.Stroke();
        }
        HudLook.Disc(p, HudLook.OnCircle(Hub, Radius, AngleOf(_current)), 5f, HudLook.Amber);

        // The lever: a thin white line to a small peach knob.
        Vector2 tip = HudLook.OnCircle(Hub, Radius - 14f, _needle);
        p.strokeColor = HudLook.Text;
        p.lineWidth = 2f;
        p.lineCap = LineCap.Round;
        p.BeginPath(); p.MoveTo(Hub); p.LineTo(tip); p.Stroke();
        HudLook.Disc(p, tip, 4.5f, HudLook.Amber);
        HudLook.Ring(p, Hub, 6f, HudLook.Text, 1.5f);
    }
}

/// <summary>The rudder: a small arc, port (red) on the left and starboard (green) on the right.</summary>
public class RudderArc : VisualElement
{
    public const float W = 290f, H = 64f;
    private static readonly Vector2 Hub = new Vector2(W / 2f, H + 46f);
    private const float Radius = 96f, Span = 34f;
    private float _shown;
    private readonly Label _label;

    public RudderArc()
    {
        style.width = W;
        style.height = H;
        generateVisualContent += Draw;
        _label = HudLook.Words("RUDDER MIDSHIPS", 13f, HudLook.Text, true);
        _label.style.position = Position.Absolute;
        _label.style.left = 0; _label.style.right = 0; _label.style.bottom = 0;
        _label.style.unityTextAlign = TextAnchor.MiddleCenter;
        Add(_label);
        pickingMode = PickingMode.Ignore;
    }

    /// <summary>-1 = full port .. 0 midships .. +1 = full starboard; degrees for the label.</summary>
    public void Set(float normalized, float degrees)
    {
        normalized = Mathf.Clamp(normalized, -1f, 1f);
        if (Mathf.Abs(normalized - _shown) > 0.002f)
        {
            _shown = normalized;
            MarkDirtyRepaint();
        }
        int angle = Mathf.RoundToInt(Mathf.Abs(degrees));
        _label.text = angle < 2 ? "RUDDER MIDSHIPS" : "RUDDER " + angle + "° " + (degrees < 0f ? "LEFT (PORT)" : "RIGHT (STBD)");
        _label.style.color = angle < 2 ? HudLook.Text : degrees < 0f ? HudLook.Port : HudLook.Starboard;
    }

    private void Draw(MeshGenerationContext ctx)
    {
        var p = ctx.painter2D;
        HudLook.Sector(p, Hub, Radius - 12f, Radius, 90f, 90f + Span, new Color(HudLook.Port.r, HudLook.Port.g, HudLook.Port.b, 0.55f));
        HudLook.Sector(p, Hub, Radius - 12f, Radius, 90f - Span, 90f, new Color(HudLook.Starboard.r, HudLook.Starboard.g, HudLook.Starboard.b, 0.55f));
        HudLook.Ring(p, Hub, Radius + 1f, HudLook.BrassDim, 2f, 90f - Span, 90f + Span);
        p.strokeColor = HudLook.Text;
        p.lineWidth = 2f;
        p.BeginPath(); p.MoveTo(HudLook.OnCircle(Hub, Radius - 14f, 90f)); p.LineTo(HudLook.OnCircle(Hub, Radius + 2f, 90f)); p.Stroke();
        float a = 90f - _shown * Span;
        p.strokeColor = HudLook.Amber;
        p.lineWidth = 5f;
        p.lineCap = LineCap.Round;
        p.BeginPath(); p.MoveTo(HudLook.OnCircle(Hub, Radius - 20f, a)); p.LineTo(HudLook.OnCircle(Hub, Radius + 6f, a)); p.Stroke();
    }
}

/// <summary>A compass tape: the heading scrolls under a fixed amber marker, with N, E, S, W and numbers.</summary>
public class CompassTape : VisualElement
{
    public const float W = 420f, H = 34f;
    private const float Visible = 120f; // degrees across the tape
    private float _heading = -1f;
    private readonly List<Label> _marks = new List<Label>();

    public CompassTape()
    {
        style.width = W;
        style.height = H;
        style.overflow = Overflow.Hidden;
        generateVisualContent += Draw;
        for (int i = 0; i < 6; i++)
        {
            var l = HudLook.Words("", 13f, HudLook.TextDim, true);
            l.style.position = Position.Absolute;
            l.style.width = 40f;
            l.style.top = 15f;
            l.style.unityTextAlign = TextAnchor.UpperCenter;
            l.pickingMode = PickingMode.Ignore;
            _marks.Add(l);
            Add(l);
        }
        pickingMode = PickingMode.Ignore;
    }

    public void Set(float heading)
    {
        if (Mathf.Abs(Mathf.DeltaAngle(heading, _heading)) < 0.2f) return;
        _heading = heading;
        // Labels every 30 degrees: letters at the cardinal points.
        float first = Mathf.Ceil((heading - Visible / 2f) / 30f) * 30f;
        for (int i = 0; i < _marks.Count; i++)
        {
            float deg = first + i * 30f;
            float x = W / 2f + Mathf.DeltaAngle(heading, deg) * (W / Visible);
            int d = ((int)Mathf.Repeat(deg, 360f) + 360) % 360;
            string text = d == 0 ? "N" : d == 90 ? "E" : d == 180 ? "S" : d == 270 ? "W" : d.ToString();
            _marks[i].text = text;
            _marks[i].style.left = x - 20f;
            _marks[i].style.color = text.Length == 1 ? HudLook.Amber : HudLook.TextDim;
            _marks[i].style.display = x < -20f || x > W + 20f ? DisplayStyle.None : DisplayStyle.Flex;
        }
        MarkDirtyRepaint();
    }

    private void Draw(MeshGenerationContext ctx)
    {
        var p = ctx.painter2D;
        p.strokeColor = HudLook.TextDim;
        float first = Mathf.Ceil((_heading - Visible / 2f) / 5f) * 5f;
        for (float deg = first; deg <= _heading + Visible / 2f; deg += 5f)
        {
            float x = W / 2f + Mathf.DeltaAngle(_heading, deg) * (W / Visible);
            bool major = Mathf.Approximately(Mathf.Repeat(deg, 30f), 0f);
            p.lineWidth = major ? 2f : 1f;
            p.BeginPath(); p.MoveTo(new Vector2(x, 0f)); p.LineTo(new Vector2(x, major ? 12f : 6f)); p.Stroke();
        }
        // The lubber line: where the bow points.
        p.fillColor = HudLook.Amber;
        p.BeginPath();
        p.MoveTo(new Vector2(W / 2f - 7f, 0f)); p.LineTo(new Vector2(W / 2f + 7f, 0f)); p.LineTo(new Vector2(W / 2f, 11f));
        p.ClosePath();
        p.Fill();
    }
}

/// <summary>
/// The radar chart: head-up (the bow always points up, so left on the chart is left for the
/// ship), showing the channel, reefs, mines, the destination (or an arrow to it on the rim),
/// a rescue waiting, and the ship's wake, under a turning sweep.
/// </summary>
public class ChartScope : VisualElement
{
    public float Size { get; }
    public float RangeMetres = 120f;

    public struct Blip
    {
        public Vector3 Position;
        public bool Mine;
    }

    private readonly Label _north;
    private readonly Label _range;
    private Vector3 _ship;
    private float _heading;
    private IReadOnlyList<Blip> _blips = Array.Empty<Blip>();
    private IReadOnlyList<Vector3> _channel = Array.Empty<Vector3>();
    private IReadOnlyList<Vector3> _trail = Array.Empty<Vector3>();
    private Vector3? _dock, _rescue;
    private float _sweep, _blink;

    public ChartScope(float size)
    {
        Size = size;
        style.width = size;
        style.height = size;
        HudLook.Radius(this, size / 2f);
        HudLook.Border(this, 1.5f, HudLook.Brass);
        style.backgroundColor = HudLook.Scope;
        style.overflow = Overflow.Hidden;
        pickingMode = PickingMode.Ignore;
        generateVisualContent += Draw;

        _north = HudLook.Words("N", 13f, HudLook.Amber, true);
        _north.style.position = Position.Absolute;
        _north.style.width = 16f;
        _north.style.unityTextAlign = TextAnchor.MiddleCenter;
        Add(_north);
        _range = HudLook.Words("", 11f, HudLook.TextDim, false);
        _range.style.position = Position.Absolute;
        _range.style.bottom = 14f;
        _range.style.left = 0; _range.style.right = 0;
        _range.style.unityTextAlign = TextAnchor.MiddleCenter;
        Add(_range);
    }

    public void SetWorld(IReadOnlyList<Blip> blips, IReadOnlyList<Vector3> channel)
    {
        _blips = blips ?? Array.Empty<Blip>();
        _channel = channel ?? Array.Empty<Vector3>();
    }

    public void Set(Vector3 ship, float heading, IReadOnlyList<Vector3> trail, Vector3? dock, Vector3? rescue, float dt)
    {
        _ship = ship;
        _heading = heading;
        _trail = trail ?? Array.Empty<Vector3>();
        _dock = dock;
        _rescue = rescue;
        _sweep = Mathf.Repeat(_sweep + dt * 150f, 360f);
        _blink += dt;
        Vector2 n = ToChart(ship + Vector3.forward * RangeMetres * 0.84f); // north is world +z
        _north.style.left = n.x - 8f;
        _north.style.top = n.y - 9f;
        _range.text = Mathf.RoundToInt(RangeMetres) + " m";
        MarkDirtyRepaint();
    }

    private Vector2 Centre => new Vector2(Size / 2f, Size / 2f);
    private float Scale => (Size / 2f - 6f) / RangeMetres;

    /// <summary>World point to chart pixels, rotated so the bow points up.</summary>
    private Vector2 ToChart(Vector3 world)
    {
        Vector3 d = world - _ship;
        float a = _heading * Mathf.Deg2Rad;
        float x = d.x * Mathf.Cos(a) - d.z * Mathf.Sin(a);
        float z = d.x * Mathf.Sin(a) + d.z * Mathf.Cos(a);
        return Centre + new Vector2(x, -z) * Scale;
    }

    private bool Inside(Vector2 p, float margin = 0f) => (p - Centre).magnitude <= Size / 2f - 4f - margin;

    private void Draw(MeshGenerationContext ctx)
    {
        var p = ctx.painter2D;
        Vector2 c = Centre;
        float r = Size / 2f - 4f;

        // Range rings and cross hairs.
        var faint = new Color(1f, 1f, 1f, 0.16f);
        HudLook.Ring(p, c, r * 0.33f, faint, 1f);
        HudLook.Ring(p, c, r * 0.66f, faint, 1f);
        p.strokeColor = faint; p.lineWidth = 1f;
        p.BeginPath(); p.MoveTo(c + new Vector2(-r, 0)); p.LineTo(c + new Vector2(r, 0)); p.Stroke();
        p.BeginPath(); p.MoveTo(c + new Vector2(0, -r)); p.LineTo(c + new Vector2(0, r)); p.Stroke();

        // The channel: the safe way through.
        p.strokeColor = new Color(1f, 1f, 1f, 0.22f);
        p.lineWidth = 5f;
        p.lineCap = LineCap.Round;
        for (int i = 1; i < _channel.Count; i++)
        {
            Vector2 a = ToChart(_channel[i - 1]), b = ToChart(_channel[i]);
            if (!Inside(a, -20f) && !Inside(b, -20f)) continue;
            p.BeginPath(); p.MoveTo(a); p.LineTo(b); p.Stroke();
        }

        // The wake.
        for (int i = 0; i < _trail.Count; i++)
        {
            Vector2 t = ToChart(_trail[i]);
            if (!Inside(t)) continue;
            HudLook.Disc(p, t, 1.6f, new Color(0.9f, 0.95f, 1f, 0.15f + 0.4f * i / Mathf.Max(1, _trail.Count)));
        }

        // Hazards: reefs amber, mines red and blinking.
        bool mineOn = Mathf.Repeat(_blink * 2f, 1f) < 0.6f;
        foreach (var b in _blips)
        {
            Vector2 at = ToChart(b.Position);
            if (!Inside(at)) continue;
            if (b.Mine) { if (mineOn) HudLook.Disc(p, at, 4f, HudLook.Port); }
            else HudLook.Disc(p, at, 3.5f, new Color(1f, 1f, 1f, 0.8f));
        }

        if (_rescue.HasValue)
        {
            Vector2 at = ToChart(_rescue.Value);
            if (Inside(at)) HudLook.Ring(p, at, 6f + 2f * Mathf.Sin(_blink * 5f), HudLook.Amber, 2f);
            else Arrow(p, at, HudLook.Amber);
        }

        if (_dock.HasValue)
        {
            Vector2 at = ToChart(_dock.Value);
            if (Inside(at, 6f))
            {
                p.fillColor = HudLook.Amber;
                p.BeginPath();
                p.MoveTo(at + new Vector2(-7, -7)); p.LineTo(at + new Vector2(7, -7)); p.LineTo(at + new Vector2(7, 7)); p.LineTo(at + new Vector2(-7, 7));
                p.ClosePath(); p.Fill();
            }
            else Arrow(p, at, HudLook.Amber);
        }

        // The sweep: a bright line with a fading wedge behind it.
        for (int i = 0; i < 10; i++)
        {
            float a = 90f - _sweep + i * 3f;
            HudLook.Sector(p, c, 0f, r, a, a + 3.2f, new Color(1f, 1f, 1f, 0.10f * (1f - i / 10f)));
        }

        // The ship, bow up.
        p.fillColor = Color.white;
        p.BeginPath();
        p.MoveTo(c + new Vector2(0, -9)); p.LineTo(c + new Vector2(6, 7)); p.LineTo(c + new Vector2(-6, 7));
        p.ClosePath(); p.Fill();
    }

    /// <summary>An arrowhead on the rim pointing toward something off the chart.</summary>
    private void Arrow(Painter2D p, Vector2 target, Color color)
    {
        Vector2 dir = (target - Centre).normalized;
        float r = Size / 2f - 14f;
        Vector2 tip = Centre + dir * (r + 6f);
        Vector2 side = new Vector2(-dir.y, dir.x);
        p.fillColor = color;
        p.BeginPath();
        p.MoveTo(tip); p.LineTo(tip - dir * 12f + side * 7f); p.LineTo(tip - dir * 12f - side * 7f);
        p.ClosePath(); p.Fill();
    }
}
