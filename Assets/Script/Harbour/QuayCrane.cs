using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A ship-to-shore container crane on a quay, built in code: legs on rails along the quay
/// edge, a boom reaching out over the berth, a trolley running along the boom, and a
/// spreader on hoist cables that picks up one container at a time. The gantry travels along
/// the quay, the trolley out and back, the spreader down and up: real crane moves, sped up
/// so loading a whole ship takes seconds.
/// It loads the cargo at the home quay (from a stack waiting on the quay) and unloads it at
/// the destination, where yard trucks (<see cref="YardTruck"/>) haul each box away inland.
/// </summary>
public class QuayCrane : MonoBehaviour
{
    private const float Height = 15f;      // boom height above the quay deck
    private const float ContainerHalfHeight = 0.62f;

    private Transform _gantry, _trolley, _spreader;
    private LineRenderer[] _cables;
    private float _deckY, _faceX;
    private Vector3 _spreaderRest;
    private static Material _red, _white, _dark, _cable;

    /// <summary>When true, every move finishes at once (Space skips the loading or unloading).</summary>
    public bool Hurry { get; set; }

    /// <summary>World x of the quay face, and the deck height: where the stack and trucks go.</summary>
    public float FaceX => _faceX;
    public float DeckY => _deckY;

    public static QuayCrane Build(Transform parent, Vector3 berth, float faceX, float deckY)
    {
        Materials();
        var go = new GameObject("QuayCrane");
        go.transform.SetParent(parent, false);
        var crane = go.AddComponent<QuayCrane>();
        crane._deckY = deckY;
        crane._faceX = faceX;

        // The gantry: four legs on the rails, cross beams, and the boom over the water.
        crane._gantry = new GameObject("Gantry").transform;
        crane._gantry.SetParent(go.transform, false);
        crane._gantry.position = new Vector3(faceX, deckY, berth.z + 6f);
        Transform g = crane._gantry;
        foreach (float x in new[] { 1.4f, 10.4f })
            foreach (float z in new[] { -3f, 3f })
            {
                Box(g, _red, new Vector3(x, Height * 0.5f, z), new Vector3(0.7f, Height, 0.7f));
                Box(g, _dark, new Vector3(x, 0.35f, z), new Vector3(1.1f, 0.7f, 1.6f)); // bogie on the rail
            }
        foreach (float z in new[] { -3f, 3f })
        {
            Box(g, _red, new Vector3(5.9f, Height - 0.4f, z), new Vector3(9.7f, 0.6f, 0.6f));
            Box(g, _red, new Vector3(5.9f, 5.5f, z), new Vector3(9.7f, 0.45f, 0.45f));
        }
        // The boom: from behind the land legs out past the ship's far side.
        float boomFrom = 15f, boomTo = -11f;
        foreach (float z in new[] { -1.1f, 1.1f })
            Box(g, _red, new Vector3((boomFrom + boomTo) * 0.5f, Height + 0.5f, z), new Vector3(boomFrom - boomTo, 0.7f, 0.5f));
        Box(g, _white, new Vector3(12.5f, Height + 1.9f, 0f), new Vector3(4.2f, 2.2f, 3.2f)); // machinery house
        Box(g, _red, new Vector3(5.9f, Height + 4f, 0f), new Vector3(0.5f, 7f, 0.5f));        // apex
        Box(g, _white, new Vector3(1.4f, Height - 2.2f, -3.6f), new Vector3(1.6f, 1.6f, 1.4f));  // driver's cab

        // The trolley on the boom and the spreader on its cables.
        crane._trolley = new GameObject("Trolley").transform;
        crane._trolley.SetParent(g, false);
        crane._trolley.localPosition = new Vector3(4f, Height, 0f);
        Box(crane._trolley, _white, Vector3.zero, new Vector3(2.4f, 0.8f, 2.8f));

        crane._spreader = new GameObject("Spreader").transform;
        crane._spreader.SetParent(go.transform, false);
        Box(crane._spreader, _dark, Vector3.zero, new Vector3(1.7f, 0.25f, 1.5f));
        crane._spreaderRest = new Vector3(0f, -6f, 0f);
        crane._spreader.position = crane._trolley.position + crane._spreaderRest;

        crane._cables = new LineRenderer[2];
        for (int i = 0; i < 2; i++)
        {
            var lr = new GameObject("Cable").AddComponent<LineRenderer>();
            lr.transform.SetParent(go.transform, false);
            lr.sharedMaterial = _cable;
            lr.widthMultiplier = 0.08f;
            lr.positionCount = 2;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            crane._cables[i] = lr;
        }
        return crane;
    }

