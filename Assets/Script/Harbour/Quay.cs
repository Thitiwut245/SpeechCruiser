using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A terminal's berth, along its quay face (TerminalBuilder): one berth the length of
/// the ship, marked as real ports mark them (a yellow edge line and end bars on the quay,
/// black fenders hanging on its face, bollards for the mooring lines), and at the destination
/// a yellow outline on the water showing exactly where the ship should lie.
/// Its trigger, just inside the quay face, is how ShipHull knows the ship has bumped it.
/// The berth: the ship lies with its centre at <see cref="Berth"/>, bow to +Z, quay to starboard.
/// </summary>
public class Quay : MonoBehaviour
{
    private const float MaxBerthHalfLength = 10f;

    /// <summary>Half the berth's length: up to 10 m, less if the quay is shorter.</summary>
    public float BerthHalfLength { get; private set; } = MaxBerthHalfLength;

    /// <summary>Where the ship's centre lies when alongside (at the ship's ride height).</summary>
    public Vector3 Berth { get; private set; }
    /// <summary>World x of the quay face.</summary>
    public float FaceX { get; private set; }
    /// <summary>Height of the quay's deck.</summary>
    public float DeckY { get; private set; }
    /// <summary>The terminal's paved yard, in the world.</summary>
    public Bounds DeckBounds { get; private set; }
    /// <summary>The home quay (where the ship loads) and the destination's (where it unloads).</summary>
    public static Quay Home { get; private set; }
    public static Quay Destination { get; private set; }

    /// <summary>The ship-to-shore crane working this berth.</summary>
    public QuayCrane Crane { get; private set; }

    /// <summary>The bollards the lines go to, bow first.</summary>
    public readonly List<Vector3> Bollards = new List<Vector3>();

    private GameObject _outline;
    private static Material _yellow, _black, _iron;

    /// <summary>The destination berth's yellow outline on the water: shown until the ship is tied up.</summary>
    public void ShowOutline(bool show)
    {
        if (_outline != null) _outline.SetActive(show);
    }

    /// <summary>
    /// Fits the berth to a terminal whose quay wall runs along <paramref name="faceX"/>, its
    /// deck at <paramref name="deckY"/>, covering <paramref name="footprint"/> (x/z), for a
    /// ship lying at <paramref name="berth"/>.
    /// </summary>
    public static Quay Build(Transform parent, Rect footprint, Vector3 berth, float faceX, float deckY, bool destination)
    {
        Materials();
        var go = new GameObject(destination ? "DestinationQuay" : "HomeQuay");
        go.transform.SetParent(parent, false);
        var quay = go.AddComponent<Quay>();
        quay.Berth = berth;
        quay.FaceX = faceX;
        quay.DeckY = deckY;
        quay.DeckBounds = new Bounds(new Vector3(footprint.center.x, deckY * 0.5f, footprint.center.y),
                                     new Vector3(footprint.width, deckY, footprint.height));
        quay.BerthHalfLength = MaxBerthHalfLength;
        float BerthHalfLength = quay.BerthHalfLength;

        float deck = quay.DeckY;
        float z0 = berth.z;

        // Fenders: black rubber hanging down the face, every few metres along the berth.
        for (float z = -BerthHalfLength; z <= BerthHalfLength + 0.01f; z += 3.5f)
            Piece(go, PrimitiveType.Cylinder, _black, new Vector3(faceX - 0.3f, deck * 0.5f, z0 + z), new Vector3(0.6f, deck * 0.5f, 0.6f));

        // Bollards near the edge: bow, two springs, stern.
        foreach (float z in new[] { BerthHalfLength - 1f, 3f, -3f, -BerthHalfLength + 1f })
        {
            var at = new Vector3(faceX + 1f, deck, z0 + z);
            Piece(go, PrimitiveType.Cylinder, _iron, at + Vector3.up * 0.3f, new Vector3(0.45f, 0.3f, 0.45f));
            Piece(go, PrimitiveType.Cylinder, _iron, at + Vector3.up * 0.65f, new Vector3(0.65f, 0.06f, 0.65f));
            quay.Bollards.Add(at + Vector3.up * 0.5f);
        }

        // The berth painted on the quay: a yellow edge line and a bar at each end.
        // Just inside the terminal's white edge line (0–0.3 m) and a hair thicker, so the two never share a surface.
        Piece(go, PrimitiveType.Cube, _yellow, new Vector3(faceX + 0.45f, deck + 0.025f, z0), new Vector3(0.3f, 0.05f, BerthHalfLength * 2f));
        foreach (float z in new[] { -BerthHalfLength, BerthHalfLength })
            Piece(go, PrimitiveType.Cube, _yellow, new Vector3(faceX + 1.6f, deck + 0.02f, z0 + z), new Vector3(2.6f, 0.04f, 0.35f));

        if (destination)
        {
            // Where to lie, on the (calm) harbour water: a yellow outline round the ship's place.
            quay._outline = new GameObject("BerthOutline");
            quay._outline.transform.SetParent(go.transform, false);
            // Sized to the ship (17.3 m), whatever the quay's length.
            float halfW = faceX - berth.x - 0.6f, y = 0.08f, shipHalf = MaxBerthHalfLength;
            float l = shipHalf * 2f;
            Piece(quay._outline, PrimitiveType.Cube, _yellow, new Vector3(berth.x - halfW, y, z0), new Vector3(0.35f, 0.05f, l));
            Piece(quay._outline, PrimitiveType.Cube, _yellow, new Vector3(berth.x + halfW, y, z0), new Vector3(0.35f, 0.05f, l));
            Piece(quay._outline, PrimitiveType.Cube, _yellow, new Vector3(berth.x, y, z0 - shipHalf), new Vector3(halfW * 2f, 0.05f, 0.35f));
            Piece(quay._outline, PrimitiveType.Cube, _yellow, new Vector3(berth.x, y, z0 + shipHalf), new Vector3(halfW * 2f, 0.05f, 0.35f));
        }

        quay.Crane = QuayCrane.Build(go.transform, berth, faceX, deck);
        if (destination) Destination = quay; else Home = quay;

        // The terminal, for bumps: running into it bumps the ship off.
        var bump = new GameObject("QuayEdge");
        bump.transform.SetParent(go.transform, false);
        bump.transform.position = new Vector3(footprint.center.x, 0f, footprint.center.y);
        var box = bump.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(footprint.width, 12f, footprint.height);
        return quay;
    }

    private static void Piece(GameObject parent, PrimitiveType type, Material mat, Vector3 position, Vector3 scale)
    {
        var p = GameObject.CreatePrimitive(type);
        Object.Destroy(p.GetComponent<Collider>());
        p.transform.SetParent(parent.transform, false);
        p.transform.position = position;
        p.transform.localScale = scale;
        var r = p.GetComponent<Renderer>();
        r.sharedMaterial = mat;
    }

    private static void Materials()
    {
        if (_yellow != null) return;
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        Material M(string name, Color c)
        {
            var m = new Material(lit) { name = name };
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", 0.15f);
            return m;
        }
        _yellow = M("Berth yellow", new Color(0.98f, 0.80f, 0.12f));
        _black = M("Fender", new Color(0.08f, 0.08f, 0.09f));
        _iron = M("Bollard", new Color(0.22f, 0.24f, 0.27f));
    }
}
