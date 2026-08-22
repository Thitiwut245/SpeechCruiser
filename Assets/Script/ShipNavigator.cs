using UnityEngine;

[RequireComponent(typeof(ShipEngine), typeof(ShipHelm), typeof(ShipMovement))]
public class ShipNavigator : MonoBehaviour
{
    public static ShipNavigator Instance; // Singleton facade

    public ShipEngine Engine { get; private set; }
    public ShipHelm Helm { get; private set; }
    public ShipMovement Movement { get; private set; }

    // Legacy constants for backward compatibility with OllamaClient.cs ExecuteAIAction
    public const float SPEED_FULL_AHEAD = 8.0f;
    public const float SPEED_HALF_AHEAD = 5.0f;
    public const float SPEED_SLOW_AHEAD = 3.0f;
    public const float SPEED_STOP = 0.0f;
    public const float SPEED_ASTERN = -2.5f;

    private void Awake()
    {
        Instance = this;
        Engine = GetComponent<ShipEngine>();
        Helm = GetComponent<ShipHelm>();
        Movement = GetComponent<ShipMovement>();
    }

    // ==============================
    // ENGINE API (Facade)
    // ==============================
    public void SetGear(ShipEngine.TelegraphGear gear)
    {
        Engine.SetGear(gear);
    }

    public void SetSpeed(float newSpeed)
    {
        Engine.SetSpeed(newSpeed);
    }

    public void IncreaseSpeed()
    {
        Engine.IncreaseSpeed();
    }

    public void DecreaseSpeed()
    {
        Engine.DecreaseSpeed();
    }

    // ==============================
    // HELM API (Facade)
    // ==============================
    public void ChangeHeading(float degrees)
    {
        Helm.OrderHeadingChange(degrees);
    }

    public void TurnShip(float rudderDegrees)
    {
        Helm.TurnShip(rudderDegrees);
    }

    public void HardTurn(float direction)
    {
        Helm.HardTurn(direction);
    }

    public void RudderMidships()
    {
        Helm.RudderMidships();
    }

    // ==============================
    // TELEMETRY PROPERTIES (Read-Only)
    // ==============================
    public float currentSpeed => Engine.Speed;
    public float fuel => Engine.CurrentFuel;
    public string telegraphGearDisplay => Engine.GearDisplay;
    public ShipEngine.TelegraphGear currentGear => Engine.Gear;
    public float currentRudderAngle => Helm.CurrentRudderAngle;
}