    private void LateUpdate()
    {
        for (int i = 0; i < _cables.Length; i++)
        {
            Vector3 side = new Vector3(0f, 0f, i == 0 ? -0.6f : 0.6f);
            _cables[i].SetPosition(0, _trolley.position + side);
            _cables[i].SetPosition(1, _spreader.position + side + Vector3.up * 0.12f);
        }
    }

    // ------------------------------------------------------------ moves

    /// <summary>
    /// Picks <paramref name="box"/> up where it is and sets it down at <paramref name="to"/>()
    /// (asked every frame, so a target on a bobbing ship is followed). <paramref name="pace"/>
    /// scales every move's time: 1 is a steady crane, 0.4 a hurried one.
    /// </summary>
    public IEnumerator Move(Transform box, System.Func<Vector3> to, float pace)
    {
        float safeY = _deckY + Height - 4f;
        Vector3 from = box.position;

        yield return Travel(from, safeY, 0.55f * pace);
        yield return Hoist(() => from.y + ContainerHalfHeight + 0.12f, 0.35f * pace);
        box.SetParent(_spreader, true);
        yield return Hoist(() => safeY, 0.35f * pace);
        Vector3 target = to();
        yield return Travel(target, safeY, 0.6f * pace);
        yield return Hoist(() => to().y + ContainerHalfHeight + 0.12f, 0.4f * pace);
        box.SetParent(null, true);
        box.position = to();
        yield return Hoist(() => safeY, 0.25f * pace);
    }

    /// <summary>Gantry along the quay and trolley along the boom, together, to stand over <paramref name="over"/>.</summary>
    private IEnumerator Travel(Vector3 over, float hoistY, float seconds)
    {
        Vector3 g0 = _gantry.position, t0 = _trolley.localPosition;
        Vector3 g1 = new Vector3(g0.x, g0.y, over.z);
        Vector3 t1 = new Vector3(over.x - _gantry.position.x, t0.y, t0.z);
        for (float t = 0f; t < seconds && !Hurry; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / seconds);
            _gantry.position = Vector3.Lerp(g0, g1, k);
            _trolley.localPosition = Vector3.Lerp(t0, t1, k);
            _spreader.position = new Vector3(_trolley.position.x, Mathf.Lerp(_spreader.position.y, hoistY, k), _trolley.position.z);
            yield return null;
        }
        _gantry.position = g1;
        _trolley.localPosition = t1;
        _spreader.position = new Vector3(_trolley.position.x, hoistY, _trolley.position.z);
    }

    private IEnumerator Hoist(System.Func<float> y, float seconds)
    {
        float y0 = _spreader.position.y;
        for (float t = 0f; t < seconds && !Hurry; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / seconds);
            _spreader.position = new Vector3(_trolley.position.x, Mathf.Lerp(y0, y(), k), _trolley.position.z);
            yield return null;
        }
        _spreader.position = new Vector3(_trolley.position.x, y(), _trolley.position.z);
    }

    // ------------------------------------------------------------ the quay stack

    /// <summary>A place on the quay for the n-th container: rows along the berth, up to three high.</summary>
    public Vector3 StackSpot(Vector3 berth, int n)
    {
        int row = n % 6, layer = n / 6;
        return new Vector3(_faceX + 5.9f, _deckY + ContainerHalfHeight + layer * 1.3f, berth.z - 7.5f + row * 3f);
    }

    // ------------------------------------------------------------ parts

    private static void Box(Transform parent, Material mat, Vector3 local, Vector3 size)
    {
        var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.Destroy(b.GetComponent<Collider>());
        b.transform.SetParent(parent, false);
        b.transform.localPosition = local;
        b.transform.localScale = size;
        b.GetComponent<Renderer>().sharedMaterial = mat;
    }

    private static void Materials()
    {
        if (_red != null) return;
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        Material M(string n, Color c) { var m = new Material(lit) { name = n }; m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", 0.2f); return m; }
        _red = M("Crane red", new Color(0.74f, 0.20f, 0.14f));
        _white = M("Crane white", new Color(0.92f, 0.92f, 0.90f));
        _dark = M("Crane dark", new Color(0.16f, 0.17f, 0.19f));
        _cable = M("Cable", new Color(0.1f, 0.1f, 0.1f));
    }
}

