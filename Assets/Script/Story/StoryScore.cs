using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Story mode's score, as plain rules so they can be tested and explained on the report.
/// Coins on the channel (PathCoins) score while the voyage runs: 10 each, doubled from the
/// sixth coin in a row and tripled from the eleventh; a missed coin or a hit starts the run
/// again. On arrival the report adds: each find the lookout reported and the ship picked up
/// (SeaFinds), a clean run (no hits), on time (inside par), every container delivered, and every
/// order given in standard radio English.
/// Coins also pay credits, banked only when the cargo arrives. The best score per mission is
/// kept in PlayerPrefs as story.best.&lt;id&gt;.
/// </summary>
public static class StoryScore
{
    public const int CoinPoints = 10;
    public const int CoinCredits = 3;
    public const int CleanRunBonus = 200;
    public const int OnTimeBonus = 150;
    public const int AllCargoBonus = 100;
    public const int StandardOrderPoints = 10;
    public const int FindPoints = 50;

    private const string BestPref = "story.best.";

    /// <summary>1, 2 or 3: how much the next coin is worth, after <paramref name="run"/> coins in a row.</summary>
    public static int Multiplier(int run) => run >= 10 ? 3 : run >= 5 ? 2 : 1;

    /// <summary>Points for a coin taken after <paramref name="run"/> coins in a row.</summary>
    public static int ForCoin(int run) => CoinPoints * Multiplier(run);

    /// <summary>The end-of-voyage bonuses, in the order the report lists them (label, points); only those earned.</summary>
    public static List<KeyValuePair<string, int>> Bonuses(int hits, bool onTime, bool allCargo, int standardOrders, int finds = 0)
    {
        var list = new List<KeyValuePair<string, int>>();
        if (finds > 0) list.Add(new KeyValuePair<string, int>("finds", finds * FindPoints));
        if (hits == 0) list.Add(new KeyValuePair<string, int>("clean run", CleanRunBonus));
        if (onTime) list.Add(new KeyValuePair<string, int>("on time", OnTimeBonus));
        if (allCargo) list.Add(new KeyValuePair<string, int>("all cargo", AllCargoBonus));
        if (standardOrders > 0) list.Add(new KeyValuePair<string, int>("standard orders", standardOrders * StandardOrderPoints));
        return list;
    }

    public static int Best(string missionId) => PlayerPrefs.GetInt(BestPref + missionId, 0);

    /// <summary>Keeps <paramref name="score"/> if it beats the best; true when it did.</summary>
    public static bool RecordBest(string missionId, int score)
    {
        if (string.IsNullOrEmpty(missionId) || score <= Best(missionId)) return false;
        PlayerPrefs.SetInt(BestPref + missionId, score);
        PlayerPrefs.Save();
        return true;
    }

    /// <summary>Forgets <paramref name="missionId"/>'s best score (New Career).</summary>
    public static void ClearBest(string missionId) => PlayerPrefs.DeleteKey(BestPref + missionId);
}
