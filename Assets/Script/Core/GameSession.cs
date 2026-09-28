using System;
using UnityEngine;

/// <summary>A delivery job: how the level is built and what it pays.</summary>
public struct JobSpec
{
    public string Name;
    public int Containers;
    public int Reward;
    /// <summary>Multiplies the level's hazard density.</summary>
    public float DensityScale;
    /// <summary>Extra rows of course on top of the base length.</summary>
    public int ExtraRows;
    /// <summary>Whether naval mines appear, or reefs only.</summary>
    public bool Mines;

    public static readonly JobSpec Easy = new JobSpec
    { Name = "EASY", Containers = 5, Reward = 300, DensityScale = 0.75f, ExtraRows = 0, Mines = false };

    public static readonly JobSpec Normal = new JobSpec
    { Name = "NORMAL", Containers = 10, Reward = 700, DensityScale = 1f, ExtraRows = 0, Mines = true };

    public static readonly JobSpec Hard = new JobSpec
    { Name = "HARD", Containers = 15, Reward = 1500, DensityScale = 1.25f, ExtraRows = 6, Mines = true };
}

/// <summary>What happened on the last voyage. The results screen reads it.</summary>
public class MissionResult
{
    public bool Delivered;
    public string JobName;
    public int JobReward;
    public int CargoLoaded;
    public int CargoDelivered;
    /// <summary>Paid on delivery (0 when the ship was lost).</summary>
    public int Pay;
    /// <summary>Charged when the ship was lost (0 on delivery).</summary>
    public int TowingFee;
    public int Hits;
    public int MineHits;
    public float Seconds;
    public int HullAfter;
    public int HullMax;
    public int RepairCost;
    public int CreditsBefore;
    public int CreditsAfter;
}

/// <summary>
/// Player progress that must survive scene loads: credits, upgrades, tutorial flag and the
/// mission picked in the shipyard. Lives on the persistent Services prefab.
/// </summary>
public class GameSession : Singleton<GameSession>
{
    [Header("Player Progress")]
    [SerializeField] private int _credits = 500;
    [SerializeField] private int _engineLevel = 1;
    [SerializeField] private int _helmLevel = 1;

    public int Credits => _credits;
    public int EngineLevel => _engineLevel;
    public int HelmLevel => _helmLevel;

    public JobSpec CurrentJob { get; private set; } = JobSpec.Easy;
    public int CurrentReward => CurrentJob.Reward;
    public int CurrentContainerCount => CurrentJob.Containers;

    /// <summary>Hull points carried between missions; -1 means a fresh, undamaged hull.</summary>
    public int HullHealth { get; set; } = -1;

    public int CargoLoaded { get; private set; }
    public int CargoAboard { get; private set; }

    /// <summary>One line describing how the last mission went, shown in the shipyard.</summary>
    public string LastMissionSummary { get; set; } = "";

    /// <summary>Set when a voyage ends; null before the first one.</summary>
    public MissionResult LastResult { get; set; }

    public const int RepairCostPerHullPoint = 50;
    public const int EngineBaseCost = 100;
    public const int HelmBaseCost = 100;

    public int EngineUpgradeCost => EngineBaseCost * _engineLevel;
    public int HelmUpgradeCost => HelmBaseCost * _helmLevel;

    /// <summary>Cargo makes the ship slower to speed up: -2% per container aboard.</summary>
    public float CargoAccelerationFactor => 1f - 0.02f * CargoAboard;

    /// <summary>Cargo makes the ship slower to turn: -1% per container aboard.</summary>
    public float CargoTurnFactor => 1f - 0.01f * CargoAboard;

    private const string TutorialPref = "tutorial_done";

    /// <summary>Saved, so the tutorial only runs until the player finishes or skips it once.</summary>
    public bool HasSeenTutorial
    {
        get { return PlayerPrefs.GetInt(TutorialPref, 0) == 1; }
        set
        {
            PlayerPrefs.SetInt(TutorialPref, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    public int MissionsCompleted { get; private set; }

    /// <summary>True until the player has delivered a cargo once.</summary>
    public bool IsFirstVoyage => MissionsCompleted == 0;

    public void RecordMissionCompleted()
    {
        MissionsCompleted++;
    }

    public event Action OnProgressUpdated;

    public void AddCredits(int amount)
    {
        _credits += amount;
        OnProgressUpdated?.Invoke();
    }

    public void SpendCredits(int amount)
    {
        _credits -= amount;
        OnProgressUpdated?.Invoke();
    }

    public void SetJob(JobSpec job)
    {
        CurrentJob = job;
    }

    public void LoadCargo(int containers)
    {
        CargoLoaded = containers;
        CargoAboard = containers;
    }

    /// <summary>Removes up to <paramref name="containers"/> from the deck; returns how many actually went over.</summary>
    public int LoseCargo(int containers)
    {
        int lost = Mathf.Min(containers, CargoAboard);
        CargoAboard -= lost;
        return lost;
    }

    public void ClearCargo()
    {
        CargoLoaded = 0;
        CargoAboard = 0;
    }

    /// <summary>Charges up to <paramref name="fee"/>, never below zero credits; returns what was charged.</summary>
    public int ChargeFee(int fee)
    {
        int charged = Mathf.Clamp(fee, 0, Mathf.Max(0, _credits));
        if (charged > 0) SpendCredits(charged);
        return charged;
    }

    public bool TryBuyUpgrade(string upgradeType, int cost)
    {
        if (_credits < cost) return false;

        if (upgradeType == "Engine") _engineLevel++;
        else if (upgradeType == "Helm") _helmLevel++;
        else return false;

        _credits -= cost;
        OnProgressUpdated?.Invoke();
        Debug.Log($"[GAME SESSION]: Upgraded {upgradeType}. Credits remaining: {_credits}");
        return true;
    }
}
