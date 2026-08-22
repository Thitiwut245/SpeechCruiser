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
    [Tooltip("Knots gained per second. Arcade tuning: roughly two seconds from stop to full ahead.")]
    [FormerlySerializedAs("accelerationRate")] [SerializeField] private float AccelerationRate = 3.5f;

    [Tooltip("Knots shed per second. Kept above acceleration so the telegraph feels responsive when backing off.")]
    [FormerlySerializedAs("decelerationRate")] [SerializeField] private float DecelerationRate = 5f;

    [Header("Hull Physics")]
    [Tooltip("Extra coasting drag once the telegraph is at STOP.")]
    [FormerlySerializedAs("waterDrag")] [SerializeField] private float WaterDrag = 0.6f;

    [Header("Fuel System")]
    [FormerlySerializedAs("fuel")] [SerializeField] private float Fuel = 100f;                   // Fuel percentage (0-100)
    [FormerlySerializedAs("fuelConsumptionRate")] [SerializeField] private float FuelConsumptionRate = 0.3f;    // % consumed per second at full speed

    [Header("Telemetry (Read-Only)")]
    [FormerlySerializedAs("telegraphGearDisplay")] [SerializeField] private string TelegraphGearDisplay = "STOP";

    private float _targetSpeed = 0f;

    public event Action<string> OnGearChanged;
    public event Action<float> OnFuelDepleted;

    public float Speed => CurrentSpeed;
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

        // Apply Upgrade Multiplier to Acceleration
        float accelMultiplier = 1f;
        if (GameManager.Instance != null)
        {
            accelMultiplier += (GameManager.Instance.EngineLevel - 1) * 0.2f;
        }

        float rate = (Mathf.Abs(_targetSpeed) > Mathf.Abs(CurrentSpeed)) ? AccelerationRate * accelMultiplier : DecelerationRate;
        CurrentSpeed = Mathf.MoveTowards(CurrentSpeed, _targetSpeed, rate * Time.deltaTime);

        if (CurrentGear == TelegraphGear.Stop && Mathf.Abs(CurrentSpeed) > 0.01f)
        {
            CurrentSpeed = Mathf.MoveTowards(CurrentSpeed, 0f, WaterDrag * Mathf.Abs(CurrentSpeed) * Time.deltaTime);
        }

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

        // Apply Upgrade Multiplier (each level adds 20% speed)
        float multiplier = 1f;
        if (GameManager.Instance != null)
        {
            multiplier += (GameManager.Instance.EngineLevel - 1) * 0.2f;
        }

        return baseSpeed * multiplier;
    }

    private void UpdateTelegraphDisplay()
    {
        switch (CurrentGear)
        {
            case TelegraphGear.Flank:      TelegraphGearDisplay = "FLANK AHEAD (10 kn)"; break;
            case TelegraphGear.Full:       TelegraphGearDisplay = "FULL AHEAD (8 kn)"; break;
            case TelegraphGear.Half:       TelegraphGearDisplay = "HALF AHEAD (5 kn)"; break;
            case TelegraphGear.Slow:       TelegraphGearDisplay = "SLOW AHEAD (3 kn)"; break;
            case TelegraphGear.DeadSlow:   TelegraphGearDisplay = "DEAD SLOW (1.5 kn)"; break;
            case TelegraphGear.Stop:       TelegraphGearDisplay = "STOP (0 kn)"; break;
            case TelegraphGear.SlowAstern: TelegraphGearDisplay = "SLOW ASTERN (-2 kn)"; break;
            case TelegraphGear.FullAstern: TelegraphGearDisplay = "FULL ASTERN (-4 kn)"; break;
            default:                       TelegraphGearDisplay = "STOP (0 kn)"; break;
        }
    }
}
