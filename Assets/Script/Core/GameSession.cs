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
    /// <summary>Deliveries needed before this job is offered.</summary>
    public int UnlockDeliveries;
    /// <summary>What a tow home costs when the ship is lost on this job.</summary>
    public int TowingFee;
    /// <summary>Story missions: the route's length in rows, whatever the career tier (0 = by tier).</summary>
    public int Rows;
    /// <summary>Story missions: a fixed layout, the same on every attempt (0 = a new one each time).</summary>
    public int Seed;
    /// <summary>A story mission's job: pays its own reward, no tier bonus, no learning layout.</summary>
    public bool Story;

    public static readonly JobSpec Easy = new JobSpec
    { Name = "EASY", Containers = 5, Reward = 300, DensityScale = 0.75f, ExtraRows = 0, Mines = false, MinWeave = 1.1f,
      UnlockDeliveries = 0, TowingFee = 100 };

    public static readonly JobSpec Normal = new JobSpec
    { Name = "NORMAL", Containers = 10, Reward = 700, DensityScale = 1f, ExtraRows = 0, Mines = true, MinWeave = 1.2f,
      UnlockDeliveries = 2, TowingFee = 200 };

    public static readonly JobSpec Hard = new JobSpec
    { Name = "HARD", Containers = 15, Reward = 1500, DensityScale = 1.25f, ExtraRows = 6, Mines = true, MinWeave = 1.3f,
      UnlockDeliveries = 5, TowingFee = 400 };

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
    public int CreditsBefore;
    public int CreditsAfter;
    /// <summary>Par time for the route; delivering inside it earns OnTimeBonus.</summary>
    public float ParSeconds;
    public int OnTimeBonus;
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
    [SerializeField] private int _credits = StartingCredits;

    public const int StartingCredits = 500;
    [SerializeField] private int _engineLevel = 1;
    [SerializeField] private int _helmLevel = 1;
    private int _lookoutLevel = 1, _platingLevel = 1, _crewLevel = 1;

    public int Credits => _credits;
    public int EngineLevel => _engineLevel;
    public int HelmLevel => _helmLevel;

    /// <summary>The level (1 to 3) of an upgrade.</summary>
    public int Level(Upgrade u)
    {
        switch (u)
        {
            case Upgrade.Engine: return _engineLevel;
            case Upgrade.Rudder: return _helmLevel;
            case Upgrade.Lookout: return _lookoutLevel;
            case Upgrade.Hull: return _platingLevel;
            default: return _crewLevel;
        }
    }

    /// <summary>Lookout gear: the radar sees 25% further per level.</summary>
    public float RadarRangeFactor => 1f + 0.25f * (_lookoutLevel - 1);

    /// <summary>Hull plating: one more hit per level.</summary>
    public int ExtraHull => _platingLevel - 1;

    /// <summary>Crew experience: 2 = "did you mean...?" hints; 3 = also earlier warnings and longer emergency calls.</summary>
    public int CrewLevel => _crewLevel;

    /// <summary>
    /// Why the next level of <paramref name="u"/> can't be fitted yet, or null if it can: it needs
    /// its radio certificate (earned at sea, Progression) and the credits.
    /// </summary>
    public string WhyNot(Upgrade u)
    {
        int level = Level(u);
        if (level >= MaxUpgradeLevel) return "max";
        var cert = Progression.For(u, level + 1);
        if (!Progression.Earned(cert)) return "certificate";
        if (_credits < cert.Credits) return "credits";
        return null;
    }

    /// <summary>Fits the next level of <paramref name="u"/> if its certificate is earned and it can be paid for.</summary>
    public bool TryBuy(Upgrade u)
    {
        if (WhyNot(u) != null) return false;
        var cert = Progression.For(u, Level(u) + 1);
        switch (u)
        {
            case Upgrade.Engine: _engineLevel++; break;
            case Upgrade.Rudder: _helmLevel++; break;
            case Upgrade.Lookout: _lookoutLevel++; break;
            case Upgrade.Hull: _platingLevel++; break;
            default: _crewLevel++; break;
        }
        _credits -= cert.Credits;
        SaveCareer();
        OnProgressUpdated?.Invoke();
        Debug.Log($"[GAME SESSION]: fitted {u} Mk {Level(u)}. Credits remaining: {_credits}");
        return true;
    }

    /// <summary>The story mission being sailed, or else the job picked in the shipyard.</summary>
    public JobSpec CurrentJob => StoryProgress.ActiveMission != null ? StoryProgress.ActiveMission.Job : _careerJob;
    private JobSpec _careerJob = JobSpec.Easy;
    public int CurrentReward => RewardFor(CurrentJob);

    /// <summary>Longer routes at higher tiers pay more: +15% per tier above the first.</summary>
    public const float TierPayBonus = 0.15f;

    public int RewardFor(JobSpec job) => job.Story ? job.Reward : Mathf.RoundToInt(job.Reward * (1f + TierPayBonus * (DifficultyTier - 1)));

    /// <summary>Normal opens after 2 deliveries and Hard after 5, so the first voyages are Easy ones.</summary>
    public bool IsUnlocked(JobSpec job) => MissionsCompleted >= job.UnlockDeliveries;
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

    /// <summary>One line describing how the last mission went (an abandoned one is mentioned in the shipyard).</summary>
    public string LastMissionSummary { get; set; } = "";

    /// <summary>Set when a voyage ends; null before the first one.</summary>
    public MissionResult LastResult { get; set; }

    // Mk 2 costs the base, Mk 3 twice that: $750 per upgrade line, something worth saving for.
    public const int EngineBaseCost = 250;
    public const int HelmBaseCost = 250;

    public int EngineUpgradeCost => _engineLevel < MaxUpgradeLevel ? Progression.For(Upgrade.Engine, _engineLevel + 1).Credits : 0;
    public int HelmUpgradeCost => _helmLevel < MaxUpgradeLevel ? Progression.For(Upgrade.Rudder, _helmLevel + 1).Credits : 0;

    /// <summary>Both upgrades stop at Mk 3: past that the ship outruns the channels.</summary>
    public const int MaxUpgradeLevel = 3;

    /// <summary>Engine: +20% speed per level. Braking scales with it, so she stops in the same time.</summary>
    public float EngineSpeedFactor => 1f + 0.2f * (_engineLevel - 1);

    /// <summary>Rudder: +15% turn rate and swing per level, so the ship turns tighter.</summary>
    public float HelmTurnFactor => 1f + 0.15f * (_helmLevel - 1);

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
    private static readonly string[] CareerKeys = { "credits", "engine", "helm", "hull", "delivered", "voyages", "job", "atsea", "lookout", "plating", "crew" };

    /// <summary>A career has been played before (the title screen offers to start a new one). Opening the shipyard isn't enough.</summary>
    public bool HasSavedCareer => PlayerPrefs.GetInt(CareerPref + "voyages", 0) > 0;

    /// <summary>Anything saved at all, even an upgrade bought before the first voyage: New Career can clear it.</summary>
    public bool HasAnySave => PlayerPrefs.HasKey(CareerPref + "voyages");

    /// <summary>What a voyage left unfinished (game closed at sea) costs on the next launch.</summary>
    public const int AbandonedHullHealth = 1;

    /// <summary>
    /// Set while a voyage is under way. If the game closes before the voyage ends, the next
    /// launch counts it as lost: the tow is charged and the hull comes home patched.
    /// </summary>
    public void MarkAtSea(bool atSea)
    {
        PlayerPrefs.SetInt(CareerPref + "atsea", atSea ? 1 : 0);
        PlayerPrefs.Save();
    }

    /// <summary>Leaving a voyage before it ends counts as losing the ship. Returns the fee charged.</summary>
    public int AbandonVoyage()
    {
        // The tutorial voyage is practice: leaving it costs nothing.
        if (!HasSeenTutorial)
        {
            ClearCargo();
            LastMissionSummary = "TUTORIAL VOYAGE LEFT. NO CHARGE: TRY AGAIN ANY TIME";
            MarkAtSea(false);
            return 0;
        }
        int fee = ChargeFee(CurrentJob.TowingFee);
        HullHealth = AbandonedHullHealth;
        ClearCargo();
        LastMissionSummary = "VOYAGE ABANDONED. CARGO LOST, TOWED HOME: -$" + fee;
        MarkAtSea(false);
        return fee;
    }

    /// <summary>What abandoning now would actually cost (the tow, capped at the credits; nothing on the tutorial voyage).</summary>
    public int AbandonCost => HasSeenTutorial ? Mathf.Min(CurrentJob.TowingFee, Mathf.Max(0, _credits)) : 0;

    protected override void Awake()
    {
        base.Awake();
        if (Instance != this) return;
        // "-fresh" on the command line (the study's launcher): each participant starts a new
        // career with the drill, whatever the last one reached. Settings (mic, keys, volume) stay.
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-fresh") >= 0) StartNewCareer();
        else LoadCareer();
    }

    private void LoadCareer()
    {
        // Anything saved loads (an upgrade bought before the first voyage counts); the title
        // only offers Continue once a voyage has been sailed.
        if (!PlayerPrefs.HasKey(CareerPref + "voyages")) return;
        _credits = PlayerPrefs.GetInt(CareerPref + "credits", _credits);
        _engineLevel = Mathf.Clamp(PlayerPrefs.GetInt(CareerPref + "engine", _engineLevel), 1, MaxUpgradeLevel);
        _helmLevel = Mathf.Clamp(PlayerPrefs.GetInt(CareerPref + "helm", _helmLevel), 1, MaxUpgradeLevel);
        _lookoutLevel = Mathf.Clamp(PlayerPrefs.GetInt(CareerPref + "lookout", 1), 1, MaxUpgradeLevel);
        _platingLevel = Mathf.Clamp(PlayerPrefs.GetInt(CareerPref + "plating", 1), 1, MaxUpgradeLevel);
        _crewLevel = Mathf.Clamp(PlayerPrefs.GetInt(CareerPref + "crew", 1), 1, MaxUpgradeLevel);
        Progression.Begin(_engineLevel, _helmLevel, PlayerPrefs.GetInt("tutorial_done", 0) == 1);
        _hullHealth = PlayerPrefs.GetInt(CareerPref + "hull", -1);
        MissionsCompleted = Mathf.Max(0, PlayerPrefs.GetInt(CareerPref + "delivered", 0));
        VoyagesStarted = Mathf.Max(0, PlayerPrefs.GetInt(CareerPref + "voyages", 0));
        string job = PlayerPrefs.GetString(CareerPref + "job", JobSpec.Easy.Name);
        _careerJob = job == JobSpec.Hard.Name ? JobSpec.Hard : job == JobSpec.Normal.Name ? JobSpec.Normal : JobSpec.Easy;
        if (!IsUnlocked(_careerJob)) _careerJob = JobSpec.Easy;

        // The game was closed mid-voyage last time: that voyage is lost.
        if (PlayerPrefs.GetInt(CareerPref + "atsea", 0) == 1) AbandonVoyage();
    }

    private void SaveCareer()
    {
        PlayerPrefs.SetInt(CareerPref + "credits", _credits);
        PlayerPrefs.SetInt(CareerPref + "engine", _engineLevel);
        PlayerPrefs.SetInt(CareerPref + "helm", _helmLevel);
        PlayerPrefs.SetInt(CareerPref + "lookout", _lookoutLevel);
        PlayerPrefs.SetInt(CareerPref + "plating", _platingLevel);
        PlayerPrefs.SetInt(CareerPref + "crew", _crewLevel);
        PlayerPrefs.SetInt(CareerPref + "hull", _hullHealth);
        PlayerPrefs.SetInt(CareerPref + "delivered", MissionsCompleted);
        PlayerPrefs.SetInt(CareerPref + "voyages", VoyagesStarted);
        PlayerPrefs.SetString(CareerPref + "job", _careerJob.Name);
        PlayerPrefs.Save();
    }

    /// <summary>Starts over: $500, first upgrades, a sound hull, the learning layout and the story again.</summary>
    public void StartNewCareer()
    {
        foreach (var key in CareerKeys) PlayerPrefs.DeleteKey(CareerPref + key);
        StoryProgress.Reset();
        Progression.Reset(); // certificates and rank start again too
        // A new career is a new learner: the drill comes back too.
        PlayerPrefs.DeleteKey("tutorial_done");
        PlayerPrefs.DeleteKey("tutorial_step");
        PlayerPrefs.Save();
        _credits = StartingCredits;
        _engineLevel = 1;
        _helmLevel = 1;
        _lookoutLevel = _platingLevel = _crewLevel = 1;
        _hullHealth = -1;
        MissionsCompleted = 0;
        VoyagesStarted = 0;
        _careerJob = JobSpec.Easy;
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

    /// <summary>The career job picked in the shipyard (a story mission sets its own).</summary>
    public void SetJob(JobSpec job)
    {
        _careerJob = job;
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

    /// <summary>The old way in ("Engine" or "Helm"); the cost now comes from the certificate.</summary>
    public bool TryBuyUpgrade(string upgradeType, int cost)
    {
        if (upgradeType == "Engine") return TryBuy(Upgrade.Engine);
        if (upgradeType == "Helm") return TryBuy(Upgrade.Rudder);
        return false;
    }
}
