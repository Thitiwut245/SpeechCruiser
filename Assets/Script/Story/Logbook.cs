using UnityEngine;

/// <summary>The kinds of thing the lookout can spot off the route (SeaFinds).</summary>
public enum FindKind
{
    Container,
    Buoy,
    Wreck,
    Barrels,
}

/// <summary>
/// The Captain's logbook: every find picked up (three per story mission, SeaFinds) and every
/// legend of the sea seen (the story's sights). Each entry has a short note in plain English that
/// teaches one sea word. Saved in PlayerPrefs (log.find.&lt;mission&gt;.&lt;n&gt;, log.sight.&lt;kind&gt;);
/// shown on the story screen, where empty slots say there is still something out there.
/// </summary>
public static class Logbook
{
    public const int FindsPerMission = 3;
    private const string FindPref = "log.find.";
    private const string SightPref = "log.sight.";

    public static readonly SightKind[] Legends = { SightKind.DeepShadow, SightKind.SerpentCoils, SightKind.GhostShip, SightKind.SpiritLight };

    /// <summary>The <paramref name="n"/>-th find of a mission (0..2): each mission has its own mix, never two the same.</summary>
    public static FindKind KindFor(StoryMission mission, int n)
    {
        int kinds = System.Enum.GetValues(typeof(FindKind)).Length;
        int start = mission != null ? (mission.Chapter * 3 + mission.Number) % kinds : 0;
        return (FindKind)((start + n) % kinds);
    }

    /// <summary>How the lookout says it on the radio: "a drifting container".</summary>
    public static string Spoken(FindKind kind)
    {
        switch (kind)
        {
            case FindKind.Buoy: return "an old buoy";
            case FindKind.Wreck: return "a wreck";
            case FindKind.Barrels: return "floating barrels";
            default: return "a drifting container";
        }
    }

    public static string Title(FindKind kind)
    {
        switch (kind)
        {
            case FindKind.Buoy: return "Old buoy";
            case FindKind.Wreck: return "Wreck";
            case FindKind.Barrels: return "Floating barrels";
            default: return "Drifting container";
        }
    }

    public static string Note(FindKind kind)
    {
        switch (kind)
        {
            case FindKind.Buoy: return "A buoy floats on the sea and marks safe water or danger. This old one drifted far from its place.";
            case FindKind.Wreck: return "The top of a ship that sank long ago. Sailors call a sunken ship a \"wreck\".";
            case FindKind.Barrels: return "Barrels lost from a cargo ship. Things lost from ships and floating at sea are called \"flotsam\".";
            default: return "A container that fell off a ship in bad weather. Ships report lost cargo, because it is a danger to others.";
        }
    }

    public static string Title(SightKind kind)
    {
        switch (kind)
        {
            case SightKind.SerpentCoils: return "The sea serpent";
            case SightKind.GhostShip: return "The ghost ship";
            case SightKind.SpiritLight: return "The spirit light";
            default: return "The Old One of the Deep";
        }
    }

    public static string Note(SightKind kind)
    {
        switch (kind)
        {
            case SightKind.SerpentCoils: return "A long body in the waves, and a head that looked at the ship. Old sailors drew sea serpents on their charts.";
            case SightKind.GhostShip: return "An old sailing ship with glowing sails, in the fog, then gone. Sailors say ghost ships warn of danger.";
            case SightKind.SpiritLight: return "A kind light that led the ship home through the storm. Maybe the legends were true.";
            default: return "A giant shadow under the water, bigger than the ship. Tomas's grandfather called it the Old One of the Deep.";
        }
    }

    // ------------------------------------------------------------ saved

    public static bool HasFind(string missionId, int n) => PlayerPrefs.GetInt(FindPref + missionId + "." + n, 0) == 1;

    /// <summary>Writes the find in the book; true the first time.</summary>
    public static bool RecordFind(string missionId, int n)
    {
        if (string.IsNullOrEmpty(missionId) || HasFind(missionId, n)) return false;
        PlayerPrefs.SetInt(FindPref + missionId + "." + n, 1);
        PlayerPrefs.Save();
        return true;
    }

    public static bool HasSight(SightKind kind) => PlayerPrefs.GetInt(SightPref + kind, 0) == 1;

    /// <summary>Writes the legend in the book; true the first time.</summary>
    public static bool RecordSight(SightKind kind)
    {
        if (HasSight(kind)) return false;
        PlayerPrefs.SetInt(SightPref + kind, 1);
        PlayerPrefs.Save();
        return true;
    }

    public static int FoundIn(StoryMission mission)
    {
        int n = 0;
        for (int i = 0; i < FindsPerMission; i++) if (HasFind(mission.Id, i)) n++;
        return n;
    }

    /// <summary>Entries written, and the entries there are: every mission's finds plus the legends.</summary>
    public static int Written
    {
        get
        {
            int n = 0;
            foreach (var m in StoryCatalog.Missions) n += FoundIn(m);
            foreach (var k in Legends) if (HasSight(k)) n++;
            return n;
        }
    }

    public static int Entries => StoryCatalog.Missions.Count * FindsPerMission + Legends.Length;

    /// <summary>A clean book (New Career).</summary>
    public static void Reset()
    {
        foreach (var m in StoryCatalog.Missions)
            for (int i = 0; i < FindsPerMission; i++) PlayerPrefs.DeleteKey(FindPref + m.Id + "." + i);
        foreach (var k in Legends) PlayerPrefs.DeleteKey(SightPref + k);
    }
}
