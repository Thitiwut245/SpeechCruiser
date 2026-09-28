using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Hull dynamics for a heavy cargo ship, tuned for voice orders: things happen slowly
/// enough that an order given early lands in time.
///
/// Turning: 20°/sec at full rudder and cruising speed gives a turn circle of about
/// 23 units at full ahead (a bit over one ship length), and the swing itself has
/// inertia, so the bow takes a moment to start coming round and keeps going a little
/// after the rudder comes off. The rudder keeps some bite at a standstill (a pivot),
/// the hull slides outward through a turn, and the hull model banks into it so the
/// manoeuvre reads from the fixed isometric camera. Only the model banks: colliders,
/// radar and cargo stay level on the ship's root.
/// </summary>
[RequireComponent(typeof(ShipEngine), typeof(ShipHelm))]
public class ShipMovement : MonoBehaviour
{
    [Header("Turning")]
    [Tooltip("Degrees/sec of yaw at full rudder and full authority speed. 20 gives a turn circle of about " +
             "23 units at full ahead. Past ~35 the hull turns inside its own length and stops reading as a ship.")]
    [FormerlySerializedAs("turningPower")] [SerializeField] private float TurningPower = 20f;

    [Tooltip("How fast the rate of turn can change, in degrees/sec per second. This is the hull's swing " +
             "inertia: low values make the bow slow to start and slow to stop coming round.")]
    [SerializeField] private float YawAccel = 14f;

    [Tooltip("Speed at which the rudder reaches full bite. Set near cruising speed so the turn circle stays a " +
             "consistent size across the telegraph — set it low and the ship turns tighter the slower it goes.")]
    [SerializeField] private float FullAuthoritySpeed = 6f;

    [Tooltip("Fraction of turning power still available dead in the water. 0 = a stopped ship cannot turn at all.")]
    [Range(0f, 1f)]
    [SerializeField] private float PivotAuthority = 0.3f;

    [Header("Feel")]
    [Tooltip("How far the hull slides outward through a turn. 0 = the ship tracks its nose exactly.")]
    [Range(0f, 1f)]
    [SerializeField] private float LateralDrift = 0.18f;

    [Tooltip("Visual roll into the turn, in degrees.")]
    [SerializeField] private float BankAngle = 6f;

    [Tooltip("How quickly the hull rolls into and out of the bank.")]
    [SerializeField] private float BankSmoothing = 3f;

    [Tooltip("Hull model to roll. Leave empty (or set to this object) and a child 'HullVisual' is made at " +
             "start, so the roll never tilts the colliders, radar or cargo grid.")]
    [SerializeField] private Transform HullVisual;

    private ShipEngine _engine;
    private ShipHelm _helm;
    private float _currentBank = 0f;
    private float _turnRate = 0f;
    private Vector3 _knockbackVelocity = Vector3.zero;

    /// <summary>Current rate of turn, degrees/sec (positive = to starboard).</summary>
    public float TurnRate => _turnRate;
    public float YawAcceleration => YawAccel;

    /// <summary>The banking model. Cargo is parented here so it rolls with the deck.</summary>
    public Transform Visual => HullVisual != null ? HullVisual : transform;

    public void ApplyKnockback(Vector3 velocity)
    {
        _knockbackVelocity += velocity;
    }

    /// <summary>Stops any swing in progress (after a collision push-out).</summary>
    public void KillTurn()
    {
        _turnRate = 0f;
    }

    private void Awake()
    {
        _engine = GetComponent<ShipEngine>();
        _helm = GetComponent<ShipHelm>();
        EnsureVisualChild();
    }

    /// <summary>
    /// The ship prefab is a model root with the gameplay components on it, so rolling the
    /// root would tilt everything. Move the drawn mesh onto a child and roll that instead.
    /// </summary>
    private void EnsureVisualChild()
    {
        if (HullVisual != null && HullVisual != transform) return;

        var visual = new GameObject("HullVisual").transform;
        visual.SetParent(transform, false);

        var filter = GetComponent<MeshFilter>();
        var meshRenderer = GetComponent<MeshRenderer>();
        if (filter != null && meshRenderer != null)
        {
            visual.gameObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            var copy = visual.gameObject.AddComponent<MeshRenderer>();
            copy.sharedMaterials = meshRenderer.sharedMaterials;
            copy.shadowCastingMode = meshRenderer.shadowCastingMode;
            copy.receiveShadows = meshRenderer.receiveShadows;
            meshRenderer.enabled = false;
        }

        // Drawn children (not colliders) roll with the hull too.
        var children = new System.Collections.Generic.List<Transform>();
        foreach (Transform child in transform)
        {
            if (child == visual) continue;
            if (child.GetComponent<Renderer>() != null && child.GetComponentInChildren<Collider>() == null) children.Add(child);
        }
        foreach (var child in children) child.SetParent(visual, true);

        HullVisual = visual;
    }

    private void Update()
    {
        float rudder = _helm.MaxAngle > 0f ? _helm.CurrentRudderAngle / _helm.MaxAngle : 0f;

        UpdateRotation(rudder);
        UpdateTranslation(rudder);
        UpdateBank(rudder);
    }

    private float Sternward => _engine.Speed < -0.05f ? -1f : 1f;

    /// <summary>+1 making way ahead (or stopped), -1 astern: the rudder turns the ship the other way going backwards.</summary>
    public float RudderEffect => Sternward;

    private void UpdateRotation(float rudder)
    {
        float speed = _engine.Speed;

        // Rudder bite ramps from a slow standstill pivot up to full authority,
        // and reverses when the ship is making way astern.
        float bite = Mathf.Lerp(PivotAuthority, 1f, Mathf.Clamp01(Mathf.Abs(speed) / FullAuthoritySpeed));
        float cargo = GameSession.Instance != null ? GameSession.Instance.CargoTurnFactor : 1f;
        float target = rudder * TurningPower * cargo * bite * Sternward;

        _turnRate = Mathf.MoveTowards(_turnRate, target, YawAccel * Time.deltaTime);
        if (Mathf.Abs(_turnRate) < 0.01f) return;

        transform.Rotate(0f, _turnRate * Time.deltaTime, 0f, Space.World);
    }

    private void UpdateTranslation(float rudder)
    {
        if (_knockbackVelocity.sqrMagnitude > 0.001f)
        {
            transform.position += _knockbackVelocity * Time.deltaTime;
            _knockbackVelocity = Vector3.Lerp(_knockbackVelocity, Vector3.zero, Time.deltaTime * 4f);
        }

        float speed = _engine.Speed;
        if (Mathf.Abs(speed) < 0.01f) return;

        // Slide away from the turn so the stern kicks out. The turn reverses when going
        // astern, so the slide does too. Flattened to the water plane.
        Vector3 drift = transform.right * (-rudder * Sternward * LateralDrift * Mathf.Abs(speed));
        drift.y = 0f;

        Vector3 forward = transform.forward;
        forward.y = 0f;
        transform.position += (forward.normalized * speed + drift) * Time.deltaTime;
    }

    private void UpdateBank(float rudder)
    {
        float lean = Mathf.Clamp01(Mathf.Abs(_engine.Speed) / FullAuthoritySpeed);
        float targetBank = -rudder * Sternward * BankAngle * lean;

        _currentBank = Mathf.Lerp(_currentBank, targetBank, Time.deltaTime * BankSmoothing);

        Vector3 local = Visual.localEulerAngles;
        Visual.localEulerAngles = new Vector3(local.x, local.y, _currentBank);
    }
}
