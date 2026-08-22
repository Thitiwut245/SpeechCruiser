using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Arcade hull dynamics tuned for the isometric camera.
///
/// The tuning target is turn radius measured in ship lengths, not realism.
/// The hull is ~17.6 units long. A real cruiser needs 3-5 ship lengths to come
/// about; 30°/sec puts this one at ~0.9 lengths (15 unit radius at 8 kn), which
/// is arcade-tight but still reads as a ship coming round rather than spinning
/// on the spot. Anything much past 40°/sec pivots the hull inside its own
/// length and looks broken.
///
/// The rudder also keeps some bite at a standstill (a pivot), the hull slides
/// outward through a turn instead of tracking on rails, and it banks into the
/// turn so the manoeuvre is legible from a fixed isometric angle.
/// </summary>
[RequireComponent(typeof(ShipEngine), typeof(ShipHelm))]
public class ShipMovement : MonoBehaviour
{
    [Header("Turning")]
    [Tooltip("Degrees/sec of yaw at full rudder and full speed. Drives the turn radius — raise it to tighten " +
             "turns. Past ~40 the hull turns inside its own length and stops reading as a ship.")]
    [FormerlySerializedAs("turningPower")] [SerializeField] private float TurningPower = 30f;

    [Tooltip("Speed at which the rudder reaches full bite. Set near cruising speed so the turn circle stays a " +
             "consistent size across the telegraph — set it low and the ship turns tighter the slower it goes.")]
    [SerializeField] private float FullAuthoritySpeed = 6f;

    [Tooltip("Fraction of turning power still available dead in the water. 0 = a stopped ship cannot turn at all.")]
    [Range(0f, 1f)]
    [SerializeField] private float PivotAuthority = 0.3f;

    [Header("Arcade Feel")]
    [Tooltip("How far the hull slides outward through a turn. 0 = the ship tracks its nose exactly.")]
    [Range(0f, 1f)]
    [SerializeField] private float LateralDrift = 0.18f;

    [Tooltip("Visual roll into the turn, in degrees.")]
    [SerializeField] private float BankAngle = 12f;

    [Tooltip("How quickly the hull rolls into and out of the bank.")]
    [SerializeField] private float BankSmoothing = 4f;

    [Tooltip("Optional hull mesh to roll. Leave empty to bank the whole ship object.")]
    [SerializeField] private Transform HullVisual;

    private ShipEngine _engine;
    private ShipHelm _helm;
    private float _currentBank = 0f;
    private float _turnRate = 0f;

    private void Awake()
    {
        _engine = GetComponent<ShipEngine>();
        _helm = GetComponent<ShipHelm>();
    }

    private void Update()
    {
        float rudder = _helm.MaxAngle > 0f ? _helm.CurrentRudderAngle / _helm.MaxAngle : 0f;

        UpdateRotation(rudder);
        UpdateTranslation(rudder);
        UpdateBank(rudder);
    }

    private void UpdateRotation(float rudder)
    {
        float speed = _engine.Speed;

        // Rudder bite ramps from a slow standstill pivot up to full authority,
        // and reverses when the ship is making way astern.
        float bite = Mathf.Lerp(PivotAuthority, 1f, Mathf.Clamp01(Mathf.Abs(speed) / FullAuthoritySpeed));
        float sternward = speed < -0.05f ? -1f : 1f;

        _turnRate = rudder * TurningPower * bite * sternward;

        if (Mathf.Abs(_turnRate) < 0.01f) return;

        transform.Rotate(0f, _turnRate * Time.deltaTime, 0f, Space.World);
    }

    private void UpdateTranslation(float rudder)
    {
        float speed = _engine.Speed;
        if (Mathf.Abs(speed) < 0.01f) return;

        // Slide away from the turn so the stern kicks out — reads as momentum
        // rather than the hull being welded to its heading. Flattened to the
        // water plane, because transform.right tilts with the visual bank and
        // would otherwise lift the ship off the surface over a long turn.
        Vector3 drift = transform.right * (-rudder * LateralDrift * Mathf.Abs(speed));
        drift.y = 0f;

        transform.position += (transform.forward * speed + drift) * Time.deltaTime;
    }

    private void UpdateBank(float rudder)
    {
        float lean = Mathf.Clamp01(Mathf.Abs(_engine.Speed) / FullAuthoritySpeed);
        float targetBank = -rudder * BankAngle * lean;

        _currentBank = Mathf.Lerp(_currentBank, targetBank, Time.deltaTime * BankSmoothing);

        if (HullVisual != null)
        {
            Vector3 local = HullVisual.localEulerAngles;
            HullVisual.localEulerAngles = new Vector3(local.x, local.y, _currentBank);
            return;
        }

        Vector3 angles = transform.eulerAngles;
        transform.eulerAngles = new Vector3(angles.x, angles.y, _currentBank);
    }
}