/// <summary>
/// A little yard truck that comes along the quay to a container, takes it on its trailer,
/// and drives it away inland, then disappears.
/// </summary>
public class YardTruck : MonoBehaviour
{
    private const float Speed = 11f;
    private static Material _cab, _trailer;

    /// <summary>A truck driving up and down between two points for ever, pausing at each end (terminal life).</summary>
    public static YardTruck Patrol(Transform parent, Vector3 a, Vector3 b, float delay)
    {
        var truck = Make(parent);
        truck.transform.SetPositionAndRotation(a, Quaternion.LookRotation(b - a));
        truck.StartCoroutine(truck.Loop(a, b, delay));
        return truck;
    }

    private IEnumerator Loop(Vector3 a, Vector3 b, float delay)
    {
        yield return new WaitForSeconds(delay * 6f);
        while (true)
        {
            foreach (var to in new[] { b, a })
            {
                Quaternion from = transform.rotation, toward = Quaternion.LookRotation(to - transform.position);
                for (float t = 0f; t < 0.8f; t += Time.deltaTime)
                {
                    transform.rotation = Quaternion.Slerp(from, toward, t / 0.8f);
                    yield return null;
                }
                yield return Drive(to);
                yield return new WaitForSeconds(2f);
            }
        }
    }

    /// <summary>Sends a truck from <paramref name="landward"/> to fetch <paramref name="box"/> and carry it back that way.</summary>
    public static YardTruck Fetch(Transform parent, Transform box, Vector3 landward, float deckY)
    {
        var truck = Make(parent);
        truck.StartCoroutine(truck.Run(box, landward, deckY));
        return truck;
    }

    private static YardTruck Make(Transform parent)
    {
        if (_cab == null)
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            _cab = new Material(lit) { name = "Truck cab" };
            _cab.SetColor("_BaseColor", new Color(0.95f, 0.72f, 0.12f));
            _trailer = new Material(lit) { name = "Truck trailer" };
            _trailer.SetColor("_BaseColor", new Color(0.18f, 0.19f, 0.21f));
        }
        var go = new GameObject("YardTruck");
        go.transform.SetParent(parent, false);
        var truck = go.AddComponent<YardTruck>();
        Part(go.transform, _trailer, new Vector3(0f, 0.55f, -0.6f), new Vector3(1.7f, 0.3f, 3.2f));
        Part(go.transform, _cab, new Vector3(0f, 0.95f, 1.6f), new Vector3(1.6f, 1.3f, 1.1f));
        foreach (float x in new[] { -0.75f, 0.75f })
            foreach (float z in new[] { -1.6f, 0.2f, 1.6f })
                Part(go.transform, _trailer, new Vector3(x, 0.3f, z), new Vector3(0.3f, 0.6f, 0.6f));
        return truck;
    }

    private IEnumerator Run(Transform box, Vector3 landward, float deckY)
    {
        Vector3 at = new Vector3(box.position.x, deckY, box.position.z);
        Vector3 home = new Vector3(at.x, deckY, landward.z);
        transform.SetPositionAndRotation(home, Quaternion.LookRotation(at - home));
        yield return Drive(at);
        // Back up under it, take it, and turn for home.
        box.SetParent(transform, true);
        box.localPosition = new Vector3(0f, 0.7f + 0.62f, -0.6f);
        box.localRotation = Quaternion.identity;
        Quaternion from = transform.rotation, toward = Quaternion.LookRotation(home - at);
        for (float t = 0f; t < 0.6f; t += Time.deltaTime)
        {
            transform.rotation = Quaternion.Slerp(from, toward, t / 0.6f);
            yield return null;
        }
        yield return Drive(home);
        Destroy(gameObject);
    }

    private IEnumerator Drive(Vector3 to)
    {
        while ((transform.position - to).sqrMagnitude > 0.05f)
        {
            transform.position = Vector3.MoveTowards(transform.position, to, Speed * Time.deltaTime);
            yield return null;
        }
    }

    private static void Part(Transform parent, Material mat, Vector3 local, Vector3 size)
    {
        var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.Destroy(b.GetComponent<Collider>());
        b.transform.SetParent(parent, false);
        b.transform.localPosition = local;
        b.transform.localScale = size;
        b.GetComponent<Renderer>().sharedMaterial = mat;
    }
}
