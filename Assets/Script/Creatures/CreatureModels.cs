using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The sea's animals as detailed low-poly models, built in code with LowPolyMesh: a whale (the
/// story's deep shadow, and the ones seen far off), a dolphin, a gull, and the sea serpent's
/// head. Each comes back as a rig of named parts the behaviours move (a whale's tail and fluke
/// and flippers, a gull's wings, a serpent's jaw). Forward is +Z, up is +Y, lengths in metres.
/// </summary>
public static class CreatureModels
{
    // ------------------------------------------------------------------ whale

    public class WhaleRig
    {
        public Transform Root, Body, Tail, Fluke, FlipperL, FlipperR;
        /// <summary>The blowhole, in the body's space (the spout starts here).</summary>
        public Vector3 Blowhole;
        public float Length, Height;
    }

    public struct WhaleColours
    {
        public Color Back, Belly, Fins, Grooves, Barnacles, Eye;

        /// <summary>The story's deep shadow: near black (the scene's post-processing lifts dark colours).</summary>
        public static WhaleColours Shadow => new WhaleColours
        {
            Back = new Color(0.012f, 0.02f, 0.03f), Belly = new Color(0.08f, 0.09f, 0.11f), Fins = new Color(0.03f, 0.04f, 0.055f),
            Grooves = new Color(0.035f, 0.04f, 0.05f), Barnacles = new Color(0.22f, 0.23f, 0.23f), Eye = new Color(0.005f, 0.005f, 0.008f),
        };

        /// <summary>A humpback: slate back, pale belly and long pale flippers.</summary>
        public static WhaleColours Humpback => new WhaleColours
        {
            Back = new Color(0.13f, 0.16f, 0.2f), Belly = new Color(0.62f, 0.64f, 0.66f), Fins = new Color(0.55f, 0.58f, 0.6f),
            Grooves = new Color(0.4f, 0.42f, 0.45f), Barnacles = new Color(0.7f, 0.7f, 0.66f), Eye = new Color(0.03f, 0.03f, 0.04f),
        };
    }

    // The body's profile, head to tail, for a 30 m whale: z, half width, half height, centre height.
    private static readonly float[,] WhaleFront =
    {
        { 15.0f, 0.55f, 0.35f, -0.45f }, { 14.0f, 1.3f, 0.85f, -0.35f }, { 12.5f, 2.1f, 1.45f, -0.2f }, { 10.5f, 2.8f, 2.05f, -0.05f },
        { 8.0f, 3.3f, 2.55f, 0f }, { 5.0f, 3.6f, 2.85f, 0f }, { 2.0f, 3.55f, 2.8f, 0.05f }, { -1.0f, 3.1f, 2.5f, 0.1f }, { -3.0f, 2.7f, 2.25f, 0.12f },
    };
    private static readonly float[,] WhaleTail = // relative to the tail's pivot at z = -3
    {
        { 0.0f, 2.7f, 2.25f, 0.12f }, { -2.5f, 2.0f, 1.9f, 0.16f }, { -5.0f, 1.25f, 1.45f, 0.2f }, { -7.5f, 0.62f, 0.98f, 0.2f }, { -9.6f, 0.42f, 0.62f, 0.16f },
    };

