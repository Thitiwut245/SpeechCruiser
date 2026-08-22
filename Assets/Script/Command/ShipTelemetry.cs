using UnityEngine;

/// <summary>
/// One rounded snapshot of ship state, used by both the bark system and the
/// conversation model.
///
/// Values are deliberately COARSE. The old code fed the model
/// "speed 3.4 kn, heading 213°" recomputed every call, which did two bad things:
/// it changed the prompt on every single request and so destroyed Ollama's prefix
/// cache, and it handed a 4B model spurious precision to invent detail around.
/// Speed is whole knots, fuel and hazard distance snap to 5s, heading is a compass
/// point. Identical situations now produce an identical prompt.
/// </summary>
public struct ShipTelemetry
{
    public string Gear;
    public int SpeedKn;
    public string Compass;
    public int FuelPct;
    public bool HazardDetected;
    public int HazardDistanceM;

    private static int RoundTo(float v, int step)
    {
        return Mathf.RoundToInt(v / step) * step;
    }

    public static ShipTelemetry Capture()
    {
        var t = new ShipTelemetry
        {
            Gear = "STOP",
            SpeedKn = 0,
            Compass = "N",
            FuelPct = 0,
            HazardDetected = false,
            HazardDistanceM = 0,
        };

        var nav = ShipNavigator.Instance;
        if (nav != null)
        {
            t.Gear = nav.telegraphGearDisplay;
            t.SpeedKn = Mathf.RoundToInt(nav.currentSpeed);
            t.Compass = CompassPoint(nav.transform.eulerAngles.y);
            t.FuelPct = RoundTo(nav.fuel, 5);
        }

        var radar = RadarSystem.Instance;
        if (radar != null && radar.IsHazardDetected)
        {
            t.HazardDetected = true;
            t.HazardDistanceM = Mathf.Max(RoundTo(radar.CurrentHazardDistance, 5), 5);
        }

        return t;
    }

    /// <summary>Compact single line for the conversation model's prompt.</summary>
    public string ToPromptLine()
    {
        string radar = HazardDetected
            ? "reef " + HazardDistanceM + "m dead ahead"
            : "clear";

        return "[STATUS: telegraph " + Gear +
               ", speed " + SpeedKn + " kn" +
               ", heading " + Compass +
               ", fuel " + FuelPct + "%" +
               ", radar " + radar + "]";
    }

    public static string CompassPoint(float heading)
    {
        heading = ((heading % 360f) + 360f) % 360f;

        if (heading >= 337.5f || heading < 22.5f) return "N";
        if (heading < 67.5f) return "NE";
        if (heading < 112.5f) return "E";
        if (heading < 157.5f) return "SE";
        if (heading < 202.5f) return "S";
        if (heading < 247.5f) return "SW";
        if (heading < 292.5f) return "W";
        return "NW";
    }
}
