using System;
using UnityEngine;
using UnityEngine.Serialization;

public class ShipEngine : MonoBehaviour
{
    public enum TelegraphGear
    {
        FullAstern = -2,
        SlowAstern = -1,
        Stop = 0,
        DeadSlow = 1,
        Slow = 2,
        Half = 3,
        Full = 4,
        Flank = 5   // Emergency maximum — burns fuel fast
    }

    [Header("Telegraph & Engine")]
    [FormerlySerializedAs("currentGear")]
    [SerializeField] private TelegraphGear CurrentGear = TelegraphGear.Stop;

    [FormerlySerializedAs("currentSpeed")]
    [SerializeField] private float CurrentSpeed = 0f;         // Actual current speed (smoothly approaches target)

    [FormerlySerializedAs("isEngineRunning")]
    [SerializeField] private bool IsEngineRunning = false;

    [Header("Speed Per Gear (knots)")]
    [FormerlySerializedAs("speedFlank")] [SerializeField] private float SpeedFlank = 10.0f;
    [FormerlySerializedAs("speedFull")] [SerializeField] private float SpeedFull = 8.0f;
    [FormerlySerializedAs("speedHalf")] [SerializeField] private float SpeedHalf = 5.0f;
    [FormerlySerializedAs("speedSlow")] [SerializeField] private float SpeedSlow = 3.0f;
    [FormerlySerializedAs("speedDeadSlow")] [SerializeField] private float SpeedDeadSlow = 1.5f;
    [FormerlySerializedAs("speedStop")] [SerializeField] private float SpeedStop = 0.0f;
    [FormerlySerializedAs("speedSlowAstern")] [SerializeField] private float SpeedSlowAstern = -2.0f;
    [FormerlySerializedAs("speedFullAstern")] [SerializeField] private float SpeedFullAstern = -4.0f;

    [Header("Engine Dynamics")]
    [Tooltip("Knots gained per second. A loaded cargo ship: about six seconds from stop to full ahead.")]
    [FormerlySerializedAs("accelerationRate")] [SerializeField] private float AccelerationRate = 1.4f;

    [Tooltip("Knots lost per second while coasting (engine eased or stopped). Low on purpose: the ship " +
             "carries its way, so orders have to be given early. Full ahead to a stop takes about eight seconds.")]
    [FormerlySerializedAs("decelerationRate")] [SerializeField] private float DecelerationRate = 1f;

    [Tooltip("Knots lost per second while the engine pushes against the motion (astern while moving ahead). " +
             "The emergency brake: always stronger than just stopping the engine.")]
    [SerializeField] private float AsternBrakeRate = 2.5f;

    [Header("Fuel System")]
    [FormerlySerializedAs("fuel")] [SerializeField] private float Fuel = 100f;                   // Fuel percentage (0-100)
    [FormerlySerializedAs("fuelConsumptionRate")] [SerializeField] private float FuelConsumptionRate = 0.3f;    // % consumed per second at full speed

    [Header("Telemetry (Read-Only)")]
    [FormerlySerializedAs("telegraphGearDisplay")] [SerializeField] private string TelegraphGearDisplay = "STOP";

    private float _targetSpeed = 0f;

    public event Action<string> OnGearChanged;
    public event Action<float> OnFuelDepleted;

    public float Speed => CurrentSpeed;

    /// <summary>
    /// Aground on a sandbank: +1 if the ship ran on going ahead, -1 going astern, 0 afloat.
    /// The ship can't move that way until it backs off the other way (ShipHull refloats it).
    /// </summary>
    public int AgroundDirection { get; private set; }

    /// <summary>The hull has run onto a sandbank: the ship stops dead and can't go on that way.</summary>
    public void RunAground(int direction)
    {
        AgroundDirection = direction >= 0 ? 1 : -1;
        CurrentSpeed = 0f;
    }

    /// <summary>Off the sandbank, afloat again.</summary>
    public void Refloat() => AgroundDirection = 0;

    /// <summary>The speed the current telegraph order is heading for, in knots (includes upgrades).</summary>
    public float OrderedSpeed => GetSpeedForGear(CurrentGear);
    public TelegraphGear Gear => CurrentGear;
    public bool EngineRunning => IsEngineRunning;
    public float CurrentFuel => Fuel;
    public string GearDisplay => TelegraphGearDisplay;

    private void Update()
    {
        UpdateEngine();
        UpdateFuel();
    }

    public void SetGear(TelegraphGear gear)
    {
        CurrentGear = gear;
        UpdateTelegraphDisplay();
        
        OnGearChanged?.Invoke(TelegraphGearDisplay);
    }

    public void SetSpeed(float newSpeed)
    {
        if (newSpeed >= SpeedFull - 0.5f) SetGear(TelegraphGear.Full);
        else if (newSpeed >= SpeedHalf - 0.5f) SetGear(TelegraphGear.Half);
        else if (newSpeed >= SpeedSlow - 0.5f) SetGear(TelegraphGear.Slow);
        else if (newSpeed >= SpeedDeadSlow - 0.5f) SetGear(TelegraphGear.DeadSlow);
        else if (newSpeed <= SpeedFullAstern + 0.5f) SetGear(TelegraphGear.FullAstern);
        else if (newSpeed < -0.1f) SetGear(TelegraphGear.SlowAstern);
        else SetGear(TelegraphGear.Stop);
    }