    /// <summary>A whale <paramref name="length"/> long. Its root sits at the middle of the back's height.</summary>
    public static WhaleRig Whale(Transform parent, string name, float length, WhaleColours c)
    {
        float f = length / 30f;
        var rig = new WhaleRig { Length = length, Height = 2.85f * 2f * f };
        rig.Root = new GameObject(name).transform;
        rig.Root.SetParent(parent, false);

        // Body (head to the middle), with the jaw, throat grooves, head knobs, eye, mouth line and blowhole.
        var body = new LowPolyMesh();
        Profile(body, WhaleFront, f, 12, c.Back, c.Belly);
        // Lower jaw: a long scoop under the head, and the throat grooves running back from it.
        var jaw = new List<Vector3>(); var jawR = new List<Vector2>();
        foreach (var (z, rx, ry) in new[] { (14.6f, 0.45f, 0.2f), (13.2f, 1.25f, 0.5f), (11.0f, 2.2f, 0.85f), (8.5f, 2.6f, 0.9f), (6.5f, 2.3f, 0.6f) })
        {
            jaw.Add(new Vector3(0f, -0.95f - (15f - z) * 0.07f, z) * f);
            jawR.Add(new Vector2(rx, ry) * f);
        }
        body.Loft(jaw, jawR, 10, c.Belly, c.Belly);
        for (int g = -4; g <= 4; g++)
        {
            float x = g * 0.42f;
            Vector3 a = new Vector3(x, Bottom(WhaleFront, 12.8f, x) - 0.02f, 12.8f) * f;
            Vector3 b = new Vector3(x * 1.25f, Bottom(WhaleFront, 4.5f, x * 1.25f) - 0.02f, 4.5f) * f;
            body.Box((a + b) * 0.5f, new Vector3(0.1f, 0.06f, (b - a).magnitude / f) * f, c.Grooves, Quaternion.LookRotation(b - a));
        }
        // Knobs (tubercles) on the snout, as humpbacks have.
        for (int row = 0; row < 3; row++)
            for (int s = -1; s <= 1; s += 2)
            {
                float z = 14.2f - row * 1.1f, x = s * (0.5f + row * 0.35f);
                body.Ellipsoid(new Vector3(x, Top(WhaleFront, z, x) + 0.05f, z) * f, Vector3.one * 0.22f * f, 6, 4, c.Back, 0f, 1f);
            }
        // Barnacle patches on the chin.
        for (int i = 0; i < 7; i++)
        {
            float z = 14.2f - i * 0.3f, x = (i % 2 == 0 ? 0.3f : -0.35f) * (1 + i * 0.15f);
            body.Ellipsoid(new Vector3(x, -1.05f - i * 0.03f, z) * f, new Vector3(0.22f, 0.12f, 0.2f) * f, 5, 3, c.Barnacles, 0f, 1f);
        }
        // Mouth line, eyes, blowhole.
        for (int s = -1; s <= 1; s += 2)
        {
            Vector3 m0 = new Vector3(s * 0.55f, -0.62f, 14.9f) * f, m1 = new Vector3(s * 2.75f, -0.85f, 9.4f) * f;
            body.Box((m0 + m1) * 0.5f, new Vector3(0.09f, 0.09f, (m1 - m0).magnitude / f) * f, c.Eye, Quaternion.LookRotation(m1 - m0));
            body.Ellipsoid(new Vector3(s * 2.85f, -0.55f, 9.1f) * f, new Vector3(0.12f, 0.18f, 0.24f) * f, 6, 4, c.Eye, 0f, 1f);
        }
        float holeY = Top(WhaleFront, 10.4f, 0f);
        body.Box(new Vector3(-0.22f, holeY + 0.02f, 10.4f) * f, new Vector3(0.14f, 0.08f, 0.55f) * f, c.Eye, Quaternion.Euler(0f, -12f, 0f));
        body.Box(new Vector3(0.22f, holeY + 0.02f, 10.4f) * f, new Vector3(0.14f, 0.08f, 0.55f) * f, c.Eye, Quaternion.Euler(0f, 12f, 0f));
        body.Ellipsoid(new Vector3(0f, holeY - 0.05f, 10.9f) * f, new Vector3(0.5f, 0.25f, 0.4f) * f, 6, 4, c.Back, 0f, 1f); // splash guard
        rig.Blowhole = new Vector3(0f, holeY + 0.1f, 10.4f) * f;
        rig.Body = Part(rig.Root, "Body", body);

        // Tail stock, from just behind the middle: it bends up and down for the dive and the beat.
        var tail = new LowPolyMesh();
        Profile(tail, WhaleTail, f, 10, c.Back, c.Belly);
        // The small dorsal fin and the knuckles along the tail stock.
        tail.Fin(new[] { new Vector2(-0.6f, 0f), new Vector2(0.9f, 0f), new Vector2(1.3f, 0.95f), new Vector2(0.95f, 1.0f), new Vector2(0.2f, 0.55f) }, 0.3f, c.Back,
                 Matrix4x4.TRS(new Vector3(0f, Top(WhaleTail, -1.2f, 0f) - 0.15f, -1.2f) * f, Quaternion.Euler(0f, 90f, 0f), Vector3.one * f));
        for (int k = 0; k < 4; k++)
        {
            float z = -3.4f - k * 1.3f;
            tail.Ellipsoid(new Vector3(0f, Top(WhaleTail, z, 0f), z) * f, new Vector3(0.18f, 0.2f, 0.35f) * f, 6, 4, c.Back, 0f, 1f);
        }
        rig.Tail = Part(rig.Root, "Tail", tail);
        rig.Tail.localPosition = new Vector3(0f, 0f, -3f) * f;

        // Fluke: two broad, notched halves with a ragged trailing edge, on the end of the tail stock.
        var fluke = new LowPolyMesh();
        var half = new List<Vector2>
        {
            new Vector2(0f, 0.5f), new Vector2(1.6f, 0.45f), new Vector2(3.4f, -0.1f), new Vector2(4.7f, -1.3f), new Vector2(4.5f, -1.75f),
            new Vector2(3.8f, -1.45f), new Vector2(3.2f, -1.6f), new Vector2(2.5f, -1.3f), new Vector2(1.8f, -1.45f), new Vector2(1.0f, -1.2f),
            new Vector2(0.35f, -1.35f), new Vector2(0f, -0.95f),
        };
        fluke.Fin(half, 0.28f, c.Fins, Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(90f, 0f, 0f), Vector3.one * f));
        var other = new List<Vector2>();
        for (int i = half.Count - 1; i >= 0; i--) other.Add(new Vector2(-half[i].x, half[i].y));
        fluke.Fin(other, 0.28f, c.Fins, Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(90f, 0f, 0f), Vector3.one * f));
        // A few barnacles along the fluke's edge.
        for (int i = 0; i < 4; i++) fluke.Ellipsoid(new Vector3((i - 1.5f) * 1.6f, 0.1f, 0.35f) * f, new Vector3(0.18f, 0.1f, 0.16f) * f, 5, 3, c.Barnacles, 0f, 1f);
        rig.Fluke = Part(rig.Tail, "Fluke", fluke);
        rig.Fluke.localPosition = new Vector3(0f, 0.16f, -9.8f) * f;

        // Long pectoral flippers with a knobbly leading edge.
        rig.FlipperR = Flipper(rig.Root, "Flipper R", 1f, f, c);
        rig.FlipperL = Flipper(rig.Root, "Flipper L", -1f, f, c);
        return rig;
    }

    private static Transform Flipper(Transform root, string name, float side, float f, WhaleColours c)
    {
        var m = new LowPolyMesh();
        var outline = new List<Vector2>
        {
            new Vector2(0f, 0.85f), new Vector2(1.6f, 0.95f), new Vector2(2.2f, 0.8f), new Vector2(3.0f, 0.88f), new Vector2(3.8f, 0.7f), new Vector2(4.6f, 0.72f),
            new Vector2(5.6f, 0.5f), new Vector2(6.6f, 0.45f), new Vector2(7.8f, 0.22f), new Vector2(8.6f, 0f), new Vector2(8.2f, -0.25f), new Vector2(6.5f, -0.4f),
            new Vector2(4f, -0.62f), new Vector2(1.8f, -0.8f), new Vector2(0f, -0.9f),
        };
        if (side < 0f) { for (int i = 0; i < outline.Count; i++) outline[i] = new Vector2(-outline[i].x, outline[i].y); outline.Reverse(); }
        m.Fin(outline, 0.32f, c.Fins, Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(90f, 0f, 0f), Vector3.one * f));
        for (int i = 0; i < 4; i++) m.Ellipsoid(new Vector3(side * (1.8f + i * 1.6f), 0.05f, 0.8f - i * 0.1f) * f, new Vector3(0.2f, 0.12f, 0.16f) * f, 5, 3, c.Barnacles, 0f, 1f);
        var t = Part(root, name, m);
        t.localPosition = new Vector3(side * 2.9f, -1.3f, 8.2f) * f;
        t.localRotation = Quaternion.Euler(0f, side * 32f, -side * 28f); // swept back and drooping
        return t;
    }

    private static void Profile(LowPolyMesh m, float[,] p, float f, int sides, Color top, Color belly)
    {
        var centres = new List<Vector3>(); var radii = new List<Vector2>();
        for (int i = 0; i < p.GetLength(0); i++)
        {
            centres.Add(new Vector3(0f, p[i, 3], p[i, 0]) * f);
            radii.Add(new Vector2(p[i, 1], p[i, 2]) * f);
        }
        m.Loft(centres, radii, sides, top, belly);
    }

    // The body's top and bottom at (x, z) on the 30 m profile (for placing details on the skin).
    private static float Top(float[,] p, float z, float x) { Sample(p, z, out float rx, out float ry, out float cy); return cy + ry * Mathf.Sqrt(Mathf.Max(0f, 1f - (x / rx) * (x / rx))); }
    private static float Bottom(float[,] p, float z, float x) { Sample(p, z, out float rx, out float ry, out float cy); return cy - ry * Mathf.Sqrt(Mathf.Max(0f, 1f - (x / rx) * (x / rx))); }
    private static void Sample(float[,] p, float z, out float rx, out float ry, out float cy)
    {
        int n = p.GetLength(0);
        for (int i = 0; i < n - 1; i++)
        {
            if (z > p[i, 0] || z < p[i + 1, 0]) continue;
            float t = Mathf.InverseLerp(p[i, 0], p[i + 1, 0], z);
            rx = Mathf.Lerp(p[i, 1], p[i + 1, 1], t); ry = Mathf.Lerp(p[i, 2], p[i + 1, 2], t); cy = Mathf.Lerp(p[i, 3], p[i + 1, 3], t);
            return;
        }
        int e = z > p[0, 0] ? 0 : n - 1;
        rx = p[e, 1]; ry = p[e, 2]; cy = p[e, 3];
    }

    // ------------------------------------------------------------------ dolphin

    public class DolphinRig
    {
        public Transform Root, Fluke;
    }

    /// <summary>A bottlenose dolphin about 2.6 m long: grey back, pale belly, curved dorsal fin, beak.</summary>
    public static DolphinRig Dolphin(Transform parent)
    {
        Color back = new Color(0.27f, 0.31f, 0.37f), side = new Color(0.5f, 0.54f, 0.6f), belly = new Color(0.86f, 0.87f, 0.88f), eye = new Color(0.02f, 0.02f, 0.03f);
        var rig = new DolphinRig { Root = new GameObject("Dolphin").transform };
        rig.Root.SetParent(parent, false);
        var m = new LowPolyMesh();
        var centres = new List<Vector3>(); var radii = new List<Vector2>();
        foreach (var (z, rx, ry, y) in new[]
        {
            (1.32f, 0.04f, 0.035f, -0.06f), (1.18f, 0.075f, 0.065f, -0.05f), (1.08f, 0.1f, 0.09f, -0.02f), (0.98f, 0.17f, 0.19f, 0.04f), (0.78f, 0.24f, 0.26f, 0.02f),
            (0.42f, 0.3f, 0.32f, 0f), (0.05f, 0.29f, 0.3f, 0f), (-0.35f, 0.21f, 0.24f, 0.01f), (-0.72f, 0.11f, 0.16f, 0.02f), (-0.98f, 0.055f, 0.09f, 0.02f),
        })
        {
            centres.Add(new Vector3(0f, y, z)); radii.Add(new Vector2(rx, ry));
        }
        m.Loft(centres, radii, 10, back, belly, -0.2f);
        // A lighter flank band, as bottlenoses have.
        m.Ellipsoid(new Vector3(0f, -0.04f, 0.25f), new Vector3(0.302f, 0.2f, 0.62f), 10, 5, side, 0.55f, 0.75f);
        // Curved dorsal fin, flippers, eyes.
        m.Fin(new[] { new Vector2(-0.22f, 0f), new Vector2(0.22f, 0f), new Vector2(0.38f, 0.36f), new Vector2(0.31f, 0.38f), new Vector2(0.05f, 0.2f) }, 0.05f, back,
              Matrix4x4.TRS(new Vector3(0f, 0.27f, -0.02f), Quaternion.Euler(0f, 90f, 0f), Vector3.one));
        for (int s = -1; s <= 1; s += 2)
        {
            var fl = new List<Vector2> { new Vector2(0f, 0.1f), new Vector2(0.28f, 0.02f), new Vector2(0.34f, -0.08f), new Vector2(0f, -0.08f) };
            if (s < 0) { for (int i = 0; i < fl.Count; i++) fl[i] = new Vector2(-fl[i].x, fl[i].y); fl.Reverse(); }
            m.Fin(fl, 0.035f, back, Matrix4x4.TRS(new Vector3(s * 0.2f, -0.2f, 0.55f), Quaternion.Euler(90f + 25f, s * 30f, -s * 20f), Vector3.one));
            m.Ellipsoid(new Vector3(s * 0.15f, 0.01f, 0.98f), new Vector3(0.025f, 0.03f, 0.04f), 5, 3, eye, 0f, 1f);
        }
        Part(rig.Root, "Body", m);
        var fluke = new LowPolyMesh();
        var half = new List<Vector2> { new Vector2(0f, 0.06f), new Vector2(0.2f, 0.0f), new Vector2(0.36f, -0.14f), new Vector2(0.3f, -0.2f), new Vector2(0.12f, -0.14f), new Vector2(0f, -0.1f) };
        fluke.Fin(half, 0.04f, back, Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(90f, 0f, 0f), Vector3.one));
        var other = new List<Vector2>(); for (int i = half.Count - 1; i >= 0; i--) other.Add(new Vector2(-half[i].x, half[i].y));
        fluke.Fin(other, 0.04f, back, Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(90f, 0f, 0f), Vector3.one));
        rig.Fluke = Part(rig.Root, "Fluke", fluke);
        rig.Fluke.localPosition = new Vector3(0f, 0.02f, -1.02f);
        return rig;
    }

    // ------------------------------------------------------------------ gull

    public class GullRig
    {
        public Transform Root, WingL, WingR;
    }

    /// <summary>A herring gull, a little larger than life so it reads from the bridge camera.</summary>
    public static GullRig Gull(Transform parent)
    {
        Color white = new Color(0.95f, 0.95f, 0.93f), grey = new Color(0.62f, 0.66f, 0.7f), black = new Color(0.08f, 0.08f, 0.09f), beak = new Color(0.95f, 0.75f, 0.15f);
        var rig = new GullRig { Root = new GameObject("Gull").transform };
        rig.Root.SetParent(parent, false);
        var m = new LowPolyMesh();
        m.Loft(new[] { new Vector3(0f, 0.04f, 0.42f), new Vector3(0f, 0.02f, 0.3f), new Vector3(0f, 0f, 0.08f), new Vector3(0f, 0f, -0.2f), new Vector3(0f, 0.02f, -0.42f) },
               new[] { new Vector2(0.07f, 0.07f), new Vector2(0.1f, 0.1f), new Vector2(0.13f, 0.13f), new Vector2(0.1f, 0.09f), new Vector2(0.03f, 0.02f) }, 8, white, white);
        m.Ellipsoid(new Vector3(0f, 0.12f, 0.46f), new Vector3(0.09f, 0.09f, 0.1f), 8, 5, white, 0f, 1f); // head
        m.Pyramid(new Vector3(0f, 0.1f, 0.55f), new Vector3(0f, 0.08f, 0.7f), 0.025f, beak);
        m.Ellipsoid(new Vector3(0.06f, 0.15f, 0.5f), Vector3.one * 0.015f, 4, 3, black, 0f, 1f);
        m.Ellipsoid(new Vector3(-0.06f, 0.15f, 0.5f), Vector3.one * 0.015f, 4, 3, black, 0f, 1f);
        m.Fin(new[] { new Vector2(-0.1f, 0f), new Vector2(0.1f, 0f), new Vector2(0.08f, -0.16f), new Vector2(-0.08f, -0.16f) }, 0.015f, white,
              Matrix4x4.TRS(new Vector3(0f, 0.03f, -0.38f), Quaternion.Euler(90f, 0f, 0f), Vector3.one)); // tail
        Part(rig.Root, "Body", m);
        rig.WingR = GullWing(rig.Root, 1f, grey, black);
        rig.WingL = GullWing(rig.Root, -1f, grey, black);
        return rig;
    }

    private static Transform GullWing(Transform root, float side, Color grey, Color black)
    {
        var m = new LowPolyMesh();
        var inner = new List<Vector2> { new Vector2(0f, 0.14f), new Vector2(0.5f, 0.12f), new Vector2(0.5f, -0.08f), new Vector2(0f, -0.12f) };
        var outer = new List<Vector2> { new Vector2(0.5f, 0.12f), new Vector2(0.78f, 0.04f), new Vector2(0.9f, -0.04f), new Vector2(0.5f, -0.08f) };
        if (side < 0f)
        {
            for (int i = 0; i < 4; i++) { inner[i] = new Vector2(-inner[i].x, inner[i].y); outer[i] = new Vector2(-outer[i].x, outer[i].y); }
            inner.Reverse(); outer.Reverse();
        }
        var at = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(90f, 0f, 0f), Vector3.one);
        m.Fin(inner, 0.02f, grey, at);
        m.Fin(outer, 0.02f, black, at); // black wing tips
        var t = Part(root, side > 0 ? "Wing R" : "Wing L", m);
        t.localPosition = new Vector3(side * 0.08f, 0.08f, 0.1f);
        return t;
    }

    // ------------------------------------------------------------------ serpent head

    public class SerpentHeadRig
    {
        public Transform Root, Jaw;
    }

    /// <summary>The sea serpent's head, about 3 m long: horned, frilled, toothed, eyes that glow.</summary>
    public static SerpentHeadRig SerpentHead(Transform parent, Color skin, Color dark, Color belly, Color frill)
    {
        Color teeth = new Color(0.9f, 0.88f, 0.78f), eye = new Color(1f, 0.82f, 0.2f), mouth = new Color(0.35f, 0.05f, 0.06f);
        var rig = new SerpentHeadRig { Root = new GameObject("Head").transform };
        rig.Root.SetParent(parent, false);
        var m = new LowPolyMesh();
        m.Loft(new[] { new Vector3(0f, 0f, -0.3f), new Vector3(0f, 0.05f, 0.6f), new Vector3(0f, 0.08f, 1.5f), new Vector3(0f, 0.02f, 2.4f), new Vector3(0f, -0.05f, 3.1f) },
               new[] { new Vector2(0.8f, 0.78f), new Vector2(0.95f, 0.72f), new Vector2(0.78f, 0.5f), new Vector2(0.48f, 0.32f), new Vector2(0.2f, 0.16f) }, 10, skin, belly, -0.55f);
        // Brow ridges, horns, nostrils, cheek frills, a row of teeth hanging from the upper jaw.
        for (int s = -1; s <= 1; s += 2)
        {
            m.Box(new Vector3(s * 0.5f, 0.52f, 1.7f), new Vector3(0.28f, 0.14f, 0.7f), dark, Quaternion.Euler(-8f, s * -12f, s * 10f));
            m.Pyramid(new Vector3(s * 0.4f, 0.6f, 0.5f), new Vector3(s * 0.7f, 1.35f, -0.6f), 0.13f, dark);
            m.Pyramid(new Vector3(s * 0.62f, 0.35f, 0.2f), new Vector3(s * 1.0f, 0.75f, -0.5f), 0.08f, dark);
            m.Box(new Vector3(s * 0.12f, 0.18f, 3.0f), new Vector3(0.06f, 0.05f, 0.1f), mouth);
            var fan = new List<Vector2> { new Vector2(0f, 0.25f), new Vector2(0.55f, 0.65f), new Vector2(0.9f, 0.35f), new Vector2(1.05f, 0f), new Vector2(0.9f, -0.35f), new Vector2(0.5f, -0.55f), new Vector2(0f, -0.25f) };
            if (s < 0) { for (int i = 0; i < fan.Count; i++) fan[i] = new Vector2(-fan[i].x, fan[i].y); fan.Reverse(); }
            m.Fin(fan, 0.05f, frill, Matrix4x4.TRS(new Vector3(s * 0.75f, 0f, 0.1f), Quaternion.Euler(0f, s * -35f, 0f), Vector3.one));
            for (int k = 0; k < 5; k++)
                m.Pyramid(new Vector3(s * (0.4f - k * 0.05f), -0.28f, 2.6f - k * 0.38f), new Vector3(s * (0.4f - k * 0.05f), -0.55f, 2.6f - k * 0.38f), 0.045f, teeth);
        }
        m.Box(new Vector3(0f, -0.3f, 1.8f), new Vector3(0.7f, 0.06f, 2.2f), mouth); // inside of the mouth
        Part(rig.Root, "Skull", m);

        var eyes = new LowPolyMesh();
        for (int s = -1; s <= 1; s += 2) eyes.Ellipsoid(new Vector3(s * 0.6f, 0.34f, 1.95f), new Vector3(0.1f, 0.12f, 0.2f), 6, 4, eye, 0f, 1f);
        Part(rig.Root, "Eyes", eyes, 2.5f);

        var jaw = new LowPolyMesh();
        jaw.Loft(new[] { new Vector3(0f, 0f, 0f), new Vector3(0f, -0.05f, 1.2f), new Vector3(0f, -0.04f, 2.2f), new Vector3(0f, 0f, 2.85f) },
                 new[] { new Vector2(0.68f, 0.26f), new Vector2(0.62f, 0.24f), new Vector2(0.4f, 0.18f), new Vector2(0.16f, 0.1f) }, 9, skin, belly, 0.1f);
        for (int s = -1; s <= 1; s += 2)
            for (int k = 0; k < 4; k++)
                jaw.Pyramid(new Vector3(s * (0.35f - k * 0.05f), 0.15f, 2.45f - k * 0.4f), new Vector3(s * (0.35f - k * 0.05f), 0.4f, 2.45f - k * 0.4f), 0.04f, teeth);
        rig.Jaw = Part(rig.Root, "Jaw", jaw);
        rig.Jaw.localPosition = new Vector3(0f, -0.38f, 0.2f);
        return rig;
    }

    // ------------------------------------------------------------------ shared

    public static Transform Part(Transform parent, string name, LowPolyMesh mesh, float glow = 0f, bool shadows = true)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        mesh.AttachTo(go, shadows && glow <= 0f, glow);
        return go.transform;
    }
}
