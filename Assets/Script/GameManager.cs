using System;
using UnityEngine;

public enum GameState
{
    Shipyard,
    Playing,
    Victory,
    GameOver
}

/// <summary>
/// Per-scene mission state machine for the Game scene (Shipyard phase -> Playing -> result).
/// Rebuilt on every Game scene load; persistent progress lives in GameSession.
/// </summary>
public class GameManager : Singleton<GameManager>
{
    [Tooltip("Hull points a towed ship comes home with.")]
    [SerializeField] private int TowedHullHealth = 1;

    [Tooltip("Par speed along the guaranteed channel, units/s. Between half (5) and full (8) ahead at Mk 1, so " +
             "beating par takes open-water speed or a better engine.")]
    [SerializeField] private float ParSpeed = 6f;

    [Tooltip("Share of the pay added when the cargo arrives inside par time.")]
    [Range(0f, 1f)]
    [SerializeField] private float OnTimeBonusShare = 0.2f;

    /// <summary>Par time for this voyage, in seconds (0 until the voyage starts).</summary>
    public float ParSeconds { get; private set; }

    /// <summary>Par for the level currently built: the shipyard shows it before sailing.</summary>
    [Tooltip("Seconds added to the par time for docking: slowing down, the pilot, and making fast.")]
    [SerializeField] private float DockingAllowance = 40f;

    public float ParFor(LevelGenerator level) => level != null ? Mathf.Round(level.SmoothedSolutionLength / Mathf.Max(ParSpeed, 0.1f) + DockingAllowance) : 0f;

    public float OnTimeShare => OnTimeBonusShare;

    public GameState CurrentState { get; private set; } = GameState.Shipyard;

    public event Action<GameState> OnStateChanged;

    private float _missionStartTime;
    private int _hits;
    private int _mineHits;

    private void OnEnable()
    {
        ShipHull.OnShipDestroyed += MissionFailed;
        ShipHull.OnHullHit += CountHit;
    }

    private void OnDisable()
    {
        ShipHull.OnShipDestroyed -= MissionFailed;
        ShipHull.OnHullHit -= CountHit;
    }

    private void CountHit(int containersLost)
    {
        if (CurrentState != GameState.Playing) return;
        _hits++;
        if (containersLost >= 2) _mineHits++;
    }

    private void Start()
    {
        SetState(GameState.Shipyard);
    }

    public void SetState(GameState newState)
    {
        if (newState == GameState.Playing && CurrentState != GameState.Playing)
        {
            _missionStartTime = Time.time;
            // The tutorial voyage stops and reverses on purpose: no par to miss.
            bool drill = GameSession.Instance != null && !GameSession.Instance.HasSeenTutorial;
            ParSeconds = drill ? 0f : ParFor(LevelGenerator.Instance);
            _hits = 0;
            _mineHits = 0;
            if (GameSession.Instance != null)
            {
                GameSession.Instance.RecordVoyageStarted();
                GameSession.Instance.MarkAtSea(true);
            }
        }

        CurrentState = newState;

        // Lock controls if not playing
        if (ShipNavigator.Instance != null && ShipNavigator.Instance.Movement != null)
        {
            ShipNavigator.Instance.Movement.enabled = (newState == GameState.Playing);

            // If returning to shipyard or ending game, stop the ship
            if (newState != GameState.Playing)
            {
                ShipNavigator.Instance.SetGear(ShipEngine.TelegraphGear.Stop);
                ShipNavigator.Instance.SetSpeed(0f);
            }
        }

        OnStateChanged?.Invoke(newState);
        Debug.Log($"[GAME MANAGER]: State changed to {newState}");
    }

    public void MissionSuccess()
    {
        if (CurrentState != GameState.Playing) return;

        Debug.Log("[GAME MANAGER]: Mission Success! You reached the destination.");

        // Pay for what arrived: containers knocked overboard are not paid for.
        var session = GameSession.Instance;
        var result = BeginResult(true);
        int loaded = Mathf.Max(1, session.CargoLoaded);
        int pay = Mathf.RoundToInt(session.CurrentReward * (float)session.CargoAboard / loaded);
        // Speed pays: arriving inside par adds a share of what was earned.
        result.ParSeconds = ParSeconds;
        result.OnTimeBonus = ParSeconds > 0f && result.Seconds <= ParSeconds ? Mathf.RoundToInt(pay * OnTimeBonusShare) : 0;
        session.AddCredits(pay + result.OnTimeBonus);
        session.RecordMissionCompleted();
        session.MarkAtSea(false);
        session.LastMissionSummary = $"DELIVERED {session.CargoAboard}/{session.CargoLoaded} CONTAINERS: +${pay + result.OnTimeBonus}";
        result.Pay = pay;
        FinishResult(result);
        session.ClearCargo();

        // The results screen waits for the player; Continue calls ReturnToShipyard.
        SetState(GameState.Victory);
    }

    public void MissionFailed()
    {
        if (CurrentState != GameState.Playing) return;

        Debug.Log("[GAME MANAGER]: Mission Failed! Hull breached.");

        // Losing the ship costs something: the cargo, a towing fee, and a barely patched hull.
        var session = GameSession.Instance;
        var result = BeginResult(false);
        // The tow costs more on bigger jobs (JobSpec.TowingFee); never below zero credits.
        int fee = session.ChargeFee(session.CurrentJob.TowingFee);
        session.HullHealth = TowedHullHealth;
        session.MarkAtSea(false);
        session.LastMissionSummary = $"SHIP LOST. CARGO GONE, TOWED HOME: -${fee}";
        result.TowingFee = fee;
        result.CargoDelivered = 0;
        FinishResult(result);
        session.ClearCargo();
        SetState(GameState.GameOver);
    }

    private MissionResult BeginResult(bool delivered)
    {
        var session = GameSession.Instance;
        return new MissionResult
        {
            Delivered = delivered,
            JobName = session.CurrentJob.Name,
            JobReward = session.CurrentReward,
            CargoLoaded = session.CargoLoaded,
            CargoDelivered = session.CargoAboard,
            Hits = _hits,
            MineHits = _mineHits,
            Seconds = Time.time - _missionStartTime,
            CreditsBefore = session.Credits,
        };
    }

    private void FinishResult(MissionResult result)
    {
        var session = GameSession.Instance;
        const int hullMax = 3;
        var hull = ShipNavigator.Instance != null ? ShipNavigator.Instance.GetComponentInChildren<ShipHull>() : null;
        result.HullMax = hull != null ? hull.MaxHealthAmount : hullMax;
        result.HullAfter = session.HullHealth >= 0 ? Mathf.Min(session.HullHealth, result.HullMax) : result.HullMax;
        result.CreditsAfter = session.Credits;
        session.LastResult = result;
    }

    // Reloading the Game scene rebuilds the ship, level and HUD; the new GameManager starts in Shipyard.
    /// <summary>Reloads the Game scene into the shipyard. False if another load is already running.</summary>
    public bool ReturnToShipyard()
    {
        if (SceneLoader.Instance.IsLoading) return false;
        SceneLoader.Instance.Load(SceneId.Game);
        return true;
    }
}
