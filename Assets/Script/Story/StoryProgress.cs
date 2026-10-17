using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Which story missions are done (saved in PlayerPrefs as story.done.&lt;id&gt;) and which one
/// is being sailed. Missions open in order: each needs the one before it. Credits, upgrades
/// and the hull are the career's, shared with Free Sail.
/// </summary>
public static class StoryProgress
{
    private const string DonePref = "story.done.";

    /// <summary>The mission being sailed; null in Free Sail. Survives scene loads.</summary>
    public static StoryMission ActiveMission { get; private set; }

    // Briefings already shown this session: a retry goes straight to the ship.
    private static readonly HashSet<string> _briefed = new HashSet<string>();

    public static bool IsDone(StoryMission mission) => mission != null && PlayerPrefs.GetInt(DonePref + mission.Id, 0) == 1;

    /// <summary>The first mission is always open; every other needs the one before it.</summary>
    public static bool IsUnlocked(StoryMission mission)
    {
        int i = StoryCatalog.IndexOf(mission);
        return i == 0 || (i > 0 && IsDone(StoryCatalog.Missions[i - 1]));
    }

    public static bool IsUnlocked(StoryChapter chapter) => IsUnlocked(chapter.Missions[0]);

    public static int DoneCount
    {
        get
        {
            int n = 0;
            foreach (var m in StoryCatalog.Missions) if (IsDone(m)) n++;
            return n;
        }
    }

    /// <summary>The first open mission not yet done, or null when the story is finished.</summary>
    public static StoryMission NextToPlay
    {
        get
        {
            foreach (var m in StoryCatalog.Missions) if (!IsDone(m) && IsUnlocked(m)) return m;
            return null;
        }
    }

    public static bool IsFinished => NextToPlay == null;

    /// <summary>Sail this mission next (the Game scene reads it).</summary>
    public static void Begin(StoryMission mission) => ActiveMission = mission;

    /// <summary>Back to Free Sail.</summary>
    public static void Leave() => ActiveMission = null;

    public static void MarkDone(StoryMission mission)
    {
        if (mission == null) return;
        PlayerPrefs.SetInt(DonePref + mission.Id, 1);
        PlayerPrefs.Save();
    }

    /// <summary>True the first time it is asked for a mission this session.</summary>
    public static bool TakeBriefing(StoryMission mission) => mission != null && _briefed.Add(mission.Id);

    /// <summary>Forgets every finished mission (New Career).</summary>
    public static void Reset()
    {
        foreach (var m in StoryCatalog.Missions) { PlayerPrefs.DeleteKey(DonePref + m.Id); StoryScore.ClearBest(m.Id); }
        Logbook.Reset();
        PlayerPrefs.Save();
        _briefed.Clear();
        ActiveMission = null;
    }
}
