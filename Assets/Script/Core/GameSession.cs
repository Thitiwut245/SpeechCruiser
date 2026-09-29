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
    /// <summary>How much longer than a straight run the safe route must be: how much it winds.</summary>
    public float MinWeave;

    public static readonly JobSpec Easy = new JobSpec
    { Name = "EASY", Containers = 5, Reward = 300, DensityScale = 0.75f, ExtraRows = 0, Mines = false, MinWeave = 1.1f };

    public static readonly JobSpec Normal = new JobSpec
    { Name = "NORMAL", Containers = 10, Reward = 700, DensityScale = 1f, ExtraRows = 0, Mines = true, MinWeave = 1.2f };

    public static readonly JobSpec Hard = new JobSpec
    { Name = "HARD", Containers = 15, Reward = 1500, DensityScale = 1.25f, ExtraRows = 6, Mines = true, MinWeave = 1.3f };

    public bool IsSameJob(JobSpec other) => Name == other.Name;
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
/// The career (credits, upgrades, hull, deliveries, voyages, last job) is saved in
/// PlayerPrefs whenever it changes, so quitting and coming back carries on where it was.
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
    public int HullHealth
    {
        get { return _hullHealth; }
        set { _hullHealth = value; SaveCareer(); }
    }
    private int _hullHealth = -1;

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

    /// <summary>Cargoes delivered in this career. Routes get longer as this grows (see DifficultyTier).</summary>
    public int MissionsCompleted { get; private set; }

    /// <summary>Voyages started in this career, delivered or not.</summary>
    public int VoyagesStarted { get; private set; }

    // ------------------------------------------------------------ saved career

    private const string CareerPref = "career.";
    private static readonly string[] CareerKeys = { "credits", "engine", "helm", "hull", "delivered", "voyages", "job" };

    /// <summary>A career has been played before (the title screen offers to start a new one).</summary>
    public bool HasSavedCareer => PlayerPrefs.HasKey(CareerPref + "voyages");

    protected override void Awake()
    {
        base.Awake();
        if (Instance != this) return;
        LoadCareer();
    }

    private void LoadCareer()
    {
        if (!HasSavedCareer) return;
        _credits = PlayerPrefs.GetInt(CareerPref + "credits", _credits);
        _engineLevel = Mathf.Max(1, PlayerPrefs.GetInt(CareerPref + "engine", _engineLevel));
        _helmLevel = Mathf.Max(1, PlayerPrefs.GetInt(CareerPref + "helm", _helmLevel));
        _hullHealth = PlayerPrefs.GetInt(CareerPref + "hull", -1);
        MissionsCompleted = Mathf.Max(0, PlayerPrefs.GetInt(CareerPref + "delivered", 0));
        VoyagesStarted = Mathf.Max(0, PlayerPrefs.GetInt(CareerPref + "voyages", 0));
        string job = PlayerPrefs.GetString(CareerPref + "job", JobSpec.Easy.Name);
        CurrentJob = job == JobSpec.Hard.Name ? JobSpec.Hard : job == JobSpec.Normal.Name ? JobSpec.Normal : JobSpec.Easy;
    }

    private void SaveCareer()
    {
        PlayerPrefs.SetInt(CareerPref + "credits", _credits);
        PlayerPrefs.SetInt(CareerPref + "engine", _engineLevel);
        PlayerPrefs.SetInt(CareerPref + "helm", _helmLevel);
        PlayerPrefs.SetInt(CareerPref + "hull", _hullHealth);
        PlayerPrefs.SetInt(CareerPref + "delivered", MissionsCompleted);
        PlayerPrefs.SetInt(CareerPref + "voyages", VoyagesStarted);
        PlayerPrefs.SetString(CareerPref + "job", CurrentJob.Name);
        PlayerPrefs.Save();
    }

    /// <summary>Starts over: $500, first upgrades, a sound hull, and the learning layout again.</summary>
    public void StartNewCareer()
    {
        foreach (var key in CareerKeys) PlayerPrefs.DeleteKey(CareerPref + key);
        PlayerPrefs.Save();
        _credits = 500;
        _engineLevel = 1;
        _helmLevel = 1;
        _hullHealth = -1;
        MissionsCompleted = 0;
        VoyagesStarted = 0;
        CurrentJob = JobSpec.Easy;
        LastMissionSummary = "";
        LastResult = null;
        OnProgressUpdated?.Invoke();
    }

    /// <summary>Voyages on which an Easy job gets the gentle learning layout.</summary>
    public const int GentleVoyages = 2;

    /// <summary>Deliveries per step of extra route length, and the most steps there can be.</summary>
    public const int DeliveriesPerTier = 3;
    public const int MaxTier = 3;

    /// <summary>
    /// The learning layout (long clear runway, fewer hazards, gentle route): only for an Easy
    /// job, and only on the first voyages started. Sinking doesn't earn it back, and a Hard job
    /// never gets it.
    /// </summary>
    public bool UseGentleLayout => IsGentle(CurrentJob);

    /// <summary>Whether this job would get the gentle layout on the next voyage.</summary>
    public bool IsGentle(JobSpec job) => job.IsSameJob(JobSpec.Easy) && VoyagesStarted < GentleVoyages;

    /// <summary>1 at the start; +1 every few deliveries, capped. Credits play no part.</summary>
    public int DifficultyTier => 1 + Mathf.Min(MissionsCompleted / DeliveriesPerTier, MaxTier - 1);

    public void RecordMissionCompleted()
    {
        MissionsCompleted++;
        SaveCareer();
    }

    public void RecordVoyageStarted()
    {
        VoyagesStarted++;
        SaveCareer();
    }

    public event Action OnProgressUpdated;

    public void AddCredits(int amount)
    {
        _credits += amount;
        SaveCareer();
        OnProgressUpdated?.Invoke();
    }

    public void SpendCredits(int amount)
    {
        _credits -= amount;
        SaveCareer();
        OnProgressUpdated?.Invoke();
    }

    public void SetJob(JobSpec job)
    {
        CurrentJob = job;
        SaveCareer();
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
        SaveCareer();
        OnProgressUpdated?.Invoke();
        Debug.Log($"[GAME SESSION]: Upgraded {upgradeType}. Credits remaining: {_credits}");
        return true;
    }
}
