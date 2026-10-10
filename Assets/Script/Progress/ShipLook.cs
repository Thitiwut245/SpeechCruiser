using UnityEngine;

/// <summary>
/// The upgrades, visible on the ship: what the Captain earned can be seen, at sea and in port.
///   Engine       Mk II a white band on the funnel; Mk III twin exhaust stacks too. Under way,
///                the funnel puffs smoke.
///   Rudder       bow-thruster marks on the bow (Mk II), and on the stern too (Mk III).
///   Lookout      a spinning radar scanner on the mast (Mk II); a second, taller radar and a
///                crow's nest on the foremast (Mk III).
///   Hull         a belt of steel plates along the waterline (Mk II); bow plating too (Mk III).
///   Crew         a lookout in an orange vest on the bow (Mk II); officers on the bridge wings (Mk III).
/// Sits on the ship's drawn hull (CargoShipModel's space) and rebuilds when a level changes.
/// </summary>
public class ShipLook : MonoBehaviour
{
    private const float DeckY = CargoShipModel.DeckY;
    private static readonly Color White = new Color(0.95f, 0.95f, 0.93f), Steel = new Color(0.42f, 0.45f, 0.48f), Dark = new Color(0.12f, 0.13f, 0.14f);
    private static readonly Color Plate = new Color(0.33f, 0.36f, 0.4f), Vest = new Color(0.95f, 0.45f, 0.1f), Skin = new Color(0.86f, 0.66f, 0.5f), Navy = new Color(0.1f, 0.14f, 0.25f);

    private Transform _parts, _scanner, _scanner2;
    private int _shown = -1;
    private float _nextCheck, _nextPuff;

    /// <summary>Puts the upgrades on a drawn ship.</summary>
    public static ShipLook Attach(GameObject hullVisual)
    {
        var look = hullVisual.GetComponent<ShipLook>();
        return look != null ? look : hullVisual.AddComponent<ShipLook>();
    }

    private static int Signature(GameSession s)
    {
        if (s == null) return 11111;
        return s.Level(Upgrade.Engine) * 10000 + s.Level(Upgrade.Rudder) * 1000 + s.Level(Upgrade.Lookout) * 100 + s.Level(Upgrade.Hull) * 10 + s.Level(Upgrade.Crew);
    }

    private void Update()
    {
        if (Time.unscaledTime >= _nextCheck)
        {
            _nextCheck = Time.unscaledTime + 0.5f;
            int sig = Signature(GameSession.Instance);
            if (sig != _shown) { _shown = sig; Build(GameSession.Instance); }
        }
        if (_scanner != null) _scanner.Rotate(0f, 140f * Time.deltaTime, 0f, Space.Self);
        if (_scanner2 != null) _scanner2.Rotate(0f, -110f * Time.deltaTime, 0f, Space.Self);

        // A strong engine under way: smoke from the funnel.
        var s = GameSession.Instance;
        var nav = ShipNavigator.Instance;
        if (s != null && s.Level(Upgrade.Engine) >= 2 && nav != null && nav.transform.IsChildOf(transform.root) && Mathf.Abs(nav.currentSpeed) > 2f && Time.time >= _nextPuff)
        {
            _nextPuff = Time.time + (s.Level(Upgrade.Engine) >= 3 ? 0.35f : 0.6f);
            Spray.Wisp(transform.TransformPoint(new Vector3(0f, DeckY + 7.1f, -6.7f)), new Color(0.32f, 0.33f, 0.35f), 0f);
        }
    }

