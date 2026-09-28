using UnityEngine;

/// <summary>
/// One rounded snapshot of ship state, used by both the bark system and the
/// conversation model.
///
/// Values are deliberately COARSE. The old code fed the model
/// "speed 3.4 kn, heading 213°" recomputed every call, which did two bad things:
/// it changed the prompt on every single request, which defeats prompt caching, and it
/// handed the model spurious precision to invent detail around.
/// Speed is whole knots, fuel and hazard distance snap to 5s, heading is a compass
/// point. Identical situations now produce an identical prompt.
/// </summary>
public struct ShipTelemetry
{
    public string Gear;
    public int SpeedKn;
    /// <summary>Speed the current order is heading for, e.g. 9.6 after an engine upgrade.</summary>
    public float OrderedKn;
    public string Compass;
    public int FuelPct;
    public bool HazardDetected;
    public int HazardDistanceM;

    /// <summary>True when a hazard is on the ship's course (not just nearby).</summary>
    public bool HazardOnCourse;
    /// <summary>"Mine", "Reef" or "Hazard".</summary>
    public string HazardKind;
    /// <summary>Plain words for where it is: "dead ahead", "ahead on the left", "behind"...</summary>
    public string HazardWhere;
    /// <summary>Which way to turn away from it: "right (starboard)" or "left (port)".</summary>
    public string HazardAvoid;
    /// <summary>Seconds until impact at the current speed, when on course.</summary>
    public float HazardSecondsToImpact;

    public bool DestinationKnown;
    public int DestinationDistanceM;
    /// <summary>Compass bearing to the dock, 0-359.</summary>
    public int DestinationBearing;
    /// <summary>Where the dock lies from the bow: "dead ahead", "on the port bow", "astern"...</summary>
    public string DestinationRelative;

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
            HazardOnCourse = false,
            HazardKind = "Hazard",
            HazardWhere = "",
            HazardAvoid = "",
            HazardSecondsToImpact = float.PositiveInfinity,
            DestinationKnown = false,
            DestinationRelative = "",
        };

        var nav = ShipNavigator.Instance;
        if (nav != null)
        {
            t.Gear = nav.telegraphGearDisplay;
            t.SpeedKn = Mathf.RoundToInt(nav.currentSpeed);
            t.OrderedKn = nav.Engine != null ? Mathf.Abs(nav.Engine.OrderedSpeed) : 0f;
            t.Compass = CompassPoint(nav.transform.eulerAngles.y);
            t.FuelPct = RoundTo(nav.fuel, 5);
        }

        // The hazard on course matters most; otherwise report the closest one.
        var radar = RadarSystem.Instance;
        if (radar != null && radar.IsThreat)
        {
            t.HazardDetected = true;
            t.HazardOnCourse = true;
            t.HazardKind = radar.ThreatKind;
            t.HazardDistanceM = Mathf.Max(RoundTo(radar.ThreatDistance, 5), 5);
            t.HazardSecondsToImpact = radar.TimeToImpact;
            SetHazardDirection(ref t, radar.ThreatBearing);
        }
        else if (radar != null && radar.IsHazardDetected)
        {
            t.HazardDetected = true;
            t.HazardKind = radar.CurrentHazardKind;
            t.HazardDistanceM = Mathf.Max(RoundTo(radar.CurrentHazardDistance, 5), 5);
            SetHazardDirection(ref t, Mathf.DeltaAngle(0f, radar.CurrentHazardAngle));
        }

        var level = LevelGenerator.Instance;
        if (nav != null && level != null && level.IsLevelReady)
        {
            Vector3 toDock = level.EndPosition - nav.transform.position;
            toDock.y = 0f;
            float bearing = (Mathf.Atan2(toDock.x, toDock.z) * Mathf.Rad2Deg + 360f) % 360f;

            t.DestinationKnown = true;
            t.DestinationDistanceM = Mathf.Max(RoundTo(toDock.magnitude, 10), 10);
            t.DestinationBearing = Mathf.RoundToInt(bearing) % 360;
            t.DestinationRelative = RelativeBearing(Mathf.DeltaAngle(nav.transform.eulerAngles.y, bearing));
        }

        return t;
    }

    /// <summary>Compact single line for the conversation model's prompt.</summary>
    public string ToPromptLine()
    {
        string radar = HazardDetected
            ? HazardKind.ToLowerInvariant() + " " + HazardDistanceM + "m " + HazardWhere + (HazardOnCourse ? " (on our course)" : "")
            : "clear";

        string dock = DestinationKnown
            ? ", dock " + DestinationRelative + " " + DestinationDistanceM + "m"
            : "";

        return "[STATUS: telegraph " + Gear +
               ", speed " + SpeedKn + " kn" +
               ", heading " + Compass +
               ", fuel " + FuelPct + "%" +
               ", radar " + radar + dock + "]";
    }

    private static void SetHazardDirection(ref ShipTelemetry t, float relative)
    {
        t.HazardWhere = PlainDirection(relative);
        t.HazardAvoid = relative < 0f ? "right (starboard)" : "left (port)";
    }

    /// <summary>Beginner wording for a bearing relative to the bow (negative = left).</summary>
    public static string PlainDirection(float relative)
    {
        float a = Mathf.Abs(relative);
        string side = relative < 0f ? "left" : "right";

        if (a <= 12f) return "dead ahead";
        if (a <= 60f) return "ahead on the " + side;
        if (a <= 120f) return "on the " + side;
        if (a <= 168f) return "behind on the " + side;
        return "behind";
    }

    /// <summary>Standard bridge wording for a bearing relative to the bow (negative = port).</summary>
    public static string RelativeBearing(float relative)
    {
        float a = Mathf.Abs(relative);
        string side = relative < 0f ? "port" : "starboard";

        if (a <= 10f) return "dead ahead";
        if (a <= 45f) return "fine on the " + side + " bow";
        if (a <= 80f) return "on the " + side + " bow";
        if (a <= 110f) return "on the " + side + " beam";
        if (a <= 160f) return "on the " + side + " quarter";
        return "astern";
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
