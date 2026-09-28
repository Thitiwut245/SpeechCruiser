using UnityEngine;

[RequireComponent(typeof(ShipEngine), typeof(ShipHelm), typeof(ShipMovement))]
public class ShipNavigator : MonoBehaviour
{
    public static ShipNavigator Instance; // Singleton facade

    public ShipEngine Engine { get; private set; }
    public ShipHelm Helm { get; private set; }
    public ShipMovement Movement { get; private set; }

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
    /// <summary>Turn by a set number of degrees. maxRudder 0 = hard over.</summary>
    public void ChangeHeading(float degrees, float maxRudder = 0f)
    {
        Helm.OrderHeadingChange(degrees, maxRudder);
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