    private void Build(GameSession s)
    {
        if (_parts != null) Destroy(_parts.gameObject);
        _scanner = _scanner2 = null;
        if (s == null) return;
        _parts = new GameObject("Upgrades").transform;
        _parts.SetParent(transform, false);
        var m = new LowPolyMesh();

        int engine = s.Level(Upgrade.Engine), rudder = s.Level(Upgrade.Rudder), lookout = s.Level(Upgrade.Lookout);
        int hull = s.Level(Upgrade.Hull), crew = s.Level(Upgrade.Crew);

        // Engine: a white band round the funnel; twin exhaust stacks above it.
        if (engine >= 2) m.Box(new Vector3(0f, DeckY + 5.95f, -6.72f), new Vector3(1.42f, 0.24f, 1.12f), White, Quaternion.Euler(-9f, 0f, 0f));
        if (engine >= 3)
            for (int side = -1; side <= 1; side += 2)
                m.Frustum(new Vector3(side * 0.22f, DeckY + 6.85f, -6.6f), 0.13f, 0.12f, 0.55f, 8, 1f, 1f, Dark, true, true);

        // Rudder: bow-thruster marks (a ring with a cross) on both bows; stern ones too at Mk III.
        if (rudder >= 2) { Thruster(m, 7.2f, 2.22f); }
        if (rudder >= 3) { Thruster(m, -6.6f, 2.45f); }

        // Hull: a belt of steel plates along the waterline; bow plates at Mk III.
        if (hull >= 2)
            for (int side = -1; side <= 1; side += 2)
                for (float z = -5.4f; z < 4.4f; z += 1.25f)
                    m.Box(new Vector3(side * 2.38f, -0.55f, z + 0.6f), new Vector3(0.08f, 0.62f, 1.15f), Plate);
        if (hull >= 3)
            for (int side = -1; side <= 1; side += 2)
            {
                m.Box(new Vector3(side * 2.05f, -0.35f, 6.6f), new Vector3(0.08f, 0.9f, 1.6f), Plate, Quaternion.Euler(0f, side * -18f, 0f));
                m.Box(new Vector3(side * 1.35f, 0.0f, 7.9f), new Vector3(0.08f, 1.0f, 1.3f), Plate, Quaternion.Euler(0f, side * -40f, 0f));
            }

        // Lookout: a crow's nest on the foremast at Mk III.
        if (lookout >= 3)
        {
            m.Frustum(new Vector3(0f, 4.6f, 6.4f), 0.42f, 0.45f, 0.45f, 8, 1f, 1f, Steel, false, false);
            m.Box(new Vector3(0f, 4.55f, 6.4f), new Vector3(0.85f, 0.06f, 0.85f), Steel);
        }

        // Crew: a lookout on the bow; officers on the bridge wings.
        if (crew >= 2) Sailor(m, new Vector3(0f, 2.05f, 7.6f), Vest);
        if (crew >= 3) { Sailor(m, new Vector3(2.4f, DeckY + 4.07f, -4.95f), Navy); Sailor(m, new Vector3(-2.4f, DeckY + 4.07f, -4.95f), Navy); }

        if (m.Verts.Count > 0) CreatureModels.Part(_parts, "Fittings", m);

        // Radar scanners (they spin, so they are parts of their own).
        if (lookout >= 2) _scanner = Scanner(new Vector3(0f, DeckY + 6.72f, -5.4f), 1.8f);
        if (lookout >= 3)
        {
            var mast = new LowPolyMesh();
            mast.Box(new Vector3(0f, DeckY + 6.0f, -4.85f), new Vector3(0.12f, 1.6f, 0.12f), Steel);
            CreatureModels.Part(_parts, "Radar Mast 2", mast);
            _scanner2 = Scanner(new Vector3(0f, DeckY + 6.85f, -4.85f), 2.3f);
        }
    }

    private Transform Scanner(Vector3 at, float length)
    {
        var m = new LowPolyMesh();
        m.Box(Vector3.zero, new Vector3(length, 0.14f, 0.28f), White);
        m.Box(new Vector3(0f, -0.12f, 0f), new Vector3(0.18f, 0.12f, 0.18f), Dark);
        var t = CreatureModels.Part(_parts, "Radar Scanner", m);
        t.localPosition = at;
        return t;
    }

    private static void Thruster(LowPolyMesh m, float z, float halfWidth)
    {
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 c = new Vector3(side * halfWidth, 0.25f, z);
            Quaternion r = Quaternion.Euler(0f, side * (z > 0f ? -14f : 10f), 0f);
            m.Box(c, new Vector3(0.05f, 0.08f, 0.62f), White, r);
            m.Box(c, new Vector3(0.05f, 0.62f, 0.08f), White, r);
            m.Box(c + r * new Vector3(0f, 0.31f, 0f), new Vector3(0.05f, 0.06f, 0.62f), White, r);
            m.Box(c + r * new Vector3(0f, -0.31f, 0f), new Vector3(0.05f, 0.06f, 0.62f), White, r);
            m.Box(c + r * new Vector3(0f, 0f, 0.31f), new Vector3(0.05f, 0.62f, 0.06f), White, r);
            m.Box(c + r * new Vector3(0f, 0f, -0.31f), new Vector3(0.05f, 0.62f, 0.06f), White, r);
        }
    }

    /// <summary>A small standing crew member (about 1.7 m): legs, body in <paramref name="coat"/>, head, cap.</summary>
    private static void Sailor(LowPolyMesh m, Vector3 foot, Color coat)
    {
        m.Box(foot + new Vector3(0f, 0.42f, 0f), new Vector3(0.3f, 0.84f, 0.2f), Navy);
        m.Box(foot + new Vector3(0f, 1.12f, 0f), new Vector3(0.42f, 0.58f, 0.26f), coat);
        m.Ellipsoid(foot + new Vector3(0f, 1.55f, 0f), new Vector3(0.13f, 0.15f, 0.13f), 6, 4, Skin, 0f, 1f);
        m.Box(foot + new Vector3(0f, 1.69f, 0.03f), new Vector3(0.28f, 0.07f, 0.3f), Navy);
    }
}
