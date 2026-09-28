/// <summary>
/// Every order the player can give. This is a CLOSED set — the whole point of the
/// deterministic command path is that nothing outside this enum can ever be produced,
/// so the LLM cannot invent an action that the ship does not implement.
/// </summary>
public enum ShipAction
{
    None = 0,

    // --- Engine telegraph: absolute gears ---
    EngineFullAstern,
    EngineSlowAstern,
    EngineStop,
    EngineDeadSlow,
    EngineSlow,
    EngineHalf,
    EngineFull,
    EngineFlank,

    // --- Engine telegraph: relative shifts ---
    EngineIncrease,
    EngineDecrease,

    // --- Helm ---
    TurnPort,
    TurnStarboard,
    HardPort,
    HardStarboard,
    RudderMidships,

    // --- Queries: no machinery change, answered from live telemetry ---
    QueryStatus,
    QueryHazard,
    QueryFuel,
    QueryHeading,
    QuerySpeed,
    QueryDestination,

    // --- Undo: restores the gear and rudder from before the last order ---
    Belay,
}

/// <summary>
/// Mutually exclusive families of order. A single utterance may contain at most one
/// action from each — you cannot order two speeds at once. Without this, "go fast and
/// turn left" matched both EngineFull (from "fast") and EngineSlow (from "go"), and
/// whichever executed last silently won.
/// </summary>
public enum ActionCategory
{
    Nothing,
    Engine,
    Helm,
    Query,
}

public static class ShipActionExtensions
{
    public static ActionCategory Category(this ShipAction a)
    {
        if (a == ShipAction.None) return ActionCategory.Nothing;
        if (a.IsQuery()) return ActionCategory.Query;
        if (a.IsHelm()) return ActionCategory.Helm;
        return ActionCategory.Engine;
    }

    /// <summary>True if this action physically changes the ship: engine orders (Harris) and helm orders (Finn).</summary>
    public static bool IsMachinery(this ShipAction a)
    {
        switch (a)
        {
            case ShipAction.None:
            case ShipAction.QueryStatus:
            case ShipAction.QueryHazard:
            case ShipAction.QueryFuel:
            case ShipAction.QueryHeading:
            case ShipAction.QuerySpeed:
            case ShipAction.QueryDestination:
                return false;
            default:
                return true;
        }
    }

    /// <summary>True if this action is a request for information.</summary>
    public static bool IsQuery(this ShipAction a)
    {
        switch (a)
        {
            case ShipAction.QueryStatus:
            case ShipAction.QueryHazard:
            case ShipAction.QueryFuel:
            case ShipAction.QueryHeading:
            case ShipAction.QuerySpeed:
            case ShipAction.QueryDestination:
                return true;
            default:
                return false;
        }
    }

    /// <summary>Absolute forward gears. Vetoed whenever the order says astern.</summary>
    public static bool IsAheadGear(this ShipAction a)
    {
        switch (a)
        {
            case ShipAction.EngineDeadSlow:
            case ShipAction.EngineSlow:
            case ShipAction.EngineHalf:
            case ShipAction.EngineFull:
            case ShipAction.EngineFlank:
                return true;
            default:
                return false;
        }
    }

    public static bool IsAsternGear(this ShipAction a)
    {
        return a == ShipAction.EngineSlowAstern || a == ShipAction.EngineFullAstern;
    }

    public static bool IsPortTurn(this ShipAction a)
    {
        return a == ShipAction.TurnPort || a == ShipAction.HardPort;
    }

    public static bool IsStarboardTurn(this ShipAction a)
    {
        return a == ShipAction.TurnStarboard || a == ShipAction.HardStarboard;
    }

    /// <summary>True if this is a helm order (rudder), as opposed to an engine order.</summary>
    public static bool IsHelm(this ShipAction a)
    {
        switch (a)
        {
            case ShipAction.TurnPort:
            case ShipAction.TurnStarboard:
            case ShipAction.HardPort:
            case ShipAction.HardStarboard:
            case ShipAction.RudderMidships:
                return true;
            default:
                return false;
        }
    }
}
