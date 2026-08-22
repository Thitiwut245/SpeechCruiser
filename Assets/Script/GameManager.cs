using System;
using UnityEngine;

public enum GameState
{
    Shipyard,
    Playing,
    Victory,
    GameOver
}

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Player Progress")]
    [SerializeField] private int _credits = 0;
    [SerializeField] private int _engineLevel = 1;
    [SerializeField] private int _helmLevel = 1;

    public int Credits => _credits;
    public int EngineLevel => _engineLevel;
    public int HelmLevel => _helmLevel;

    public GameState CurrentState { get; private set; } = GameState.Shipyard;

    public event Action<GameState> OnStateChanged;
    public event Action OnProgressUpdated;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        // Start in Shipyard state
        SetState(GameState.Shipyard);
        
        // Listen to hull destruction
        ShipHull.OnShipDestroyed += MissionFailed;
    }

    private void OnDestroy()
    {
        ShipHull.OnShipDestroyed -= MissionFailed;
    }

    public void SetState(GameState newState)
    {
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
        
        // Award credits
        AddCredits(500); 
        
        SetState(GameState.Victory);
    }

    public void MissionFailed()
    {
        if (CurrentState != GameState.Playing) return;

        Debug.Log("[GAME MANAGER]: Mission Failed! Hull breached.");
        SetState(GameState.GameOver);
    }

    // --- Progression API ---

    public void AddCredits(int amount)
    {
        _credits += amount;
        OnProgressUpdated?.Invoke();
    }

    public bool TryBuyUpgrade(string upgradeType, int cost)
    {
        if (_credits < cost) return false;

        if (upgradeType == "Engine") _engineLevel++;
        else if (upgradeType == "Helm") _helmLevel++;
        else return false;

        _credits -= cost;
        OnProgressUpdated?.Invoke();
        Debug.Log($"[GAME MANAGER]: Upgraded {upgradeType}. Credits remaining: {_credits}");
        return true;
    }
}
