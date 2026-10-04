using UnityEngine;

/// <summary>
/// Something strange in the water beside the route. It stays hidden until the story director
/// reveals it (the ship has come near), then plays out on its own and leaves. Scenery only:
/// no collider, not on the radar. Phase 1 builds the deep shadow; the chapter 2 and 3 sights
/// fall back to it until they are made.
/// </summary>
public class SeaSight : MonoBehaviour
{
    // Deep shadow: rises toward the surface, glides past, sinks away.
    private const float ShadowSpeed = 3f;
    private const float RiseSeconds = 3f;
    private const float StaySeconds = 11f;
    private const float RestY = -1.35f; // a sliver of dark back and its ridge break the surface
    private const float HiddenY = -9f;

    public SightKind Kind { get; private set; }
    public bool Revealed => _revealedAt >= 0f;

    private float _revealedAt = -1f;
    private Transform _body;
    private Transform _tail;
    private Vector3 _heading;
    private float _surface;

    /// <summary>A hidden sight at <paramref name="position"/> (y = the water's surface), heading along <paramref name="heading"/>.</summary>
    public static SeaSight Create(Transform parent, SightKind kind, Vector3 position, Vector3 heading)
    {
        var root = new GameObject("Sea Sight " + kind);
        root.transform.SetParent(parent, false);
        root.transform.position = new Vector3(position.x, position.y + HiddenY, position.z);
        root.transform.rotation = Quaternion.LookRotation(heading.normalized, Vector3.up);
        var sight = root.AddComponent<SeaSight>();
        sight.Kind = kind;
        sight._heading = heading.normalized;
        sight._surface = position.y;

        if (kind != SightKind.DeepShadow)
            Debug.Log("[STORY]: " + kind + " isn't built yet; showing the deep shadow instead.");
        sight.BuildDeepShadow();
        root.SetActive(false);
        return sight;
    }

    private void BuildDeepShadow()
    {
        // Near black: the scene's post-processing lifts dark colours a lot.
        Color dark = new Color(0.004f, 0.008f, 0.016f), back = new Color(0.01f, 0.02f, 0.035f);
        var body = new GameObject("Body");
        body.transform.SetParent(transform, false);
        var m = new LowPolyMesh();
        // A long body, far bigger than the ship, with a low ridge along the back.
        m.Ellipsoid(Vector3.zero, new Vector3(4.5f, 1.8f, 16f), 10, 7, dark, 0f, 1f);
        for (int i = 0; i < 7; i++)
        {
            float z = 9f - i * 3f;
            float h = 1.9f + 0.5f * Mathf.Sin(i * 0.9f);
            m.Pyramid(new Vector3(0f, 1.2f, z), new Vector3(0f, h + 0.6f, z - 0.8f), 0.7f, back);
        }
        m.Ellipsoid(new Vector3(-5f, -0.6f, 4f), new Vector3(2.6f, 0.3f, 1.2f), 6, 4, dark, 0f, 1f); // fins
        m.Ellipsoid(new Vector3(5f, -0.6f, 4f), new Vector3(2.6f, 0.3f, 1.2f), 6, 4, dark, 0f, 1f);
        m.AttachTo(body, false);
        _body = body.transform;

        var tail = new GameObject("Tail");
        tail.transform.SetParent(transform, false);
        tail.transform.localPosition = new Vector3(0f, 0f, -15f);
        var t = new LowPolyMesh();
        t.Ellipsoid(new Vector3(0f, 0f, -3f), new Vector3(1.2f, 0.9f, 4f), 8, 5, dark, 0f, 1f);
        t.Ellipsoid(new Vector3(-3f, 0f, -6.5f), new Vector3(3.2f, 0.25f, 1.6f), 6, 4, back, 0f, 1f);
        t.Ellipsoid(new Vector3(3f, 0f, -6.5f), new Vector3(3.2f, 0.25f, 1.6f), 6, 4, back, 0f, 1f);
        t.AttachTo(tail, false);
        _tail = tail.transform;
    }

    /// <summary>Starts it: rise, glide past, sink and go.</summary>
    public void Reveal()
    {
        if (Revealed) return;
        _revealedAt = Time.time;
        gameObject.SetActive(true);
    }

    private void Update()
    {
        if (!Revealed) return;
        float age = Time.time - _revealedAt;

        float y;
        if (age < RiseSeconds) y = Mathf.Lerp(HiddenY, RestY, Mathf.SmoothStep(0f, 1f, age / RiseSeconds));
        else if (age < RiseSeconds + StaySeconds) y = RestY + Mathf.Sin(age * 0.8f) * 0.25f;
        else y = Mathf.Lerp(RestY, HiddenY, (age - RiseSeconds - StaySeconds) / 4f);

        var p = transform.position + _heading * ShadowSpeed * Time.deltaTime;
        transform.position = new Vector3(p.x, _surface + y, p.z);
        if (_tail != null) _tail.localRotation = Quaternion.Euler(Mathf.Sin(age * 1.6f) * 10f, Mathf.Sin(age * 0.8f) * 12f, 0f);
        if (age > RiseSeconds + StaySeconds + 4f) Destroy(gameObject);
    }
}