    /// <summary>
    /// A collision: the ship loses most of its way and the engine room eases two steps
    /// toward STOP, so the player can carry on without re-ordering from a dead stop.
    /// </summary>
    public void AbsorbImpact(float keptSpeedFraction)
    {
        CurrentSpeed *= keptSpeedFraction;
        int gear = (int)CurrentGear;
        SetGear((TelegraphGear)(gear > 0 ? Mathf.Max(0, gear - 2) : 0));
    }

    public void IncreaseSpeed()
    {
        int next = Mathf.Min((int)CurrentGear + 1, (int)TelegraphGear.Flank);
        SetGear((TelegraphGear)next);
    }

    public void DecreaseSpeed()
    {
        int next = Mathf.Max((int)CurrentGear - 1, (int)TelegraphGear.FullAstern);
        SetGear((TelegraphGear)next);
    }

    private void UpdateEngine()
    {
        _targetSpeed = GetSpeedForGear(CurrentGear);

        // The engine upgrade scales speed, acceleration and braking together, so a faster
        // ship still stops in the same time instead of sliding twice as far.
        float engine = GameSession.Instance != null ? GameSession.Instance.EngineSpeedFactor : 1f;
        float accelMultiplier = engine;
        if (GameSession.Instance != null) accelMultiplier *= GameSession.Instance.CargoAccelerationFactor;

        // Three regimes: pushing against the motion brakes hard, speeding up uses the
        // engine, and anything else is the hull coasting down on its own.
        bool pushingAgainst = Mathf.Abs(CurrentSpeed) > 0.05f && Mathf.Abs(_targetSpeed) > 0.05f &&
                              Mathf.Sign(_targetSpeed) != Mathf.Sign(CurrentSpeed);
        float rate = pushingAgainst ? AsternBrakeRate * engine
                   : Mathf.Abs(_targetSpeed) > Mathf.Abs(CurrentSpeed) ? AccelerationRate * accelMultiplier
                   : DecelerationRate * engine;
        CurrentSpeed = Mathf.MoveTowards(CurrentSpeed, _targetSpeed, rate * Time.deltaTime);
        // Aground, the engine can only back the ship off the way it came.
        if (AgroundDirection != 0 && CurrentSpeed * AgroundDirection > 0f) CurrentSpeed = 0f;

        IsEngineRunning = Mathf.Abs(CurrentSpeed) > 0.05f;
    }

    private void UpdateFuel()
    {
        if (!IsEngineRunning || Fuel <= 0f) return;

        float speedRatio = Mathf.Abs(CurrentSpeed) / SpeedFull;
        Fuel -= speedRatio * FuelConsumptionRate * Time.deltaTime;
        Fuel = Mathf.Max(Fuel, 0f);

        if (Fuel <= 0f)
        {
            SetGear(TelegraphGear.Stop);
            Fuel = 0f;
            OnFuelDepleted?.Invoke(Fuel);
        }
    }

    private float GetSpeedForGear(TelegraphGear gear)
    {
        float baseSpeed = SpeedStop;
        switch (gear)
        {
            case TelegraphGear.Flank:      baseSpeed = SpeedFlank; break;
            case TelegraphGear.Full:       baseSpeed = SpeedFull; break;
            case TelegraphGear.Half:       baseSpeed = SpeedHalf; break;
            case TelegraphGear.Slow:       baseSpeed = SpeedSlow; break;
            case TelegraphGear.DeadSlow:   baseSpeed = SpeedDeadSlow; break;
            case TelegraphGear.Stop:       baseSpeed = SpeedStop; break;
            case TelegraphGear.SlowAstern: baseSpeed = SpeedSlowAstern; break;
            case TelegraphGear.FullAstern: baseSpeed = SpeedFullAstern; break;
        }

        // Engine upgrade: +20% per level (capped at Mk 3 in GameSession).
        return baseSpeed * (GameSession.Instance != null ? GameSession.Instance.EngineSpeedFactor : 1f);
    }

    private void UpdateTelegraphDisplay()
    {
        // Knots come from the gear's real speed, so an engine upgrade shows on the HUD.
        string name;
        switch (CurrentGear)
        {
            case TelegraphGear.Flank:      name = "FLANK AHEAD"; break;
            case TelegraphGear.Full:       name = "FULL AHEAD"; break;
            case TelegraphGear.Half:       name = "HALF AHEAD"; break;
            case TelegraphGear.Slow:       name = "SLOW AHEAD"; break;
            case TelegraphGear.DeadSlow:   name = "DEAD SLOW"; break;
            case TelegraphGear.SlowAstern: name = "SLOW ASTERN"; break;
            case TelegraphGear.FullAstern: name = "FULL ASTERN"; break;
            default:                       name = "STOP"; break;
        }
        TelegraphGearDisplay = name + " (" + GetSpeedForGear(CurrentGear).ToString("0.#") + " kn)";
    }
}
