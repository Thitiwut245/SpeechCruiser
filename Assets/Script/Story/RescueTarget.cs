using UnityEngine;

/// <summary>
/// Someone adrift in a rowboat, waiting to be picked up: the boat bobs, the person waves,
/// and a bright ring pulses on the water so it shows from the bridge camera. Scenery only
/// (no collider): the story director decides when the ship is close and slow enough.
/// </summary>
public class RescueTarget : MonoBehaviour
{
    private Transform _boat;
    private Transform _arm;
    private Transform _person;
    private Transform _ring;
    private float _phase;
    private float _boardedAt = -1f;
    private Transform _ship;

    public bool Boarded => _boardedAt >= 0f;

    /// <summary>A rowboat with <paramref name="who"/> in it, at <paramref name="position"/> on the water.</summary>
    public static RescueTarget Create(Transform parent, Vector3 position, float yaw, CharacterProfile who)
    {
        var root = new GameObject("Rescue Rowboat");
        root.transform.SetParent(parent, false);
        root.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        var target = root.AddComponent<RescueTarget>();

        var boat = new GameObject("Boat");
        boat.transform.SetParent(root.transform, false);
        boat.transform.localScale = Vector3.one * 2.2f;
        Hull().AttachTo(boat);
        target._boat = boat.transform;

        var person = new GameObject("Person");
        person.transform.SetParent(boat.transform, false);
        person.transform.localPosition = new Vector3(0f, 0.05f, -0.2f);
        Person(who).AttachTo(person);
        target._person = person.transform;

        var arm = new GameObject("WavingArm");
        arm.transform.SetParent(person.transform, false);
        arm.transform.localPosition = new Vector3(0.28f, 0.95f, 0f);
        var armMesh = new LowPolyMesh();
        armMesh.Box(new Vector3(0f, 0.3f, 0f), new Vector3(0.12f, 0.6f, 0.12f), who.Clothes);
        armMesh.Box(new Vector3(0f, 0.65f, 0f), new Vector3(0.13f, 0.13f, 0.13f), who.Skin);
        armMesh.AttachTo(arm);
        target._arm = arm.transform;

        var ring = new GameObject("Marker");
        ring.transform.SetParent(root.transform, false);
        ring.transform.localPosition = new Vector3(0f, 0.15f, 0f);
        var ringMesh = new LowPolyMesh();
        ringMesh.Ring(Vector3.zero, 5.2f, 6f, 24, new Color(1f, 0.55f, 0.1f));
        ringMesh.AttachTo(ring, false);
        target._ring = ring.transform;

        target._phase = Random.value * 10f;
        return target;
    }

    private static LowPolyMesh Hull()
    {
        var m = new LowPolyMesh();
        Color wood = new Color(0.62f, 0.2f, 0.15f), trim = new Color(0.92f, 0.9f, 0.85f), inside = new Color(0.45f, 0.3f, 0.18f);
        // A long tub, wider at the top, pointed by squeezing the ends.
        m.Frustum(new Vector3(0f, -0.25f, 0f), 0.75f, 1f, 0.55f, 10, 0.55f, 1.55f, wood, false, false);
        m.Frustum(new Vector3(0f, 0.28f, 0f), 1.01f, 1.01f, 0.08f, 10, 0.55f, 1.55f, trim, false, false);
        m.Box(new Vector3(0f, 0f, 0f), new Vector3(0.9f, 0.06f, 2.6f), inside);       // floor
        m.Box(new Vector3(0f, 0.18f, 0.55f), new Vector3(1f, 0.06f, 0.28f), inside);   // seats
        m.Box(new Vector3(0f, 0.18f, -0.75f), new Vector3(0.9f, 0.06f, 0.28f), inside);
        // An orange flag on a pole: easy to see from above.
        m.Box(new Vector3(-0.35f, 1.1f, 1.1f), new Vector3(0.05f, 1.8f, 0.05f), trim);
        m.Box(new Vector3(-0.35f, 1.75f, 0.8f), new Vector3(0.03f, 0.4f, 0.6f), new Color(1f, 0.5f, 0.05f));
        return m;
    }

    private static LowPolyMesh Person(CharacterProfile who)
    {
        var m = new LowPolyMesh();
        m.Frustum(new Vector3(0f, 0.1f, 0f), 0.32f, 0.24f, 0.75f, 7, 1f, 0.7f, who.Clothes, true, true);
        m.Ellipsoid(new Vector3(0f, 1.08f, 0f), new Vector3(0.2f, 0.24f, 0.21f), 8, 6, who.Skin, 0f, 1f);
        m.Ellipsoid(new Vector3(0f, 1.14f, 0.03f), new Vector3(0.23f, 0.22f, 0.24f), 8, 5, who.HairColour, 0f, 0.5f);
        // A life jacket.
        m.Frustum(new Vector3(0f, 0.45f, 0f), 0.34f, 0.3f, 0.35f, 7, 1f, 0.75f, new Color(1f, 0.45f, 0.05f), false, false);
        return m;
    }

    /// <summary>The person climbs aboard: they hop toward the ship and the empty boat drifts off.</summary>
    public void Board(Transform ship)
    {
        if (Boarded) return;
        _boardedAt = Time.time;
        _ship = ship;
        if (_ring != null) _ring.gameObject.SetActive(false);
    }

    private void Update()
    {
        float t = Time.time + _phase;
        if (_boat != null)
        {
            _boat.localPosition = new Vector3(0f, Mathf.Sin(t * 1.3f) * 0.12f, 0f);
            _boat.localRotation = Quaternion.Euler(Mathf.Sin(t * 1.1f) * 4f, 0f, Mathf.Sin(t * 0.9f) * 6f);
        }
        if (_arm != null && !Boarded) _arm.localRotation = Quaternion.Euler(0f, 0f, -20f + Mathf.Sin(t * 6f) * 35f);
        if (_ring != null && _ring.gameObject.activeSelf)
        {
            float pulse = 1f + 0.12f * Mathf.Sin(t * 3f);
            _ring.localScale = new Vector3(pulse, 1f, pulse);
        }

        if (!Boarded) return;

        // Over half a second the person arcs onto the ship's deck, then is gone.
        float k = (Time.time - _boardedAt) / 0.6f;
        if (_person != null)
        {
            if (k >= 1f || _ship == null)
            {
                Destroy(_person.gameObject);
                _person = null;
            }
            else
            {
                Vector3 from = _boat.TransformPoint(new Vector3(0f, 0.05f, -0.2f));
                Vector3 to = _ship.position + Vector3.up * 2f;
                _person.position = Vector3.Lerp(from, to, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 3f;
            }
        }
        // The empty boat drifts away and sinks out of sight.
        if (Time.time - _boardedAt > 1.5f)
        {
            transform.position += (transform.forward * 0.6f + Vector3.down * 0.25f) * Time.deltaTime;
            if (Time.time - _boardedAt > 12f) Destroy(gameObject);
        }
    }
}
